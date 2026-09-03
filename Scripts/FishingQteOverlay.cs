using Localizor;
using UnityEngine;

namespace FishingMod
{
    internal static class FishingText
    {
        internal static string FishName(FishingFish fish)
        {
            return fish == null ? string.Empty : Loc(fish.NameKey, fish.FallbackName);
        }

        internal static string Rarity(FishingRarity rarity)
        {
            switch (rarity)
            {
                case FishingRarity.Common: return Loc("fishingmod_rarity_common", "Common");
                case FishingRarity.Uncommon: return Loc("fishingmod_rarity_uncommon", "Uncommon");
                case FishingRarity.Rare: return Loc("fishingmod_rarity_rare", "Rare");
                case FishingRarity.VeryRare: return Loc("fishingmod_rarity_very_rare", "Very rare");
                case FishingRarity.Epic: return Loc("fishingmod_rarity_epic", "Epic");
                default: return Loc("fishingmod_rarity_legendary", "Legendary");
            }
        }

        internal static string Command(FishingQteCommand command)
        {
            switch (command)
            {
                case FishingQteCommand.Up: return Loc("fishingmod_qte_key_up", "UP / W / Z");
                case FishingQteCommand.Left: return Loc("fishingmod_qte_key_left", "LEFT / A / Q");
                case FishingQteCommand.Down: return Loc("fishingmod_qte_key_down", "DOWN / S");
                case FishingQteCommand.Right: return Loc("fishingmod_qte_key_right", "RIGHT / D");
                default: return Loc("fishingmod_qte_key_reel", "SPACE");
            }
        }

        internal static string Hooked(FishingFish fish)
        {
            return Format(Loc("fishingmod_qte_hooked", "{fish} hooked!"), "fish", FishName(fish));
        }

        internal static string RarityAndBonus(FishingFish fish)
        {
            return Format(
                Format(
                    Loc("fishingmod_qte_rarity_bonus", "{rarity}  |  +{bonus}% happiness for 3 days"),
                    "rarity",
                    Rarity(fish.Rarity)),
                "bonus",
                fish.HappinessBonus.ToString());
        }

        internal static string LineRemaining(float meters)
        {
            return Format(
                Loc("fishingmod_qte_line", "{meters} m of line remaining"),
                "meters",
                meters.ToString("0.0"));
        }

        internal static string Instruction => Loc(
            "fishingmod_qte_instruction",
            "Press the shown key. A mistake only releases half of one successful pull.");

        internal static string CancelHint => Loc("fishingmod_qte_cancel", "Escape: abandon the fish");
        internal static string Success => Loc("fishingmod_qte_success", "Good! 3.5 m reeled in");
        internal static string Failure => Loc("fishingmod_qte_failure", "Missed: 1.75 m released");

        internal static string Caught(FishingCatchBonusResult result)
        {
            string caught = FishName(result.CaughtFish);
            if (!result.HappinessEnabled)
                return Format(Loc("fishingmod_result_happiness_disabled", "{fish} caught! Happiness is disabled for this game."), "fish", caught);
            if (!result.CaughtFishIsCounted)
            {
                string message = Loc(
                    "fishingmod_result_best_kept",
                    "{fish} caught! Best active catch remains {best}: +{bonus}% happiness.");
                message = Format(message, "fish", caught);
                message = Format(message, "best", FishName(result.CountedFish));
                return Format(message, "bonus", result.CountedFish.HappinessBonus.ToString());
            }

            string applied = Loc(
                "fishingmod_result_caught",
                "{fish} caught! +{bonus}% happiness for 3 days.");
            applied = Format(applied, "fish", caught);
            return Format(applied, "bonus", result.CaughtFish.HappinessBonus.ToString());
        }

        internal static string Cancelled(FishingFish fish)
        {
            return Format(
                Loc("fishingmod_result_cancelled", "{fish} released. The fishing activity bonus is kept."),
                "fish",
                FishName(fish));
        }

        private static string Loc(string key, string fallback)
        {
            try
            {
                string localized = key.GetLocalization();
                if (!string.IsNullOrWhiteSpace(localized) && localized != key)
                    return localized;
            }
            catch
            {
                // The English fallback keeps the QTE usable if locale registration is late.
            }

            return fallback;
        }

        private static string Format(string text, string token, string value)
        {
            return text.Replace("{" + token + "}", value ?? string.Empty);
        }
    }

    internal sealed class FishingQteOverlay
    {
        private GUIStyle _titleStyle;
        private GUIStyle _detailStyle;
        private GUIStyle _commandStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _toastStyle;

