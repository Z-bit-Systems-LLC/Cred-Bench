using System.Security.Cryptography;

namespace CredBench.Core.Services.GlobalPlatform;

public record InitializeUpdateResult
{
    public required byte[] HostChallenge { get; init; }
    public required byte[] KeyDiversification { get; init; }
    public required byte[] KeyInfo { get; init; }
    public required byte[] SequenceCounter { get; init; }
    public required byte[] CardChallenge { get; init; }
    public required byte[] CardCryptogram { get; init; }
    /// <summary>Raw INITIALIZE UPDATE response (without SW) for diagnostics.</summary>
    public byte[] RawResponse { get; init; } = [];
}

/// <summary>
/// GlobalPlatform card management — SCP02 channel establishment and card content management.
/// </summary>
public class GlobalPlatformService
{
    // Default ISD AID (GP 2.1.1)
    private static readonly byte[] IsdAid = [0xA0, 0x00, 0x00, 0x01, 0x51, 0x00, 0x00, 0x00];

    /// <summary>
    /// Establishes an SCP02 secure channel using the same key for ENC, MAC, and DEK.
    /// </summary>
    public Scp02Session EstablishSecureChannel(ICardConnection connection, byte[] staticKey)
    {
        return EstablishSecureChannel(connection, staticKey, staticKey, staticKey);
    }

    /// <summary>
    /// Establishes an SCP02 secure channel with separate ENC, MAC, and DEK keys.
    /// Performs SELECT ISD → INITIALIZE UPDATE → key derivation → cryptogram
    /// verification → EXTERNAL AUTHENTICATE.
    /// </summary>
    public Scp02Session EstablishSecureChannel(
        ICardConnection connection,
        byte[] staticEncKey, byte[] staticMacKey, byte[] staticDekKey)
    {
        SelectIsd(connection);

        var hostChallenge = RandomNumberGenerator.GetBytes(8);
        var initResponse = SendInitializeUpdate(connection, hostChallenge);

        // Response: key_diversification(10) | key_info(2) | seq_counter(2) | card_challenge(6) | card_cryptogram(8) = 28 bytes
        if (initResponse.Length < 28)
            throw new GlobalPlatformException(
                $"INITIALIZE UPDATE response too short: {initResponse.Length} bytes");

        var keyDiversification = initResponse[..10];
        var keyInfo = initResponse[10..12];
        var sequenceCounter = initResponse[12..14];
        var cardChallenge = initResponse[14..20];
        var cardCryptogram = initResponse[20..28];

        if (keyInfo[1] != 0x02)
            throw new GlobalPlatformException(
                $"Unsupported SCP version: {keyInfo[1]:X2} (expected SCP02). " +
                $"Key diversification: {ToHex(keyDiversification)}, Key info: {ToHex(keyInfo)}");

        var session = new Scp02Session();
        session.DeriveSessionKeys(staticEncKey, staticMacKey, staticDekKey, sequenceCounter);

        if (!session.VerifyCardCryptogram(hostChallenge, cardChallenge, cardCryptogram))
        {
            var computed = session.ComputeExpectedCardCryptogram(hostChallenge, cardChallenge);
            throw new GlobalPlatformException(
                $"Card cryptogram verification failed — wrong keys? " +
                $"Full response ({initResponse.Length} bytes): {ToHex(initResponse)}, " +
                $"Host challenge: {ToHex(hostChallenge)}, " +
                $"Key diversification: {ToHex(keyDiversification)}, " +
                $"Key version: {keyInfo[0]:X2}, SCP: {keyInfo[1]:X2}, " +
                $"Seq: {ToHex(sequenceCounter)}, " +
                $"Card challenge: {ToHex(cardChallenge)}, " +
                $"Card cryptogram (received): {ToHex(cardCryptogram)}, " +
                $"Card cryptogram (computed): {ToHex(computed)}, " +
                $"S-ENC: {ToHex(session.SessionEncKey)}, " +
                $"S-MAC: {ToHex(session.SessionMacKey)}, " +
                $"Static key: {ToHex(staticEncKey)}");
        }

        var hostCryptogram = session.ComputeHostCryptogram(cardChallenge, hostChallenge);
        SendExternalAuthenticate(connection, session, hostCryptogram);

        return session;
    }

    /// <summary>
    /// Performs SELECT ISD and INITIALIZE UPDATE, returning the parsed card response
    /// and host challenge. Key matching can then be done locally without further card I/O.
    /// </summary>
    public InitializeUpdateResult SendInitializeUpdatePhase(ICardConnection connection)
    {
        SelectIsd(connection);

        var hostChallenge = RandomNumberGenerator.GetBytes(8);
        var initResponse = SendInitializeUpdate(connection, hostChallenge);

        if (initResponse.Length < 28)
            throw new GlobalPlatformException(
                $"INITIALIZE UPDATE response too short: {initResponse.Length} bytes");

        var keyDiversification = initResponse[..10];
        var keyInfo = initResponse[10..12];

        if (keyInfo[1] != 0x02)
            throw new GlobalPlatformException(
                $"Unsupported SCP version: {keyInfo[1]:X2} (expected SCP02). " +
                $"Key diversification: {ToHex(keyDiversification)}, Key info: {ToHex(keyInfo)}");

        return new InitializeUpdateResult
        {
            HostChallenge = hostChallenge,
            KeyDiversification = keyDiversification,
            KeyInfo = keyInfo,
            SequenceCounter = initResponse[12..14],
            CardChallenge = initResponse[14..20],
            CardCryptogram = initResponse[20..28],
            RawResponse = initResponse
        };
    }

