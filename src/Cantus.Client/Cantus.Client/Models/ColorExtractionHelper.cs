using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Windows.UI;

namespace Cantus.Client.Models;

public static class ColorExtractionHelper
{
    private const float GRAYSCALE_SATURATION_THRESHOLD = 0.10f;
    private const float MIN_ACCENT_SATURATION = 0.35f;
    private const float MAX_ACCENT_SATURATION = 0.95f;
    private const float MIN_ACCENT_LIGHTNESS = 0.48f;
    private const float MAX_ACCENT_LIGHTNESS = 0.65f;
    private const float MIN_SECONDARY_HUE_SEPARATION = 30f;
    private const float SECONDARY_HUE_SHIFT = 35f;
    private const float MAX_BACKGROUND_SATURATION = 0.45f;

    public static ColorPalette GeneratePaletteFromMetadata(string? title, string? artist, string? albumArtUrl)
    {
        string seedString = $"{albumArtUrl ?? ""}|{artist ?? ""}|{title ?? ""}";
        if (string.IsNullOrWhiteSpace(seedString) || seedString == "||")
        {
            return ColorPalette.MidnightViolet;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seedString));

        // Derive Hue (0-360), Saturation (0.6-0.9), Lightness (0.4-0.65) for primary accent
        float hue = (hash[0] | (hash[1] << 8)) % 360f;
        float sat = 0.70f + (hash[2] % 25) / 100f; // 0.70 - 0.95
        float lum = 0.50f + (hash[3] % 15) / 100f; // 0.50 - 0.65

        // Primary Accent Color
        Color primaryAccent = HslToRgb(hue, sat, lum);

        // Secondary Accent (complementary or analogous shifted by 35 degrees)
        float secondaryHue = (hue + SECONDARY_HUE_SHIFT) % 360f;
        Color secondaryAccent = HslToRgb(secondaryHue, sat * 0.9f, Math.Min(1.0f, lum + 0.15f));

