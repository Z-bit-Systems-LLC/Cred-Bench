# LEAF Verified in Cred-Bench — Implementation Plan

This plan adds two capabilities to Cred-Bench:

1. **Detection.** Read and verify LEAF Verified credentials: NXP MIFARE
   DUOX cards carrying the LEAF Open Application, and JavaCard cards
   running the emulation applet.
2. **Programming.** Load the LEAF Verified emulation applet onto a blank
   JavaCard, then personalise it with a certificate issued by a **test
   root** that Cred-Bench creates or loads.

The protocol work is already done and tested in
[AsymCred](https://github.com/Z-bit-Systems-LLC/AsymCred), on branch
`feature/leaf-verified` (commit `bcbcb94`). This plan ports that
behaviour into Cred-Bench's architecture. Where it says "as AsymCred
does", the reference implementations are:

| What | AsymCred file |
|---|---|
| Reader flow (C, normative) | `leaf/src/leaf.c`, `leaf/include/asymcred/asymcred_leaf.h` |
| Certificate profile (C) | `leaf/src/leaf_cert.c` |
| Reader flow (Java — the closest model for C#) | `card/tool/src/com/zbitsystems/leaf/tool/LeafReader.java` |
| Certificate profile (Java) | `card/tool/src/com/zbitsystems/leaf/tool/LeafCertificate.java` |
| Test root and card certificate issuance | `card/tool/src/com/zbitsystems/leaf/tool/TestCa.java` |
| Personalisation sequence | `card/tool/src/com/zbitsystems/leaf/tool/LeafProvisioner.java` |
| The applet | `card/src/com/zbitsystems/leaf/LeafOpenApplet.java` |
| Test vectors (openssl-generated) | `tests/support/leaf_vectors.h` |
| Pinned protocol facts and open questions | `CLAUDE.md`, section "LEAF Verified" |

---

## 1. Background: two LEAFs

Cred-Bench already has a `LEAFDetector`, but it detects **LEAF Universal**
(LEAF 4.0). That is a different and older credential. Keep both, and
keep them distinct in code, UI and docs.

| | LEAF Universal (existing) | LEAF Verified (new) |
|---|---|---|
| Cryptography | Symmetric, AES-128 | Asymmetric, ECC P-256 |
| Chip | DESFire EV2/EV3 | MIFARE DUOX |
| AIDs | `F51CD8`, `F51CD9`, `F51CDB` | `F51CD6` (the Open Application) |
| Selection | ISO SELECT (current detector) | DESFire native `SelectApplication` (`90 5A`) |
| What a reader can read | Presence only; files are access-controlled | An X.509 certificate (freely readable) plus a challenge signature |
| Credential | Inside protected files | **Open ID**: the certificate's subject serialNumber, a 38-bit value sent as **W40-2** |
| Trust | Shared AES keys | Certificate signed by the LEAF CA. The production root is NXP LEAF RootCAvE200 |

Production LEAF Verified cards come from Wavelynx. Their personalisation
uses NXP's DUOX protocol, which is under NDA, so Cred-Bench does **not**
program DUOX cards. It programs a JavaCard that *emulates* one.

---

## 2. Protocol reference

The bytes below are verified against the onboarding guide, NXP's
middleware and Proxmark3. All multi-byte DESFire fields are LSB first.

### Reader flow

| Step | C-APDU | Expected response |
|---|---|---|
| SelectApplication | `90 5A 00 00 03 D6 1C F5 00` | `91 00` (also accept `90 00`) |
| ReadData — header | `90 AD 00 00 07 02 000000 080000 00` (file 2, offset 0, length 8) | 8 bytes + `91 00` |
| ReadData — rest | `90 AD 00 00 07 02 <off:3> <len:3> 00`, len ≤ 255 | data + `91 00`, or a frame + `91 AF` |
| GetAdditionalFrame | `90 AF 00 00 00` | next frame + `91 AF` / `91 00` |
| ISOInternalAuthenticate | `00 88 00 00 16 80 00 7C 12 81 10 <RndA:16> 00` | `7C 54 81 10 <RndB:16> 82 40 <R‖S:64> 90 00` |

- Take the certificate's total length from its DER header, which is in
  the 8-byte read: `30 82 HH LL` means `4 + 0xHHLL` bytes in total.
- **`Lc` is `0x16`.** The onboarding guide's prose says `0x14`, but its
  own code and Proxmark3 send 22 data bytes. Use `0x16`.
- **The card signs** `F0 F0 ‖ 80 00 ‖ RndB ‖ RndA` (36 bytes) with
  ECDSA-P256-SHA256. RndA must come from `RandomNumberGenerator`.
- **The signature is raw R‖S, exactly 64 bytes.** Reject `82 44`, the
  padded form, as AsymCred does, until a real card is seen to send it.
- Error SWs worth naming in the UI:
  - `91 A0` application not found (not LEAF Verified);
  - `91 BE` boundary error;
  - `91 F0` file not found;
  - `91 AE` authentication error;
  - `91 CA` command aborted.

### Certificate profile

These are the rules AsymCred enforces. Cred-Bench should *check* the same
rules, but report any violations rather than stopping at the first one:

- signature algorithm `ecdsa-with-SHA256` (`1.2.840.10045.4.3.2`),
  identical inside and outside tbsCertificate;
- SubjectPublicKeyInfo `id-ecPublicKey` + `prime256v1`, uncompressed
  65-byte point;
- exactly one subject `serialNumber` (`2.5.4.5`), PrintableString or
  UTF8String, 1–12 decimal digits, ≤ 2^38 − 1. This is the **Open ID**;
- validity dates are **not** checked. Readers have no trusted clock, and
  test certificates use `99991231235959Z`.

### Production trust anchor

NXP LEAF RootCAvE200 (P-256, pathlen 0), as published in the onboarding
guide. It is issued by RootCAvE500 (P-521, self-signed).

```
042D2781BE41C22758A613810F67EC78DF1176C4765B212B49218C6C58408A5ADA
3D9973209D8228913A8816973CFE5C9EBFD8C6697532CDD5B53EE134D2F11B3C
```

### W40-2

Frame layout, numbering bit 0 as the first transmitted bit:

- bit 0: even parity over bits 1–19;
- bits 1–38: the Open ID, MSB first;
- bit 39: odd parity over bits 0–38.

As a 40-bit integer:
`(even << 39) | (openId << 1) | odd`, where
`even = popcount(openId >> 19) & 1` and
`odd = ((even + popcount(openId)) & 1) ^ 1`.

Use the onboarding guide's vectors as unit tests:

| Open ID | W40-2 (hex) |
|---|---|
| 160000000000 | `4A817C8000` |
| 1 | `0000000002` |
| 274877906943 (2^38 − 1) | `FFFFFFFFFE` |

---

## 3. Detection

### 3.1 Model

Add a new technology rather than widening `LEAFDetails`. The two LEAFs
share nothing but a name, and a card could carry both.

```csharp
// CardTechnology.cs
LEAFVerified = 32
```

```csharp
// Core/Models/TechnologyDetails/LEAFVerifiedDetails.cs
public enum LeafTrust { Verified, UntrustedIssuer, ProfileViolation, Unreadable }

public record LEAFVerifiedDetails
{
    public string? OpenId { get; init; }             // 12 digits, zero-padded
    public string? W40Hex { get; init; }             // 10 hex chars
    public string? W40Bits { get; init; }            // 40 chars '0'/'1'
    public string? CardPublicKeyHex { get; init; }   // 04||X||Y
    public string? Issuer { get; init; }             // RFC 2253
    public byte[]? Certificate { get; init; }        // DER, for export
    public LeafTrust Trust { get; init; }
    public string? TrustAnchorName { get; init; }    // "NXP LEAF RootCAvE200", or a test root name
    public bool? CardSignatureValid { get; init; }   // null = authenticate not attempted or refused
    public IReadOnlyList<string> ProfileIssues { get; init; } = [];
    public string? LastStatusWord { get; init; }     // for diagnostics

    public IReadOnlyList<(string Label, string Value)> GetFields() { /* same pattern as PKOCDetails */ }
}
```

Wire the model in everywhere the existing ones are:
- `DetectionResult.LEAFVerifiedDetails`;
- the `switch` in `CardDetectionService.DetectAsync`;
- `BuildGeneralDetails`, so the summary reads e.g. `"LEAF Verified"`.

### 3.2 Detector

`Core/Services/CardDetectors/LEAFVerifiedDetector.cs`. Model it on
`LeafReader.java`. Two dependencies are injected so it can be tested:

```csharp
public interface ILeafTrustStore
{
    // Built-in NXP E200 plus any test roots the user has added.
    IReadOnlyList<(string Name, ECDsa Key)> Anchors { get; }
}

public class LEAFVerifiedDetector(ILeafTrustStore trust, Func<byte[]>? challenge = null) : ICardDetector
{
    public CardTechnology Technology => CardTechnology.LEAFVerified;
    private readonly Func<byte[]> _challenge = challenge ?? (() => RandomNumberGenerator.GetBytes(16));
    ...
}
```

Algorithm:

1. Send `SelectApplication`. If the response is not `91 00`/`90 00`,
   return `(false, null, null)`.
2. Read the 8-byte header, then the rest in ≤255-byte requests. Follow
   `91 AF` with `90 AF 00 00 00`. Fail on over-delivery, short delivery,
   or a declared length above 1024.
3. Parse and check the profile (§3.3). Then try each anchor in order and
   record the first that verifies.
4. **Always** send ISOInternalAuthenticate with a fresh RndA, even when
   the issuer is untrusted. Verify it with the *certificate's* key and set
   `CardSignatureValid`. (AsymCred withholds authentication from
   untrusted cards because it makes access decisions. Cred-Bench is a
   diagnostic bench, so it reports everything and labels it; see §3.4.)
5. Return `(true, "LEAF Verified (Open ID …)", details)` whenever
   SelectApplication succeeded. Partial failures go into the details,
   not into a "not detected" result.

Signature verification is native .NET, with no library needed:

```csharp
// Card challenge: raw R||S
using var cardKey = ECDsa.Create(new ECParameters
{
    Curve = ECCurve.NamedCurves.nistP256,
    Q = new ECPoint { X = point[1..33], Y = point[33..65] },
});
byte[] message = [0xF0, 0xF0, 0x80, 0x00, .. rndB, .. rndA];
bool ok = cardKey.VerifyData(message, rawSig, HashAlgorithmName.SHA256,
                             DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
```

### 3.3 Certificate parsing

Use `X509Certificate2` for the fields and `System.Formats.Asn1` to get
the exact signed bytes:

```csharp
var x509 = X509CertificateLoader.LoadCertificate(der);   // .NET 9+

// tbsCertificate and signature, byte-exact
var outer = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
ReadOnlyMemory<byte> tbs = outer.ReadEncodedValue();
outer.ReadSequence();                                  // signatureAlgorithm
byte[] sigDer = outer.ReadBitString(out _);

bool signedBy(ECDsa anchor) => anchor.VerifyData(tbs.Span, sigDer,
    HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

// Open ID
var serial = x509.SubjectName.EnumerateRelativeDistinguishedNames()
    .Where(r => r.GetSingleElementType().Value == "2.5.4.5")
    .Select(r => r.GetSingleElementValue())
    .ToList();                                         // must be exactly one
```

Do the profile checks explicitly:
- `x509.SignatureAlgorithm.Value`;
- the key algorithm and curve OIDs from `x509.PublicKey`;
- the digit and range checks on the serial number.

Add any violation to `ProfileIssues`. A parse exception means
`Trust = Unreadable`; keep the raw bytes so they can be exported.

### 3.4 Trust display

The LEAF Verified tab should make the state unmistakable:

| State | Show |
|---|---|
| `Verified` + `CardSignatureValid == true` | ✅ "Verified — <anchor name>". Open ID and W40 prominent |
| `UntrustedIssuer` | ⚠ "Certificate not signed by a trusted root (issuer: …)". Show the Open ID, labelled *unverified* |
| `CardSignatureValid == false` | ❌ "Card failed the challenge — possible clone". Show the Open ID, labelled *unverified* |
| `ProfileViolation` | ⚠ list the `ProfileIssues` |

Add an **Export certificate** button that saves a `.crt` (PEM). That is
the quickest way to answer the open questions in §7 from a Wavelynx
card.

### 3.5 Detector order

`CardDetectionService` runs every detector on one connection, and the
order matters on JavaCard. A successful ISO SELECT of another applet,
such as PKOC, makes that applet current. The `90 5A` that follows then
goes to the PKOC applet, and an emulated LEAF card looks absent until
the next card reset. Unmatched SELECTs (PIV, DESFire AIDs) are harmless:
JavaCard forwards them to the current applet, which answers `6D 00`.

**Register `LEAFVerifiedDetector` before `PKOCDetector`** in both places.
Today they disagree with each other:

- `src/UI/Windows/App.xaml.cs`: PIV, DESFire, ISO14443, PKOC, LEAF.
- `src/UI/CLI/Program.cs`: ISO14443, PIV, DESFire, PKOC, LEAF.

On a real DUOX the order does not matter: `90 5A` works at any time.

### 3.6 Related detector touch-ups

- `DESFireDetector` will also fire on a DUOX, via GetVersion. Add DUOX
  to its type naming **only after** recording a real card's GetVersion
  bytes. Do not guess the hardware type value.
- Rename the existing LEAF display strings to say **LEAF Universal**.
- Update `docs/LEAF.md` with a LEAF Verified section; this plan can
  become the basis for it.

### 3.7 Trust store

`LeafTrustStore : ILeafTrustStore`:
- The built-in anchor is E200, from the hex above.
- The user can add **test roots** by loading a `ca.crt`. Accept PEM or
  DER, and files from either Cred-Bench or AsymCred's `leaf-tool`.
- Persist the list of added roots in the app's settings mechanism.
- Show anchor names in the UI.
- Never ship a test root built in.

### 3.8 UI and CLI

Follow the "Adding New Card Technologies" checklist in `CLAUDE.md`:
- `LeafVerifiedDetailsControl` (XAML), with its DataTemplate and TabItem;
- `Resources.resx` strings under a `LEAFV_` prefix;
- `ResultFormatter` in the CLI;
- detector registration in both places, in the order from §3.5.

The CLI also needs a way to trust a test root, e.g.
`--leaf-anchor <ca.crt>` (repeatable).

---

## 4. Programming a test card

### 4.1 Flow

```
SCP02 (key discovery, as PKOC)
  → DELETE applet F05A424C45414601, package F05A424C454146   (only ours, never PKOC's)
  → INSTALL [for load] / LOAD leaf.cap / INSTALL [for install], privilege Default Selected
  → close the SCP02 session, ISO SELECT 00 A4 04 00 08 F05A424C45414601
  → GET PUBLIC KEY            80 10 00 00 00           → 04||X||Y  9000
  → issue certificate (test root, Open ID)            — host side
  → PUT CERTIFICATE ×n        80 12 <off hi> <off lo> <Lc ≤128> <chunk>
  → LOCK                      80 14 00 00 02 <len hi> <len lo>
  → reset the card, run LEAFVerifiedDetector with the test root trusted — must report Verified
```

| Personalisation SW | Meaning |
|---|---|
| `90 00` | OK |
| `69 84` | LOCK refused: DER length mismatch, or the certificate does not contain this card's key |
| `69 85` | Already locked (personalisation closed for good) |
| `6A 84` | PUT past 1024 bytes |

Before LOCK the card answers `90 5A … D6 1C F5` with `91 A0`, so a
half-programmed card detects as "not LEAF" rather than as broken.

### 4.2 `AppletLoader` change

`SendInstallForInstall` hard-codes privileges `0x00`. Add a parameter
and leave PKOC's behaviour unchanged:

```csharp
public void LoadAndInstall(..., byte privileges = 0x00, IProgress<...>? progress = null)
```

LEAF needs **`0x04` (Card Reset / Default Selected)**, because LEAF
readers never send an ISO SELECT. Things to verify on the target card
(JCOP4 or similar) and record in this document:

- Only one application can hold the privilege. If another one does,
  expect INSTALL to fail, typically with `6A 80`. Surface that as "another
  applet is already the default".
- GlobalPlatform 2.2+ cards can configure implicit selection on the
  **contactless** interface separately. If the card powers up with the
  ISD selected on contactless after install, the install parameters
  need the contactless implicit-selection setting. Confirm empirically:
  after programming, a fresh tap must answer `90 5A … D6 1C F5` with
  `91 00` without any SELECT.

### 4.3 `LeafCardProgrammer`

`Core/Services/Leaf/LeafCardProgrammer.cs`, parallel to
`PkocCardProgrammer`. Move `EstablishWithKeyDiscovery` out of the PKOC
programmer into a shared helper (e.g. `GpSessionFactory`) rather than
copying it.

```csharp
public LeafProgrammingResult ProgramCard(
    ICardConnection connection, byte[] capFileData, LeafTestCa ca, long openId,
    byte[]? gpKey = null, IProgress<(int, int)>? progress = null, IProgress<string>? status = null);

public record LeafProgrammingResult(long OpenId, byte[] CardPublicKey, byte[] Certificate);
```

### 4.4 Test root (`LeafTestCa`)

`Core/Services/Leaf/LeafTestCa.cs`: create, save, load and issue.
**Use the same file format as AsymCred's `leaf-tool`**, so one test root
serves both tools:

- `ca.key`: PKCS#8, PEM `-----BEGIN PRIVATE KEY-----`, unencrypted.
  Use `ECDsa.ImportFromPem` / `ExportPkcs8PrivateKeyPem`.
- `ca.crt`: PEM certificate.
- Refuse to overwrite an existing pair.

Root certificate:
- self-signed P-256, ecdsa-with-SHA256;
- subject `O=Z-bit Systems, CN=<name>`;
- BasicConstraints critical, CA:TRUE, pathlen 0;
- KeyUsage critical, keyCertSign | cRLSign;
- SubjectKeyIdentifier.

Card certificate. AsymCred's parser accepts exactly this:

```csharp
var subject = new X500DistinguishedNameBuilder();
subject.Add("2.5.4.5", openId.ToString("D12"), UniversalTagNumber.PrintableString);

var req = new CertificateRequest(subject.Build(),
    new PublicKey(cardKey),             // ECDsa holding only the card's public point
    HashAlgorithmName.SHA256);
req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(caCert, true, false));

byte[] serial = RandomNumberGenerator.GetBytes(16); serial[0] &= 0x7F; serial[0] |= 0x40;
using var cert = req.Create(caCertWithKey, DateTimeOffset.UtcNow.AddDays(-1), caCert.NotAfter, serial);
```

Two .NET-specific pitfalls:

- **`CertificateRequest.Create` refuses a `notAfter` later than the
  issuer's.** `leaf-tool` gives card certificates `99991231235959Z`
  under a 25-year root, which .NET will not do. Use `caCert.NotAfter`
  for card certificates; nothing checks validity anyway (§2).
- The signature algorithm must come out as `ecdsa-with-SHA256` with
  **no** parameters, and the key as named-curve `prime256v1`. .NET does
  both by default. A unit test should still assert it, by running the
  §3.3 profile check on every issued certificate.

Open ID input: 0 to 274877906943. Offer "Random" (12 digits, in
range) next to a text box. Show the resulting W40 before programming.

### 4.5 UI

A **Program LEAF Verified card** section on the LEAF Verified tab,
mirroring the PKOC tab's programming section (`PkocViewModel`):
- GP key (default or custom);
- test root: Create… / Load… / show its name and key;
- Open ID with a Random button;
- Program, with progress and status;
- the result: Open ID, W40, and "Verified ✓" from the read-back.

Put the ViewModel in `Core/ViewModels/LeafVerifiedViewModel.cs`, keep
its strings in resx, and register it in `App.xaml.cs`.

### 4.6 PKOC and LEAF on one card

Both applets fit: about 12 KB and 17 KB as CAP files, a few KB once
loaded. Each programmer deletes only its own AIDs. With both installed,
LEAF must be the default applet. PKOC readers select by AID, so PKOC
still works. A reader that tries PKOC *before* LEAF in the same field
session will miss LEAF (see §3.5), so warn about that in the UI when
both are present.

---

## 5. Getting `leaf.cap` into Cred-Bench — decision needed

> **Decided (2026-11-05):** same as PKOC. The applet source is copied to
> `src/Applet/src/com/zbitsystems/leaf/` (from AsymCred `bcbcb94`), `ant build-leaf` builds it,
> and `src/Applet/build/leaf.cap` is committed and embedded in `Core.csproj`. The copies are
> expected to stay static.

`PkocViewModel` loads `pkoc.cap` from an embedded resource that is
linked from `src/Applet/build/pkoc.cap` and committed. AsymCred does
**not** commit `leaf.cap`. Choose one:

| Option | How | Trade-off |
|---|---|---|
| **A. Vendor the artifact** (recommended) | Build `leaf.cap` in AsymCred, commit it as `src/Applet/build/leaf.cap`, add `!build/leaf.cap` to `src/Applet/.gitignore`, embed it like `pkoc.cap`, and record the AsymCred commit it came from | Simple and reproducible. Must be refreshed by hand when the applet changes |
| B. Vendor the source | Copy `com/zbitsystems/leaf/` into `src/Applet/src/` and add the `build-leaf` target, as was done for PKOC | Applet source lives in two repos and can drift, as PKOC already can |
| C. Cross-repo link | `EmbeddedResource Include="..\..\..\AsymCred\card\build\leaf.cap"` | Breaks any checkout without AsymCred beside it, and breaks CI |

The applet is Apache-2.0 in AsymCred, and Cred-Bench may embed it under
those terms. Keep its `LICENSE-APACHE-2.0.txt` notice beside the vendored
file.

---

## 6. Tests (`test/Core.Tests`)

Port the byte arrays from AsymCred `tests/support/leaf_vectors.h` into a
`LeafVectors.cs`. They hold a real certificate, real signatures and
fixed randoms, so .NET's own ECDSA verifies them end to end, with no
stubs.

The vectors use `RndA = A0..AF` and `RndB = B0..BF`. Inject
`challenge: () => RndA` to reproduce `LEAF_CARD_SIG`.

**`LEAFVerifiedDetectorTests`.** A mock `ICardConnection` scripts a
DESFire-style card that splits reads into 59-byte frames with `91 AF`.
- C-APDUs byte for byte, including `Lc = 0x16` on authenticate;
- happy path: Open ID `160000000000`, W40 `4A817C8000`, Verified, card
  signature valid;
- read sizes: header 8, then 255, then 144, for the 407-byte certificate;
- `91 A0` on select → not detected;
- untrusted anchor → detected, `UntrustedIssuer`, authenticate still sent;
- second anchor matches → `TrustAnchorName` is the second;
- flipped signature byte → `CardSignatureValid == false`;
- `82 44` signature, or missing `82` → reported as a malformed response;
- over-delivery, short read, `91 BE` mid-read;
- certificate with a non-digit or out-of-range serialNumber →
  `ProfileViolation`.

**`LEAFVerifiedDetailsTests`.** The three W40 vectors from §2, and
`GetFields()`.

**`LeafTestCaTests`.**
- create → save → load round trip, and refusal to overwrite;
- issued certificate passes the profile check;
- `signedBy(ca)` is true;
- Open ID 2^38 is refused;
- `notAfter` ≤ the CA's;
- a `ca.crt`/`ca.key` pair made by `leaf-tool init-ca` loads. Commit a
  small fixture pair under `test/`.

**`LeafCardProgrammerTests`.** A mock connection.
- DELETE targets only the LEAF AIDs;
- INSTALL [for install] carries privilege `0x04`;
- personalisation order: SELECT, GET KEY, PUT ×n with correct offsets, LOCK;
- `69 84` and `69 85` map to clear errors.

**`AppletLoaderTests`.** The privileges byte appears in the INSTALL
data, and the default stays `0x00` (a PKOC regression guard).

Cross-check by hand against AsymCred:

```bash
# card programmed by Cred-Bench, read by AsymCred's tool
java -jar leaf-tool.jar read --anchor <cred-bench ca.crt> --trace
# card programmed by leaf-tool, read by Cred-Bench with that ca.crt trusted
```

---

## 7. Open questions — settle on the first Wavelynx card

These are the same as AsymCred's (`CLAUDE.md`, "Open until a Wavelynx
card is read"). Use Cred-Bench's export and trace to answer them:

1. Is the card certificate signed **directly** by RootCAvE200, or is there
   an intermediate? A community note reports the guide's key failing
   where the on-card certificate's key worked.
2. Is file 2 plain DER at offset 0, or padded or length-prefixed?
3. Does any card return the 68-byte (`82 44`) signature form?
4. What GetVersion bytes does a DUOX return (for §3.6)?

Also record, from the first JavaCard programmed:

5. Does `0x04` alone make the applet implicitly selected on the
   contactless interface (§4.2)?

---

## 8. Order of work

> **Status (2026-11-05):** steps 1–8 done. A Wavelynx card was read (step 5); `leaf.cap` is
> vendored like PKOC (step 6); programming is in Core (step 7) and in a combined **Program Card**
> dialog for PKOC and LEAF rather than a section on the LEAF tab (step 8). Verified on a JCOP4
> card: PKOC then LEAF in one run, LEAF found without SELECT after a fresh reset, and test roots
> interchangeable with `leaf-tool`. §7 question 5: Card Reset plus `EF 03 CF 01 C0` works on
> JCOP4; Card Reset alone was not tested. User docs: [LEAF.md](LEAF.md#programming-a-test-card).

1. `CardTechnology.LEAFVerified`, `LEAFVerifiedDetails` with W40, and its
   tests.
2. `LeafTrustStore`, the certificate profile parser, and
   `LEAFVerifiedDetector`, with vector tests.
3. Wiring: `CardDetectionService`, `DetectionResult`, both detector
   registrations (LEAF Verified before PKOC), CLI formatter and
   `--leaf-anchor`.
4. WPF tab: details, trust states, certificate export.
5. *Milestone: read a Wavelynx card; answer §7 questions 1–4.*
6. Decide §5 and bring in `leaf.cap`.
7. `AppletLoader` privileges, the shared GP session helper,
   `LeafTestCa`, `LeafCardProgrammer`, and their tests.
8. Programming UI; program a JavaCard; answer §7 question 5;
   cross-check with `leaf-tool`.
9. Docs: `docs/LEAF.md` (both generations), and the `CLAUDE.md` tables
   for the new detail control and technology.
