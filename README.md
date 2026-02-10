# Cred-Bench

A cross-platform diagnostic tool for identifying smart card technologies and examining the credential data. Available as a Windows WPF GUI and a cross-platform CLI.

## Features

- **Card type identification**: Automatically detects PIV, DESFire, ISO14443, PKOC, and LEAF credentials
- **Credential data display**: Shows the actual data transmitted by the card (UID, CSN, ATR, version info, etc.)
- **Technology-specific details**: Dedicated views for each card type showing relevant fields
- **Real-time monitoring**: Automatically detects card insertion and removal
- **Reader hot-plug support**: Handles USB reader connection/disconnection
- **Modern UI**: Fluent Design WPF interface using WPF-UI
- **Cross-platform CLI**: Command-line interface with text and JSON output

## Supported Card Technologies

| Technology | Documentation |
|------------|---------------|
| ISO 14443  | [docs/ISO14443.md](docs/ISO14443.md) |
| PIV        | [docs/PIV.md](docs/PIV.md) |
| PKOC       | [docs/PKOC.md](docs/PKOC.md) |
| DESFire    | [docs/DESFire.md](docs/DESFire.md) |
| LEAF       | [docs/LEAF.md](docs/LEAF.md) |
| General    | [docs/General.md](docs/General.md) |

See the [docs/](docs/) folder for detailed detection and credential parsing documentation for each card technology.

## Requirements

- .NET 10.0 Runtime
- PC/SC compliant smart card reader
- Windows 10/11 (for WPF GUI)
- macOS or Linux with pcscd (for CLI)

## Installation

Download the latest release from the [Releases](https://github.com/Z-bit-Systems-LLC/Cred-Bench/releases) page.

## Building from Source

```bash
# Clone the repository
git clone https://github.com/Z-bit-Systems-LLC/Cred-Bench.git
cd Cred-Bench

# Build the solution
dotnet build

# Run tests
dotnet test

# Run the WPF application (Windows only)
dotnet run --project src/UI/Windows/Windows.csproj

# Run the CLI (cross-platform)
dotnet run --project src/UI/CLI/CLI.csproj
```

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
│   └── UI/
│       ├── CLI/        # Cross-platform CLI
│       └── Windows/    # WPF application
│           ├── Services/   # Windows-specific PC/SC implementation
│           └── Views/      # XAML views
└── test/
    └── Core.Tests/     # Unit tests
```

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
