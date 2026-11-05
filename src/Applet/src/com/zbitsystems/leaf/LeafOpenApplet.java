/*
 * LEAF Verified Open Application - JavaCard emulation for test cards
 *
 * Answers the reader side of the LEAF Verified Open Application exactly as
 * the LEAF Community onboarding guide describes a MIFARE DUOX card doing:
 *   - SelectApplication (90 5A, AID D6 1C F5)            -> 91 00
 *   - ReadData file 2  (90 AD ... )                      -> certificate, 91 AF / 91 00
 *   - GetAdditionalFrame (90 AF)                         -> next frame
 *   - ISOInternalAuthenticate (00 88, key 0)             -> 7C 54 81 10 RndB 82 40 R||S
 *     signing F0 F0 || OptsA TLV || RndB || RndA with ECDSA-SHA256
 *
 * The P-256 key pair is generated on-card at install; the private key never
 * leaves the card. Before use, the card is personalised once with a
 * certificate for that key, signed by a test CA (see tool/), and locked:
 *   - GET PUBLIC KEY  80 10 00 00 00         -> 65-byte uncompressed point
 *   - PUT CERTIFICATE 80 12 <offset> Lc data    (P1P2 = byte offset)
 *   - LOCK            80 14 00 00 02 <len>      validates, then disables
 *                                               all three commands
 *
 * This is a test credential, not a DUOX: no DESFire file system, no AES
 * secure messaging, no originality certificate. A reader that identifies
 * cards by ATS or GetVersion before selecting will tell the difference.
 *
 * LEAF readers never send an ISO SELECT, so this applet must be the
 * card's implicitly selected (default) applet on the contactless
 * interface - see card/README.md.
 *
 * SPDX-License-Identifier: Apache-2.0
 * Copyright (c) Z-bit Systems, LLC
 */
package com.zbitsystems.leaf;

import javacard.framework.*;
import javacard.security.*;

public class LeafOpenApplet extends Applet {

    // DESFire native commands, ISO 7816-4 wrapped
    private static final byte CLA_NATIVE = (byte) 0x90;
    private static final byte INS_SELECT_APPLICATION = (byte) 0x5A;
    private static final byte INS_READ_DATA = (byte) 0xAD;
    private static final byte INS_ADDITIONAL_FRAME = (byte) 0xAF;

    // ISO 7816-4
    private static final byte CLA_ISO = (byte) 0x00;
    private static final byte INS_INTERNAL_AUTHENTICATE = (byte) 0x88;

    // Personalisation, available until LOCK
    private static final byte CLA_PERSO = (byte) 0x80;
    private static final byte INS_GET_PUBLIC_KEY = (byte) 0x10;
    private static final byte INS_PUT_CERTIFICATE = (byte) 0x12;
    private static final byte INS_LOCK = (byte) 0x14;

    // DESFire status words
    private static final short SW_DF_OK = (short) 0x9100;
    private static final short SW_DF_ADDITIONAL_FRAME = (short) 0x91AF;
    private static final short SW_DF_ILLEGAL_COMMAND = (short) 0x911C;
    private static final short SW_DF_LENGTH_ERROR = (short) 0x917E;
    private static final short SW_DF_APPLICATION_NOT_FOUND = (short) 0x91A0;
    private static final short SW_DF_BOUNDARY_ERROR = (short) 0x91BE;
    private static final short SW_DF_COMMAND_ABORTED = (short) 0x91CA;
    private static final short SW_DF_FILE_NOT_FOUND = (short) 0x91F0;

    // The Open Application, AID 0xF51CD6 least significant byte first
    private static final byte[] LEAF_OPEN_AID = { (byte) 0xD6, (byte) 0x1C, (byte) 0xF5 };
    private static final byte CERT_FILE = (byte) 0x02;
    private static final byte KEY_NO = (byte) 0x00;

    // Internal Authenticate TLVs
    private static final byte TAG_OPTS_A = (byte) 0x80;
    private static final byte TAG_AUTH_DATA = (byte) 0x7C;
    private static final byte TAG_RND = (byte) 0x81;
    private static final byte TAG_SIGNATURE = (byte) 0x82;

