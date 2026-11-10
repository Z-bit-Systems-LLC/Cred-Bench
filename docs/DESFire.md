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

If SELECT succeeds, the detector proceeds to GetVersion. If GetVersion fails but SELECT succeeded, the card is still reported as DESFire, with the version marked unavailable (see [Version Unavailable](#version-unavailable)).

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

It sends at most two AdditionalFrame commands and stops early if a frame fails. Whatever arrived is still decoded: a card that answers only the first frame still yields its product, hardware version and storage size.

## Response Parsing

Decoding lives in `DESFireVersionDecoder` (`Core/Services/CardDetectors/DESFireVersionDecoder.cs`).

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

### GetVersion Payload

GetVersion needs no authentication. The three frames, with status words removed, form a 28-byte payload:

| Frame | Bytes | Contents |
|-------|-------|----------|
| 1 – Hardware | 0–6 | Vendor, type, subtype, major, minor, storage size, protocol |
| 2 – Software | 7–13 | Same layout as the hardware frame |
| 3 – Production | 14–27 | UID (7), batch number (5), production week (BCD), production year (BCD) |

EV2 and later can append a FabKey identifier to frame 3; bytes after byte 27 are ignored.

### Product Identification

The product comes from the hardware **type** (byte 1) and **major version** (byte 3) together. The major version alone is ambiguous: `22` is DESFire EV2 XL with type `01` but MIFARE Plus EV2 with type `02`, and `30` is DESFire Light or NTAG 424 DNA depending on type. The values follow NXP product data and the Proxmark3 client (`getCardType` in `client/src/cmdhfmfdes.c`).

All entries assume vendor `04` (NXP). Any other vendor is reported as `DESFire-compatible (vendor 0xXX)`.

| Type | HW major | Product |
|------|----------|---------|
| `01` | `00` | DESFire (MF3ICD40) |
| `01` | `01` | DESFire EV1 |
| `01` | `12` | DESFire EV2 |
| `01` | `22` | DESFire EV2 XL |
| `01` | `33` | DESFire EV3 |
| `01` | `A0` | MIFARE DUOX |
| `08` | `30` | DESFire Light |
| `81` | `42` | DESFire EV2 (JavaCard/SmartMX implementation) |
| `81` | `43` | DESFire EV3 (JavaCard/SmartMX implementation) |
| `91` | `62` | DESFire EV2 (Apple Wallet) |
| `02` | `11` | MIFARE Plus EV1 |
| `02` | `22` | MIFARE Plus EV2 |
| `04` | `30` | NTAG 424 DNA |
| any  | `10` (minor `00`) | NTAG 413 DNA |

MIFARE Plus and NTAG DNA cards answer GetVersion too. They are still reported under the DESFire tab, but named as the product they are. An unknown major version falls back to the type family, e.g. `DESFire (unrecognized version)`. The raw payload is shown as **GetVersion Data** so unrecognized cards can be added to the table.

Versions are shown as hex `major.minor`, the NXP and Proxmark3 convention: EV2 hardware is `12.0`, EV3 is `33.0`.

### Storage Size

The upper 7 bits of the storage byte give `n`, for 2^n bytes. If the low bit is set, the size lies between 2^n and 2^(n+1) bytes.

| Value    | Size            | Typical card        |
|----------|-----------------|---------------------|
| `0x11`   | 256 B to 512 B  | NTAG 424 DNA (416 B) |
| `0x13`   | 512 B to 1 KB   | DESFire Light (640 B) |
| `0x16`   | 2 KB            |                     |
| `0x18`   | 4 KB            |                     |
| `0x1A`   | 8 KB            |                     |
| `0x1C`   | 16 KB           |                     |
| `0x1E`   | 32 KB           |                     |

### Production Date

Week and year are BCD: `23 21` is week 23 of 2021. An all-zero pair is treated as not recorded and omitted. Bytes that are not a valid BCD week (1–53) and year are shown raw, marked as unrecognized.

### Version Unavailable

When the card is DESFire but GetVersion gives no version, the card type is `DESFire (version unknown)` and the reason is shown in place of the version rows:

| Situation | Reason |
|-----------|--------|
| SELECT succeeded, GetVersion not understood | `GetVersion not answered after SELECT` |
| `91 00` with no data | `GetVersion returned no data` |
| `91 xx` error | `GetVersion rejected: <error>` |
| `91 AF` with fewer than 7 data bytes | `GetVersion hardware frame too short` |
| `90 00` with data | `GetVersion answered with an ISO status word` |

### Display Fields

| Field            | Source                     | Example            |
|------------------|----------------------------|--------------------|
| Card Type        | Type + major version       | `DESFire EV3`      |
| Vendor           | Hardware byte 0            | `NXP`              |
| Hardware Version | Hardware bytes 3–4 (hex)   | `33.0`             |
| Software Version | Software bytes 3–4 (hex)   | `3.0`              |
| Version          | Only when unavailable      | `Unavailable (GetVersion rejected: illegal command)` |
| Storage Size     | Hardware byte 5            | `8 KB`             |
| Production Date  | Frame 3 bytes 12–13 (BCD)  | `Week 23, 2021`    |
| Batch Number     | Frame 3 bytes 7–11         | `BA00123456`       |
| GetVersion Data  | Full payload, hex          | `04010133001A05…`  |
