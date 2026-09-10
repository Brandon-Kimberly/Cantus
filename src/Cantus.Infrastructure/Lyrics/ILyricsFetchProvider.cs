using Cantus.Core.Models;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// A single lyrics source that can participate in the fallback chain.
/// Implementations must classify their result (found / authoritative miss /
/// transiently unavailable) so <see cref="CachedLyricsService"/> can decide
/// whether a full-chain miss is safe to negative-cache.
/// </summary>
public interface ILyricsFetchProvider
{
    /// <summary>Short source name for logs (e.g. "LRCLIB", "NetEase").</summary>
    string ProviderName { get; }

    Task<LyricsFetchResult> FetchLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);
}
