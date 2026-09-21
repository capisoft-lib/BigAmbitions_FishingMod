#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BAModAPI;
using HarmonyLib;
using Helpers;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace FishingMod.Editor
{
    internal static class FishingModChecks
    {
        private static int _checks;

        internal static int Run()
        {
            _checks = 0;
            CheckOptions();
            CheckRodRenderingConfiguration();
            CheckReusableCastVisual();
            CheckAudioAssets();
            CheckGameplayConfiguration();
            CheckHappinessBehavior();
            CheckEconomyBehavior();
            CheckWaterDetection();
            CheckExtendedWaterDetection();
            CheckStaticWaterDetection();
            CheckHamptonsMarinaMousePlane();
            CheckHamptonsEastCoast();
            CheckLandInFrontOfWater();
            _checks += FishingNativeWaterChecks.Run();
            _checks += FishingDirectCastChecks.Run();
            CheckShoreResolution();
            CheckElevatedShoreResolution();
            Debug.Log("[FishingMod.Checks] PASS " + _checks + "/" + _checks + ".");
            return _checks;
        }

        private static void CheckReusableCastVisual()
        {
            var owner = new GameObject("Fishing visual lifecycle test");
            owner.SetActive(false); // Native Awake expects a complete live character.
            var character = owner.AddComponent<ThirdPersonCharacter>();
            character.appearanceSetter = owner.AddComponent<AppearanceSetter>();
            FishingCastVisual visual = null;
            try
            {
                visual = new FishingCastVisual(character, Vector3.zero, startImmediately: false);
                var root = (Transform)typeof(FishingCastVisual).GetField("_root", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(visual);
                int instance = root.GetInstanceID();
                int children = root.GetComponentsInChildren<Transform>(true).Length;
                Check(!root.gameObject.activeSelf && !visual.IsAlive, "preparation remains invisible and inactive");
                for (int i = 0; i < 8; i++)
                {
                    Vector3 target = new Vector3(10+i*3,-2.8f,12-i);
                    visual.BeginCast(target);
                    Check(visual.IsAlive && visual.Elapsed == 0 && !visual.ConsumeSplashSoundEvent(), "reused cast resets time and events");
                    Check(Vector3.Distance(visual.LandingPoint, FishingCastGeometry.LandingPoint(target)) < .001f, "reused cast takes the new target");
                    visual.Advance(visual.Duration);
                    visual.RenderLate();
                    Check(visual.ConsumeSplashSoundEvent(), "reused cast emits a fresh splash");
                    Check(root.GetInstanceID() == instance && root.GetComponentsInChildren<Transform>(true).Length == children,
                        "consecutive casts reuse the same visual objects");
                    visual.EndCast();
                    Check(!root.gameObject.activeSelf && !visual.IsAlive, "end hides visuals and stops animation");
                }
                visual.Dispose();
                Check(!visual.CanReuse(character), "disposed visuals cannot be reused");
            }
            finally { visual?.Dispose(); UnityEngine.Object.DestroyImmediate(owner); }
        }

        private static void CheckOptions()
        {
            string id = "FishingMod.Tests." + Guid.NewGuid().ToString("N");
            string toggleKey = "m:" + id + ":enable_qte";
            string difficultyKey = "m:" + id + ":difficulty_tenths";
            try
            {
                FishingOptions.Register(id);
                if (!FishingOptions.EnableQte || FishingOptions.Difficulty != 1f) throw new Exception("Option defaults");
                _checks++;
                var options = BigAmbitions.Mods.OptionsService.RegisteredEntries[id].Options;
                var slider = options.OfType<BigAmbitions.Mods.SliderOption>().Single();
                if (slider.Min != 2 || slider.Max != 50 || slider.DefaultValue != 10) throw new Exception("Slider range");
                slider.OnValueChanged(2);
                if (Mathf.Abs(FishingOptions.Difficulty-.2f)>.0001f) throw new Exception("Difficulty callback");
                _checks++;
                FishingOptions.Unregister();
                UnityEngine.PlayerPrefs.SetInt(toggleKey,0);
                UnityEngine.PlayerPrefs.SetInt(difficultyKey,35);
                FishingOptions.Register(id);
                if (FishingOptions.EnableQte || FishingOptions.Difficulty != 3.5f) throw new Exception("Options persistence");
                _checks++;
                AccessTools.Method(typeof(FishingOptions),"Reset").Invoke(null,null);
                if (!FishingOptions.EnableQte || FishingOptions.Difficulty != 1f) throw new Exception("Options reset");
                _checks++;
            }
            finally
            {
                FishingOptions.Unregister();
                UnityEngine.PlayerPrefs.DeleteKey(toggleKey);
                UnityEngine.PlayerPrefs.DeleteKey(difficultyKey);
            }
        }

        private static void CheckGameplayConfiguration()
        {
            Check(typeof(FishingModInitializationEntry).IsDefined(
                    typeof(ModEntryOnInitializationLoadAttribute), inherit: false),
                "happiness bootstrap uses initialization scope");
            Check(typeof(FishingModEntry).IsDefined(
                    typeof(ModEntryOnCityLoadAttribute), inherit: false),
                "fishing runtime remains city scoped");
            Type[] registeredTypes = typeof(FishingModEntry).Assembly
                .GetCustomAttributes<RegisterModClassAttribute>()
                .Select(attribute => attribute.ModClassType)
                .ToArray();
            Check(registeredTypes.Length == 2
                && registeredTypes.Contains(typeof(FishingModInitializationEntry))
                && registeredTypes.Contains(typeof(FishingModEntry)),
                "assembly registers initialization and city entries");
            Check(FishingHappinessService.HasNativeContract, "native happiness modifier contract available");
            Check(FishingHappinessService.FishingActivityAmount == 10
                && FishingHappinessService.FishingActivityHours == 48,
                "fishing activity grants +10 happiness for 48 hours");
            Check(FishingHappinessService.CatchBonusHours == 72,
                "caught fish happiness lasts 72 hours");
            Check(FishingFishCatalog.All.Count == 6, "six weighted fish available");
            Check(Math.Abs(FishingBiteRules.FishChance - 0.80d) < 0.0001d,
                "each cast has an 80 percent fish chance");
            Check(Mathf.Approximately(FishingBiteRules.BiteDelaySeconds(0d), 2f)
                && Mathf.Approximately(FishingBiteRules.BiteDelaySeconds(1d), 20f),
                "hooked fish waits between 2 and 20 seconds");
            Check(Mathf.Approximately(FishingBiteRules.NoFishWaitSeconds, 20f),
                "empty cast waits 20 seconds before retrieval");
            Check(FishingInputRules.PausesClock(false, true, false)
                && !FishingInputRules.PausesClock(false, true, true)
                && FishingInputRules.PausesClock(true, false, true),
                "world pause freezes waiting without blocking Space during the QTE; menus freeze both");
            Check(Mathf.Approximately(FishingQteSession.FailureMeters, FishingQteSession.SuccessMeters * 0.5f),
                "QTE mistake loses half a successful pull");
            FishingQteSession qte = new FishingQteSession(FishingFishCatalog.All[0], new System.Random(12345));
            Check(Mathf.Approximately(qte.Progress, FishingQteSession.InitialProgress)
                && Mathf.Approximately(qte.Progress, 0.30f),
                "QTE starts at 30 percent line progress");
            FishingQteOutcome escapeOutcome = FishingQteOutcome.None;
            int escapeSafety = 0;
            while (!qte.IsEscaped && escapeSafety++ < 20)
            {
                FishingQteCommand wrong = (FishingQteCommand)(((int)qte.ExpectedCommand + 1) % 5);
                escapeOutcome = qte.Submit(wrong);
            }
            Check(qte.IsEscaped && escapeOutcome == FishingQteOutcome.Escaped
                && Mathf.Approximately(qte.Progress, 0f),
                "fish escapes when QTE progress reaches zero");
            Check(FishingMath.VisibleProgressSegments(0f, 96) == 0,
                "QTE line ring starts empty");
            Check(FishingMath.VisibleProgressSegments(0.5f, 96) == 48,
                "QTE line ring shows half the retrieved line");
            Check(FishingMath.VisibleProgressSegments(1f, 96) == 96,
                "QTE line ring completes with the catch");
            Rect fullHdWheel = FishingQteOverlay.CenteredWheelRect(1920f, 1080f);
            Check(fullHdWheel.center == new Vector2(960f, 540f),
                "QTE control wheel is centered on screen");
            Check(Mathf.Approximately(fullHdWheel.width, fullHdWheel.height) && Mathf.Approximately(fullHdWheel.width, 340f),
                "QTE control wheel stays round and responsively capped");
            Check(Shader.Find("Hidden/Internal-Colored") != null,
                "QTE control wheel shader available");
        }

        private static void CheckAudioAssets()
        {
            string soundsRoot = Path.Combine(Application.dataPath, "Mods", "FishingMod", "Sounds~");
            Check(FishingAudio.RequiredSounds.Count == 8, "eight fishing sounds are mapped");
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < FishingAudio.RequiredSounds.Count; i++)
            {
                KeyValuePair<FishingSound, string> spec = FishingAudio.RequiredSounds[i];
                Check(names.Add(spec.Value), "fishing sound filename is unique: " + spec.Value);
                string path = Path.Combine(soundsRoot, spec.Value);
                Check(File.Exists(path), "fishing sound exists: " + spec.Value);
                FishingWaveData wave = FishingWaveDecoder.Load(path);
                Check(wave.Channels == 1 && wave.SampleRate == 44100,
                    "fishing sound is 44.1 kHz mono PCM: " + spec.Value);
                Check(wave.FrameCount > 0 && wave.DurationSeconds >= MinimumDuration(spec.Key)
                    && wave.DurationSeconds <= MaximumDuration(spec.Key),
                    "fishing sound duration is bounded: " + spec.Value);

                float peak = 0f;
                for (int sample = 0; sample < wave.Samples.Length; sample++)
                    peak = Mathf.Max(peak, Mathf.Abs(wave.Samples[sample]));
                Check(peak > 0.01f && peak <= 1f, "fishing sound has valid sample levels: " + spec.Value);
            }
        }

        private static float MinimumDuration(FishingSound sound)
        {
            switch (sound)
            {
                case FishingSound.Cast: return 2f;
                case FishingSound.ReelOut:
                case FishingSound.ReelIn: return 0.5f;
                case FishingSound.FishLanded: return 0.4f;
                default: return 0.05f;
            }
        }

        private static float MaximumDuration(FishingSound sound)
        {
            switch (sound)
            {
                case FishingSound.Cast: return 3.2f;
                case FishingSound.ReelOut:
                case FishingSound.ReelIn:
                case FishingSound.FishLanded: return 1f;
                case FishingSound.QteSuccess:
                case FishingSound.QteFailure:
                case FishingSound.LineSnap: return 0.25f;
                default: return 0.5f;
            }
        }

        private static void CheckHappinessBehavior()
        {
            FieldInfo modifiersField = typeof(HappinessHelper).GetField(
                "Modifiers",
                BindingFlags.Static | BindingFlags.NonPublic);
            object originalModifiers = modifiersField.GetValue(null);
            GameInstance originalSave = SaveGameManager.Current;
            Dictionary<string, HappinessModifier> testModifiers = null;
            Harmony bootstrapHarmony = new Harmony("capisoft.fishingmod.tests.happiness-bootstrap");
            modifiersField.SetValue(null, null);
            SaveGameManager.Current = new GameInstance { gameVariables = new GameVariables() };

            try
            {
                FishingHappinessBootstrapPatch.Install(bootstrapHarmony, _ => { });
                Check(FishingHappinessBootstrapPatch.TargetMethod.DeclaringType == typeof(HappinessHelper)
                    && FishingHappinessBootstrapPatch.TargetMethod.Name == nameof(HappinessHelper.OnHappinessModifiersLoaded),
                    "happiness bootstrap targets the native registry callback");
                Check(Harmony.GetPatchInfo(FishingHappinessBootstrapPatch.TargetMethod).Postfixes.Any(
                        patch => patch.owner == bootstrapHarmony.Id),
                    "happiness bootstrap postfix is installed before save loading");

                FishingFish savedPerch = FishingFishCatalog.All[1];
                SaveGameManager.Current = new GameInstance
                {
                    gameVariables = new GameVariables(),
                    happinessModifiers = new List<HappinessModifierData>
                    {
                        new HappinessModifierData
                        {
                            type = savedPerch.HappinessModifierType,
                            hoursLeft = FishingHappinessService.CatchBonusHours
                        }
                    }
                };
                HappinessHelper.OnHappinessModifiersLoaded(new List<HappinessModifier>());
                testModifiers = (Dictionary<string, HappinessModifier>)modifiersField.GetValue(null);
                HappinessHelper.ConvertTemporalBoostsToRegularBoosts();
                Check(testModifiers.Count == FishingFishCatalog.All.Count + 1,
                    "native registry callback registers every saved happiness definition");
                Check(SaveGameManager.Current.happinessModifiers.Count == 1
                    && SaveGameManager.Current.happinessModifiers[0].type == savedPerch.HappinessModifierType,
                    "saved perch modifier survives player bootstrap conversion");

                SaveGameManager.Current = new GameInstance { gameVariables = new GameVariables() };
                FishingHappinessService service = new FishingHappinessService();
                service.Initialize();
                Check(service.ApplyFishingActivity(), "fishing activity modifier applied");
                Check(SaveGameManager.Current.happinessModifiers.Count == 1
                    && SaveGameManager.Current.happinessModifiers[0].hoursLeft == 48,
                    "fishing activity stores one 48-hour modifier");
                service.ApplyFishingActivity();
                Check(SaveGameManager.Current.happinessModifiers.Count == 1,
                    "repeated fishing does not stack activity modifiers");
                Check(Mathf.Approximately(SaveGameManager.Current.Happiness, 10f),
                    "native happiness includes fishing activity amount");

                FishingFish roach = FishingFishCatalog.All[0];
                FishingFish sturgeon = FishingFishCatalog.All[FishingFishCatalog.All.Count - 1];
                FishingCatchBonusResult bestResult = service.ApplyCatch(sturgeon);
                Check(bestResult.CountedFish == sturgeon && CountCatchModifiers() == 1,
                    "best catch creates exactly one catch modifier");
                Check(Mathf.Approximately(SaveGameManager.Current.Happiness, 24f),
                    "native happiness combines activity and best catch");

                HappinessModifierData sturgeonData = FindCatchModifier(sturgeon);
                sturgeonData.hoursLeft = 19;
                FishingCatchBonusResult worseResult = service.ApplyCatch(roach);
                Check(worseResult.CountedFish == sturgeon && sturgeonData.hoursLeft == 19,
                    "worse catch neither replaces nor refreshes best bonus");
                Check(CountCatchModifiers() == 1, "worse catch cannot stack a second bonus");

                service.ApplyCatch(sturgeon);
                Check(FindCatchModifier(sturgeon).hoursLeft == 72,
                    "same best catch refreshes bonus to 72 hours");
                Check(CountCatchModifiers() == 1, "refreshed catch bonus remains unique");
            }
            finally
            {
                FishingHappinessBootstrapPatch.Uninstall(bootstrapHarmony);
                if (testModifiers != null)
                    foreach (HappinessModifier modifier in testModifiers.Values)
                        if (modifier != null) UnityEngine.Object.DestroyImmediate(modifier);
                SaveGameManager.Current = originalSave;
                modifiersField.SetValue(null, originalModifiers);
            }
        }

        private static UI.UIs _economyTestUi;
        private static int _moneyTestFault;
        private static int _moneyTestCalls;

        private static void CheckEconomyBehavior()
        {
            GameInstance originalSave = SaveGameManager.Current;
            var testHost = new GameObject("Fishing economy test (inactive UI)");
            testHost.SetActive(false);
            var harmony = new Harmony("capisoft.fishingmod.tests.economy");
            var messages = new List<string>();
            try
            {
                _economyTestUi = testHost.AddComponent<UI.UIs>();
                _economyTestUi.topBar = testHost.AddComponent<UI.Topbar.Topbar>();
                // Use native money/history/tax code with only an isolated, inactive HUD singleton.
                harmony.Patch(AccessTools.PropertyGetter(typeof(InstanceBehavior<UI.UIs>), "Instance"),
                    prefix: new HarmonyMethod(typeof(FishingModChecks), nameof(EconomyTestUi)));
                harmony.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.ChangeMoneySafe)),
                    prefix: new HarmonyMethod(typeof(FishingModChecks), nameof(BeforeTestMoney)),
                    postfix: new HarmonyMethod(typeof(FishingModChecks), nameof(AfterTestMoney)));
                _moneyTestFault = 0;
                _moneyTestCalls = 0;
                var save = new GameInstance { gameVariables = new GameVariables(), Money = 100f };
                SaveGameManager.Current = save;
                var bestBonus = new HappinessModifierData
                {
                    type = FishingFishCatalog.All[5].HappinessModifierType, hoursLeft = 19
                };
                save.happinessModifiers.Add(bestBonus);

                foreach (FishingFish fish in FishingFishCatalog.All)
                {
                    float before = save.Money;
                    int count = save.Transactions.Count;
                    var session = FinishedQte(fish, caught: true);
                    FishingMoneyResult result = FishingEconomyService.Settle(session, messages.Add);
                    Check(result.Recorded && result.Amount == fish.SalePrice && save.Money == before + fish.SalePrice,
                        "native bank receives the actual caught fish price: " + fish.Id);
                    Transaction entry = save.Transactions.Last();
                    Check(save.Transactions.Count == count + 1
                        && entry.transactionType == FishingEconomyService.SaleTransaction
                        && entry.transactionData["fishId"] == fish.Id && entry.amount == fish.SalePrice
                        && entry.timestamp.Day == save.Day && entry.balance == Mathf.Floor(save.Money),
                        "native sale ledger retains fish, amount, current day and balance: " + fish.Id);
                    Check(!entry.isTaxDeductible && entry.transactionCategories == null,
                        "fishing sale is not casino income or a business deduction: " + fish.Id);
                    Check(!FishingEconomyService.Settle(session, messages.Add).Recorded
                        && save.Money == before + fish.SalePrice && save.Transactions.Count == count + 1,
                        "repeated settlement cannot sell twice: " + fish.Id);
                }
                Check(save.happinessModifiers.Count == 1 && ReferenceEquals(save.happinessModifiers[0], bestBonus)
                    && bestBonus.hoursLeft == 19, "auto-sales leave the better active happiness modifier unchanged");
                Check(save.CurrentTaxPeriodGamblingWinnings == 0f && save.CurrentTaxPeriodGamblingLosses == 0f
                    && save.currentTaxPeriodDeductibleExpenses.Count == 0, "fishing does not alter casino or business deduction ledgers");

                foreach (float balance in new[] { 100f, 5f, 2.25f, 0f, -10f })
                {
                    save.Money = balance;
                    int count = save.Transactions.Count;
                    float cost = FishingEconomyRules.LineBreakCost(balance);
                    var session = FinishedQte(FishingFishCatalog.All[0], caught: false);
                    FishingMoneyResult result = FishingEconomyService.Settle(session, messages.Add);
                    Check(result.Recorded && result.Amount == -cost && save.Money == balance - cost,
                        "native line debit is capped by positive funds: " + balance);
                    Check(save.Transactions.Count == count + (cost > 0f ? 1 : 0),
                        "zero-cost break adds no financial history entry: " + balance);
                    if (cost > 0f)
                        Check(save.Transactions.Last().transactionType == FishingEconomyService.LineBreakTransaction
                            && !save.Transactions.Last().isTaxDeductible,
                            "native line replacement uses its own non-deductible transaction");
                    Check(!FishingEconomyService.Settle(session, messages.Add).Recorded
                        && save.Money == balance - cost, "native line debit is once only: " + balance);
                }

                int callsBefore = _moneyTestCalls;
                var unfinished = new FishingQteSession(FishingFishCatalog.All[0], new System.Random(1));
                Check(!FishingEconomyService.Settle(null, messages.Add).Recorded
                    && !FishingEconomyService.Settle(unfinished, messages.Add).Recorded
                    && _moneyTestCalls == callsBefore, "empty/cancelled or unfinished sessions do not call native money");

                FishingRuntime runtime = testHost.AddComponent<FishingRuntime>();
                FieldInfo stateField = AccessTools.Field(typeof(FishingRuntime), "_state");
                stateField.SetValue(runtime, Enum.Parse(stateField.FieldType, "WaitingForBite"));
                AccessTools.Field(typeof(FishingRuntime), "_log").SetValue(runtime, new Action<string>(messages.Add));
                AccessTools.Field(typeof(FishingRuntime), "_pendingFish").SetValue(runtime, FishingFishCatalog.All[5]);
                AccessTools.Field(typeof(FishingRuntime), "_biteTimer").SetValue(runtime, new FishingBiteTimer(20f));
                float cancellationBalance = save.Money;
                AccessTools.Method(typeof(FishingRuntime), "CancelSequence").Invoke(runtime, new object[] { "test waiting cancellation" });
                Check(stateField.GetValue(runtime).ToString() == "Idle"
                    && AccessTools.Field(typeof(FishingRuntime), "_pendingFish").GetValue(runtime) == null
                    && AccessTools.Field(typeof(FishingRuntime), "_biteTimer").GetValue(runtime) == null,
                    "waiting cancellation clears the pending fish and timer");
                Check(save.Money == cancellationBalance && _moneyTestCalls == callsBefore,
                    "cancelling a selected fish neither sells it nor charges for line break");

                save.Money = 100f;
                for (int fault = 1; fault <= 3; fault++)
                {
                    _moneyTestFault = fault;
                    int count = save.Transactions.Count;
                    callsBefore = _moneyTestCalls;
                    var session = FinishedQte(FishingFishCatalog.All[0], caught: true);
                    FishingMoneyResult result = FishingEconomyService.Settle(session, messages.Add);
                    Check(result.Recorded == (fault == 3) && save.Money == (fault == 3 ? 105f : 100f)
                        && save.Transactions.Count == count + (fault == 3 ? 1 : 0),
                        "native rejection/early exception/committed callback exception handled: " + fault);
                    Check(!FishingEconomyService.Settle(session, messages.Add).Recorded
                        && _moneyTestCalls == callsBefore + 1, "uncertain transactions are never retried: " + fault);
                }
                _moneyTestFault = 0;
                SaveGameManager.Current = null;
                Check(!FishingEconomyService.Settle(FinishedQte(FishingFishCatalog.All[0], true), messages.Add).Recorded,
                    "no active save cannot receive fishing money");
                Check(FishingText.MoneyResult(new FishingMoneyResult(true, 75f), true).Contains("75")
                    && FishingText.MoneyResult(new FishingMoneyResult(true, -2.25f), false).Contains("2"),
                    "catch and line result notifications include recorded amounts");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                _economyTestUi = null;
                _moneyTestFault = 0;
                SaveGameManager.Current = originalSave;
                UnityEngine.Object.DestroyImmediate(testHost);
            }
        }

        private static FishingQteSession FinishedQte(FishingFish fish, bool caught)
        {
            var session = new FishingQteSession(fish, new System.Random(1));
            for (int i = 0; i < 100 && !session.IsFinished; i++)
            {
                if (caught) session.Submit(session.ExpectedCommand);
                else session.Advance(fish.ResponseWindowSeconds + 1f);
            }
            return session;
        }

        private static bool EconomyTestUi(ref UI.UIs __result)
        {
            __result = _economyTestUi;
            return false;
        }

        private static bool BeforeTestMoney(ref bool __result)
        {
            _moneyTestCalls++;
            if (_moneyTestFault == 2) throw new InvalidOperationException("Synthetic pre-transaction exception");
            if (_moneyTestFault != 1) return true;
            __result = false;
            return false;
        }

        private static void AfterTestMoney()
        {
            if (_moneyTestFault == 3) throw new InvalidOperationException("Synthetic post-transaction exception");
        }

        private static int CountCatchModifiers()
        {
            int count = 0;
            List<HappinessModifierData> modifiers = SaveGameManager.Current.happinessModifiers;
            for (int i = 0; i < modifiers.Count; i++)
                if (modifiers[i] != null && FishingFishCatalog.FindByModifierType(modifiers[i].type) != null)
                    count++;
            return count;
        }

        private static HappinessModifierData FindCatchModifier(FishingFish fish)
        {
            List<HappinessModifierData> modifiers = SaveGameManager.Current.happinessModifiers;
            for (int i = 0; i < modifiers.Count; i++)
                if (modifiers[i] != null && modifiers[i].type == fish.HappinessModifierType)
                    return modifiers[i];
            throw new InvalidOperationException("Expected catch modifier was not found: " + fish.Id);
        }

        private static void CheckRodRenderingConfiguration()
        {
            GameObject rodObject = new GameObject("Fishing Test Rod");
            LineRenderer rod = rodObject.AddComponent<LineRenderer>();
            FishingCastVisual.ConfigureRodWidth(rod, 1.35f);
            Keyframe[] keys = rod.widthCurve.keys;
            Check(Mathf.Approximately(rod.widthMultiplier, 1f), "rod width multiplier remains absolute");
            Check(keys.Length == 3, "rod taper has three controlled widths");
            Check(keys[0].value <= 0.034f && keys[0].value > keys[keys.Length - 1].value,
                "rod base stays slender");
            Check(keys[keys.Length - 1].value <= 0.009f, "rod tip stays slender");

            Shader shader = Shader.Find("HDRP/Unlit");
            Check(shader != null, "HDRP unlit shader available");
            Material material = new Material(shader);
            Color expected = new Color(0.08f, 0.13f, 0.08f, 1f);
            FishingCastVisual.ApplyMaterialColor(material, expected);
            Check(material.HasProperty("_UnlitColor")
                && Approximately(material.GetColor("_UnlitColor"), expected), "HDRP rod color applied");

            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(rodObject);
        }

        private static void CheckStaticWaterDetection()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var detector = new FishingWaterDetector();
            Check(detector.IndexedTileCount == StaticWater.GameWaterAtlas.PolygonCount && detector.SurfaceCount == 1, "static atlas ready without scene objects");
            var sites = new[] {new Vector3(0,-2.8f,-500), new Vector3(-1000,-2.8f,-700),
                new Vector3(-1800,-2.8f,-700), new Vector3(-3500,-2.8f,-1700)};
            var sectors = new[] {"city-port/", "bridge/", "industry/", "hamptons/"};
            for (int i = 0; i < sites.Length; i++)
            {
                Vector3 target = sites[i];
                Check(detector.TryGetWaterPoint(new Ray(target + Vector3.up * 30, Vector3.down), null, out var point)
                    && Vector3.Distance(point,target) < .01f, "static water vertical ray " + sectors[i]);
                Check(detector.LastMatchedZoneId.StartsWith(sectors[i], StringComparison.Ordinal), "static sector identity " + sectors[i]);
                Vector3 camera = target + new Vector3(15,30,20);
                Check(detector.TryGetWaterPoint(new Ray(camera,target-camera), null, out point)
                    && Vector3.Distance(point,target) < .01f, "static water angled camera " + sectors[i]);
            }
            foreach (var dry in new[] {new Vector3(0,20,-100),new Vector3(-1600,20,-1400),new Vector3(-3000,20,-1200),new Vector3(9000,20,9000)})
                Check(!detector.TryGetWaterPoint(new Ray(dry,Vector3.down),null,out _), "land or unmapped point rejected " + dry);
            Check(detector.LastMatchedZoneId == null, "failed static probe clears previous zone");
            Vector3 origin = sites[0] + Vector3.up * 30;
            Check(!detector.TryGetWaterPoint(new Ray(origin,Vector3.up),null,out _), "backward sea plane rejected");
            Check(!detector.TryGetWaterPoint(new Ray(origin,Vector3.right),null,out _), "parallel sea ray rejected");
            Check(!detector.TryGetWaterPoint(new Ray(sites[0]+Vector3.up*1100,Vector3.down),null,out _), "out of range sea rejected");
            Check(!detector.TryGetWaterPoint(new Ray(new Vector3(float.NaN,20,-500),Vector3.down),null,out _), "non-finite target rejected");

            GameObject solid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                solid.name = "Ocean Water Dock"; // A misleading water name must not bypass blockers.
                solid.transform.position = new Vector3(0,0,-500);
                solid.transform.localScale = new Vector3(5,1,5);
                Physics.SyncTransforms();
                Ray ray = new Ray(origin,Vector3.down);
                Check(!detector.TryGetWaterPoint(ray,null,out _), "static water blocked by quay even with water name");
                Check(detector.LastFailureReason == "solid_occlusion" && detector.LastBlockingCollider == solid.GetComponent<Collider>(),
                    "rejected quay identifies actual blocking collider");
                Check(detector.TryGetWaterPoint(ray,solid.transform,out _), "ignored player root does not block static water");
                Check(detector.LastFailureReason == null && detector.LastBlockingCollider == null,
                    "successful static query clears previous rejection details");
                solid.GetComponent<Collider>().isTrigger = true;
                Physics.SyncTransforms();
                Check(detector.TryGetWaterPoint(ray,null,out _), "trigger above static water ignored");
                solid.GetComponent<Collider>().isTrigger = false;
                solid.transform.position = new Vector3(0,-10,-500);
                Physics.SyncTransforms();
                Check(detector.TryGetWaterPoint(ray,null,out _), "submerged bottom does not block sea surface");
                solid.transform.position = new Vector3(-3000,0,-1200);
                Physics.SyncTransforms();
                Check(!detector.TryGetWaterPoint(new Ray(new Vector3(-3000,20,-1200),Vector3.down),null,out _),
                    "water-named collider on land cannot bypass polygon rejection");
                Check(detector.LastFailureReason == "outside_static_polygons" && detector.LastBlockingCollider == null,
                    "polygon miss is distinguished from physical occlusion");
            }
            finally { UnityEngine.Object.DestroyImmediate(solid); }

            GameObject crowded = new GameObject("StaticWater crowded ray");
            try
            {
                for (int i=0; i<140; i++)
                {
                    var trigger = new GameObject("trigger " + i);
                    trigger.transform.SetParent(crowded.transform);
                    trigger.transform.position = new Vector3(0, i*.1f, -500);
                    trigger.AddComponent<BoxCollider>().isTrigger = true;
                }
                var blocker = new GameObject("real blocker");
                blocker.transform.SetParent(crowded.transform);
                blocker.transform.position = new Vector3(0,-1,-500);
                var collider = blocker.AddComponent<BoxCollider>();
                Physics.SyncTransforms();
                Check(Physics.RaycastAll(new Ray(origin,Vector3.down),30f,Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide).Length > 128, "static saturation fixture exceeds reusable buffer");
                Check(!detector.TryGetWaterPoint(new Ray(origin,Vector3.down),null,out _), "saturated ray buffer retains blocker");
                collider.enabled=false;
                Physics.SyncTransforms();
                Check(detector.TryGetWaterPoint(new Ray(origin,Vector3.down),null,out _), "saturated trigger-only ray permits water");
            }
            finally { UnityEngine.Object.DestroyImmediate(crowded); }
            int builds = detector.CacheBuildCount;
            for (int i=0;i<30;i++) {detector.RequestRefresh();detector.ForceRefresh();detector.AdvanceIndexing();}
            Check(detector.CacheBuildCount == builds && !detector.IsIndexing && detector.LastIndexStepObjects == 0,
                "static runtime never enumerates scene after refresh requests or updates");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Check(detector.TryGetWaterPoint(new Ray(origin,Vector3.down),null,out _), "static atlas survives scene change without scan");
        }

        private static void CheckHamptonsMarinaMousePlane()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root = new GameObject("TheHamptons");
            try
            {
                var roads = new GameObject("Roads"); roads.transform.SetParent(root.transform);
                var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                plane.name="RoadGroundPlane (4)"; plane.transform.SetParent(roads.transform);
                plane.layer=LayerMask.NameToLayer("Roads");
                plane.transform.position=new Vector3(-2548.2f,.05f,-1110.4f);
                plane.transform.localScale=new Vector3(56.2f,1f,67.57f);
                var renderer=plane.GetComponent<Renderer>(); renderer.enabled=false;
                var collider=plane.GetComponent<Collider>();
                Physics.SyncTransforms();
                Check(FishingWaterDetector.IsHamptonsMarinaMousePlane(collider),"verified marina mouse plane identified");
                var groundRoot=new GameObject("HamptonsGroundPlanes"); groundRoot.transform.SetParent(root.transform);
                var ground=new GameObject("Ground (2)"); ground.transform.SetParent(groundRoot.transform);
                ground.layer=LayerMask.NameToLayer("Ground");
                ground.transform.position=new Vector3(-2548.1523f,-1f,-1110.37f);
                ground.transform.localScale=new Vector3(562.0169f,.5626125f,675.6826f);
                var groundCollider=ground.AddComponent<BoxCollider>();
                Physics.SyncTransforms();
                Check(FishingWaterDetector.IsHamptonsMarinaGroundVolume(groundCollider),"verified marina ground volume identified");
                var detector=new FishingWaterDetector();
                var targets=new[]{new Vector3(-2700.5f,-2.8f,-870.6f),new Vector3(-2700.1f,-2.8f,-871.4f),
                    new Vector3(-2696.7f,-2.8f,-875.7f),new Vector3(-2706.1f,-2.8f,-876f),
                    new Vector3(-2696.6f,-2.8f,-878.5f),new Vector3(-2697.6f,-2.8f,-873.5f),
                    new Vector3(-2698.3f,-2.8f,-875.4f),new Vector3(-2704.1f,-2.8f,-870.3f),
                    new Vector3(-2698.8f,-2.8f,-864.9f),
                    new Vector3(-2697.8f,-2.8f,-871f),new Vector3(-2693.4f,-2.8f,-870.3f),
                    new Vector3(-2697.9f,-2.8f,-870.3f),new Vector3(-2697.1f,-2.8f,-869.5f),
                    new Vector3(-2698.8f,-2.8f,-872.8f),new Vector3(-2704f,-2.8f,-871f),
                    new Vector3(-2696.3f,-2.8f,-890.4f)};
                foreach(var target in targets)
                {
                    Vector3 origin=target+new Vector3(10,30,-15);Ray ray=new Ray(origin,target-origin);
                    Check(groundCollider.Raycast(ray,out _,100f),"reported marina ray also hits invisible ground volume");
                    Check(collider.Raycast(ray,out _,100f),"reported marina ray actually hits invisible mouse plane");
                    Check(detector.TryGetWaterPoint(ray,null,out var water) && Vector3.Distance(water,target)<.01f,
                        "reported marina target accepted behind invisible mouse plane " + target);
                }
                Ray direct=new Ray(targets[0]+Vector3.up*30,Vector3.down);
                var obstruction=GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstruction.transform.SetParent(root.transform);
                obstruction.transform.position=new Vector3(targets[0].x,1f,targets[0].z);
                obstruction.transform.localScale=new Vector3(4,1,4);
                obstruction.name="Real pontoon deck";
                Physics.SyncTransforms();
                Check(!detector.TryGetWaterPoint(direct,null,out _)
                    && detector.LastBlockingCollider==obstruction.GetComponent<Collider>(),"real pontoon still blocks above exempt plane");
                obstruction.name="Boat";
                obstruction.layer=LayerMask.NameToLayer("Vehicles");
                Physics.SyncTransforms();
                Check(!detector.TryGetWaterPoint(direct,null,out _),"boat still blocks above exempt plane");
                UnityEngine.Object.DestroyImmediate(obstruction);
                groundRoot.name="OtherGround";
                Check(!detector.TryGetWaterPoint(direct,null,out _) && detector.LastBlockingCollider==groundCollider,
                    "unrecognized rendererless Ground collider still blocks");
                groundRoot.name="HamptonsGroundPlanes";
                ground.transform.localScale=Vector3.one; Physics.SyncTransforms();
                Check(!FishingWaterDetector.IsHamptonsMarinaGroundVolume(groundCollider),"small ground collider not exempt");
                ground.transform.localScale=new Vector3(562.0169f,.5626125f,675.6826f); Physics.SyncTransforms();
                ground.AddComponent<MeshRenderer>();
                Check(!FishingWaterDetector.IsHamptonsMarinaGroundVolume(groundCollider),"ground with renderer not exempt");
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<MeshRenderer>());
                renderer.enabled=true;
                Check(!FishingWaterDetector.IsHamptonsMarinaMousePlane(collider),"visible road plane not exempt");
                Check(!detector.TryGetWaterPoint(direct,null,out _),"visible geometry blocks marina water");
                renderer.enabled=false;
                roads.name="AnotherRoads";
                Check(!FishingWaterDetector.IsHamptonsMarinaMousePlane(collider),"same name in other hierarchy not exempt");
                roads.name="Roads";plane.transform.localScale=Vector3.one;
                Physics.SyncTransforms();
                Check(!FishingWaterDetector.IsHamptonsMarinaMousePlane(collider),"small collision-only mesh not exempt");
                Check(!detector.TryGetWaterPoint(new Ray(new Vector3(-2600,20,-1100),Vector3.down),null,out _),
                    "marina fix does not turn inland coordinates into water");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void CheckHamptonsEastCoast()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("TheHamptons");
            try
            {
                var roads=new GameObject("Roads"); roads.transform.SetParent(root.transform);
                var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);
                plane.name="RoadGroundPlane (2)"; plane.transform.SetParent(roads.transform);
                plane.layer=LayerMask.NameToLayer("Roads");
                plane.transform.position=new Vector3(-2333.6343f,.05f,-1316.2461f);
                plane.transform.localScale=new Vector3(47.48446f,1,108.74351f);
                var renderer=plane.GetComponent<Renderer>(); renderer.enabled=false;
                var collider=plane.GetComponent<Collider>();
                var groundRoot=new GameObject("HamptonsGroundPlanes"); groundRoot.transform.SetParent(root.transform);
                var ground=new GameObject("Ground"); ground.transform.SetParent(groundRoot.transform);
                ground.layer=LayerMask.NameToLayer("Ground");
                ground.transform.position=new Vector3(-2328.9805f,-1,-1316.2461f);
                ground.transform.localScale=new Vector3(484.1732f,.5626125f,1087.4351f);
                var groundCollider=ground.AddComponent<BoxCollider>();
                Physics.SyncTransforms();
                var detector=new FishingWaterDetector();
                var targets=new[]{new Vector3(-2230.5f,-2.8f,-874.3f),new Vector3(-2235.2f,-2.8f,-877.2f),
                    new Vector3(-2236.4f,-2.8f,-875.3f)};
                foreach(var target in targets)
                {
                    Vector3 origin=target+new Vector3(10,30,-15); Ray ray=new Ray(origin,target-origin);
                    Check(collider.Raycast(ray,out _,100f) && groundCollider.Raycast(ray,out _,100f),
                        "east coast reported ray hits both invisible layers " + target);
                    Check(detector.TryGetWaterPoint(ray,null,out var point) && Vector3.Distance(point,target)<.01f,
                        "east coast reported water target accepted " + target);
                }
                Ray direct=new Ray(targets[0]+Vector3.up*30,Vector3.down);
                var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.transform.SetParent(root.transform);
                blocker.transform.position=targets[0]+Vector3.up*3; blocker.transform.localScale=new Vector3(4,1,4);
                Physics.SyncTransforms();
                Check(!detector.TryGetWaterPoint(direct,null,out _),"east coast real dock blocks");
                blocker.layer=LayerMask.NameToLayer("Vehicles");
                Check(!detector.TryGetWaterPoint(direct,null,out _),"east coast boat blocks");
                UnityEngine.Object.DestroyImmediate(blocker);
                renderer.enabled=true;
                Check(!detector.TryGetWaterPoint(direct,null,out _),"east coast visible road blocks");
                renderer.enabled=false; groundRoot.name="OtherGround";
                Check(!detector.TryGetWaterPoint(direct,null,out _) && detector.LastBlockingCollider==groundCollider,
                    "east coast unrelated invisible ground blocks");
                groundRoot.name="HamptonsGroundPlanes"; roads.name="OtherRoads";
                Check(!FishingWaterDetector.IsHamptonsEastCoastOccluder(collider),"east coast wrong road hierarchy not exempt");
                roads.name="Roads"; plane.transform.localScale=Vector3.one; ground.transform.localScale=Vector3.one;
                Physics.SyncTransforms();
                Check(!FishingWaterDetector.IsHamptonsEastCoastOccluder(collider)
                    && !FishingWaterDetector.IsHamptonsEastCoastOccluder(groundCollider),"east coast small colliders not exempt");
                Check(!detector.TryGetWaterPoint(new Ray(new Vector3(-2300,20,-1300),Vector3.down),null,out _),
                    "east coast inland coordinates remain outside water");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void CheckLandInFrontOfWater()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("TheHamptons");
            try
            {
                var roads=new GameObject("Roads");roads.transform.SetParent(root.transform);
                var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);
                plane.name="RoadGroundPlane (2)";plane.transform.SetParent(roads.transform);
                plane.layer=LayerMask.NameToLayer("Roads");
                plane.transform.position=new Vector3(-2333.6343f,.05f,-1316.2461f);
                plane.transform.localScale=new Vector3(47.48446f,1,108.74351f);
                plane.GetComponent<Renderer>().enabled=false;Physics.SyncTransforms();
                var detector=new FishingWaterDetector();
                Vector3 target=new Vector3(-2540.85368f,-2.8f,-1810.61994f);
                Vector3 offset=new Vector3(12,35,-16);
                Check(!detector.TryGetWaterPoint(new Ray(target+offset,-offset),null,out _)
                    && detector.LastFailureReason=="land_before_water",
                    "click on native land cannot project through to atlas water behind it");
                Check(detector.TryGetWaterPoint(new Ray(target+Vector3.up*35,Vector3.down),null,out _),
                    "same water target remains valid when ray does not cross land");
                Check(!detector.TryGetWaterPoint(new Ray(new Vector3(-2539.8765f,35,-1811.9228f),Vector3.down),null,out _),
                    "actual road impact point remains land");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void CheckWaterDetection()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject waterRoot = new GameObject("Ocean Water");
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            water.name = "Water Tile A";
            water.transform.position = new Vector3(0f, 0f, 10f);
            water.transform.localScale = new Vector3(12f, 0.1f, 12f);
            water.transform.SetParent(waterRoot.transform, true);
            GameObject secondWaterTile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            secondWaterTile.name = "Water Tile B";
            secondWaterTile.transform.position = new Vector3(12f, 0f, 10f);
            secondWaterTile.transform.localScale = new Vector3(12f, 0.1f, 12f);
            secondWaterTile.transform.SetParent(waterRoot.transform, true);
            Physics.SyncTransforms();

            FishingWaterDetector detector = new FishingWaterDetector(useStaticAtlas: false);
            detector.ForceRefresh();
            Check(detector.SurfaceCount == 1, "root water tiles share one height group");
            Check(detector.IndexedTileCount == 2, "root water tiles keep local bounds");
            Ray directRay = new Ray(new Vector3(0f, 5f, 0f), new Vector3(0f, -0.5f, 1f).normalized);
            Check(detector.TryGetWaterPoint(directRay, null, out Vector3 directPoint), "direct water collider hit");
            Check(Mathf.Abs(directPoint.y) < 0.2f, "direct water height");
            Check(detector.CacheBuildCount == 1, "water cache reused on click");

            UnityEngine.Object.DestroyImmediate(waterRoot);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material tiledWaterMaterial = new Material(Shader.Find("HDRP/Unlit")) { name = "Harbor Water" };
            GameObject plane = CreateWaterPlane("Surface Tile Low", new Vector3(0f, 0f, 0f), tiledWaterMaterial);
            GameObject highLeft = CreateWaterPlane("Surface Tile High Left", new Vector3(-40f, 10f, 0f), tiledWaterMaterial);
            GameObject highRight = CreateWaterPlane("Surface Tile High Right", new Vector3(40f, 10f, 0f), tiledWaterMaterial);
            Physics.SyncTransforms();

            detector.ForceRefresh();
            Check(detector.SurfaceCount == 2, "water tiles indexed by elevation");
            Check(detector.IndexedTileCount == 3, "material water tiles keep local bounds");
            Ray fallbackRay = new Ray(new Vector3(0f, 20f, 0f), Vector3.down);
            Check(detector.TryGetWaterPoint(fallbackRay, null, out Vector3 fallbackPoint), "renderer plane fallback");
            Check(Mathf.Abs(fallbackPoint.y) < 0.05f, "distant higher tiles do not create false water");

            GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "Concrete Quay";
            blocker.transform.position = new Vector3(0f, 15f, 0f);
            blocker.transform.localScale = new Vector3(8f, 1.5f, 8f);
            Physics.SyncTransforms();
            Check(!detector.TryGetWaterPoint(fallbackRay, null, out _), "occluded water rejected");

            UnityEngine.Object.DestroyImmediate(blocker);
            UnityEngine.Object.DestroyImmediate(plane);
            UnityEngine.Object.DestroyImmediate(highLeft);
            UnityEngine.Object.DestroyImmediate(highRight);
            UnityEngine.Object.DestroyImmediate(tiledWaterMaterial);
        }

        private static GameObject CreateWaterPlane(string name, Vector3 position, Material material)
        {
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = name;
            plane.transform.position = position;
            plane.transform.localScale = new Vector3(2f, 1f, 2f);
            plane.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(plane.GetComponent<Collider>());
            return plane;
        }

        private static void PumpWaterIndex(FishingWaterDetector detector)
        {
            int before = detector.CacheBuildCount;
            for (int i = 0; i < 10000; i++)
            {
                detector.AdvanceIndexing();
                if (detector.CacheBuildCount > before) return;
            }
            throw new InvalidOperationException("Incremental water indexing did not finish.");
        }

        private static void CheckExtendedWaterDetection()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var material = new Material(Shader.Find("HDRP/Unlit")) { name = "Lake Water" };
            var district = new GameObject("Ocean Harbor District");
            var water = CreateWaterPlane("WaterSurface", Vector3.zero, material);
            water.transform.SetParent(district.transform, true);
            var dock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dock.name = "Concrete Dock";
            dock.transform.SetParent(district.transform, true);
            dock.transform.position = new Vector3(0f, 3f, 0f);
            dock.transform.localScale = new Vector3(5f, 0.5f, 5f);
            Physics.SyncTransforms();
            var detector = new FishingWaterDetector(useStaticAtlas: false);
            detector.ForceRefresh();
            Check(detector.IndexedTileCount == 1, "water-named district does not classify its dock as water");
            Check(!detector.TryGetWaterPoint(new Ray(new Vector3(0f, 20f, 0f), Vector3.down), null, out _),
                "solid bridge or dock remains an occluder even under an ocean parent");
            Check(detector.TryGetWaterPoint(new Ray(new Vector3(7f, 40f, 0f), Vector3.down), null, out _),
                "visible water beside an elevated dock remains fishable");
            UnityEngine.Object.DestroyImmediate(district);

            detector = new FishingWaterDetector(useStaticAtlas: false);
            detector.ForceRefresh();
            var streamed = CreateWaterPlane("ParkPond", new Vector3(100f, 2f, 0f), material);
            int initialBuilds = detector.CacheBuildCount;
            detector.RequestRefresh();
            for (int i = 0; i < 20; i++)
                detector.TryGetWaterPoint(new Ray(new Vector3(500f, 30f, 0f), Vector3.down), null, out _);
            Check(detector.CacheBuildCount == initialBuilds && !detector.IsIndexing,
                "expired refresh interval and ground clicks never rebuild or start a city scan");
            PumpWaterIndex(detector);
            Check(detector.TryGetWaterPoint(new Ray(new Vector3(100f, 30f, 0f), Vector3.down), null, out var streamedPoint)
                && Mathf.Abs(streamedPoint.y - 2f) < 0.01f, "renderer-only pond loaded after initial scan is recovered by budgeted indexing");
            int builds = detector.CacheBuildCount;
            for (int i = 0; i < 10; i++) detector.TryGetWaterPoint(new Ray(new Vector3(500f, 30f, 0f), Vector3.down), null, out _);
            Check(detector.CacheBuildCount == builds, "repeated misses do not repeatedly rescan the city");

            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Check(EditorSceneManager.SaveScene(activeScene, "Assets/FishingWaterTest-" + Guid.NewGuid().ToString("N") + ".unity"),
                "isolated scene fixture saved before additive-scene test");
            var additive = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(activeScene);
            var bay = CreateWaterPlane("Bay Surface", new Vector3(200f, 4f, 0f), material);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(bay, additive);
            detector.TryGetWaterPoint(new Ray(new Vector3(500f, 30f, 0f), Vector3.down), null, out _);
            Check(detector.CacheBuildCount == builds, "additive scene change never causes synchronous work in a click");
            PumpWaterIndex(detector);
            Check(detector.TryGetWaterPoint(new Ray(new Vector3(200f, 30f, 0f), Vector3.down), null, out var bayPoint)
                && Mathf.Abs(bayPoint.y - 4f) < 0.01f && detector.CacheBuildCount == builds + 1,
                "additive neighborhood water refreshes even while the active scene is unchanged");
            EditorSceneManager.CloseScene(additive, true);
            UnityEngine.Object.DestroyImmediate(streamed);

            var objects = new GameObject("Incremental water scan budget fixture");
            for (int i = 0; i < 1024; i++) new GameObject("Non-water object").transform.SetParent(objects.transform, false);
            detector.RequestRefresh();
            detector.AdvanceIndexing();
            Check(detector.IsIndexing && detector.LastIndexStepObjects <= 128,
                "large hierarchy scan yields instead of processing every object in one frame");
            Check(detector.CacheBuildCount == builds + 1 && detector.IndexedTileCount > 0,
                "previous published water index remains available during incremental rebuild");
            detector.CancelIndexing();
            Check(!detector.IsIndexing, "unload cancels partial scan and releases staging references");
            UnityEngine.Object.DestroyImmediate(objects);

            var triangle = new GameObject("Pond mesh");
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(0f, 0f, 10f) },
                triangles = new[] { 0, 2, 1 }
            };
            mesh.RecalculateBounds();
            triangle.AddComponent<MeshFilter>().sharedMesh = mesh;
            triangle.AddComponent<MeshRenderer>().sharedMaterial = material;
            detector.ForceRefresh();
            Check(detector.TryGetWaterPoint(new Ray(new Vector3(2f, 20f, 2f), Vector3.down), null, out _),
                "irregular pond mesh contains its actual water footprint");
            Check(!detector.TryGetWaterPoint(new Ray(new Vector3(9f, 20f, 9f), Vector3.down), null, out _),
                "mesh bounding-box corners do not invent water outside an irregular bay");
            UnityEngine.Object.DestroyImmediate(triangle);
            UnityEngine.Object.DestroyImmediate(mesh);

            Type hdrpType = Type.GetType("UnityEngine.Rendering.HighDefinition.WaterSurface, Unity.RenderPipelines.HighDefinition.Runtime", true);
            var hdrpObject = new GameObject("Unnamed native water");
            var hdrp = hdrpObject.AddComponent(hdrpType);
            FieldInfo geometry = hdrpType.GetField("geometryType");
            geometry.SetValue(hdrp, Enum.Parse(geometry.FieldType, "Quad"));
            hdrpObject.transform.position = new Vector3(0f, 1f, 0f);
            hdrpObject.transform.localScale = new Vector3(10f, 1f, 10f);
            detector.ForceRefresh();
            Check(detector.TryGetWaterPoint(new Ray(new Vector3(0f, 20f, 0f), Vector3.down), null, out var nativePoint)
                && Mathf.Abs(nativePoint.y - 1f) < 0.01f, "native finite HDRP water works without a renderer or collider");
            Check(!detector.TryGetWaterPoint(new Ray(new Vector3(20f, 20f, 0f), Vector3.down), null, out _),
                "native finite HDRP water never becomes an infinite plane");
            geometry.SetValue(hdrp, Enum.Parse(geometry.FieldType, "Infinite"));
            detector.ForceRefresh();
            Check(detector.TryGetWaterPoint(new Ray(new Vector3(200f, 20f, 0f), Vector3.down), null, out _),
                "explicit native infinite ocean covers distant bays at its actual water level");
            UnityEngine.Object.DestroyImmediate(hdrpObject);
            UnityEngine.Object.DestroyImmediate(material);
        }

        private static void CheckElevatedShoreResolution()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            var island = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Mesh mesh = ground.GetComponent<MeshFilter>().sharedMesh;
            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
            var filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
            foreach (float elevation in new[] { 0f, 24f, 60f })
            {
                ground.transform.position = new Vector3(0f, elevation, 10f);
                ground.transform.localScale = new Vector3(1f, 1f, 2f);
                island.transform.position = new Vector3(0f, elevation, 25f);
                island.transform.localScale = new Vector3(0.3f, 1f, 0.3f);
                var sources = new List<NavMeshBuildSource>();
                foreach (var surface in new[] { ground, island })
                    sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh,
                        sourceObject = mesh, transform = surface.transform.localToWorldMatrix, area = 0 });
                NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources,
                    new Bounds(new Vector3(0f, elevation, 15f), new Vector3(30f, 10f, 40f)), Vector3.zero, Quaternion.identity);
                Check(data != null, "elevated quay/island NavMesh built: " + elevation);
                NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);
                try
                {
                    Check(NavMesh.SamplePosition(new Vector3(0f, elevation, 2f), out NavMeshHit start, 2f, filter),
                        "player sampled on elevated quay: " + elevation);
                    var resolver = new FishingShoreResolver();
                    Check(resolver.TryFindClosestReachable(settings.agentTypeID, NavMesh.AllAreas, start.position,
                        new Vector3(0f, -2.8f, 25f), out Vector3 shore, out float length),
                        "reachable shore found above sea level despite a nearer disconnected island: " + elevation);
                    Check(shore.z > 17f && shore.z < 21f && Mathf.Abs(shore.y - elevation) < 0.25f && length > 10f,
                        "quay/bridge route stays on the player's connected level: " + elevation);
                }
                finally { instance.Remove(); UnityEngine.Object.DestroyImmediate(data); }
            }
            UnityEngine.Object.DestroyImmediate(ground);
            UnityEngine.Object.DestroyImmediate(island);
        }

        private static void CheckShoreResolution()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Test Shore";
            ground.transform.position = new Vector3(0f, 0f, 10f);
            ground.transform.localScale = new Vector3(1f, 1f, 2f);

            Mesh mesh = ground.GetComponent<MeshFilter>().sharedMesh;
            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
            List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = mesh,
                    transform = ground.transform.localToWorldMatrix,
                    area = 0
                }
            };
            Bounds bounds = new Bounds(new Vector3(0f, 0f, 10f), new Vector3(30f, 10f, 40f));
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            Check(data != null, "test NavMesh built");
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);

            NavMeshQueryFilter filter = new NavMeshQueryFilter
            {
                agentTypeID = settings.agentTypeID,
                areaMask = NavMesh.AllAreas
            };
            Check(NavMesh.SamplePosition(new Vector3(0f, 0f, 2f), out NavMeshHit startHit, 2f, filter),
                "test start sampled on NavMesh");

            FishingShoreResolver resolver = new FishingShoreResolver();
            Vector3 waterPoint = new Vector3(0f, 0f, 25f);
            Check(resolver.TryFindClosestReachable(settings.agentTypeID, NavMesh.AllAreas, startHit.position, waterPoint,
                out Vector3 shore, out float routeLength), "reachable shoreline resolved");
            Check(shore.z > 17f && shore.z < 21f, "shoreline remains near navigable edge");
            Check(routeLength > 10f && routeLength < 25f, "complete route length measured");

            instance.Remove();
            UnityEngine.Object.DestroyImmediate(data);
            UnityEngine.Object.DestroyImmediate(ground);
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            _checks++;
            Debug.Log("[FishingMod.Checks] PASS " + description);
        }

        private static bool Approximately(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) < 0.001f
                && Mathf.Abs(left.g - right.g) < 0.001f
                && Mathf.Abs(left.b - right.b) < 0.001f
                && Mathf.Abs(left.a - right.a) < 0.001f;
        }
    }
}
#endif
