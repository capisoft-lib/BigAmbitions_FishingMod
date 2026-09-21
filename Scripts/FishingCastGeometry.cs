using UnityEngine;

namespace FishingMod
{
    internal static class FishingCastGeometry
    {
        internal static Vector3 ElbowPosition(Vector3 shoulder, Vector3 wrist, Vector3 hint,
            float upperLength, float lowerLength)
        {
            Vector3 delta = wrist - shoulder;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upperLength-lowerLength)+.0001f,
                upperLength+lowerLength-.0001f);
            Vector3 direction = delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.forward;
            Vector3 bend = Vector3.ProjectOnPlane(hint, direction).normalized;
            if (bend.sqrMagnitude < .01f) bend = Vector3.Cross(direction, Vector3.forward).normalized;
            if (bend.sqrMagnitude < .01f) bend = Vector3.Cross(direction, Vector3.up).normalized;
            float along = (upperLength*upperLength-lowerLength*lowerLength+distance*distance)/(2f*distance);
            float height = Mathf.Sqrt(Mathf.Max(0f,upperLength*upperLength-along*along));
            return shoulder + direction*along + bend*height;
        }
        internal static void GripFrames(Vector3 rodDirection, Vector3 up, Vector3 fallbackSide,
            out Quaternion right, out Quaternion left)
        {
            Vector3 across = Vector3.Cross(up, rodDirection).normalized;
            if (across.sqrMagnitude < .01f) across = Vector3.ProjectOnPlane(fallbackSide, rodDirection).normalized;
            if (Vector3.Dot(across, fallbackSide) < 0f) across = -across;
            Vector3 normal = Vector3.Cross(rodDirection, across).normalized;
            right = Quaternion.LookRotation(-across, normal);
            left = Quaternion.LookRotation(across, -normal);
        }

        internal static Vector3 LandingPoint(Vector3 clickedWater) => clickedWater + Vector3.up * .06f;

        internal static Vector3 ConstrainGrip(Vector3 desired, Vector3 leftOffset, Vector3 rightShoulder,
            Vector3 leftShoulder, float rightReach, float leftReach)
        {
            // Project the common grip into both arm reach spheres, keeping the
            // two hands on the same handle instead of clamping each independently.
            for (int i=0;i<20;i++)
            {
                desired=rightShoulder+Vector3.ClampMagnitude(desired-rightShoulder,rightReach);
                Vector3 left=desired+leftOffset;
                desired=leftShoulder+Vector3.ClampMagnitude(left-leftShoulder,leftReach)-leftOffset;
            }
            return desired;
        }

        internal static Vector3 FlightPoint(Vector3 launch, Vector3 landing, float progress, float scale)
        {
            progress=Mathf.Clamp01(progress);
            float distance=Vector3.Distance(launch,landing);
            float apex=Mathf.Clamp(distance*.12f,1f*scale,8f*scale);
            return Vector3.Lerp(launch,landing,progress)+Vector3.up*FishingMath.BallisticHeight(progress,apex);
        }
    }
}