        return BuildDynamicPalette(title, primaryAccent, secondaryAccent, hue, 0.35f);
    }

    public static ColorPalette GeneratePaletteFromSwatches(string? title, IReadOnlyList<ColorSwatch> swatches)
    {
        if (swatches is null || swatches.Count == 0)
        {
            return ColorPalette.MidnightViolet;
        }

        int maxPopulation = 0;
        int dominantIndex = 0;
        for (int index = 0; index < swatches.Count; index++)
        {
            if (swatches[index].Population > maxPopulation)
            {
                maxPopulation = swatches[index].Population;
                dominantIndex = index;
            }
        }

        int primaryIndex = FindBestSwatch(swatches, maxPopulation, excludeIndex: -1, minHueSeparationFrom: null);
        ColorSwatch primarySwatch = swatches[primaryIndex];

        float primarySaturation = ClampAccentSaturation(primarySwatch.Saturation);
        float primaryLightness = Math.Clamp(primarySwatch.Lightness, MIN_ACCENT_LIGHTNESS, MAX_ACCENT_LIGHTNESS);
        Color primaryAccent = HslToRgb(primarySwatch.Hue, primarySaturation, primaryLightness);

        int secondaryIndex = FindBestSwatch(swatches, maxPopulation, excludeIndex: primaryIndex, minHueSeparationFrom: primarySwatch.Hue);
        Color secondaryAccent;
        if (secondaryIndex >= 0)
        {
            ColorSwatch secondarySwatch = swatches[secondaryIndex];
            float secondarySaturation = ClampAccentSaturation(secondarySwatch.Saturation) * 0.9f;
            float secondaryLightness = Math.Min(
                1.0f,
                Math.Clamp(secondarySwatch.Lightness, MIN_ACCENT_LIGHTNESS, MAX_ACCENT_LIGHTNESS) + 0.15f);
            secondaryAccent = HslToRgb(secondarySwatch.Hue, secondarySaturation, secondaryLightness);
        }
        else
        {
            float secondaryHue = (primarySwatch.Hue + SECONDARY_HUE_SHIFT) % 360f;
            secondaryAccent = HslToRgb(secondaryHue, primarySaturation * 0.9f, Math.Min(1.0f, primaryLightness + 0.15f));
        }

        ColorSwatch dominantSwatch = swatches[dominantIndex];
        float backgroundSaturation = Math.Min(dominantSwatch.Saturation, MAX_BACKGROUND_SATURATION);

        return BuildDynamicPalette(title, primaryAccent, secondaryAccent, dominantSwatch.Hue, backgroundSaturation);
    }

    public static Color HslToRgb(float h, float s, float l)
    {
        float r, g, b;

        if (s == 0)
        {
            r = g = b = l; // achromatic
        }
        else
        {
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;
            r = HueToRgb(p, q, h / 360f + 1f / 3f);
            g = HueToRgb(p, q, h / 360f);
            b = HueToRgb(p, q, h / 360f - 1f / 3f);
        }

        return Color.FromArgb(
            255,
            (byte)Math.Clamp((int)Math.Round(r * 255f), 0, 255),
            (byte)Math.Clamp((int)Math.Round(g * 255f), 0, 255),
            (byte)Math.Clamp((int)Math.Round(b * 255f), 0, 255)
        );
    }

    public static (float Hue, float Saturation, float Lightness) RgbToHsl(Color color)
    {
        float r = color.R / 255f;
        float g = color.G / 255f;
        float b = color.B / 255f;

        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float lightness = (max + min) / 2f;

        if (max == min)
        {
            return (0f, 0f, lightness); // achromatic
        }

        float delta = max - min;
        float saturation = lightness > 0.5f
            ? delta / (2f - max - min)
            : delta / (max + min);

        float hue;
        if (max == r)
        {
            hue = (g - b) / delta + (g < b ? 6f : 0f);
        }
        else if (max == g)
        {
            hue = (b - r) / delta + 2f;
        }
        else
        {
            hue = (r - g) / delta + 4f;
        }

        return (hue * 60f, saturation, lightness);
    }

    private static ColorPalette BuildDynamicPalette(
        string? title,
        Color primaryAccent,
        Color secondaryAccent,
        float backgroundHue,
        float backgroundSaturation)
    {
        // Dark Background (Hue matched, very low lightness)
        Color background = HslToRgb(backgroundHue, backgroundSaturation, 0.05f);

        // Surface Card (Translucent 80%, slightly lighter)
        Color surfaceCardRgb = HslToRgb(backgroundHue, 0.28f, 0.09f);
        Color surfaceCard = Color.FromArgb(204, surfaceCardRgb.R, surfaceCardRgb.G, surfaceCardRgb.B);

        // Subtle Card Border (Translucent 20% primary accent)
        Color cardBorder = Color.FromArgb(40, primaryAccent.R, primaryAccent.G, primaryAccent.B);

        // Glow Color (25% opacity primary)
        Color glowColor = Color.FromArgb(60, primaryAccent.R, primaryAccent.G, primaryAccent.B);

        return new ColorPalette(
            Name: $"Dynamic ({title ?? "Track"})",
            Background: background,
            SurfaceCard: surfaceCard,
            CardBorder: cardBorder,
            PrimaryAccent: primaryAccent,
            SecondaryAccent: secondaryAccent,
            TextPrimary: Color.FromArgb(255, 248, 250, 252),
            TextSecondary: Color.FromArgb(255, 203, 213, 225),
            TextMuted: Color.FromArgb(255, 100, 116, 139),
            GlowColor: glowColor,
            ActiveLyricColor: Color.FromArgb(255, 255, 255, 255),
            PastLyricColor: Color.FromArgb(120, 100, 116, 139),
            UpcomingLyricColor: Color.FromArgb(200, 148, 163, 184)
        );
    }

    private static int FindBestSwatch(
        IReadOnlyList<ColorSwatch> swatches,
        int maxPopulation,
        int excludeIndex,
        float? minHueSeparationFrom)
    {
        int bestIndex = -1;
        float bestScore = float.MinValue;

        for (int index = 0; index < swatches.Count; index++)
        {
            if (index == excludeIndex)
            {
                continue;
            }

            if (minHueSeparationFrom is float referenceHue
                && HueDistance(swatches[index].Hue, referenceHue) < MIN_SECONDARY_HUE_SEPARATION)
            {
                continue;
            }

            float score = ScoreSwatch(swatches[index], maxPopulation);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static float ScoreSwatch(ColorSwatch swatch, int maxPopulation)
    {
        float populationScore = maxPopulation > 0 ? (float)swatch.Population / maxPopulation : 0f;
        float lightnessScore = 1f - 2f * Math.Abs(swatch.Lightness - 0.5f);
        return 3f * swatch.Saturation + populationScore + lightnessScore;
    }

    private static float HueDistance(float hueA, float hueB)
    {
        float distance = Math.Abs(hueA - hueB) % 360f;
        return distance > 180f ? 360f - distance : distance;
    }

    private static float ClampAccentSaturation(float saturation)
    {
        // Genuinely grayscale artwork keeps its neutral character instead of
        // being forced into an artificially colorful accent.
        return saturation < GRAYSCALE_SATURATION_THRESHOLD
            ? saturation
            : Math.Clamp(saturation, MIN_ACCENT_SATURATION, MAX_ACCENT_SATURATION);
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }
}
