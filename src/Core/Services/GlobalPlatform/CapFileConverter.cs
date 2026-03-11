using System.IO.Compression;

namespace CredBench.Core.Services.GlobalPlatform;

/// <summary>
/// Converts a JavaCard CAP file (ZIP archive) to IJC (Install JavaCard) format
/// by extracting and concatenating component files in the order required by
/// GlobalPlatform LOAD.
/// </summary>
internal static class CapFileConverter
{
    /// <summary>
    /// CAP component load order per JavaCard VM specification.
    /// </summary>
    private static readonly string[] ComponentOrder =
    [
        "Header",
        "Directory",
        "Applet",
        "Import",
        "ConstantPool",
        "Class",
        "Method",
        "StaticField",
        "RefLocation",
        "Descriptor",
    ];

    /// <summary>
    /// Converts a .cap (ZIP) file to IJC format. If the data is already in IJC format
    /// (not a valid ZIP), it is returned as-is.
    /// </summary>
    public static byte[] ToIjc(byte[] capFileData)
    {
        // Check for ZIP magic bytes (PK\x03\x04)
        if (capFileData.Length < 4 ||
            capFileData[0] != 0x50 || capFileData[1] != 0x4B ||
            capFileData[2] != 0x03 || capFileData[3] != 0x04)
        {
            // Not a ZIP — assume already IJC format
            return capFileData;
        }

        using var stream = new MemoryStream(capFileData);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        // Index entries by component name (case-insensitive)
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            // Entries are like "com/zbitsystems/pkoc/javacard/Header.cap"
            var fileName = Path.GetFileNameWithoutExtension(entry.Name);
            if (entry.Name.EndsWith(".cap", StringComparison.OrdinalIgnoreCase))
                entries.TryAdd(fileName, entry);
        }

        using var output = new MemoryStream();
        foreach (var component in ComponentOrder)
        {
            if (!entries.TryGetValue(component, out var entry))
                continue;

            using var entryStream = entry.Open();
            entryStream.CopyTo(output);
        }

        if (output.Length == 0)
            throw new InvalidOperationException(
                "CAP file contains no recognized JavaCard components.");

        return output.ToArray();
    }
}
