using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.Services.Leaf;
using NUnit.Framework;

namespace CredBench.Core.Tests.Leaf;

[TestFixture]
public class LeafPrintedIdLookupTests
{
    private const string TapUrl = "https://d.leaf-community.com?uid=00112233&bid=44556677&cmac=8899AABB";
    private const string KeyId = "test-key";

    private ECDsa _key = null!;

    [SetUp]
    public void SetUp() => _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    [TearDown]
    public void TearDown() => _key.Dispose();

    private sealed class FakeServer : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(Respond(request));
        }
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private string Token(object payload, ECDsa? signer = null, string kid = KeyId)
    {
        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid, typ = "JWT" }));
        var body = B64(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = (signer ?? _key).SignData(Encoding.ASCII.GetBytes($"{header}.{body}"),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{body}.{B64(signature)}";
    }

    private string Jwks()
    {
        var q = _key.ExportParameters(false).Q;
        return JsonSerializer.Serialize(new
        {
            keys = new[] { new { kty = "EC", use = "sig", kid = KeyId, crv = "P-256", alg = "ES256", x = B64(q.X!), y = B64(q.Y!) } }
        });
    }

    private FakeServer Server(string? token, HttpStatusCode tapStatus = HttpStatusCode.Found, string? jwks = null)
    {
        var server = new FakeServer();
        server.Respond = request =>
        {
            if (request.RequestUri!.AbsolutePath == "/.well-known/jwks.json")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jwks ?? Jwks()) };

            var response = new HttpResponseMessage(tapStatus);
            if (token is not null)
                response.Headers.Location = new Uri($"/m/orders/o-TEST/editor?token={token}", UriKind.Relative);
            return response;
        };
        return server;
    }

    [Test]
    public async Task Lookup_ValidToken_ReturnsVerifiedPrintedId()
    {
        var server = Server(Token(new { open_id = 160000000000L, printed_id = "ABCDE-001" }));

        var result = await new LeafPrintedIdLookup(server).LookupAsync(TapUrl);

        Assert.That(result.PrintedId, Is.EqualTo("ABCDE-001"));
        Assert.That(result.OpenId, Is.EqualTo(160000000000L));
        Assert.That(result.SignatureVerified, Is.True);
        Assert.That(result.KeyId, Is.EqualTo(KeyId));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task Lookup_NeverFollowsTheRedirect()
    {
        var server = Server(Token(new { open_id = 1L, printed_id = "X" }));

        await new LeafPrintedIdLookup(server).LookupAsync(TapUrl);

        Assert.That(server.Requests.Select(u => u.AbsolutePath), Is.EqualTo(new[] { "/", "/.well-known/jwks.json" }));
        Assert.That(server.Requests[0].ToString(), Is.EqualTo(TapUrl.Replace("com?", "com/?")));
    }

    [TestCase("http://d.leaf-community.com?uid=00")]
    [TestCase("https://evil.example.com?uid=00")]
    [TestCase("https://d.leaf-community.com.evil.example?uid=00")]
    [TestCase("https://d.leaf-community.com:8443?uid=00")]
    [TestCase("not a url")]
    public async Task Lookup_OtherHosts_AreNeverContacted(string url)
    {
        var server = Server(Token(new { open_id = 1L, printed_id = "X" }));

        var result = await new LeafPrintedIdLookup(server).LookupAsync(url);

        Assert.That(server.Requests, Is.Empty);
        Assert.That(result.PrintedId, Is.Null);
        Assert.That(result.Error, Does.Contain("not contacted"));
    }

    [Test]
    public async Task Lookup_NoRedirect_ExplainsSingleUse()
    {
        var server = Server(null, HttpStatusCode.OK);

        var result = await new LeafPrintedIdLookup(server).LookupAsync(TapUrl);

        Assert.That(result.PrintedId, Is.Null);
        Assert.That(result.Error, Does.Contain("single-use"));
    }

    [Test]
    public async Task Lookup_TokenSignedByAnotherKey_IsUnverified()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var server = Server(Token(new { open_id = 1L, printed_id = "FORGED-1" }, other));

        var result = await new LeafPrintedIdLookup(server).LookupAsync(TapUrl);

        Assert.That(result.PrintedId, Is.EqualTo("FORGED-1"));
        Assert.That(result.SignatureVerified, Is.False);
        Assert.That(result.Error, Does.Contain("does not verify"));
    }

    [Test]
    public async Task Lookup_UnknownKeyId_IsUnverified()
    {
        var server = Server(Token(new { open_id = 1L, printed_id = "X" }, kid: "other"));

        var result = await new LeafPrintedIdLookup(server).LookupAsync(TapUrl);

        Assert.That(result.SignatureVerified, Is.False);
        Assert.That(result.Error, Does.Contain("no published key"));
    }

    [Test]
    public async Task Lookup_MalformedToken_Fails()
    {
        var server = Server("not.a-token");

        var result = await new LeafPrintedIdLookup(server).LookupAsync(TapUrl);

        Assert.That(result.PrintedId, Is.Null);
        Assert.That(result.Error, Does.StartWith("The token could not be read"));
    }

    [Test]
    public void Apply_MatchingOpenId_NotesVerification()
    {
        var details = new LEAFVerifiedDetails { OpenId = 160000000000 };
        var result = new LeafPrintedIdResult("ABCDE-001", 160000000000, true, KeyId, null);

        var applied = LeafPrintedIdLookup.Apply(details, result);

        Assert.That(applied.PrintedId, Is.EqualTo("ABCDE-001"));
        Assert.That(applied.PrintedIdNote, Does.Contain("signature verified"));
        Assert.That(applied.PrintedIdNote, Does.Contain("Open ID matches"));
        Assert.That(applied.GetFields(), Has.Some.EqualTo(("Printed ID", "ABCDE-001")));
    }

    [Test]
    public void Apply_DifferentOpenId_Warns()
    {
        var details = new LEAFVerifiedDetails { OpenId = 1 };
        var result = new LeafPrintedIdResult("ABCDE-001", 2, true, KeyId, null);

        Assert.That(LeafPrintedIdLookup.Apply(details, result).PrintedIdNote, Does.Contain("WARNING"));
    }

    [Test]
    public void Apply_Failure_KeepsErrorAsNote()
    {
        var result = new LeafPrintedIdResult(null, null, false, null, "boom");

        var applied = LeafPrintedIdLookup.Apply(new LEAFVerifiedDetails(), result);

        Assert.That(applied.PrintedId, Is.Null);
        Assert.That(applied.PrintedIdNote, Is.EqualTo("boom"));
    }

    [Test]
    public void NdefUri_ParsesShortAndLongRecords()
    {
        byte[] shortRecord = [0xD1, 0x01, 0x04, (byte)'U', 0x04, (byte)'a', (byte)'.', (byte)'b'];
        byte[] longRecord = [0xC1, 0x01, 0x00, 0x00, 0x00, 0x04, (byte)'U', 0x03, (byte)'a', (byte)'.', (byte)'b'];
        byte[] textThenUri = [0x91, 0x01, 0x02, (byte)'T', 0x00, 0x00, 0x51, 0x01, 0x02, (byte)'U', 0x00, (byte)'x'];

        Assert.Multiple(() =>
        {
            Assert.That(NdefUri.FindUri(shortRecord), Is.EqualTo("https://a.b"));
            Assert.That(NdefUri.FindUri(longRecord), Is.EqualTo("http://a.b"));
            Assert.That(NdefUri.FindUri(textThenUri), Is.EqualTo("x"));
            Assert.That(NdefUri.FindUri([0xD1, 0x01, 0x40, (byte)'U', 0x04]), Is.Null, "Truncated");
            Assert.That(NdefUri.FindUri([]), Is.Null);
        });
    }
}