    private static final short LEN_RND = (short) 16;
    private static final short LEN_PUBLIC_KEY = (short) 65;
    private static final short LEN_RAW_SIGNATURE = (short) 64;
    private static final short LEN_SCALAR = (short) 32;
    private static final short MAX_DER_SIG_LEN = (short) 72;
    private static final short MAX_OPTS_A = (short) 32;

    // Prefix the card signs ahead of the OptsA TLV
    private static final byte MSG_PREFIX = (byte) 0xF0;

    // Certificate file capacity, and the most ReadData returns per frame
    // before answering 91 AF - a classic DESFire frame, so readers exercise
    // their continuation path.
    private static final short MAX_CERT = (short) 1024;
    private static final short FRAME = (short) 59;

    // Smallest certificate LOCK accepts (a reader reads 8 header bytes)
    private static final short MIN_CERT = (short) 8;

    // Transient state indices
    private static final short ST_APP_SELECTED = (short) 0;
    private static final short RD_OFFSET = (short) 0;
    private static final short RD_REMAINING = (short) 1;

    private final ECPrivateKey privateKey;
    private final byte[] publicKeyBytes;
    private final Signature ecdsaSha256;
    private final RandomData random;

    private final byte[] certificate;
    private short certificateLength;
    private boolean locked;

    private final byte[] state;       // CLEAR_ON_DESELECT
    private final short[] pendingRead; // CLEAR_ON_DESELECT
    private final byte[] work;         // CLEAR_ON_DESELECT

