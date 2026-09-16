// The board's tile shader. Two jobs:
//
//   1. Look like the URP Lit material it replaces: one flat base colour per tile, pushed through a
//      MaterialPropertyBlock (_BaseColor), lit by the main directional light with SH ambient, casting and
//      receiving shadows so plateaus and props keep reading as solid.
//   2. Draw the armed attack's range band (design: #presentation, #attacks). Ranges are Euclidean, so the
//      band really is an annulus: every fragment measures its own world XZ distance to the attacker, dims
//      itself outside [min, max] and lights up on the two ring lines. Because it is per fragment, the
//      circle bends over steps and plateaus for free — no decal (URP's Decal Projector does not render on
//      WebGL2, Unity bug IN-90245), no ring mesh to seam at a height change.
//
// WebGL2 constraints: no compute, no SV_ tricks, target 3.0, everything a GLES3 fragment shader can do.
Shader "Mimas/HexTile"
{
    Properties
    {
        [MainColor] _BaseColor("Base Colour", Color) = (0.6, 0.6, 0.6, 1)

        [Header(Range band)]
        _RangeCenter("Centre (xz world, w = on)", Vector) = (0, 0, 0, 0)
        _RangeMin("Inner radius (world)", Float) = 0
        _RangeMax("Outer radius (world)", Float) = 0
        _RangeLine("Ring half width (world)", Float) = 0.05
        _RangeColor("Ring colour", Color) = (1, 0.69, 0.66, 1)
        _RangeDim("Dim outside the band", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RangeCenter;
                float4 _RangeColor;
                float _RangeMin;
                float _RangeMax;
                float _RangeLine;
                float _RangeDim;
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
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            /// Base colour with the range band applied: dimmed outside, ring lines drawn on both radii.
            half3 ApplyRangeBand(half3 albedo, float3 positionWS)
            {
                if (_RangeCenter.w < 0.5) return albedo;

                float d = distance(positionWS.xz, _RangeCenter.xz);

                // One-texel feather so the lines stay thin at any camera distance without shimmering.
                float aa = max(fwidth(d), 1e-5);

                float inBand = step(_RangeMin - _RangeLine, d) * step(d, _RangeMax + _RangeLine);
                albedo *= lerp(_RangeDim, 1.0, inBand);

                float outer = 1.0 - smoothstep(_RangeLine, _RangeLine + aa, abs(d - _RangeMax));
                float inner = _RangeMin > 1e-4 ? 1.0 - smoothstep(_RangeLine, _RangeLine + aa, abs(d - _RangeMin)) : 0.0;
                float ring = saturate(outer + inner) * _RangeColor.a;

                return lerp(albedo, _RangeColor.rgb, ring);
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half3 albedo = ApplyRangeBand(_BaseColor.rgb, input.positionWS);
                float3 normalWS = normalize(input.normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half lambert = saturate(dot(normalWS, mainLight.direction));

                // Deliberately no mainLight.distanceAttenuation. For the main directional light that field is
                // only the per-object "is the main light on" flag (unity_LightData.z), and this project's
                // renderer is Forward+, which never fills it unless the shader declares the cluster-light-loop
                // keyword — it comes back 0 and the board renders on ambient alone. The main light either
                // exists (and _MainLightColor carries it) or it does not.
                half3 direct = mainLight.color * (lambert * mainLight.shadowAttenuation);
                half3 ambient = SampleSH(normalWS);

                return half4(albedo * (direct + ambient), 1.0);
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
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RangeCenter;
                float4 _RangeColor;
                float _RangeMin;
                float _RangeMax;
                float _RangeLine;
                float _RangeDim;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFragment(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RangeCenter;
                float4 _RangeColor;
                float _RangeMin;
                float _RangeMax;
                float _RangeLine;
                float _RangeDim;
            CBUFFER_END

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings DepthVertex(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFragment(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
