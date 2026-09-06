using System;
using System.Collections.Generic;
using Windows.UI;

namespace Cantus.Client.Models;

public sealed record ColorSwatch(Color Color, int Population, float Hue, float Saturation, float Lightness);

/// <summary>
/// Extracts dominant colors from raw RGBA pixel data using median-cut quantization.
/// </summary>
public static class ColorQuantizer
{
    private const int MAX_SWATCH_COUNT = 8;
    private const int TARGET_SAMPLE_COUNT = 16384;
    private const float MIN_SAMPLE_LIGHTNESS = 0.05f;
    private const float MAX_SAMPLE_LIGHTNESS = 0.95f;
    private const byte MIN_SAMPLE_ALPHA = 128;
    private const int CHANNEL_SHIFT = 3;

    private sealed class ColorBin
    {
        public byte R5;
        public byte G5;
        public byte B5;
        public int Population;
        public long SumR;
        public long SumG;
        public long SumB;
    }

    /// <summary>
    /// Quantizes an RGBA pixel buffer into up to eight dominant color swatches,
    /// sorted by population descending. Near-black, near-white, and mostly
    /// transparent pixels are excluded so that letterboxing and vignettes do not
    /// dominate the result. Returns an empty list for degenerate input.
    /// </summary>
    public static IReadOnlyList<ColorSwatch> Quantize(byte[] rgbaPixels, int width, int height)
    {
        if (rgbaPixels is null || width <= 0 || height <= 0)
        {
            return Array.Empty<ColorSwatch>();
        }

        int pixelCount = width * height;
        if (rgbaPixels.Length < pixelCount * 4)
        {
            return Array.Empty<ColorSwatch>();
        }

        Dictionary<int, ColorBin> histogram = BuildHistogram(rgbaPixels, pixelCount);
        if (histogram.Count == 0)
        {
            return Array.Empty<ColorSwatch>();
        }

        List<ColorBin> bins = new(histogram.Values);
        List<(int Start, int Length)> boxes = new() { (0, bins.Count) };

        while (boxes.Count < MAX_SWATCH_COUNT)
        {
            int boxToSplit = FindWidestSplittableBox(bins, boxes);
            if (boxToSplit < 0)
            {
                break;
            }

            (int start, int length) = boxes[boxToSplit];
            int firstHalfLength = SplitAtPopulationMedian(bins, start, length);
            boxes[boxToSplit] = (start, firstHalfLength);
            boxes.Add((start + firstHalfLength, length - firstHalfLength));
        }

        List<ColorSwatch> swatches = new(boxes.Count);
        foreach ((int start, int length) in boxes)
        {
            swatches.Add(BuildSwatch(bins, start, length));
        }

        swatches.Sort((left, right) => right.Population.CompareTo(left.Population));
        return swatches;
    }

    private static Dictionary<int, ColorBin> BuildHistogram(byte[] rgbaPixels, int pixelCount)
    {
        int step = Math.Max(1, pixelCount / TARGET_SAMPLE_COUNT);
        Dictionary<int, ColorBin> histogram = new();

        for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex += step)
        {
            int offset = pixelIndex * 4;
            byte r = rgbaPixels[offset];
            byte g = rgbaPixels[offset + 1];
            byte b = rgbaPixels[offset + 2];
            byte a = rgbaPixels[offset + 3];

            if (a < MIN_SAMPLE_ALPHA)
            {
                continue;
            }

            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));
            float lightness = (max + min) / 510f;
            if (lightness < MIN_SAMPLE_LIGHTNESS || lightness > MAX_SAMPLE_LIGHTNESS)
            {
                continue;
            }

            int key = ((r >> CHANNEL_SHIFT) << 10) | ((g >> CHANNEL_SHIFT) << 5) | (b >> CHANNEL_SHIFT);
            if (!histogram.TryGetValue(key, out ColorBin? bin))
            {
                bin = new ColorBin
                {
                    R5 = (byte)(r >> CHANNEL_SHIFT),
                    G5 = (byte)(g >> CHANNEL_SHIFT),
                    B5 = (byte)(b >> CHANNEL_SHIFT)
                };
                histogram[key] = bin;
            }

            bin.Population++;
            bin.SumR += r;
            bin.SumG += g;
            bin.SumB += b;
        }

        return histogram;
    }

    private static int FindWidestSplittableBox(List<ColorBin> bins, List<(int Start, int Length)> boxes)
    {
        int widestBoxIndex = -1;
        int widestRange = 0;

        for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
        {
            (int start, int length) = boxes[boxIndex];
            if (length < 2)
            {
                continue;
            }

            (int rangeR, int rangeG, int rangeB) = GetChannelRanges(bins, start, length);
            int maxRange = Math.Max(rangeR, Math.Max(rangeG, rangeB));
            if (maxRange > widestRange)
            {
                widestRange = maxRange;
                widestBoxIndex = boxIndex;
            }
        }

        return widestBoxIndex;
    }

    private static (int RangeR, int RangeG, int RangeB) GetChannelRanges(List<ColorBin> bins, int start, int length)
    {
        int minR = int.MaxValue;
        int maxR = int.MinValue;
        int minG = int.MaxValue;
        int maxG = int.MinValue;
        int minB = int.MaxValue;
        int maxB = int.MinValue;

        for (int index = start; index < start + length; index++)
        {
            ColorBin bin = bins[index];
            minR = Math.Min(minR, bin.R5);
            maxR = Math.Max(maxR, bin.R5);
            minG = Math.Min(minG, bin.G5);
            maxG = Math.Max(maxG, bin.G5);
            minB = Math.Min(minB, bin.B5);
            maxB = Math.Max(maxB, bin.B5);
        }

        return (maxR - minR, maxG - minG, maxB - minB);
    }

    private static int SplitAtPopulationMedian(List<ColorBin> bins, int start, int length)
    {
        (int rangeR, int rangeG, int rangeB) = GetChannelRanges(bins, start, length);

        Comparison<ColorBin> comparison;
        if (rangeR >= rangeG && rangeR >= rangeB)
        {
            comparison = (left, right) => left.R5.CompareTo(right.R5);
        }
        else if (rangeG >= rangeB)
        {
            comparison = (left, right) => left.G5.CompareTo(right.G5);
        }
        else
        {
            comparison = (left, right) => left.B5.CompareTo(right.B5);
        }

        bins.Sort(start, length, Comparer<ColorBin>.Create(comparison));

        long totalPopulation = 0;
        for (int index = start; index < start + length; index++)
        {
            totalPopulation += bins[index].Population;
        }

        long cumulativePopulation = 0;
        for (int index = start; index < start + length - 1; index++)
        {
            cumulativePopulation += bins[index].Population;
            if (cumulativePopulation * 2 >= totalPopulation)
            {
                return index - start + 1;
            }
        }

        return length - 1;
    }

    private static ColorSwatch BuildSwatch(List<ColorBin> bins, int start, int length)
    {
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        int population = 0;

        for (int index = start; index < start + length; index++)
        {
            ColorBin bin = bins[index];
            sumR += bin.SumR;
            sumG += bin.SumG;
            sumB += bin.SumB;
            population += bin.Population;
        }

        int divisor = Math.Max(1, population);
        Color color = Color.FromArgb(
            255,
            (byte)(sumR / divisor),
            (byte)(sumG / divisor),
            (byte)(sumB / divisor));

        (float hue, float saturation, float lightness) = ColorExtractionHelper.RgbToHsl(color);
        return new ColorSwatch(color, population, hue, saturation, lightness);
    }
}
