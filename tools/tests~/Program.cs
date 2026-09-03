using System;
using System.IO;
using System.Text;

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
                Check(FishingMath.VisibleProgressSegments(0f, 96) == 0, "line progress ring starts empty");
                Check(FishingMath.VisibleProgressSegments(0.5f, 96) == 48, "line progress ring reaches half a circle");
                Check(FishingMath.VisibleProgressSegments(0.001f, 96) == 1, "line progress ring shows its first segment");
                Check(FishingMath.VisibleProgressSegments(1f, 96) == 96, "line progress ring completes the circle");
                Check(FishingMath.VisibleProgressSegments(1f, 0) == 0, "line progress ring rejects an empty segment count");
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
                CheckBiteRules();
                CheckQteProgress();
                CheckWaveDecoder();
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

        private static void CheckBiteRules()
        {
            Check(Approximately((float)FishingBiteRules.FishChance, 0.80f), "cast fish chance is 80 percent");
            Check(FishingBiteRules.HasFish(0d), "zero bite roll has a fish");
            Check(FishingBiteRules.HasFish(0.799999d), "bite roll below 80 percent has a fish");
            Check(!FishingBiteRules.HasFish(0.80d), "bite roll at 80 percent has no fish");
            Check(!FishingBiteRules.HasFish(1d), "maximum bite roll has no fish");
            Check(Approximately(FishingBiteRules.BiteDelaySeconds(0d), 2f), "bite delay starts at 2 seconds");
            Check(Approximately(FishingBiteRules.BiteDelaySeconds(0.5d), 11f), "bite delay midpoint is 11 seconds");
            Check(Approximately(FishingBiteRules.BiteDelaySeconds(1d), 20f), "bite delay ends at 20 seconds");
            Check(Approximately(FishingBiteRules.NoFishWaitSeconds, 20f), "empty cast waits exactly 20 seconds");
        }

        private static void CheckQteProgress()
        {
            FishingFish fish = FishingFishCatalog.All[0];
            FishingQteSession qte = new FishingQteSession(fish, new Random(12345));
            float initialRemaining = fish.InitialLineMeters * (1f - FishingQteSession.InitialProgress);
            Check(Approximately(qte.Progress, 0.30f), "QTE starts at 30 percent progress");
            Check(Approximately(qte.RemainingLineMeters, initialRemaining),
                "QTE starts with 70 percent of the line remaining");

            FishingQteCommand expected = qte.ExpectedCommand;
            Check(qte.Submit(expected) == FishingQteOutcome.Success, "correct QTE reels line");
            float afterSuccess = initialRemaining - FishingQteSession.SuccessMeters;
            Check(Approximately(qte.RemainingLineMeters, afterSuccess), "success reels 3.5 metres");

            FishingQteCommand wrong = (FishingQteCommand)(((int)qte.ExpectedCommand + 1) % 5);
            Check(qte.Submit(wrong) == FishingQteOutcome.Failure, "wrong QTE releases line");
            Check(Approximately(qte.RemainingLineMeters, afterSuccess + FishingQteSession.FailureMeters),
                "failure costs half a success");

            Check(qte.Advance(fish.ResponseWindowSeconds + 0.01f) == FishingQteOutcome.Failure,
                "QTE timeout counts as one failure before zero progress");

            FishingQteOutcome escapeOutcome = FishingQteOutcome.None;
            int escapeSafety = 0;
            while (!qte.IsEscaped && escapeSafety++ < 20)
            {
                wrong = (FishingQteCommand)(((int)qte.ExpectedCommand + 1) % 5);
                escapeOutcome = qte.Submit(wrong);
            }
            Check(qte.IsEscaped && escapeOutcome == FishingQteOutcome.Escaped,
                "fish escapes when progress falls to zero");
            Check(Approximately(qte.Progress, 0f), "escaped QTE reports zero progress");
            Check(qte.Submit(qte.ExpectedCommand) == FishingQteOutcome.None,
                "escaped QTE ignores further input");

            FishingQteSession catchable = new FishingQteSession(fish, new Random(54321));
            int safety = 0;
            FishingQteOutcome outcome = FishingQteOutcome.None;
            while (!catchable.IsComplete && safety++ < 100)
                outcome = catchable.Submit(catchable.ExpectedCommand);
            Check(catchable.IsComplete && outcome == FishingQteOutcome.Completed,
                "QTE remains completable from 30 percent");
            Check(Approximately(catchable.Progress, 1f), "completed QTE reports full progress");
        }

        private static void CheckWaveDecoder()
        {
            FishingWaveData wave = FishingWaveDecoder.Decode(CreateTestWave());
            Check(wave.Channels == 1, "WAV decoder retains channel count");
            Check(wave.SampleRate == 44100, "WAV decoder retains sample rate");
            Check(wave.FrameCount == 3, "WAV decoder retains frame count");
            Check(Approximately(wave.Samples[0], -1f)
                && Approximately(wave.Samples[1], 0f)
                && Approximately(wave.Samples[2], 0.5f),
                "WAV decoder converts signed PCM samples");

            byte[] invalid = CreateTestWave();
            invalid[0] = (byte)'X';
            bool rejected = false;
            try { FishingWaveDecoder.Decode(invalid); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "WAV decoder rejects an invalid RIFF header");
        }

        private static byte[] CreateTestWave()
        {
            short[] samples = { short.MinValue, 0, 16384 };
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples.Length * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((ushort)1);
                writer.Write((ushort)1);
                writer.Write(44100);
                writer.Write(44100 * 2);
                writer.Write((ushort)2);
                writer.Write((ushort)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(samples.Length * 2);
                for (int i = 0; i < samples.Length; i++) writer.Write(samples[i]);
                writer.Flush();
                return stream.ToArray();
            }
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
