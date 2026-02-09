using CredBench.CLI;
using CredBench.Core.Models;
using CredBench.Core.Services;
using CredBench.Core.Services.CardDetectors;

var json = false;
string? readerName = null;
var list = false;

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

ICardDetector[] detectors =
[
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
          -h, --help            Show this help
        """);
}
