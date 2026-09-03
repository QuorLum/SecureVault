using SecureVault.Core.Cache;
using SecureVault.Core.Crypto;
using SecureVault.Core.Format;
using SecureVault.Core.Organization;

namespace SecureVault.Core.Tests;

public class VaultCacheTests
{
    [Fact]
    public void Cache_RoundTrips_AndUsesRandomNoncePerWrite_ReviewerFixVerification()
    {
        Guid vaultUUID = Guid.NewGuid();
        using var masterKey = new SecureBuffer(32); masterKey.AsSpan().Fill(0x88);
        using var cacheKey = CacheEncryption.DeriveCacheKey(masterKey);

        string tempCacheDir = Path.Combine(Path.GetTempPath(), $"cache_test_{Guid.NewGuid():N}");
        try
        {
            using var cache = new VaultCache(vaultUUID, cacheKey, tempCacheDir);

            var index = new VaultIndex();
            index.Entries.Add(new IndexEntry
            {
                FileGuid = Guid.NewGuid(),
                FileName = "test.txt",
                Category = (byte)FileCategory.TextNotes
            });

            var thumbnails = new Dictionary<Guid, byte[]>
            {
                [index.Entries[0].FileGuid] = new byte[] { 0x52, 0x49, 0x46, 0x46 } // WebP magic
            };

            var uiState = new UIState
            {
                WindowWidth = 1280,
                WindowHeight = 800,
                IsMaximized = true
            };

            // Save Snapshot 1
            cache.SaveSnapshot(index, thumbnails, uiState);
            byte[] fileBytes1 = File.ReadAllBytes(cache.CacheFilePath);

            // Save identical Snapshot 2
            cache.SaveSnapshot(index, thumbnails, uiState);
            byte[] fileBytes2 = File.ReadAllBytes(cache.CacheFilePath);

            // REVIEWER REQUIREMENT: Assert fresh random nonce is used on every single write
            byte[] nonce1 = fileBytes1.Take(CacheEncryption.NonceSize).ToArray();
            byte[] nonce2 = fileBytes2.Take(CacheEncryption.NonceSize).ToArray();
            Assert.False(nonce1.AsSpan().SequenceEqual(nonce2));

            // Load and verify round-trip
            var (loadedIndex, loadedThumbs, loadedUI) = cache.LoadSnapshot();
            Assert.NotNull(loadedIndex);
            Assert.NotNull(loadedThumbs);
            Assert.NotNull(loadedUI);

            Assert.Single(loadedIndex.Entries);
            Assert.Equal("test.txt", loadedIndex.Entries[0].FileName);
            Assert.True(loadedThumbs.ContainsKey(index.Entries[0].FileGuid));
            Assert.Equal(1280, loadedUI.WindowWidth);
            Assert.True(loadedUI.IsMaximized);
        }
        finally
        {
            if (Directory.Exists(tempCacheDir)) Directory.Delete(tempCacheDir, true);
        }
    }

    [Fact]
    public void StalenessDetection_FlagsMismatchedIndexVersions()
    {
        Guid vaultUUID = Guid.NewGuid();
        using var masterKey = new SecureBuffer(32); masterKey.AsSpan().Fill(0x88);
        using var cacheKey = CacheEncryption.DeriveCacheKey(masterKey);

        string tempCacheDir = Path.Combine(Path.GetTempPath(), $"cache_stale_test_{Guid.NewGuid():N}");
        try
        {
            using var cache = new VaultCache(vaultUUID, cacheKey, tempCacheDir);

            var index = new VaultIndex { Version = 5 };
            cache.SaveSnapshot(index, new(), new());

            // Same version -> not stale
            Assert.False(cache.IsStale(index));

            // Modified index -> version incremented -> stale
            var modifiedIndex = new VaultIndex { Version = 6 };
            Assert.True(cache.IsStale(modifiedIndex));
        }
        finally
        {
            if (Directory.Exists(tempCacheDir)) Directory.Delete(tempCacheDir, true);
        }
    }

    [Fact]
    public void CorruptedCache_FallsBackGracefullyWithoutCrashing()
    {
        Guid vaultUUID = Guid.NewGuid();
        using var masterKey = new SecureBuffer(32); masterKey.AsSpan().Fill(0x88);
        using var cacheKey = CacheEncryption.DeriveCacheKey(masterKey);

        string tempCacheDir = Path.Combine(Path.GetTempPath(), $"cache_corrupt_test_{Guid.NewGuid():N}");
        try
        {
            using var cache = new VaultCache(vaultUUID, cacheKey, tempCacheDir);
            var index = new VaultIndex();
            cache.SaveSnapshot(index, new(), new());

            // Corrupt one byte in the ciphertext body
            byte[] fileBytes = File.ReadAllBytes(cache.CacheFilePath);
            fileBytes[^1] ^= 0xFF;
            File.WriteAllBytes(cache.CacheFilePath, fileBytes);

            // LoadSnapshot should catch corruption and return nulls without throwing
            var (loadedIndex, loadedThumbs, loadedUI) = cache.LoadSnapshot();
            Assert.Null(loadedIndex);
            Assert.Null(loadedThumbs);
            Assert.Null(loadedUI);
        }
        finally
        {
            if (Directory.Exists(tempCacheDir)) Directory.Delete(tempCacheDir, true);
        }
    }
}
