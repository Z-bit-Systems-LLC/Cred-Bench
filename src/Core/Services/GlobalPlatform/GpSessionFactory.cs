namespace CredBench.Core.Services.GlobalPlatform;

/// <summary>
/// Opens an authenticated SCP02 session with a card's Issuer Security Domain, either with an
/// explicit key or by discovering one of the common default keys. Shared by the applet programmers.
/// </summary>
public class GpSessionFactory
{
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

    private readonly GlobalPlatformService _gpService;

    public GpSessionFactory() : this(new GlobalPlatformService()) { }

    public GpSessionFactory(GlobalPlatformService gpService)
    {
        _gpService = gpService;
    }

    /// <summary>
    /// Parses a 16-byte GP key typed as 32 hex characters (spaces and dashes allowed).
    /// </summary>
    public static byte[] ParseKey(string hex)
    {
        var cleaned = hex.Replace(" ", "").Replace("-", "");
        if (cleaned.Length != 32)
            throw new ArgumentException("GP key must be 16 bytes (32 hex characters).");
        return Convert.FromHexString(cleaned);
    }

    /// <summary>
    /// Authenticates with <paramref name="gpKey"/>, or discovers a default key when it is null.
    /// </summary>
    public Scp02Session Establish(ICardConnection connection, byte[]? gpKey, IProgress<string>? statusProgress = null)
    {
        return gpKey != null
            ? _gpService.EstablishSecureChannel(connection, gpKey)
            : EstablishWithKeyDiscovery(connection, statusProgress);
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
}
