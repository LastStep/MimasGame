// Unlit overlay for the aiming layer: the dashed path preview, its aim ring and X mark, and the
// placeholder projectile. One flat colour, alpha blended, depth tested against the board so a line really
// does disappear behind a plateau, and dashes generated from the line's own UV rather than a texture asset.
//
// LineRenderer with textureMode = Stretch gives u = 0..1 along the whole line, so _DashCount is "how many
// dashes over this line" and the caller scales it by the line's world length to keep dash size constant.
// _DashDuty of 1 makes the shader solid, which is how the ring, the X and the projectile use it.
Shader "Mimas/AimLine"
{
    Properties
    {
        [MainColor] _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _DashCount("Dashes along the line", Float) = 20
        _DashDuty("Lit fraction of a dash", Range(0.05, 1)) = 0.55
        _DashSpeed("Dash scroll, dashes per second", Float) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _DashCount;
                float _DashDuty;
                float _DashSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half alpha = _BaseColor.a;

                if (_DashDuty < 0.999)
                {
                    // Flow towards the target: the phase walks forward, so the dashes read as direction.
                    float phase = frac(input.uv.x * _DashCount - _Time.y * _DashSpeed);
                    float aa = max(fwidth(phase), 1e-4);
                    alpha *= 1.0 - smoothstep(_DashDuty - aa, _DashDuty + aa, phase);
                }

                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
