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
        private Quaternion _leftHandStartRotation;
        private Quaternion _rightHandStartRotation;
        private Vector3 _initialForward;
        private Vector3 _waterDirection;
        private Vector3 _landingPoint;
        private readonly Transform _rightUpper, _leftUpper;
        private readonly Transform _rightLower, _leftLower;
        private readonly List<Transform> _posedArms = new List<Transform>();
        private readonly List<Quaternion> _armBefore = new List<Quaternion>();
        private readonly float _rightReach, _leftReach;
        private float _flightDuration;
        private readonly Quaternion _rightGripBasis = Quaternion.identity, _leftGripBasis = Quaternion.identity;
        private readonly Vector3 _rightPalmOffset, _leftPalmOffset;
        private readonly List<Transform> _fingers = new List<Transform>();
        private readonly List<Quaternion> _fingerBefore = new List<Quaternion>();
        private readonly float _scale;

        private CastPose _pose;
        private Vector3 _rodDirection;
        private Vector3 _launchPoint;
        private bool _launchCaptured;
        private bool _disposed;
        private bool _active;
        private readonly Animator _preparedAnimator;
        private readonly Transform _preparedLeftHand, _preparedRightHand;
        private static readonly float[] LineSag = BuildLineSag();
        private static readonly Vector3[] RippleCircle = BuildRippleCircle();
        private float _elapsed;
        private float _fightElapsed;
        private float _retrieveProgress;
        private bool _fightActive;
        private bool _releaseSoundPending;
        private bool _splashSoundPending;

        internal FishingCastVisual(ThirdPersonCharacter character, Vector3 waterPoint, bool startImmediately = true)
        {
            _character = character ?? throw new ArgumentNullException(nameof(character));
            _initialForward = HorizontalDirection(character.transform.forward, Vector3.forward);

            Vector3 direction = waterPoint - character.transform.position;
            direction.y = 0f;
            _waterDirection = HorizontalDirection(direction, _initialForward);

            _landingPoint = FishingCastGeometry.LandingPoint(waterPoint);
            _flightDuration = FishingMath.FlightSeconds(Vector3.Distance(character.transform.position, waterPoint));
            Animator animator = character.animator;
            _preparedAnimator = animator;
            _preparedLeftHand = character.leftHand;
            _preparedRightHand = character.rightHand;
            if (animator != null && animator.isHuman)
            {
                _rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                _leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                Transform rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                Transform leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                _rightLower = rightLower;
                _leftLower = leftLower;
                _rightReach = ArmLength(_rightUpper,rightLower,character.rightHand);
                _leftReach = ArmLength(_leftUpper,leftLower,character.leftHand);
                _rightGripBasis = GripBasis(animator, character.rightHand, HumanBodyBones.RightMiddleProximal,
                    HumanBodyBones.RightIndexProximal, HumanBodyBones.RightLittleProximal);
                _leftGripBasis = GripBasis(animator, character.leftHand, HumanBodyBones.LeftMiddleProximal,
                    HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftLittleProximal);
                _rightPalmOffset = PalmOffset(animator, character.rightHand, HumanBodyBones.RightMiddleProximal);
                _leftPalmOffset = PalmOffset(animator, character.leftHand, HumanBodyBones.LeftMiddleProximal);
                foreach (HumanBodyBones bone in new[]{HumanBodyBones.RightIndexProximal,HumanBodyBones.RightIndexIntermediate,
                    HumanBodyBones.RightIndexDistal,HumanBodyBones.RightMiddleProximal,HumanBodyBones.RightMiddleIntermediate,
                    HumanBodyBones.RightMiddleDistal,HumanBodyBones.RightRingProximal,HumanBodyBones.RightRingIntermediate,
                    HumanBodyBones.RightRingDistal,HumanBodyBones.RightLittleProximal,HumanBodyBones.RightLittleIntermediate,
                    HumanBodyBones.RightLittleDistal,HumanBodyBones.LeftIndexProximal,HumanBodyBones.LeftIndexIntermediate,
                    HumanBodyBones.LeftIndexDistal,HumanBodyBones.LeftMiddleProximal,HumanBodyBones.LeftMiddleIntermediate,
                    HumanBodyBones.LeftMiddleDistal,HumanBodyBones.LeftRingProximal,HumanBodyBones.LeftRingIntermediate,
                    HumanBodyBones.LeftRingDistal,HumanBodyBones.LeftLittleProximal,HumanBodyBones.LeftLittleIntermediate,
                    HumanBodyBones.LeftLittleDistal})
                {
                    Transform finger=animator.GetBoneTransform(bone);
                    if(finger!=null) _fingers.Add(finger);
                }
            }
            _scale = Mathf.Clamp((_rightReach > 0f ? _rightReach/.95f : .6f)/.6f,.78f,1.35f);

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
            _root.gameObject.SetActive(false);
            // Reserve pose snapshots before the first click.
            _fingerBefore.Capacity = _fingers.Count;
            _posedArms.Capacity = 6;
            _armBefore.Capacity = 6;
            if (startImmediately) BeginCast(waterPoint);
        }

        internal bool CanReuse(ThirdPersonCharacter character) => !_disposed && _root != null
            && character != null && character == _character && character.animator == _preparedAnimator
            && character.leftHand == _preparedLeftHand && character.rightHand == _preparedRightHand;

        internal void BeginCast(Vector3 waterPoint)
        {
            if (!CanReuse(_character)) throw new InvalidOperationException("Fishing visual rig changed.");
            EndCast();
            _initialForward = HorizontalDirection(_character.transform.forward, Vector3.forward);
            _waterDirection = HorizontalDirection(waterPoint - _character.transform.position, _initialForward);
            _landingPoint = FishingCastGeometry.LandingPoint(waterPoint);
            _flightDuration = FishingMath.FlightSeconds(Vector3.Distance(_character.transform.position, waterPoint));
            _leftHandStartRotation = _character.leftHand != null ? _character.leftHand.rotation : Quaternion.identity;
            _rightHandStartRotation = _character.rightHand != null ? _character.rightHand.rotation : Quaternion.identity;
            _elapsed = _fightElapsed = _retrieveProgress = 0f;
            _launchCaptured = _fightActive = _releaseSoundPending = _splashSoundPending = false;
            _launchPoint = default;
            _bobber.gameObject.SetActive(false);
            _fishingLine.enabled = _ripple.enabled = false;
            _active = true;
            _root.gameObject.SetActive(true);
            _character.SetHandIKTargets(_leftHandTarget, _rightHandTarget, smooth: true);
            _character.SetHeadIKTarget(_headTarget, smooth: true);
            Advance(0f);
        }

        internal void EndCast()
        {
            if (!_active) return;
            RestoreFingerPose();
            if (_character != null)
            {
                _character.SetHandIKTargets(null, null, smooth: true);
                _character.SetHeadIKTarget(null, smooth: true);
            }
            _active = false;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        internal bool IsAlive => _active && !_disposed && _character != null && _root != null;
        internal float ImpactTime => FishingMath.ReleaseTime + _flightDuration;
        internal float Duration => Mathf.Max(2.5f, ImpactTime + .85f);
        internal Vector3 LandingPoint => _landingPoint;
        internal bool IsComplete => _elapsed >= Duration;
        internal float Elapsed => _elapsed;

        internal void AdvanceFight(float deltaTime, float retrieveProgress)
        {
            if (!IsAlive) return;
            _fightActive = true;
            _fightElapsed += Mathf.Max(0f, deltaTime);
            _retrieveProgress = Mathf.Clamp01(retrieveProgress);
        }

        internal void AdvanceWaiting(float deltaTime, float retrieveProgress)
        {
            if (!IsAlive) return;
            _fightActive = false;
            _fightElapsed += Mathf.Max(0f, deltaTime);
            _retrieveProgress = Mathf.Clamp01(retrieveProgress);
        }

        internal void Advance(float deltaTime)
        {
            if (!IsAlive) return;
            float previousElapsed = _elapsed;
            _elapsed = Mathf.Min(Duration, _elapsed + Mathf.Max(0f, deltaTime));
            if (previousElapsed < FishingMath.ReleaseTime && _elapsed >= FishingMath.ReleaseTime)
                _releaseSoundPending = true;
            float splashTime = ImpactTime;
            if (previousElapsed < splashTime && _elapsed >= splashTime)
                _splashSoundPending = true;
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
            // Fingers wrap across the handle, not along its length. Mirrored
            // finger directions keep each wrist on its own side of the rod.
            FishingCastGeometry.GripFrames(_rodDirection, up, side, out Quaternion rightFrame, out Quaternion leftFrame);
            Quaternion rightRotation = rightFrame * Quaternion.Inverse(_rightGripBasis);
            Quaternion leftRotation = leftFrame * Quaternion.Inverse(_leftGripBasis);
            Vector3 rightPalm = rightRotation * _rightPalmOffset;
            Vector3 leftPalm = leftRotation * _leftPalmOffset;
            rightPosition -= rightPalm;
            Vector3 leftOffset = -_rodDirection * (.20f * _scale) + rightPalm - leftPalm;
            if(_rightUpper != null && _leftUpper != null)
                rightPosition=FishingCastGeometry.ConstrainGrip(rightPosition,leftOffset,_rightUpper.position,
                    _leftUpper.position,_rightReach,_leftReach);
            Vector3 leftPosition=rightPosition+leftOffset;

            Quaternion sweep = Quaternion.FromToRotation(_initialForward, _rodDirection);
            _rightHandTarget.SetPositionAndRotation(rightPosition, _rightUpper != null
                ? rightRotation : sweep * _rightHandStartRotation);
            _leftHandTarget.SetPositionAndRotation(leftPosition, _leftUpper != null
                ? leftRotation : sweep * _leftHandStartRotation);
            _headTarget.position = chest + forward * (3f * _scale) - up * (.30f * _scale);
            // Native ThirdPersonCharacter.Update ran before this late-ordered runtime.
            // Synchronize the actual rig targets now, before animation evaluation.
            var appearance=_character.appearanceSetter;
            if(appearance!=null)
            {
                if(appearance.rightHandIKTarget!=null) appearance.rightHandIKTarget.SetPositionAndRotation(_rightHandTarget.position,_rightHandTarget.rotation);
                if(appearance.leftHandIKTarget!=null) appearance.leftHandIKTarget.SetPositionAndRotation(_leftHandTarget.position,_leftHandTarget.rotation);
                if(appearance.headIKTarget!=null) appearance.headIKTarget.position=_headTarget.position;
            }
        }

        internal bool ConsumeReleaseSoundEvent()
        {
            if (!_releaseSoundPending) return false;
            _releaseSoundPending = false;
            return true;
        }

        internal bool ConsumeSplashSoundEvent()
        {
            if (!_splashSoundPending) return false;
            _splashSoundPending = false;
            return true;
        }

        internal void RenderLate()
        {
            if (!IsAlive) return;
            RestoreFingerPose();

            // Native rig elbow hints and rotation weights differ between avatars.
            // Finish both chains after native animation with explicit outward hints.
            Vector3 armSide = Vector3.Cross(_character.transform.up, _waterDirection).normalized;
            SolveArm(_rightUpper, _rightLower, _character.rightHand, _rightHandTarget, armSide);
            SolveArm(_leftUpper, _leftLower, _character.leftHand, _leftHandTarget, -armSide);

            Vector3 grip = _character.rightHand != null
                ? _character.rightHand.TransformPoint(_rightPalmOffset)
                : _rightHandTarget.position;
            Vector3 up = _character.transform.up;
            Vector3 forward = _waterDirection;

            Vector3 rodDirection=_rodDirection;
            if(_character.leftHand!=null && _character.rightHand!=null)
            {
                Vector3 between=grip-_character.leftHand.TransformPoint(_leftPalmOffset);
                if(between.magnitude>.1f && Vector3.Dot(between.normalized,_rodDirection)>.3f) rodDirection=between.normalized;
            }
            CurlFingers(grip,rodDirection);
            Vector3 handleStart = grip - rodDirection * (0.38f * _scale);
            Vector3 handleEnd = grip + rodDirection * (0.12f * _scale);
            SetCylinder(_handle, handleStart, handleEnd, 0.055f * _scale);

            Vector3 reelCenter = grip - rodDirection * (0.08f * _scale) - up * (0.085f * _scale);
            SetCylinder(_reel, reelCenter - _character.transform.right * (0.065f * _scale),
                reelCenter + _character.transform.right * (0.065f * _scale), 0.085f * _scale);

            float rodLength = 3.05f * _scale;
            float fightFlex = _fightActive ? 0.08f + Mathf.Sin(_fightElapsed * 7f) * 0.035f : 0f;
            Vector3 bendDirection = -forward * ((_pose.Flex * 0.46f + fightFlex) * _scale)
                - up * ((_pose.Flex * 0.12f + fightFlex * 0.35f) * _scale);
            Vector3 rodStart = grip + rodDirection * (0.08f * _scale);
            Vector3 rodTip = rodStart;
            for (int i = 0; i < RodPointCount; i++)
            {
                float progress = i / (float)(RodPointCount - 1);
                float bend = progress * progress;
                Vector3 position = rodStart + rodDirection * (rodLength * progress) + bendDirection * bend;
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

            float flight = FishingMath.Clamp01((_elapsed - FishingMath.ReleaseTime) / _flightDuration);
            Vector3 bobberPosition = FishingCastGeometry.FlightPoint(_launchPoint, _landingPoint, flight, _scale);
            if (flight >= 1f)
            {
                Vector3 reeledPoint = _character.transform.position + _waterDirection * (1.10f * _scale);
                reeledPoint.y = _landingPoint.y;
                bobberPosition = Vector3.Lerp(_landingPoint, reeledPoint, FishingMath.Smooth01(_retrieveProgress));
                bobberPosition.y += Mathf.Sin(_fightElapsed * 4f) * 0.025f;
                bobberPosition.y += Mathf.Sin(_retrieveProgress * Mathf.PI) * (0.18f * _scale);
            }
            _bobber.position = bobberPosition;

            UpdateFishingLine(rodTip, bobberPosition, flight);
            UpdateRipple(flight);
        }

        public void Dispose()
        {
            if (_disposed) return;
            EndCast();
            _disposed = true;

            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            for (int i = 0; i < _materials.Count; i++)
                if (_materials[i] != null) UnityEngine.Object.Destroy(_materials[i]);
            _materials.Clear();
        }

        private static float ArmLength(Transform upper, Transform lower, Transform hand)
            => upper != null && lower != null && hand != null
                ? (Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,hand.position))*.95f : .57f;

        private static Quaternion GripBasis(Animator animator, Transform hand, HumanBodyBones middle,
            HumanBodyBones index, HumanBodyBones little)
        {
            Transform m=animator.GetBoneTransform(middle),i=animator.GetBoneTransform(index),l=animator.GetBoneTransform(little);
            if(hand==null || m==null || i==null || l==null) return Quaternion.identity;
            Vector3 forward=hand.InverseTransformDirection(m.position-hand.position);
            Vector3 normal=hand.InverseTransformDirection(Vector3.Cross(i.position-l.position,m.position-hand.position));
            return forward.sqrMagnitude>.000001f && normal.sqrMagnitude>.00000001f
                ? Quaternion.LookRotation(forward,normal) : Quaternion.identity;
        }

        private static Vector3 PalmOffset(Animator animator, Transform hand, HumanBodyBones middle)
        {
            Transform knuckle = animator.GetBoneTransform(middle);
            return hand != null && knuckle != null
                ? hand.InverseTransformPoint(Vector3.Lerp(hand.position, knuckle.position, .8f))
                : Vector3.zero;
        }

        internal void RestoreFingerPose()
        {
            for(int i=0;i<_fingerBefore.Count;i++) if(_fingers[i]!=null) _fingers[i].localRotation=_fingerBefore[i];
            _fingerBefore.Clear();
            for(int i=0;i<_armBefore.Count;i++) if(_posedArms[i]!=null) _posedArms[i].localRotation=_armBefore[i];
            _armBefore.Clear();
            _posedArms.Clear();
        }

        private void SolveArm(Transform upper, Transform lower, Transform hand, Transform target, Vector3 outward)
        {
            if (upper == null || lower == null || hand == null) return;
            foreach (Transform bone in new[] { upper, lower, hand })
            {
                _posedArms.Add(bone);
                _armBefore.Add(bone.localRotation);
            }
            Vector3 shoulder = upper.position;
            float upperLength = Vector3.Distance(shoulder, lower.position);
            float lowerLength = Vector3.Distance(lower.position, hand.position);
            Vector3 elbow = FishingCastGeometry.ElbowPosition(shoulder, target.position,
                outward - _character.transform.up * .65f, upperLength, lowerLength);
            upper.rotation = Quaternion.FromToRotation(lower.position - shoulder, elbow - shoulder) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position,
                target.position - lower.position) * lower.rotation;
            hand.rotation = target.rotation;
        }

        private void CurlFingers(Vector3 grip, Vector3 rodDirection)
        {
            foreach(var finger in _fingers) _fingerBefore.Add(finger!=null ? finger.localRotation : Quaternion.identity);
            float amount=FishingMath.Segment(_elapsed,0f,.3f);
            foreach(var finger in _fingers)
            {
                if(finger==null || finger.childCount==0) continue;
                Vector3 along=finger.GetChild(0).position-finger.position;
                Vector3 closest=grip+rodDirection*Vector3.Dot(finger.position-grip,rodDirection);
                Vector3 inward=closest-finger.position;
                Vector3 axis=Vector3.Cross(along,inward);
                if(axis.sqrMagnitude>.00000001f)
                {
                    float angle = Mathf.Min(65f, Vector3.Angle(along, inward));
                    finger.rotation=Quaternion.AngleAxis(angle*amount,axis.normalized)*finger.rotation;
                }
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
                point.y -= LineSag[i] * sag;
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

            float splashTime = _elapsed - ImpactTime;
            if (splashTime > 0.85f)
            {
                _ripple.enabled = false;
                return;
            }

            _ripple.enabled = true;
            float radius = Mathf.Lerp(0.08f, 0.75f * _scale, FishingMath.Smooth01(splashTime / 0.85f));
            for (int i = 0; i < RipplePointCount; i++)
            {
                _ripple.SetPosition(i, _landingPoint + RippleCircle[i] * radius + Vector3.up * .015f);
            }
        }

        private static float[] BuildLineSag()
        {
            var values = new float[FishingLinePointCount];
            for (int i = 0; i < values.Length; i++) values[i] = Mathf.Sin(i * Mathf.PI / (values.Length - 1));
            return values;
        }

        private static Vector3[] BuildRippleCircle()
        {
            var values = new Vector3[RipplePointCount];
            for (int i = 0; i < values.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / values.Length;
                values[i] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            return values;
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

        internal static CastPose EvaluatePose(float elapsed)
        {
            CastPose ready = new CastPose(0.24f, -0.18f, 0.22f, 0.18f, 0.98f, 0f, 0f, 0.02f);
            CastPose windUp = new CastPose(0.02f, 0.13f, 0.24f, -0.38f, 0.92f, 0f, 0f, 0.14f);
            CastPose release = new CastPose(0.44f, -0.03f, 0.17f, 0.82f, 0.57f, 0f, 0f, 0.30f);
            CastPose follow = new CastPose(0.46f, -0.13f, 0.16f, 0.99f, 0.12f, 0f, 0f, 0.10f);
            CastPose settle = new CastPose(0.43f, -0.14f, 0.02f, 0.94f, 0.34f, 3f, 2f, 0.03f);

            if (elapsed < 0.16f) return ready;
            if (elapsed < 0.67f) return CastPose.Lerp(ready, windUp, FishingMath.Segment(elapsed, 0.16f, 0.67f));
            if (elapsed < FishingMath.ReleaseTime)
                return CastPose.Lerp(windUp, release, FishingMath.Segment(elapsed, 0.67f, FishingMath.ReleaseTime));
            if (elapsed < 1.45f) return CastPose.Lerp(release, follow, FishingMath.Segment(elapsed, FishingMath.ReleaseTime, 1.45f));
            return CastPose.Lerp(follow, settle, FishingMath.Segment(elapsed, 1.45f, 2.25f));
        }

        internal readonly struct CastPose
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
