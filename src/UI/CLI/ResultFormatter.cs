using System.Text.Json;
using System.Text.Json.Serialization;
using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;

namespace CredBench.CLI;

public static class ResultFormatter
{
    public static void PrintText(DetectionResult result, TextWriter writer)
    {
        // General
        if (result.GeneralDetails is { } general)
        {
            writer.WriteLine("── General ──");
            WriteField(writer, "Reader", general.ReaderName);
            WriteField(writer, "Protocol", general.Protocol);
            WriteField(writer, "ATR", general.ATR);
            WriteField(writer, "UID", general.UID);
            WriteField(writer, "CSN", general.CSN);
            WriteField(writer, "Card Type", general.CardTypeSummary);
            writer.WriteLine();
        }

        // ISO14443
        if (result.ISO14443Details is { } iso)
        {
            writer.WriteLine("── ISO 14443 ──");
            WriteField(writer, "UID", iso.UID);
            WriteField(writer, "UID Bytes", iso.UIDBytes);
            WriteField(writer, "UID Length", iso.UIDLength);
            WriteField(writer, "CSN", iso.CSN);
            WriteField(writer, "Manufacturer", iso.Manufacturer);
            WriteField(writer, "SAK", iso.SAK?.ToString("X2"));
            WriteField(writer, "Card Type", iso.CardType);
            if (iso.CSNWiegandBits is { } wiegand)
                WriteField(writer, iso.WiegandBitsLabel, wiegand);
            writer.WriteLine();
        }

        // PIV
        if (result.PIVDetails is { } piv)
        {
            writer.WriteLine("── PIV ──");
            WriteField(writer, "Status", piv.Status);
            WriteField(writer, "CHUID", piv.CHUID);
            WriteField(writer, "FASC-N", piv.FASCN);
            WriteField(writer, "Agency Code", piv.AgencyCode);
            WriteField(writer, "System Code", piv.SystemCode);
            WriteField(writer, "Credential #", piv.CredentialNumber);
            WriteField(writer, "Credential Series", piv.CredentialSeries);
            WriteField(writer, "ICI", piv.IndividualCredentialIssue);
            WriteField(writer, "Person ID", piv.PersonIdentifier);
            WriteField(writer, "Org Category", piv.OrganizationalCategory);
            WriteField(writer, "Org ID", piv.OrganizationalIdentifier);
            WriteField(writer, "Person/Org Assoc", piv.PersonOrgAssociation);
            if (piv.FASCNWiegandBits is { } wiegand)
                WriteField(writer, piv.WiegandBitsLabel, wiegand);
            writer.WriteLine();
        }

        // DESFire
        if (result.DESFireDetails is { } desfire)
        {
            writer.WriteLine("── DESFire ──");
            WriteField(writer, "Card Type", desfire.CardType);
            WriteField(writer, "Hardware Version", desfire.HardwareVersion);
            WriteField(writer, "Software Version", desfire.SoftwareVersion);
            WriteField(writer, "Storage Size", desfire.StorageSize);
            if (desfire.Applications.Count > 0)
            {
                WriteField(writer, "Applications", string.Join(", ", desfire.Applications));
            }
            writer.WriteLine();
        }

        // PKOC
        if (result.PKOCDetails is { } pkoc)
        {
            writer.WriteLine("── PKOC ──");
            WriteField(writer, "Protocol Version", pkoc.ProtocolVersion);
            WriteField(writer, "Public Key", pkoc.PublicKeyHex);
            WriteField(writer, "Credential 256-bit", pkoc.Credential256Hex);
            WriteField(writer, "Credential 75-bit", pkoc.Credential75Hex);
            WriteField(writer, "Credential 64-bit", pkoc.Credential64Hex);
            if (pkoc.Credential256Bits is { } bits256)
                WriteField(writer, "256-bit Wiegand", bits256);
            if (pkoc.Credential75Bits is { } bits75)
                WriteField(writer, "75-bit Wiegand", bits75);
            if (pkoc.Credential64Bits is { } bits64)
                WriteField(writer, "64-bit Wiegand", bits64);
            writer.WriteLine();
        }

        // LEAF
        if (result.LEAFDetails is { } leaf)
        {
            writer.WriteLine("── LEAF ──");
            WriteField(writer, "Application Type", leaf.ApplicationType);
            if (leaf.DetectedAIDs.Count > 0)
            {
                WriteField(writer, "AIDs", string.Join(", ", leaf.DetectedAIDs));
            }
            writer.WriteLine();
        }
    }

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

    private static void WriteField(TextWriter writer, string label, string? value)
    {
        if (value is not null)
            writer.WriteLine($"  {label,-20} {value}");
    }
}
