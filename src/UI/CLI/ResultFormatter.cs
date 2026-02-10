using System.Text.Json;
using System.Text.Json.Serialization;
using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;

namespace CredBench.CLI;

/// <summary>
/// Formats <see cref="DetectionResult"/> for CLI output in text or JSON format.
/// </summary>
public static class ResultFormatter
{
    /// <summary>
    /// Writes the detection result as human-readable text with labeled sections.
    /// </summary>
    public static void PrintText(DetectionResult result, TextWriter writer)
    {
        if (result.GeneralDetails is { } general)
            WriteSection(writer, "General", general.GetFields());

        if (result.ISO14443Details is { } iso)
            WriteSection(writer, "ISO 14443", iso.GetFields());

        if (result.PIVDetails is { } piv)
            WriteSection(writer, "PIV", piv.GetFields());

        if (result.DESFireDetails is { } desfire)
            WriteSection(writer, "DESFire", desfire.GetFields());

        if (result.PKOCDetails is { } pkoc)
            WriteSection(writer, "PKOC", pkoc.GetFields());

        if (result.LEAFDetails is { } leaf)
            WriteSection(writer, "LEAF", leaf.GetFields());
    }

    /// <summary>
    /// Writes the detection result as JSON.
    /// </summary>
    public static void PrintJson(DetectionResult result, TextWriter writer)
    {
        var obj = new Dictionary<string, object>();

        if (result.GeneralDetails is { } general)
        {
            obj["general"] = new
            {
                reader = general.ReaderName,
                protocol = general.Protocol,
                atr = general.ATR,
                uid = general.UID,
                csn = general.CSN,
                cardType = general.CardTypeSummary
            };
        }

        if (result.ISO14443Details is { } iso)
        {
            obj["iso14443"] = new
            {
                uid = iso.UID,
                uidBytes = iso.UIDBytes,
                uidLength = iso.UIDLength,
                csn = iso.CSN,
                manufacturer = iso.Manufacturer,
                sak = iso.SAK?.ToString("X2"),
                cardType = iso.CardType,
                wiegandBits = iso.CSNWiegandBits
            };
        }

        if (result.PIVDetails is { } piv)
        {
            obj["piv"] = new
            {
                status = piv.Status,
                chuid = piv.CHUID,
                fascn = piv.FASCN,
                agencyCode = piv.AgencyCode,
                systemCode = piv.SystemCode,
                credentialNumber = piv.CredentialNumber,
                credentialSeries = piv.CredentialSeries,
                individualCredentialIssue = piv.IndividualCredentialIssue,
                personIdentifier = piv.PersonIdentifier,
                organizationalCategory = piv.OrganizationalCategory,
                organizationalIdentifier = piv.OrganizationalIdentifier,
                personOrgAssociation = piv.PersonOrgAssociation,
                wiegandBits = piv.FASCNWiegandBits
            };
        }

        if (result.DESFireDetails is { } desfire)
        {
            obj["desfire"] = new
            {
                cardType = desfire.CardType,
                hardwareVersion = desfire.HardwareVersion,
                softwareVersion = desfire.SoftwareVersion,
                storageSize = desfire.StorageSize,
                applications = desfire.Applications
            };
        }

        if (result.PKOCDetails is { } pkoc)
        {
            obj["pkoc"] = new
            {
                protocolVersion = pkoc.ProtocolVersion,
                publicKey = pkoc.PublicKeyHex,
                credential256 = pkoc.Credential256Hex,
                credential75 = pkoc.Credential75Hex,
                credential64 = pkoc.Credential64Hex,
                wiegand256 = pkoc.Credential256Bits,
                wiegand75 = pkoc.Credential75Bits,
                wiegand64 = pkoc.Credential64Bits
            };
        }

        if (result.LEAFDetails is { } leaf)
        {
            obj["leaf"] = new
            {
                applicationType = leaf.ApplicationType,
                aids = leaf.DetectedAIDs
            };
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        writer.WriteLine(JsonSerializer.Serialize(obj, options));
    }

    private static void WriteSection(TextWriter writer, string header, IReadOnlyList<(string Label, string Value)> fields)
    {
        writer.WriteLine($"── {header} ──");
        foreach (var (label, value) in fields)
            writer.WriteLine($"  {label,-20} {value}");
        writer.WriteLine();
    }
}
