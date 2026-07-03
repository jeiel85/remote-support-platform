using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RemoteSupport.Infrastructure;

public sealed record OperatorOidcSession(string AccessToken, DateTimeOffset ExpiresAt);

public sealed class OperatorOidcClient(HttpClient http)
{
    public async Task<OperatorOidcSession> SignInAsync(OperatorOidcConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Uri discoveryUri = new(configuration.Authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
        using HttpResponseMessage discoveryResponse = await http.GetAsync(discoveryUri, cancellationToken).ConfigureAwait(false);
        discoveryResponse.EnsureSuccessStatusCode();
        using JsonDocument discovery = await JsonDocument.ParseAsync(
            await discoveryResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Uri authorizationEndpoint = SecureEndpoint(discovery.RootElement, "authorization_endpoint", configuration.Authority);
        Uri tokenEndpoint = SecureEndpoint(discovery.RootElement, "token_endpoint", configuration.Authority);

        string verifier = EphemeralPeerIdentity.Base64Url(RandomNumberGenerator.GetBytes(48));
        string challenge = EphemeralPeerIdentity.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = EphemeralPeerIdentity.Base64Url(RandomNumberGenerator.GetBytes(32));
        string nonce = EphemeralPeerIdentity.Base64Url(RandomNumberGenerator.GetBytes(32));
        int port = ReserveLoopbackPort();
        Uri redirect = new($"http://127.0.0.1:{port}/callback/");
        using HttpListener listener = new();
        listener.Prefixes.Add(redirect.AbsoluteUri);
        listener.Start();
        Uri authorization = BuildAuthorizationUri(authorizationEndpoint, configuration, redirect, challenge, state, nonce);
        using Process? browser = Process.Start(new ProcessStartInfo(authorization.AbsoluteUri) { UseShellExecute = true });
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new ControlPlaneClientException("AUTH_REQUIRED", "Operator sign-in timed out.", 0) { Source = exception.Source };
        }
        await RespondAsync(context.Response, cancellationToken).ConfigureAwait(false);
        string returnedState = context.Request.QueryString["state"] ?? string.Empty;
        string code = context.Request.QueryString["code"] ?? string.Empty;
        string? error = context.Request.QueryString["error"];
        if (error is not null || code.Length is < 8 or > 4096 || !FixedText(state, returnedState))
            throw new ControlPlaneClientException("AUTH_PROOF_INVALID", "OIDC authorization response was invalid.", 0);

        using FormUrlEncodedContent tokenRequest = new(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = configuration.ClientId,
            ["code"] = code,
            ["redirect_uri"] = redirect.AbsoluteUri,
            ["code_verifier"] = verifier,
        });
        using HttpResponseMessage tokenResponse = await http.PostAsync(tokenEndpoint, tokenRequest, cancellationToken).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();
        using JsonDocument token = await JsonDocument.ParseAsync(
            await tokenResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        string accessToken = token.RootElement.GetProperty("access_token").GetString() ?? string.Empty;
        int expiresIn = token.RootElement.TryGetProperty("expires_in", out JsonElement expiry) ? expiry.GetInt32() : 300;
        if (accessToken.Length is < 32 or > 16_384 || expiresIn is < 30 or > 86_400)
            throw new ControlPlaneClientException("AUTH_PROOF_INVALID", "OIDC token response was invalid.", 0);
        return new OperatorOidcSession(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private static Uri BuildAuthorizationUri(Uri endpoint, OperatorOidcConfiguration configuration, Uri redirect,
        string challenge, string state, string nonce)
    {
        Dictionary<string, string> values = new()
        {
            ["response_type"] = "code",
            ["client_id"] = configuration.ClientId,
            ["redirect_uri"] = redirect.AbsoluteUri,
            ["scope"] = string.Join(' ', configuration.Scopes),
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["nonce"] = nonce,
        };
        string query = string.Join('&', values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        return new UriBuilder(endpoint) { Query = query }.Uri;
    }

    private static Uri SecureEndpoint(JsonElement discovery, string name, Uri authority)
    {
        if (!discovery.TryGetProperty(name, out JsonElement property) ||
            !Uri.TryCreate(property.GetString(), UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(endpoint.Host, authority.Host, StringComparison.OrdinalIgnoreCase))
            throw new ControlPlaneClientException("AUTH_PROOF_INVALID", "OIDC discovery endpoint is invalid.", 0);
        return endpoint;
    }

    private static int ReserveLoopbackPort()
    {
        System.Net.Sockets.TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task RespondAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        const string body = "<!doctype html><meta charset=utf-8><title>Remote Support</title><p>Sign-in completed. You may close this window.</p>";
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.Close();
    }

    private static bool FixedText(string left, string right)
    {
        byte[] leftBytes = Encoding.ASCII.GetBytes(left);
        byte[] rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
