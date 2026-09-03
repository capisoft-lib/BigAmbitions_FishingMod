using System;
using System.Collections.Generic;
using System.Reflection;
using Helpers;
using UnityEngine;

namespace FishingMod
{
    internal readonly struct FishingCatchBonusResult
    {
        internal FishingCatchBonusResult(FishingFish caughtFish, FishingFish countedFish, bool happinessEnabled)
        {
            CaughtFish = caughtFish;
            CountedFish = countedFish;
            HappinessEnabled = happinessEnabled;
        }

        internal FishingFish CaughtFish { get; }
        internal FishingFish CountedFish { get; }
        internal bool HappinessEnabled { get; }
        internal bool CaughtFishIsCounted => CaughtFish == CountedFish;
    }

    internal sealed class FishingHappinessService
    {
        internal const string FishingActivityType = "fishingmod_happiness_activity";
        internal const int FishingActivityAmount = 10;
        internal const int FishingActivityHours = 48;
        internal const int CatchBonusHours = 72;

        private static readonly FieldInfo ModifiersField = typeof(HappinessHelper).GetField(
            "Modifiers",
            BindingFlags.Static | BindingFlags.NonPublic);

        internal static bool HasNativeContract =>
            ModifiersField != null
            && typeof(HappinessHelper).GetMethod(
                "AddModifier",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { typeof(string), typeof(int), typeof(bool) },
                null) != null
            && typeof(HappinessHelper).GetMethod(
                "UpdateHappiness",
                BindingFlags.Static | BindingFlags.Public) != null
            && typeof(SaveGameManager).GetMethod(
                "MarkChange",
                BindingFlags.Static | BindingFlags.Public) != null;

        internal void Initialize()
        {
            EnsureDefinitionsRegistered();
            if (SaveGameManager.Current != null)
                HappinessHelper.UpdateHappiness();
        }

        internal bool ApplyFishingActivity()
        {
            GameInstance save = SaveGameManager.Current;
            if (save == null || IsHappinessDisabled(save)) return false;

            EnsureDefinitionsRegistered();
            HappinessHelper.AddModifier(FishingActivityType, FishingActivityHours, additiveHours: false);
            SaveGameManager.MarkChange();
            return true;
        }

        internal FishingCatchBonusResult ApplyCatch(FishingFish caughtFish)
        {
            if (caughtFish == null) throw new ArgumentNullException(nameof(caughtFish));
            GameInstance save = SaveGameManager.Current;
            if (save == null || IsHappinessDisabled(save))
                return new FishingCatchBonusResult(caughtFish, caughtFish, happinessEnabled: false);

            EnsureDefinitionsRegistered();
            if (save.happinessModifiers == null)
                save.happinessModifiers = new List<HappinessModifierData>();

            FishingFish activeBest = null;
            for (int i = 0; i < save.happinessModifiers.Count; i++)
            {
                HappinessModifierData data = save.happinessModifiers[i];
                if (data == null || data.hoursLeft <= 0) continue;
                activeBest = FishingFishCatalog.BetterOf(
                    activeBest,
                    FishingFishCatalog.FindByModifierType(data.type));
            }

            FishingFish countedFish = FishingFishCatalog.BetterOf(activeBest, caughtFish);
            if (activeBest != null && countedFish == activeBest && caughtFish.HappinessBonus < activeBest.HappinessBonus)
                return new FishingCatchBonusResult(caughtFish, activeBest, happinessEnabled: true);

            save.happinessModifiers.RemoveAll(data =>
                data != null && FishingFishCatalog.FindByModifierType(data.type) != null);
            HappinessHelper.AddModifier(countedFish.HappinessModifierType, CatchBonusHours, additiveHours: false);
            SaveGameManager.MarkChange();
            return new FishingCatchBonusResult(caughtFish, countedFish, happinessEnabled: true);
        }

        private static bool IsHappinessDisabled(GameInstance save)
        {
            return save.gameVariables != null && save.gameVariables.disableHappiness;
        }

        private static void EnsureDefinitionsRegistered()
        {
            if (ModifiersField == null)
                throw new MissingFieldException(typeof(HappinessHelper).FullName, "Modifiers");
            if (!(ModifiersField.GetValue(null) is Dictionary<string, HappinessModifier> modifiers))
                throw new InvalidOperationException("The native happiness modifier registry is not initialized.");

            RegisterDefinition(
                modifiers,
                FishingActivityType,
                FishingActivityAmount,
                FishingActivityHours);
            IReadOnlyList<FishingFish> fish = FishingFishCatalog.All;
            for (int i = 0; i < fish.Count; i++)
                RegisterDefinition(
                    modifiers,
                    fish[i].HappinessModifierType,
                    fish[i].HappinessBonus,
                    CatchBonusHours);
        }

        private static void RegisterDefinition(
            IDictionary<string, HappinessModifier> modifiers,
            string type,
            int amount,
            int hours)
        {
            if (!modifiers.TryGetValue(type, out HappinessModifier modifier) || modifier == null)
            {
                modifier = ScriptableObject.CreateInstance<HappinessModifier>();
                modifier.name = type;
                modifiers[type] = modifier;
            }

            modifier.type = type;
            modifier.amount = amount;
            modifier.hoursDuration = hours;
            modifier.maxHoursDuration = hours;
            modifier.oneTimeOnly = false;
            modifier.hideDuration = false;
            modifier.nonTemporalType = string.Empty;
            modifier.hideFlags = HideFlags.HideAndDontSave;
        }
    }
}
