namespace SecureVault.Core.Cache;

/// <summary>
/// Thread-safe in-memory LRU cache for decrypted chunks (E15).
/// Prevents redundant chunk decryption during media playback and random seeking.
/// Defaults to 16 chunks (16MB maximum memory footprint).
/// </summary>
public sealed class ChunkLruCache
{
    private readonly int _maxChunks;
    private readonly object _lock = new();
    private readonly Dictionary<ulong, LinkedListNode<CacheItem>> _map;
    private readonly LinkedList<CacheItem> _lruList;

    public int Count
    {
        get
        {
            lock (_lock) return _map.Count;
        }
    }

    public ChunkLruCache(int maxChunks = 16)
    {
        if (maxChunks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxChunks), "Max chunks must be positive.");

        _maxChunks = maxChunks;
        _map = new Dictionary<ulong, LinkedListNode<CacheItem>>(maxChunks);
        _lruList = new LinkedList<CacheItem>();
    }

    public byte[]? Get(ulong chunkOffset)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(chunkOffset, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                return (byte[])node.Value.Data.Clone();
            }
            return null;
        }
    }

    public void Put(ulong chunkOffset, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (_lock)
        {
            if (_map.TryGetValue(chunkOffset, out var existingNode))
            {
                existingNode.Value = new CacheItem(chunkOffset, (byte[])data.Clone());
                _lruList.Remove(existingNode);
                _lruList.AddFirst(existingNode);
                return;
            }

            if (_map.Count >= _maxChunks)
            {
                var oldest = _lruList.Last;
                if (oldest != null)
                {
                    ulong offsetToRemove = oldest.Value.ChunkOffset;
                    if (oldest.Value.Data != null)
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(oldest.Value.Data);
                    }
                    _lruList.RemoveLast();
                    _map.Remove(offsetToRemove);
                }
            }

            var newItem = new CacheItem(chunkOffset, (byte[])data.Clone());
            var newNode = new LinkedListNode<CacheItem>(newItem);
            _lruList.AddFirst(newNode);
            _map[chunkOffset] = newNode;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var node in _map.Values)
            {
                if (node.Value?.Data != null)
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(node.Value.Data);
                }
            }
            _map.Clear();
            _lruList.Clear();
        }
    }

    private sealed record CacheItem(ulong ChunkOffset, byte[] Data);
}
