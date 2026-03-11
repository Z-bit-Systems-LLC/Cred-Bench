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

## License

Apache-2.0 — see [LICENSE](../../LICENSE)
