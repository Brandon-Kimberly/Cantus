namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Options for the NetEase Cloud Music fallback lyrics provider. NetEase's
/// endpoints are unofficial (no key, reverse-engineered by the open-source
/// community and used by tools like syncedlyrics), so the provider can be
/// disabled entirely via configuration.
/// </summary>
public sealed class NeteaseOptions
{
    public const string SECTION_NAME = "Netease";
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://music.163.com";
    public int TimeoutSeconds { get; set; } = 10;
    public int SearchLimit { get; set; } = 10;
    public int DurationToleranceSeconds { get; set; } = 5;
}
