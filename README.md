# Cred Bench

A diagnostic tool that identifies smart card technologies and shows the credential data a card presents to a PC/SC reader.

[![Release](https://img.shields.io/github/v/release/Z-bit-Systems-LLC/Cred-Bench)](https://github.com/Z-bit-Systems-LLC/Cred-Bench/releases/latest)
[![Build Status](https://dev.azure.com/Z-bitSystems/Cred%20Bench/_apis/build/status/Z-bit-Systems-LLC.Cred-Bench?branchName=main)](https://dev.azure.com/Z-bitSystems/Cred%20Bench/_build/latest?definitionId=11&branchName=main)

## About

Access control credentials come in many technologies, and a card's label rarely tells you which ones it carries or what identifier a reader will take from it. Integrators, reader vendors and technicians need a quick way to see what is on a card before enrolling it or troubleshooting a reader that won't accept it.

Cred Bench talks to any PC/SC smart card reader, runs a set of detectors against the presented card and shows what each one finds: the technology, the identifier an access control system would use, and the supporting data (ATR, UID, version info, certificates and signatures). It is available as a Windows desktop app and a cross-platform command-line tool.

Core functionality is under an open source license, so a fully functional Cred Bench can be built at no cost. We encourage credential and reader vendors to use this project to speed up development of their own tools.

## Features

- **Card type identification** - Detects ISO 14443, PIV, PKOC, MIFARE DESFire, LEAF Universal and LEAF Verified credentials in a single scan
- **Credential data display** - Shows the data the card actually returns (UID, ATR, FASC-N, PKOC public key, LEAF certificate, etc.)
- **Technology-specific details** - A dedicated tab per technology with the fields relevant to it
- **Wiegand output formats** - Shows PKOC and LEAF Verified credentials as Wiegand bits, hex or decimal
- **LEAF Verified validation** - Checks the card certificate against the NXP LEAF root and any test roots you trust, and verifies the card's signature
- **Real-time monitoring** - Scans automatically when a card is presented and clears when it is removed
- **Reader hot-plug support** - Handles USB readers being connected and disconnected
- **Test card programming** (Windows) - **Program Card…** installs PKOC and/or a LEAF Verified test credential on a blank JavaCard and lists the card's applets, packages and free memory
- **Cross-platform CLI** - Text or JSON output for scripting, on Windows, macOS, Linux and Raspberry Pi

## Supported Card Technologies

| Technology | Standard | Documentation |
|------------|----------|---------------|
| ISO 14443 | ISO/IEC 14443-A/B contactless cards | [docs/ISO14443.md](docs/ISO14443.md) |
| PIV | US federal FIPS 201 identity credentials | [docs/PIV.md](docs/PIV.md) |
| PKOC | PSIA Public Key Open Credential (ECC P-256) | [docs/PKOC.md](docs/PKOC.md) |
| DESFire | NXP MIFARE DESFire | [docs/DESFire.md](docs/DESFire.md) |
| LEAF Universal / LEAF Verified | LEAF symmetric (AES-128) and asymmetric (ECC P-256) credentials | [docs/LEAF.md](docs/LEAF.md) |
| General | ATR, protocol and reader information common to all cards | [docs/General.md](docs/General.md) |

## Get Cred Bench

### Download the App

The Windows app is available on the Microsoft Store:

[![Microsoft Store](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9nj16v3kf7m2?hl=en-US&gl=US)

Or download the latest Windows release from [GitHub Releases](https://github.com/Z-bit-Systems-LLC/Cred-Bench/releases/latest). The `Cred-Bench-vX.Y.Z-win-x64.zip` package is self-contained: unzip it and run `CredBench.exe`; no .NET runtime is needed.

The CLI is not packaged yet; build it from source as shown below.

## Getting Started

### Prerequisites

- A PC/SC compliant smart card reader
- **Windows app:** Windows 10/11 (x64)
- **CLI:** Windows, macOS, or Linux with pcsc-lite (`pcscd`) running
- **Building from source:** .NET 10.0 SDK or later

### Building from Source

1. Clone the repository with submodules (the Windows app uses the shared [Guidelines](https://github.com/Z-bit-Systems-LLC/Guidelines) WPF library):
   ```bash
   git clone --recurse-submodules https://github.com/Z-bit-Systems-LLC/Cred-Bench.git
   cd Cred-Bench
   ```
   If you already cloned without submodules, run `git submodule update --init --recursive`.

2. Build the solution (Windows), or just the CLI (any platform):
   ```bash
   dotnet build Cred-Bench.slnx
   dotnet build src/UI/CLI/CLI.csproj
   ```

3. Run the application:
   ```bash
   # Windows app
   dotnet run --project src/UI/Windows/Windows.csproj

   # CLI
   dotnet run --project src/UI/CLI/CLI.csproj
   ```

> **Note:** The full solution includes the WPF app, which only builds on Windows. On macOS, Linux or Raspberry Pi, build the CLI project directly.

### Quick Start

1. Connect a PC/SC reader and launch Cred Bench
2. Select the reader from the drop-down
3. Present a card; Cred Bench scans it and lists every technology it detects
4. Open a technology's tab to see its credential data, or the General tab for the ATR and reader details

## CLI Usage

```
cred-bench [options]

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
```

### Common Scenarios

#### Scan a card

```bash
# List connected readers, then scan with a specific one
cred-bench --list
cred-bench --reader "HID Global OMNIKEY 5427 CK"
```

Without `--reader`, the first available reader is used.

#### Script against the results

```bash
cred-bench --json > card.json
```

Use the JSON output in enrolment scripts or test automation.

#### Verify a LEAF Verified test card

```bash
cred-bench --leaf-anchor my-test-root.pem
```

Cards signed by the NXP LEAF root verify without options. Add `--leaf-anchor` once per test root to trust cards created with **Program Card…** or AsymCred's `leaf-tool`. See [docs/LEAF.md](docs/LEAF.md#trust-anchors).

## JavaCard Applets

The `src/Applet/` directory contains two open-source JavaCard applets. Their CAP files are committed and embedded in Cred Bench, so the Windows app's **Program Card…** dialog can install either or both on a blank JavaCard (tested on NXP JCOP4) without other tools.

| Applet | Package AID | Applet AID | Purpose |
|---|---|---|---|
| PKOC | `A000000898000002` | `A000000898000001` | PSIA PKOC NFC Card 1.1 credential |
| LEAF Verified emulation | `F05A424C454146` | `F05A424C45414601` | Test credential for LEAF Verified readers |

- **PKOC** generates a P-256 key pair on the card and supports the SELECT/AUTHENTICATE APDU flow, creating a PKOC credential. See [docs/PKOC.md](docs/PKOC.md#programming-a-test-card).
- **LEAF Verified emulation** answers a LEAF Verified reader the way a MIFARE DUOX carrying the LEAF Open Application does: the reader reads the card certificate and challenges the card's on-card P-256 key. It installs as the default-selected applet, because LEAF readers never send an ISO SELECT, and is personalised with a certificate from a **test root** that Cred Bench creates or loads (compatible with AsymCred's `leaf-tool`). See [docs/LEAF.md](docs/LEAF.md#programming-a-test-card).

> **LEAF Verified emulation is for testing only, not for production.** Its certificates come from your own test root, not the LEAF CA, and readers that trust only the NXP root reject it. Production LEAF Verified credentials are MIFARE DUOX cards issued through the LEAF program. Do not issue emulated cards to users or enrol them in a production access system.

Both applets are copies of the sources in [AsymCred](https://github.com/Z-bit-Systems-LLC/AsymCred). See [src/Applet/README.md](src/Applet/README.md) for commands, build instructions and prerequisites (JDK 8 + Apache Ant, separate from the .NET build) and manual installation with GlobalPlatformPro.

## Testing and Development

```bash
# Unit tests (any platform)
dotnet test test/Core.Tests/Core.Tests.csproj

# UI tests (Windows only; they take over the desktop while they run)
dotnet test test/UI.Tests/UI.Tests.csproj
```

The UI tests drive the WPF app with FlaUI against mocked PC/SC services. See the [Developer Guidelines](CLAUDE.md) for the architecture, adding a card technology, and reproducing the CI code inspection.

### Project Structure

```
Cred-Bench/
├── src/
│   ├── Core/           # Cross-platform library: models, detectors, services, ViewModels
│   ├── UI/
│   │   ├── CLI/        # Cross-platform CLI
│   │   └── Windows/    # WPF application and Windows PC/SC implementation
│   └── Applet/         # PKOC and LEAF Verified emulation JavaCard applets (Java, built with Ant)
├── lib/Guidelines/     # Shared Z-bit Systems WPF library (submodule)
└── test/
    ├── Core.Tests/     # Unit tests
    └── UI.Tests/       # FlaUI tests of the WPF app with mocked PC/SC services
```

### Technology Stack

- **Framework**: .NET 10.0
- **UI**: WPF with WPF-UI (Fluent Design)
- **MVVM**: CommunityToolkit.Mvvm
- **PC/SC**: PCSC-Sharp
- **DI**: Microsoft.Extensions.Hosting
- **Testing**: NUnit, Moq, FlaUI

## Documentation

### Project Documentation
- **[Developer Guidelines](CLAUDE.md)** - Development guidelines and build commands
- **[Card technology docs](docs/)** - APDU commands, TLV parsing and credential extraction for each technology
- **[Release notes](docs/release-notes/)** - Changes in each release

### Shared Libraries
- **[Guidelines](https://github.com/Z-bit-Systems-LLC/Guidelines)** - Shared WPF components and CI/CD documentation

## Contributing

We welcome contributions! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request against the `main` branch

Follow the existing code style, include tests for new detectors and features, and update the docs as needed.

For documentation contributions:
1. Place new documentation in the `docs/` directory
2. Use descriptive filenames with `.md` extension
3. Update this README to include the new file
4. Follow the existing documentation style and structure

### Release Process

1. Bump `VersionPrefix` in `Directory.Build.props`
2. Write the release notes in `docs/release-notes/vX.Y.Z.md`
3. Make sure the CI build on `main` passes, then push a `vX.Y.Z` tag

The CI pipeline builds the Windows package and creates the GitHub release, using the notes file as its description.

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.

## Support

For questions, issues, or feature requests:
- Open an issue on [GitHub Issues](https://github.com/Z-bit-Systems-LLC/Cred-Bench/issues)
- Contact [Z-bit Systems, LLC](https://z-bitco.com)

## Related Projects

- [OSDP Bench](https://github.com/bytedreamer/OSDP-Bench) - Configuration and troubleshooting tool for OSDP devices
- [AsymCred](https://github.com/Z-bit-Systems-LLC/AsymCred) - Source of the PKOC and LEAF Verified JavaCard applets

---

**About Z-bit Systems, LLC**

Z-bit Systems specializes in access control systems, physical security integration, and enterprise security software solutions. We provide commercial support, custom development, and consulting services. Learn more at [z-bitco.com](https://z-bitco.com).
