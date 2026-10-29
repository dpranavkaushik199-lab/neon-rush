using UnityEngine;

namespace NeonRunner
{
    /// <summary>Per-renderer colour overrides for URP Lit/Unlit materials without creating material copies.</summary>
    public static class Tint
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static MaterialPropertyBlock block;

        public static void Set(Renderer renderer, Color color)
        {
            if (!renderer) return;
            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(block);
        }

        public static void SetEmission(Renderer renderer, Color color)
        {
            if (!renderer) return;
            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(EmissionColor, color);
            renderer.SetPropertyBlock(block);
        }

        public static void Clear(Renderer renderer)
        {
            if (renderer) renderer.SetPropertyBlock(null);
        }
    }
}
