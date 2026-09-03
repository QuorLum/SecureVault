using STH1123.ReedSolomon;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Format;

/// <summary>
/// Reed-Solomon error correction codec implementing RS(255, 223) over GF(2^8).
/// Provides ~14.3% parity overhead capable of repairing up to 16 corrupted bytes per 255-byte block.
/// </summary>
public sealed class ReedSolomonCodec
{
    public const int DataBlockSize = 223;
    public const int ParityBlockSize = 32;
    public const int TotalBlockSize = 255;

    private readonly ReedSolomonEncoder _encoder;
    private readonly ReedSolomonDecoder _decoder;

    public ReedSolomonCodec()
    {
        var field = GenericGF.QR_CODE_FIELD_256;
        _encoder = new ReedSolomonEncoder(field);
        _decoder = new ReedSolomonDecoder(field);
    }

    /// <summary>
    /// Computes concatenated Reed-Solomon parity bytes for the given data payload.
    /// </summary>
    public byte[] Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return Array.Empty<byte>();
        }

        int blockCount = (data.Length + DataBlockSize - 1) / DataBlockSize;
        byte[] parity = new byte[blockCount * ParityBlockSize];

        int[] buffer = new int[TotalBlockSize];

        for (int b = 0; b < blockCount; b++)
        {
            Array.Clear(buffer, 0, buffer.Length);

            int dataOffset = b * DataBlockSize;
            int dataLen = Math.Min(DataBlockSize, data.Length - dataOffset);

            for (int i = 0; i < dataLen; i++)
            {
                buffer[i] = data[dataOffset + i];
            }

            _encoder.Encode(buffer, ParityBlockSize);

            int parityOffset = b * ParityBlockSize;
            for (int i = 0; i < ParityBlockSize; i++)
            {
                parity[parityOffset + i] = (byte)buffer[DataBlockSize + i];
            }
        }

        return parity;
    }

    /// <summary>
    /// Validates and auto-repairs corrupted bytes in the data payload using the provided parity.
    /// </summary>
    public (byte[] RepairedData, int ErrorsFixed) Decode(ReadOnlySpan<byte> data, ReadOnlySpan<byte> parity)
    {
        if (data.IsEmpty)
        {
            return (Array.Empty<byte>(), 0);
        }

        int blockCount = (data.Length + DataBlockSize - 1) / DataBlockSize;
        if (parity.Length < blockCount * ParityBlockSize)
        {
            throw new CorruptedVaultException($"Insufficient parity bytes for RS decode (expected {blockCount * ParityBlockSize}, got {parity.Length}).");
        }

        byte[] repaired = data.ToArray();
        int totalErrors = 0;
        int[] buffer = new int[TotalBlockSize];

        for (int b = 0; b < blockCount; b++)
        {
            Array.Clear(buffer, 0, buffer.Length);

            int dataOffset = b * DataBlockSize;
            int dataLen = Math.Min(DataBlockSize, data.Length - dataOffset);

            for (int i = 0; i < dataLen; i++)
            {
                buffer[i] = repaired[dataOffset + i];
            }

            int parityOffset = b * ParityBlockSize;
            for (int i = 0; i < ParityBlockSize; i++)
            {
                buffer[DataBlockSize + i] = parity[parityOffset + i];
            }

            try
            {
                _decoder.Decode(buffer, ParityBlockSize);

                // Verify codeword integrity by re-encoding parity
                int[] recheck = new int[TotalBlockSize];
                Array.Copy(buffer, 0, recheck, 0, DataBlockSize);
                _encoder.Encode(recheck, ParityBlockSize);

                for (int i = 0; i < ParityBlockSize; i++)
                {
                    if (recheck[DataBlockSize + i] != buffer[DataBlockSize + i])
                    {
                        throw new UncorrectableCorruptionException("Reed-Solomon decoding failed: corruption exceeds error correction capacity.");
                    }
                }
            }
            catch (UncorrectableCorruptionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new UncorrectableCorruptionException("Reed-Solomon decoding failed: corruption exceeds error correction capacity.", ex);
            }

            for (int i = 0; i < dataLen; i++)
            {
                byte corrected = (byte)buffer[i];
                if (repaired[dataOffset + i] != corrected)
                {
                    repaired[dataOffset + i] = corrected;
                    totalErrors++;
                }
            }
        }

        return (repaired, totalErrors);
    }
}
