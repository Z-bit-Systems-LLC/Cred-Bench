# PKOC Detection and Credential Parsing

## Overview

PKOC (Public Key Open Credential) is a PSIA standard for NFC-based physical access control using elliptic curve cryptography. Unlike traditional credentials that rely on shared secrets, PKOC uses asymmetric key pairs (ECC P-256) where the public key serves as the credential.

## Detection

### Step 1: SELECT PKOC Applet

```
SELECT: 00 A4 04 00 08 A000000898000001 00
```

| Byte | Value    | Description                 |
|------|----------|-----------------------------|
| CLA  | `0x00`   | ISO 7816-4                  |
| INS  | `0xA4`   | SELECT                      |
| P1   | `0x04`   | Select by DF name (AID)     |
| P2   | `0x00`   | First or only occurrence    |
| Lc   | `0x08`   | AID length (8 bytes)        |
| Data | `A0...`  | PKOC AID                    |
| Le   | `0x00`   | Max response                |

**PKOC AID:** `A0 00 00 08 98 00 00 01`

**Success:** Status word `90 00`. Response contains a TLV with the protocol version.

### SELECT Response Parsing

The response contains a TLV structure with the protocol version:

| Tag    | Length | Description                        |
|--------|--------|------------------------------------|
| `0x5C` | `0x02` | Protocol Version (2 bytes, e.g., `01 01` = v1.1) |

### Step 2: AUTHENTICATE

The AUTHENTICATE command retrieves the card's public key. Per PSIA PKOC 1.1 spec:

```
AUTHENTICATE: 80 80 00 01 38 [data] 00
```

| Byte | Value    | Description                 |
|------|----------|-----------------------------|
| CLA  | `0x80`   | Proprietary class           |
| INS  | `0x80`   | AUTHENTICATE                |
| P1   | `0x00`   | —                           |
| P2   | `0x01`   | —                           |
| Lc   | `0x38`   | Data length (56 bytes)      |
| Data | See below | TLV command data           |
| Le   | `0x00`   | Max response                |

### AUTHENTICATE Command Data (56 bytes)

The command data contains three TLV elements:

| Tag    | Length | Value                | Description                    |
|--------|--------|----------------------|--------------------------------|
| `0x4C` | `0x10` | 16 random bytes      | Transaction ID                 |
| `0x5C` | `0x02` | Protocol version     | Echoed from SELECT response    |
| `0x4D` | `0x20` | 32 zero bytes        | Reader Identifier (zeros = identification-only mode) |

### AUTHENTICATE Response Parsing

The response contains a TLV with the public key:

| Tag    | Length | Description                        |
|--------|--------|------------------------------------|
| `0x5A` | `0x41` | Public Key (65 bytes, ECC P-256 uncompressed) |

## Credential Parsing

### ECC P-256 Public Key Structure

The 65-byte uncompressed public key follows the SEC 1 format:

```
04 [32-byte X component] [32-byte Y component]
```

| Offset | Length   | Description                     |
|--------|---------|---------------------------------|
| 0      | 1 byte  | `0x04` = uncompressed point     |
| 1      | 32 bytes| X component of the public key   |
| 33     | 32 bytes| Y component of the public key   |

### Credential Derivation

Per the PSIA PKOC spec, the credential is derived from the **X component** of the public key. Three credential sizes are supported:

| Size    | Derivation                              | Use Case                          |
|---------|-----------------------------------------|-----------------------------------|
| 256-bit | Full X component (32 bytes)             | Full credential                   |
| 75-bit  | Lower 75 bits of X                      | Recommended for legacy panels     |
| 64-bit  | Lower 64 bits of X (last 8 bytes)       | Minimum for legacy panels         |

### 75-bit Extraction

The lower 75 bits are extracted by:

1. Converting the 32-byte X hex to bytes
2. Taking the last 9 full bytes (72 bits)
3. Masking the preceding byte to extract the remaining 3 bits (`byte & 0x07`)
4. Combining into a 10-byte result (80 bits, top 5 bits zeroed)

### 64-bit Extraction

The lower 64 bits are simply the last 16 hex characters (8 bytes) of the X component.

### Wiegand Representation

Each credential size is converted to a binary bit string:

| Size    | Hex Length | Wiegand Length |
|---------|------------|----------------|
| 256-bit | 64 chars   | 256 bits       |
| 75-bit  | 20 chars   | 75 bits        |
| 64-bit  | 16 chars   | 64 bits        |

### Display Fields

| Field              | Source               | Description                             |
|--------------------|----------------------|-----------------------------------------|
| Protocol Version   | SELECT response      | PKOC protocol version (e.g., `01.01`)   |
| Credential Format  | User selection       | Dropdown: 256-bit, 75-bit, or 64-bit    |
| Hex                | X component          | Credential hex for selected size, copyable |
| Wiegand Bits       | Credential binary    | Binary bit string for selected size, copyable |
