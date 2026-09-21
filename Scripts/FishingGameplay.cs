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
        Escaped,
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
        internal int SalePrice => FishingEconomyRules.SalePrice(Rarity);
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

    internal static class FishingEconomyRules
    {
        internal const float MaximumLineBreakCost = 5f;

        internal static int SalePrice(FishingRarity rarity)
        {
            switch (rarity)
            {
                case FishingRarity.Common: return 5;
                case FishingRarity.Uncommon: return 8;
                case FishingRarity.Rare: return 15;
                case FishingRarity.VeryRare: return 25;
                case FishingRarity.Epic: return 40;
                case FishingRarity.Legendary: return 75;
                default: throw new ArgumentOutOfRangeException(nameof(rarity));
            }
        }

        internal static float LineBreakCost(float balance)
        {
            if (float.IsNaN(balance) || float.IsInfinity(balance) || balance <= 0f) return 0f;
            float cost = Math.Min(balance, MaximumLineBreakCost);
            // At extremely large float balances, a $5 debit can round up to $8 or more.
            return (double)balance - (balance - cost) > MaximumLineBreakCost ? 0f : cost;
        }
    }

    internal static class FishingBiteRules
    {
        internal const double FishChance = 0.80d;
        internal const float MinimumBiteDelaySeconds = 2f;
        internal const float MaximumBiteDelaySeconds = 20f;
        internal const float NoFishWaitSeconds = 20f;

        internal static bool HasFish(double roll)
        {
            if (double.IsNaN(roll)) throw new ArgumentOutOfRangeException(nameof(roll));
            return roll < FishChance;
        }

        internal static float BiteDelaySeconds(double roll)
        {
            if (double.IsNaN(roll)) throw new ArgumentOutOfRangeException(nameof(roll));
            if (roll <= 0d) return MinimumBiteDelaySeconds;
            if (roll >= 1d) return MaximumBiteDelaySeconds;
            return MinimumBiteDelaySeconds
                + (float)roll * (MaximumBiteDelaySeconds - MinimumBiteDelaySeconds);
        }
    }

    internal sealed class FishingBiteTimer
    {
        internal FishingBiteTimer(float durationSeconds)
        {
            if (float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) || durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            DurationSeconds = durationSeconds;
        }

        internal float DurationSeconds { get; }
        internal float ElapsedSeconds { get; private set; }
        internal float RemainingSeconds => Math.Max(0f, DurationSeconds - ElapsedSeconds);
        internal bool IsDue => ElapsedSeconds >= DurationSeconds;

        internal void Advance(float seconds, bool paused = false)
        {
            if (!paused && seconds > 0f && !float.IsInfinity(seconds)) ElapsedSeconds += seconds;
        }

        internal void AdvanceCast(float previousElapsed, float currentElapsed, float contactTime = FishingMath.ReleaseTime + FishingMath.FlightDuration)
        {
            Advance(Math.Max(0f, currentElapsed - contactTime) - Math.Max(0f, previousElapsed - contactTime));
        }
    }

    internal static class FishingInputRules
    {
        internal static bool CanReleaseQteInput(bool keysHeld, int currentFrame, int lastInputFrame)
        {
            return !keysHeld && currentFrame > lastInputFrame + 1;
        }

        internal static bool PausesClock(bool uiBlocked, bool gamePaused, bool hooked)
        {
            // Keep the existing unscaled QTE clock, including an already-paused world.
            // A paused world freezes the cast/bite wait, but must not swallow a QTE input.
            return uiBlocked || (gamePaused && !hooked);
        }

        internal static bool CancelsWaiting(bool focused, bool textInput, bool menuOpen,
            bool clicked, bool movement, bool escape, bool displaced)
        {
            if (displaced) return true;
            if (!focused) return false;
            if (escape) return true;
            if (textInput) return false;
            return clicked || (!menuOpen && movement);
        }
    }

    internal sealed class FishingQteSession
    {
        internal const float SuccessMeters = 3.5f;
        internal const float FailureMeters = SuccessMeters * 0.5f;
        internal const float InitialProgress = 0.30f;

        private readonly Random _random;
        private bool _settlementClaimed;

        internal FishingQteSession(FishingFish fish, Random random, float difficulty = 1f)
        {
            Fish = fish ?? throw new ArgumentNullException(nameof(fish));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            Difficulty = float.IsNaN(difficulty) || float.IsInfinity(difficulty) ? 1f : Math.Max(.2f, Math.Min(5f, difficulty));
            RemainingLineMeters = fish.InitialLineMeters * (1f - InitialProgress);
            ChooseNextCommand();
        }

        internal FishingFish Fish { get; }
        internal float Difficulty { get; }
        internal void CompleteAutomatically()
        {
            if (!IsFinished) RemainingLineMeters = 0f;
        }
        internal FishingQteCommand ExpectedCommand { get; private set; }
        internal float TimeRemaining { get; private set; }
        internal float RemainingLineMeters { get; private set; }
        internal int SuccessfulSteps { get; private set; }
        internal int FailedSteps { get; private set; }
        internal bool IsComplete => RemainingLineMeters <= 0.001f;
        internal bool IsEscaped { get; private set; }
        internal bool IsFinished => IsComplete || IsEscaped;
        internal float Progress => IsComplete
            ? 1f
            : Math.Max(0f, Math.Min(1f, 1f - RemainingLineMeters / Fish.InitialLineMeters));

        internal bool TryClaimSettlement(out FishingQteOutcome outcome)
        {
            outcome = FishingQteOutcome.None;
            if (!IsFinished || _settlementClaimed) return false;
            // Claim before calling native code: a callback/exception must never pay or charge twice.
            _settlementClaimed = true;
            outcome = IsComplete ? FishingQteOutcome.Completed : FishingQteOutcome.Escaped;
            return true;
        }

        internal FishingQteOutcome Advance(float deltaTime)
        {
            if (IsFinished || deltaTime <= 0f) return FishingQteOutcome.None;
            TimeRemaining -= deltaTime;
            return TimeRemaining > 0f ? FishingQteOutcome.None : RegisterFailure();
        }

        internal FishingQteOutcome Submit(FishingQteCommand command)
        {
            if (IsFinished) return FishingQteOutcome.None;
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
            if (RemainingLineMeters >= Fish.InitialLineMeters - 0.001f)
            {
                RemainingLineMeters = Fish.InitialLineMeters;
                IsEscaped = true;
                return FishingQteOutcome.Escaped;
            }
            ChooseNextCommand();
            return FishingQteOutcome.Failure;
        }

        private void ChooseNextCommand()
        {
            ExpectedCommand = (FishingQteCommand)_random.Next(0, 5);
            TimeRemaining = Fish.ResponseWindowSeconds / Difficulty;
        }
    }
}
