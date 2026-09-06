using System;
using System.Collections.Generic;
using Cantus.Client.Models;
using FluentAssertions;
using Windows.UI;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class ColorQuantizerTests
{
    private static byte[] CreateSolidBuffer(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        byte[] buffer = new byte[width * height * 4];
        for (int pixelIndex = 0; pixelIndex < width * height; pixelIndex++)
        {
            int offset = pixelIndex * 4;
            buffer[offset] = r;
            buffer[offset + 1] = g;
            buffer[offset + 2] = b;
            buffer[offset + 3] = a;
        }

        return buffer;
    }

    private static void SetPixel(byte[] buffer, int pixelIndex, byte r, byte g, byte b, byte a = 255)
    {
        int offset = pixelIndex * 4;
        buffer[offset] = r;
        buffer[offset + 1] = g;
        buffer[offset + 2] = b;
        buffer[offset + 3] = a;
    }

    [Fact]
    public void Quantize_SolidRedBuffer_ReturnsSingleDominantRedSwatch()
    {
        // Arrange
        byte[] buffer = CreateSolidBuffer(16, 16, 255, 0, 0);

        // Act
        IReadOnlyList<ColorSwatch> swatches = ColorQuantizer.Quantize(buffer, 16, 16);

        // Assert
        swatches.Should().HaveCount(1);
        swatches[0].Population.Should().Be(256);
        swatches[0].Color.Should().Be(Color.FromArgb(255, 255, 0, 0));
        swatches[0].Hue.Should().BeApproximately(0f, 1f);
        swatches[0].Saturation.Should().BeApproximately(1f, 0.01f);
        swatches[0].Lightness.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Quantize_TwoToneBuffer_RanksSwatchesByPopulation()
    {
        // Arrange - 48 blue pixels, 16 yellow pixels
        byte[] buffer = CreateSolidBuffer(8, 8, 0, 0, 255);
        for (int pixelIndex = 48; pixelIndex < 64; pixelIndex++)
        {
            SetPixel(buffer, pixelIndex, 255, 255, 0);
        }

        // Act
        IReadOnlyList<ColorSwatch> swatches = ColorQuantizer.Quantize(buffer, 8, 8);

        // Assert
        swatches.Should().HaveCount(2);
        swatches[0].Population.Should().Be(48);
        swatches[0].Color.Should().Be(Color.FromArgb(255, 0, 0, 255));
        swatches[1].Population.Should().Be(16);
        swatches[1].Color.Should().Be(Color.FromArgb(255, 255, 255, 0));
    }

    [Fact]
    public void Quantize_NearBlackAndNearWhitePixels_AreExcluded()
    {
        // Arrange - 32 black, 16 white, 16 red
        byte[] buffer = CreateSolidBuffer(8, 8, 0, 0, 0);
        for (int pixelIndex = 32; pixelIndex < 48; pixelIndex++)
        {
            SetPixel(buffer, pixelIndex, 255, 255, 255);
        }

        for (int pixelIndex = 48; pixelIndex < 64; pixelIndex++)
        {
            SetPixel(buffer, pixelIndex, 200, 0, 0);
        }

        // Act
        IReadOnlyList<ColorSwatch> swatches = ColorQuantizer.Quantize(buffer, 8, 8);

        // Assert
        swatches.Should().HaveCount(1);
        swatches[0].Population.Should().Be(16);
        swatches[0].Color.R.Should().Be(200);
        swatches[0].Color.G.Should().Be(0);
    }

    [Fact]
    public void Quantize_TransparentPixels_AreExcluded()
    {
        // Arrange - 32 transparent red, 32 opaque green
        byte[] buffer = CreateSolidBuffer(8, 8, 255, 0, 0, a: 0);
        for (int pixelIndex = 32; pixelIndex < 64; pixelIndex++)
        {
            SetPixel(buffer, pixelIndex, 0, 200, 0);
        }

        // Act
        IReadOnlyList<ColorSwatch> swatches = ColorQuantizer.Quantize(buffer, 8, 8);

        // Assert
        swatches.Should().HaveCount(1);
        swatches[0].Population.Should().Be(32);
        swatches[0].Color.G.Should().Be(200);
        swatches[0].Color.R.Should().Be(0);
    }

    [Fact]
    public void Quantize_AllPixelsFiltered_ReturnsEmptyList()
    {
        // Arrange - pure black image (below the lightness floor)
        byte[] buffer = CreateSolidBuffer(8, 8, 0, 0, 0);

        // Act
        IReadOnlyList<ColorSwatch> swatches = ColorQuantizer.Quantize(buffer, 8, 8);

        // Assert
        swatches.Should().BeEmpty();
    }

    [Fact]
    public void Quantize_InvalidInput_ReturnsEmptyList()
    {
        // Act & Assert
        ColorQuantizer.Quantize(null!, 8, 8).Should().BeEmpty();
        ColorQuantizer.Quantize(Array.Empty<byte>(), 8, 8).Should().BeEmpty();
        ColorQuantizer.Quantize(new byte[16], 0, 8).Should().BeEmpty();
        ColorQuantizer.Quantize(new byte[16], 8, -1).Should().BeEmpty();
        ColorQuantizer.Quantize(new byte[16], 8, 8).Should().BeEmpty();
    }

    [Fact]
    public void Quantize_ManyDistinctColors_ReturnsAtMostEightSwatches()
    {
        // Arrange - 32 distinct hues, one row each
        byte[] buffer = new byte[32 * 32 * 4];
        for (int y = 0; y < 32; y++)
        {
            Color rowColor = ColorExtractionHelper.HslToRgb(y * (360f / 32f), 0.8f, 0.5f);
            for (int x = 0; x < 32; x++)
            {
                SetPixel(buffer, y * 32 + x, rowColor.R, rowColor.G, rowColor.B);
            }
        }

        // Act
        IReadOnlyList<ColorSwatch> swatches = ColorQuantizer.Quantize(buffer, 32, 32);

        // Assert
        swatches.Should().NotBeEmpty();
        swatches.Count.Should().BeLessThanOrEqualTo(8);
    }

    [Fact]
    public void Quantize_LargeNoisyBuffer_DownsamplesAndCompletes()
    {
        // Arrange - 640x640 of seeded pseudo-random colors
        Random random = new(42);
        byte[] buffer = new byte[640 * 640 * 4];
        random.NextBytes(buffer);
        for (int pixelIndex = 0; pixelIndex < 640 * 640; pixelIndex++)
        {
            buffer[pixelIndex * 4 + 3] = 255;
        }

        // Act
        IReadOnlyList<ColorSwatch> swatches = ColorQuantizer.Quantize(buffer, 640, 640);

        // Assert
        swatches.Should().NotBeEmpty();
        swatches.Count.Should().BeLessThanOrEqualTo(8);

        int totalPopulation = 0;
        foreach (ColorSwatch swatch in swatches)
        {
            totalPopulation += swatch.Population;
        }

        // Downsampling caps the number of sampled pixels well below the raw pixel count.
        totalPopulation.Should().BeGreaterThan(0);
        totalPopulation.Should().BeLessThanOrEqualTo(20000);
    }

    [Fact]
    public void Quantize_SolidBuffer_PopulationMatchesSampledPixelCount()
    {
        // Arrange - small enough that every pixel is sampled (step == 1)
        byte[] buffer = CreateSolidBuffer(100, 100, 30, 144, 255);

        // Act
        IReadOnlyList<ColorSwatch> swatches = ColorQuantizer.Quantize(buffer, 100, 100);

        // Assert
        swatches.Should().HaveCount(1);
        swatches[0].Population.Should().Be(10000);
    }
}