    /// <summary>
    /// Tries to derive session keys and verify the card cryptogram locally (no card I/O).
    /// Returns the session if the key matches, null otherwise.
    /// </summary>
    public Scp02Session? TryDeriveSession(InitializeUpdateResult initData, byte[] staticKey)
    {
        return TryDeriveSession(initData, staticKey, staticKey, staticKey);
    }

    /// <summary>
    /// Tries to derive session keys and verify the card cryptogram locally (no card I/O).
    /// Returns the session if the key matches, null otherwise.
    /// </summary>
    public Scp02Session? TryDeriveSession(
        InitializeUpdateResult initData,
        byte[] staticEncKey, byte[] staticMacKey, byte[] staticDekKey)
    {
        var session = new Scp02Session();
        session.DeriveSessionKeys(staticEncKey, staticMacKey, staticDekKey, initData.SequenceCounter);

        return session.VerifyCardCryptogram(
            initData.HostChallenge, initData.CardChallenge, initData.CardCryptogram)
            ? session
            : null;
    }

    /// <summary>
    /// Completes authentication by sending EXTERNAL AUTHENTICATE with an already-verified session.
    /// </summary>
    public void CompleteAuthentication(
        ICardConnection connection, Scp02Session session, InitializeUpdateResult initData)
    {
        var hostCryptogram = session.ComputeHostCryptogram(initData.CardChallenge, initData.HostChallenge);
        SendExternalAuthenticate(connection, session, hostCryptogram);
    }

    /// <summary>
    /// Queries installed card content using GET STATUS.
    /// Returns all packages and applets on the card.
    /// </summary>
    public CardContent GetCardContent(ICardConnection connection, Scp02Session session)
    {
        var packages = GetStatus(connection, session, CardContentType.ExecutableLoadFiles);
        var applets = GetStatus(connection, session, CardContentType.Applications);
        var memory = GetFreeMemory(connection, session);
        return new CardContent
        {
            Packages = packages,
            Applets = applets,
            FreePersistentMemory = memory.Persistent,
            FreeTransientResetMemory = memory.TransientReset,
            FreeTransientDeselectMemory = memory.TransientDeselect,
        };
    }

    /// <summary>
    /// Queries free memory via GET DATA with the JCOP/GP tag 0xFF21 (free NVM),
    /// 0xFF22 (free transient reset), 0xFF23 (free transient deselect).
    /// Returns null for each value if the card doesn't support the query.
    /// </summary>
    public (int? Persistent, int? TransientReset, int? TransientDeselect) GetFreeMemory(
        ICardConnection connection, Scp02Session session)
    {
        var persistent = TryGetDataValue(connection, session, 0xFF, 0x21);
        var transientReset = TryGetDataValue(connection, session, 0xFF, 0x22);
        var transientDeselect = TryGetDataValue(connection, session, 0xFF, 0x23);
        return (persistent, transientReset, transientDeselect);
    }

    private int? TryGetDataValue(ICardConnection connection, Scp02Session session, byte p1, byte p2)
    {
        // GET DATA: 80 CA [P1] [P2] 00
        var command = new byte[] { 0x80, 0xCA, p1, p2, 0x00 };
        var wrapped = session.WrapCommand(command);
        var response = connection.Transmit(wrapped);
        var (sw1, sw2) = GetStatusWords(response);

        if (sw1 != 0x90 || sw2 != 0x00)
            return null;

        // Response data is the bytes before SW, interpret as big-endian integer
        var data = response[..^2];
        if (data.Length == 0) return null;

        int value = 0;
        foreach (var b in data)
            value = (value << 8) | b;
        return value;
    }

    /// <summary>
    /// Sends GET STATUS for the given content type. Handles multi-response chaining
    /// (GET STATUS returns 0x6310 when more data is available).
    /// </summary>
    public List<CardContentEntry> GetStatus(
        ICardConnection connection, Scp02Session session, CardContentType contentType)
    {
        var entries = new List<CardContentEntry>();
        bool firstCommand = true;

        while (true)
        {
            // P1 = content type, P2 = 0x00 first request / 0x01 next occurrence
            byte p2 = firstCommand ? (byte)0x00 : (byte)0x01;
            // Filter: tag 4F length 00 = return all AIDs
            byte[] filter = [0x4F, 0x00];

            var command = BuildCommand(0x80, 0xF2, (byte)contentType, p2, filter);
            var wrapped = session.WrapCommand(command);
            var response = connection.Transmit(wrapped);
            var (sw1, sw2) = GetStatusWords(response);

            // 6A88 = no data found (empty card) — not an error
            if (sw1 == 0x6A && sw2 == 0x88)
                break;

            if (sw1 != 0x90 && sw1 != 0x63)
                CheckStatus(sw1, sw2, "GET STATUS");

            // Parse TLV entries from response data (everything before SW)
            ParseGetStatusResponse(response[..^2], entries);

            // 0x6310 means more data available
            if (sw1 == 0x63 && sw2 == 0x10)
            {
                firstCommand = false;
                continue;
            }

            break;
        }

        return entries;
    }

