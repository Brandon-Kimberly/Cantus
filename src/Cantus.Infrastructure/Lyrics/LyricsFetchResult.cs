using Cantus.Core.Models;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Result of an LRCLIB lookup that distinguishes "LRCLIB answered and has no
/// lyrics" from "LRCLIB could not be reached". Only the former should enter
/// the negative cache; caching the latter poisons the track for the whole
/// negative-cache TTL when the outage was transient.
/// </summary>
/// <param name="Lyrics">The lyrics, when found.</param>
/// <param name="IsDefinitive">
/// True when LRCLIB responded authoritatively (lyrics found, or both the exact
/// and search endpoints answered without a match). False when the lookup
/// failed for a transient reason (network error, timeout, 5xx).
/// </param>
public sealed record LyricsFetchResult(SyncedLyrics? Lyrics, bool IsDefinitive)
{
    public static LyricsFetchResult Found(SyncedLyrics lyrics) => new(lyrics, true);

    public static LyricsFetchResult NotFound() => new(null, true);

    public static LyricsFetchResult Unavailable() => new(null, false);
}
