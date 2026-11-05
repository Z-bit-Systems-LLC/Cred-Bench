using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CredBench.Core.Models.TechnologyDetails;

namespace CredBench.Core.Services.Leaf;

/// <summary>Outcome of a Printed ID lookup.</summary>
/// <param name="PrintedId">The Printed ID from the token, or null if the lookup failed.</param>
/// <param name="OpenId">The Open ID the token names, if any.</param>
/// <param name="SignatureVerified">True if the token verified under the server's published key.</param>
/// <param name="KeyId">The token's key ID.</param>
/// <param name="Error">Why the lookup failed, or why the token could not be verified.</param>
public sealed record LeafPrintedIdResult(
    string? PrintedId, long? OpenId, bool SignatureVerified, string? KeyId, string? Error);

/// <summary>
/// Looks up a LEAF Verified card's Printed ID through the LEAF Community tap service.
/// <para>
/// The card's NDEF application (F51CD5) holds a one-time Secure Unique NFC URL on
/// d.leaf-community.com. The server checks it and redirects to a page whose <c>token</c> query
/// parameter is an ES256 JWT carrying <c>open_id</c> and <c>printed_id</c>. This service sends
/// that single request with redirects turned off, so the target page (an order manifest) is never
/// fetched, then verifies the token against the server's published JWKS.
/// </para>
/// <para>
/// This contacts an external service, so callers must only use it when the user asks.
/// </para>
/// </summary>
public class LeafPrintedIdLookup(HttpMessageHandler? handler = null)
{
    /// <summary>The only host a lookup will contact.</summary>
    public const string ServiceHost = "d.leaf-community.com";

    private static readonly Uri JwksUri = new($"https://{ServiceHost}/.well-known/jwks.json");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>True if <paramref name="url"/> is an https URL on the LEAF Community tap service.</summary>
    public static bool IsTapServiceUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.Equals(uri.Host, ServiceHost, StringComparison.OrdinalIgnoreCase);

    public async Task<LeafPrintedIdResult> LookupAsync(string tapUrl, CancellationToken cancellationToken = default)
    {
        // The URL comes from the card: never let a card point us at another server
        if (!IsTapServiceUrl(tapUrl))
            return Failed($"The card's tap URL is not on https://{ServiceHost}; it was not contacted.");

        using var client = CreateClient();

        string token;
        try
        {
            using var response = await client.GetAsync(tapUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;
            if (status is < 300 or > 399 || response.Headers.Location is not { } location)
                return Failed($"The tap service answered {status} instead of a redirect. " +
                              "The tap URL is single-use: rescan the card to get a fresh one.");

            var target = location.IsAbsoluteUri ? location : new Uri(new Uri(tapUrl), location);
            if (System.Web.HttpUtility.ParseQueryString(target.Query)["token"] is not { Length: > 0 } t)
                return Failed("The tap service's redirect carries no token.");
            token = t;
        }
        catch (HttpRequestException ex)
        {
            return Failed($"Could not reach {ServiceHost}: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed($"{ServiceHost} did not answer in time.");
        }

        Jwt jwt;
        try
        {
            jwt = Jwt.Parse(token);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return Failed($"The token could not be read: {ex.Message}");
        }

        var (verified, verifyError) = await VerifyAsync(client, jwt, cancellationToken);
        return new LeafPrintedIdResult(jwt.PrintedId, jwt.OpenId, verified, jwt.KeyId,
            jwt.PrintedId is null ? "The token has no printed_id." : verifyError);
    }

    /// <summary>
    /// Adds a lookup result to the card details, cross-checking the token's Open ID against the certificate.
    /// </summary>
    public static LEAFVerifiedDetails Apply(LEAFVerifiedDetails details, LeafPrintedIdResult result)
    {
        if (result.PrintedId is null)
            return details with { PrintedId = null, PrintedIdNote = result.Error };

        List<string> notes = [$"From {ServiceHost}"];
        notes.Add(result.SignatureVerified
            ? $"token signature verified ({result.KeyId})"
            : $"token signature NOT verified: {result.Error}");

        if (result.OpenId is { } tokenOpenId && details.OpenId is { } certOpenId)
            notes.Add(tokenOpenId == certOpenId
                ? "Open ID matches the certificate"
                : $"WARNING: token Open ID {tokenOpenId} differs from the certificate's {certOpenId}");

        return details with { PrintedId = result.PrintedId, PrintedIdNote = string.Join("; ", notes) };
    }

    private HttpClient CreateClient()
    {
        var client = handler is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            : new HttpClient(handler, disposeHandler: false);
        client.Timeout = Timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Cred-Bench");
        return client;
    }

    private static async Task<(bool Verified, string? Error)> VerifyAsync(
        HttpClient client, Jwt jwt, CancellationToken cancellationToken)
    {
        if (jwt.Algorithm != "ES256")
            return (false, $"unsupported algorithm {jwt.Algorithm}");

        JsonElement keys;
        try
        {
            var jwks = await client.GetStringAsync(JwksUri, cancellationToken);
            keys = JsonDocument.Parse(jwks).RootElement.GetProperty("keys").Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                       or InvalidOperationException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return (false, $"could not fetch the server's keys: {ex.Message}");
        }

        foreach (var key in keys.EnumerateArray())
        {
            if (!key.TryGetProperty("kid", out var kid) || kid.GetString() != jwt.KeyId)
                continue;

            try
            {
                if (key.GetProperty("kty").GetString() != "EC" || key.GetProperty("crv").GetString() != "P-256")
                    return (false, "the published key is not P-256");

                using var ecdsa = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint
                    {
                        X = Base64UrlDecode(key.GetProperty("x").GetString()!),
                        Y = Base64UrlDecode(key.GetProperty("y").GetString()!)
                    }
                });
                return ecdsa.VerifyData(jwt.SigningInput, jwt.Signature, HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                    ? (true, null)
                    : (false, "signature does not verify");
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or KeyNotFoundException
                                           or InvalidOperationException)
            {
                return (false, $"the published key is unusable: {ex.Message}");
            }
        }

        return (false, $"no published key with id {jwt.KeyId}");
    }

    private static LeafPrintedIdResult Failed(string error) => new(null, null, false, null, error);

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    private sealed record Jwt(
        string? Algorithm, string? KeyId, string? PrintedId, long? OpenId, byte[] SigningInput, byte[] Signature)
    {
        public static Jwt Parse(string token)
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                throw new FormatException("not a JWT");

            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var h = header.RootElement;
            var p = payload.RootElement;

            return new Jwt(
                h.TryGetProperty("alg", out var alg) ? alg.GetString() : null,
                h.TryGetProperty("kid", out var kid) ? kid.GetString() : null,
                p.TryGetProperty("printed_id", out var printed) && printed.ValueKind == JsonValueKind.String
                    ? printed.GetString()
                    : null,
                p.TryGetProperty("open_id", out var openId) && openId.ValueKind == JsonValueKind.Number
                    ? openId.GetInt64()
                    : null,
                Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
                Base64UrlDecode(parts[2]));
        }
    }
}
