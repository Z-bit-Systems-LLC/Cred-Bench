using System.Diagnostics;
using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;

namespace CredBench.Core.Services.CardDetectors;

/// <summary>
/// Detects LEAF Universal (LEAF 4.0) credentials on MIFARE DESFire EV2/3 cards.
/// LEAF cards contain three DESFire applications:
/// - F51CD8: UNIVERSAL ID Application
/// - F51CD9: UNIVERSAL ID Application (secondary)
/// - F51CDB: ENTERPRISE ID Application
/// </summary>
public class LEAFDetector : ICardDetector
{
    public CardTechnology Technology => CardTechnology.LEAF;

    // LEAF DESFire Application IDs (3 bytes each, big-endian for ISO SELECT)
    private static readonly byte[] UniversalIdAid1 = [0xF5, 0x1C, 0xD8]; // F51CD8
    private static readonly byte[] UniversalIdAid2 = [0xF5, 0x1C, 0xD9]; // F51CD9
    private static readonly byte[] EnterpriseIdAid = [0xF5, 0x1C, 0xDB]; // F51CDB

    public (bool Detected, string? Details, object? TypedDetails) Detect(ICardConnection connection)
    {
        Debug.WriteLine("=== LEAF Detection Started ===");

        var detectedAids = new List<string>();
        string? applicationType = null;

        // Try to select each LEAF application using ISO SELECT
        // LEAF cards use ISO 7816-4 SELECT command format, not native DESFire

        // Try UNIVERSAL ID Application 1 (F51CD8)
        if (TrySelectLeafApplication(connection, UniversalIdAid1, "F51CD8"))
        {
            Debug.WriteLine("[LEAF] UNIVERSAL ID (F51CD8) detected");
            detectedAids.Add("F51CD8 (UNIVERSAL ID)");
            applicationType ??= "UNIVERSAL ID";
        }

        // Try UNIVERSAL ID Application 2 (F51CD9)
        if (TrySelectLeafApplication(connection, UniversalIdAid2, "F51CD9"))
        {
            Debug.WriteLine("[LEAF] UNIVERSAL ID (F51CD9) detected");
            detectedAids.Add("F51CD9 (UNIVERSAL ID)");
            applicationType ??= "UNIVERSAL ID";
        }

        // Try ENTERPRISE ID Application (F51CDB)
        if (TrySelectLeafApplication(connection, EnterpriseIdAid, "F51CDB"))
        {
            Debug.WriteLine("[LEAF] ENTERPRISE ID (F51CDB) detected");
            detectedAids.Add("F51CDB (ENTERPRISE ID)");
            applicationType ??= "ENTERPRISE ID";
        }

        if (detectedAids.Count > 0)
        {
            var details = new LEAFDetails
            {
                ApplicationType = applicationType ?? "Unknown",
                DetectedAIDs = detectedAids
            };
            return (true, $"LEAF Universal credential ({applicationType})", details);
        }

        Debug.WriteLine("[LEAF] No LEAF applications found");
        return (false, null, null);
    }

    private static bool TrySelectLeafApplication(ICardConnection connection, byte[] aid, string aidName)
    {
        try
        {
            // ISO 7816-4 SELECT command: 00 A4 04 00 [Lc] [AID] 00
            byte[] selectCommand = [0x00, 0xA4, 0x04, 0x00, 0x03, aid[0], aid[1], aid[2], 0x00];

            Debug.WriteLine($"[LEAF] SELECT {aidName}: {BitConverter.ToString(selectCommand)}");
            var response = connection.Transmit(selectCommand);
            Debug.WriteLine($"[LEAF] Response: {BitConverter.ToString(response)}");

            if (response.Length >= 2)
            {
                var sw1 = response[^2];
                var sw2 = response[^1];

                // ISO success: 90 00
                if (sw1 == 0x90 && sw2 == 0x00)
                {
                    Debug.WriteLine($"[LEAF] SELECT {aidName} succeeded (SW=9000)");
                    return true;
                }

                // More data available: 61 xx
                if (sw1 == 0x61)
                {
                    Debug.WriteLine($"[LEAF] SELECT {aidName} succeeded (SW=61{sw2:X2})");
                    return true;
                }

                Debug.WriteLine($"[LEAF] SELECT {aidName} failed (SW={sw1:X2}{sw2:X2})");
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LEAF] SELECT {aidName} exception: {ex.Message}");
            return false;
        }
    }
}
