// URP Lit, extended with a fresnel-driven rim glow that is independent of the surface's own
// albedo/lighting. Built for chess pieces that share one baked black/white texture: a plain
// _EmissionColor add (or a _BaseColor override) either washes out on bright pieces or erases all
// mesh shading, because it competes with (or replaces) the lit albedo. A rim term keyed off the
// fresnel (view . normal) falloff sits on top of full normal PBR shading instead, so silhouette
// edges glow a consistent color/intensity on every piece color, while the sculpted detail
// (grooves, base rings, AO) stays fully visible in the lit interior of the mesh.
Shader "Custom/PieceLitRimGlow"
{
    Properties
    {
        _WorkflowMode("WorkflowMode", Float) = 1.0

        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)

        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0

        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}

        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap("Specular", 2D) = "white" {}

        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1.0

        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}

        _Parallax("Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}

        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}

        [HDR] _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}

        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailAlbedoMap("Detail Albedo x2", 2D) = "linearGrey" {}
        _DetailNormalMapScale("Scale", Range(0.0, 2.0)) = 1.0
        [Normal] _DetailNormalMap("Normal Map", 2D) = "bump" {}

        // Rim glow — the betrayal/status denoter. Fresnel-masked, driven per-instance via
        // MaterialPropertyBlock (_RimGlowColor/_RimGlowIntensity), independent of _BaseColor.
        // The rim REPLACES the surface color toward the glow color (lerp) rather than only adding,
        // so it reads with equal strength on light and dark pieces, then a small additive kick is
        // layered on top for HDR punch/bloom. A shader-time pulse breathes the whole effect.
        [HDR] _RimGlowColor("Rim Glow Color", Color) = (1, 0, 0, 1)
        _RimGlowIntensity("Rim Glow Intensity", Range(0.0, 4.0)) = 0.0
        _RimGlowPower("Rim Glow Falloff", Range(0.5, 8.0)) = 2.5
        _RimGlowAdditive("Rim Glow Additive Kick", Range(0.0, 4.0)) = 0.8
        _RimGlowPulseSpeed("Rim Glow Pulse Speed", Range(0.0, 12.0)) = 5.0
        _RimGlowPulseAmount("Rim Glow Pulse Amount", Range(0.0, 1.0)) = 0.35

        // Dissolve — promotion's morph-away/reform effect. _DissolveAmount 0 = fully intact,
        // 1 = fully clipped away. Driven per-instance via MaterialPropertyBlock, same pattern as
        // the rim glow above, so it costs nothing extra to batching. Noise is procedural (a hashed
        // value-noise built from object-space position) rather than a sampled texture, so there's
        // no texture asset to import/assign per material.
        //
        // The edge itself is two-toned rather than a single flat color: a narrow white-hot
        // _DissolveCoreColor right at the cut, cross-fading out to a wider _DissolveEdgeColor
        // "ember" band — reads as combustion (white-hot center, cooling embers) instead of a glowing
        // outline. A subtle high-frequency time flicker on the whole band sells "unstable fire"
        // rather than a static gradient.
        _DissolveAmount("Dissolve Amount", Range(0.0, 1.0)) = 0.0
        [HDR] _DissolveEdgeColor("Dissolve Edge Color", Color) = (1, 0.35, 0.02, 1)
        [HDR] _DissolveCoreColor("Dissolve Core Color", Color) = (1, 1, 0.85, 1)
        _DissolveEdgeWidth("Dissolve Edge Width", Range(0.0, 0.5)) = 0.1
        _DissolveEdgeIntensity("Dissolve Edge Intensity", Range(0.0, 6.0)) = 2.5
        _DissolveNoiseScale("Dissolve Noise Scale", Range(1.0, 40.0)) = 12.0
        _DissolveFlickerSpeed("Dissolve Flicker Speed", Range(0.0, 40.0)) = 18.0
        _DissolveFlickerAmount("Dissolve Flicker Amount", Range(0.0, 1.0)) = 0.25

        [HideInInspector] _ClearCoatMask("_ClearCoatMask", Float) = 0.0
        [HideInInspector] _ClearCoatSmoothness("_ClearCoatSmoothness", Float) = 0.0

        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        [HideInInspector] _AddPrecomputedVelocity("_AddPrecomputedVelocity", Float) = 0.0
        [HideInInspector] _XRMotionVectorsPass("_XRMotionVectorsPass", Float) = 1.0

        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        _QueueOffset("Queue offset", Float) = 0.0

        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _GlossMapScale("Smoothness", Float) = 0.0
        [HideInInspector] _Glossiness("Smoothness", Float) = 0.0
        [HideInInspector] _GlossyReflections("EnvironmentReflections", Float) = 0.0

        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex LitPassVertex
            #pragma fragment RimGlowLitPassFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ _ALPHAPREMULTIPLY_ON _ALPHAMODULATE_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _SPECULAR_SETUP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            float4 _RimGlowColor;
            float _RimGlowIntensity;
            float _RimGlowPower;
            float _RimGlowAdditive;
            float _RimGlowPulseSpeed;
            float _RimGlowPulseAmount;

            float _DissolveAmount;
            float4 _DissolveEdgeColor;
            float4 _DissolveCoreColor;
            float _DissolveEdgeWidth;
            float _DissolveEdgeIntensity;
            float _DissolveNoiseScale;
            float _DissolveFlickerSpeed;
            float _DissolveFlickerAmount;

            // Cheap 3D value-noise hash, keyed off object-space position so the dissolve pattern
            // is stable in world space (doesn't swim with the mesh's UV seams) and tiles for free
            // across any mesh without needing unwrapped UVs or a sampled texture.
            float3 DissolveHash3(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            half DissolveNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                half n000 = DissolveHash3(i + float3(0, 0, 0)).x;
                half n100 = DissolveHash3(i + float3(1, 0, 0)).x;
                half n010 = DissolveHash3(i + float3(0, 1, 0)).x;
                half n110 = DissolveHash3(i + float3(1, 1, 0)).x;
                half n001 = DissolveHash3(i + float3(0, 0, 1)).x;
                half n101 = DissolveHash3(i + float3(1, 0, 1)).x;
                half n011 = DissolveHash3(i + float3(0, 1, 1)).x;
                half n111 = DissolveHash3(i + float3(1, 1, 1)).x;

                half nx00 = lerp(n000, n100, f.x);
                half nx10 = lerp(n010, n110, f.x);
                half nx01 = lerp(n001, n101, f.x);
                half nx11 = lerp(n011, n111, f.x);
                half nxy0 = lerp(nx00, nx10, f.y);
                half nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            // Same body as URP's LitPassFragment (LitForwardPass.hlsl), plus one additive term.
            half4 RimGlowLitPassFragment(Varyings input) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                #if defined(_PARALLAXMAP)
                #if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
                    half3 viewDirTS = input.viewDirTS;
                #else
                    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, viewDirWS);
                #endif
                    ApplyPerPixelDisplacement(viewDirTS, input.uv);
                #endif

                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);

                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(input.positionCS);
                #endif

                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

                #if defined(_DBUFFER)
                    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
                #endif

                InitializeBakedGIData(input, inputData);

                // Dissolve: clip pixels where the procedural noise falls below the current
                // threshold, then light up a two-tone "burning edge" just above the threshold — a
                // narrow white-hot core right at the cut, cross-fading out to a wider ember band —
                // so the cut reads as combustion rather than a flat alpha wipe or single-color
                // outline. A fast, low-amplitude time flicker rides on top so the band feels like
                // unstable fire instead of a static gradient. _DissolveAmount is 0 by default (skip
                // all of this) so pieces with no dissolve in flight pay nothing extra.
                half3 dissolveGlow = 0.0;
                if (_DissolveAmount > 0.0)
                {
                    float3 objectPos = TransformWorldToObject(input.positionWS);
                    half noise = DissolveNoise(objectPos * _DissolveNoiseScale);
                    clip(noise - _DissolveAmount);

                    half distFromEdge = noise - _DissolveAmount;
                    half emberMask = 1.0 - saturate(distFromEdge / max(_DissolveEdgeWidth, 1e-4));
                    half coreMask = 1.0 - saturate(distFromEdge / max(_DissolveEdgeWidth * 0.25, 1e-4));
                    half flicker = 1.0 - _DissolveFlickerAmount * (0.5 + 0.5 * sin(_Time.y * _DissolveFlickerSpeed + noise * 20.0));

                    dissolveGlow = lerp(_DissolveEdgeColor.rgb, _DissolveCoreColor.rgb, coreMask) * emberMask * _DissolveEdgeIntensity * flicker;
                }

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb += dissolveGlow;

                // Fresnel rim: 0 straight-on, 1 at grazing angles. An additive-only rim can never
                // read equally on light and dark pieces — adding red to an already-bright surface
                // barely shifts the pixel, while the same add on a dark surface is high-contrast.
                // So the rim is applied in two layers:
                //   1. REPLACE: lerp the lit surface toward the glow color by the rim mask. A lerp
                //      overrides whatever albedo is underneath, so the mark reads with identical
                //      strength on white and black pieces, while the mesh interior (mask ~0) keeps
                //      full PBR shading and detail.
                //   2. ADD: a smaller HDR additive kick on top of the replaced rim for punch and
                //      bloom pickup.
                // A shader-time sine pulse breathes intensity so the mark reads as "unstable /
                // pending", with no per-frame C# cost.
                half pulse = 1.0 - _RimGlowPulseAmount * (0.5 + 0.5 * sin(_Time.y * _RimGlowPulseSpeed));
                half rimIntensity = _RimGlowIntensity * pulse;
                half rimFresnel = pow(saturate(1.0 - dot(inputData.normalWS, inputData.viewDirectionWS)), _RimGlowPower);
                half rimMask = saturate(rimFresnel * rimIntensity);
                color.rgb = lerp(color.rgb, _RimGlowColor.rgb, rimMask);
                color.rgb += _RimGlowColor.rgb * (rimMask * _RimGlowAdditive);

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex UniversalVertexMeta
            #pragma fragment UniversalFragmentMetaLit
            #pragma shader_feature_local_fragment _SPECULAR_SETUP
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local_fragment _SPECGLOSSMAP
            #pragma shader_feature EDITOR_VISUALIZATION
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitMetaPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
    CustomEditor "UnityEditor.Rendering.Universal.ShaderGUI.LitShader"
}
