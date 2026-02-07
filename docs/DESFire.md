# DESFire Detection and Parsing

## Overview

MIFARE DESFire is an NXP contactless smart card IC based on open global standards. DESFire cards support multiple applications with flexible file structures and various security levels. Detection uses a multi-strategy approach since different DESFire variants respond to different command formats.

## Detection

### Strategy 1: SELECT by AID

The detector attempts to SELECT known DESFire AIDs using ISO 7816-4:

```
SELECT: 00 A4 04 00 [Lc] [AID] 00
```

**DESFire AIDs tried (in order):**

| AID                            | Description      |
|--------------------------------|------------------|
| `D2 76 00 00 85 01 00`         | DESFire Standard |
| `D2 76 00 00 85 01 01`         | DESFire EV1      |
| `D2 76 00 00 85 01 02`         | DESFire EV2      |

**Success:** Status word `90 00`, `61 xx` (more data), or `91 00` (DESFire native success).

If SELECT succeeds, the detector proceeds to GetVersion. If GetVersion fails but SELECT succeeded, the card is still reported as DESFire.

### Strategy 2: GetVersion (Native Wrapped)

Sent without prior SELECT, using native DESFire command wrapped in ISO APDU:

```
GetVersion: 90 60 00 00 00
```

| Byte | Value    | Description                     |
|------|----------|---------------------------------|
| CLA  | `0x90`   | Native DESFire wrapped          |
| INS  | `0x60`   | GetVersion                      |
| P1   | `0x00`   | —                               |
| P2   | `0x00`   | —                               |
| Le   | `0x00`   | Max response                    |

### Strategy 3: GetVersion (ISO Wrapped)

```
GetVersion: 00 60 00 00 00
```

Uses `CLA=0x00` (ISO class) instead of `0x90` for cards that prefer ISO framing.

### Strategy 4: GetVersion (Raw)

```
GetVersion: 60
```

Single-byte raw DESFire native command for cards that accept raw framing.

### Additional Frames

GetVersion returns data across three frames. After the first response with status `91 AF` (more data), the detector sends:

```
AdditionalFrame (native): 90 AF 00 00 00
AdditionalFrame (ISO):    00 AF 00 00 00
```

## Response Parsing

### DESFire Status Words

DESFire uses status word `91 xx` for native responses:

| Status   | Meaning                    |
|----------|----------------------------|
| `91 00`  | Success                    |
| `91 AF`  | More frames available      |
| `91 0B`  | Authentication required    |
| `91 1C`  | Illegal command            |
| `91 9D`  | Permission denied          |
| `91 AE`  | Authentication error       |
| `91 A0`  | Application not found      |

Any `91 xx` response confirms the card is DESFire, even if the specific operation was denied.

### GetVersion Frame 1 (Hardware Info)

| Byte | Description              | Values                          |
|------|--------------------------|---------------------------------|
| 0    | Vendor ID                | `0x04` = NXP                    |
| 1    | Type                     | Hardware type                   |
| 2    | Subtype                  | Hardware subtype                |
| 3    | Major version            | See card type table             |
| 4    | Minor version            | Minor version number            |
| 5    | Storage size             | See storage table               |
| 6    | Protocol                 | Communication protocol          |

### Card Type Identification

| Major Version | Card Type     |
|---------------|---------------|
| `0x00`        | DESFire       |
| `0x01`        | DESFire EV1   |
| `0x12`        | DESFire EV2   |
| `0x33`        | DESFire EV3   |

### Storage Size

| Value    | Size  |
|----------|-------|
| `0x16`   | 2 KB  |
| `0x18`   | 4 KB  |
| `0x1A`   | 8 KB  |
| `0x1C`   | 16 KB |
| `0x1E`   | 32 KB |

### Display Fields

| Field           | Source                | Description                     |
|-----------------|-----------------------|---------------------------------|
| Card Type       | Major version byte    | DESFire / EV1 / EV2 / EV3      |
| Version         | Bytes 3-4             | Hardware version (e.g., `1.0`)  |
| Storage         | Byte 5                | Storage capacity (e.g., `4KB`)  |