    private static void ParseGetStatusResponse(byte[] data, List<CardContentEntry> entries)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            // Each entry: [AID_length][AID][lifecycle][privileges]
            if (pos >= data.Length) break;
            int aidLen = data[pos++];
            if (pos + aidLen + 2 > data.Length) break;

            var aid = data[pos..(pos + aidLen)];
            pos += aidLen;
            byte lifecycle = data[pos++];
            byte privileges = data[pos++];

            entries.Add(new CardContentEntry
            {
                Aid = aid,
                LifecycleState = lifecycle,
                Privileges = privileges
            });
        }
    }

    /// <summary>
    /// Deletes an on-card entity (applet or package) by AID.
    /// Silently succeeds if the AID is not found (SW 6A88).
    /// </summary>
    public void Delete(ICardConnection connection, Scp02Session session,
        byte[] aid, bool deleteRelated = true)
    {
        byte p2 = deleteRelated ? (byte)0x80 : (byte)0x00;

        var data = new byte[2 + aid.Length];
        data[0] = 0x4F; // AID tag
        data[1] = (byte)aid.Length;
        Buffer.BlockCopy(aid, 0, data, 2, aid.Length);

        var command = BuildCommand(0x80, 0xE4, 0x00, p2, data);
        var wrapped = session.WrapCommand(command);
        var response = connection.Transmit(wrapped);
        var (sw1, sw2) = GetStatusWords(response);

        // 6A88 = referenced data not found — not an error for "delete if exists"
        if (sw1 == 0x6A && sw2 == 0x88) return;

        CheckStatus(sw1, sw2, "DELETE");
    }

    // ── Internal APDU helpers ──────────────────────────────────────────

    private void SelectIsd(ICardConnection connection)
    {
        var command = new byte[5 + IsdAid.Length];
        command[0] = 0x00; // CLA
        command[1] = 0xA4; // INS SELECT
        command[2] = 0x04; // P1 by name
        command[3] = 0x00; // P2
        command[4] = (byte)IsdAid.Length;
        Buffer.BlockCopy(IsdAid, 0, command, 5, IsdAid.Length);

        var response = connection.Transmit(command);
        var (sw1, sw2) = GetStatusWords(response);

        if (sw1 != 0x90 || sw2 != 0x00)
            throw new GlobalPlatformException(sw1, sw2);
    }

    private byte[] SendInitializeUpdate(ICardConnection connection, byte[] hostChallenge)
    {
        // 80 50 00 00 08 [host_challenge] 00
        var command = new byte[14];
        command[0] = 0x80;
        command[1] = 0x50; // INITIALIZE UPDATE
        command[2] = 0x00; // P1 key version
        command[3] = 0x00; // P2 key ID
        command[4] = 0x08; // Lc
        Buffer.BlockCopy(hostChallenge, 0, command, 5, 8);
        command[13] = 0x00; // Le

        var response = connection.Transmit(command);
        var (sw1, sw2) = GetStatusWords(response);
        CheckStatus(sw1, sw2, "INITIALIZE UPDATE");

        return response[..^2];
    }

    private void SendExternalAuthenticate(
        ICardConnection connection, Scp02Session session, byte[] hostCryptogram)
    {
        // Security level 0x01 = C-MAC on all subsequent commands
        var command = BuildCommand(0x80, 0x82, 0x01, 0x00, hostCryptogram);
        var wrapped = session.WrapCommand(command);

        var response = connection.Transmit(wrapped);
        var (sw1, sw2) = GetStatusWords(response);
        CheckStatus(sw1, sw2, "EXTERNAL AUTHENTICATE");

        // Enable ICV encryption for subsequent commands (SCP02 i=55)
        session.EnableIcvEncryption();
    }

    // ── Shared utilities (used by AppletLoader too) ────────────────────

    internal static byte[] BuildCommand(byte cla, byte ins, byte p1, byte p2, byte[] data)
    {
        var command = new byte[5 + data.Length];
        command[0] = cla;
        command[1] = ins;
        command[2] = p1;
        command[3] = p2;
        command[4] = (byte)data.Length;
        Buffer.BlockCopy(data, 0, command, 5, data.Length);
        return command;
    }

    internal static (byte Sw1, byte Sw2) GetStatusWords(byte[] response)
    {
        if (response.Length < 2)
            throw new GlobalPlatformException("Response too short for status words");
        return (response[^2], response[^1]);
    }

    internal static void CheckStatus(byte sw1, byte sw2, string commandName)
    {
        if (sw1 != 0x90 || sw2 != 0x00)
            throw new GlobalPlatformException(sw1, sw2);
    }

    private static string ToHex(byte[] data) =>
        BitConverter.ToString(data).Replace("-", "");
}
