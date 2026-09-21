using System;

namespace FishingMod
{
    internal static class FishingMath
    {
        internal const float ReleaseTime = 1.05f;
        internal const float FlightDuration = 0.90f;
        internal const float SequenceDuration = 3.35f;

        internal static float FlightSeconds(float distance) => Math.Max(0.55f, Math.Min(3f, distance / 22f));

        internal static float Clamp01(float value)
        {
            if (value <= 0f) return 0f;
            return value >= 1f ? 1f : value;
        }

        internal static float Smooth01(float value)
        {
            value = Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        internal static float Segment(float value, float start, float end)
        {
            if (end <= start) return value >= end ? 1f : 0f;
            return Smooth01((value - start) / (end - start));
        }

        internal static float BallisticHeight(float progress, float apexHeight)
        {
            progress = Clamp01(progress);
            return 4f * apexHeight * progress * (1f - progress);
        }

        internal static int VisibleProgressSegments(float progress, int totalSegments)
        {
            if (totalSegments <= 0) return 0;
            float clamped = Clamp01(progress);
            return clamped <= 0f ? 0 : (int)Math.Ceiling(clamped * totalSegments);
        }

        internal static float ShoreScore(float horizontalWaterDistance, float pathLength, float verticalDifference)
        {
            return Math.Max(0f, horizontalWaterDistance) * 1000f
                 + Math.Max(0f, verticalDifference) * 10f
                 + Math.Max(0f, pathLength);
        }

        internal static bool LooksLikeWater(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string text = value.Trim();
            // Imported assets commonly use WaterSurface, Lake01 or OceanMesh, not separate words.
            var words = new System.Text.StringBuilder(text.Length + 8);
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0 && char.IsUpper(text[i]) && char.IsLower(text[i - 1])) words.Append(' ');
                words.Append(char.ToLowerInvariant(text[i]));
            }
            text = words.ToString();
            string compact = text.Replace(" ", "").Replace("_", "");
            if (compact.Contains("waterpedestrian") || compact.Contains("waterbottle")
                || compact.Contains("coolant") || compact.Contains("shower") || compact.Contains("drinkingwater")
                || compact.Contains("watertower") || compact.Contains("waterpipe") || compact.Contains("waterfall")
                || compact.Contains("lakehouse") || compact.Contains("waterfront"))
                return false;

            string[] tokens = { "water", "ocean", "river", "canal", "sea", "lake", "pond", "lagoon" };
            for (int i = 0; i < tokens.Length; i++)
            {
                int index = 0;
                while ((index = text.IndexOf(tokens[i], index, StringComparison.Ordinal)) >= 0)
                {
                    int before = index - 1;
                    int after = index + tokens[i].Length;
                    bool leftBoundary = before < 0 || !char.IsLetterOrDigit(text[before]);
                    bool rightBoundary = after >= text.Length || !char.IsLetter(text[after]);
                    if (leftBoundary && rightBoundary) return true;
                    index++;
                }
            }

            return false;
        }
    }
}
