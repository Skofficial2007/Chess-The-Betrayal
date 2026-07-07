using UnityEngine;
using UnityEngine.Rendering;

namespace ChessTheBetrayal.Infrastructure
{
    /// <summary>
    /// Runtime-built ShaderVariantCollection warmup — no .shadervariants asset, no Editor recording
    /// session required. PieceLitRimGlow's rim-glow/dissolve controls (_RimGlowIntensity,
    /// _DissolveAmount, ...) are plain uniforms, not shader_feature keywords, so they don't add
    /// variants; the real variant surface is URP's own global lighting/shadow/SSAO keywords, which
    /// are identical for every material sharing a scene. WarmUp() with each tracked material's
    /// *currently active* keyword set (i.e. no explicit keywords passed — ShaderVariantCollection
    /// resolves that from global state) is therefore enough to compile the exact variant every
    /// piece is about to render with, without needing to guess/enumerate combinations by hand.
    /// </summary>
    public sealed class ShaderVariantPrewarmService : IShaderPrewarmService
    {
        private readonly Material[] _materialsToWarm;

        public ShaderVariantPrewarmService(Material[] materialsToWarm)
        {
            _materialsToWarm = materialsToWarm;
        }

        public void Prewarm()
        {
            if (_materialsToWarm == null || _materialsToWarm.Length == 0) return;

            var collection = new ShaderVariantCollection();
            foreach (Material material in _materialsToWarm)
            {
                if (material == null || material.shader == null) continue;

                foreach (var passType in PassTypesToWarm)
                {
                    // Empty keyword array = "whatever this material/global state currently has
                    // active" — see class doc for why that's the right variant to target here.
                    collection.Add(new ShaderVariantCollection.ShaderVariant(material.shader, passType));
                }
            }

            collection.WarmUp();
        }

        private static readonly PassType[] PassTypesToWarm =
        {
            PassType.Normal,
            PassType.ShadowCaster,
        };
    }
}