        internal void DrawQte(FishingQteSession session, FishingQteOutcome feedback, bool showFeedback)
        {
            if (session == null) return;
            EnsureStyles();

            float width = Mathf.Min(620f, Screen.width - 32f);
            float height = 286f;
            Rect panel = new Rect((Screen.width - width) * 0.5f, Mathf.Max(24f, Screen.height * 0.07f), width, height);
            DrawSolid(panel, new Color(0.025f, 0.055f, 0.075f, 0.94f));
            DrawSolid(new Rect(panel.x, panel.y, panel.width, 5f), RarityColor(session.Fish.Rarity));

            GUI.Label(new Rect(panel.x + 20f, panel.y + 14f, panel.width - 40f, 36f), FishingText.Hooked(session.Fish), _titleStyle);
            GUI.Label(new Rect(panel.x + 20f, panel.y + 49f, panel.width - 40f, 25f), FishingText.RarityAndBonus(session.Fish), _detailStyle);

            GUI.Label(new Rect(panel.x + 20f, panel.y + 79f, panel.width - 40f, 24f), FishingText.Instruction, _smallStyle);
            GUI.Label(new Rect(panel.x + 20f, panel.y + 102f, panel.width - 40f, 66f), FishingText.Command(session.ExpectedCommand), _commandStyle);

            Rect timeBar = new Rect(panel.x + 32f, panel.y + 169f, panel.width - 64f, 11f);
            DrawProgress(timeBar, session.TimeRemaining / session.Fish.ResponseWindowSeconds, new Color(1f, 0.72f, 0.18f, 1f));

            GUI.Label(new Rect(panel.x + 20f, panel.y + 187f, panel.width - 40f, 23f), FishingText.LineRemaining(session.RemainingLineMeters), _detailStyle);
            Rect lineBar = new Rect(panel.x + 32f, panel.y + 215f, panel.width - 64f, 16f);
            DrawProgress(lineBar, session.Progress, new Color(0.18f, 0.75f, 0.95f, 1f));

            string footer = FishingText.CancelHint;
            if (showFeedback && feedback == FishingQteOutcome.Success) footer = FishingText.Success;
            else if (showFeedback && feedback == FishingQteOutcome.Failure) footer = FishingText.Failure;
            GUI.Label(new Rect(panel.x + 20f, panel.y + 242f, panel.width - 40f, 28f), footer, _smallStyle);
        }

        internal void DrawToast(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            EnsureStyles();
            float width = Mathf.Min(720f, Screen.width - 32f);
            Rect toast = new Rect((Screen.width - width) * 0.5f, Mathf.Max(30f, Screen.height * 0.08f), width, 76f);
            DrawSolid(toast, new Color(0.025f, 0.055f, 0.075f, 0.94f));
            DrawSolid(new Rect(toast.x, toast.y, toast.width, 5f), new Color(0.18f, 0.75f, 0.95f, 1f));
            GUI.Label(new Rect(toast.x + 18f, toast.y + 12f, toast.width - 36f, 54f), text, _toastStyle);
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = NewStyle(25, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            _detailStyle = NewStyle(16, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.72f, 0.91f, 1f));
            _commandStyle = NewStyle(38, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.83f, 0.25f));
            _smallStyle = NewStyle(14, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.88f, 0.92f, 0.94f));
            _toastStyle = NewStyle(19, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            _toastStyle.wordWrap = true;
        }

        private static GUIStyle NewStyle(int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = alignment,
                normal = { textColor = color }
            };
        }

        private static void DrawProgress(Rect rect, float progress, Color fill)
        {
            progress = Mathf.Clamp01(progress);
            DrawSolid(rect, new Color(0f, 0f, 0f, 0.62f));
            Rect inner = new Rect(rect.x + 2f, rect.y + 2f, Mathf.Max(0f, (rect.width - 4f) * progress), rect.height - 4f);
            DrawSolid(inner, fill);
        }

        private static void DrawSolid(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static Color RarityColor(FishingRarity rarity)
        {
            switch (rarity)
            {
                case FishingRarity.Common: return new Color(0.66f, 0.72f, 0.76f);
                case FishingRarity.Uncommon: return new Color(0.30f, 0.82f, 0.42f);
                case FishingRarity.Rare: return new Color(0.25f, 0.55f, 1f);
                case FishingRarity.VeryRare: return new Color(0.63f, 0.34f, 0.95f);
                case FishingRarity.Epic: return new Color(0.95f, 0.35f, 0.75f);
                default: return new Color(1f, 0.70f, 0.12f);
            }
        }
    }
}
