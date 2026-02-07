# General Card Information

## Overview

The General tab displays foundational information about the smart card and the reader connection that is common to all card technologies.

## Data Sources

### Reader Name

The PC/SC reader name as reported by the operating system. This is the identifier used to establish the connection.

Example: `ACS ACR122U PICC Interface 0`

### Card Protocol

The active communication protocol negotiated between the reader and card via PC/SC:

| Protocol         | Description                                             |
|------------------|---------------------------------------------------------|
| T=0 (Character)  | Character-oriented half-duplex protocol. Transmits data byte-by-byte. Used primarily with contact cards. |
| T=1 (Block)      | Block-oriented half-duplex protocol. Transmits data in blocks with error detection. Most common for contactless cards. |
| Raw              | Raw transmission mode without protocol framing.         |

Contactless cards (ISO 14443) typically negotiate **T=1**.

### ATR (Answer To Reset)

The ATR is the first data sent by the card when powered on. It contains information about the card's communication parameters, supported protocols, and optionally the card type.

The ATR is retrieved via the PC/SC `SCardGetAttrib` function with the `SCARD_ATTR_ATR_STRING` attribute.

**Format:** Space-separated hex bytes (e.g., `3B 8F 80 01 80 4F 0C A0 00 00 03 06 ...`)

### Display Fields

| Field    | Source              | Description                              |
|----------|---------------------|------------------------------------------|
| Reader   | Connection          | PC/SC reader name                        |
| Protocol | Connection          | Active card protocol (T=0, T=1, or Raw)  |
| ATR      | Card reset response | Answer To Reset hex string, copyable     |
