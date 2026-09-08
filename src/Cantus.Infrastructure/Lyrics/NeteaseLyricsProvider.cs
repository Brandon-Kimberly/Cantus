using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cantus.Core.Models;
using Cantus.Core.Parsers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Lyrics;

/// <summary>
/// Fallback lyrics source backed by NetEase Cloud Music's unofficial web API
/// (no key required; the same endpoints used by open-source fetchers such as
/// syncedlyrics). Synced-only by design: a result without timestamped lines is
/// reported as an authoritative miss rather than served as plain text, since
/// LRCLIB earlier in the chain already covers plain lyrics.
/// </summary>
public class NeteaseLyricsProvider : ILyricsFetchProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // NetEase prepends credit lines (lyricist/composer/arranger/etc.) to the
    // LRC body; they are metadata, not lyrics, and would render as the first
    // "sung" lines. Matches a timestamped line whose text starts with a known
    // CJK credit label followed by a colon.
    private const string CREDIT_LINE_PATTERN =
        @"^\[\d{2,}:\d{2}(?:[.:]\d{1,3})?\]\s*(?:作词|作曲|编曲|制作人|制作|监制|混音|母带|录音|出品|发行|和声|吉他|贝斯|键盘|弦乐|鼓)\s*[:：]";

    private static readonly Regex CreditLineRegex = new(CREDIT_LINE_PATTERN, RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly NeteaseOptions _options;
    private readonly ILogger<NeteaseLyricsProvider> _logger;

    public NeteaseLyricsProvider(
        HttpClient httpClient,
        IOptions<NeteaseOptions> options,
        ILogger<NeteaseLyricsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        }
    }

    public string ProviderName => "NetEase";

    public virtual async Task<LyricsFetchResult> FetchLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        try
        {
            long? songId = await TryFindSongIdAsync(track, cancellationToken);
            if (songId is null)
            {
                return LyricsFetchResult.NotFound();
            }

            return await TryGetLyricsAsync(songId.Value, track, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout surfaces as a cancellation that the caller
            // did not request: a transient failure, not an authoritative miss.
            _logger.LogWarning(
                "NetEase lyrics request timed out for track {TrackId} ({Artist} - {Title})",
                track.Id,
                track.Artist,
                track.Title);
            return LyricsFetchResult.Unavailable();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to retrieve lyrics from NetEase for track {TrackId} ({Artist} - {Title})",
                track.Id,
                track.Artist,
                track.Title);
            return LyricsFetchResult.Unavailable();
        }
    }

    private async Task<long?> TryFindSongIdAsync(TrackInfo track, CancellationToken ct)
    {
        string query = Uri.EscapeDataString($"{track.Title} {track.Artist}");
        string url = $"/api/search/get?s={query}&type=1&limit={_options.SearchLimit}";

        using HttpResponseMessage response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        NeteaseSearchResponseDto? search =
            await response.Content.ReadFromJsonAsync<NeteaseSearchResponseDto>(JsonOptions, cancellationToken: ct);

        if (search?.Result?.Songs is not { Count: > 0 } songs)
        {
            return null;
        }

        int targetDurationSec = (int)Math.Round(track.Duration.TotalSeconds);

        NeteaseSongDto? match = songs
            .Where(s => ArtistsMatch(s, track))
            .Where(s => targetDurationSec <= 0 || DurationWithinTolerance(s, targetDurationSec))
            .OrderBy(s => targetDurationSec > 0
                ? Math.Abs((s.Duration / 1000) - targetDurationSec)
                : 0)
            .FirstOrDefault();

        return match?.Id;
    }

    private bool DurationWithinTolerance(NeteaseSongDto song, int targetDurationSec)
    {
        return Math.Abs((song.Duration / 1000) - targetDurationSec) <= _options.DurationToleranceSeconds;
    }

    private static bool ArtistsMatch(NeteaseSongDto song, TrackInfo track)
    {
        if (song.Artists is null || song.Artists.Count == 0)
        {
            return false;
        }

        return song.Artists.Any(a =>
            !string.IsNullOrWhiteSpace(a.Name) &&
            (track.Artist.Contains(a.Name, StringComparison.OrdinalIgnoreCase) ||
             a.Name.Contains(track.Artist, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<LyricsFetchResult> TryGetLyricsAsync(long songId, TrackInfo track, CancellationToken ct)
    {
        string url = $"/api/song/lyric?id={songId}&lv=1&kv=1&tv=-1";

        using HttpResponseMessage response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        NeteaseLyricResponseDto? lyric =
            await response.Content.ReadFromJsonAsync<NeteaseLyricResponseDto>(JsonOptions, cancellationToken: ct);

        if (lyric is null)
        {
            return LyricsFetchResult.NotFound();
        }

        if (lyric.NoLyric)
        {
            return LyricsFetchResult.Found(new SyncedLyrics
            {
                TrackId = track.Id,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                Lines = [],
                IsSynced = false,
                IsInstrumental = true,
                PlainLyrics = null
            });
        }

        string? lrcText = lyric.Lrc?.Lyric;
        if (lyric.Uncollected || string.IsNullOrWhiteSpace(lrcText))
        {
            return LyricsFetchResult.NotFound();
        }

        string cleaned = StripCreditLines(lrcText);
        SyncedLyrics parsed = LrcParser.Parse(
            cleaned,
            track.Id,
            track.Title,
            track.Artist,
            track.Album,
            plainLyrics: null);

        if (parsed.Lines is not { Count: > 0 })
        {
            return LyricsFetchResult.NotFound();
        }

        _logger.LogInformation(
            "NetEase lyrics found for track {TrackId} ({Artist} - {Title}) via song id {SongId}",
            track.Id,
            track.Artist,
            track.Title,
            songId);

        return LyricsFetchResult.Found(parsed);
    }

    internal static string StripCreditLines(string lrcText)
    {
        IEnumerable<string> kept = lrcText
            .Split('\n')
            .Where(line => !CreditLineRegex.IsMatch(line.TrimEnd('\r')));

        return string.Join('\n', kept);
    }

    internal sealed class NeteaseSearchResponseDto
    {
        [JsonPropertyName("result")]
        public NeteaseSearchResultDto? Result { get; set; }
    }

    internal sealed class NeteaseSearchResultDto
    {
        [JsonPropertyName("songs")]
        public List<NeteaseSongDto>? Songs { get; set; }
    }

    internal sealed class NeteaseSongDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Duration in milliseconds.</summary>
        [JsonPropertyName("duration")]
        public long Duration { get; set; }

        [JsonPropertyName("artists")]
        public List<NeteaseArtistDto>? Artists { get; set; }
    }

    internal sealed class NeteaseArtistDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    internal sealed class NeteaseLyricResponseDto
    {
        [JsonPropertyName("nolyric")]
        public bool NoLyric { get; set; }

        [JsonPropertyName("uncollected")]
        public bool Uncollected { get; set; }

        [JsonPropertyName("lrc")]
        public NeteaseLrcDto? Lrc { get; set; }
    }

    internal sealed class NeteaseLrcDto
    {
        [JsonPropertyName("lyric")]
        public string? Lyric { get; set; }
    }
}
