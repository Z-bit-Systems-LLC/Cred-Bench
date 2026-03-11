namespace CredBench.Core.Services.GlobalPlatform;

/// <summary>
/// Loads a compiled JavaCard applet (.cap) onto a card via an established SCP02 session.
/// Sequence: INSTALL [for load] → LOAD (chunked) → INSTALL [for install and make selectable].
/// </summary>
public class AppletLoader
{
    private const int MaxBlockSize = 240;

    /// <summary>
    /// Loads a CAP file and installs the applet in a single operation.
    /// </summary>
    public void LoadAndInstall(
        ICardConnection connection,
        Scp02Session session,
        byte[] capFileData,
        byte[] packageAid,
        byte[] appletAid,
        byte[] instanceAid,
        IProgress<(int Current, int Total)>? progress = null)
    {
        SendInstallForLoad(connection, session, packageAid);
        SendLoadBlocks(connection, session, capFileData, progress);
        SendInstallForInstall(connection, session, packageAid, appletAid, instanceAid);
    }

    private void SendInstallForLoad(
        ICardConnection connection, Scp02Session session, byte[] packageAid)
    {
        // [pkg AID len][pkg AID][SD AID len=00][hash len=00][params len=00][token len=00]
        var data = new byte[1 + packageAid.Length + 4];
        int pos = 0;
        data[pos++] = (byte)packageAid.Length;
        Buffer.BlockCopy(packageAid, 0, data, pos, packageAid.Length);
        pos += packageAid.Length;
        data[pos++] = 0x00; // SD AID (use card's ISD)
        data[pos++] = 0x00; // Hash
        data[pos++] = 0x00; // Parameters
        data[pos] = 0x00;   // Token

        var command = GlobalPlatformService.BuildCommand(0x80, 0xE6, 0x02, 0x00, data);
        var wrapped = session.WrapCommand(command);

        var response = connection.Transmit(wrapped);
        var (sw1, sw2) = GlobalPlatformService.GetStatusWords(response);
        GlobalPlatformService.CheckStatus(sw1, sw2, "INSTALL [for load]");
    }

    private void SendLoadBlocks(
        ICardConnection connection,
        Scp02Session session,
        byte[] capFileData,
        IProgress<(int Current, int Total)>? progress)
    {
        var ijcData = CapFileConverter.ToIjc(capFileData);
        var loadFileData = WrapWithC4Tag(ijcData);
        int totalBlocks = (loadFileData.Length + MaxBlockSize - 1) / MaxBlockSize;
        int blockNumber = 0;
        int offset = 0;

        while (offset < loadFileData.Length)
        {
            int blockSize = Math.Min(loadFileData.Length - offset, MaxBlockSize);
            bool isLast = offset + blockSize >= loadFileData.Length;
            byte p1 = isLast ? (byte)0x80 : (byte)0x00;

            var blockData = loadFileData[offset..(offset + blockSize)];
            var command = GlobalPlatformService.BuildCommand(
                0x80, 0xE8, p1, (byte)blockNumber, blockData);
            var wrapped = session.WrapCommand(command);

            var response = connection.Transmit(wrapped);
            var (sw1, sw2) = GlobalPlatformService.GetStatusWords(response);
            GlobalPlatformService.CheckStatus(sw1, sw2, $"LOAD block {blockNumber}");

            blockNumber++;
            offset += blockSize;
            progress?.Report((blockNumber, totalBlocks));
        }
    }

    private void SendInstallForInstall(
        ICardConnection connection,
        Scp02Session session,
        byte[] packageAid,
        byte[] appletAid,
        byte[] instanceAid)
    {
        // [pkg AID len][pkg AID][module AID len][module AID][instance AID len][instance AID]
        // [privileges len][privileges][params len][C9 00][token len]
        var data = new byte[
            1 + packageAid.Length +
            1 + appletAid.Length +
            1 + instanceAid.Length +
            1 + 1 +  // privileges
            1 + 2 +  // install params (C9 00)
            1         // token
        ];

        int pos = 0;
        data[pos++] = (byte)packageAid.Length;
        Buffer.BlockCopy(packageAid, 0, data, pos, packageAid.Length);
        pos += packageAid.Length;

        data[pos++] = (byte)appletAid.Length;
        Buffer.BlockCopy(appletAid, 0, data, pos, appletAid.Length);
        pos += appletAid.Length;

        data[pos++] = (byte)instanceAid.Length;
        Buffer.BlockCopy(instanceAid, 0, data, pos, instanceAid.Length);
        pos += instanceAid.Length;

        data[pos++] = 0x01; // Privileges length
        data[pos++] = 0x00; // No special privileges

        data[pos++] = 0x02; // Install parameters field length
        data[pos++] = 0xC9; // Application-specific parameters tag
        data[pos++] = 0x00; // Empty

        data[pos] = 0x00;   // Token length

        var command = GlobalPlatformService.BuildCommand(0x80, 0xE6, 0x0C, 0x00, data);
        var wrapped = session.WrapCommand(command);

        var response = connection.Transmit(wrapped);
        var (sw1, sw2) = GlobalPlatformService.GetStatusWords(response);
        GlobalPlatformService.CheckStatus(sw1, sw2, "INSTALL [for install]");
    }

    /// <summary>
    /// Wraps raw CAP data with a BER-TLV C4 tag for the LOAD command.
    /// </summary>
    internal static byte[] WrapWithC4Tag(byte[] data)
    {
        byte[] header;
        if (data.Length < 128)
            header = [0xC4, (byte)data.Length];
        else if (data.Length < 256)
            header = [0xC4, 0x81, (byte)data.Length];
        else
            header = [0xC4, 0x82, (byte)(data.Length >> 8), (byte)(data.Length & 0xFF)];

        var result = new byte[header.Length + data.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(data, 0, result, header.Length, data.Length);
        return result;
    }
}
