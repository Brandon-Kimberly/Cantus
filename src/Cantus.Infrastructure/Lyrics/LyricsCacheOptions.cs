namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Options for the lyrics cache layer, independent of any single provider:
/// the negative-cache TTL applies to the whole fallback chain, not just
/// LRCLIB.
/// </summary>
public sealed class LyricsCacheOptions
{
    public const string SECTION_NAME = "LyricsCache";
    public int NegativeCacheDays { get; set; } = 30;
}
