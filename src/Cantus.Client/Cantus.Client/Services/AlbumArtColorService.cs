using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using StbImageSharp;

namespace Cantus.Client.Services;

/// <summary>
/// Downloads album artwork and extracts its dominant color swatches.
/// Successful extractions are cached by URL; failures are not cached so that
/// transient network errors retry on the next track change.
/// </summary>
public sealed class AlbumArtColorService
{
    private const int MAX_IMAGE_BYTES = 4 * 1024 * 1024;
    private const int FETCH_TIMEOUT_SECONDS = 10;
    private const int CACHE_CAPACITY = 16;

    private static AlbumArtColorService? _instance;
    public static AlbumArtColorService Instance => _instance ??= new AlbumArtColorService();

    private readonly HttpClient _httpClient;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, IReadOnlyList<ColorSwatch>> _cachedSwatchesByUrl = new();
    private readonly Queue<string> _cacheEvictionOrder = new();

    public AlbumArtColorService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(FETCH_TIMEOUT_SECONDS) })
    {
    }

    public AlbumArtColorService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool TryGetCachedSwatches(string? albumArtUrl, [NotNullWhen(true)] out IReadOnlyList<ColorSwatch>? swatches)
    {
        swatches = null;
        if (string.IsNullOrWhiteSpace(albumArtUrl))
        {
            return false;
        }

        lock (_cacheLock)
        {
            return _cachedSwatchesByUrl.TryGetValue(albumArtUrl, out swatches);
        }
    }

    public async Task<IReadOnlyList<ColorSwatch>?> GetSwatchesAsync(string? albumArtUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(albumArtUrl))
        {
            return null;
        }

        if (TryGetCachedSwatches(albumArtUrl, out IReadOnlyList<ColorSwatch>? cachedSwatches))
        {
            return cachedSwatches;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(FETCH_TIMEOUT_SECONDS));

            using HttpResponseMessage response = await _httpClient
                .GetAsync(albumArtUrl, timeoutCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[AlbumArtColorService] Album art request returned {(int)response.StatusCode} for {albumArtUrl}");
                return null;
            }

            if (response.Content.Headers.ContentLength is long contentLength && contentLength > MAX_IMAGE_BYTES)
            {
                Console.WriteLine($"[AlbumArtColorService] Album art exceeds {MAX_IMAGE_BYTES} bytes: {albumArtUrl}");
                return null;
            }

            byte[] imageBytes = await response.Content
                .ReadAsByteArrayAsync(timeoutCts.Token)
                .ConfigureAwait(false);

            if (imageBytes.Length == 0 || imageBytes.Length > MAX_IMAGE_BYTES)
            {
                return null;
            }

            timeoutCts.Token.ThrowIfCancellationRequested();

            ImageResult image = ImageResult.FromMemory(imageBytes, ColorComponents.RedGreenBlueAlpha);
            IReadOnlyList<ColorSwatch> swatches = ColorQuantizer.Quantize(image.Data, image.Width, image.Height);
            if (swatches.Count == 0)
            {
                return null;
            }

            AddToCache(albumArtUrl, swatches);
            return swatches;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AlbumArtColorService] Failed to extract colors from {albumArtUrl}: {ex.Message}");
            return null;
        }
    }

    private void AddToCache(string albumArtUrl, IReadOnlyList<ColorSwatch> swatches)
    {
        lock (_cacheLock)
        {
            if (_cachedSwatchesByUrl.ContainsKey(albumArtUrl))
            {
                return;
            }

            while (_cacheEvictionOrder.Count >= CACHE_CAPACITY)
            {
                string evictedUrl = _cacheEvictionOrder.Dequeue();
                _cachedSwatchesByUrl.Remove(evictedUrl);
            }

            _cachedSwatchesByUrl[albumArtUrl] = swatches;
            _cacheEvictionOrder.Enqueue(albumArtUrl);
        }
    }
}
