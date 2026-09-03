using System;
using Helpers;
using UI.MiniMenu;
using UI.Smartphone;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace FishingMod
{
    [DefaultExecutionOrder(10000)]
    public sealed class FishingRuntime : MonoBehaviour
    {
        private const float EmptyLineRetrievalSeconds = 1.6f;

        private enum SequenceState
        {
            Idle,
            Walking,
            Casting,
            WaitingForBite,
            RetrievingEmptyLine,
            Hooked
        }

        private readonly FishingWaterDetector _waterDetector = new FishingWaterDetector();
        private readonly FishingShoreResolver _shoreResolver = new FishingShoreResolver();
        private readonly FishingHappinessService _happiness = new FishingHappinessService();
        private readonly FishingQteOverlay _overlay = new FishingQteOverlay();
        private readonly System.Random _random = new System.Random(Guid.NewGuid().GetHashCode());

        private Action<string> _log;
        private FishingAudio _audio;
        private SequenceState _state;
        private PlayerController _player;
        private FishingCastVisual _cast;
        private Vector3 _waterPoint;
        private Vector3 _shorePoint;
        private bool _ownsNavigationBlocker;
        private bool _disposed;
        private float _walkStartedAt;
        private FishingFish _pendingFish;
        private float _biteWaitRemaining;
        private float _emptyLineRetrieveElapsed;
        private bool _emptyLineReelRepeatPlayed;
        private bool _activityBonusApplied;
        private FishingQteSession _qte;
        private FishingQteOutcome _lastQteOutcome;
        private float _qteFeedbackUntil;
        private string _resultMessage;
        private float _resultMessageUntil;

        internal void Initialize(string modRootPath, Action<string> log)
        {
            _log = log ?? (_ => { });
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
            _log("[FishingMod] Cached " + _waterDetector.IndexedTileCount
                + " local water tile(s) across " + _waterDetector.SurfaceCount
                + " height group(s) for scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle + ".");
        }

        private void Update()
        {
            if (_disposed) return;

            if (_state == SequenceState.Casting)
            {
                if (_cast == null || !_cast.IsAlive)
                {
                    CancelSequence("character or cast visual disappeared");
                    return;
                }

                _cast.Advance(Time.deltaTime);
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

            if (_state == SequenceState.Walking)
            {
                if (_player != null && _player.Character != null &&
                    (_player.transform.position - _shorePoint).sqrMagnitude <= 0.0625f)
                {
                    OnShoreReached();
                    return;
                }

                if (_player == null || !_player.hasOnGoalReachedAction)
                {
                    CancelSequence("shore movement was cancelled");
                    return;
                }

                if (Time.unscaledTime - _walkStartedAt > 120f)
                {
                    CancelSequence("shore movement timed out");
                    return;
                }
            }

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
                if (_state == SequenceState.Hooked && _qte != null && !IsQtePausedByUi())
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

        private void TryHandleWaterClick()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (!TryGetReadyPlayer(out PlayerController player)) return;

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null && eventSystem.IsPointerOverGameObject()) return;
            if (MouseController.currentTargetEntity != null && MouseController.currentTargetEntity.primaryInteractionEnabled) return;

            Camera camera = GameManager.GetMainCamera();
            if (camera == null) return;
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!_waterDetector.TryGetWaterPoint(ray, player.Character.transform, out Vector3 waterPoint)) return;

            if (!_shoreResolver.TryFindClosestReachable(
                    player.Character.navmeshAgent,
                    player.Character.transform.position,
                    waterPoint,
                    out Vector3 shorePoint,
                    out float pathLength))
            {
                Debug.LogWarning("[FishingMod] Water clicked, but no complete shoreline route was found.");
                return;
            }

            if (_state == SequenceState.Walking) CancelSequence("replaced by a new water click");
            StartWalking(player, waterPoint, shorePoint, pathLength);
        }

        private static bool TryGetReadyPlayer(out PlayerController player)
        {
            player = null;
            try
            {
                if (!GameManager.IsInitialized || !BuildingManager.IsInitialized || GameManager.isCitySceneBeingUnloaded)
                    return false;
                if (BuildingManager.IsInsideBuilding || CityMap.IsOpen || FullMenu.IsOpen || MiniMenu.IsOpen)
                    return false;
                if (GameManager.ShouldBlockKeyboardShortcuts() || GameManager.HasInputSelected())
                    return false;
                if (PlayerHelper.playerDead || PlayerHelper.IsUsingVehicle || PlayerHelper.IsHoldingItem)
                    return false;

                GameManager game = GameManager.Instance;
                player = game != null ? game.playerController : null;
                if (player == null || player.Character == null || player.awaitingRepositioning || player.NavigationDisabled)
                    return false;
                if (!player.IsOnNavmesh() || !Application.isFocused) return false;
                return true;
            }
            catch
            {
                player = null;
                return false;
            }
        }

        private void StartWalking(PlayerController player, Vector3 waterPoint, Vector3 shorePoint, float pathLength)
        {
            _player = player;
            _waterPoint = waterPoint;
            _shorePoint = shorePoint;
            _state = SequenceState.Walking;
            _walkStartedAt = Time.unscaledTime;

            UnityAction onReached = OnShoreReached;
            player.SetGoal(shorePoint, onReached);
            if (!player.hasOnGoalReachedAction)
            {
                CancelSequence("native player navigation rejected the shoreline goal");
                return;
            }

            _log("[FishingMod] Water click accepted; shoreline "
                + Vector3.Distance(player.transform.position, shorePoint).ToString("0.0")
                + " m away, route " + pathLength.ToString("0.0") + " m.");
        }

        private void OnShoreReached()
        {
            if (_disposed || _state != SequenceState.Walking || _player == null || _player.Character == null)
                return;

            try
            {
                ThirdPersonCharacter character = _player.Character;
                Vector3 facing = _waterPoint - character.transform.position;
                facing.y = 0f;
                if (facing.sqrMagnitude < 0.01f) facing = character.transform.forward;

                _player.ResetWalkingAnimation();
                character.ForceToRotation(Quaternion.LookRotation(facing.normalized, Vector3.up));
                _player.SetNavigationBlocker(NavigationBlocker.EntertainActivity);
                _ownsNavigationBlocker = true;
                PlanBiteAtCastStart();
                _cast = new FishingCastVisual(character, _waterPoint);
                _audio?.Play(FishingSound.Cast, 0.48f, 1f);
                _state = SequenceState.Casting;
                _log("[FishingMod] Shore reached at " + Format(_shorePoint) + "; long cast started toward " + Format(_waterPoint) + ".");
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
                + " after " + _biteWaitRemaining.ToString("0.0")
                + " s. Fishing activity +10/48 h applied=" + _activityBonusApplied + ".");
        }

        private void PlanBiteAtCastStart()
        {
            bool hasFish = FishingBiteRules.HasFish(_random.NextDouble());
            _pendingFish = hasFish ? FishingFishCatalog.Select(_random.NextDouble()) : null;
            _biteWaitRemaining = hasFish
                ? FishingBiteRules.BiteDelaySeconds(_random.NextDouble())
                : FishingBiteRules.NoFishWaitSeconds;
        }

        private void UpdateWaitingForBite()
        {
            if (_cast == null || !_cast.IsAlive || _player == null || _player.Character == null)
            {
                CancelSequence("character or waiting fishing line disappeared");
                return;
            }

            if (GameManager.isCitySceneBeingUnloaded || PlayerHelper.playerDead)
            {
                CancelSequence("player became unavailable while waiting for a bite");
                return;
            }

            if (IsQtePausedByUi()) return;

            float deltaTime = Time.unscaledDeltaTime;
            _cast.AdvanceWaiting(deltaTime, 0f);
            _biteWaitRemaining -= deltaTime;
            if (_biteWaitRemaining > 0f) return;

            if (_pendingFish == null)
            {
                _emptyLineRetrieveElapsed = 0f;
                _emptyLineReelRepeatPlayed = false;
                _audio?.Play(FishingSound.ReelIn, 0.34f, 0.96f);
                _state = SequenceState.RetrievingEmptyLine;
                _log("[FishingMod] No fish bit after 20.0 s; reeling the empty line in.");
                return;
            }

            FishingFish fish = _pendingFish;
            _pendingFish = null;
            _qte = new FishingQteSession(fish, _random);
            _cast.AdvanceFight(0f, _qte.Progress);
            _state = SequenceState.Hooked;
            _log("[FishingMod] " + fish.FallbackName + " hooked (conditional weight "
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

            if (GameManager.isCitySceneBeingUnloaded || PlayerHelper.playerDead)
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

            if (GameManager.isCitySceneBeingUnloaded || PlayerHelper.playerDead)
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
                _audio?.Play(FishingSound.LineSnap, 0.58f, 0.94f);
                _log("[FishingMod] " + escapedFish.FallbackName + " escaped after line progress reached 0%.");
                string escaped = FishingText.Escaped(escapedFish);
                ReleaseCastResources();
                _state = SequenceState.Idle;
                _player = null;
                ShowResult(escaped);
                return;
            }

            CompleteCatch();
        }

        private void CompleteCatch()
        {
            FishingFish caughtFish = _qte.Fish;
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

            string result = FishingText.Caught(bonus);
            _audio?.Play(FishingSound.FishLanded, 0.64f, 0.98f + (float)_random.NextDouble() * 0.04f);
            _log("[FishingMod] Caught " + caughtFish.FallbackName + "; active catch bonus "
                + bonus.CountedFish.FallbackName + " +" + bonus.CountedFish.HappinessBonus + "/72 h.");
            ReleaseCastResources();
            _state = SequenceState.Idle;
            _player = null;
            ShowResult(result);
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

        private static bool IsQtePausedByUi()
        {
            return !Application.isFocused || CityMap.IsOpen || FullMenu.IsOpen || MiniMenu.IsOpen;
        }

        private void ShowResult(string message)
        {
            _resultMessage = message;
            _resultMessageUntil = Time.unscaledTime + 6f;
        }

        private void CancelSequence(string reason)
        {
            if (_state == SequenceState.Walking && _player != null)
                _player.RemoveGoal();
            ReleaseCastResources();
            if (_state != SequenceState.Idle) _log("[FishingMod] Sequence stopped: " + reason + ".");
            _state = SequenceState.Idle;
            _player = null;
        }

        private void ReleaseCastResources()
        {
            if (_cast != null)
            {
                _cast.Dispose();
                _cast = null;
            }

            _qte = null;
            _pendingFish = null;
            _biteWaitRemaining = 0f;
            _emptyLineRetrieveElapsed = 0f;
            _emptyLineReelRepeatPlayed = false;
            _activityBonusApplied = false;

            if (_ownsNavigationBlocker && _player != null)
            {
                try { _player.UnsetNavigationBlocker(NavigationBlocker.EntertainActivity); }
                catch (Exception exception) { Debug.LogException(exception); }
            }

            _ownsNavigationBlocker = false;
        }

        internal void Dispose()
        {
            if (_disposed) return;
            CancelSequence("mod unloaded");
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
