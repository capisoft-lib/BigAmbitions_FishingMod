using Localizor;
using UnityEngine;
using UnityEngine.Rendering;

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
        private const int ProgressSegments = 96;
        private const int CircleSegments = 48;

        private GUIStyle _titleStyle;
        private GUIStyle _detailStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _microStyle;
        private GUIStyle _toastStyle;
        private Material _wheelMaterial;

        internal void DrawQte(FishingQteSession session, FishingQteOutcome feedback, bool showFeedback)
        {
            if (session == null) return;
            EnsureStyles();

            Rect wheel = CenteredWheelRect(Screen.width, Screen.height);
            DrawControlWheel(wheel, session.ExpectedCommand, session.Progress);

            float labelWidth = Mathf.Min(620f, Screen.width - 24f);
            float labelX = (Screen.width - labelWidth) * 0.5f;
            DrawShadowedLabel(new Rect(labelX, wheel.y - 72f, labelWidth, 36f), FishingText.Hooked(session.Fish), _titleStyle);
            DrawShadowedLabel(new Rect(labelX, wheel.y - 39f, labelWidth, 25f), FishingText.RarityAndBonus(session.Fish), _detailStyle);

            GUI.Label(
                new Rect(wheel.center.x - 44f, wheel.center.y + wheel.height * 0.115f, 88f, 18f),
                FishingText.Command(FishingQteCommand.Reel),
                _microStyle);

            DrawShadowedLabel(
                new Rect(labelX, wheel.yMax + 10f, labelWidth, 25f),
                FishingText.LineRemaining(session.RemainingLineMeters),
                _detailStyle);

            string footer = FishingText.CancelHint;
            if (showFeedback && feedback == FishingQteOutcome.Success) footer = FishingText.Success;
            else if (showFeedback && feedback == FishingQteOutcome.Failure) footer = FishingText.Failure;
            DrawShadowedLabel(new Rect(labelX, wheel.yMax + 38f, labelWidth, 24f), footer, _smallStyle);
            DrawShadowedLabel(new Rect(labelX, wheel.yMax + 62f, labelWidth, 24f), FishingText.Instruction, _smallStyle);
        }

        internal static Rect CenteredWheelRect(float screenWidth, float screenHeight)
        {
            float diameter = Mathf.Clamp(Mathf.Min(screenWidth, screenHeight) * 0.34f, 250f, 340f);
            return new Rect((screenWidth - diameter) * 0.5f, (screenHeight - diameter) * 0.5f, diameter, diameter);
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
            _smallStyle = NewStyle(14, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.88f, 0.92f, 0.94f));
            _microStyle = NewStyle(11, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.22f, 0.24f, 0.25f));
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

        private void DrawControlWheel(Rect rect, FishingQteCommand command, float progress)
        {
            if (Event.current.type != EventType.Repaint || !EnsureWheelMaterial()) return;

            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            Color pad = new Color(0.92f, 0.93f, 0.92f, 0.98f);
            Color inactive = new Color(0.55f, 0.57f, 0.58f, 1f);
            Color active = new Color(0.025f, 0.035f, 0.04f, 1f);
            Color progressTrack = new Color(0.31f, 0.35f, 0.36f, 1f);
            Color progressFill = new Color(0.05f, 0.78f, 0.34f, 1f);
            float scale = rect.width / 200f;

            GL.PushMatrix();
            try
            {
                _wheelMaterial.SetPass(0);
                GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
                GL.Begin(GL.TRIANGLES);
                AddFilledCircle(center + Vector2.up * 5f * scale, radius - 7f * scale, new Color(0f, 0f, 0f, 0.34f), CircleSegments);
                AddFilledCircle(center, radius - 13f * scale, pad, CircleSegments);
                AddRing(center, radius - 13f * scale, 2f * scale, CircleSegments, CircleSegments, new Color(1f, 1f, 1f, 0.72f));
                AddRing(center, radius - 2f * scale, 7f * scale, ProgressSegments, ProgressSegments, progressTrack);
                AddRing(center, radius - 2f * scale, 7f * scale, FishingMath.VisibleProgressSegments(progress, ProgressSegments), ProgressSegments, progressFill);

                float arrowOffset = 48f * scale;
                AddArrow(center + Vector2.up * -arrowOffset, Vector2.up * -1f, command == FishingQteCommand.Up ? active : inactive, scale);
                AddArrow(center + Vector2.left * arrowOffset, Vector2.left, command == FishingQteCommand.Left ? active : inactive, scale);
                AddArrow(center + Vector2.up * arrowOffset, Vector2.up, command == FishingQteCommand.Down ? active : inactive, scale);
                AddArrow(center + Vector2.right * arrowOffset, Vector2.right, command == FishingQteCommand.Right ? active : inactive, scale);
                float spaceScale = command == FishingQteCommand.Reel ? scale * 1.08f : scale;
                AddRing(center, 21f * spaceScale, 8f * spaceScale, CircleSegments, CircleSegments, command == FishingQteCommand.Reel ? active : inactive);
                GL.End();
            }
            finally
            {
                GL.PopMatrix();
            }
        }

        private static void AddFilledCircle(Vector2 center, float radius, Color color, int segments)
        {
            GL.Color(color);
            float step = Mathf.PI * 2f / segments;
            for (int i = 0; i < segments; i++)
            {
                float angle0 = i * step;
                float angle1 = angle0 + step;
                AddTriangle(
                    center,
                    center + new Vector2(Mathf.Cos(angle0), Mathf.Sin(angle0)) * radius,
                    center + new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1)) * radius);
            }
        }

        private static void AddRing(Vector2 center, float outerRadius, float thickness, int visibleSegments, int totalSegments, Color color)
        {
            if (visibleSegments <= 0 || totalSegments <= 0) return;
            visibleSegments = Mathf.Min(visibleSegments, totalSegments);
            float innerRadius = Mathf.Max(0f, outerRadius - thickness);
            float angleStep = Mathf.PI * 2f / totalSegments;
            float startAngle = -Mathf.PI * 0.5f;
            GL.Color(color);
            for (int i = 0; i < visibleSegments; i++)
            {
                float angle0 = startAngle + i * angleStep;
                float angle1 = angle0 + angleStep;
                Vector2 outer0 = center + new Vector2(Mathf.Cos(angle0), Mathf.Sin(angle0)) * outerRadius;
                Vector2 outer1 = center + new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1)) * outerRadius;
                Vector2 inner0 = center + new Vector2(Mathf.Cos(angle0), Mathf.Sin(angle0)) * innerRadius;
                Vector2 inner1 = center + new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1)) * innerRadius;
                AddQuad(outer0, outer1, inner1, inner0);
            }
        }

        private static void AddArrow(Vector2 center, Vector2 direction, Color color, float scale)
        {
            direction.Normalize();
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            Vector2 tail = center - direction * 18f * scale;
            Vector2 headBase = center + direction * 5f * scale;
            Vector2 tip = center + direction * 22f * scale;
            GL.Color(color);
            AddQuad(
                tail - perpendicular * 4f * scale,
                headBase - perpendicular * 4f * scale,
                headBase + perpendicular * 4f * scale,
                tail + perpendicular * 4f * scale);
            AddFilledCircle(tail, 4f * scale, color, 12);
            AddTriangle(tip, headBase - perpendicular * 13f * scale, headBase + perpendicular * 13f * scale);
        }

        private static void DrawShadowedLabel(Rect rect, string text, GUIStyle style)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
            GUI.color = previous;
            GUI.Label(rect, text, style);
        }

        private static void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            AddTriangle(a, b, c);
            AddTriangle(a, c, d);
        }

        private static void AddTriangle(Vector2 a, Vector2 b, Vector2 c)
        {
            GL.Vertex3(a.x, a.y, 0f);
            GL.Vertex3(b.x, b.y, 0f);
            GL.Vertex3(c.x, c.y, 0f);
        }

        private bool EnsureWheelMaterial()
        {
            if (_wheelMaterial != null) return true;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return false;

            _wheelMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _wheelMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _wheelMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _wheelMaterial.SetInt("_Cull", (int)CullMode.Off);
            _wheelMaterial.SetInt("_ZWrite", 0);
            _wheelMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            return true;
        }

        internal void Dispose()
        {
            if (_wheelMaterial == null) return;
            Object.Destroy(_wheelMaterial);
            _wheelMaterial = null;
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
