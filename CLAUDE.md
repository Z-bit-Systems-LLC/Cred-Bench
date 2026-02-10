# Cred-Bench Development Guidelines

## Project Overview

Cred-Bench is a cross-platform application for identifying smart card technologies (PIV, DESFire, iClass, PKOC) using PC/SC readers. It includes a Windows WPF GUI and a cross-platform CLI. It follows the same architecture patterns as OSDP-Bench.

## Architecture

- **Core**: Platform-agnostic library containing models, services, and ViewModels
- **Windows**: WPF UI with Windows-specific PC/SC implementation
- **CLI**: Cross-platform command-line interface for card detection
- **Separation of concerns**: UI projects depend on Core, Core has no UI dependencies

## Key Technologies

- .NET 10.0
- WPF-UI (Fluent Design)
- CommunityToolkit.Mvvm (source generators)
- PCSC-Sharp for smart card communication
- Microsoft.Extensions.Hosting for DI

## Code Style

- Use C# 13 features (collection expressions, primary constructors where appropriate)
- Prefer records for immutable data models
- Use `[ObservableProperty]` and `[RelayCommand]` attributes from CommunityToolkit.Mvvm
- Follow async/await patterns for all I/O operations
- Handle exceptions at service boundaries, not in detectors

## Card Detection

Each detector implements `ICardDetector`:
```csharp
public interface ICardDetector
{
    CardTechnology Technology { get; }
    (bool Detected, string? Details, object? TypedDetails) Detect(ICardConnection connection);
}
```

Detection is orchestrated by `CardDetectionService` which:
1. Opens a single `ICardConnection` to the reader
2. Retrieves ATR, UID, and protocol
3. Iterates through all registered detectors
4. Catches individual detector exceptions
5. Combines results into a single `DetectionResult`

For APDU commands, TLV parsing, and credential extraction details per technology, see the [docs/](docs/) folder.

## Testing

- Mock `ISmartCardService` for detector unit tests
- Test success, failure, and exception scenarios
- Use Moq for service mocking

## Build Commands

```bash
# Build
dotnet build Cred-Bench.slnx

# Test
dotnet test test/Core.Tests/Core.Tests.csproj

# Run (Windows WPF)
dotnet run --project src/UI/Windows/Windows.csproj

# Run (CLI)
dotnet run --project src/UI/CLI/CLI.csproj

# CLI with options
dotnet run --project src/UI/CLI/CLI.csproj -- --list
dotnet run --project src/UI/CLI/CLI.csproj -- --json
dotnet run --project src/UI/CLI/CLI.csproj -- --reader "Reader Name"

# Publish (Windows)
dotnet publish src/UI/Windows/Windows.csproj -c Release -r win-x64 --self-contained

# Publish (CLI — macOS)
dotnet publish src/UI/CLI/CLI.csproj -c Release -r osx-arm64 --self-contained
```

## Adding New Card Technologies

1. Add to `CardTechnology` enum if needed
2. Create detail model in `Core/Models/TechnologyDetails/`
3. Create detector class in `Core/Services/CardDetectors/`
4. Implement `ICardDetector` interface
5. Register detector in DI container (`App.xaml.cs`) and in CLI detector array (`Program.cs`)
6. Create detail UserControl in `Windows/Views/CardDetails/`
7. Add DataTemplate in `MainWindow.xaml` Resources
8. Add TabItem with visibility binding in `MainWindow.xaml`
9. Add formatting for the new technology in `CLI/ResultFormatter.cs`

## UI Card Detail Tabs

Each card technology has a dedicated UserControl for displaying its details. The tabs use implicit DataTemplates in `MainWindow.xaml` to automatically select the correct control based on the bound detail model type.

| Detail Control             | Detail Model         | Documentation |
|----------------------------|----------------------|---------------|
| `GeneralDetailsControl`    | `GeneralCardDetails` | [docs/General.md](docs/General.md) |
| `Iso14443DetailsControl`   | `ISO14443Details`    | [docs/ISO14443.md](docs/ISO14443.md) |
| `PivDetailsControl`        | `PIVDetails`         | [docs/PIV.md](docs/PIV.md) |
| `PkocDetailsControl`       | `PKOCDetails`        | [docs/PKOC.md](docs/PKOC.md) |
| `DesfireDetailsControl`    | `DESFireDetails`     | [docs/DESFire.md](docs/DESFire.md) |
| `LeafDetailsControl`       | `LEAFDetails`        | [docs/LEAF.md](docs/LEAF.md) |

Controls are in `Windows/Views/CardDetails/`, models in `Core/Models/TechnologyDetails/`.

## File Organization

- Models in `Core/Models/`
- Technology detail models in `Core/Models/TechnologyDetails/`
- Interfaces in `Core/Services/`
- Implementations in `Core/Services/` or `Windows/Services/`
- Card detectors in `Core/Services/CardDetectors/`
- XAML views in `Windows/Views/`
- Card detail controls in `Windows/Views/CardDetails/`
- Converters in `Windows/Converters/`
- CLI entry point and output formatting in `UI/CLI/`
