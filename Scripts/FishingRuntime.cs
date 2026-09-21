using System;
using HarmonyLib;
using BigAmbitions.InputSystem;
using Helpers;
using UI.MiniMenu;
using UI.Smartphone;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FishingMod
{
    [DefaultExecutionOrder(10000)]
    public sealed class FishingRuntime : MonoBehaviour
    {
        private const float EmptyLineRetrievalSeconds = 1.6f;

        private enum SequenceState
        {
            Idle,
            Casting,
            WaitingForBite,
            RetrievingEmptyLine,
            Hooked
        }

        private readonly FishingWaterDetector _waterDetector = new FishingWaterDetector();
        private readonly FishingHappinessService _happiness = new FishingHappinessService();
        private readonly FishingQteOverlay _overlay = new FishingQteOverlay();
        private readonly System.Random _random = new System.Random(Guid.NewGuid().GetHashCode());

        private Action<string> _log;
        private FishingAudio _audio;
        private SequenceState _state;
        private PlayerController _player;
        private FishingCastVisual _cast;
        private FishingCastVisual _preparedCast;
        private float _nextVisualPrepareAt;
        private Vector3 _waterPoint;
        private Vector3 _shorePoint;
        private bool _ownsNavigationBlocker;
        private bool _disposed;
        private static FishingRuntime _inputOwner;
        private Harmony _inputHarmony;
        private PlayerController _pendingInputPlayer;
        private bool _waitingForInputRelease;
        private int _lastQteInputFrame;
        private FishingFish _pendingFish;
        private FishingBiteTimer _biteTimer;
        private float _emptyLineRetrieveElapsed;
        private bool _emptyLineReelRepeatPlayed;
        private bool _activityBonusApplied;
        private FishingQteSession _qte;
        private bool _automaticCatch;
        private float _automaticCatchElapsed;
        private FishingQteOutcome _lastQteOutcome;
        private float _qteFeedbackUntil;
        private string _resultMessage;
        private float _resultMessageUntil;
        private float _nextClickDiagnosticAt;

        internal void Initialize(string modRootPath, Action<string> log)
        {
            _log = log ?? (_ => { });
            _inputOwner = this;
            _inputHarmony = new Harmony("capisoft.fishingmod.qte-input");
            _inputHarmony.Patch(AccessTools.Method(typeof(GameManager), "ShouldBlockKeyboardShortcuts"),
                prefix: new HarmonyMethod(typeof(FishingRuntime), nameof(BlockQteShortcuts)));
            _inputHarmony.Patch(AccessTools.Method(typeof(GameSpeedController), "TogglePause"),
                prefix: new HarmonyMethod(typeof(FishingRuntime), nameof(BlockQteSpacePause)));
            try
            {
                _audio = new FishingAudio();
                _audio.Initialize(gameObject, modRootPath, _log);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _audio?.Dispose();
                _audio = null;
                _log("[FishingMod] Audio initialization failed; fishing remains playable without sound.");
            }
            _waterDetector.ForceRefresh();
            try
            {
                _happiness.Initialize();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _log("[FishingMod] Happiness registry was not ready; registration will be retried after the cast.");
            }
            _log("[FishingMod] Static water atlas ready: " + FishingWaterDetector.StaticPolygonCount
                + " polygons, Y=" + FishingWaterDetector.StaticSeaHeight
                + "; scene scans disabled; click visibility checks enabled; cast in place; F casts with the same water and land validation.");
        }

        private void Update()
        {
            if (_disposed) return;
            if (_waitingForInputRelease)
            {
                bool held = QteKeysHeld();
                if (held) _lastQteInputFrame = Time.frameCount;
                if (FishingInputRules.CanReleaseQteInput(held, Time.frameCount, _lastQteInputFrame))
                    ReleasePendingInput();
                return;
            }
            _cast?.RestoreFingerPose();

            if ((_state == SequenceState.Casting || _state == SequenceState.WaitingForBite
                || _state == SequenceState.RetrievingEmptyLine) && TryCancelPendingActivity()) return;

            if (_state == SequenceState.Casting)
            {
                if (_cast == null || !_cast.IsAlive)
                {
                    CancelSequence("character or cast visual disappeared");
                    return;
                }

                if (IsQtePausedByUi()) return;
                float previousElapsed = _cast.Elapsed;
                _cast.Advance(Time.unscaledDeltaTime);
                _biteTimer?.AdvanceCast(previousElapsed, _cast.Elapsed, _cast.ImpactTime);
                if (_cast.ConsumeReleaseSoundEvent())
                    _audio?.Play(FishingSound.ReelOut, 0.42f, 1.04f);
                if (_cast.ConsumeSplashSoundEvent())
                    _audio?.Play(FishingSound.BobberSplash, 0.56f, 1f);
                if (_cast.IsComplete) FinishCast();
                return;
            }

            if (_state == SequenceState.WaitingForBite)
            {
                UpdateWaitingForBite();
                return;
            }

            if (_state == SequenceState.RetrievingEmptyLine)
            {
                UpdateEmptyLineRetrieval();
                return;
            }

            if (_state == SequenceState.Hooked)
            {
                UpdateQte();
                return;
            }

            PrepareCastVisual();
            TryHandleWaterClick();
        }

        private void LateUpdate()
        {
            if (!_disposed
                && (_state == SequenceState.Casting
                    || _state == SequenceState.WaitingForBite
                    || _state == SequenceState.RetrievingEmptyLine
                    || _state == SequenceState.Hooked)
                && _cast != null)
                _cast.RenderLate();
        }

        private void OnGUI()
        {
            if (_disposed) return;
            int previousDepth = GUI.depth;
            GUI.depth = -1000;
            try
            {
                if (_state == SequenceState.Hooked && !_automaticCatch && _qte != null && !IsQtePausedByUi())
                    _overlay.DrawQte(_qte, _lastQteOutcome, Time.unscaledTime < _qteFeedbackUntil);
                else if (_state == SequenceState.WaitingForBite && !IsQtePausedByUi())
                    _overlay.DrawWaiting();
                else if (!string.IsNullOrWhiteSpace(_resultMessage) && Time.unscaledTime < _resultMessageUntil)
                    _overlay.DrawToast(_resultMessage);
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        private void PrepareCastVisual()
        {
            // Prepare once as soon as the city character exists, before a cast.
            if (!GameManager.IsInitialized || GameManager.isCitySceneBeingUnloaded) return;
            var character = GameManager.Instance?.playerController?.Character;
            if (character == null || character.animator == null || !character.animator.isInitialized) return;
            if (_preparedCast != null && _preparedCast.CanReuse(character)) return;
            if (Time.unscaledTime < _nextVisualPrepareAt) return;
            _nextVisualPrepareAt = Time.unscaledTime + 1f;
            _preparedCast?.Dispose();
            _preparedCast = null;
            try { _preparedCast = new FishingCastVisual(character, character.transform.position, startImmediately: false); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        private void TryHandleWaterClick()
        {
            bool keyboardCast = Input.GetKeyDown(KeyCode.F);
            if (!Input.GetMouseButtonDown(0) && !keyboardCast) return;
            if (!TryGetReadyPlayer(out PlayerController player, out string reason))
            {
                LogRefusedClick(reason, player);
                return;
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null && eventSystem.IsPointerOverGameObject()) { LogRefusedClick("pointer_over_ui",player); return; }
            if (MouseController.currentTargetEntity != null && MouseController.currentTargetEntity.primaryInteractionEnabled)
            {
                var entity = MouseController.currentTargetEntity;
                LogRefusedClick("native_interaction type=" + entity.GetType().Name + " object=" + entity.name,player);
                return;
            }

            Camera camera = GameManager.GetMainCamera();
            if (camera == null) { LogRefusedClick("no_camera",player); return; }
            if (_preparedCast == null || !_preparedCast.CanReuse(player.Character))
            { LogRefusedClick("visual_preparing", player); return; }
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            bool foundWater = _waterDetector.TryGetWaterPoint(ray, player.Character.transform, out Vector3 waterPoint);
            if (!foundWater)
            {
                var blocker = _waterDetector.LastBlockingCollider;
                string details = _waterDetector.LastFailureReason + " target=" + Format(_waterDetector.LastCandidatePoint);
                if (blocker != null)
                {
                    string path = blocker.name;
                    for (Transform parent = blocker.transform.parent; parent != null; parent = parent.parent)
                        path = parent.name + "/" + path;
                    details += " blocker=" + path + " type=" + blocker.GetType().Name
                        + " layer=" + LayerMask.LayerToName(blocker.gameObject.layer)
                        + " boundsCenter=" + Format(blocker.bounds.center) + " boundsSize=" + Format(blocker.bounds.size);
                }
                LogRefusedClick(details,player);
                if(_waterDetector.LastFailureReason=="solid_occlusion" || _waterDetector.LastFailureReason=="land_before_water") ShowResult(FishingText.ForceCastHint);
                else if(keyboardCast) ShowResult(FishingText.NoWaterTarget);
                return;
            }

            if (!_waterDetector.IsPlayerNearWater(player.Character.transform.position, out float shoreDistance))
            {
                LogRefusedClick("too_far_from_water distance=" + shoreDistance.ToString("0.0")
                    + "m limit=" + FishingWaterDetector.MaxFishingDistance.ToString("0") + "m", player);
                ShowResult(FishingText.TooFarFromWater(shoreDistance));
                return;
            }

            _log("[FishingMod] Static water target " + _waterDetector.LastMatchedZoneId + " at " + Format(waterPoint)
                + (keyboardCast ? " (keyboard cast)." : "."));
            StartCastAtCurrentPosition(player, waterPoint);
        }

        private void LogRefusedClick(string reason, PlayerController player)
        {
            if (Time.unscaledTime < _nextClickDiagnosticAt) return;
            _nextClickDiagnosticAt = Time.unscaledTime + 1f;
            string position = player != null && player.Character != null
                ? Format(player.Character.transform.position) : "unavailable";
            _log("[FishingMod] Click refused: " + reason + "; player=" + position + "; timeScale=" + Time.timeScale + ".");
        }

        private static bool TryGetReadyPlayer(out PlayerController player, out string reason)
        {
            player = null;
            reason = "city_not_ready";
            try
            {
                if (!GameManager.IsInitialized || !BuildingManager.IsInitialized || GameManager.isCitySceneBeingUnloaded)
                    return false;
                GameManager game = GameManager.Instance;
                player = game != null ? game.playerController : null;
                if (BuildingManager.IsInsideBuilding) { reason="inside_building"; return false; }
                if (CityMap.IsOpen || FullMenu.IsOpen || MiniMenu.IsOpen) { reason="menu_open"; return false; }
                if (GameManager.ShouldBlockKeyboardShortcuts()) { reason="native_shortcuts_blocked"; return false; }
                if (GameManager.HasInputSelected()) { reason="text_input_selected"; return false; }
                if (FuneralHelper.PlayerDead) { reason="player_dead"; return false; }
                if (PlayerHelper.IsUsingVehicle) { reason="using_vehicle"; return false; }
                if (PlayerHelper.IsHoldingItem) { reason="holding_item"; return false; }
                if (player == null || player.Character == null) { reason="no_player"; return false; }
                if (player.awaitingRepositioning) { reason="awaiting_repositioning"; return false; }
                if (player.NavigationDisabled) { reason="navigation_disabled"; return false; }
                if (!Application.isFocused) { reason="game_not_focused"; return false; }
                reason = null;
                return true;
            }
            catch (Exception exception)
            {
                player = null;
                reason = "readiness_exception=" + exception.GetType().Name;
                return false;
            }
        }

        private void StartCastAtCurrentPosition(PlayerController player, Vector3 waterPoint)
        {
            _player = player;
            _waterPoint = waterPoint;
            _shorePoint = player.Character.transform.position;
            try
            {
                ThirdPersonCharacter character = _player.Character;
                Vector3 facing = _waterPoint - character.transform.position;
                facing.y = 0f;
                if (facing.sqrMagnitude < 0.01f) facing = character.transform.forward;

                if(character.navmeshAgent!=null && character.navmeshAgent.enabled && character.navmeshAgent.isOnNavMesh)
                    _player.ResetWalkingAnimation();
                else _player.RemoveGoal();
                character.ForceToRotation(Quaternion.LookRotation(facing.normalized, Vector3.up));
                _player.SetNavigationBlocker(NavigationBlocker.EntertainActivity);
                _ownsNavigationBlocker = true;
                PlanBiteAtCastStart();
                _cast = _preparedCast;
                _cast.BeginCast(_waterPoint);
                _audio?.Play(FishingSound.Cast, 0.48f, 1f);
                _state = SequenceState.Casting;
                _log("[FishingMod] Cast started in place at " + Format(_shorePoint) + "; exact target " + Format(_waterPoint) + ".");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                CancelSequence("cast initialization failed");
            }
        }

        private void FinishCast()
        {
            _activityBonusApplied = false;
            try
            {
                _activityBonusApplied = _happiness.ApplyFishingActivity();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _log("[FishingMod] Could not apply the fishing activity happiness modifier.");
            }

            _emptyLineRetrieveElapsed = 0f;
            _emptyLineReelRepeatPlayed = false;
            _lastQteOutcome = FishingQteOutcome.None;
            _qteFeedbackUntil = 0f;
            _cast.AdvanceWaiting(0f, 0f);
            _state = SequenceState.WaitingForBite;
            _log("[FishingMod] Cast completed; "
                + (_pendingFish != null ? _pendingFish.FallbackName + " will bite" : "no fish selected")
                + " in " + _biteTimer.RemainingSeconds.ToString("0.0")
                + " s (planned " + _biteTimer.DurationSeconds.ToString("0.0")
                + " s after water impact). Fishing activity +10/48 h applied=" + _activityBonusApplied + ".");
        }

        private void PlanBiteAtCastStart()
        {
            bool hasFish = FishingBiteRules.HasFish(_random.NextDouble());
            _pendingFish = hasFish ? FishingFishCatalog.Select(_random.NextDouble()) : null;
            float delay = hasFish
                ? FishingBiteRules.BiteDelaySeconds(_random.NextDouble())
                : FishingBiteRules.NoFishWaitSeconds;
            _biteTimer = new FishingBiteTimer(delay);
        }

        private void UpdateWaitingForBite()
        {
            if (_cast == null || !_cast.IsAlive || _player == null || _player.Character == null)
            {
                CancelSequence("character or waiting fishing line disappeared");
                return;
            }

            if (GameManager.isCitySceneBeingUnloaded || FuneralHelper.PlayerDead)
            {
                CancelSequence("player became unavailable while waiting for a bite");
                return;
            }

            if (IsQtePausedByUi()) return;

            float deltaTime = Time.unscaledDeltaTime;
            _cast.AdvanceWaiting(deltaTime, 0f);
            _biteTimer.Advance(deltaTime);
            if (!_biteTimer.IsDue) return;

            if (_pendingFish == null)
            {
                _emptyLineRetrieveElapsed = 0f;
                _emptyLineReelRepeatPlayed = false;
                _audio?.Play(FishingSound.ReelIn, 0.34f, 0.96f);
                _state = SequenceState.RetrievingEmptyLine;
                _log("[FishingMod] No fish selected; empty line retrieval after "
                    + _biteTimer.ElapsedSeconds.ToString("0.00") + " active seconds in water (target 20.00 s).");
                return;
            }

            FishingFish fish = _pendingFish;
            _pendingFish = null;
            _qte = new FishingQteSession(fish, _random, FishingOptions.Difficulty);
            _automaticCatch = !FishingOptions.EnableQte;
            _automaticCatchElapsed = 0f;
            _cast.AdvanceFight(0f, _qte.Progress);
            _state = SequenceState.Hooked;
            _log("[FishingMod] " + fish.FallbackName + " hooked after " + _biteTimer.ElapsedSeconds.ToString("0.00")
                + " active seconds in water (target " + _biteTimer.DurationSeconds.ToString("0.00") + " s; conditional weight "
                + fish.ChanceWeight + "%, initial line progress "
                + (FishingQteSession.InitialProgress * 100f).ToString("0") + "%, "
                + fish.RequiredSuccesses + " configured pulls, +"
                + fish.HappinessBonus + " happiness for 72 h).");
        }

        private void UpdateEmptyLineRetrieval()
        {
            if (_cast == null || !_cast.IsAlive || _player == null || _player.Character == null)
            {
                CancelSequence("character or empty fishing line disappeared");
                return;
            }

            if (GameManager.isCitySceneBeingUnloaded || FuneralHelper.PlayerDead)
            {
                CancelSequence("player became unavailable while reeling the line in");
                return;
            }

            if (IsQtePausedByUi()) return;

            float deltaTime = Time.unscaledDeltaTime;
            _emptyLineRetrieveElapsed += deltaTime;
            if (!_emptyLineReelRepeatPlayed && _emptyLineRetrieveElapsed >= EmptyLineRetrievalSeconds * 0.5f)
            {
                _emptyLineReelRepeatPlayed = true;
                _audio?.Play(FishingSound.ReelIn, 0.31f, 1.02f);
            }
            float progress = Mathf.Clamp01(_emptyLineRetrieveElapsed / EmptyLineRetrievalSeconds);
            _cast.AdvanceWaiting(deltaTime, progress);
            if (progress < 1f) return;

            _log("[FishingMod] Empty line retrieved; no fish caught.");
            ReleaseCastResources();
            _state = SequenceState.Idle;
            _player = null;
            ShowResult(FishingText.NoFish);
        }

        private void UpdateQte()
        {
            if (_qte == null || _cast == null || !_cast.IsAlive || _player == null || _player.Character == null)
            {
                CancelSequence("character or fishing QTE disappeared");
                return;
            }

            if (GameManager.isCitySceneBeingUnloaded || FuneralHelper.PlayerDead)
            {
                CancelSequence("player became unavailable during the fishing QTE");
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                string cancelled = FishingText.Cancelled(_qte.Fish);
                CancelSequence("fish released by player");
                ShowResult(cancelled);
                return;
            }

            if (IsQtePausedByUi()) return;

            if (_automaticCatch)
            {
                _automaticCatchElapsed += Time.unscaledDeltaTime;
                _cast.AdvanceFight(Time.unscaledDeltaTime, Mathf.Lerp(FishingQteSession.InitialProgress, 1f,
                    _automaticCatchElapsed / EmptyLineRetrievalSeconds));
                if (_automaticCatchElapsed >= EmptyLineRetrievalSeconds)
                {
                    _qte.CompleteAutomatically();
                    CompleteCatch();
                }
                return;
            }

            FishingQteOutcome outcome = TryReadQteCommand(out FishingQteCommand command)
                ? _qte.Submit(command)
                : _qte.Advance(Time.unscaledDeltaTime);
            _cast.AdvanceFight(Time.unscaledDeltaTime, _qte.Progress);
            if (outcome == FishingQteOutcome.None) return;

            _lastQteOutcome = outcome;
            _qteFeedbackUntil = Time.unscaledTime + 0.55f;
            if (outcome == FishingQteOutcome.Success)
            {
                _audio?.Play(FishingSound.ReelIn, 0.30f, 0.96f + (float)_random.NextDouble() * 0.08f);
                _audio?.Play(FishingSound.QteSuccess, 0.17f, 0.97f + (float)_random.NextDouble() * 0.06f);
                _log("[FishingMod] QTE success; line remaining " + _qte.RemainingLineMeters.ToString("0.0") + " m.");
                return;
            }

            if (outcome == FishingQteOutcome.Failure)
            {
                _audio?.Play(FishingSound.QteFailure, 0.16f, 0.97f + (float)_random.NextDouble() * 0.05f);
                _log("[FishingMod] QTE miss; line released by " + FishingQteSession.FailureMeters.ToString("0.00")
                    + " m, remaining " + _qte.RemainingLineMeters.ToString("0.0") + " m.");
                return;
            }

            if (outcome == FishingQteOutcome.Escaped)
            {
                FishingFish escapedFish = _qte.Fish;
                FishingMoneyResult loss = FishingEconomyService.Settle(_qte, _log);
                string escaped = FishingText.Escaped(escapedFish) + "\n" + FishingText.MoneyResult(loss, caught: false);
                // Clear the terminal session before optional presentation can throw or re-enter.
                ReleaseCastResources();
                _state = SequenceState.Idle;
                _player = null;
                ShowResult(escaped);
                _audio?.Play(FishingSound.LineSnap, 0.58f, 0.94f);
                _log("[FishingMod] " + escapedFish.FallbackName + " escaped after line progress reached 0%.");
                return;
            }

            CompleteCatch();
        }

        private void CompleteCatch()
        {
            if (_qte == null || !_qte.IsComplete) return;
            FishingFish caughtFish = _qte.Fish;
            FishingMoneyResult sale = FishingEconomyService.Settle(_qte, _log);
            ReleaseCastResources();
            _state = SequenceState.Idle;
            _player = null;
            FishingCatchBonusResult bonus;
            try
            {
                bonus = _happiness.ApplyCatch(caughtFish);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _log("[FishingMod] Could not apply the caught-fish happiness modifier.");
                bonus = new FishingCatchBonusResult(caughtFish, caughtFish, happinessEnabled: false);
            }

            string result = FishingText.Caught(bonus) + "\n" + FishingText.MoneyResult(sale, caught: true);
            ShowResult(result);
            _audio?.Play(FishingSound.FishLanded, 0.64f, 0.98f + (float)_random.NextDouble() * 0.04f);
            _log("[FishingMod] Caught " + caughtFish.FallbackName + "; active catch bonus "
                + bonus.CountedFish.FallbackName + " +" + bonus.CountedFish.HappinessBonus + "/72 h.");
        }

        private static bool TryReadQteCommand(out FishingQteCommand command)
        {
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Z))
            {
                command = FishingQteCommand.Up;
                return true;
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.Q))
            {
                command = FishingQteCommand.Left;
                return true;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            {
                command = FishingQteCommand.Down;
                return true;
            }
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            {
                command = FishingQteCommand.Right;
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Space))
            {
                command = FishingQteCommand.Reel;
                return true;
            }

            command = default;
            return false;
        }

        private bool IsQtePausedByUi()
        {
            bool uiBlocked = !Application.isFocused || CityMap.IsOpen || FullMenu.IsOpen || MiniMenu.IsOpen;
            return FishingInputRules.PausesClock(uiBlocked, Time.timeScale <= 0f, _state == SequenceState.Hooked && !_automaticCatch);
        }

        private bool TryCancelPendingActivity()
        {
            bool menuOpen = CityMap.IsOpen || FullMenu.IsOpen || MiniMenu.IsOpen;
            bool movement = false;
            if (Application.isFocused && !menuOpen && !GameManager.HasInputSelected())
            {
                bool useLegacyKeys = true;
                try
                {
                    if (InputHelper.IsInitialized())
                    {
                        movement = PlayerAction.Move.Vector().sqrMagnitude > 0.01f || PlayerAction.AutoRun.Pressed();
                        useLegacyKeys = false;
                    }
                }
                catch { /* Legacy keyboard fallback also works before native bindings are ready. */ }
                if (useLegacyKeys) movement = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S)
                    || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.Q)
                    || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.LeftArrow)
                    || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.RightArrow);
            }
            // Right-click controls the camera and must not cancel fishing.
            bool clicked = Input.GetMouseButtonDown(0);
            bool displaced = _player != null && (_player.transform.position - _shorePoint).sqrMagnitude > 0.25f;
            if (!FishingInputRules.CancelsWaiting(Application.isFocused, GameManager.HasInputSelected(), menuOpen,
                    clicked, movement, Input.GetKeyDown(KeyCode.Escape), displaced)) return false;

            PlayerController player = _player;
            CancelSequence("cancelled by click, movement or Escape before the fight");
            ShowResult(FishingText.WaitCancelled);
            // We run after vanilla input, which could not navigate while our blocker was
            // held. Replay only the ordinary ground destination so the cancelling click works.
            if (Input.GetMouseButtonDown(0) && !menuOpen && player != null
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                && (MouseController.currentTargetEntity == null || !MouseController.currentTargetEntity.primaryInteractionEnabled))
            {
                Camera camera = GameManager.GetMainCamera();
                if (camera != null && Physics.Raycast(camera.ScreenPointToRay(Input.mousePosition),
                        out RaycastHit hit, 800f, LayerHelper.mouseGroundMask))
                    player.SetNewDestination(hit.point, showParticleEffect: true);
            }
            return true;
        }

        private void ShowResult(string message)
        {
            _resultMessage = message;
            _resultMessageUntil = Time.unscaledTime + 6f;
        }

        private void CancelSequence(string reason)
        {
            ReleaseCastResources();
            if (_state != SequenceState.Idle) _log("[FishingMod] Sequence stopped: " + reason + ".");
            _state = SequenceState.Idle;
            _player = null;
        }

        private void ReleaseCastResources()
        {
            // Keep native input blocked until the terminal QTE key is released,
            // including a full neutral frame for native key-up processing.
            bool deferInputRelease = _state == SequenceState.Hooked && !_disposed;
            if (_cast != null)
            {
                _cast.EndCast();
                _cast = null;
            }

            _qte = null;
            _pendingFish = null;
            _biteTimer = null;
            _emptyLineRetrieveElapsed = 0f;
            _emptyLineReelRepeatPlayed = false;
            _activityBonusApplied = false;

            if (_ownsNavigationBlocker && _player != null)
            {
                if (deferInputRelease) _pendingInputPlayer = _player;
                else
                {
                    try { _player.UnsetNavigationBlocker(NavigationBlocker.EntertainActivity); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
            }

            _ownsNavigationBlocker = false;
            if (deferInputRelease)
            {
                _waitingForInputRelease = true;
                _lastQteInputFrame = Time.frameCount;
            }
        }

        private static bool QteKeysHeld()
        {
            return Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow)
                || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow)
                || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S)
                || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.Q)
                || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Escape);
        }

        private static bool OwnsQteInput => _inputOwner != null && !_inputOwner._disposed
            && (_inputOwner._state == SequenceState.Hooked || _inputOwner._waitingForInputRelease);

        private static bool BlockQteShortcuts(ref bool __result)
        {
            if (!OwnsQteInput) return true;
            __result = true;
            return false;
        }

        private static bool BlockQteSpacePause()
        {
            return !OwnsQteInput || !(Input.GetKey(KeyCode.Space) || Input.GetKeyUp(KeyCode.Space));
        }

        private void ReleasePendingInput()
        {
            try
            {
                if (_pendingInputPlayer != null)
                    _pendingInputPlayer.UnsetNavigationBlocker(NavigationBlocker.EntertainActivity);
            }
            catch (Exception exception) { Debug.LogException(exception); }
            _pendingInputPlayer = null;
            _waitingForInputRelease = false;
        }

        internal void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelSequence("mod unloaded");
            _preparedCast?.Dispose();
            _preparedCast = null;
            ReleasePendingInput();
            _inputHarmony?.UnpatchAll(_inputHarmony.Id);
            _inputHarmony = null;
            if (_inputOwner == this) _inputOwner = null;
            _waterDetector.CancelIndexing();
            _overlay.Dispose();
            _audio?.Dispose();
            _audio = null;
            _disposed = true;
            _log = null;
        }

        private void OnDestroy()
        {
            Dispose();
        }

        private static string Format(Vector3 value)
        {
            return "(" + value.x.ToString("0.0") + ", " + value.y.ToString("0.0") + ", " + value.z.ToString("0.0") + ")";
        }
    }
}
