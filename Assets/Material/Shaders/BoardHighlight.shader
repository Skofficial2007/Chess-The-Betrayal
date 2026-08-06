// Flat, unlit colour for the markers drawn on board squares — the move dots, the capture ring,
// the betrayal marker, the check frame and the square tints.
//
// Unlit on purpose. These are affordances telling the player what a tap will do, not objects in
// the world, so they must not be relit, shadowed or exposure-shifted along with the scene. The
// colour authored in the palette is the colour that reaches the screen.
Shader "Chess/BoardHighlight"
{
    Properties
    {
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 0.7)
        [Tooltip] _Glow ("Glow", Range(0, 4)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "BoardHighlight"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            // Declaring every material property inside a single CBUFFER named UnityPerMaterial is
            // what keeps this shader on the SRP Batcher's fast path. Adding a property outside this
            // block silently drops the whole shader off it.
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Glow;
            CBUFFER_END

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // Pushing RGB above 1 is what carries a colour over the bloom threshold, so the
                // states that mean danger can glow while the calm ones stay matte. Alpha is left
                // untouched so a shape keeps exactly the coverage it was authored with.
                half3 colour = _BaseColor.rgb * (1.0h + _Glow);
                return half4(colour, _BaseColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
