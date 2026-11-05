# LEAF Detection and Parsing

Cred-Bench detects two LEAF generations. They share only a name, so they are separate
technologies, detectors, tabs and CLI sections:

| | LEAF Universal | LEAF Verified |
|---|---|---|
| Cryptography | Symmetric, AES-128 | Asymmetric, ECC P-256 |
| Chip | DESFire EV2/EV3 | MIFARE DUOX (or a JavaCard emulation) |
| AIDs | `F51CD8`, `F51CD9`, `F51CDB` | `F51CD6` (the Open Application) |
| Selection | ISO SELECT | DESFire native `SelectApplication` (`90 5A`) |
| What a reader can read | Presence only | An X.509 certificate plus a challenge signature |
| Credential | Inside protected files | **Open ID**, sent as **W40-2** |
| Detector | `LEAFDetector` | `LEAFVerifiedDetector` |

---

# LEAF Universal

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

---

# LEAF Verified

## Overview

LEAF Verified is the LEAF Community's asymmetric credential, on NXP MIFARE DUOX. Each card holds
a P-256 key pair and an X.509 certificate for its public key, signed by the LEAF CA at wafer
provisioning. The certificate's subject `serialNumber` is the **Open ID**, a 38-bit number that
a reader reports as a **W40-2** Wiegand frame.

Reference: the [LEAF Verified device onboarding guide](https://github.com/LEAF-Community/leaf-verified-device-onboarding-guide).
The reader flow mirrors AsymCred's `LeafReader` (branch `feature/leaf-verified`).

## Detection

All multi-byte DESFire fields are LSB first.

| Step | C-APDU | Expected response |
|---|---|---|
| SelectApplication | `90 5A 00 00 03 D6 1C F5 00` | `91 00` (also accepts `90 00`) |
| ReadData: header | `90 AD 00 00 07 02 000000 080000 00` (file 2, offset 0, length 8) | 8 bytes + `91 00` |
| ReadData: rest | `90 AD 00 00 07 02 <off:3> <len:3> 00`, len ≤ 255 | data + `91 00`, or a frame + `91 AF` |
| GetAdditionalFrame | `90 AF 00 00 00` | next frame + `91 AF` / `91 00` |
| ISOInternalAuthenticate | `00 88 00 00 16 80 00 7C 12 81 10 <RndA:16> 00` | `7C 54 81 10 <RndB:16> 82 40 <R‖S:64> 90 00` |

- The certificate length comes from its DER header in the 8-byte read: `30 82 HH LL` is
  `4 + 0xHHLL` bytes. Lengths above 1024 are refused.
- `Lc` of the authenticate command is `0x16`. The guide's prose says `0x14`, but its own code
  and Proxmark3 send 22 data bytes.
- The card signs `F0 F0 ‖ 80 00 ‖ RndB ‖ RndA` with ECDSA-P256-SHA256. RndA is fresh from
  `RandomNumberGenerator` on every read. The signature is raw `R‖S`; the 68-byte `82 44` form
  is reported as unsupported until a real card is seen to send it.

### Detection Logic

1. `SelectApplication`. Any answer other than `91 00`/`90 00` means "not LEAF Verified".
2. Read file 2 in full, following `91 AF` frames. Over-delivery or short delivery is an error.
3. Check the certificate against the LEAF profile, then try each trust anchor in order.
4. **Always** send `ISOInternalAuthenticate`, even when the issuer is untrusted, and verify
   the answer with the certificate's key. (A production reader would stop at step 3. Cred-Bench
   is a diagnostic bench, so it reports everything and labels it.)
5. Once `SelectApplication` succeeded, the card is reported as LEAF Verified. Any later
   failure is recorded in the details (error text and status word), not as "not detected".

### Detector Order

On a JavaCard emulation card the LEAF applet is the default-selected applet, and `90 5A` reaches
it only while no other applet has been selected. A successful ISO SELECT of another applet such
as PKOC would hide it until the next card reset. `LEAFVerifiedDetector` is therefore registered
**first**, in both `App.xaml.cs` and the CLI's `Program.cs`. On a real DUOX the order does not
matter.

## Certificate Profile

Checked by `LeafCertificate`. Every violation is reported, not just the first:

- signature algorithm `ecdsa-with-SHA256` (`1.2.840.10045.4.3.2`), no parameters, identical
  inside and outside `tbsCertificate`;
- SubjectPublicKeyInfo `id-ecPublicKey` + `prime256v1`, uncompressed 65-byte point;
- exactly one subject `serialNumber` (`2.5.4.5`), PrintableString or UTF8String, 1–12 decimal
  digits, at most 2^38 − 1. This is the Open ID.

Validity dates are **not** checked: readers have no trusted clock, and test certificates use
`99991231235959Z`.

## Trust Anchors

| Anchor | Source |
|---|---|
| NXP LEAF RootCAvE200 (P-256) | Built in, from the onboarding guide: `042D2781…D2F11B3C` |
| Test roots | Added by the user from a `ca.crt` (PEM or DER), e.g. from AsymCred's `leaf-tool init-ca` |

- **GUI:** the LEAF Verified tab lists the anchors. **Add Test Root…** loads a certificate, and
  test roots are saved (as PEM) in `%LOCALAPPDATA%\CredBench\settings.json`. Adding or removing
  a root re-checks the certificate on screen at once, with no rescan needed.
