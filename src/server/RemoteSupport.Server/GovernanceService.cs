using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RemoteSupport.Server;

internal sealed class GovernanceOptions
{
    public const string SectionName = "Governance";
    public string ExportDirectory { get; set; } = "artifacts/governance-exports";
    public int ClosureCoolingOffDays { get; set; } = 7;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExportDirectory))
            throw new InvalidOperationException("Governance:ExportDirectory is required.");
        if (ClosureCoolingOffDays is < 1 or > 30)
            throw new InvalidOperationException("Governance:ClosureCoolingOffDays must be between 1 and 30.");
    }
}

internal sealed class GovernanceService(IGovernanceStore store, ControlPlaneCrypto crypto,
    ISystemClock clock, GovernanceOptions options, GovernanceExportStore exportStore)
{
    private static readonly HashSet<string> ValidRoles = new(StringComparer.Ordinal)
    {
        "OWNER", "ADMIN", "SECURITY_AUDITOR", "OPERATOR", "READ_ONLY_ANALYST",
    };
    private static readonly HashSet<string> InviteRoles = new(StringComparer.Ordinal)
    {
        "ADMIN", "SECURITY_AUDITOR", "OPERATOR", "READ_ONLY_ANALYST",
    };
    private static readonly HashSet<string> AllowedFeatures = new(StringComparer.Ordinal)
    {
        "VIEW_SCREEN", "REMOTE_INPUT", "CLIPBOARD_TEXT", "FILE_TRANSFER", "CHAT", "MULTI_MONITOR",
    };

    public TenantContract CreateTenant(CreateTenantRequest request, string idempotencyKey, TenantActor actor)
    {
        ValidateIdempotency(idempotencyKey);
        string name = RequiredText(request.Name, 200, "TENANT_NAME_INVALID");
        string slug = request.Slug.Trim().ToLowerInvariant();
        if (slug.Length is < 3 or > 63 || !System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]+$"))
            throw BadRequest("TENANT_SLUG_INVALID");
        string region = RequiredText(request.DataRegion, 32, "DATA_REGION_INVALID");
        string idempotencyHash = crypto.LookupHash("tenant-create\0" + actor.Subject + "\0" + idempotencyKey);
        Guid tenantId = crypto.DeriveGuid("tenant-id", actor.Subject + "\0" + idempotencyKey);
        string requestHash = RequestHash(request);
        TenantAggregate? previous = store.Snapshot(tenantId);
        if (previous is not null)
        {
            if (!previous.Idempotency.TryGetValue(idempotencyHash, out IdempotencyRecord? record) ||
                !string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
                throw Conflict("IDEMPOTENCY_KEY_REUSED");
            return ToContract(previous.Tenant);
        }
        DateTimeOffset now = clock.UtcNow;
        TenantRecord tenant = new(tenantId, name, slug, "ACTIVE", "BETA", region, 1, now, now, null);
        MembershipRecord owner = new(actor.UserId, actor.Subject, actor.DisplayName, actor.Email,
            ["OWNER"], "ACTIVE", 1, now, now, null);
        TenantAggregate aggregate = new()
        {
            Tenant = tenant,
            Settings = new TenantSettingsRecord(1, 90,
                ["VIEW_SCREEN", "REMOTE_INPUT", "CLIPBOARD_TEXT", "FILE_TRANSFER", "CHAT", "MULTI_MONITOR"],
                536_870_912, false, now),
        };
        aggregate.Memberships.Add(owner.UserId, owner);
        aggregate.Idempotency.Add(idempotencyHash, new IdempotencyRecord(requestHash, "TENANT", tenantId));
        GovernanceAudit.Append(aggregate, actor, "TENANT", "TENANT_CREATED", "SUCCEEDED", "TENANT",
            tenantId.ToString("D"), new { name, slug, region }, now);
        if (!store.TryCreate(aggregate)) throw Conflict("TENANT_SLUG_CONFLICT");
        return ToContract(tenant);
    }

    public TenantRequestContext ResolveContext(Guid tenantId, TenantActor actor, bool allowClosed = false) => store.Execute(tenantId, tenant =>
    {
        if (tenant.Tenant.Status != "ACTIVE" && !(allowClosed && tenant.Tenant.Status == "CLOSED")) throw NotFound();
        MembershipRecord? membership = tenant.Memberships.Values.SingleOrDefault(value =>
            string.Equals(value.ExternalSubject, actor.Subject, StringComparison.Ordinal) &&
            (value.Status == "ACTIVE" || (allowClosed && value.Status == "REMOVED" && value.Roles.Contains("OWNER", StringComparer.Ordinal))));
        if (membership is null) throw NotFound();
        return new TenantRequestContext(tenantId, membership.UserId, membership.Roles, [],
            membership.PrivilegeVersion, tenant.Tenant.AuthorizationVersion, actor);
    });

    public OperatorIdentity ResolveOperatorIdentity(Guid tenantId, TenantActor actor) => store.Execute(tenantId, tenant =>
    {
        if (tenant.Tenant.Status != "ACTIVE" || !tenant.Memberships.Values.Any(value =>
                value.ExternalSubject == actor.Subject && value.Status == "ACTIVE")) throw NotFound();
        return new OperatorIdentity(tenantId, actor.Subject, actor.DisplayName, tenant.Tenant.Name, true);
    });

    public TenantContract GetTenant(TenantRequestContext context) => store.Execute(context.TenantId, tenant =>
    {
        context.RequireRole("OWNER", "ADMIN", "SECURITY_AUDITOR", "OPERATOR", "READ_ONLY_ANALYST");
        return ToContract(tenant.Tenant);
    });

    public TenantSettingsContract GetSettings(TenantRequestContext context) => store.Execute(context.TenantId, tenant =>
    {
        context.RequireRole("OWNER", "ADMIN", "SECURITY_AUDITOR", "READ_ONLY_ANALYST");
        return ToContract(tenant.Settings);
    });

    public TenantSettingsContract UpdateSettings(TenantRequestContext context, TenantSettingsPatch patch, long version)
        => store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN");
            context.RequireFreshMfa(clock.UtcNow);
            if (version != tenant.Settings.Version) throw Conflict("RESOURCE_VERSION_CONFLICT");
            if (patch.RetentionDays is null && patch.AllowedFeatures is null && patch.FileSizeLimitBytes is null)
                throw BadRequest("SETTINGS_PATCH_EMPTY");
            int retention = patch.RetentionDays ?? tenant.Settings.RetentionDays;
            if (retention is < 1 or > 3650) throw BadRequest("RETENTION_INVALID");
            string[] features = patch.AllowedFeatures?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                ?? tenant.Settings.AllowedFeatures;
            if (features.Any(feature => !AllowedFeatures.Contains(feature))) throw BadRequest("FEATURE_POLICY_INVALID");
            long fileLimit = patch.FileSizeLimitBytes ?? tenant.Settings.FileSizeLimitBytes;
            if (fileLimit is < 0 or > 1_099_511_627_776) throw BadRequest("FILE_LIMIT_INVALID");
            DateTimeOffset now = clock.UtcNow;
            tenant.Settings = new TenantSettingsRecord(tenant.Settings.Version + 1, retention, features,
                fileLimit, false, now);
            TouchAuthorization(tenant, now);
            GovernanceAudit.Append(tenant, context.Actor, "POLICY", "TENANT_SETTINGS_UPDATED", "SUCCEEDED",
                "TENANT", tenant.Tenant.Id.ToString("D"), new
                {
                    retentionDays = retention,
                    allowedFeatures = features,
                    fileSizeLimitBytes = fileLimit,
                    recordingEnabled = false,
                    settingsVersion = tenant.Settings.Version
                }, now);
            return ToContract(tenant.Settings);
        });

    public PagedMemberships ListMemberships(TenantRequestContext context) => store.Execute(context.TenantId, tenant =>
    {
        context.RequireRole("OWNER", "ADMIN", "SECURITY_AUDITOR");
        MembershipContract[] items = tenant.Memberships.Values.Where(value => value.Status != "REMOVED")
            .OrderBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase).Select(ToContract).ToArray();
        return new PagedMemberships(items);
    });

    public MembershipContract UpdateMembership(TenantRequestContext context, Guid userId,
        MembershipPatch patch, long version) => store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN");
            context.RequireFreshMfa(clock.UtcNow);
            if (!tenant.Memberships.TryGetValue(userId, out MembershipRecord? target) || target.Status == "REMOVED")
                throw NotFound();
            if (version != target.PrivilegeVersion) throw Conflict("RESOURCE_VERSION_CONFLICT");
            bool actorOwner = context.HasRole("OWNER");
            if (!actorOwner && target.Roles.Contains("OWNER", StringComparer.Ordinal))
                throw new ControlPlaneException(403, "OWNER_BOUNDARY", "Administrators cannot modify owners.");
            string[] roles = patch.Roles is null ? target.Roles : ValidateRoles(patch.Roles, ValidRoles);
            if (!actorOwner && roles.Contains("OWNER", StringComparer.Ordinal))
                throw new ControlPlaneException(403, "OWNER_BOUNDARY", "Only owners can assign the owner role.");
            string status = patch.Status ?? target.Status;
            if (status is not ("ACTIVE" or "SUSPENDED")) throw BadRequest("MEMBERSHIP_STATUS_INVALID");
            EnsureOwnerRemains(tenant, target, roles, status);
            DateTimeOffset now = clock.UtcNow;
            MembershipRecord updated = target with
            {
                Roles = roles,
                Status = status,
                PrivilegeVersion = target.PrivilegeVersion + 1,
                UpdatedAt = now,
            };
            tenant.Memberships[userId] = updated;
            TouchAuthorization(tenant, now);
            GovernanceAudit.Append(tenant, context.Actor, "IDENTITY", "MEMBERSHIP_UPDATED", "SUCCEEDED",
                "USER", userId.ToString("D"), new { roles, status, updated.PrivilegeVersion }, now);
            return ToContract(updated);
        });

    public void RemoveMembership(TenantRequestContext context, Guid userId, long version) =>
        store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN");
            context.RequireFreshMfa(clock.UtcNow);
            if (!tenant.Memberships.TryGetValue(userId, out MembershipRecord? target) || target.Status == "REMOVED")
                throw NotFound();
            if (version != target.PrivilegeVersion) throw Conflict("RESOURCE_VERSION_CONFLICT");
            if (!context.HasRole("OWNER") && target.Roles.Contains("OWNER", StringComparer.Ordinal))
                throw new ControlPlaneException(403, "OWNER_BOUNDARY", "Administrators cannot remove owners.");
            EnsureOwnerRemains(tenant, target, target.Roles, "REMOVED");
            DateTimeOffset now = clock.UtcNow;
            tenant.Memberships[userId] = target with
            {
                Status = "REMOVED",
                PrivilegeVersion = target.PrivilegeVersion + 1,
                UpdatedAt = now,
                RemovedAt = now,
            };
            TouchAuthorization(tenant, now);
            GovernanceAudit.Append(tenant, context.Actor, "IDENTITY", "MEMBERSHIP_REMOVED", "SUCCEEDED",
                "USER", userId.ToString("D"), new { }, now);
            return true;
        });

    public PagedInvitations ListInvitations(TenantRequestContext context) => store.Execute(context.TenantId, tenant =>
    {
        context.RequireRole("OWNER", "ADMIN", "SECURITY_AUDITOR");
        InvitationContract[] items = tenant.Invitations.Values.OrderByDescending(value => value.CreatedAt)
            .Select(value => ToContract(value, null, clock.UtcNow)).ToArray();
        return new PagedInvitations(items);
    });

    public InvitationContract CreateInvitation(TenantRequestContext context, InvitationRequest request,
        string idempotencyKey) => store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN");
            context.RequireFreshMfa(clock.UtcNow);
            ValidateIdempotency(idempotencyKey);
            string email = NormalizeEmail(request.Email);
            string[] roles = ValidateRoles(request.Roles, InviteRoles);
            int lifetime = request.ExpiresInSeconds ?? 172_800;
            if (lifetime is < 3_600 or > 604_800) throw BadRequest("INVITATION_EXPIRY_INVALID");
            string idempotencyHash = crypto.LookupHash("invitation-idempotency\0" + context.TenantId + "\0" + idempotencyKey);
            string requestHash = RequestHash(request);
            if (tenant.Idempotency.TryGetValue(idempotencyHash, out IdempotencyRecord? prior))
            {
                if (!string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal))
                    throw Conflict("IDEMPOTENCY_KEY_REUSED");
                InvitationRecord existing = tenant.Invitations[prior.ResourceId];
                return ToContract(existing, crypto.DeriveSecret("invitation", context.TenantId + "\0" + idempotencyKey), clock.UtcNow);
            }
            if (tenant.Invitations.Values.Any(value => value.Email == email && value.Status == "PENDING" && value.ExpiresAt > clock.UtcNow))
                throw Conflict("INVITATION_ALREADY_PENDING");
            DateTimeOffset now = clock.UtcNow;
            string secret = crypto.DeriveSecret("invitation", context.TenantId + "\0" + idempotencyKey);
            Guid id = crypto.DeriveGuid("invitation-id", context.TenantId + "\0" + idempotencyKey);
            InvitationRecord invitation = new(id, email, roles, crypto.LookupHash(secret), "PENDING",
                context.UserId, now.AddSeconds(lifetime), now, null, null, null);
            tenant.Invitations.Add(id, invitation);
            tenant.Idempotency.Add(idempotencyHash, new IdempotencyRecord(requestHash, "INVITATION", id));
            GovernanceAudit.Append(tenant, context.Actor, "IDENTITY", "INVITATION_CREATED", "SUCCEEDED",
                "INVITATION", id.ToString("D"), new
                {
                    emailDomain = email[(email.IndexOf('@') + 1)..],
                    roles,
                    invitation.ExpiresAt
                }, now);
            return ToContract(invitation, secret, now);
        });

    public void RevokeInvitation(TenantRequestContext context, Guid invitationId) =>
        store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN");
            if (!tenant.Invitations.TryGetValue(invitationId, out InvitationRecord? invitation)) throw NotFound();
            if (invitation.Status != "PENDING") return true;
            DateTimeOffset now = clock.UtcNow;
            tenant.Invitations[invitationId] = invitation with { Status = "REVOKED", RevokedAt = now };
            GovernanceAudit.Append(tenant, context.Actor, "IDENTITY", "INVITATION_REVOKED", "SUCCEEDED",
                "INVITATION", invitationId.ToString("D"), new { }, now);
            return true;
        });

    public MembershipContract AcceptInvitation(Guid invitationId, InvitationAcceptanceRequest request, TenantActor actor)
    {
        if (request.InvitationToken.Length is < 32 or > 512) throw NotFound();
        string hash = crypto.LookupHash(request.InvitationToken);
        Guid tenantId = store.FindTenantBySecret("INVITATION", hash) ?? throw NotFound();
        return store.Execute(tenantId, tenant =>
        {
            InvitationRecord? invitation = tenant.Invitations.Values.SingleOrDefault(value =>
                value.Id == invitationId && string.Equals(value.TokenHash, hash, StringComparison.Ordinal));
            DateTimeOffset now = clock.UtcNow;
            if (invitation is null || invitation.Status != "PENDING" || invitation.ExpiresAt <= now ||
                actor.Email is null || !string.Equals(actor.Email, invitation.Email, StringComparison.OrdinalIgnoreCase))
                throw NotFound();
            if (tenant.Memberships.Values.Any(value => value.ExternalSubject == actor.Subject && value.Status != "REMOVED"))
                throw Conflict("MEMBERSHIP_ALREADY_EXISTS");
            MembershipRecord membership = new(actor.UserId, actor.Subject, actor.DisplayName, actor.Email,
                invitation.Roles, "ACTIVE", 1, now, now, null);
            tenant.Memberships[actor.UserId] = membership;
            tenant.Invitations[invitation.Id] = invitation with
            {
                Status = "ACCEPTED",
                AcceptedByUserId = actor.UserId,
                AcceptedAt = now,
            };
            TouchAuthorization(tenant, now);
            GovernanceAudit.Append(tenant, actor, "IDENTITY", "INVITATION_ACCEPTED", "SUCCEEDED",
                "USER", actor.UserId.ToString("D"), new { invitationId = invitation.Id, roles = invitation.Roles }, now);
            return ToContract(membership);
        });
    }

    public EnrollmentTokenResult CreateEnrollmentToken(TenantRequestContext context,
        EnrollmentTokenRequest request, string idempotencyKey) => store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN");
            context.RequireFreshMfa(clock.UtcNow);
            ValidateIdempotency(idempotencyKey);
            if (request.ExpiresInSeconds is < 60 or > 86_400) throw BadRequest("ENROLLMENT_EXPIRY_INVALID");
            int uses = request.AllowedInstallations ?? 1;
            if (uses is < 1 or > 1000 || request.DeviceGroupId is not null)
                throw BadRequest("ENROLLMENT_OPTIONS_INVALID");
            string idempotencyHash = crypto.LookupHash("enrollment-idempotency\0" + context.TenantId + "\0" + idempotencyKey);
            string requestHash = RequestHash(request);
            string secret = crypto.DeriveSecret("device-enrollment", context.TenantId + "\0" + idempotencyKey);
            if (tenant.Idempotency.TryGetValue(idempotencyHash, out IdempotencyRecord? prior))
            {
                if (!string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal))
                    throw Conflict("IDEMPOTENCY_KEY_REUSED");
                EnrollmentTokenRecord existing = tenant.EnrollmentTokens[prior.ResourceId];
                return new EnrollmentTokenResult(secret, existing.ExpiresAt);
            }
            DateTimeOffset now = clock.UtcNow;
            Guid id = crypto.DeriveGuid("device-enrollment-id", context.TenantId + "\0" + idempotencyKey);
            EnrollmentTokenRecord token = new(id, crypto.LookupHash(secret), context.UserId, uses, 0,
                now.AddSeconds(request.ExpiresInSeconds), null, now);
            tenant.EnrollmentTokens.Add(id, token);
            tenant.Idempotency.Add(idempotencyHash, new IdempotencyRecord(requestHash, "ENROLLMENT_TOKEN", id));
            GovernanceAudit.Append(tenant, context.Actor, "DEVICE", "ENROLLMENT_TOKEN_CREATED", "SUCCEEDED",
                "ENROLLMENT_TOKEN", id.ToString("D"), new { maximumUses = uses, token.ExpiresAt }, now);
            return new EnrollmentTokenResult(secret, token.ExpiresAt);
        });

    public DeviceEnrollmentResult EnrollDevice(DeviceEnrollmentRequest request, string idempotencyKey)
    {
        ValidateIdempotency(idempotencyKey);
        if (request.EnrollmentToken.Length is < 32 or > 512 || request.InstallationId == Guid.Empty)
            throw NotFound();
        string tokenHash = crypto.LookupHash(request.EnrollmentToken);
        Guid tenantId = store.FindTenantBySecret("ENROLLMENT", tokenHash) ?? throw NotFound();
        return store.Execute(tenantId, tenant =>
        {
            DateTimeOffset now = clock.UtcNow;
            EnrollmentTokenRecord? token = tenant.EnrollmentTokens.Values.SingleOrDefault(value =>
                string.Equals(value.TokenHash, tokenHash, StringComparison.Ordinal));
            if (token is null || token.RevokedAt is not null || token.ExpiresAt <= now || token.UseCount >= token.MaximumUses)
                throw NotFound();
            string idempotencyHash = crypto.LookupHash("device-enroll-idempotency\0" + tenantId + "\0" + idempotencyKey);
            DeviceRecord? previous = tenant.Devices.Values.SingleOrDefault(value =>
                string.Equals(value.EnrollmentIdempotencyHash, idempotencyHash, StringComparison.Ordinal));
            string credential = crypto.DeriveSecret("device-credential", tenantId + "\0" + idempotencyKey);
            if (previous is not null) return new DeviceEnrollmentResult(previous.Id, credential, 1, ActivePolicyVersion(tenant));
            if (tenant.Devices.Values.Any(value => value.InstallationId == request.InstallationId))
                throw Conflict("INSTALLATION_ALREADY_ENROLLED");
            string keyThumbprint;
            string proofThumbprint;
            try
            {
                keyThumbprint = ControlPlaneCrypto.Thumbprint(request.DevicePublicKey);
                proofThumbprint = ControlPlaneCrypto.Thumbprint(request.Proof.PublicKey);
            }
            catch (Exception exception) when (exception is FormatException or KeyNotFoundException or InvalidOperationException)
            {
                throw BadRequest("DEVICE_KEY_INVALID");
            }
            if (!string.Equals(keyThumbprint, proofThumbprint, StringComparison.Ordinal) || request.Proof.Nonce.Length is < 16 or > 256 ||
                !ControlPlaneCrypto.VerifyP256(request.DevicePublicKey, request.Proof.Algorithm,
                    EnrollmentProofBytes(request, keyThumbprint), request.Proof.Signature))
                throw new ControlPlaneException(403, "DEVICE_PROOF_INVALID", "Device proof was invalid.");
            ValidateDeviceInfo(request.DeviceInfo);
            Guid deviceId = crypto.DeriveGuid("device-id", tenantId + "\0" + idempotencyKey);
            DeviceRecord device = new(deviceId, request.InstallationId, request.DeviceInfo.DisplayName.Trim(),
                request.DeviceInfo.Architecture, request.DeviceInfo.OsVersion.Trim(), request.DeviceInfo.AppVersion.Trim(),
                request.DevicePublicKey.GetRawText(), keyThumbprint, crypto.LookupHash(credential), "ACTIVE", 1,
                now, now, null, null, idempotencyHash);
            tenant.Devices.Add(deviceId, device);
            tenant.EnrollmentTokens[token.Id] = token with { UseCount = token.UseCount + 1 };
            TouchAuthorization(tenant, now);
            GovernanceAudit.Append(tenant, new TenantActor(deviceId, deviceId.ToString("D"), device.DisplayName,
                null, "DEVICE", null, [], null), "DEVICE", "DEVICE_ENROLLED", "SUCCEEDED", "DEVICE",
                deviceId.ToString("D"), new
                {
                    device.Architecture,
                    device.OsVersion,
                    device.AppVersion,
                    keyThumbprint
                }, now);
            return new DeviceEnrollmentResult(deviceId, credential, 1, ActivePolicyVersion(tenant));
        });
    }

    public PagedDevices ListDevices(TenantRequestContext context) => store.Execute(context.TenantId, tenant =>
    {
        context.RequireRole("OWNER", "ADMIN", "SECURITY_AUDITOR", "OPERATOR", "READ_ONLY_ANALYST");
        return new PagedDevices(tenant.Devices.Values.OrderBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ToContract).ToArray());
    });

    public DeviceContract GetDevice(TenantRequestContext context, Guid deviceId) => store.Execute(context.TenantId, tenant =>
    {
        context.RequireRole("OWNER", "ADMIN", "SECURITY_AUDITOR", "OPERATOR", "READ_ONLY_ANALYST");
        return tenant.Devices.TryGetValue(deviceId, out DeviceRecord? device) ? ToContract(device) : throw NotFound();
    });

    public void RevokeDevice(TenantRequestContext context, Guid deviceId, long version) =>
        store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN");
            context.RequireFreshMfa(clock.UtcNow);
            if (!tenant.Devices.TryGetValue(deviceId, out DeviceRecord? device)) throw NotFound();
            if (device.Status == "REVOKED") return true;
            if (version != device.AuthorizationVersion) throw Conflict("RESOURCE_VERSION_CONFLICT");
            DateTimeOffset now = clock.UtcNow;
            tenant.Devices[deviceId] = device with
            {
                Status = "REVOKED",
                AuthorizationVersion = device.AuthorizationVersion + 1,
                UpdatedAt = now,
                RevokedAt = now,
            };
            TouchAuthorization(tenant, now);
            GovernanceAudit.Append(tenant, context.Actor, "DEVICE", "DEVICE_REVOKED", "SUCCEEDED", "DEVICE",
                deviceId.ToString("D"), new { authorizationVersion = device.AuthorizationVersion + 1 }, now);
            return true;
        });

    public IReadOnlyList<PolicyContract> ListPolicies(TenantRequestContext context) => store.Execute(context.TenantId, tenant =>
    {
        context.RequireRole("OWNER", "ADMIN", "SECURITY_AUDITOR");
        return tenant.Policies.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToContract).ToArray();
    });

    public PolicyContract CreatePolicy(TenantRequestContext context, PolicyDocumentRequest request,
        string idempotencyKey) => store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN");
            ValidateIdempotency(idempotencyKey);
            _ = GovernancePolicyEngine.Parse(request.Document);
            string name = RequiredText(request.Name, 200, "POLICY_NAME_INVALID");
            if (tenant.Policies.Values.Any(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw Conflict("POLICY_NAME_CONFLICT");
            string idempotencyHash = crypto.LookupHash("policy-idempotency\0" + context.TenantId + "\0" + idempotencyKey);
            string requestHash = RequestHash(request);
            if (tenant.Idempotency.TryGetValue(idempotencyHash, out IdempotencyRecord? prior))
            {
                if (!string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal)) throw Conflict("IDEMPOTENCY_KEY_REUSED");
                return ToContract(tenant.Policies[prior.ResourceId]);
            }
            DateTimeOffset now = clock.UtcNow;
            Guid id = crypto.DeriveGuid("policy-id", context.TenantId + "\0" + idempotencyKey);
            PolicyVersionRecord version = new(1, request.Document.Clone(), DocumentHash(request.Document),
                context.UserId, now, null);
            PolicyRecord policy = new(id, name, request.Description, "DRAFT", null, 1, context.UserId,
                now, now, new Dictionary<int, PolicyVersionRecord> { [1] = version });
            tenant.Policies.Add(id, policy);
            tenant.Idempotency.Add(idempotencyHash, new IdempotencyRecord(requestHash, "POLICY", id));
            GovernanceAudit.Append(tenant, context.Actor, "POLICY", "POLICY_CREATED", "SUCCEEDED", "POLICY",
                id.ToString("D"), new { name, version = 1, documentHash = version.DocumentHash }, now);
            return ToContract(policy);
        });

    public PolicyContract CreatePolicyVersion(TenantRequestContext context, Guid policyId,
        PolicyDocumentRequest request, long resourceVersion) => store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN");
            _ = GovernancePolicyEngine.Parse(request.Document);
            if (!tenant.Policies.TryGetValue(policyId, out PolicyRecord? policy)) throw NotFound();
            if (policy.ResourceVersion != resourceVersion) throw Conflict("RESOURCE_VERSION_CONFLICT");
            int nextVersion = policy.Versions.Keys.Max() + 1;
            DateTimeOffset now = clock.UtcNow;
            policy.Versions.Add(nextVersion, new PolicyVersionRecord(nextVersion, request.Document.Clone(),
                DocumentHash(request.Document), context.UserId, now, null));
            PolicyRecord updated = policy with
            {
                Description = request.Description ?? policy.Description,
                ResourceVersion = policy.ResourceVersion + 1,
                UpdatedAt = now,
            };
            tenant.Policies[policyId] = updated;
            GovernanceAudit.Append(tenant, context.Actor, "POLICY", "POLICY_VERSION_CREATED", "SUCCEEDED",
                "POLICY", policyId.ToString("D"), new
                {
                    version = nextVersion,
                    documentHash = policy.Versions[nextVersion].DocumentHash
                }, now);
            return ToContract(updated);
        });

    public PolicyContract ActivatePolicy(TenantRequestContext context, Guid policyId,
        ActivatePolicyVersionRequest request, long resourceVersion) => store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN");
            context.RequireFreshMfa(clock.UtcNow);
            if (!tenant.Policies.TryGetValue(policyId, out PolicyRecord? policy)) throw NotFound();
            if (policy.ResourceVersion != resourceVersion) throw Conflict("RESOURCE_VERSION_CONFLICT");
            if (!policy.Versions.TryGetValue(request.Version, out PolicyVersionRecord? version)) throw NotFound();
            DateTimeOffset now = clock.UtcNow;
            policy.Versions[request.Version] = version with { ActivatedAt = now };
            PolicyRecord updated = policy with
            {
                Status = "ACTIVE",
                ActiveVersion = request.Version,
                ResourceVersion = policy.ResourceVersion + 1,
                UpdatedAt = now,
            };
            tenant.Policies[policyId] = updated;
            TouchAuthorization(tenant, now);
            GovernanceAudit.Append(tenant, context.Actor, "POLICY", "POLICY_ACTIVATED", "SUCCEEDED", "POLICY",
                policyId.ToString("D"), new { version = request.Version, version.DocumentHash }, now);
            return ToContract(updated);
        });

    public PolicyDecisionContract EvaluatePolicy(TenantRequestContext context, PolicyEvaluationRequest request) =>
        store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN", "OPERATOR");
            DateTimeOffset now = clock.UtcNow;
            PolicyDecisionContract decision = GovernancePolicyEngine.Evaluate(tenant, context, request, now);
            tenant.PolicyDecisions[decision.DecisionId] = new PolicyDecisionRecord(decision.DecisionId,
                context.UserId, request.DeviceId, decision.InputHash, decision, now);
            GovernanceAudit.Append(tenant, context.Actor, "AUTHORIZATION", "POLICY_EVALUATED",
                decision.Allow ? "SUCCEEDED" : "DENIED", request.DeviceId is null ? null : "DEVICE",
                request.DeviceId?.ToString("D"), new
                {
                    decisionId = decision.DecisionId,
                    decision.Allow,
                    decision.PolicyVersionIds,
                    decision.GrantedScopes,
                    decision.ExplanationCodes,
                    decision.InputHash
                }, now);
            return decision;
        });

    public PagedAuditEvents ListAudit(TenantRequestContext context, DateTimeOffset? from,
        DateTimeOffset? to, string? category) => store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER", "ADMIN", "SECURITY_AUDITOR");
            AuditVerificationContract verification = GovernanceAudit.Verify(tenant, clock.UtcNow);
            IEnumerable<GovernanceAuditRecord> query = tenant.AuditEvents;
            if (from is { } start) query = query.Where(value => value.OccurredAt >= start);
            if (to is { } end) query = query.Where(value => value.OccurredAt <= end);
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(value => value.Category == category);
            AuditEventContract[] items = query.OrderByDescending(value => value.Sequence).Take(500)
                .Select(GovernanceAudit.ToContract).ToArray();
            return new PagedAuditEvents(items, null, verification.Valid);
        });

    public AuditVerificationContract VerifyAudit(TenantRequestContext context) => store.Execute(context.TenantId, tenant =>
    {
        context.RequireRole("OWNER", "ADMIN", "SECURITY_AUDITOR");
        return GovernanceAudit.Verify(tenant, clock.UtcNow);
    });

    public string ExportAudit(TenantRequestContext context) => store.Execute(context.TenantId, tenant =>
    {
        context.RequireRole("OWNER", "ADMIN", "SECURITY_AUDITOR");
        context.RequireFreshMfa(clock.UtcNow);
        GovernanceAudit.Append(tenant, context.Actor, "AUDIT", "AUDIT_EXPORTED", "SUCCEEDED", "TENANT",
            context.TenantId.ToString("D"), new { format = "JSONL" }, clock.UtcNow);
        return GovernanceAudit.ExportJsonLines(tenant);
    });

    public DataExportResult RequestDataExport(TenantRequestContext context, DataExportRequest request,
        string idempotencyKey, string baseUrl) => store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER");
            context.RequireFreshMfa(clock.UtcNow);
            ValidateIdempotency(idempotencyKey);
            string format = request.Format ?? "JSONL";
            if (format is not ("JSONL" or "CSV_BUNDLE")) throw BadRequest("EXPORT_FORMAT_INVALID");
            string idempotencyHash = crypto.LookupHash("export-idempotency\0" + context.TenantId + "\0" + idempotencyKey);
            string requestHash = RequestHash(request);
            if (tenant.Idempotency.TryGetValue(idempotencyHash, out IdempotencyRecord? prior))
            {
                if (!string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal)) throw Conflict("IDEMPOTENCY_KEY_REUSED");
                return ToDataExportContract(tenant.DataExports[prior.ResourceId], baseUrl);
            }
            DateTimeOffset now = clock.UtcNow;
            Guid id = crypto.DeriveGuid("export-id", context.TenantId + "\0" + idempotencyKey);
            DataExportRecord export = new(id, context.UserId, format, "QUEUED", now, null, null,
                null, null, null, null);
            tenant.DataExports.Add(id, export);
            tenant.Idempotency.Add(idempotencyHash, new IdempotencyRecord(requestHash, "DATA_EXPORT", id));
            GovernanceAudit.Append(tenant, context.Actor, "PRIVACY", "DATA_EXPORT_REQUESTED", "SUCCEEDED",
                "DATA_EXPORT", id.ToString("D"), new { format }, now);
            return ToDataExportContract(export, baseUrl);
        });

    public DataExportResult GetDataExport(TenantRequestContext context, Guid requestId, string baseUrl) =>
        store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER");
            return tenant.DataExports.TryGetValue(requestId, out DataExportRecord? export)
                ? ToDataExportContract(export, baseUrl) : throw NotFound();
        });

    public ExportDownload DownloadDataExport(TenantRequestContext context, Guid requestId, string token) =>
        store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER");
            if (!tenant.DataExports.TryGetValue(requestId, out DataExportRecord? export) || export.State != "READY" ||
                export.DownloadExpiresAt <= clock.UtcNow || export.DownloadedAt is not null || export.ObjectKey is null)
                throw NotFound();
            string expected = crypto.DeriveSecret("export-download", requestId.ToString("D"));
            if (!FixedSecret(expected, token)) throw NotFound();
            byte[] content = exportStore.Read(export.ObjectKey);
            string actualHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            if (!string.Equals(actualHash, export.ObjectSha256, StringComparison.Ordinal))
                throw new ControlPlaneException(503, "EXPORT_INTEGRITY_FAILED", "The export failed integrity verification.");
            DateTimeOffset now = clock.UtcNow;
            tenant.DataExports[requestId] = export with { DownloadedAt = now };
            GovernanceAudit.Append(tenant, context.Actor, "PRIVACY", "DATA_EXPORT_DOWNLOADED", "SUCCEEDED",
                "DATA_EXPORT", requestId.ToString("D"), new { sha256 = actualHash }, now);
            string fileName = $"remote-support-export-{requestId:D}.{(export.Format == "JSONL" ? "jsonl" : "zip")}";
            return new ExportDownload(content, export.Format == "JSONL" ? "application/x-ndjson" : "application/zip", fileName);
        });

    public TenantClosureResult RequestClosure(TenantRequestContext context, TenantClosureRequest request,
        string idempotencyKey) => store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER");
            context.RequireFreshMfa(clock.UtcNow);
            ValidateIdempotency(idempotencyKey);
            if (!string.Equals(request.ConfirmationPhrase.Trim(), tenant.Tenant.Slug, StringComparison.Ordinal))
                throw BadRequest("CLOSURE_CONFIRMATION_INVALID");
            _ = RequiredText(request.Reason, 2000, "CLOSURE_REASON_INVALID");
            string idempotencyHash = crypto.LookupHash("closure-idempotency\0" + context.TenantId + "\0" + idempotencyKey);
            string requestHash = RequestHash(request);
            if (tenant.Idempotency.TryGetValue(idempotencyHash, out IdempotencyRecord? prior))
            {
                if (!string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal)) throw Conflict("IDEMPOTENCY_KEY_REUSED");
                return ToContract(tenant.ClosureRequests[prior.ResourceId]);
            }
            if (tenant.ClosureRequests.Values.Any(value => value.State is "COOLING_OFF" or "SCHEDULED"))
                throw Conflict("CLOSURE_ALREADY_PENDING");
            DateTimeOffset now = clock.UtcNow;
            Guid id = crypto.DeriveGuid("closure-id", context.TenantId + "\0" + idempotencyKey);
            ClosureRecord closure = new(id, context.UserId, "COOLING_OFF", now,
                now.AddDays(options.ClosureCoolingOffDays), now, null, null, 1);
            tenant.ClosureRequests.Add(id, closure);
            tenant.Idempotency.Add(idempotencyHash, new IdempotencyRecord(requestHash, "TENANT_CLOSURE", id));
            GovernanceAudit.Append(tenant, context.Actor, "PRIVACY", "TENANT_CLOSURE_REQUESTED", "SUCCEEDED",
                "TENANT_CLOSURE", id.ToString("D"), new { closure.EffectiveAt, reasonStored = false }, now);
            return ToContract(closure);
        });

    public TenantClosureResult GetClosure(TenantRequestContext context, Guid requestId) =>
        store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER");
            return tenant.ClosureRequests.TryGetValue(requestId, out ClosureRecord? closure)
                ? ToContract(closure) : throw NotFound();
        });

    public void CancelClosure(TenantRequestContext context, Guid requestId, long version) =>
        store.Execute(context.TenantId, tenant =>
        {
            context.RequireRole("OWNER");
            context.RequireFreshMfa(clock.UtcNow);
            if (!tenant.ClosureRequests.TryGetValue(requestId, out ClosureRecord? closure)) throw NotFound();
            if (version != closure.StateVersion) throw Conflict("RESOURCE_VERSION_CONFLICT");
            if (closure.State != "COOLING_OFF" || closure.EffectiveAt <= clock.UtcNow)
                throw Conflict("CLOSURE_CANNOT_CANCEL");
            DateTimeOffset now = clock.UtcNow;
            tenant.ClosureRequests[requestId] = closure with
            {
                State = "CANCELLED",
                CancelledAt = now,
                StateVersion = closure.StateVersion + 1,
            };
            GovernanceAudit.Append(tenant, context.Actor, "PRIVACY", "TENANT_CLOSURE_CANCELLED", "CANCELLED",
                "TENANT_CLOSURE", requestId.ToString("D"), new { }, now);
            return true;
        });

    public SupportGrantContract CreateSupportGrant(TenantRequestContext context, SupportGrantRequest request)
    {
        context.RequireRole("OWNER");
        context.RequireFreshMfa(clock.UtcNow);
        if (request.TenantId != context.TenantId || request.SupportSubject.Length is < 1 or > 256 ||
            request.DurationMinutes is < 1 or > 60 || string.IsNullOrWhiteSpace(request.ReasonCode))
            throw new ControlPlaneException(403, "SUPPORT_GRANT_INVALID", "The support grant was invalid.");
        return store.Execute(context.TenantId, tenant =>
        {
            DateTimeOffset now = clock.UtcNow;
            SupportGrantRecord grant = new(Guid.CreateVersion7(now), request.SupportSubject, context.Actor.Subject,
                request.ReasonCode, now, now.AddMinutes(request.DurationMinutes), request.BreakGlass);
            tenant.SupportGrants.Add(grant.Id, grant);
            GovernanceAudit.Append(tenant, context.Actor, "SUPPORT", request.BreakGlass ? "BREAK_GLASS_GRANTED" : "JIT_ACCESS_GRANTED",
                "SUCCEEDED", "SUPPORT_GRANT", grant.Id.ToString("D"), new
                {
                    grant.SupportSubject,
                    grant.ReasonCode,
                    grant.ExpiresAt,
                    grant.BreakGlass
                }, now);
            return new SupportGrantContract(grant.Id, context.TenantId, grant.SupportSubject, grant.ReasonCode,
                grant.ExpiresAt, grant.BreakGlass);
        });
    }

    public TenantContract ReadTenantAsSupport(Guid tenantId, Guid grantId, TenantActor supportActor) =>
        store.Execute(tenantId, tenant =>
        {
            if (supportActor.PlatformRole != "PLATFORM_SUPPORT" ||
                !tenant.SupportGrants.TryGetValue(grantId, out SupportGrantRecord? grant) ||
                grant.ExpiresAt <= clock.UtcNow ||
                !string.Equals(grant.SupportSubject, supportActor.Subject, StringComparison.Ordinal))
                throw NotFound();
            GovernanceAudit.Append(tenant, supportActor with { ActorType = "PLATFORM_SUPPORT" }, "SUPPORT",
                "CUSTOMER_METADATA_READ", "SUCCEEDED", "TENANT", tenantId.ToString("D"),
                new { grantId, grant.ReasonCode, grant.BreakGlass }, clock.UtcNow);
            return ToContract(tenant.Tenant);
        });

    internal IGovernanceStore Store => store;

    private static byte[] EnrollmentProofBytes(DeviceEnrollmentRequest request, string thumbprint)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new
        {
            request.InstallationId,
            deviceKeyThumbprint = thumbprint,
            request.DeviceInfo,
            request.Proof.Nonce,
        });
        byte[] prefix = Encoding.UTF8.GetBytes("RSP-DEVICE-ENROLLMENT-V1\0");
        byte[] canonical = ControlPlaneCrypto.Canonicalize(payload);
        byte[] result = new byte[prefix.Length + canonical.Length];
        prefix.CopyTo(result, 0);
        canonical.CopyTo(result, prefix.Length);
        return result;
    }

    internal static byte[] CreateEnrollmentProofBytes(DeviceEnrollmentRequest request, string thumbprint) =>
        EnrollmentProofBytes(request, thumbprint);

    private static void ValidateDeviceInfo(DeviceInfo info)
    {
        _ = RequiredText(info.DisplayName, 200, "DEVICE_INFO_INVALID");
        _ = RequiredText(info.OsVersion, 128, "DEVICE_INFO_INVALID");
        _ = RequiredText(info.AppVersion, 64, "DEVICE_INFO_INVALID");
        if (info.Architecture is not ("x64" or "arm64")) throw BadRequest("DEVICE_INFO_INVALID");
    }

    private static int ActivePolicyVersion(TenantAggregate tenant) => tenant.Policies.Values
        .Where(value => value.ActiveVersion is not null).Select(value => value.ActiveVersion!.Value).DefaultIfEmpty(0).Max();

    private static void EnsureOwnerRemains(TenantAggregate tenant, MembershipRecord target,
        string[] roles, string status)
    {
        bool targetWasOwner = target.Status == "ACTIVE" && target.Roles.Contains("OWNER", StringComparer.Ordinal);
        bool targetWillOwn = status == "ACTIVE" && roles.Contains("OWNER", StringComparer.Ordinal);
        if (targetWasOwner && !targetWillOwn && !tenant.Memberships.Values.Any(value =>
                value.UserId != target.UserId && value.Status == "ACTIVE" && value.Roles.Contains("OWNER", StringComparer.Ordinal)))
            throw Conflict("LAST_OWNER_REQUIRED");
    }

    private static string[] ValidateRoles(IReadOnlyList<string> roles, HashSet<string> allowed)
    {
        string[] result = roles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (result.Length is < 1 or > 5 || result.Any(role => !allowed.Contains(role))) throw BadRequest("ROLES_INVALID");
        return result;
    }

    private static string NormalizeEmail(string value)
    {
        string email = value.Trim().ToLowerInvariant();
        int separator = email.LastIndexOf('@');
        if (email.Length is < 3 or > 320 || separator is < 1 || separator == email.Length - 1 ||
            email.Contains(' ') || email.Contains('\r') || email.Contains('\n')) throw BadRequest("EMAIL_INVALID");
        return email;
    }

    private static string RequiredText(string value, int maximum, string code)
    {
        string result = value?.Trim() ?? string.Empty;
        if (result.Length is < 1 || result.Length > maximum || result.Any(char.IsControl)) throw BadRequest(code);
        return result;
    }

    private static void ValidateIdempotency(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length is < 16 or > 128)
            throw BadRequest("IDEMPOTENCY_KEY_INVALID");
    }

    private static string RequestHash<T>(T value) => Convert.ToHexString(SHA256.HashData(
        ControlPlaneCrypto.Canonicalize(JsonSerializer.SerializeToElement(value)))).ToLowerInvariant();

    private static string DocumentHash(JsonElement value) => Convert.ToHexString(
        SHA256.HashData(ControlPlaneCrypto.Canonicalize(value))).ToLowerInvariant();

    private static bool FixedSecret(string expected, string actual)
    {
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        byte[] actualBytes = Encoding.UTF8.GetBytes(actual ?? string.Empty);
        return expectedBytes.Length == actualBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static void TouchAuthorization(TenantAggregate tenant, DateTimeOffset now) => tenant.Tenant = tenant.Tenant with
    {
        AuthorizationVersion = tenant.Tenant.AuthorizationVersion + 1,
        UpdatedAt = now,
    };

    private static TenantContract ToContract(TenantRecord value) => new(value.Id, value.Name, value.Slug,
        value.Status, value.PlanCode, value.DataRegion, value.CreatedAt);
    private static TenantSettingsContract ToContract(TenantSettingsRecord value) => new(value.Version,
        value.RetentionDays, value.AllowedFeatures, value.FileSizeLimitBytes, false);
    private static MembershipContract ToContract(MembershipRecord value) => new(value.UserId, value.DisplayName,
        value.Email, value.Roles, value.Status, value.PrivilegeVersion);
    private static InvitationContract ToContract(InvitationRecord value, string? token, DateTimeOffset now) =>
        new(value.Id, value.Email, value.Roles, value.Status == "PENDING" && value.ExpiresAt <= now ? "EXPIRED" : value.Status,
            value.ExpiresAt, value.CreatedAt, token);
    private static DeviceContract ToContract(DeviceRecord value) => new(value.Id, value.DisplayName, value.Status,
        value.AppVersion, value.OsVersion, false, value.EnrolledAt, value.LastSeenAt, value.AuthorizationVersion);
    private static PolicyContract ToContract(PolicyRecord value) => new(value.Id, value.Name, value.Status,
        value.ActiveVersion, value.CreatedAt, value.ResourceVersion);
    private DataExportResult ToDataExportContract(DataExportRecord value, string baseUrl)
    {
        string? url = value.State == "READY" && value.DownloadExpiresAt > clock.UtcNow && value.DownloadedAt is null
            ? $"{baseUrl.TrimEnd('/')}/v1/tenant/data-exports/{value.Id:D}/download?token={Uri.EscapeDataString(crypto.DeriveSecret("export-download", value.Id.ToString("D")))}"
            : null;
        return new DataExportResult(value.Id, value.State, value.RequestedAt, value.DownloadExpiresAt, url);
    }
    private static TenantClosureResult ToContract(ClosureRecord value) => new(value.Id, value.State,
        value.EffectiveAt, value.StateVersion);

    private static ControlPlaneException BadRequest(string code) => new(400, code, "The request was invalid.");
    private static ControlPlaneException Conflict(string code) => new(409, code, "The resource state conflicted with the request.");
    private static ControlPlaneException NotFound() => new(404, "RESOURCE_NOT_FOUND", "The resource was not found.");
}

internal sealed record ExportDownload(byte[] Content, string ContentType, string FileName);
