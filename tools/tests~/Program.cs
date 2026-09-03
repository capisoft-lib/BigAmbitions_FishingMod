using System;

namespace FishingMod
{
    internal static class Program
    {
        private static int _passed;

        private static int Main()
        {
            try
            {
                Check(FishingMath.Clamp01(-2f) == 0f, "clamp below zero");
                Check(FishingMath.Clamp01(2f) == 1f, "clamp above one");
                Check(FishingMath.Clamp01(0.4f) == 0.4f, "clamp keeps interior value");
                Check(FishingMath.Smooth01(0f) == 0f, "smooth starts at zero");
                Check(FishingMath.Smooth01(1f) == 1f, "smooth ends at one");
                Check(FishingMath.Smooth01(0.25f) < FishingMath.Smooth01(0.75f), "smooth is ordered");
                Check(FishingMath.Segment(0f, 1f, 2f) == 0f, "segment clamps before start");
                Check(FishingMath.Segment(3f, 1f, 2f) == 1f, "segment clamps after end");
                Check(Approximately(FishingMath.Segment(1.5f, 1f, 2f), 0.5f), "segment midpoint");
                Check(FishingMath.BallisticHeight(0f, 5f) == 0f, "arc starts on endpoint");
                Check(FishingMath.BallisticHeight(1f, 5f) == 0f, "arc ends on endpoint");
                Check(Approximately(FishingMath.BallisticHeight(0.5f, 5f), 5f), "arc reaches apex");
                Check(FishingMath.ShoreScore(1f, 900f, 0f) < FishingMath.ShoreScore(2f, 0f, 0f), "shore proximity outranks path length");
                Check(FishingMath.ShoreScore(2f, 10f, 0f) < FishingMath.ShoreScore(2f, 20f, 0f), "shorter equal-distance route wins");
                Check(FishingMath.LooksLikeWater("HDRP Water Surface"), "water surface token");
                Check(FishingMath.LooksLikeWater("East_River_Renderer"), "river token");
                Check(FishingMath.LooksLikeWater("Ocean-Mesh"), "ocean token");
                Check(FishingMath.LooksLikeWater("Canal.001"), "canal token");
                Check(!FishingMath.LooksLikeWater("WaterPedestrianPool"), "water pedestrian excluded");
                Check(!FishingMath.LooksLikeWater("CoolantWaterBottle"), "water bottle excluded");
                Check(!FishingMath.LooksLikeWater("SeasonalDecorations"), "sea substring boundary");
                Check(FishingMath.ReleaseTime > 0.5f && FishingMath.ReleaseTime < FishingMath.FlightDuration + 0.5f, "release timing range");
                Check(FishingMath.SequenceDuration > FishingMath.ReleaseTime + FishingMath.FlightDuration, "settled phase follows flight");
                CheckFishCatalog();
                CheckQteRecovery();
                Console.WriteLine("PASS " + _passed + "/" + _passed);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL after " + _passed + " checks: " + exception.Message);
                return 1;
            }
        }

        private static void CheckFishCatalog()
        {
            var fish = FishingFishCatalog.All;
            Check(fish.Count == 6, "six fish configured");
            int totalWeight = 0;
            for (int i = 0; i < fish.Count; i++)
            {
                totalWeight += fish[i].ChanceWeight;
                Check(FishingFishCatalog.FindByModifierType(fish[i].HappinessModifierType) == fish[i],
                    "fish modifier resolves " + fish[i].Id);
                if (i == 0) continue;
                Check(fish[i].ChanceWeight < fish[i - 1].ChanceWeight,
                    "better fish probability decreases " + fish[i].Id);
                Check(fish[i].HappinessBonus > fish[i - 1].HappinessBonus,
                    "better fish happiness increases " + fish[i].Id);
                Check(fish[i].RequiredSuccesses > fish[i - 1].RequiredSuccesses,
                    "rarer fish needs more pulls " + fish[i].Id);
                Check(fish[i].ResponseWindowSeconds < fish[i - 1].ResponseWindowSeconds
                    && fish[i].ResponseWindowSeconds >= 0.90f,
                    "rarer fish window stays achievable " + fish[i].Id);
            }

            Check(totalWeight == 100, "fish probability weights total 100");
            Check(FishingFishCatalog.Select(0d) == fish[0], "zero roll selects common fish");
            Check(FishingFishCatalog.Select(0.299999d) == fish[0], "first probability upper edge");
            Check(FishingFishCatalog.Select(0.30d) == fish[1], "second probability lower edge");
            Check(FishingFishCatalog.Select(0.54d) == fish[2], "third probability lower edge");
            Check(FishingFishCatalog.Select(0.94d) == fish[5], "legendary probability lower edge");
            Check(FishingFishCatalog.Select(1d) == fish[5], "one roll clamps to legendary fish");
            Check(FishingFishCatalog.BetterOf(fish[1], fish[4]) == fish[4], "best fish wins comparison");
        }

        private static void CheckQteRecovery()
        {
            FishingFish fish = FishingFishCatalog.All[0];
            FishingQteSession qte = new FishingQteSession(fish, new Random(12345));
            Check(Approximately(qte.RemainingLineMeters, fish.RequiredSuccesses * FishingQteSession.SuccessMeters),
                "QTE starts at configured line length");

            FishingQteCommand expected = qte.ExpectedCommand;
            Check(qte.Submit(expected) == FishingQteOutcome.Success, "correct QTE reels line");
            float afterSuccess = fish.InitialLineMeters - FishingQteSession.SuccessMeters;
            Check(Approximately(qte.RemainingLineMeters, afterSuccess), "success reels 3.5 metres");

            FishingQteCommand wrong = (FishingQteCommand)(((int)qte.ExpectedCommand + 1) % 5);
            Check(qte.Submit(wrong) == FishingQteOutcome.Failure, "wrong QTE releases line");
            Check(Approximately(qte.RemainingLineMeters, afterSuccess + FishingQteSession.FailureMeters),
                "failure costs half a success");

            for (int i = 0; i < 20; i++)
            {
                wrong = (FishingQteCommand)(((int)qte.ExpectedCommand + 1) % 5);
                qte.Submit(wrong);
            }
            Check(Approximately(qte.RemainingLineMeters, fish.InitialLineMeters), "failures cap at initial line length");
            Check(qte.Advance(fish.ResponseWindowSeconds + 0.01f) == FishingQteOutcome.Failure,
                "QTE timeout counts as one failure");

            int safety = 0;
            FishingQteOutcome outcome = FishingQteOutcome.None;
            while (!qte.IsComplete && safety++ < 100)
                outcome = qte.Submit(qte.ExpectedCommand);
            Check(qte.IsComplete && outcome == FishingQteOutcome.Completed, "QTE remains completable after mistakes");
            Check(Approximately(qte.Progress, 1f), "completed QTE reports full progress");
        }

        private static bool Approximately(float left, float right)
        {
            return Math.Abs(left - right) < 0.0001f;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            _passed++;
        }
    }
}
