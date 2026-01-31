# Cred-Bench

A Windows application to identify smart card types (DESFire, iClass, PKOC, PIV) presented to USB PC/SC readers.

## Features

- **Multi-technology detection**: Identifies PIV, DESFire, iClass, and PKOC credentials
- **Real-time monitoring**: Automatically detects card insertion and removal
- **Reader hot-plug support**: Handles USB reader connection/disconnection
- **Modern UI**: Fluent Design WPF interface using WPF-UI

## Supported Card Technologies

| Technology | Detection Method |
|------------|------------------|
| PIV | SELECT AID command |
| DESFire | GetVersion command |
| iClass | ATR pattern matching + proprietary commands |
| PKOC | NDEF application with PKOC signature |

## Requirements

- Windows 10/11
- .NET 10.0 Runtime
- PC/SC compliant smart card reader

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

# Run the application
dotnet run --project src/UI/Windows/Windows.csproj
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
- **Testing**: xUnit, Moq

## License

This project is licensed under the Eclipse Public License 2.0 - see the [LICENSE](LICENSE) file for details.

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Author

**Z-bit Systems LLC**

- Website: [z-bitsystems.com](https://z-bitsystems.com)
- GitHub: [@Z-bit-Systems-LLC](https://github.com/Z-bit-Systems-LLC)
