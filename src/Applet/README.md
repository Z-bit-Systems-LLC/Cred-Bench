# Cred-Bench JavaCard Applets

| Applet | CAP | Package AID | Applet AID | Source |
|---|---|---|---|---|
| PKOC | `build/pkoc.cap` | `A000000898000002` | `A000000898000001` | AsymCred `card/`, byte-identical |
| LEAF Verified emulation | `build/leaf.cap` | `F05A424C454146` | `F05A424C45414601` | AsymCred `card/` at `bcbcb94` (branch `feature/leaf-verified`) |

Both sources are copies of [AsymCred](https://github.com/Z-bit-Systems-LLC/AsymCred) `card/`,
kept here on purpose so Cred-Bench builds on its own. The applets are expected to stay static.
If one changes, change it in AsymCred too and record the new source commit above.

Both CAP files are committed and embedded in `Core.csproj`. A rebuild is not byte-identical
(CAP files embed build metadata), so only rebuild when the applet source changes.

# PKOC JavaCard Applet

Open-source JavaCard applet implementing the PSIA PKOC NFC Card 1.1 specification. Generates a P-256 key pair on-card (private key never leaves the card) and supports the SELECT/AUTHENTICATE APDU flow for PKOC credential presentation.

## Features

- **SELECT** (AID `A000000898000001`): Returns protocol version TLV (`5C 02 01 01`)
- **AUTHENTICATE** (CLA=80, INS=80): Signs a 16-byte transaction ID with ECDSA-SHA256, returns 65-byte uncompressed public key + 64-byte raw R||S signature
- On-card P-256 key generation at install time
- Forward-compatible TLV parsing (unknown tags silently ignored)

## Prerequisites

| Tool | Version | Location |
|---|---|---|
| JDK 8 | Temurin 8.0.482+ | `C:\Program Files\Eclipse Adoptium\jdk-8.0.482.8-hotspot` |
| Apache Ant | 1.10.15 | `C:\tools\apache-ant-1.10.15` |
| JavaCard SDK | 3.0.4 | `src/Applet/sdks/jc304_kit` (cloned from [oracle_javacard_sdks](https://github.com/martinpaljak/oracle_javacard_sdks)) |
| ant-javacard | v26.02.22 | `src/Applet/lib/ant-javacard.jar` |
| jCardSim | 3.0.5-SNAPSHOT | `src/Applet/lib/jcardsim-3.0.5-SNAPSHOT.jar` (built from [source](https://github.com/licel/jcardsim)) |

### First-time SDK setup

```bash
# Clone JavaCard SDKs (from src/Applet/ directory)
git clone --depth 1 https://github.com/martinpaljak/oracle_javacard_sdks.git sdks

# Download ant-javacard
curl -sL -o lib/ant-javacard.jar "https://github.com/martinpaljak/ant-javacard/releases/download/v26.02.22/ant-javacard.jar"

# jCardSim 3.0.5 must be built from source (2.2.2 on Maven Central lacks ECDSA-SHA256)
# See: https://github.com/licel/jcardsim
# Requires Maven + JC_CLASSIC_HOME pointed at sdks/jc305u3_kit
```

## Build

```bash
# Set environment (per shell session — does not modify system JAVA_HOME)
export JAVA_HOME="/c/Program Files/Eclipse Adoptium/jdk-8.0.482.8-hotspot"
export PATH="/c/tools/apache-ant-1.10.15/bin:$JAVA_HOME/bin:$PATH"

# Build .cap file
ant build
# Output: build/pkoc.cap

# Run tests (jCardSim, no physical hardware needed)
ant test

# Clean
ant clean
```

## Project Structure

```
src/Applet/
├── build.xml                                   # Ant build file (build + test targets)
├── src/com/zbitsystems/pkoc/
│   └── PkocApplet.java                        # PKOC applet implementation
├── test/com/zbitsystems/pkoc/
│   └── PkocAppletTest.java                    # jCardSim tests (8 tests)
├── lib/                                        # JARs (git-ignored)
│   ├── ant-javacard.jar
│   ├── jcardsim-3.0.5-SNAPSHOT.jar
│   ├── junit-4.13.2.jar
│   └── hamcrest-core-1.3.jar
├── sdks/                                       # JavaCard SDKs (git-ignored)
└── build/                                      # Output (git-ignored)
    └── pkoc.cap
```

## Test Coverage

| Test | Validates |
|---|---|
| testSelectReturnsProtocolVersion | SELECT response: `5C 02 01 01` |
| testAuthenticateReturnsPublicKeyAndSignature | Response structure: `5A 41 [65] 9E 40 [64]` |
| testSignatureIsVerifiable | ECDSA signature verifies against transaction ID |
| testConsistentPublicKeyAcrossAuthentications | Same key returned every time |
| testDifferentSignaturesForDifferentTransactions | Unique signatures per transaction |
| testUnsupportedInstructionReturnsError | SW `6D00` for unknown INS |
| testAuthenticateWithTruncatedDataReturnsError | Rejects short command data |
| testAuthenticateMissingTransactionIdReturnsError | Rejects missing `0x4C` TLV |

## Loading onto a Physical Card

Use [GlobalPlatformPro](https://github.com/martinpaljak/GlobalPlatformPro) to load the `.cap` onto a blank JavaCard:

```bash
# With default GP keys (404142...)
gp --install build/pkoc.cap

# Verify installation
gp --list
```

Target hardware should support JavaCard 3.0.4+, ECC P-256, and NFC (ISO 14443-A). Recommended: NXP JCOP4 or Infineon SLE97 series.

# LEAF Verified Emulation Applet

`com.zbitsystems.leaf.LeafOpenApplet` answers a LEAF Verified reader the way a MIFARE DUOX
carrying the LEAF Open Application does, so readers can be tested against cards issued under a
**test root**. Production DUOX personalisation is under NDA; this is a test credential, not a DUOX.

| Command | Behaviour |
|---|---|
| `90 5A` SelectApplication | `D6 1C F5` → `91 00` once locked; `000000` → `91 00`; anything else `91 A0` |
| `90 AD` ReadData | File 2 (the certificate) in 59-byte frames with `91 AF` |
| `90 AF` GetAdditionalFrame | Next frame |
| `00 88` ISOInternalAuthenticate | Signs `F0 F0 ‖ 80 00 ‖ RndB ‖ RndA` with the on-card P-256 key |

Personalisation, once, before LOCK (`00 A4 04 00 08 F05A424C45414601` first):

| Command | Purpose |
|---|---|
| `80 10 00 00 00` | GET PUBLIC KEY → `04 ‖ X ‖ Y` |
| `80 12 <offset> <Lc> <data>` | PUT CERTIFICATE chunk (P1P2 = byte offset) |
| `80 14 00 00 02 <length>` | LOCK: checks the DER length and that the certificate holds this card's key |

LEAF readers never send an ISO SELECT, so install it as the **default-selected** applet
(privilege `0x04`, CardReset). The jCardSim tests stay in AsymCred: they depend on its `leaf-tool` classes.

```bash
ant build-leaf
# Output: build/leaf.cap
```

### Installing on a card (tested 2026-11-05: NXP JCOP4, IC type D321, on an ACR122)

Cred-Bench's **Program Card…** dialog does all of this (install, personalise, read back): see
[docs/LEAF.md](../../docs/LEAF.md#programming-a-test-card). By hand:

GlobalPlatformPro 26.02.11 ignores `--default` and `--default-contactless` during `--install`:
its INSTALL [for install] carries privileges `000000`. Give the privilege explicitly:

```bash
gp --install build/leaf.cap --privs CardReset
gp --list    # APP F05A424C45414601 should show "Privs: CardReset"; the ISD loses CardReset
```

The tested card was installed with an additional GP Amendment C implicit-selection parameter
(`EF 03 CF 01 C0`, contact and contactless), sent by hand with `gp -s`. It was not re-tested
without it, so if `--privs CardReset` alone leaves native commands answering `6E00`, use:

```bash
gp --delete F05A424C45414601
gp -s 80E60C002707F05A424C45414608F05A424C4541460108F05A424C454146010304000007C900EF03CF01C00000
```

Then personalise with AsymCred's `leaf-tool` (`init-ca`, then `provision --ca <dir> --open-id <n>`).

**Testing on an ACR122:** the reader keeps the card powered between PC/SC connections, so
whichever applet GPPro or a tool last selected stays selected (`6E00` from the ISD looks like a
failed implicit selection). Lift the card, or turn the RF field off and on (`FF 00 00 00 04
D4 32 01 00`, then `... 01 01`), and wait a moment before the next read.

## License

Apache-2.0 — see [LICENSE](../../LICENSE)
