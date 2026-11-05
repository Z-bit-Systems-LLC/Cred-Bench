using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.Services.CardDetectors;
using CredBench.Core.Services.GlobalPlatform;

namespace CredBench.Core.Services.Leaf;

/// <summary>
/// The outcome of programming a LEAF Verified test card.
/// </summary>
/// <param name="OpenId">The Open ID written into the card certificate.</param>
/// <param name="CardPublicKey">The key the card generated on-card (04 || X || Y).</param>
/// <param name="Certificate">The card certificate (DER) issued by the test root.</param>
/// <param name="ReadBack">The card read back over the same connection with the test root trusted.</param>
/// <param name="ImplicitSelectionParameterAccepted">
/// False when the card rejected the contactless implicit-selection install parameter and the applet was
/// installed with Card Reset alone; a reader that never sends SELECT may then not reach it.
/// </param>
public record LeafProgrammingResult(
    long OpenId,
    byte[] CardPublicKey,
    byte[] Certificate,
    LEAFVerifiedDetails ReadBack,
    bool ImplicitSelectionParameterAccepted);

/// <summary>
/// Programs a blank JavaCard as a LEAF Verified test card: installs the LeafOpenApplet emulation as the
/// default-selected applet, has a test root certify the key it generates, writes that certificate and
/// locks the card. Only deletes its own AIDs, never PKOC's.
/// </summary>
public class LeafCardProgrammer
{
    /// <summary>Package AID F05A424C454146 ("ZBLEAF").</summary>
    public static readonly byte[] PackageAid = [0xF0, 0x5A, 0x42, 0x4C, 0x45, 0x41, 0x46];

    /// <summary>Applet and instance AID F05A424C45414601.</summary>
    public static readonly byte[] AppletAid = [0xF0, 0x5A, 0x42, 0x4C, 0x45, 0x41, 0x46, 0x01];

    private const byte ClaPerso = 0x80;
    private const byte InsGetPublicKey = 0x10;
    private const byte InsPutCertificate = 0x12;
    private const byte InsLock = 0x14;
    private const int CertificateChunk = 128;
    private const int MaxCertificateLength = 1024;

    private readonly GlobalPlatformService _gpService = new();
    private readonly GpSessionFactory _sessionFactory;
    private readonly AppletLoader _appletLoader = new();

    public LeafCardProgrammer()
    {
        _sessionFactory = new GpSessionFactory(_gpService);
    }

    /// <summary>
    /// Installs and personalises the LEAF applet, then reads the card back with the test root trusted.
    /// </summary>
    public LeafProgrammingResult ProgramCard(
        ICardConnection connection,
        byte[] capFileData,
        LeafTestCa testCa,
        long openId,
        byte[]? gpKey = null,
        IProgress<(int Current, int Total)>? progress = null,
        IProgress<string>? statusProgress = null)
    {
        if (openId is < 0 or > LEAFVerifiedDetails.OpenIdMax)
            throw new ArgumentOutOfRangeException(nameof(openId), $"Open ID must be 0 to {LEAFVerifiedDetails.OpenIdMax}.");

        var session = _sessionFactory.Establish(connection, gpKey, statusProgress);

        statusProgress?.Report("Deleting existing LEAF applet...");
        _gpService.Delete(connection, session, AppletAid);
        _gpService.Delete(connection, session, PackageAid);

        statusProgress?.Report("Loading applet...");
        _appletLoader.Load(connection, session, capFileData, PackageAid, progress);

        statusProgress?.Report("Installing as the default-selected applet...");
        var implicitSelectionAccepted = InstallAsDefault(connection, session);

        statusProgress?.Report("Personalising...");
        var (cardKey, certificate) = Personalise(connection, testCa, openId);

        statusProgress?.Report("Reading the card back...");
        var readBack = ReadBack(connection, testCa);

        return new LeafProgrammingResult(openId, cardKey, certificate, readBack, implicitSelectionAccepted);
    }

