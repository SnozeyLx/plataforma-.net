using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Games.MassiveServer.Services;

/// <summary>
/// Provides AES-256-GCM packet encryption, HMAC handshake verification, and sliding-window replay protection for v2 (0xD6) UDP frames.
/// </summary>
public static class UdpPacketCipher
{
    public const byte ProtocolV2Marker = 0xD6;
    public const byte DirectionClientToServer = 1;
    public const byte DirectionServerToClient = 2;
    public const int KeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;
    public const int HeaderSizeBytes = 1 + sizeof(ulong) + TagSizeBytes; // marker(1) + seq(8) + tag(16) = 25
    public const int HandshakePayloadSizeBytes = 1 + 16 + sizeof(long) + 32; // marker(1) + ticketId(16) + ts(8) + hmac(32) = 57

    /// <summary>Computes the HMAC-SHA256 tag for a v2 UDP connection request.</summary>
    public static void ComputeHandshakeMac(
        ReadOnlySpan<byte> sessionKey,
        Guid ticketId,
        long timestampUnixMs,
        Span<byte> destination)
    {
        Span<byte> message = stackalloc byte[24];
        ticketId.TryWriteBytes(message[..16]);
        BinaryPrimitives.WriteInt64LittleEndian(message[16..], timestampUnixMs);
        HMACSHA256.HashData(sessionKey, message, destination);
    }

    /// <summary>Verifies a v2 UDP connection request HMAC in constant time.</summary>
    public static bool VerifyHandshakeMac(
        ReadOnlySpan<byte> sessionKey,
        Guid ticketId,
        long timestampUnixMs,
        ReadOnlySpan<byte> providedMac)
    {
        if (providedMac.Length != 32 || sessionKey.Length != KeySizeBytes)
        {
            return false;
        }

        Span<byte> expectedMac = stackalloc byte[32];
        ComputeHandshakeMac(sessionKey, ticketId, timestampUnixMs, expectedMac);
        return CryptographicOperations.FixedTimeEquals(expectedMac, providedMac);
    }

    /// <summary>Encrypts a v2 gameplay payload into <paramref name="destination"/> and returns total frame length.</summary>
    public static int Encrypt(
        AesGcm aesGcm,
        byte direction,
        ulong sequence,
        ReadOnlySpan<byte> plaintext,
        Span<byte> destination)
    {
        var totalLength = HeaderSizeBytes + plaintext.Length;
        if (destination.Length < totalLength)
        {
            throw new ArgumentException("Destination buffer is too small for the encrypted UDP frame.", nameof(destination));
        }

        destination[0] = ProtocolV2Marker;
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(1, sizeof(ulong)), sequence);

        Span<byte> nonce = stackalloc byte[NonceSizeBytes];
        WriteNonce(direction, sequence, nonce);

        Span<byte> associatedData = destination[..9]; // marker + sequence
        var tagSlice = destination.Slice(9, TagSizeBytes);
        var ciphertextSlice = destination.Slice(HeaderSizeBytes, plaintext.Length);

        aesGcm.Encrypt(nonce, plaintext, ciphertextSlice, tagSlice, associatedData);
        return totalLength;
    }

    /// <summary>Decrypts and authenticates an incoming v2 UDP frame.</summary>
    public static bool TryDecrypt(
        AesGcm aesGcm,
        byte expectedDirection,
        ReadOnlySpan<byte> frame,
        Span<byte> plaintextDestination,
        out ulong sequence,
        out int plaintextLength)
    {
        sequence = 0;
        plaintextLength = 0;
        if (frame.Length <= HeaderSizeBytes || frame[0] != ProtocolV2Marker)
        {
            return false;
        }

        plaintextLength = frame.Length - HeaderSizeBytes;
        if (plaintextDestination.Length < plaintextLength)
        {
            return false;
        }

        sequence = BinaryPrimitives.ReadUInt64LittleEndian(frame.Slice(1, sizeof(ulong)));
        if (sequence == 0)
        {
            return false;
        }

        Span<byte> nonce = stackalloc byte[NonceSizeBytes];
        WriteNonce(expectedDirection, sequence, nonce);

        var associatedData = frame[..9];
        var tagSlice = frame.Slice(9, TagSizeBytes);
        var ciphertextSlice = frame.Slice(HeaderSizeBytes, plaintextLength);

        try
        {
            aesGcm.Decrypt(nonce, ciphertextSlice, tagSlice, plaintextDestination[..plaintextLength], associatedData);
            return true;
        }
        catch (CryptographicException)
        {
            plaintextLength = 0;
            return false;
        }
    }

    private static void WriteNonce(byte direction, ulong sequence, Span<byte> nonce)
    {
        nonce[0] = direction;
        nonce[1] = 0;
        nonce[2] = 0;
        nonce[3] = 0;
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], sequence);
    }

    /// <summary>64-packet sliding window to reject replayed or stale UDP sequence numbers.</summary>
    public struct ReplayWindow
    {
        private ulong _highestSequence;
        private ulong _bitmap;

        public bool TryAccept(ulong sequence)
        {
            if (sequence == 0)
            {
                return false;
            }

            if (sequence > _highestSequence)
            {
                var shift = sequence - _highestSequence;
                _bitmap = shift >= 64 ? 1UL : (_bitmap << (int)shift) | 1UL;
                _highestSequence = sequence;
                return true;
            }

            var delta = _highestSequence - sequence;
            if (delta >= 64)
            {
                return false;
            }

            var mask = 1UL << (int)delta;
            if ((_bitmap & mask) != 0)
            {
                return false;
            }

            _bitmap |= mask;
            return true;
        }
    }
}
