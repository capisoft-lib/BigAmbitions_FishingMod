using System.Globalization;
using BigAmbitions.Mods;
using BigAmbitions.ModsInternal;
using HarmonyLib;
using Localizor.LanguageChangeEvent;
using UnityEngine;
using UnityEngine.UI;

namespace FishingMod
{
    internal static class FishingOptions
    {
        private const string ToggleId = "enable_qte";
        private const string DifficultyId = "difficulty_tenths";
        private static string _modId;
        private static Harmony _harmony;
        internal static bool EnableQte { get; private set; } = true;
        internal static float Difficulty { get; private set; } = 1f;

        internal static void Register(string modId)
        {
            _modId = modId;
            // Same persisted keys as the native ModOptionPrefs implementation.
            EnableQte = UnityEngine.PlayerPrefs.GetInt("m:" + modId + ":" + ToggleId, 1) != 0;
            Difficulty = Mathf.Clamp(UnityEngine.PlayerPrefs.GetInt("m:" + modId + ":" + DifficultyId, 10), 2, 50) / 10f;
            _harmony = new Harmony("capisoft.fishingmod.options");
            _harmony.Patch(AccessTools.Method(typeof(ModOptionsSliderControl), "Initialize"),
                postfix: new HarmonyMethod(typeof(FishingOptions), nameof(FormatDifficulty)));
            OptionsService.OnReset += Reset;
            OptionsService.Register(modId, new ModOptions()
                .AddToggle(ToggleId, "fishingmod_enable_qte", true, value => EnableQte = value)
                .AddSlider(DifficultyId, "fishingmod_difficulty", 2, 50, 10,
                    value => Difficulty = Mathf.Clamp(value, 2, 50) / 10f, "fishingmod_difficulty_value"));
        }

        private static void Reset() { EnableQte = true; Difficulty = 1f; }

        // The native slider stores integers. Keep native persistence/reset and
        // display tenths as 0.2..5.0, scoped strictly to this mod's slider.
        private static void FormatDifficulty(ModOption option, Slider ___slider, TextLocalizationComponent ___valueLabel)
        {
            if (!(option is SliderOption data) || data.ModId != _modId || data.Id != DifficultyId) return;
            void Display(float value) => ___valueLabel.Arguments = new
            {
                value = (value / 10f).ToString("0.0", CultureInfo.CurrentCulture)
            };
            ___slider.onValueChanged.AddListener(Display);
            Display(___slider.value);
        }

        internal static void Unregister()
        {
            OptionsService.OnReset -= Reset;
            if (_modId != null) OptionsService.RemoveModOptions(_modId);
            _harmony?.UnpatchAll(_harmony.Id);
            _harmony = null;
            _modId = null;
        }
    }
}
