using CredBench.Core.Services.GlobalPlatform;

namespace CredBench.Core.Services.Pkoc;

/// <summary>
/// Orchestrates PKOC applet deployment: SCP02 channel → delete existing → load CAP → install.
/// </summary>
public class PkocCardProgrammer
{
    // Package AID from build.xml: A000000898000002
    private static readonly byte[] PkocPackageAid =
        [0xA0, 0x00, 0x00, 0x08, 0x98, 0x00, 0x00, 0x02];

    // Applet AID (also used as instance AID): A000000898000001
    private static readonly byte[] PkocAppletAid =
        [0xA0, 0x00, 0x00, 0x08, 0x98, 0x00, 0x00, 0x01];

    // SELECT PKOC command for state detection
    private static readonly byte[] SelectPkocCommand =
    [
        0x00, 0xA4, 0x04, 0x00,
        0x08,
        0xA0, 0x00, 0x00, 0x08, 0x98, 0x00, 0x00, 0x01,
        0x00
    ];

    /// <summary>
    /// Default GlobalPlatform keys used by most blank JavaCards.
    /// </summary>
    public static readonly byte[] DefaultGpKey =
    [
        0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47,
        0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F
    ];

    /// <summary>
    /// Common default GP keys to try when the caller doesn't specify a key.
    /// Tried in order — first successful authentication wins.
    /// </summary>
    public static readonly byte[][] CommonDefaultKeys =
    [
        // GP standard default
        [0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47,
         0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F],
    ];

    private readonly GlobalPlatformService _gpService = new();
    private readonly AppletLoader _appletLoader = new();

    /// <summary>
    /// Programs the PKOC applet onto a blank JavaCard.
    /// Establishes SCP02, removes any existing PKOC applet, loads the CAP, and installs.
    /// When no explicit GP key is provided, tries common default keys in sequence.
    /// </summary>
    public void ProgramCard(
        ICardConnection connection,
        byte[] capFileData,
        byte[]? gpKey = null,
        bool deleteExisting = true,
        IProgress<(int Current, int Total)>? progress = null,
        IProgress<string>? statusProgress = null)
    {
        Scp02Session session;

        if (gpKey != null)
        {
            session = _gpService.EstablishSecureChannel(connection, gpKey);
        }
        else
        {
            session = EstablishWithKeyDiscovery(connection, statusProgress);
        }

        statusProgress?.Report("Deleting existing applet...");
        if (deleteExisting)
        {
            _gpService.Delete(connection, session, PkocAppletAid);
            _gpService.Delete(connection, session, PkocPackageAid);
        }

        statusProgress?.Report("Loading applet...");
        _appletLoader.LoadAndInstall(
            connection, session, capFileData,
            PkocPackageAid, PkocAppletAid, PkocAppletAid,
            progress);
    }

    /// <summary>
    /// Performs INITIALIZE UPDATE once, then tries common default keys locally (no extra card I/O).
    /// Completes authentication with the first matching key.
    /// </summary>
    private Scp02Session EstablishWithKeyDiscovery(
        ICardConnection connection, IProgress<string>? statusProgress)
    {
        statusProgress?.Report("Connecting to card...");
        var initData = _gpService.SendInitializeUpdatePhase(connection);

        var keyNames = new[]
        {
            "GP standard (404142...4F)",
        };

        // Phase 1: Try static keys directly
        statusProgress?.Report("Trying common default keys...");

        for (var i = 0; i < CommonDefaultKeys.Length; i++)
        {
            var session = _gpService.TryDeriveSession(initData, CommonDefaultKeys[i]);
            if (session == null)
                continue;

            var keyName = i < keyNames.Length ? keyNames[i] : $"Key #{i + 1}";
            statusProgress?.Report($"Matched key: {keyName}. Authenticating...");
            _gpService.CompleteAuthentication(connection, session, initData);
            return session;
        }

        // Phase 2: Try key diversification (VISA2 and EMV KDFs) with GP default master key
        var masterKeys = new (byte[] Key, string Name, bool UseVisa2)[]
        {
            (CommonDefaultKeys[0], "GP default + VISA2", true),
            (CommonDefaultKeys[0], "GP default + EMV", false),
        };

        statusProgress?.Report("Trying key diversification...");

        var kdd = initData.KeyDiversification;
        for (var i = 0; i < masterKeys.Length; i++)
        {
            var (masterKey, name, useVisa2) = masterKeys[i];

            var (enc, mac, dek) = useVisa2
                ? KeyDiversification.Visa2(masterKey, kdd)
                : KeyDiversification.Emv(masterKey, kdd);

            var session = _gpService.TryDeriveSession(initData, enc, mac, dek);
            if (session == null)
                continue;

            statusProgress?.Report($"Matched: {name}. Authenticating...");
            _gpService.CompleteAuthentication(connection, session, initData);
            return session;
        }

        // Compute diagnostic values for debugging
        var toHex = (byte[] b) => BitConverter.ToString(b).Replace("-", "");
        var diagSession = new Scp02Session();
        diagSession.DeriveSessionKeys(CommonDefaultKeys[0], CommonDefaultKeys[0], CommonDefaultKeys[0],
            initData.SequenceCounter);
        var diagComputed = diagSession.ComputeExpectedCardCryptogram(
            initData.HostChallenge, initData.CardChallenge);

        var details =
            $"Raw INIT UPDATE ({initData.RawResponse.Length}B): {toHex(initData.RawResponse)}, " +
            $"Host challenge: {toHex(initData.HostChallenge)}, " +
            $"Seq: {toHex(initData.SequenceCounter)}, " +
            $"Card challenge: {toHex(initData.CardChallenge)}, " +
            $"Received cryptogram: {toHex(initData.CardCryptogram)}, " +
            $"Computed (GP default undiv): {toHex(diagComputed)}, " +
            $"S-ENC: {toHex(diagSession.SessionEncKey)}";

        throw new GlobalPlatformException(
            "Key discovery failed — the card does not use a known default key. " +
            "Try entering the correct key under Custom GP Key.",
            details);
    }

    /// <summary>
    /// Checks whether the PKOC applet is already installed by attempting SELECT.
    /// Does not require an SCP02 session.
    /// </summary>
    public bool IsAppletInstalled(ICardConnection connection)
    {
        var response = connection.Transmit(SelectPkocCommand);
        return response.Length >= 2 && response[^2] == 0x90 && response[^1] == 0x00;
    }

    /// <summary>
    /// Establishes SCP02 and queries card content (installed applets, packages, free memory).
    /// </summary>
    public CardContent QueryCardContent(
        ICardConnection connection,
        byte[]? gpKey = null,
        IProgress<string>? statusProgress = null)
    {
        Scp02Session session;

        if (gpKey != null)
        {
            statusProgress?.Report("Authenticating...");
            session = _gpService.EstablishSecureChannel(connection, gpKey);
        }
        else
        {
            session = EstablishWithKeyDiscovery(connection, statusProgress);
        }

        statusProgress?.Report("Reading card content...");
        return _gpService.GetCardContent(connection, session);
    }
}