    /// <summary>
    /// INSTALL with Card Reset and the contactless implicit-selection parameter. A card that does not
    /// implement GlobalPlatform Amendment C rejects the parameter with 6A80; retry with Card Reset alone.
    /// </summary>
    private bool InstallAsDefault(ICardConnection connection, Scp02Session session)
    {
        try
        {
            _appletLoader.Install(connection, session, PackageAid, AppletAid, AppletAid,
                AppletLoader.PrivilegeCardReset, AppletLoader.ImplicitSelectionAllInterfaces);
            return true;
        }
        catch (GlobalPlatformException ex) when (ex.StatusWord == 0x6A80)
        {
            try
            {
                _appletLoader.Install(connection, session, PackageAid, AppletAid, AppletAid,
                    AppletLoader.PrivilegeCardReset);
                return false;
            }
            catch (GlobalPlatformException retry) when (retry.StatusWord == 0x6A80)
            {
                throw new GlobalPlatformException(
                    "The card refused to make the LEAF applet the default-selected applet (6A80). " +
                    "Another applet may hold that role and need deleting first.", retry);
            }
        }
    }

    /// <summary>
    /// SELECT the applet by AID, read the key it generated, write the certificate the test root issues
    /// for it, and LOCK. Ends the SCP02 session (the ISD is no longer selected).
    /// </summary>
    internal static (byte[] CardKey, byte[] Certificate) Personalise(ICardConnection connection, LeafTestCa testCa, long openId)
    {
        byte[] select = [0x00, 0xA4, 0x04, 0x00, (byte)AppletAid.Length, .. AppletAid, 0x00];
        Expect(connection.Transmit(select), "SELECT LEAF applet");

        var response = connection.Transmit([ClaPerso, InsGetPublicKey, 0x00, 0x00, 0x00]);
        Expect(response, "GET PUBLIC KEY");
        if (response.Length != 65 + 2)
            throw new GlobalPlatformException($"GET PUBLIC KEY returned {response.Length - 2} bytes, expected 65.");
        var cardKey = response[..65];

        var certificate = testCa.IssueCardCertificate(cardKey, openId);
        if (certificate.Length > MaxCertificateLength)
            throw new GlobalPlatformException($"The certificate is {certificate.Length} bytes; the applet holds {MaxCertificateLength}.");

        for (var offset = 0; offset < certificate.Length; offset += CertificateChunk)
        {
            var chunk = certificate.AsSpan(offset, Math.Min(CertificateChunk, certificate.Length - offset));
            byte[] put = [ClaPerso, InsPutCertificate, (byte)(offset >> 8), (byte)offset, (byte)chunk.Length, .. chunk];
            Expect(connection.Transmit(put), $"PUT CERTIFICATE at {offset}");
        }

        byte[] lockCommand = [ClaPerso, InsLock, 0x00, 0x00, 0x02, (byte)(certificate.Length >> 8), (byte)certificate.Length];
        Expect(connection.Transmit(lockCommand), "LOCK");

        return (cardKey, certificate);
    }

    /// <summary>
    /// Runs the LEAF Verified detector on the same connection with only the test root trusted.
    /// The applet is still selected, so this checks the certificate and the card signature, not
    /// implicit selection (that needs a fresh tap).
    /// </summary>
    internal static LEAFVerifiedDetails ReadBack(ICardConnection connection, LeafTestCa testCa)
    {
        var anchor = LeafTrustStore.ParseRootCertificate(testCa.Certificate.RawData);
        var (_, _, typed) = new LEAFVerifiedDetector(new SingleAnchorStore(anchor)).Detect(connection);
        return typed as LEAFVerifiedDetails
            ?? new LEAFVerifiedDetails { Error = "The card did not answer as LEAF Verified after programming." };
    }

    private static void Expect(byte[] response, string step)
    {
        if (response.Length < 2)
            throw new GlobalPlatformException($"{step}: no status word.");

        var sw = (ushort)((response[^2] << 8) | response[^1]);
        if (sw == 0x9000)
            return;

        var meaning = sw switch
        {
            0x6985 => "the card is already personalised and locked; it can only be reprogrammed after a reinstall",
            0x6984 => "the card rejected the certificate (length mismatch, or not this card's key)",
            0x6A84 => "the certificate is too large for the applet",
            0x6A82 => "the LEAF applet is not installed",
            _ => "unexpected status"
        };
        throw new GlobalPlatformException($"{step} failed: SW {sw:X4} ({meaning}).");
    }

    private sealed class SingleAnchorStore(LeafTrustAnchor anchor) : ILeafTrustStore
    {
        public IReadOnlyList<LeafTrustAnchor> Anchors { get; } = [anchor];
    }
}
