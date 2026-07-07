// Inverted-hull selection outline for the currently selected piece. The classic technique
// (Zelda/Genshin-style): re-render the same mesh with every vertex pushed outward along its
// normal and front faces culled, so only a thin expanded "shell" survives the depth test around
// the piece's silhouette. Chosen over the alternatives deliberately:
//   - vs. a screen-space edge-detect render feature: that pays a full-screen pass every frame to
//     outline ONE object at a time — inverted hull costs exactly one extra draw call, only while
//     a piece is selected, and needs no depth/normals texture (a real cost on mobile TBDR GPUs).
//   - vs. emission+bloom alone: bloom gives a soft halo but no crisp readable edge, and low-end
//     mobile builds may run with bloom disabled entirely. The hull edge stays readable with all
//     post-processing off; the HDR color below just lets bloom sweeten it where available.
// Width is applied in WORLD units so the ring doesn't grow/shrink when a piece's transform is
// squashed by the lift/capture tweens. The fragment is a flat unlit color with a slow sine
// breathe — no lighting, no textures, target 2.0 — so it runs on any Android/iOS GPU.
//
// The outline mesh itself is created at runtime by PrimeTweenPieceAnimator (a child renderer
// sharing the piece's mesh) and animated via MaterialPropertyBlock (_OutlineWidth), same
// per-instance pattern as the rim glow/dissolve in Custom/PieceLitRimGlow.shader.
Shader "Custom/PieceSelectionOutline"
{
    Properties
    {
        // Default is a warm gold with HDR intensity — reads as "royal / your piece" against both
        // black and white pieces and picks up the scene bloom for a halo. Swap toward
        // (0.5, 2.0, 0.7) for the green variant.
        [HDR] _OutlineColor("Outline Color", Color) = (2.2, 1.6, 0.45, 1)
        _OutlineWidth("Outline Width (world units)", Range(0.0, 0.1)) = 0.024

        // Slow luminance breathe so the ring feels alive while a piece stays selected — matches
        // the "subtle idle bob" energy of the lift animation. Shader-time driven: zero C# cost.
        _OutlinePulseSpeed("Pulse Speed", Range(0.0, 12.0)) = 2.5
        _OutlinePulseAmount("Pulse Amount", Range(0.0, 1.0)) = 0.2

        // Softens the outer boundary of the hull shell with a view-facing fade instead of a hard
        // polygon edge — see the fragment shader comment below for why this is what actually
        // fixes the faceted/jagged look on low-poly meshes, not just the width increase.
        _OutlineSoftness("Edge Softness", Range(0.0, 1.0)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "InvertedHullOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // The whole trick: cull FRONT faces so only the back of the expanded shell renders,
            // and the piece's own (closer) front faces depth-reject everything but the rim.
            // ZWrite off + alpha blend (rather than the old opaque ZWrite-on pass) is what lets
            // the fragment below feather the silhouette instead of leaving a hard-edged polygon
            // outline — a straight opaque quad edge is exactly what read as "jagged" on the
            // low-poly pawn in testing.
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
                half _OutlinePulseSpeed;
                half _OutlinePulseAmount;
                half _OutlineSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OutlineVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Extrude in WORLD space, not object space: the pieces get non-uniformly squashed
                // by lift/capture tweens, and a world-space push keeps the ring an even thickness
                // through all of it. TransformObjectToWorldNormal already applies the
                // inverse-transpose, so non-uniform scale doesn't bend the extrusion direction.
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                positionWS += normalWS * _OutlineWidth;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // The jaggedness on low-poly meshes isn't the extrusion width — it's that a flat
                // opaque shell has a razor-hard edge at every facet boundary, so the silhouette
                // reads as a polygon instead of a smooth ring. Fading alpha by how face-on the
                // shell's *own* surface is to the camera (normal . view) makes each facet's edge
                // dissolve into the next rather than terminate abruptly, which is what actually
                // reads as "smoothed" rather than just "thicker". At grazing angles (facet edges,
                // silhouette turns) alpha drops toward the softness floor; face-on the ring stays
                // fully solid.
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(input.viewDirWS);
                half facing = saturate(dot(normalWS, viewDirWS));
                half alpha = lerp(1.0, facing, _OutlineSoftness);

                half pulse = 1.0 - _OutlinePulseAmount * (0.5 + 0.5 * sin(_Time.y * _OutlinePulseSpeed));
                return half4(_OutlineColor.rgb * pulse, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
