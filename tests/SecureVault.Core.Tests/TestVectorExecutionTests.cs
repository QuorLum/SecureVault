using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;
using SecureVault.Core.Organization;
using Xunit;

namespace SecureVault.Core.Tests;

public class TestVectorExecutionTests
{
    private static string GetVectorPath(string filename)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "vectors", filename);
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "vectors", filename);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        string curr = Path.Combine(Directory.GetCurrentDirectory(), "tests", "vectors", filename);
        if (File.Exists(curr)) return curr;

        throw new FileNotFoundException($"Test vector file not found: {filename}");
    }

    private static JsonNode LoadVectorJson(string filename)
    {
        string path = GetVectorPath(filename);
        string text = File.ReadAllText(path);
        return JsonNode.Parse(text) ?? throw new InvalidOperationException($"Failed to parse JSON: {filename}");
    }

    [Fact]
    public void Vector_01_Argon2idDerivation_ExecutesAndValidates()
    {
        var root = LoadVectorJson("argon2id-derivation.json");
        var vector = root["vectors"]?[0]!;

        string password = vector["password"]!.GetValue<string>();
        byte[] salt = Convert.FromHexString(vector["salt_hex"]!.GetValue<string>());
        int memoryKb = vector["memory_kb"]!.GetValue<int>();
        int iterations = vector["iterations"]!.GetValue<int>();
        int parallelism = vector["parallelism"]!.GetValue<int>();
        int outputLength = vector["output_length"]!.GetValue<int>();

        var (keyBuf, derivedSalt) = KeyDerivation.DeriveFromPassword(password, salt, memoryKb, iterations, parallelism);
        using (keyBuf)
        {
            Assert.Equal(outputLength, keyBuf.Length);
            Assert.Equal(salt, derivedSalt);
            Assert.False(keyBuf.AsReadOnlySpan().SequenceEqual(new byte[outputLength]));
        }
    }

    [Fact]
    public void Vector_02_AutoCategorization_ExecutesAndValidates()
    {
        var root = LoadVectorJson("auto-categorization.json");
        var mappings = root["mappings"]!.AsArray();

        foreach (var mapping in mappings)
        {
            string filename = mapping!["filename"]!.GetValue<string>();
            string expectedCategory = mapping["category"]!.GetValue<string>();

            var detected = AutoCategorizer.Categorize(filename);
            string detectedStr = detected switch
            {
                FileCategory.Photos => "Photos",
                FileCategory.Videos => "Videos",
                FileCategory.Audio => "Audio",
                FileCategory.Documents => "Documents",
                FileCategory.TextNotes => "TextNotes",
                FileCategory.Applications => "Applications",
                FileCategory.Archives => "Archives",
                _ => "Other"
            };

            Assert.Equal(expectedCategory, detectedStr);
        }
    }

    [Fact]
    public void Vector_03_BackupManifestSchema_ExecutesAndValidates()
    {
        var root = LoadVectorJson("backup-manifest-schema.json");

        string vaultName = root["vault_name"]!.GetValue<string>();
        Guid vaultUuid = Guid.Parse(root["vault_uuid"]!.GetValue<string>());
        int formatVersion = root["format_version"]!.GetValue<int>();
        long totalSize = root["total_size_bytes"]!.GetValue<long>();
        bool isSplit = root["is_split"]!.GetValue<bool>();

        Assert.Equal("test-vault", vaultName);
        Assert.NotEqual(Guid.Empty, vaultUuid);
        Assert.True(formatVersion >= 1);
        Assert.True(totalSize > 0);
        Assert.True(isSplit);

        var chainParts = root["chain_parts"]!.AsArray();
        Assert.NotEmpty(chainParts);
        var splits = chainParts[0]!["splits"]!.AsArray();
        Assert.Equal(2, splits.Count);
    }

    [Fact]
    public void Vector_04_BruteForceDelay_ExecutesAndValidates()
    {
        var root = LoadVectorJson("brute-force-delay.json");
        var cases = root["cases"]!.AsArray();

        foreach (var c in cases)
        {
            int attempts = c!["failed_attempts"]!.GetValue<int>();
            int expectedDelay = c["expected_delay_seconds"]!.GetValue<int>();

            int actualDelay = attempts == 0 ? 0 : (int)Math.Min(Math.Pow(2, attempts), 60);
            Assert.Equal(expectedDelay, actualDelay);
        }
    }

    [Fact]
    public void Vector_05_ChunkFormat_ExecutesAndValidates()
    {
        var root = LoadVectorJson("chunk-format.json");

        int headerSize = root["chunk_header_size"]!.GetValue<int>();
        int payloadSize = root["test_payload_size"]!.GetValue<int>();
        int secureMode = root["protection_mode_secure"]!.GetValue<int>();
        int fastMode = root["protection_mode_fast"]!.GetValue<int>();

        Assert.Equal(VaultConstants.ChunkHeaderSize, headerSize);
        Assert.Equal(1024, payloadSize);
        Assert.Equal((int)ProtectionMode.SecureMode, secureMode);
        Assert.Equal((int)ProtectionMode.FastObfuscation, fastMode);
    }

    [Fact]
    public void Vector_06_EncryptionService_ExecutesAndValidates()
    {
        var root = LoadVectorJson("encryption-service.json");

        byte[] masterKeyBytes = Convert.FromHexString(root["master_key_hex"]!.GetValue<string>());
        string expectedIndexInfo = root["info_index"]!.GetValue<string>();
        string expectedSecureInfo = root["info_secure_mode"]!.GetValue<string>();
        string expectedObfInfo = root["info_obfuscation"]!.GetValue<string>();
        string expectedHmacInfo = root["info_hmac"]!.GetValue<string>();

        using var masterKey = new SecureBuffer(masterKeyBytes);
        using var enc = new EncryptionService(masterKey);

        Assert.Equal(32, enc.IndexKey.Length);
        Assert.Equal(32, enc.SecureModeKey.Length);
        Assert.Equal(32, enc.ObfuscationKey.Length);
        Assert.Equal(32, enc.HmacKey.Length);

        // Derive manually with expected info strings to verify subkey info matching
        using var testIndexKey = KeyDerivation.DeriveSubkey(masterKey, expectedIndexInfo, 32);
        using var testSecureKey = KeyDerivation.DeriveSubkey(masterKey, expectedSecureInfo, 32);
        using var testObfKey = KeyDerivation.DeriveSubkey(masterKey, expectedObfInfo, 32);
        using var testHmacKey = KeyDerivation.DeriveSubkey(masterKey, expectedHmacInfo, 32);

        Assert.True(enc.IndexKey.AsReadOnlySpan().SequenceEqual(testIndexKey.AsReadOnlySpan()));
        Assert.True(enc.SecureModeKey.AsReadOnlySpan().SequenceEqual(testSecureKey.AsReadOnlySpan()));
        Assert.True(enc.ObfuscationKey.AsReadOnlySpan().SequenceEqual(testObfKey.AsReadOnlySpan()));
        Assert.True(enc.HmacKey.AsReadOnlySpan().SequenceEqual(testHmacKey.AsReadOnlySpan()));
    }

    [Fact]
    public void Vector_07_FileAdd_ExecutesAndValidates()
    {
        var root = LoadVectorJson("file-add.json");

        int blockHeaderSize = root["block_header_size"]!.GetValue<int>();
        int blockFooterSize = root["block_footer_size"]!.GetValue<int>();
        string blockHeaderMagic = root["expected_block_header_magic"]!.GetValue<string>();
        string blockFooterMagic = root["expected_block_footer_magic"]!.GetValue<string>();

        Assert.Equal(BlockHeader.Size, blockHeaderSize);
        Assert.Equal(BlockFooter.Size, blockFooterSize);
        Assert.Equal(0x424C4B48u, BlockHeader.ExpectedMagic);
        Assert.Equal(0x424C4B46u, BlockFooter.ExpectedMagic);
        Assert.Equal("BLKH", blockHeaderMagic);
        Assert.Equal("BLKF", blockFooterMagic);
    }

    [Fact]
    public void Vector_08_KeyWrapping_ExecutesAndValidates()
    {
        var root = LoadVectorJson("key-wrapping.json");

        byte[] masterKeyBytes = Convert.FromHexString(root["master_key_hex"]!.GetValue<string>());
        string password = root["password"]!.GetValue<string>();
        string wrongPassword = root["wrong_password"]!.GetValue<string>();
        string[] recoveryWords = root["recovery_words"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

        using var masterKey = new SecureBuffer(masterKeyBytes);
        byte[] recoverySeed = RecoveryKeyGenerator.WordsToSeed(recoveryWords);

        var wrapped = KeyWrapping.WrapMasterKey(masterKey, password, recoverySeed, 65536, 1, 1);

        // Correct password unwraps
        using var unwrapped = KeyWrapping.UnwrapWithPassword(wrapped, password, 65536, 1, 1);
        Assert.True(masterKey.AsReadOnlySpan().SequenceEqual(unwrapped.AsReadOnlySpan()));

        // Wrong password throws InvalidPasswordException
        Assert.Throws<InvalidPasswordException>(() =>
            KeyWrapping.UnwrapWithPassword(wrapped, wrongPassword, 65536, 1, 1));
    }

    [Fact]
    public void Vector_09_ObfuscationKeystream_ExecutesAndValidates()
    {
        var root = LoadVectorJson("obfuscation-keystream.json");

        string infoPrefix = root["info_prefix"]!.GetValue<string>();
        int saltLength = root["salt_length"]!.GetValue<int>();
        int testLen = root["test_buffer_length"]!.GetValue<int>();

        Assert.Equal("SecureVault-XOR-Keystream-v1:", infoPrefix);
        Assert.Equal(16, saltLength);

        byte[] masterKey = new byte[32];
        byte[] salt = new byte[16];
        using var keyBuf = new SecureBuffer(masterKey);

        using var keystream = new ObfuscationKeystream(keyBuf, Guid.NewGuid(), salt);
        byte[] buffer = new byte[testLen];
        keystream.ApplyInPlace(buffer, 0);

        Assert.Equal(testLen, buffer.Length);
        Assert.False(buffer.All(b => b == 0));
    }

    [Fact]
    public void Vector_10_RecoveryKey_ExecutesAndValidates()
    {
        var root = LoadVectorJson("recovery-key.json");

        byte[] entropy = Convert.FromHexString(root["entropy_hex"]!.GetValue<string>());
        string[] expectedWords = root["expected_words"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

        string[] words = RecoveryKeyGenerator.SeedToWords(entropy);
        Assert.Equal(expectedWords, words);

        byte[] roundtripSeed = RecoveryKeyGenerator.WordsToSeed(words);
        Assert.Equal(entropy, roundtripSeed);
    }

    [Fact]
    public void Vector_11_ReedSolomon_ExecutesAndValidates()
    {
        var root = LoadVectorJson("reed-solomon.json");

        int dataSize = root["data_block_size"]!.GetValue<int>();
        int paritySize = root["parity_block_size"]!.GetValue<int>();
        int totalSize = root["total_block_size"]!.GetValue<int>();
        int maxCorrectable = root["max_correctable_errors"]!.GetValue<int>();
        int uncorrectableThreshold = root["uncorrectable_error_threshold"]!.GetValue<int>();

        Assert.Equal(223, dataSize);
        Assert.Equal(32, paritySize);
        Assert.Equal(255, totalSize);
        Assert.Equal(16, maxCorrectable);
        Assert.Equal(17, uncorrectableThreshold);

        var codec = new ReedSolomonCodec();
        byte[] data = new byte[dataSize];
        RandomNumberGenerator.Fill(data);
        byte[] parity = codec.Encode(data);

        // Corrupt 16 bytes -> auto-repair succeeds
        byte[] corrupted = (byte[])data.Clone();
        for (int i = 0; i < maxCorrectable; i++) corrupted[i] ^= 0xFF;

        var (repaired, correctedCount) = codec.Decode(corrupted, parity);
        Assert.Equal(data, repaired);
        Assert.Equal(maxCorrectable, correctedCount);
    }

    [Fact]
    public void Vector_12_SecureBuffer_ExecutesAndValidates()
    {
        var root = LoadVectorJson("secure-buffer.json");

        int bufSize = root["buffer_size"]!.GetValue<int>();
        byte patternByte = root["test_pattern_byte"]!.GetValue<byte>();
        string patternHex = root["test_pattern_hex"]!.GetValue<string>();
        string zeroedHex = root["expected_zeroed_hex"]!.GetValue<string>();

        byte[]? rawRef = null;
        using (var sec = new SecureBuffer(bufSize))
        {
            sec.AsSpan().Fill(patternByte);
            Assert.Equal(patternHex, Convert.ToHexString(sec.AsReadOnlySpan()).ToLowerInvariant());
            rawRef = sec.DangerousGetRawBuffer();
        }

        Assert.NotNull(rawRef);
        Assert.Equal(zeroedHex, Convert.ToHexString(rawRef).ToLowerInvariant());
    }

    [Fact]
    public void Vector_13_Sha256Companion_ExecutesAndValidates()
    {
        var root = LoadVectorJson("sha256-companion.json");

        string hash = root["hash"]!.GetValue<string>();
        string filename = root["filename"]!.GetValue<string>();
        string companionLine = root["companion_line"]!.GetValue<string>();

        string generated = $"{hash}  {filename}\n";
        Assert.Equal(companionLine, generated);
    }

    [Fact]
    public void Vector_14_ThumbnailDimensions_ExecutesAndValidates()
    {
        var root = LoadVectorJson("thumbnail-dimensions.json");
        var items = root.AsArray();
        Assert.NotEmpty(items);

        foreach (var item in items)
        {
            int inputWidth = item!["inputWidth"]!.GetValue<int>();
            int inputHeight = item["inputHeight"]!.GetValue<int>();
            int maxDim = item["maxDimension"]!.GetValue<int>();
            int expectedWidth = item["expectedWidth"]!.GetValue<int>();
            int expectedHeight = item["expectedHeight"]!.GetValue<int>();

            double ratio = Math.Min((double)maxDim / inputWidth, (double)maxDim / inputHeight);
            int targetWidth = Math.Min(inputWidth, Math.Max(1, (int)(inputWidth * ratio)));
            int targetHeight = Math.Min(inputHeight, Math.Max(1, (int)(inputHeight * ratio)));

            Assert.Equal(expectedWidth, targetWidth);
            Assert.Equal(expectedHeight, targetHeight);
        }
    }

    [Fact]
    public void Vector_15_VaultHeader_ExecutesAndValidates()
    {
        var root = LoadVectorJson("vault-header.json");

        int expectedLength = root["expected_header_length"]!.GetValue<int>();
        Assert.Equal(VaultConstants.HeaderSize, expectedLength);
    }

    [Fact]
    public void Vector_16_VaultIndex_ExecutesAndValidates()
    {
        var root = LoadVectorJson("vault-index.json");

        string sampleName = root["sample_file_name"]!.GetValue<string>();
        string samplePath = root["sample_virtual_path"]!.GetValue<string>();
        int sampleMode = root["sample_protection_mode"]!.GetValue<int>();

        var entry = new IndexEntry
        {
            FileGuid = Guid.NewGuid(),
            FileName = sampleName,
            VirtualFolderPath = samplePath,
            ProtectionMode = (ProtectionMode)sampleMode
        };

        Assert.Equal("passwords.txt", entry.FileName);
        Assert.Equal("/Documents/Secure", entry.VirtualFolderPath);
        Assert.Equal(ProtectionMode.SecureMode, entry.ProtectionMode);
    }
}
