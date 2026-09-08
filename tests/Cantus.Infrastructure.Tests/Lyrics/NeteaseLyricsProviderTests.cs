using System.Net;
using System.Text;
using Cantus.Core.Models;
using Cantus.Infrastructure.Lyrics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantus.Infrastructure.Tests.Lyrics;

public class NeteaseLyricsProviderTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseHandler { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (ResponseHandler is not null)
            {
                return Task.FromResult(ResponseHandler(request));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static NeteaseLyricsProvider CreateProvider(MockHttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://music.163.com") };
        IOptions<NeteaseOptions> options = Options.Create(new NeteaseOptions());
        return new NeteaseLyricsProvider(httpClient, options, NullLogger<NeteaseLyricsProvider>.Instance);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private const string SEARCH_JSON = """
        {
            "result": {
                "songs": [
                    { "id": 111, "name": "Miss America", "duration": 295011,
                      "artists": [ { "name": "Brandon Flowers" } ] },
                    { "id": 222, "name": "Miss America", "duration": 244000,
                      "artists": [ { "name": "Carolina Liar" } ] }
                ],
                "songCount": 2
            },
            "code": 200
        }
        """;

    private static readonly TrackInfo MatchingTrack = new()
    {
        Id = "spotify_miss_america",
        Title = "Miss America",
        Artist = "Brandon Flowers",
        Duration = TimeSpan.FromMilliseconds(295011)
    };

    [Fact]
    public async Task FetchLyricsAsync_MatchFound_ParsesSyncedLyricsAndStripsCreditLines()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(SEARCH_JSON);
                }

                if (req.RequestUri!.PathAndQuery.StartsWith("/api/song/lyric") &&
                    req.RequestUri!.Query.Contains("id=111"))
                {
                    return Json("""
                        {
                            "lrc": { "lyric": "[00:00.000] 作词 : Brandon Flowers\n[00:01.000] 作曲 : Brandon Flowers\n[00:08.156]I'm writing you this letter\n[00:12.105]If you ain't figured out\n" },
                            "code": 200
                        }
                        """);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.IsDefinitive.Should().BeTrue();
        result.Lyrics.Should().NotBeNull();
        result.Lyrics!.IsSynced.Should().BeTrue();
        result.Lyrics.Lines.Should().HaveCount(2, "the two credit lines must be stripped");
        result.Lyrics.Lines[0].Text.Should().Be("I'm writing you this letter");
        result.Lyrics.Lines[1].Text.Should().Be("If you ain't figured out");
    }

    [Fact]
    public async Task FetchLyricsAsync_NoDurationOrArtistMatch_ReportsDefinitiveNotFound()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req => req.RequestUri!.PathAndQuery.StartsWith("/api/search/get")
                ? Json(SEARCH_JSON)
                : new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        TrackInfo wrongDuration = new()
        {
            Id = "t-wrong",
            Title = "Miss America",
            Artist = "Brandon Flowers",
            Duration = TimeSpan.FromSeconds(180)
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(wrongDuration);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeTrue("the search answered; no candidate matched");
    }

    [Fact]
    public async Task FetchLyricsAsync_UncollectedSong_ReportsDefinitiveNotFound()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(SEARCH_JSON);
                }

                return Json("""{ "uncollected": true, "code": 200 }""");
            }
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeTrue();
    }

    [Fact]
    public async Task FetchLyricsAsync_InstrumentalSong_ReportsInstrumentalLyrics()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(SEARCH_JSON);
                }

                return Json("""{ "nolyric": true, "code": 200 }""");
            }
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.Lyrics.Should().NotBeNull();
        result.Lyrics!.IsInstrumental.Should().BeTrue();
        result.Lyrics.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchLyricsAsync_ServerError_ReportsUnavailable()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeFalse("a 5xx is transient, not an authoritative miss");
    }

    [Fact]
    public async Task FetchLyricsAsync_LyricsWithoutTimestamps_ReportsDefinitiveNotFound()
    {
        MockHttpMessageHandler handler = new()
        {
            ResponseHandler = req =>
            {
                if (req.RequestUri!.PathAndQuery.StartsWith("/api/search/get"))
                {
                    return Json(SEARCH_JSON);
                }

                return Json("""{ "lrc": { "lyric": "just plain text with no timestamps" }, "code": 200 }""");
            }
        };

        LyricsFetchResult result = await CreateProvider(handler).FetchLyricsAsync(MatchingTrack);

        result.Lyrics.Should().BeNull();
        result.IsDefinitive.Should().BeTrue("this provider is synced-only by design");
    }
}
