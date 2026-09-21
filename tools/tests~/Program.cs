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
                CheckBiteTimerAndCancellation();
                CheckQteProgress();
                CheckEconomy();
                CheckWaveDecoder();
                CheckStaticAtlas();
                Console.WriteLine("PASS " + _passed + "/" + _passed);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL after " + _passed + " checks: " + exception.Message);
                return 1;
            }
        }

        private static void CheckStaticAtlas()
        {
            var atlas = StaticWater.GameWaterAtlas.Create();
            StaticWater.StaticWaterAtlas.Zone zone;
            Check(atlas.TryGetCandidate(0,-500,out zone) && zone.Id.StartsWith("city-port/"), "static city port");
            Check(atlas.TryGetCandidate(-1000,-700,out zone) && zone.Id.StartsWith("bridge/"), "static bridge");
            Check(atlas.TryGetCandidate(-1800,-700,out zone) && zone.Id.StartsWith("industry/"), "static industry");
            Check(atlas.TryGetCandidate(-3500,-1700,out zone) && zone.Id.StartsWith("hamptons/"), "static Hamptons");
            Check(Math.Abs(zone.Height+2.8)<.0001, "static sea height");
            Check(!atlas.TryGetCandidate(-3000,-1200,out zone), "static Hamptons land excluded");
            Check(!atlas.TryGetCandidate(-1600,-1400,out zone), "static Industry land excluded");
            Check(!atlas.TryGetCandidate(0,-100,out zone), "static city land excluded");
            Check(!atlas.TryGetCandidate(double.NaN,0,out zone), "static NaN excluded");
            Check(!atlas.TryGetCandidate(double.MaxValue,0,out zone), "static overflow excluded");
            Check(!atlas.TryGetCandidate(-2640.8,-905.0,out zone), "last logged cast beneath Yacht Club dock excluded");
            Check(!atlas.TryGetCandidate(-2646.7,-903.4,out zone), "previous logged cast beneath Yacht Club dock excluded");
            Check(atlas.TryGetCandidate(-2651.8,-894.1,out zone), "logged open water beside dock preserved");
            Check(atlas.DistanceToWater(-2649.4,-902.7) <= 10, "player on dock remains close enough to fish beside it");
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

        private static void CheckBiteTimerAndCancellation()
        {
            Check(!FishingInputRules.CanReleaseQteInput(true, 200, 10), "held terminal QTE key never returns character control");
            Check(!FishingInputRules.CanReleaseQteInput(false, 10, 10), "terminal QTE frame remains blocked");
            Check(!FishingInputRules.CanReleaseQteInput(false, 11, 10), "key-up frame remains blocked");
            Check(FishingInputRules.CanReleaseQteInput(false, 12, 10), "neutral frame returns character control");
            Check(!FishingInputRules.CanReleaseQteInput(false, 21, 20), "new held QTE key restarts release barrier");
            foreach (string name in new[] { "WaterSurface", "OceanMesh", "Lake01", "ParkPond", "RiverWater", "Lagoon" })
                Check(FishingMath.LooksLikeWater(name), "recognize genuine water surface name " + name);
            foreach (string name in new[] { "Harbor", "WaterPedestrianPool", "WaterBottle", "WaterTower", "LakeHouse", "Waterfront", "WaterPipe" })
                Check(!FishingMath.LooksLikeWater(name), "exclude district or water prop name " + name);

            foreach (float target in new[] { 2f, 11f, 20f })
            {
                var timer = new FishingBiteTimer(target);
                float impact = FishingMath.ReleaseTime + FishingMath.FlightDuration;
                timer.AdvanceCast(0f, impact);
                Check(timer.ElapsedSeconds == 0f, "bite clock excludes wind-up and flight");
                timer.AdvanceCast(impact, FishingMath.SequenceDuration);
                Check(Approximately(timer.ElapsedSeconds, FishingMath.SequenceDuration - impact), "follow-through counts toward water wait");
                float beforePause = timer.ElapsedSeconds;
                timer.Advance(40f, paused: true);
                Check(timer.ElapsedSeconds == beforePause && !timer.IsDue, "pause cannot consume the bite timer");
                timer.Advance(timer.RemainingSeconds - 0.05f);
                Check(!timer.IsDue, "no early bite before planned " + target + " seconds");
                timer.Advance(0.051f);
                Check(timer.IsDue && Math.Abs(timer.ElapsedSeconds - target) < 0.01f, "bite due after impact-relative " + target + " seconds");
            }
            var random = new Random(123);
            float min = 20f, max = 2f, sum = 0f;
            int fish = 0;
            for (int i = 0; i < 5000; i++)
            {
                if (FishingBiteRules.HasFish(random.NextDouble())) fish++;
                float delay = FishingBiteRules.BiteDelaySeconds(random.NextDouble());
                min = Math.Min(min, delay); max = Math.Max(max, delay); sum += delay;
            }
            Check(min < 2.05f && max > 19.95f && sum / 5000f > 10.5f && sum / 5000f < 11.5f,
                "random bite distribution spans 2-20 seconds with an 11-second mean");
            Check(fish > 3900 && fish < 4100, "random cast distribution retains 80 percent fish chance");
            Check(FishingInputRules.PausesClock(false, true, false), "game pause freezes the bite wait");
            Check(!FishingInputRules.PausesClock(false, false, false), "active bite wait advances");
            Check(!FishingInputRules.PausesClock(false, true, true), "Space cannot freeze a hooked fish QTE via vanilla pause");
            Check(FishingInputRules.PausesClock(true, false, true), "blocked UI still freezes the QTE");
            Check(FishingInputRules.CancelsWaiting(true, false, false, true, false, false, false), "click cancels waiting");
            Check(FishingInputRules.CancelsWaiting(true, false, false, false, true, false, false), "movement intent cancels before blocked navigation");
            Check(FishingInputRules.CancelsWaiting(true, false, true, false, false, true, false), "Escape cancels even when opening the menu");
            Check(FishingInputRules.CancelsWaiting(true, false, false, false, false, false, true), "physical displacement cancels waiting");
            Check(!FishingInputRules.CancelsWaiting(false, false, false, true, true, false, false), "background input does not cancel");
            Check(!FishingInputRules.CancelsWaiting(true, true, false, false, true, false, false), "typing does not cancel");
            Check(!FishingInputRules.CancelsWaiting(true, false, true, false, true, false, false), "menu navigation does not cancel");
            Check(!FishingInputRules.CancelsWaiting(true, false, false, false, false, false, false), "idle waiting continues");
        }

        private static void CheckQteProgress()
        {
            FishingFish fish = FishingFishCatalog.All[0];
            var easy = new FishingQteSession(fish, new Random(1), .2f);
            var hard = new FishingQteSession(fish, new Random(1), 5f);
            Check(Approximately(easy.TimeRemaining, fish.ResponseWindowSeconds * 5f), "difficulty 0.2 gives five times response window");
            Check(Approximately(hard.TimeRemaining, fish.ResponseWindowSeconds / 5f), "difficulty 5 gives one fifth response window");
            Check(new FishingQteSession(fish,new Random(1),0).Difficulty == .2f, "difficulty minimum clamped");
            Check(new FishingQteSession(fish,new Random(1),99).Difficulty == 5f, "difficulty maximum clamped");
            Check(new FishingQteSession(fish,new Random(1),float.NaN).Difficulty == 1f, "invalid difficulty uses default");
            easy.CompleteAutomatically();
            Check(easy.IsComplete && !easy.IsEscaped && easy.Progress == 1f, "disabled QTE completes catch");
            Check(easy.TryClaimSettlement(out var autoOutcome) && autoOutcome == FishingQteOutcome.Completed,
                "automatic catch uses normal settlement");
            Check(!easy.TryClaimSettlement(out _), "automatic catch pays once");
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

        private static void CheckEconomy()
        {
            int[] prices = { 5, 8, 15, 25, 40, 75 };
            for (int i = 0; i < prices.Length; i++)
            {
                FishingFish fish = FishingFishCatalog.All[i];
                Check(fish.SalePrice == prices[i], "sale price for " + fish.Id);
                if (i > 0) Check(fish.SalePrice > FishingFishCatalog.All[i - 1].SalePrice,
                    "rarity increases sale price " + fish.Id);
                var won = new FishingQteSession(fish, new Random(i));
                Check(!won.TryClaimSettlement(out _), "unfinished QTE cannot settle " + fish.Id);
                for (int step = 0; step < 100 && !won.IsComplete; step++) won.Submit(won.ExpectedCommand);
                Check(won.TryClaimSettlement(out var wonOutcome) && wonOutcome == FishingQteOutcome.Completed,
                    "completed QTE settles its catch " + fish.Id);
                Check(!won.TryClaimSettlement(out _), "catch cannot settle twice " + fish.Id);

                var lost = new FishingQteSession(fish, new Random(i));
                lost.Advance(fish.ResponseWindowSeconds + 1f);
                Check(!lost.TryClaimSettlement(out _), "intermediate timeout is free " + fish.Id);
                for (int step = 0; step < 100 && !lost.IsEscaped; step++)
                    lost.Advance(fish.ResponseWindowSeconds + 1f);
                Check(lost.TryClaimSettlement(out var lostOutcome) && lostOutcome == FishingQteOutcome.Escaped,
                    "zero progress settles a line break " + fish.Id);
                Check(!lost.TryClaimSettlement(out _), "line break cannot settle twice " + fish.Id);
            }
            Check(FishingEconomyRules.LineBreakCost(100f) == 5f, "line costs at most five dollars");
            Check(FishingEconomyRules.LineBreakCost(5f) == 5f, "exactly five dollars can pay the line");
            Check(FishingEconomyRules.LineBreakCost(2.25f) == 2.25f, "low balance limits line cost");
            Check(FishingEconomyRules.LineBreakCost(0f) == 0f, "no balance means no fee");
            Check(FishingEconomyRules.LineBreakCost(-10f) == 0f, "line cannot worsen existing debt");
            Check(FishingEconomyRules.LineBreakCost(float.NaN) == 0f, "invalid balance cannot be charged");
            Check(FishingEconomyRules.LineBreakCost(float.PositiveInfinity) == 0f, "infinite balance cannot be charged");
            Check(FishingEconomyRules.LineBreakCost(100000000f) == 0f, "float precision cannot overcharge the line");
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
