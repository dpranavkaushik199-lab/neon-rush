using System.Collections.Generic;
using UnityEngine;

namespace NeonRunner
{
    /// <summary>Anything the prosthetic arm can hook: ledge points or cop drones.</summary>
    public class GrappleTarget : MonoBehaviour
    {
        public enum TargetKind { Point, Drone }

        public static readonly List<GrappleTarget> All = new List<GrappleTarget>();

        [SerializeField] TargetKind kind = TargetKind.Point;
        [SerializeField] Vector2 anchorOffset;
        [SerializeField] Transform pulseVisual;

        public TargetKind Kind => kind;
        public Vector2 Anchor => (Vector2)transform.position + anchorOffset;
        public bool IsAvailable => isActiveAndEnabled;

        Vector3 baseScale;
        float pulse;

        void Awake()
        {
            if (pulseVisual) baseScale = pulseVisual.localScale;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void Pulse() => pulse = 1f;

        void Update()
        {
            if (!pulseVisual) return;
            pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime * 4f);
            float idle = 1f + Mathf.Sin(Time.time * 4f) * 0.06f;
            pulseVisual.localScale = baseScale * (idle + pulse * 0.6f);
            if (kind == TargetKind.Point) pulseVisual.Rotate(0f, 0f, 90f * Time.deltaTime);
        }
    }
}
