namespace Cantus.Core.Models;

/// <summary>
/// Outcome of a Spotify player transport command, so callers can distinguish
/// failures that need user action from ones that are safe to ignore.
/// </summary>
public enum PlayerCommandResult
{
    Success = 0,

    /// <summary>
    /// Spotify returned 403: the access token is missing the
    /// user-modify-playback-state scope (account linked before the scope was
    /// added) or the account is not Premium. Re-linking is the fix for the
    /// former.
    /// </summary>
    MissingPermissions = 1,

    /// <summary>
    /// Spotify returned 404: no active device to control. Starting playback in
    /// any Spotify app makes a device active.
    /// </summary>
    NoActiveDevice = 2,

    /// <summary>Any other failure (network error, invalid command, no session).</summary>
    Failed = 3
}
