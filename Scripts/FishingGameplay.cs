using System;
using System.Collections.Generic;

namespace FishingMod
{
    internal enum FishingRarity
    {
        Common,
        Uncommon,
        Rare,
        VeryRare,
        Epic,
        Legendary
    }

    internal enum FishingQteCommand
    {
        Up,
        Left,
        Down,
        Right,
        Reel
    }

    internal enum FishingQteOutcome
    {
        None,
        Success,
        Failure,
        Completed
    }

    internal sealed class FishingFish
    {
        internal FishingFish(
            string id,
            string nameKey,
            string fallbackName,
            FishingRarity rarity,
            int chanceWeight,
            int happinessBonus,
            int requiredSuccesses,
            float responseWindowSeconds)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Fish ID is required.", nameof(id));
            if (chanceWeight <= 0) throw new ArgumentOutOfRangeException(nameof(chanceWeight));
            if (happinessBonus <= 0) throw new ArgumentOutOfRangeException(nameof(happinessBonus));
            if (requiredSuccesses <= 0) throw new ArgumentOutOfRangeException(nameof(requiredSuccesses));
            if (responseWindowSeconds < 0.75f) throw new ArgumentOutOfRangeException(nameof(responseWindowSeconds));

            Id = id;
            NameKey = nameKey;
            FallbackName = fallbackName;
            Rarity = rarity;
            ChanceWeight = chanceWeight;
            HappinessBonus = happinessBonus;
            RequiredSuccesses = requiredSuccesses;
            ResponseWindowSeconds = responseWindowSeconds;
        }

        internal string Id { get; }
        internal string NameKey { get; }
        internal string FallbackName { get; }
        internal FishingRarity Rarity { get; }
        internal int ChanceWeight { get; }
        internal int HappinessBonus { get; }
        internal int RequiredSuccesses { get; }
        internal float ResponseWindowSeconds { get; }
        internal float InitialLineMeters => RequiredSuccesses * FishingQteSession.SuccessMeters;
        internal string HappinessModifierType => "fishingmod_happiness_catch_" + Id;
    }

    internal static class FishingFishCatalog
    {
        private static readonly FishingFish[] Fish =
        {
            new FishingFish("roach", "fishingmod_fish_roach", "Roach", FishingRarity.Common, 30, 2, 4, 1.35f),
            new FishingFish("perch", "fishingmod_fish_perch", "Perch", FishingRarity.Uncommon, 24, 3, 5, 1.25f),
            new FishingFish("trout", "fishingmod_fish_trout", "Trout", FishingRarity.Rare, 18, 5, 6, 1.15f),
            new FishingFish("carp", "fishingmod_fish_carp", "Carp", FishingRarity.VeryRare, 13, 7, 8, 1.05f),
            new FishingFish("pike", "fishingmod_fish_pike", "Pike", FishingRarity.Epic, 9, 10, 10, 0.95f),
            new FishingFish("sturgeon", "fishingmod_fish_sturgeon", "Sturgeon", FishingRarity.Legendary, 6, 14, 12, 0.90f)
        };

        private static readonly int TotalWeight = CalculateTotalWeight();

        internal static IReadOnlyList<FishingFish> All => Fish;

        internal static FishingFish Select(double roll)
        {
            if (double.IsNaN(roll)) throw new ArgumentOutOfRangeException(nameof(roll));
            if (roll <= 0d) return Fish[0];
            if (roll >= 1d) return Fish[Fish.Length - 1];

            double weightedRoll = roll * TotalWeight;
            int cumulative = 0;
            for (int i = 0; i < Fish.Length; i++)
            {
                cumulative += Fish[i].ChanceWeight;
                if (weightedRoll < cumulative) return Fish[i];
            }

            return Fish[Fish.Length - 1];
        }

        internal static FishingFish FindByModifierType(string modifierType)
        {
            if (string.IsNullOrEmpty(modifierType)) return null;
            for (int i = 0; i < Fish.Length; i++)
                if (string.Equals(Fish[i].HappinessModifierType, modifierType, StringComparison.Ordinal))
                    return Fish[i];
            return null;
        }

        internal static FishingFish BetterOf(FishingFish first, FishingFish second)
        {
            if (first == null) return second;
            if (second == null) return first;
            return first.HappinessBonus >= second.HappinessBonus ? first : second;
        }

        private static int CalculateTotalWeight()
        {
            int total = 0;
            for (int i = 0; i < Fish.Length; i++) total += Fish[i].ChanceWeight;
            return total;
        }
    }

    internal sealed class FishingQteSession
    {
        internal const float SuccessMeters = 3.5f;
        internal const float FailureMeters = SuccessMeters * 0.5f;

        private readonly Random _random;

        internal FishingQteSession(FishingFish fish, Random random)
        {
            Fish = fish ?? throw new ArgumentNullException(nameof(fish));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            RemainingLineMeters = fish.InitialLineMeters;
            ChooseNextCommand();
        }

        internal FishingFish Fish { get; }
        internal FishingQteCommand ExpectedCommand { get; private set; }
        internal float TimeRemaining { get; private set; }
        internal float RemainingLineMeters { get; private set; }
        internal int SuccessfulSteps { get; private set; }
        internal int FailedSteps { get; private set; }
        internal bool IsComplete => RemainingLineMeters <= 0.001f;
        internal float Progress => IsComplete ? 1f : 1f - RemainingLineMeters / Fish.InitialLineMeters;

        internal FishingQteOutcome Advance(float deltaTime)
        {
            if (IsComplete || deltaTime <= 0f) return FishingQteOutcome.None;
            TimeRemaining -= deltaTime;
            return TimeRemaining > 0f ? FishingQteOutcome.None : RegisterFailure();
        }

        internal FishingQteOutcome Submit(FishingQteCommand command)
        {
            if (IsComplete) return FishingQteOutcome.None;
            if (command != ExpectedCommand) return RegisterFailure();

            SuccessfulSteps++;
            RemainingLineMeters = Math.Max(0f, RemainingLineMeters - SuccessMeters);
            if (IsComplete) return FishingQteOutcome.Completed;
            ChooseNextCommand();
            return FishingQteOutcome.Success;
        }

        private FishingQteOutcome RegisterFailure()
        {
            FailedSteps++;
            RemainingLineMeters = Math.Min(Fish.InitialLineMeters, RemainingLineMeters + FailureMeters);
            ChooseNextCommand();
            return FishingQteOutcome.Failure;
        }

        private void ChooseNextCommand()
        {
            ExpectedCommand = (FishingQteCommand)_random.Next(0, 5);
            TimeRemaining = Fish.ResponseWindowSeconds;
        }
    }
}
