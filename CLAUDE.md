# Cred-Bench Development Guidelines

## Project Overview

Cred-Bench is a cross-platform application for identifying smart card technologies (PIV, DESFire, iClass, PKOC, LEAF) using PC/SC readers. It includes a Windows WPF GUI and a cross-platform CLI. It follows the same architecture patterns as OSDP-Bench.

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

### UI Tests

`test/UI.Tests` drives the WPF app with FlaUI, following OSDP-Bench's UI tests. They run only on Windows, take over the desktop while they run, and need an interactive session (not a service account).

- `TestApp` derives from `App` and builds the host through `App.BuildHost(overrides)`: `ISmartCardService`, `IReaderMonitorService` and the card connection are Moq mocks, and every `ICardDetector` is replaced by a `ScriptedCardDetector` whose answer each test sets (`TestApp.Detectors[CardTechnology.DESFire].Detects(...)`). Settings go to `%LOCALAPPDATA%\CredBench.UITests`, cleared on each start.
- Every fixture starts from a fresh host, mocks and main window; `CardScanTests` restarts before each test because a scan leaves its result on screen.
- Find controls by `AutomationProperties.AutomationId`. Give any control a test needs an id in the XAML rather than searching by text or position.
- Press buttons with `Press(id)` (the UIA Invoke pattern), not mouse clicks, and compare against strings from `Core.Resources` via `Text(key, args)` so tests follow resource edits.
- Tests in the `Smoke` category run on every CI build; the rest run on the daily schedule, on manual runs and on release tags.

## CI and Code Inspection

CI builds, tests and inspects on a self-hosted Linux agent (`ci/build.yml`, `ci/inspect.yml`); the UI tests and packaging run on hosted Windows agents. The inspection fails the build on any ReSharper warning, and CI installs the latest ReSharper, so a new release can add warnings to code that hasn't changed. To reproduce the CI inspection on Linux (e.g. WSL):
```bash
dotnet build Cred-Bench.slnx -c Release -p:EnableWindowsTargeting=true
jb inspectcode Cred-Bench.slnx --no-build --properties:EnableWindowsTargeting=true -o=results.sarif
```
Without `EnableWindowsTargeting` the WPF projects don't resolve on Linux and inspection reports thousands of errors. After changing `Cred-Bench.sln.DotSettings`, delete the inspection cache (`--caches-home`) first: results for unchanged files come from the cache and still follow the old settings.

## Releasing

1. Bump `VersionPrefix` in `Directory.Build.props`.
2. Write `docs/release-notes/vX.Y.Z.md` in the same format as OSDP-Bench's releases: a `## Cred Bench X.Y.Z` heading, a one-paragraph summary, `### New` / `### Fixes` / `### Changes` bullets in user terms, and a closing `**Full changelog:**` compare link to the previous tag.
3. Make sure main's CI build passes, then push a `vX.Y.Z` tag. The Package stage creates the GitHub release with the notes file as its body; it fails early if the notes file is missing. The release has no compiled app attached, and CI doesn't build one: the Windows app ships through the Microsoft Store.

Card-technology acronyms (ATR, UID, PIV, FASCN, …) are registered as abbreviations in `Cred-Bench.sln.DotSettings`; add new ones there rather than renaming members.

## Build Commands

```bash
# Build
dotnet build Cred-Bench.slnx

# Test
dotnet test test/Core.Tests/Core.Tests.csproj

# UI tests (Windows only; drives the app on the desktop)
dotnet test test/UI.Tests/UI.Tests.csproj
dotnet test test/UI.Tests/UI.Tests.csproj --filter TestCategory=Smoke

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

# Regenerate src/UI/Windows/Logo.ico and src/UI/Windows/Assets/Logo.png from src/Assets/Logo.svg
pwsh scripts/generate-icons.ps1
```

## Adding New Card Technologies

1. Add to `CardTechnology` enum if needed
2. Create detail model in `Core/Models/TechnologyDetails/`
3. Create detector class in `Core/Services/CardDetectors/`
4. Implement `ICardDetector` interface
5. Register detector in DI container (`App.xaml.cs`) and in CLI detector array (`Program.cs`). Order matters on JavaCard: keep `LEAFVerifiedDetector` first, because an ISO SELECT of another applet hides the default-selected LEAF applet
6. Create detail UserControl in `Windows/Views/CardDetails/`
7. Add DataTemplate in `MainWindow.xaml` Resources
8. Add TabItem with visibility binding and an `AutomationProperties.AutomationId` of `Tab_<Technology>` in `MainWindow.xaml`
9. Add formatting for the new technology in `CLI/ResultFormatter.cs`
10. Add the technology to `DetectorOrder` in `test/UI.Tests/TestApp.cs`, in the same position as in `App.ConfigureServices`

## UI Card Detail Tabs

Each card technology has a dedicated UserControl for displaying its details. The tabs use implicit DataTemplates in `MainWindow.xaml` to automatically select the correct control based on the bound detail model type.

| Detail Control             | Detail Model         | Documentation |
|----------------------------|----------------------|---------------|
| `GeneralDetailsControl`    | `GeneralCardDetails` | [docs/General.md](docs/General.md) |
| `Iso14443DetailsControl`   | `ISO14443Details`    | [docs/ISO14443.md](docs/ISO14443.md) |
| `PivDetailsControl`        | `PIVDetails`         | [docs/PIV.md](docs/PIV.md) |
| `PkocDetailsControl`       | `PkocViewModel` (wraps `PKOCDetails`) | [docs/PKOC.md](docs/PKOC.md) |
| `DesfireDetailsControl`    | `DESFireDetails`     | [docs/DESFire.md](docs/DESFire.md) |
| `LeafDetailsControl`       | `LEAFDetails` (LEAF Universal) | [docs/LEAF.md](docs/LEAF.md) |
| `LeafVerifiedDetailsControl` | `LeafVerifiedViewModel` (wraps `LEAFVerifiedDetails`) | [docs/LEAF.md](docs/LEAF.md#leaf-verified) |

Controls are in `Windows/Views/CardDetails/`, models in `Core/Models/TechnologyDetails/`.

## File Organization

- Models in `Core/Models/`
- Technology detail models in `Core/Models/TechnologyDetails/`
- Interfaces in `Core/Services/`
- Implementations in `Core/Services/` or `Windows/Services/`
- Card detectors in `Core/Services/CardDetectors/`
- LEAF Verified certificate parsing, trust store, test root and card programmer in `Core/Services/Leaf/`
- Card programming (PKOC and LEAF Verified test cards): `Core/ViewModels/ProgramCardViewModel.cs` and the `Windows/Views/ProgramCardWindow.xaml` dialog; GP session setup shared in `Core/Services/GlobalPlatform/GpSessionFactory.cs`
- XAML views in `Windows/Views/`
- Card detail controls in `Windows/Views/CardDetails/`
- Converters in `Windows/Converters/`
- CLI entry point and output formatting in `UI/CLI/`
- PKOC JavaCard applet in `src/Applet/` (Java, built with Ant — separate from .NET solution)
- App logo source in `src/Assets/Logo.svg`; `src/UI/Windows/Logo.ico` and the About dialog's `src/UI/Windows/Assets/Logo.png` are generated from it by `scripts/generate-icons.ps1`
- About dialog in `Windows/Views/AboutWindow.xaml`, opened from the About button in the top-right of the main window's card scan panel; the license texts it shows are `Windows/Assets/*.txt`. Add a new third-party dependency's license there