    // NIST P-256 curve parameters
    private static final byte[] P256_P = {
        (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x01,
        (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00,
        (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF,
        (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF
    };

    private static final byte[] P256_A = {
        (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x01,
        (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00,
        (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF,
        (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFC
    };

    private static final byte[] P256_B = {
        (byte) 0x5A, (byte) 0xC6, (byte) 0x35, (byte) 0xD8, (byte) 0xAA, (byte) 0x3A, (byte) 0x93, (byte) 0xE7,
        (byte) 0xB3, (byte) 0xEB, (byte) 0xBD, (byte) 0x55, (byte) 0x76, (byte) 0x98, (byte) 0x86, (byte) 0xBC,
        (byte) 0x65, (byte) 0x1D, (byte) 0x06, (byte) 0xB0, (byte) 0xCC, (byte) 0x53, (byte) 0xB0, (byte) 0xF6,
        (byte) 0x3B, (byte) 0xCE, (byte) 0x3C, (byte) 0x3E, (byte) 0x27, (byte) 0xD2, (byte) 0x60, (byte) 0x4B
    };

    private static final byte[] P256_G = {
        (byte) 0x04,
        (byte) 0x6B, (byte) 0x17, (byte) 0xD1, (byte) 0xF2, (byte) 0xE1, (byte) 0x2C, (byte) 0x42, (byte) 0x47,
        (byte) 0xF8, (byte) 0xBC, (byte) 0xE6, (byte) 0xE5, (byte) 0x63, (byte) 0xA4, (byte) 0x40, (byte) 0xF2,
        (byte) 0x77, (byte) 0x03, (byte) 0x7D, (byte) 0x81, (byte) 0x2D, (byte) 0xEB, (byte) 0x33, (byte) 0xA0,
        (byte) 0xF4, (byte) 0xA1, (byte) 0x39, (byte) 0x45, (byte) 0xD8, (byte) 0x98, (byte) 0xC2, (byte) 0x96,
        (byte) 0x4F, (byte) 0xE3, (byte) 0x42, (byte) 0xE2, (byte) 0xFE, (byte) 0x1A, (byte) 0x7F, (byte) 0x9B,
        (byte) 0x8E, (byte) 0xE7, (byte) 0xEB, (byte) 0x4A, (byte) 0x7C, (byte) 0x0F, (byte) 0x9E, (byte) 0x16,
        (byte) 0x2B, (byte) 0xCE, (byte) 0x33, (byte) 0x57, (byte) 0x6B, (byte) 0x31, (byte) 0x5E, (byte) 0xCE,
        (byte) 0xCB, (byte) 0xB6, (byte) 0x40, (byte) 0x68, (byte) 0x37, (byte) 0xBF, (byte) 0x51, (byte) 0xF5
    };

    private static final byte[] P256_N = {
        (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0x00, (byte) 0x00, (byte) 0x00, (byte) 0x00,
        (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF,
        (byte) 0xBC, (byte) 0xE6, (byte) 0xFA, (byte) 0xAD, (byte) 0xA7, (byte) 0x17, (byte) 0x9E, (byte) 0x84,
        (byte) 0xF3, (byte) 0xB9, (byte) 0xCA, (byte) 0xC2, (byte) 0xFC, (byte) 0x63, (byte) 0x25, (byte) 0x51
    };

    private LeafOpenApplet() {
        KeyPair keyPair = new KeyPair(KeyPair.ALG_EC_FP, KeyBuilder.LENGTH_EC_FP_256);
        privateKey = (ECPrivateKey) keyPair.getPrivate();
        ECPublicKey publicKey = (ECPublicKey) keyPair.getPublic();

        setP256Parameters(privateKey);
        setP256Parameters(publicKey);

        // Generate key pair - private key never leaves the card
        keyPair.genKeyPair();

        publicKeyBytes = new byte[LEN_PUBLIC_KEY];
        publicKey.getW(publicKeyBytes, (short) 0);

        ecdsaSha256 = Signature.getInstance(Signature.ALG_ECDSA_SHA_256, false);
        ecdsaSha256.init(privateKey, Signature.MODE_SIGN);

        random = RandomData.getInstance(RandomData.ALG_SECURE_RANDOM);

        certificate = new byte[MAX_CERT];

        state = JCSystem.makeTransientByteArray((short) 1, JCSystem.CLEAR_ON_DESELECT);
        pendingRead = JCSystem.makeTransientShortArray((short) 2, JCSystem.CLEAR_ON_DESELECT);

        // Signed message (2 + OptsA TLV + RndB + RndA), then the DER
        // signature, share one transient buffer.
        work = JCSystem.makeTransientByteArray(
            (short) (2 + 2 + MAX_OPTS_A + LEN_RND + LEN_RND + MAX_DER_SIG_LEN),
            JCSystem.CLEAR_ON_DESELECT);
    }

    public static void install(byte[] bArray, short bOffset, byte bLength) {
        LeafOpenApplet applet = new LeafOpenApplet();
        if (bLength > 0) {
            applet.register(bArray, (short) (bOffset + 1), bArray[bOffset]);
        } else {
            applet.register();
        }
    }

    public boolean select() {
        return true;
    }

    public void process(APDU apdu) {
        if (selectingApplet()) {
            return; // ISO SELECT by this applet's own AID: personalisation
        }

        byte[] buffer = apdu.getBuffer();
        byte cla = buffer[ISO7816.OFFSET_CLA];
        byte ins = buffer[ISO7816.OFFSET_INS];

        // Any command but GetAdditionalFrame abandons a read in progress,
        // as on a DESFire card.
        if (!(cla == CLA_NATIVE && ins == INS_ADDITIONAL_FRAME)) {
            pendingRead[RD_REMAINING] = (short) 0;
        }

        if (cla == CLA_NATIVE) {
            switch (ins) {
                case INS_SELECT_APPLICATION:
                    selectApplication(apdu);
                    return;
                case INS_READ_DATA:
                    readData(apdu);
                    return;
                case INS_ADDITIONAL_FRAME:
                    additionalFrame(apdu);
                    return;
                default:
                    ISOException.throwIt(SW_DF_ILLEGAL_COMMAND);
            }
        }

        if (cla == CLA_ISO && ins == INS_INTERNAL_AUTHENTICATE) {
            internalAuthenticate(apdu);
            return;
        }

        if (cla == CLA_PERSO) {
            if (locked) {
                ISOException.throwIt(ISO7816.SW_CONDITIONS_NOT_SATISFIED);
            }
            switch (ins) {
                case INS_GET_PUBLIC_KEY:
                    getPublicKey(apdu);
                    return;
                case INS_PUT_CERTIFICATE:
                    putCertificate(apdu);
                    return;
                case INS_LOCK:
                    lock(apdu);
                    return;
                default:
                    ISOException.throwIt(ISO7816.SW_INS_NOT_SUPPORTED);
            }
        }

        ISOException.throwIt(ISO7816.SW_INS_NOT_SUPPORTED);
    }

    // ---- DESFire native ----------------------------------------------------

    /**
     * SelectApplication: 90 5A 00 00 03 [AID LSB first] 00.
     * The Open Application exists only once the card is locked; AID 000000
     * is the PICC level, which holds nothing a reader can use.
     */
    private void selectApplication(APDU apdu) {
        byte[] buffer = apdu.getBuffer();
        short len = apdu.setIncomingAndReceive();

        state[ST_APP_SELECTED] = (byte) 0;

        if (len != (short) 3 || apdu.getIncomingLength() != (short) 3) {
            ISOException.throwIt(SW_DF_LENGTH_ERROR);
        }

        short off = apdu.getOffsetCdata();
        if (locked && Util.arrayCompare(buffer, off, LEAF_OPEN_AID, (short) 0, (short) 3) == 0) {
            state[ST_APP_SELECTED] = (byte) 1;
            ISOException.throwIt(SW_DF_OK);
        }
        if (buffer[off] == 0 && buffer[(short) (off + 1)] == 0 && buffer[(short) (off + 2)] == 0) {
            ISOException.throwIt(SW_DF_OK);
        }

        ISOException.throwIt(SW_DF_APPLICATION_NOT_FOUND);
    }

    /**
     * ReadData: 90 AD 00 00 07 [file] [offset 3, LSB first] [length 3] 00.
     * Length 0 reads from the offset to the end of the file. Output over
     * FRAME bytes continues through GetAdditionalFrame.
     */
    private void readData(APDU apdu) {
        byte[] buffer = apdu.getBuffer();
        short len = apdu.setIncomingAndReceive();

        if (len != (short) 7 || apdu.getIncomingLength() != (short) 7) {
            ISOException.throwIt(SW_DF_LENGTH_ERROR);
        }

        short off = apdu.getOffsetCdata();
        if (state[ST_APP_SELECTED] == 0 || buffer[off] != CERT_FILE) {
            ISOException.throwIt(SW_DF_FILE_NOT_FOUND);
        }

        // The file is at most 1 KiB, so a third byte is always out of range.
        if (buffer[(short) (off + 3)] != 0 || buffer[(short) (off + 6)] != 0) {
            ISOException.throwIt(SW_DF_BOUNDARY_ERROR);
        }
        short offset = Util.makeShort(buffer[(short) (off + 2)], buffer[(short) (off + 1)]);
        short length = Util.makeShort(buffer[(short) (off + 5)], buffer[(short) (off + 4)]);

        if (offset < 0 || offset > certificateLength || length < 0) {
            ISOException.throwIt(SW_DF_BOUNDARY_ERROR);
        }
        if (length == 0) {
            length = (short) (certificateLength - offset);
        }
        if (length > (short) (certificateLength - offset)) {
            ISOException.throwIt(SW_DF_BOUNDARY_ERROR);
        }

        pendingRead[RD_OFFSET] = offset;
        pendingRead[RD_REMAINING] = length;
        sendFrame(apdu);
    }

    /** GetAdditionalFrame: 90 AF 00 00 00. */
    private void additionalFrame(APDU apdu) {
        if (pendingRead[RD_REMAINING] == 0) {
            ISOException.throwIt(SW_DF_COMMAND_ABORTED);
        }
        sendFrame(apdu);
    }

    private void sendFrame(APDU apdu) {
        short n = pendingRead[RD_REMAINING];
        if (n > FRAME) {
            n = FRAME;
        }

        short from = pendingRead[RD_OFFSET];
        pendingRead[RD_OFFSET] = (short) (from + n);
        pendingRead[RD_REMAINING] = (short) (pendingRead[RD_REMAINING] - n);

        if (n > 0) {
            Util.arrayCopyNonAtomic(certificate, from, apdu.getBuffer(), (short) 0, n);
            apdu.setOutgoingAndSend((short) 0, n);
        }

        ISOException.throwIt(pendingRead[RD_REMAINING] == 0 ? SW_DF_OK : SW_DF_ADDITIONAL_FRAME);
    }

    // ---- ISO Internal Authenticate -------------------------------------------

    /**
     * 00 88 00 [key] Lc  80 L [OptsA]  7C 12 81 10 [RndA]  00
     *   -> 7C 54 81 10 [RndB] 82 40 [R||S] 90 00
     * signing F0 F0 || 80 L [OptsA] || RndB || RndA.
     */
    private void internalAuthenticate(APDU apdu) {
        byte[] buffer = apdu.getBuffer();

        if (state[ST_APP_SELECTED] == 0) {
            ISOException.throwIt(ISO7816.SW_CONDITIONS_NOT_SATISFIED);
        }
        if (buffer[ISO7816.OFFSET_P1] != 0) {
            ISOException.throwIt(ISO7816.SW_INCORRECT_P1P2);
        }
        if (buffer[ISO7816.OFFSET_P2] != KEY_NO) {
            ISOException.throwIt(ISO7816.SW_RECORD_NOT_FOUND);
        }

        short len = apdu.setIncomingAndReceive();
        if (len != apdu.getIncomingLength()) {
            ISOException.throwIt(ISO7816.SW_WRONG_LENGTH);
        }
        short pos = apdu.getOffsetCdata();
        short end = (short) (pos + len);

        // OptsA TLV, signed exactly as received
        if ((short) (end - pos) < 2 || buffer[pos] != TAG_OPTS_A) {
            ISOException.throwIt(ISO7816.SW_DATA_INVALID);
        }
        short optsLen = (short) (buffer[(short) (pos + 1)] & 0xFF);
        if (optsLen > MAX_OPTS_A || (short) (pos + 2 + optsLen) > end) {
            ISOException.throwIt(ISO7816.SW_DATA_INVALID);
        }
        short optsOff = pos;
        short optsTlvLen = (short) (2 + optsLen);
        pos += optsTlvLen;

        // 7C 12 81 10 [RndA]
        if ((short) (end - pos) != (short) (4 + LEN_RND)
                || buffer[pos] != TAG_AUTH_DATA
                || buffer[(short) (pos + 1)] != (byte) (2 + LEN_RND)
                || buffer[(short) (pos + 2)] != TAG_RND
                || buffer[(short) (pos + 3)] != (byte) LEN_RND) {
            ISOException.throwIt(ISO7816.SW_DATA_INVALID);
        }
        short rndAOff = (short) (pos + 4);

        // Message: F0 F0 || OptsA TLV || RndB || RndA
        short m = 0;
        work[m++] = MSG_PREFIX;
        work[m++] = MSG_PREFIX;
        m = Util.arrayCopyNonAtomic(buffer, optsOff, work, m, optsTlvLen);
        short rndBOff = m;
        random.generateData(work, rndBOff, LEN_RND);
        m += LEN_RND;
        m = Util.arrayCopyNonAtomic(buffer, rndAOff, work, m, LEN_RND);

        short derOff = m;
        short derLen = ecdsaSha256.sign(work, (short) 0, m, work, derOff);

        // 7C 54 81 10 [RndB] 82 40 [R||S]
        short o = 0;
        buffer[o++] = TAG_AUTH_DATA;
        buffer[o++] = (byte) (2 + LEN_RND + 2 + LEN_RAW_SIGNATURE);
        buffer[o++] = TAG_RND;
        buffer[o++] = (byte) LEN_RND;
        o = Util.arrayCopyNonAtomic(work, rndBOff, buffer, o, LEN_RND);
        buffer[o++] = TAG_SIGNATURE;
        buffer[o++] = (byte) LEN_RAW_SIGNATURE;
        o = derToRawRS(work, derOff, derLen, buffer, o);

        apdu.setOutgoingAndSend((short) 0, o);
    }

    // ---- Personalisation -------------------------------------------------

    private void getPublicKey(APDU apdu) {
        byte[] buffer = apdu.getBuffer();
        Util.arrayCopyNonAtomic(publicKeyBytes, (short) 0, buffer, (short) 0, LEN_PUBLIC_KEY);
        apdu.setOutgoingAndSend((short) 0, LEN_PUBLIC_KEY);
    }

    private void putCertificate(APDU apdu) {
        byte[] buffer = apdu.getBuffer();
        short offset = Util.getShort(buffer, ISO7816.OFFSET_P1);
        short len = apdu.setIncomingAndReceive();

        if (len != apdu.getIncomingLength()) {
            ISOException.throwIt(ISO7816.SW_WRONG_LENGTH);
        }
        if (offset < 0 || (short) (offset + len) > MAX_CERT || (short) (offset + len) < 0) {
            ISOException.throwIt(ISO7816.SW_FILE_FULL);
        }

        Util.arrayCopy(buffer, apdu.getOffsetCdata(), certificate, offset, len);
    }

    /**
     * LOCK: fix the certificate length and close personalisation for good.
     * Refuses a certificate whose DER length disagrees, or that does not
     * certify this card's own key - a card cannot be locked with a
     * certificate it could never authenticate against.
     */
    private void lock(APDU apdu) {
        byte[] buffer = apdu.getBuffer();
        short len = apdu.setIncomingAndReceive();

        if (len != (short) 2) {
            ISOException.throwIt(ISO7816.SW_WRONG_LENGTH);
        }
        short certLen = Util.getShort(buffer, apdu.getOffsetCdata());

        if (certLen < MIN_CERT || certLen > MAX_CERT
                || derTotalLength(certificate) != certLen
                || !containsPublicKey(certLen)) {
            ISOException.throwIt(ISO7816.SW_DATA_INVALID);
        }

        JCSystem.beginTransaction();
        certificateLength = certLen;
        locked = true;
        JCSystem.commitTransaction();
    }

    /** Total encoded length of the DER element at the start of `der`, or -1. */
    private static short derTotalLength(byte[] der) {
        if (der[0] != (byte) 0x30) {
            return (short) -1;
        }
        short first = (short) (der[1] & 0xFF);
        if (first < 0x80) {
            return (short) (2 + first);
        }
        if (first == 0x81) {
            return (short) (3 + (der[2] & 0xFF));
        }
        if (first == 0x82) {
            short v = Util.makeShort(der[2], der[3]);
            return (v < 0 || v > (short) (MAX_CERT - 4)) ? (short) -1 : (short) (4 + v);
        }
        return (short) -1;
    }

    /** True if the certificate carries this card's key as a BIT STRING: 03 42 00 04 X Y. */
    private boolean containsPublicKey(short certLen) {
        short last = (short) (certLen - 3 - LEN_PUBLIC_KEY);
        for (short i = 0; i <= last; i++) {
            if (certificate[i] == (byte) 0x03
                    && certificate[(short) (i + 1)] == (byte) 0x42
                    && certificate[(short) (i + 2)] == (byte) 0x00
                    && Util.arrayCompare(certificate, (short) (i + 3), publicKeyBytes, (short) 0, LEN_PUBLIC_KEY) == 0) {
                return true;
            }
        }
        return false;
    }

    // ---- Helpers ---------------------------------------------------------

    /** DER ECDSA-Sig-Value to raw R||S, 32 bytes each. */
    private static short derToRawRS(byte[] der, short derOffset, short derLen, byte[] out, short outOffset) {
        short pos = (short) (derOffset + 2); // 30 [seqLen]

        pos++; // 02
        short rLen = (short) (der[pos++] & 0xFF);
        outOffset = copyScalar(der, pos, rLen, out, outOffset);
        pos += rLen;

        pos++; // 02
        short sLen = (short) (der[pos++] & 0xFF);
        return copyScalar(der, pos, sLen, out, outOffset);
    }

    private static short copyScalar(byte[] src, short srcOffset, short srcLen, byte[] dst, short dstOffset) {
        if (srcLen > LEN_SCALAR) {
            srcOffset += (short) (srcLen - LEN_SCALAR); // DER sign padding
            srcLen = LEN_SCALAR;
        }
        if (srcLen < LEN_SCALAR) {
            short pad = (short) (LEN_SCALAR - srcLen);
            Util.arrayFillNonAtomic(dst, dstOffset, pad, (byte) 0);
            dstOffset += pad;
        }
        Util.arrayCopyNonAtomic(src, srcOffset, dst, dstOffset, srcLen);
        return (short) (dstOffset + srcLen);
    }

    private static void setP256Parameters(ECKey key) {
        key.setFieldFP(P256_P, (short) 0, (short) P256_P.length);
        key.setA(P256_A, (short) 0, (short) P256_A.length);
        key.setB(P256_B, (short) 0, (short) P256_B.length);
        key.setG(P256_G, (short) 0, (short) P256_G.length);
        key.setR(P256_N, (short) 0, (short) P256_N.length);
        key.setK((short) 1);
    }
}
