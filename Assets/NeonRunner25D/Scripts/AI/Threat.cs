using UnityEngine;

namespace NeonRunner
{
    /// <summary>How close the nearest active chaser (a chasing cop or the Security Wall) is to a point.</summary>
    public static class Threat
    {
        /// <summary>Distance at which a chaser starts to register.</summary>
        public const float FarDistance = 22f;
        /// <summary>Distance at which the threat reads as maximal.</summary>
        public const float NearDistance = 3f;

        public static float NearestCopDistance(Vector2 point)
        {
            float best = float.PositiveInfinity;
            foreach (var cop in CopBase.All)
                if (cop.State == CopBase.Awareness.Chase && !cop.IsStunned)
                    best = Mathf.Min(best, Vector2.Distance(point, cop.transform.position));
            return best;
        }

        public static float NearestWallDistance(Vector2 point)
        {
            float best = float.PositiveInfinity;
            foreach (var wall in SecurityWall.All)
                if (wall.IsActive) best = Mathf.Min(best, Mathf.Max(0f, point.x - wall.transform.position.x));
            return best;
        }

        /// <summary>0 when nothing is chasing within <see cref="FarDistance"/>, 1 when a chaser is on top of you.</summary>
        public static float Closeness(float distance) => 1f - Mathf.InverseLerp(NearDistance, FarDistance, distance);

        public static float Closeness(Vector2 point) =>
            Closeness(Mathf.Min(NearestCopDistance(point), NearestWallDistance(point)));
    }
}
