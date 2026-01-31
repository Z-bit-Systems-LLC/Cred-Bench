# Cred-Bench Development Guidelines

## Project Overview

Cred-Bench is a Windows application for identifying smart card technologies (PIV, DESFire, iClass, PKOC) using PC/SC readers. It follows the same architecture patterns as OSDP-Bench.

## Architecture

- **Core**: Platform-agnostic library containing models, services, and ViewModels
- **Windows**: WPF UI with Windows-specific PC/SC implementation
- **Separation of concerns**: UI depends on Core, Core has no UI dependencies

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
    Task<(bool Detected, string? Details)> DetectAsync(
        ISmartCardService cardService,
        string readerName,
        CancellationToken cancellationToken = default);
}
```

Detection is orchestrated by `CardDetectionService` which:
1. Iterates through all registered detectors
2. Catches individual detector exceptions
3. Combines results into a single `DetectionResult`

## APDU Commands

Common APDU patterns used:
- **SELECT by AID**: `00 A4 04 00 [Lc] [AID] 00`
- **GET UID**: `FF CA 00 00 00` (pseudo-APDU)
- **DESFire wrapped**: `90 [CMD] 00 00 [Le]`

## Testing

- Mock `ISmartCardService` for detector unit tests
- Test success, failure, and exception scenarios
- Use Moq for service mocking

## Build Commands

```bash
# Build
dotnet build Cred-Bench.sln

# Test
dotnet test test/Core.Tests/Core.Tests.csproj

# Run
dotnet run --project src/UI/Windows/Windows.csproj

# Publish
dotnet publish src/UI/Windows/Windows.csproj -c Release -r win-x64 --self-contained
```

## Adding New Card Technologies

1. Add to `CardTechnology` enum if needed
2. Create detail model in `Core/Models/TechnologyDetails/`
3. Create detector class in `Core/Services/CardDetectors/`
4. Implement `ICardDetector` interface
5. Register detector in DI container (`App.xaml.cs`)
6. Create detail UserControl in `Windows/Views/CardDetails/`
7. Add DataTemplate in `MainWindow.xaml` Resources
8. Add TabItem with visibility binding in `MainWindow.xaml`

## UI Card Detail Tabs

Each card technology has a dedicated UserControl for displaying its details. The tabs use implicit DataTemplates to automatically select the correct control based on the bound detail model type.

**Detail Controls** (`Windows/Views/CardDetails/`):
- `GeneralDetailsControl` - ATR, UID, CSN, card type summary
- `PivDetailsControl` - PIV status, CHUID
- `DesfireDetailsControl` - Card type, version, storage size
- `Iso14443DetailsControl` - UID, CSN, manufacturer, UID length
- `PkocDetailsControl` - Protocol version
- `LeafDetailsControl` - Application type, detected AIDs

**Detail Models** (`Core/Models/TechnologyDetails/`):
- `GeneralCardDetails`
- `PIVDetails`
- `DESFireDetails`
- `ISO14443Details`
- `PKOCDetails`
- `LEAFDetails`

DataTemplates in `MainWindow.xaml` map each model type to its control:
```xml
<DataTemplate DataType="{x:Type models:PIVDetails}">
    <cardDetails:PivDetailsControl />
</DataTemplate>
```

## File Organization

- Models in `Core/Models/`
- Technology detail models in `Core/Models/TechnologyDetails/`
- Interfaces in `Core/Services/`
- Implementations in `Core/Services/` or `Windows/Services/`
- Card detectors in `Core/Services/CardDetectors/`
- XAML views in `Windows/Views/`
- Card detail controls in `Windows/Views/CardDetails/`
- Converters in `Windows/Converters/`
