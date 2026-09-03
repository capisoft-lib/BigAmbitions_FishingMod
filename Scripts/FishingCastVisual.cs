using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingMod
{
    internal sealed class FishingCastVisual : IDisposable
    {
        private const int RodPointCount = 12;
        private const int FishingLinePointCount = 18;
        private const int RipplePointCount = 40;
        private const float RodBaseWidth = 0.025f;
        private const float RodTipWidth = 0.006f;

        private readonly ThirdPersonCharacter _character;
        private readonly Transform _root;
        private readonly Transform _leftHandTarget;
        private readonly Transform _rightHandTarget;
        private readonly Transform _headTarget;
        private readonly Transform _handle;
        private readonly Transform _reel;
        private readonly Transform _bobber;
        private readonly LineRenderer _rod;
        private readonly LineRenderer _fishingLine;
        private readonly LineRenderer _ripple;
        private readonly List<Material> _materials = new List<Material>();
        private readonly Quaternion _leftHandStartRotation;
        private readonly Quaternion _rightHandStartRotation;
        private readonly Vector3 _initialForward;
        private readonly Vector3 _waterDirection;
        private readonly Vector3 _landingPoint;
        private readonly Transform _chest;
        private readonly Transform _hips;
        private readonly float _scale;

        private CastPose _pose;
        private Vector3 _rodDirection;
        private Vector3 _launchPoint;
        private bool _launchCaptured;
        private bool _disposed;
        private float _elapsed;
        private float _fightElapsed;
        private float _retrieveProgress;
        private bool _fightActive;

        internal FishingCastVisual(ThirdPersonCharacter character, Vector3 waterPoint)
        {
            _character = character ?? throw new ArgumentNullException(nameof(character));
            _initialForward = HorizontalDirection(character.transform.forward, Vector3.forward);

            Vector3 direction = waterPoint - character.transform.position;
            direction.y = 0f;
            _waterDirection = HorizontalDirection(direction, _initialForward);

            float waterDistance = direction.magnitude;
            float castDistance = Mathf.Min(28f, Mathf.Max(3f, waterDistance - 0.25f));
            _landingPoint = character.transform.position + _waterDirection * castDistance;
            _landingPoint.y = waterPoint.y + 0.06f;

            Vector3 chestPosition = character.upperChest != null
                ? character.upperChest.position
                : character.transform.position + Vector3.up * 1.25f;
            float measuredArm = character.rightHand != null
                ? Vector3.Distance(chestPosition, character.rightHand.position)
                : 0.55f;
            _scale = Mathf.Clamp(measuredArm / 0.55f, 0.78f, 1.35f);

            Animator animator = character.animator;
            if (animator != null && animator.isHuman)
            {
                _chest = animator.GetBoneTransform(HumanBodyBones.Chest)
                    ?? animator.GetBoneTransform(HumanBodyBones.UpperChest)
                    ?? character.upperChest;
                _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            }
            else
            {
                _chest = character.upperChest;
            }

            _leftHandStartRotation = character.leftHand != null ? character.leftHand.rotation : Quaternion.identity;
            _rightHandStartRotation = character.rightHand != null ? character.rightHand.rotation : Quaternion.identity;

            GameObject rootObject = new GameObject("FishingMod_CastVisual");
            _root = rootObject.transform;

            _leftHandTarget = NewTarget("LeftHandTarget");
            _rightHandTarget = NewTarget("RightHandTarget");
            _headTarget = NewTarget("HeadTarget");

            Material rodMaterial = CreateMaterial(new Color(0.08f, 0.13f, 0.08f, 1f));
            Material handleMaterial = CreateMaterial(new Color(0.24f, 0.095f, 0.035f, 1f));
            Material metalMaterial = CreateMaterial(new Color(0.58f, 0.63f, 0.68f, 1f));
            Material lineMaterial = CreateMaterial(new Color(0.86f, 0.93f, 0.96f, 0.86f));
            Material redMaterial = CreateMaterial(new Color(0.93f, 0.08f, 0.055f, 1f));
            Material whiteMaterial = CreateMaterial(new Color(0.96f, 0.96f, 0.91f, 1f));
            Material rippleMaterial = CreateMaterial(new Color(0.38f, 0.82f, 1f, 0.72f));

            _rod = CreateLine("Rod", rodMaterial, RodPointCount, RodBaseWidth, RodTipWidth);
            ConfigureRodWidth(_rod, _scale);
            _handle = CreatePrimitive("Handle", PrimitiveType.Cylinder, handleMaterial).transform;
            _reel = CreatePrimitive("Reel", PrimitiveType.Cylinder, metalMaterial).transform;

            GameObject bobberRoot = new GameObject("Bobber");
            bobberRoot.transform.SetParent(_root, false);
            bobberRoot.layer = 2;
            _bobber = bobberRoot.transform;
            Transform red = CreatePrimitive("BobberRed", PrimitiveType.Sphere, redMaterial).transform;
            red.SetParent(_bobber, false);
            red.localPosition = Vector3.down * 0.035f;
            red.localScale = new Vector3(0.12f, 0.11f, 0.12f) * _scale;
            Transform white = CreatePrimitive("BobberWhite", PrimitiveType.Sphere, whiteMaterial).transform;
            white.SetParent(_bobber, false);
            white.localPosition = Vector3.up * 0.035f;
            white.localScale = new Vector3(0.09f, 0.08f, 0.09f) * _scale;

            _fishingLine = CreateLine("FishingLine", lineMaterial, FishingLinePointCount, 0.011f, 0.007f);
            _fishingLine.enabled = false;
            _ripple = CreateLine("SplashRipple", rippleMaterial, RipplePointCount, 0.025f, 0.01f);
            _ripple.loop = true;
            _ripple.enabled = false;

            _bobber.gameObject.SetActive(false);
            character.SetHandIKTargets(_leftHandTarget, _rightHandTarget, smooth: true);
            character.SetHeadIKTarget(_headTarget, smooth: true);
            Advance(0f);
        }

        internal bool IsAlive => !_disposed && _character != null;
        internal bool IsComplete => _elapsed >= FishingMath.SequenceDuration;

        internal void AdvanceFight(float deltaTime, float retrieveProgress)
        {
            if (!IsAlive) return;
            _fightActive = true;
            _fightElapsed += Mathf.Max(0f, deltaTime);
            _retrieveProgress = Mathf.Clamp01(retrieveProgress);
        }

        internal void Advance(float deltaTime)
        {
            if (!IsAlive) return;
            _elapsed = Mathf.Min(FishingMath.SequenceDuration, _elapsed + Mathf.Max(0f, deltaTime));
            _pose = EvaluatePose(_elapsed);

            Vector3 up = _character.transform.up;
            Vector3 forward = _waterDirection;
            Vector3 side = Vector3.Cross(up, forward).normalized;
            Vector3 chest = _character.upperChest != null
                ? _character.upperChest.position
                : _character.transform.position + up * (1.25f * _scale);

            Vector3 rightPosition = chest
                + forward * (_pose.HandForward * _scale)
                + up * (_pose.HandUp * _scale)
                + side * (_pose.HandSide * _scale);
            _rodDirection = (forward * _pose.RodForward + up * _pose.RodUp).normalized;
            Vector3 leftPosition = rightPosition - _rodDirection * (0.34f * _scale) - side * (0.055f * _scale);

            Quaternion sweep = Quaternion.FromToRotation(_initialForward, _rodDirection);
            _rightHandTarget.SetPositionAndRotation(rightPosition, sweep * _rightHandStartRotation);
            _leftHandTarget.SetPositionAndRotation(leftPosition, sweep * _leftHandStartRotation);
            _headTarget.position = Vector3.Lerp(_landingPoint, rightPosition + _rodDirection * 2.2f, 0.30f);
        }

        internal void RenderLate()
        {
            if (!IsAlive) return;
            ApplyBodyMotion();

            Vector3 grip = _character.rightHand != null
                ? _character.rightHand.position
                : _rightHandTarget.position;
            Vector3 up = _character.transform.up;
            Vector3 forward = _waterDirection;

            Vector3 handleStart = grip - _rodDirection * (0.38f * _scale);
            Vector3 handleEnd = grip + _rodDirection * (0.12f * _scale);
            SetCylinder(_handle, handleStart, handleEnd, 0.055f * _scale);

            Vector3 reelCenter = grip - _rodDirection * (0.08f * _scale) - up * (0.085f * _scale);
            SetCylinder(_reel, reelCenter - _character.transform.right * (0.065f * _scale),
                reelCenter + _character.transform.right * (0.065f * _scale), 0.085f * _scale);

            float rodLength = 3.05f * _scale;
            float fightFlex = _fightActive ? 0.08f + Mathf.Sin(_fightElapsed * 7f) * 0.035f : 0f;
            Vector3 bendDirection = -forward * ((_pose.Flex * 0.46f + fightFlex) * _scale)
                - up * ((_pose.Flex * 0.12f + fightFlex * 0.35f) * _scale);
            Vector3 rodStart = grip + _rodDirection * (0.08f * _scale);
            Vector3 rodTip = rodStart;
            for (int i = 0; i < RodPointCount; i++)
            {
                float progress = i / (float)(RodPointCount - 1);
                float bend = progress * progress;
                Vector3 position = rodStart + _rodDirection * (rodLength * progress) + bendDirection * bend;
                _rod.SetPosition(i, position);
                if (i == RodPointCount - 1) rodTip = position;
            }

            if (_elapsed < FishingMath.ReleaseTime) return;
            if (!_launchCaptured)
            {
                _launchCaptured = true;
                _launchPoint = rodTip;
                _bobber.gameObject.SetActive(true);
                _fishingLine.enabled = true;
            }

            float flight = FishingMath.Clamp01((_elapsed - FishingMath.ReleaseTime) / FishingMath.FlightDuration);
            Vector3 bobberPosition = Vector3.Lerp(_launchPoint, _landingPoint, flight);
            bobberPosition.y += FishingMath.BallisticHeight(flight, 4.9f * _scale);
            if (flight >= 1f)
            {
                Vector3 reeledPoint = _character.transform.position + _waterDirection * (1.10f * _scale);
                reeledPoint.y = _landingPoint.y;
                bobberPosition = Vector3.Lerp(_landingPoint, reeledPoint, FishingMath.Smooth01(_retrieveProgress));
                bobberPosition.y += Mathf.Sin((_fightActive ? _fightElapsed : _elapsed) * 4f) * 0.025f;
                bobberPosition.y += Mathf.Sin(_retrieveProgress * Mathf.PI) * (0.18f * _scale);
            }
            _bobber.position = bobberPosition;

            UpdateFishingLine(rodTip, bobberPosition, flight);
            UpdateRipple(flight);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_character != null)
            {
                _character.SetHandIKTargets(null, null, smooth: true);
                _character.SetHeadIKTarget(null, smooth: true);
            }

            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            for (int i = 0; i < _materials.Count; i++)
                if (_materials[i] != null) UnityEngine.Object.Destroy(_materials[i]);
            _materials.Clear();
        }

        private void ApplyBodyMotion()
        {
            Vector3 up = _character.transform.up;
            Vector3 right = _character.transform.right;
            if (_hips != null)
                _hips.rotation = Quaternion.AngleAxis(_pose.TorsoYaw * 0.28f, up) * _hips.rotation;
            if (_chest != null)
            {
                _chest.rotation = Quaternion.AngleAxis(_pose.TorsoYaw, up) * _chest.rotation;
                _chest.rotation = Quaternion.AngleAxis(_pose.TorsoPitch, right) * _chest.rotation;
            }
        }

        private void UpdateFishingLine(Vector3 rodTip, Vector3 bobberPosition, float flight)
        {
            float distance = Vector3.Distance(rodTip, bobberPosition);
            float sag = flight < 1f ? Mathf.Lerp(0.08f, 0.26f, flight) : Mathf.Clamp(distance * 0.035f, 0.15f, 0.85f);
            for (int i = 0; i < FishingLinePointCount; i++)
            {
                float progress = i / (float)(FishingLinePointCount - 1);
                Vector3 point = Vector3.Lerp(rodTip, bobberPosition, progress);
                point.y -= Mathf.Sin(progress * Mathf.PI) * sag;
                _fishingLine.SetPosition(i, point);
            }
        }

        private void UpdateRipple(float flight)
        {
            if (flight < 1f)
            {
                _ripple.enabled = false;
                return;
            }

            float splashTime = _elapsed - FishingMath.ReleaseTime - FishingMath.FlightDuration;
            if (splashTime > 0.85f)
            {
                _ripple.enabled = false;
                return;
            }

            _ripple.enabled = true;
            float radius = Mathf.Lerp(0.08f, 0.75f * _scale, FishingMath.Smooth01(splashTime / 0.85f));
            for (int i = 0; i < RipplePointCount; i++)
            {
                float angle = i * Mathf.PI * 2f / RipplePointCount;
                _ripple.SetPosition(i, _landingPoint + new Vector3(Mathf.Cos(angle) * radius, 0.015f, Mathf.Sin(angle) * radius));
            }
        }

        private Transform NewTarget(string name)
        {
            GameObject target = new GameObject(name);
            target.layer = 2;
            target.transform.SetParent(_root, false);
            return target.transform;
        }

        private LineRenderer CreateLine(string name, Material material, int points, float startWidth, float endWidth)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.layer = 2;
            lineObject.transform.SetParent(_root, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points;
            line.startWidth = startWidth;
            line.endWidth = endWidth;
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
            line.alignment = LineAlignment.View;
            line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private GameObject CreatePrimitive(string name, PrimitiveType type, Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.layer = 2;
            primitive.transform.SetParent(_root, false);
            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
            }

            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            return primitive;
        }

        private Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("HDRP/Unlit")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Hidden/InternalErrorShader");
            Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            ApplyMaterialColor(material, color);
            _materials.Add(material);
            return material;
        }

        internal static void ConfigureRodWidth(LineRenderer rod, float scale)
        {
            if (rod == null) throw new ArgumentNullException(nameof(rod));
            scale = Mathf.Clamp(scale, 0.78f, 1.35f);
            rod.widthMultiplier = 1f;
            rod.widthCurve = new AnimationCurve(
                new Keyframe(0f, RodBaseWidth * scale),
                new Keyframe(0.72f, RodBaseWidth * 0.52f * scale),
                new Keyframe(1f, RodTipWidth * scale));
        }

        internal static void ApplyMaterialColor(Material material, Color color)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", color);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private static void SetCylinder(Transform cylinder, Vector3 start, Vector3 end, float radius)
        {
            Vector3 direction = end - start;
            float length = direction.magnitude;
            if (length < 0.0001f) return;
            cylinder.position = (start + end) * 0.5f;
            cylinder.rotation = Quaternion.FromToRotation(Vector3.up, direction / length);
            cylinder.localScale = new Vector3(radius, length * 0.5f, radius);
        }

        private static Vector3 HorizontalDirection(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude < 0.0001f)
            {
                fallback.y = 0f;
                return fallback.sqrMagnitude < 0.0001f ? Vector3.forward : fallback.normalized;
            }

            return value.normalized;
        }

        private static CastPose EvaluatePose(float elapsed)
        {
            CastPose ready = new CastPose(0.24f, -0.18f, 0.22f, 0.18f, 0.98f, 0f, 0f, 0.02f);
            CastPose windUp = new CastPose(-0.34f, 0.24f, 0.26f, -0.72f, 0.69f, -24f, -6f, 0.20f);
            CastPose release = new CastPose(0.64f, 0.04f, 0.16f, 0.91f, 0.42f, 17f, 8f, 0.58f);
            CastPose follow = new CastPose(0.70f, -0.16f, 0.10f, 0.98f, -0.20f, 12f, 10f, 0.12f);
            CastPose settle = new CastPose(0.44f, -0.12f, 0.15f, 0.94f, 0.34f, 3f, 2f, 0.03f);

            if (elapsed < 0.16f) return ready;
            if (elapsed < 0.67f) return CastPose.Lerp(ready, windUp, FishingMath.Segment(elapsed, 0.16f, 0.67f));
            if (elapsed < FishingMath.ReleaseTime)
                return CastPose.Lerp(windUp, release, FishingMath.Segment(elapsed, 0.67f, FishingMath.ReleaseTime));
            if (elapsed < 1.45f) return CastPose.Lerp(release, follow, FishingMath.Segment(elapsed, FishingMath.ReleaseTime, 1.45f));
            return CastPose.Lerp(follow, settle, FishingMath.Segment(elapsed, 1.45f, 2.25f));
        }

        private readonly struct CastPose
        {
            internal CastPose(float handForward, float handUp, float handSide, float rodForward, float rodUp,
                float torsoYaw, float torsoPitch, float flex)
            {
                HandForward = handForward;
                HandUp = handUp;
                HandSide = handSide;
                RodForward = rodForward;
                RodUp = rodUp;
                TorsoYaw = torsoYaw;
                TorsoPitch = torsoPitch;
                Flex = flex;
            }

            internal float HandForward { get; }
            internal float HandUp { get; }
            internal float HandSide { get; }
            internal float RodForward { get; }
            internal float RodUp { get; }
            internal float TorsoYaw { get; }
            internal float TorsoPitch { get; }
            internal float Flex { get; }

            internal static CastPose Lerp(CastPose left, CastPose right, float progress)
            {
                return new CastPose(
                    Mathf.Lerp(left.HandForward, right.HandForward, progress),
                    Mathf.Lerp(left.HandUp, right.HandUp, progress),
                    Mathf.Lerp(left.HandSide, right.HandSide, progress),
                    Mathf.Lerp(left.RodForward, right.RodForward, progress),
                    Mathf.Lerp(left.RodUp, right.RodUp, progress),
                    Mathf.Lerp(left.TorsoYaw, right.TorsoYaw, progress),
                    Mathf.Lerp(left.TorsoPitch, right.TorsoPitch, progress),
                    Mathf.Lerp(left.Flex, right.Flex, progress));
            }
        }
    }
}
