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
        private enum SequenceState
        {
            Idle,
            Walking,
            Casting,
            Hooked
        }

        private readonly FishingWaterDetector _waterDetector = new FishingWaterDetector();
        private readonly FishingShoreResolver _shoreResolver = new FishingShoreResolver();
        private readonly FishingHappinessService _happiness = new FishingHappinessService();
        private readonly FishingQteOverlay _overlay = new FishingQteOverlay();
        private readonly System.Random _random = new System.Random(Guid.NewGuid().GetHashCode());

        private Action<string> _log;
        private SequenceState _state;
        private PlayerController _player;
        private FishingCastVisual _cast;
        private Vector3 _waterPoint;
        private Vector3 _shorePoint;
        private bool _ownsNavigationBlocker;
        private bool _disposed;
        private float _walkStartedAt;
        private FishingQteSession _qte;
        private FishingQteOutcome _lastQteOutcome;
        private float _qteFeedbackUntil;
        private string _resultMessage;
        private float _resultMessageUntil;

        internal void Initialize(Action<string> log)
        {
            _log = log ?? (_ => { });
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
                if (_cast.IsComplete) FinishCast();
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
            if (!_disposed && (_state == SequenceState.Casting || _state == SequenceState.Hooked) && _cast != null)
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
                _cast = new FishingCastVisual(character, _waterPoint);
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
            bool activityBonusApplied = false;
            try
            {
                activityBonusApplied = _happiness.ApplyFishingActivity();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _log("[FishingMod] Could not apply the fishing activity happiness modifier.");
            }

            FishingFish fish = FishingFishCatalog.Select(_random.NextDouble());
            _qte = new FishingQteSession(fish, _random);
            _lastQteOutcome = FishingQteOutcome.None;
            _qteFeedbackUntil = 0f;
            _cast.AdvanceFight(0f, 0f);
            _state = SequenceState.Hooked;
            _log("[FishingMod] Cast completed; " + fish.FallbackName + " hooked (weight "
                + fish.ChanceWeight + "%, " + fish.RequiredSuccesses + " clean pulls, +"
                + fish.HappinessBonus + " happiness for 72 h). Fishing activity +10/48 h applied="
                + activityBonusApplied + ".");
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
                _log("[FishingMod] QTE success; line remaining " + _qte.RemainingLineMeters.ToString("0.0") + " m.");
                return;
            }

            if (outcome == FishingQteOutcome.Failure)
            {
                _log("[FishingMod] QTE miss; line released by " + FishingQteSession.FailureMeters.ToString("0.00")
                    + " m, remaining " + _qte.RemainingLineMeters.ToString("0.0") + " m.");
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
