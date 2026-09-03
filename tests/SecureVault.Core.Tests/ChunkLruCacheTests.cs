using SecureVault.Core.Cache;
using Xunit;

namespace SecureVault.Core.Tests;

public class ChunkLruCacheTests
{
    [Fact]
    public void PutAndGet_ReturnsExactData()
    {
        var cache = new ChunkLruCache(maxChunks: 4);
        byte[] chunk0 = new byte[] { 1, 2, 3, 4, 5 };
        cache.Put(1000, chunk0);

        var retrieved = cache.Get(1000);
        Assert.NotNull(retrieved);
        Assert.Equal(chunk0, retrieved);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void CapacityExceeded_EvictsLeastRecentlyUsedChunk()
    {
        var cache = new ChunkLruCache(maxChunks: 3);
        cache.Put(10, new byte[] { 10 });
        cache.Put(20, new byte[] { 20 });
        cache.Put(30, new byte[] { 30 });

        Assert.Equal(3, cache.Count);

        // Access 10 to make it recently used
        _ = cache.Get(10);

        // Put 40, which should evict 20 (the least recently used)
        cache.Put(40, new byte[] { 40 });

        Assert.Equal(3, cache.Count);
        Assert.NotNull(cache.Get(10));
        Assert.Null(cache.Get(20)); // Evicted!
        Assert.NotNull(cache.Get(30));
        Assert.NotNull(cache.Get(40));
    }

    [Fact]
    public void Clear_EmptiesCache()
    {
        var cache = new ChunkLruCache(maxChunks: 5);
        cache.Put(1, new byte[] { 1 });
        cache.Put(2, new byte[] { 2 });
        Assert.Equal(2, cache.Count);

        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Null(cache.Get(1));
    }
}
