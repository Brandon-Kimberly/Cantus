using Cantus.Core.Models;
using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

public class SpotifyPlayerClientTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task GetCurrentPlaybackAsync_WithInvalidToken_ReturnsNull(string? token)
    {
        SpotifyPlayerClient client = new(NullLogger<SpotifyPlayerClient>.Instance);

        Core.Models.PlaybackState? result = await client.GetCurrentPlaybackAsync(token!);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PlayerCommands_WithInvalidToken_FailWithoutCallingSpotify(string token)
    {
        SpotifyPlayerClient client = new(NullLogger<SpotifyPlayerClient>.Instance);

        (await client.PausePlaybackAsync(token)).Should().Be(PlayerCommandResult.Failed);
        (await client.SetVolumeAsync(token, 50)).Should().Be(PlayerCommandResult.Failed);
        (await client.SeekPlaybackAsync(token, 1000)).Should().Be(PlayerCommandResult.Failed);
        (await client.SetShuffleAsync(token, true)).Should().Be(PlayerCommandResult.Failed);
        (await client.SetRepeatAsync(token, "context")).Should().Be(PlayerCommandResult.Failed);
    }

    [Fact]
    public async Task SetRepeatAsync_WithUnknownMode_FailsWithoutCallingSpotify()
    {
        SpotifyPlayerClient client = new(NullLogger<SpotifyPlayerClient>.Instance);

        PlayerCommandResult result = await client.SetRepeatAsync("some-token", "shuffle-all");

        result.Should().Be(PlayerCommandResult.Failed);
    }
}
