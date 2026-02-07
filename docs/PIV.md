# PIV Detection and Credential Parsing

## Overview

PIV (Personal Identity Verification) is a US federal standard (FIPS 201) for identity credentials. PIV cards contain a CHUID (Card Holder Unique Identifier) which includes a FASC-N (Federal Agency Smart Credential Number) — the primary credential identifier.

## Detection

### Step 1: SELECT PIV Application

```
SELECT: 00 A4 04 00 0B A0000003080000100001 00
```

| Byte | Value    | Description                 |
|------|----------|-----------------------------|
| CLA  | `0x00`   | ISO 7816-4                  |
| INS  | `0xA4`   | SELECT                      |
| P1   | `0x04`   | Select by DF name (AID)     |
| P2   | `0x00`   | First or only occurrence    |
| Lc   | `0x0B`   | AID length (11 bytes)       |
| Data | `A0...`  | PIV AID                     |
| Le   | `0x00`   | Max response                |

**PIV AID:** `A0 00 00 03 08 00 00 10 00 01 00`

**Success:** Status word `90 00` or `61 xx` (more data available).

### Step 2: GET DATA for CHUID

Two command variants are attempted:

**Standard:**
```
GET DATA: 00 CB 3F FF 05 5C 03 5FC102 00
```

**Alternative:**
```
GET DATA: 00 CB 3F FF 05 5C 0A 5FC102 00
```

| Byte | Value    | Description                 |
|------|----------|-----------------------------|
| CLA  | `0x00`   | ISO 7816-4                  |
| INS  | `0xCB`   | GET DATA                    |
| P1P2 | `0x3FFF` | Current DF                  |
| Lc   | `0x05`   | Tag list length             |
| Data | `5C...`  | Tag list requesting CHUID (`5F C1 02`) |
| Le   | `0x00`   | Max response                |

## CHUID Parsing

The CHUID response is a BER-TLV structure wrapped in an outer container tag `0x53`. The detector parses the TLV to find the FASC-N:

```
53 [length] ... 30 [length] [FASC-N bytes] ...
```

| Tag    | Description                         |
|--------|-------------------------------------|
| `0x53` | Outer CHUID container               |
| `0x30` | FASC-N (Federal Agency Smart Credential Number) |

BER-TLV length encoding is supported:
- **Short form:** Single byte, value 0-127
- **Long form:** First byte = `0x80 | N`, followed by N bytes of length

## FASC-N Decoding

The FASC-N is a 200-bit (25-byte) BCD-encoded credential number. Each character is encoded as 5 bits: 4 data bits (LSB first) + 1 parity bit, yielding 40 five-bit characters.

### 5-bit Character Encoding

| Value | Character | Meaning          |
|-------|-----------|------------------|
| 0-9   | `0`-`9`   | Digit            |
| 10    | `S`       | Start Sentinel   |
| 11    | `F`       | Field Separator  |
| 13    | `F`       | Field Separator  |
| 15    | `E`       | End Sentinel     |

### FASC-N Field Layout

The decoded 40-character string follows this format:

```
S [Agency 4] F [System 4] F [Credential 6] F [CS 1] F [ICI 1] F [PI 10] [OC 1] [OI 4] [POA 1] E [LRC 1]
```

| Field                        | Position | Length | Description                             |
|------------------------------|----------|--------|-----------------------------------------|
| Start Sentinel               | 0        | 1      | Always `S`                              |
| Agency Code                  | 1        | 4      | 4-digit agency identifier               |
| Field Separator              | 5        | 1      | Always `F`                              |
| System Code                  | 6        | 4      | 4-digit system identifier               |
| Field Separator              | 10       | 1      | Always `F`                              |
| Credential Number            | 11       | 6      | 6-digit credential number               |
| Field Separator              | 17       | 1      | Always `F`                              |
| Credential Series            | 18       | 1      | Credential series code                  |
| Field Separator              | 19       | 1      | Always `F`                              |
| Individual Credential Issue  | 20       | 1      | Individual credential issue code        |
| Field Separator              | 21       | 1      | Always `F`                              |
| Person Identifier            | 22       | 10     | 10-digit unique person identifier       |
| Organizational Category      | 32       | 1      | Organization category code              |
| Organizational Identifier    | 33       | 4      | 4-digit organization identifier         |
| Person/Org Association       | 37       | 1      | Person-organization association category |
| End Sentinel                 | 38       | 1      | Always `E`                              |
| Longitudinal Redundancy Check| 39       | 1      | LRC for error detection                 |

### Wiegand Representation

The raw 25-byte FASC-N is converted to a 200-bit binary string for Wiegand display. This is the full FASC-N in its raw bit form, suitable for programming into access control panels.

### Display Fields

| Field                       | Source           | Description                          |
|-----------------------------|------------------|--------------------------------------|
| Status                      | Detection result | PIV application detection status     |
| Agency Code                 | FASC-N[1..5]     | 4-digit agency code                  |
| System Code                 | FASC-N[6..10]    | 4-digit system code                  |
| Credential Number           | FASC-N[11..17]   | 6-digit credential number            |
| Credential Series           | FASC-N[18..19]   | Series code                          |
| Individual Credential Issue | FASC-N[20..21]   | Issue code                           |
| Person Identifier           | FASC-N[22..32]   | 10-digit person identifier           |
| Organizational Category     | FASC-N[32..33]   | Category code                        |
| Organizational Identifier   | FASC-N[33..37]   | 4-digit org identifier               |
| Person/Org Association      | FASC-N[37..38]   | Association category                 |
| FASC-N Hex                  | Raw bytes        | Full FASC-N in hexadecimal, copyable |
| Wiegand Bits (200)          | Raw bytes        | Binary bit string, copyable          |
