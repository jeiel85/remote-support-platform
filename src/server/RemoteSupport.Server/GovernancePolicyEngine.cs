using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteSupport.Server;

internal sealed record PolicyRuleModel(string Id, string Effect, string[] Roles, Guid[] UserIds,
    string[] Groups, bool AllDevices, Guid[] DeviceIds, Guid[] DeviceGroupIds, string[] Tags,
    string[] SessionTypes, string[] Scopes, bool RequireMfa, int? MaxAuthenticationAgeSeconds,
    bool RequireLocalConsent, string[] SourceCidrs, PolicyScheduleModel? Schedule,
    int? MaxSessionDurationSeconds, long? MaxFileBytes);
internal sealed record PolicyScheduleModel(TimeZoneInfo TimeZone, PolicyWindowModel[] Windows);
internal sealed record PolicyWindowModel(DayOfWeek[] Days, TimeOnly Start, TimeOnly End, bool AllowOvernight);

internal static class GovernancePolicyEngine
{
    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal)
    {
        "OWNER", "ADMIN", "SECURITY_AUDITOR", "OPERATOR", "READ_ONLY_ANALYST",
    };
    private static readonly HashSet<string> SessionTypes = new(StringComparer.Ordinal)
    {
        "ATTENDED", "MANAGED_ATTENDED", "UNATTENDED",
    };
    private static readonly HashSet<string> Scopes = new(StringComparer.Ordinal)
    {
        "VIEW_SCREEN", "CONTROL_POINTER", "CONTROL_KEYBOARD",
        "SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR", "SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST",
        "TRANSFER_FILE_HOST_TO_OPERATOR", "TRANSFER_FILE_OPERATOR_TO_HOST", "CHAT",
        "SWITCH_MONITOR", "REQUEST_REBOOT", "RECONNECT_AFTER_REBOOT", "UNATTENDED_SESSION",
    };

    public static IReadOnlyList<PolicyRuleModel> Parse(JsonElement document)
    {
        try
        {
            if (document.ValueKind != JsonValueKind.Object ||
                !document.TryGetProperty("schemaVersion", out JsonElement schema) || schema.GetInt32() != 1 ||
                !document.TryGetProperty("rules", out JsonElement rules) || rules.ValueKind != JsonValueKind.Array ||
                rules.GetArrayLength() is < 1 or > 500)
                throw InvalidPolicy();
            HashSet<string> ids = new(StringComparer.Ordinal);
            List<PolicyRuleModel> parsed = [];
            foreach (JsonElement rule in rules.EnumerateArray())
            {
                EnsureOnly(rule, "id", "effect", "subjects", "resources", "sessionTypes", "scopes", "conditions", "limits");
                string id = RequiredString(rule, "id", 100);
                if (!ids.Add(id)) throw InvalidPolicy();
                string effect = RequiredString(rule, "effect", 5);
                if (effect is not ("ALLOW" or "DENY")) throw InvalidPolicy();
                JsonElement subjects = RequiredObject(rule, "subjects");
                EnsureOnly(subjects, "roles", "userIds", "groups");
                string[] roles = StringArray(subjects, "roles", Roles, 5);
                Guid[] users = GuidArray(subjects, "userIds", 500);
                string[] groups = StringArray(subjects, "groups", null, 500, 200);
                if (roles.Length + users.Length + groups.Length == 0) throw InvalidPolicy();
                JsonElement resources = RequiredObject(rule, "resources");
                EnsureOnly(resources, "allDevices", "deviceIds", "deviceGroupIds", "tags");
                bool allDevices = OptionalBoolean(resources, "allDevices");
                Guid[] deviceIds = GuidArray(resources, "deviceIds", 500);
                Guid[] deviceGroups = GuidArray(resources, "deviceGroupIds", 500);
                string[] tags = StringArray(resources, "tags", null, 500, 100);
                if (!allDevices && deviceIds.Length + deviceGroups.Length + tags.Length == 0) throw InvalidPolicy();
                string[] sessionTypes = StringArray(rule, "sessionTypes", SessionTypes, 3);
                string[] scopes = StringArray(rule, "scopes", Scopes, Scopes.Count);
                if (sessionTypes.Length == 0 || scopes.Length == 0) throw InvalidPolicy();
                JsonElement? conditions = OptionalObject(rule, "conditions");
                if (conditions is { } condition) EnsureOnly(condition, "requireMfa", "maxAuthenticationAgeSeconds",
                    "requireLocalConsent", "sourceCidrs", "schedule");
                bool requireMfa = conditions is { } c && OptionalBoolean(c, "requireMfa");
                int? authAge = conditions is { } c2 ? OptionalInteger(c2, "maxAuthenticationAgeSeconds", 0, 86_400) : null;
                bool localConsent = conditions is { } c3 && OptionalBoolean(c3, "requireLocalConsent");
                string[] sourceCidrs = conditions is { } c4 ? StringArray(c4, "sourceCidrs", null, 100, 64) : [];
                if (sourceCidrs.Any(value => !TryParseCidr(value, out _, out _))) throw InvalidPolicy();
                PolicyScheduleModel? schedule = conditions is { } c5 ? ParseSchedule(c5) : null;
                JsonElement? limits = OptionalObject(rule, "limits");
                if (limits is { } limit) EnsureOnly(limit, "maxSessionDurationSeconds", "maxFileBytes");
                int? duration = limits is { } l ? OptionalInteger(l, "maxSessionDurationSeconds", 60, 28_800) : null;
                long? fileBytes = limits is { } l2 ? OptionalLong(l2, "maxFileBytes", 0, 1_099_511_627_776) : null;
                parsed.Add(new PolicyRuleModel(id, effect, roles, users, groups, allDevices, deviceIds,
                    deviceGroups, tags, sessionTypes, scopes, requireMfa, authAge, localConsent, sourceCidrs,
                    schedule, duration, fileBytes));
            }
            return parsed;
        }
        catch (ControlPlaneException) { throw; }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or OverflowException)
        {
            throw InvalidPolicy();
        }
    }

    public static PolicyDecisionContract Evaluate(TenantAggregate tenant, TenantRequestContext context,
        PolicyEvaluationRequest request, DateTimeOffset now)
    {
        if (!SessionTypes.Contains(request.SessionType) || request.RequestedScopes.Count is < 1 or > 32 ||
            request.RequestedScopes.Any(scope => !Scopes.Contains(scope)) || request.RequestedScopes.Count != request.RequestedScopes.Distinct(StringComparer.Ordinal).Count())
            throw new ControlPlaneException(400, "POLICY_INPUT_INVALID", "Policy evaluation input was invalid.");
        DeviceRecord? device = null;
        if (request.DeviceId is { } deviceId)
        {
            device = tenant.Devices.GetValueOrDefault(deviceId);
            if (device is null || device.Status != "ACTIVE")
                return HardDenied(tenant.Tenant.Id, request, now, "DEVICE_INACTIVE");
        }
        if (request.SessionType == "UNATTENDED")
        {
            // Unattended has no present local human to refuse or witness access, so these
            // three gates are unconditional and cannot be satisfied by policy configuration
            // alone (05-security/unattended-threat-model.md §3 "Policy misconfiguration").
            if (device is null) return HardDenied(tenant.Tenant.Id, request, now, "UNATTENDED_REQUIRES_DEVICE");
            if (!device.UnattendedEnabled) return HardDenied(tenant.Tenant.Id, request, now, "UNATTENDED_NOT_ENABLED_FOR_DEVICE");
            if (!request.RequestedScopes.Contains("UNATTENDED_SESSION", StringComparer.Ordinal))
                return HardDenied(tenant.Tenant.Id, request, now, "UNATTENDED_SESSION_SCOPE_REQUIRED");
            if (!context.Actor.HasFreshMfa(now, TimeSpan.FromMinutes(10)))
                return HardDenied(tenant.Tenant.Id, request, now, "STEP_UP_MFA_REQUIRED");
        }

        HashSet<string> allowed = new(StringComparer.Ordinal);
        HashSet<string> denied = new(StringComparer.Ordinal);
        List<string> versions = [];
        List<string> explanations = [];
        bool requiresConsent = false;
        bool requiresStepUp = false;
        int maxDuration = request.RequestedDurationSeconds is > 0 and <= 28_800
            ? request.RequestedDurationSeconds.Value : 28_800;
        long maxFileBytes = tenant.Settings.FileSizeLimitBytes;
        foreach (PolicyRecord policy in tenant.Policies.Values.OrderBy(value => value.Id))
        {
            if (policy.Status != "ACTIVE" || policy.ActiveVersion is not { } active ||
                !policy.Versions.TryGetValue(active, out PolicyVersionRecord? version)) continue;
            versions.Add($"{policy.Id:D}:{active.ToString(CultureInfo.InvariantCulture)}");
            foreach (PolicyRuleModel rule in Parse(version.Document))
            {
                if (!MatchesSubject(rule, context) || !rule.SessionTypes.Contains(request.SessionType, StringComparer.Ordinal) ||
                    !MatchesDevice(rule, device) || !MatchesSource(rule, context.SourceIp) ||
                    !MatchesSchedule(rule, now)) continue;
                string[] relevantScopes = rule.Scopes.Intersect(request.RequestedScopes, StringComparer.Ordinal).ToArray();
                if (relevantScopes.Length == 0) continue;
                bool mfaFresh = context.Actor.HasFreshMfa(now,
                    TimeSpan.FromSeconds(rule.MaxAuthenticationAgeSeconds ?? 3_600));
                if (rule.Effect == "ALLOW" && (rule.RequireMfa || rule.MaxAuthenticationAgeSeconds is not null) && !mfaFresh)
                {
                    requiresStepUp = true;
                    explanations.Add($"MFA_REQUIRED:{rule.Id}");
                    continue;
                }
                if (rule.Effect == "DENY")
                {
                    denied.UnionWith(relevantScopes);
                    explanations.Add($"DENY:{rule.Id}");
                }
                else
                {
                    allowed.UnionWith(relevantScopes);
                    explanations.Add($"ALLOW:{rule.Id}");
                    requiresConsent |= rule.RequireLocalConsent;
                    if (rule.MaxSessionDurationSeconds is { } duration) maxDuration = Math.Min(maxDuration, duration);
                    if (rule.MaxFileBytes is { } fileBytes) maxFileBytes = Math.Min(maxFileBytes, fileBytes);
                }
            }
        }
        allowed.RemoveWhere(scope => !FeatureEnabled(tenant.Settings, scope));
        allowed.ExceptWith(denied);
        string[] granted = request.RequestedScopes.Where(allowed.Contains).Order(StringComparer.Ordinal).ToArray();
        DeniedScopeContract[] deniedResult = request.RequestedScopes.Where(scope => !allowed.Contains(scope))
            .Order(StringComparer.Ordinal).Select(scope => new DeniedScopeContract(scope,
                denied.Contains(scope) ? "DENY_RULE" : requiresStepUp ? "MFA_REQUIRED" :
                !FeatureEnabled(tenant.Settings, scope) ? "FEATURE_DISABLED" : "NO_MATCHING_ALLOW_RULE")).ToArray();
        if (versions.Count == 0) explanations.Add("NO_ACTIVE_POLICY");
        string inputHash = InputHash(tenant.Tenant.Id, context, request, device, now);
        return new PolicyDecisionContract(Guid.CreateVersion7(now), tenant.Tenant.Id, versions,
            granted.Length > 0, granted, deniedResult, requiresConsent,
            requiresStepUp, context.Actor.MfaMethods, maxDuration, maxFileBytes,
            explanations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), now, inputHash);
    }

    private static PolicyDecisionContract HardDenied(Guid tenantId, PolicyEvaluationRequest request,
        DateTimeOffset now, string reason) => new(Guid.CreateVersion7(now), tenantId, [], false, [],
        request.RequestedScopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(scope => new DeniedScopeContract(scope, reason)).ToArray(), true,
        false, [], 0, 0, [reason], now,
        Convert.ToHexString(SHA256.HashData(ControlPlaneCrypto.Canonicalize(
            JsonSerializer.SerializeToElement(new { tenantId, request, now })))).ToLowerInvariant());

    private static bool MatchesSubject(PolicyRuleModel rule, TenantRequestContext context) =>
        rule.Roles.Intersect(context.Roles, StringComparer.Ordinal).Any() ||
        rule.UserIds.Contains(context.UserId) || rule.Groups.Intersect(context.Groups, StringComparer.Ordinal).Any();

    private static bool MatchesDevice(PolicyRuleModel rule, DeviceRecord? device) => rule.AllDevices ||
        (device is not null && rule.DeviceIds.Contains(device.Id));

    private static bool MatchesSource(PolicyRuleModel rule, string? sourceIp)
    {
        if (rule.SourceCidrs.Length == 0) return true;
        if (!IPAddress.TryParse(sourceIp, out IPAddress? address)) return false;
        return rule.SourceCidrs.Any(cidr => AddressInCidr(address, cidr));
    }

    private static bool MatchesSchedule(PolicyRuleModel rule, DateTimeOffset now)
    {
        if (rule.Schedule is null) return true;
        DateTime local = TimeZoneInfo.ConvertTime(now, rule.Schedule.TimeZone).DateTime;
        TimeOnly time = TimeOnly.FromDateTime(local);
        foreach (PolicyWindowModel window in rule.Schedule.Windows)
        {
            if (!window.AllowOvernight || window.Start <= window.End)
            {
                if (window.Days.Contains(local.DayOfWeek) && time >= window.Start && time < window.End) return true;
                continue;
            }
            if (window.Days.Contains(local.DayOfWeek) && time >= window.Start) return true;
            DayOfWeek previousDay = (DayOfWeek)(((int)local.DayOfWeek + 6) % 7);
            if (window.Days.Contains(previousDay) && time < window.End) return true;
        }
        return false;
    }

    private static bool FeatureEnabled(TenantSettingsRecord settings, string scope)
    {
        string? feature = scope switch
        {
            "VIEW_SCREEN" => "VIEW_SCREEN",
            "CONTROL_POINTER" or "CONTROL_KEYBOARD" => "REMOTE_INPUT",
            "SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR" or "SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST" => "CLIPBOARD_TEXT",
            "TRANSFER_FILE_HOST_TO_OPERATOR" or "TRANSFER_FILE_OPERATOR_TO_HOST" => "FILE_TRANSFER",
            "CHAT" => "CHAT",
            "SWITCH_MONITOR" => "MULTI_MONITOR",
            "UNATTENDED_SESSION" => "UNATTENDED_ACCESS",
            _ => null,
        };
        return feature is not null && settings.AllowedFeatures.Contains(feature, StringComparer.Ordinal);
    }

    private static string InputHash(Guid tenantId, TenantRequestContext context, PolicyEvaluationRequest request,
        DeviceRecord? device, DateTimeOffset now)
    {
        JsonElement input = JsonSerializer.SerializeToElement(new
        {
            tenantId,
            context.UserId,
            roles = context.Roles.Order(StringComparer.Ordinal),
            groups = context.Groups.Order(StringComparer.Ordinal),
            authenticationTime = context.Actor.AuthenticationTime,
            mfaMethods = context.Actor.MfaMethods.Order(StringComparer.Ordinal),
            device = device is null ? null : new { device.Id, device.Status, device.AuthorizationVersion },
            request.SessionType,
            requestedScopes = request.RequestedScopes.Order(StringComparer.Ordinal),
            request.RequestedDurationSeconds,
            request.LocalUserPresent,
            currentUtc = now,
        });
        return Convert.ToHexString(SHA256.HashData(ControlPlaneCrypto.Canonicalize(input))).ToLowerInvariant();
    }

    private static void EnsureOnly(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw InvalidPolicy();
        HashSet<string> names = new(allowed, StringComparer.Ordinal);
        if (value.EnumerateObject().Any(property => !names.Contains(property.Name))) throw InvalidPolicy();
    }

    private static JsonElement RequiredObject(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value : throw InvalidPolicy();

    private static JsonElement? OptionalObject(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out JsonElement value)) return null;
        return value.ValueKind == JsonValueKind.Object ? value : throw InvalidPolicy();
    }

    private static string RequiredString(JsonElement owner, string name, int max)
    {
        if (!owner.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) throw InvalidPolicy();
        string result = value.GetString() ?? string.Empty;
        return result.Length is >= 1 && result.Length <= max ? result : throw InvalidPolicy();
    }

    private static string[] StringArray(JsonElement owner, string name, HashSet<string>? allowed,
        int maximum, int maxLength = 200)
    {
        if (!owner.TryGetProperty(name, out JsonElement value)) return [];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum) throw InvalidPolicy();
        string[] result = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString() ?? string.Empty : throw InvalidPolicy()).ToArray();
        if (result.Any(item => item.Length is < 1 || item.Length > maxLength || (allowed is not null && !allowed.Contains(item))) ||
            result.Length != result.Distinct(StringComparer.Ordinal).Count()) throw InvalidPolicy();
        return result;
    }

    private static Guid[] GuidArray(JsonElement owner, string name, int maximum)
    {
        string[] values = StringArray(owner, name, null, maximum, 36);
        try { return values.Select(Guid.Parse).ToArray(); }
        catch (FormatException) { throw InvalidPolicy(); }
    }

    private static PolicyScheduleModel? ParseSchedule(JsonElement conditions)
    {
        if (!conditions.TryGetProperty("schedule", out JsonElement schedule)) return null;
        EnsureOnly(schedule, "timezone", "windows");
        string timezone = RequiredString(schedule, "timezone", 100);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException) { throw InvalidPolicy(); }
        catch (InvalidTimeZoneException) { throw InvalidPolicy(); }
        if (!schedule.TryGetProperty("windows", out JsonElement windows) || windows.ValueKind != JsonValueKind.Array ||
            windows.GetArrayLength() > 50) throw InvalidPolicy();
        List<PolicyWindowModel> parsed = [];
        foreach (JsonElement window in windows.EnumerateArray())
        {
            EnsureOnly(window, "daysOfWeek", "startLocal", "endLocal", "allowOvernight");
            string[] dayCodes = StringArray(window, "daysOfWeek", new HashSet<string>(StringComparer.Ordinal)
                { "MO", "TU", "WE", "TH", "FR", "SA", "SU" }, 7, 2);
            if (dayCodes.Length == 0 || !TimeOnly.TryParseExact(RequiredString(window, "startLocal", 5), "HH:mm",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly start) ||
                !TimeOnly.TryParseExact(RequiredString(window, "endLocal", 5), "HH:mm",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly end)) throw InvalidPolicy();
            bool overnight = OptionalBoolean(window, "allowOvernight");
            if (start > end && !overnight) throw InvalidPolicy();
            parsed.Add(new PolicyWindowModel(dayCodes.Select(ToDayOfWeek).ToArray(), start, end, overnight));
        }
        return new PolicyScheduleModel(zone, parsed.ToArray());
    }

    private static DayOfWeek ToDayOfWeek(string value) => value switch
    {
        "MO" => DayOfWeek.Monday,
        "TU" => DayOfWeek.Tuesday,
        "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday,
        "SA" => DayOfWeek.Saturday,
        "SU" => DayOfWeek.Sunday,
        _ => throw InvalidPolicy(),
    };

    private static bool AddressInCidr(IPAddress address, string cidr)
    {
        if (!TryParseCidr(cidr, out IPAddress? network, out int prefix) || network is null ||
            address.AddressFamily != network.AddressFamily)
            return false;
        byte[] addressBytes = address.GetAddressBytes();
        byte[] networkBytes = network.GetAddressBytes();
        int fullBytes = prefix / 8;
        int remaining = prefix % 8;
        for (int index = 0; index < fullBytes; index++)
            if (addressBytes[index] != networkBytes[index]) return false;
        if (remaining == 0) return true;
        int mask = 0xFF << (8 - remaining);
        return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }

    private static bool TryParseCidr(string value, out IPAddress? network, out int prefix)
    {
        network = null;
        prefix = 0;
        string[] parts = value.Split('/', StringSplitOptions.None);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out network) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out prefix)) return false;
        int maximum = network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        return prefix >= 0 && prefix <= maximum;
    }

    private static bool OptionalBoolean(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out JsonElement value)) return false;
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw InvalidPolicy();
    }

    private static int? OptionalInteger(JsonElement owner, string name, int minimum, int maximum)
    {
        if (!owner.TryGetProperty(name, out JsonElement value)) return null;
        int result = value.GetInt32();
        return result >= minimum && result <= maximum ? result : throw InvalidPolicy();
    }

    private static long? OptionalLong(JsonElement owner, string name, long minimum, long maximum)
    {
        if (!owner.TryGetProperty(name, out JsonElement value)) return null;
        long result = value.GetInt64();
        return result >= minimum && result <= maximum ? result : throw InvalidPolicy();
    }

    private static ControlPlaneException InvalidPolicy() =>
        new(400, "POLICY_DOCUMENT_INVALID", "The policy document was invalid.");
}
