using Cantus.Core.Logging;
using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

[TraceLog]
public interface ISpotifyPlayerClient
{
    Task<PlaybackState?> GetCurrentPlaybackAsync(string accessToken, CancellationToken cancellationToken = default);

    Task<PlayerCommandResult> PausePlaybackAsync([Redact] string accessToken, CancellationToken cancellationToken = default);

    Task<PlayerCommandResult> ResumePlaybackAsync([Redact] string accessToken, CancellationToken cancellationToken = default);

    Task<PlayerCommandResult> SkipToNextAsync([Redact] string accessToken, CancellationToken cancellationToken = default);

    Task<PlayerCommandResult> SkipToPreviousAsync([Redact] string accessToken, CancellationToken cancellationToken = default);
}
