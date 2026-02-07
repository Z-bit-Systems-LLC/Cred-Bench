# LEAF Detection and Parsing

## Overview

LEAF Universal (LEAF 4.0) is a credential format that runs as DESFire applications on MIFARE DESFire EV2/EV3 cards. LEAF cards contain up to three DESFire applications identified by specific AIDs.

## Detection

### Application IDs

LEAF credentials are identified by attempting to SELECT three known DESFire application IDs:

| AID        | Application           | Description                      |
|------------|-----------------------|----------------------------------|
| `F5 1C D8` | UNIVERSAL ID (primary)  | Universal ID application       |
| `F5 1C D9` | UNIVERSAL ID (secondary)| Universal ID application       |
| `F5 1C DB` | ENTERPRISE ID           | Enterprise ID application      |

### SELECT Command

Each AID is selected using ISO 7816-4 SELECT:

```
SELECT: 00 A4 04 00 03 [AID 3 bytes] 00
```

| Byte | Value    | Description                 |
|------|----------|-----------------------------|
| CLA  | `0x00`   | ISO 7816-4                  |
| INS  | `0xA4`   | SELECT                      |
| P1   | `0x04`   | Select by DF name           |
| P2   | `0x00`   | First or only occurrence    |
| Lc   | `0x03`   | AID length (3 bytes)        |
| Data | `F5...`  | LEAF application AID        |
| Le   | `0x00`   | Max response                |

**Success:** Status word `90 00` or `61 xx` (more data available).

The detector tries all three AIDs and reports which ones are present on the card. If any AID is successfully selected, the card is identified as LEAF.

### Detection Logic

1. Try SELECT for each of the three AIDs
2. Record which AIDs were successfully selected
3. The application type is determined by the first successful match:
   - `F51CD8` or `F51CD9` = UNIVERSAL ID
   - `F51CDB` = ENTERPRISE ID

## Credential Parsing

LEAF credential data is stored within the DESFire application files and requires authentication to read. The detector identifies the presence of LEAF applications but does not extract credential data (the files are access-controlled).

### Display Fields

| Field            | Source              | Description                            |
|------------------|---------------------|----------------------------------------|
| Application Type | First matched AID   | UNIVERSAL ID or ENTERPRISE ID          |
| Detected AIDs    | All matched AIDs    | List of successfully selected AIDs     |
