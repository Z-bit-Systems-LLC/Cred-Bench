# Cred-Bench

A cross-platform diagnostic tool for identifying smart card technologies and examining the credential data. Available as a Windows WPF GUI and a cross-platform CLI.

## Features

- **Card type identification**: Automatically detects PIV, DESFire, ISO14443, PKOC, LEAF Universal, and LEAF Verified credentials
- **Credential data display**: Shows the actual data transmitted by the card (UID, CSN, ATR, version info, etc.)
- **Technology-specific details**: Dedicated views for each card type showing relevant fields
- **Real-time monitoring**: Automatically detects card insertion and removal
- **Reader hot-plug support**: Handles USB reader connection/disconnection
- **Modern UI**: Fluent Design WPF interface using WPF-UI
- **Cross-platform CLI**: Command-line interface with text and JSON output
- **Test card programming** (Windows): **Program Card…** installs PKOC and/or a LEAF Verified
  test credential on a blank JavaCard, and lists the card's applets, packages and free memory.
  LEAF Verified cards made this way are for testing only, never production
  (see [docs/LEAF.md](docs/LEAF.md#programming-a-test-card))

## Supported Card Technologies

| Technology | Documentation |
|------------|---------------|
| ISO 14443  | [docs/ISO14443.md](docs/ISO14443.md) |
| PIV        | [docs/PIV.md](docs/PIV.md) |
| PKOC       | [docs/PKOC.md](docs/PKOC.md) |
| DESFire    | [docs/DESFire.md](docs/DESFire.md) |
| LEAF Universal / LEAF Verified | [docs/LEAF.md](docs/LEAF.md) |
| General    | [docs/General.md](docs/General.md) |

See the [docs/](docs/) folder for detailed detection and credential parsing documentation for each card technology.

## Requirements

- .NET 10.0 Runtime
- PC/SC compliant smart card reader
- Windows 10/11 (for WPF GUI)
- macOS or Linux with pcscd (for CLI)

## Building from Source

```bash
# Clone the repository (include submodules for the WPF GUI)
git clone --recurse-submodules https://github.com/Z-bit-Systems-LLC/Cred-Bench.git
cd Cred-Bench

# If already cloned without submodules, pull them for the Windows GUI
git submodule update --init --recursive

# Build the full solution (Windows only — includes WPF GUI)
dotnet build

# Build just the CLI (cross-platform — works on Linux, macOS, Raspberry Pi)
dotnet build src/UI/CLI/CLI.csproj

# Run tests
dotnet test test/Core.Tests/Core.Tests.csproj

# Run the WPF application (Windows only)
dotnet run --project src/UI/Windows/Windows.csproj

# Run the CLI (cross-platform)
dotnet run --project src/UI/CLI/CLI.csproj
```

> **Note:** The full solution (`dotnet build`) requires Windows because it includes the WPF GUI and its Guidelines dependency. On non-Windows platforms (Linux, macOS, Raspberry Pi), build the CLI project directly instead.

## CLI Usage

```
cred-bench [options]

Options:
  -l, --list            List available PC/SC readers
  -r, --reader <name>   Use a specific reader (default: first available)
  -j, --json            Output results as JSON
  -h, --help            Show this help
```

Examples:

```bash
# Scan with the first available reader
cred-bench

# List connected readers
cred-bench --list

# Scan a specific reader
cred-bench --reader "HID Global OMNIKEY 5427 CK"

# Output as JSON (useful for scripting)
cred-bench --json
```

## Architecture

```
Cred-Bench/
├── src/
│   ├── Core/           # Cross-platform library
│   │   ├── Models/     # Data models (CardTechnology, DetectionResult)
│   │   ├── Services/   # Card detection services and interfaces
│   │   └── ViewModels/ # MVVM ViewModels
│   ├── UI/
│   │   ├── CLI/        # Cross-platform CLI
│   │   └── Windows/    # WPF application
│   │       ├── Services/   # Windows-specific PC/SC implementation
│   │       └── Views/      # XAML views
│   └── Applet/         # PKOC and LEAF Verified emulation JavaCard applets (Java, built with Ant)
└── test/
    └── Core.Tests/     # Unit tests
```

## JavaCard Applets

The `src/Applet/` directory contains two open-source JavaCard applets. Their CAP files are committed and embedded in Cred-Bench, so the Windows app's **Program Card…** dialog can install either or both on a blank JavaCard (tested on NXP JCOP4) without other tools.

| Applet | Package AID | Applet AID | Purpose |
|---|---|---|---|
| PKOC | `A000000898000002` | `A000000898000001` | PSIA PKOC NFC Card 1.1 credential |
| LEAF Verified emulation | `F05A424C454146` | `F05A424C45414601` | Test credential for LEAF Verified readers |

- **PKOC** generates a P-256 key pair on the card and supports the SELECT/AUTHENTICATE APDU flow, creating a PKOC credential. See [docs/PKOC.md](docs/PKOC.md#programming-a-test-card).
- **LEAF Verified emulation** answers a LEAF Verified reader the way a MIFARE DUOX carrying the LEAF Open Application does: the reader reads the card certificate and challenges the card's on-card P-256 key. It installs as the default-selected applet, because LEAF readers never send an ISO SELECT, and is personalised with a certificate from a **test root** that Cred-Bench creates or loads (compatible with AsymCred's `leaf-tool`). See [docs/LEAF.md](docs/LEAF.md#programming-a-test-card).

> **LEAF Verified emulation is for testing only, not for production.** Its certificates come from your own test root, not the LEAF CA, and readers that trust only the NXP root reject it. Production LEAF Verified credentials are MIFARE DUOX cards issued through the LEAF program. Do not issue emulated cards to users or enrol them in a production access system.

Both applets are copies of the sources in [AsymCred](https://github.com/Z-bit-Systems-LLC/AsymCred). See [src/Applet/README.md](src/Applet/README.md) for commands, build instructions and prerequisites (JDK 8 + Apache Ant, separate from the .NET build) and manual installation with GlobalPlatformPro.

## Technology Stack

- **Framework**: .NET 10.0
- **UI**: WPF with WPF-UI (Fluent Design)
- **MVVM**: CommunityToolkit.Mvvm
- **PC/SC**: PCSC-Sharp
- **DI**: Microsoft.Extensions.Hosting
- **Testing**: NUnit, Moq

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Author

**Z-bit Systems LLC**

- Website: [z-bitco.com](https://z-bitco.com)
- GitHub: [@Z-bit-Systems-LLC](https://github.com/Z-bit-Systems-LLC)
