using System.Collections.Concurrent;

namespace SecureVault.Core.Cache;

/// <summary>
/// In-memory and cache-backed playback position tracker (E17, I17).
/// Allows resuming video and audio playback from where the user left off.
/// </summary>
public sealed class PlaybackPositionCache
{
    private readonly ConcurrentDictionary<Guid, double> _positions = new();

    public void SavePosition(Guid fileGuid, double positionFraction)
    {
        if (positionFraction >= 0.98) // If at or near end, clear to start over next time
        {
            _positions.TryRemove(fileGuid, out _);
        }
        else
        {
            _positions[fileGuid] = Math.Clamp(positionFraction, 0.0, 1.0);
        }
    }

    public double GetPosition(Guid fileGuid)
    {
        if (_positions.TryGetValue(fileGuid, out double pos))
        {
            return pos;
        }
        return 0.0;
    }

    public void ClearPosition(Guid fileGuid)
    {
        _positions.TryRemove(fileGuid, out _);
    }
}
