using CredBench.CLI;
using CredBench.Core.Models;
using CredBench.Core.Services;
using CredBench.Core.Services.CardDetectors;
using CredBench.Core.Services.Leaf;

var json = false;
string? readerName = null;
var list = false;
var leafTrustStore = new LeafTrustStore();
var leafLookup = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--help" or "-h":
            PrintUsage();
            return 0;
        case "--list" or "-l":
            list = true;
            break;
        case "--json" or "-j":
            json = true;
            break;
        case "--reader" or "-r":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Error: --reader requires a value");
                return 1;
            }
            readerName = args[++i];
            break;
        case "--leaf-lookup":
            leafLookup = true;
            break;
        case "--leaf-anchor":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Error: --leaf-anchor requires a certificate file");
                return 1;
            }
            try
            {
                var anchorFile = args[++i];
                var anchor = leafTrustStore.AddTestRoot(File.ReadAllBytes(anchorFile));
                Console.Error.WriteLine($"Trusting LEAF test root \"{anchor.Name}\" from {anchorFile}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"Error: cannot load LEAF trust anchor: {ex.Message}");
                return 1;
            }
            break;
        default:
            Console.Error.WriteLine($"Unknown option: {args[i]}");
            PrintUsage();
            return 1;
    }
}

using var smartCardService = new SmartCardService();

var readers = smartCardService.GetReaders();

if (readers.Count == 0)
{
    Console.Error.WriteLine("No PC/SC readers found.");
    return 1;
}

if (list)
{
    foreach (var r in readers)
        Console.WriteLine(r);
    return 0;
}

readerName ??= readers[0];

if (!readers.Contains(readerName))
{
    Console.Error.WriteLine($"Reader not found: {readerName}");
    Console.Error.WriteLine("Available readers:");
    foreach (var r in readers)
        Console.Error.WriteLine($"  {r}");
    return 1;
}

Console.Error.Write($"Scanning {readerName}...");

// LEAF Verified runs first: on a JavaCard emulation card the LEAF applet is only reachable
// while it is still the default-selected applet, before any ISO SELECT of another applet.
ICardDetector[] detectors =
[
    new LEAFVerifiedDetector(leafTrustStore),
    new ISO14443Detector(),
    new PIVDetector(),
    new DESFireDetector(),
    new PKOCDetector(),
    new LEAFDetector()
];

var detectionService = new CardDetectionService(smartCardService, detectors);

var progress = new Progress<CardTechnology>(tech =>
{
    if (tech != CardTechnology.Unknown)
        Console.Error.Write($" {tech}");
});

try
{
    var result = await detectionService.DetectAsync(readerName, progress);
    Console.Error.WriteLine(" done.");

    if (leafLookup && result.LEAFVerifiedDetails is { } leafDetails)
    {
        if (leafDetails.TapUrl is { } tapUrl)
        {
            Console.Error.WriteLine($"Looking up Printed ID at {LeafPrintedIdLookup.ServiceHost}...");
            var lookup = await new LeafPrintedIdLookup().LookupAsync(tapUrl);
            result = result with { LEAFVerifiedDetails = LeafPrintedIdLookup.Apply(leafDetails, lookup) };
        }
        else
        {
            Console.Error.WriteLine("No tap URL on this card; Printed ID lookup skipped.");
        }
    }

    Console.Error.WriteLine();

    if (json)
        ResultFormatter.PrintJson(result, Console.Out);
    else
        ResultFormatter.PrintText(result, Console.Out);

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(" failed.");
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage: cred-bench [options]

        Options:
          -l, --list            List available PC/SC readers
          -r, --reader <name>   Use a specific reader (default: first available)
          -j, --json            Output results as JSON
              --leaf-lookup     Look up a LEAF Verified card's Printed ID online. Sends the
                                card's one-time tap URL to d.leaf-community.com.
              --leaf-anchor <file>
                                Trust a LEAF Verified test root (PEM or DER certificate);
                                repeatable. NXP LEAF RootCAvE200 is always trusted.
          -h, --help            Show this help
        """);
}