- **CLI:** `--leaf-anchor <file>`, repeatable.

No test root is ever built in.

## W40-2

Numbering bit 0 as the first transmitted bit:

- bit 0: even parity over bits 1–19;
- bits 1–38: the Open ID, MSB first;
- bit 39: odd parity over bits 0–38.

| Open ID | W40-2 (hex) |
|---|---|
| 160000000000 | `4A817C8000` |
| 1 | `0000000002` |
| 274877906943 (2^38 − 1) | `FFFFFFFFFE` |

## Trust States

| State | Shown as |
|---|---|
| Trusted certificate and valid card signature | ✅ "Verified — <anchor>" |
| Card signature invalid | ❌ "Card failed the challenge — possible clone" |
| Untrusted issuer | ⚠ "Certificate not signed by a trusted root", Open ID marked *unverified* |
| Profile violation | ⚠ every issue listed |
| Unreadable | ⚠ error text and status word |

## Display Fields

| Field | Source |
|---|---|
| Open ID | Certificate subject `serialNumber` |
| W40-2 Hex / Bits | Computed from the Open ID |
| Certificate | Trust verdict and the anchor that signed it |
| Card Signature | ISOInternalAuthenticate result |
| Issuer | Certificate issuer DN |
| Authority Key ID | Certificate AKI extension (identifies the signing CA key) |
| Card Public Key | Certificate SubjectPublicKeyInfo, `04‖X‖Y` |
| Status Word | The last failing command's SW, when a step failed |
| Printed ID | Opt-in online lookup of the tap URL (see [Printed ID](#printed-id)) |
| Tap URL | The card's one-time NDEF URL, from application `F51CD5` |

**Export Certificate…** saves the card certificate as PEM, for inspection with `openssl x509 -text`.
The CLI's `--json` output includes it as base64.

### Status Words

| SW | Meaning |
|---|---|
| `91 A0` | Application not found (not LEAF Verified) |
| `91 BE` | Boundary error |
| `91 F0` | File not found |
| `91 AE` | Authentication error |
| `91 CA` | Command aborted |

## Field Observations

First production card read on 2026-11-05 (Wavelynx DUOX, ACR122):

1. The card certificate is signed **directly** by RootCAvE200 (issuer
   `SERIALNUMBER=63709320131000, CN=NXP LEAF RootCAvE200, O=NXP`). The guide's published
   key verifies it. No intermediate, and no Authority Key ID extension.
2. File 2 is plain DER at offset 0.
3. The card answers with the 64-byte raw `82 40` signature form.
4. GetVersion: hardware `04 01 01 A0 00 18 25`, software `04 01 07 00 01 18 25`, then the
   7-byte UID and batch number. Hardware major `0xA0` identifies DUOX.

### Card Layout (read-only probe)

| Application | File | Settings | Contents |
|---|---|---|---|
| `F51CD6` Open Application | 1 | 32 bytes, read needs a key (`91 AE`) | Unknown |
| `F51CD6` | 2 | 512 bytes, free read | Certificate `CN=AppUniversal`, `serialNumber` = Open ID |
| `F51CD7` (not in the guide) | 1 | 32 bytes, full encryption, read needs a key | Unknown |
| `F51CD7` | 2 | backup file, 880 bytes, free read | `81 01 00` + certificate `CN=AppEnterprise`, signed by RootCAvE200, no `serialNumber` |
| `F51CD5` NFC Forum NDEF (`D2760000850101`) | 1 / 2 | free read | Capability container / NDEF URI `https://d.leaf-community.com?uid=â€¦&bid=â€¦&cmac=â€¦` (SUN-style: encrypted uid, MAC) |

Both certificates carry `description=04A00001070001` and the UID as `x500UniqueIdentifier`.

`ReadData` with length 0 ("to end of file") is refused with `91 7E` on DUOX; give an explicit length.

### Printed ID

The number printed on the card (five letters, a dash and three digits, e.g. `ABCDE-001`) is not stored anywhere a reader can read
freely. It comes from the LEAF Community tap service:

1. The NDEF application `F51CD5`, file 2, holds a Secure Unique NFC URL,
   `https://d.leaf-community.com?uid=…&bid=…&cmac=…`. The card generates a new one on every
   read, with an encrypted UID, a read counter and a MAC, so each URL can be used once.
2. The server checks it and answers with a redirect to an order page. The redirect address
   carries a `token` query parameter: an ES256 JWT (`kid` `duox-tap-redirect-2025`, valid for 60 s)
   whose payload includes `open_id` and `printed_id`.
3. The signing key is published at `https://d.leaf-community.com/.well-known/jwks.json`.

Cred-Bench reads the tap URL during every scan; that read is offline. The lookup itself is
**opt-in**: the **Look Up Printed ID…** button on the LEAF Verified tab, or `--leaf-lookup` in the
CLI. `LeafPrintedIdLookup`:

- contacts only `https://d.leaf-community.com`, because the URL comes from the card and must
  not be able to point Cred-Bench at another server;
- sends the tap URL with redirects turned off, takes the token from the `Location` header,
  and never fetches the order page (an order manifest that lists other cards);
- verifies the token against the published JWKS, and checks that its `open_id` matches the
  certificate's Open ID;
- reports `Printed ID` plus a note on how far it was verified.

The tap URL is single-use, so to look up again, rescan the card first.
