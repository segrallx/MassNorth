Shader "MassNorth/InstancedKnight"
{
    Properties
    {
        _VATTex ("VAT Texture", 2D) = "black" {}
        _TotalFrames ("Total Frames", Float) = 1
        _FPS ("Animation FPS", Float) = 30

        _WalkStart ("Walk Start Frame", Float) = 0
        _WalkCount ("Walk Frame Count", Float) = 1
        _AttackStart ("Attack Start Frame", Float) = 0
        _AttackCount ("Attack Frame Count", Float) = 1

        _ArmorColor ("Armor Color", Color) = (0.12, 0.12, 0.12, 1)
        _SkinColor ("Skin Color", Color) = (0.6, 0.45, 0.26, 1)
        _BootsColor ("Boots Color", Color) = (0.025, 0.01, 0.006, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        // ---- ForwardLit Pass ----
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;   // R = part ID (0=armor, 0.33=skin, 0.66=boots)
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1; // x = vertex index normalized for VAT sampling
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float  partId     : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_VATTex);
            SAMPLER(sampler_VATTex);

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _AnimPhase)
                UNITY_DEFINE_INSTANCED_PROP(float, _AnimSpeed)
                UNITY_DEFINE_INSTANCED_PROP(float, _AnimState) // 0=walk, 1=attack
                UNITY_DEFINE_INSTANCED_PROP(float4, _TintColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            CBUFFER_START(UnityPerMaterial)
                float4 _VATTex_TexelSize;
                float  _TotalFrames;
                float  _FPS;
                float  _WalkStart;
                float  _WalkCount;
                float  _AttackStart;
                float  _AttackCount;
                float4 _ArmorColor;
                float4 _SkinColor;
                float4 _BootsColor;
            CBUFFER_END

            float3 SampleVAT(float u, float frame)
            {
                float v = (frame + 0.5) / _TotalFrames;
                return SAMPLE_TEXTURE2D_LOD(_VATTex, sampler_VATTex, float2(u, v), 0).xyz;
            }

            void ApplyVATAnimation(inout float3 pos, float2 uv2, float phase, float animSpeed, float animState)
            {
                float startFrame = animState < 0.5 ? _WalkStart : _AttackStart;
                float frameCount = animState < 0.5 ? _WalkCount : _AttackCount;

                float time = (_Time.y * _FPS * animSpeed + phase * _FPS);
                float localFrame = fmod(abs(time), max(frameCount, 1));
                float frame = startFrame + localFrame;

                float frame0 = floor(frame);
                float nextLocal = fmod(localFrame + 1, max(frameCount, 1));
                float frame1 = startFrame + nextLocal;
                float t = frac(frame);

                float u = uv2.x;
                float3 delta0 = SampleVAT(u, frame0);
                float3 delta1 = SampleVAT(u, frame1);
                float3 delta = lerp(delta0, delta1, t);

                pos += delta;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 pos = IN.positionOS.xyz;
                float phase = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimPhase);
                float animSpeed = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimSpeed);
                float animState = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimState);
                animSpeed = animSpeed > 0.01 ? animSpeed : 1.0;

                ApplyVATAnimation(pos, IN.uv2, phase, animSpeed, animState);

                OUT.positionWS = TransformObjectToWorld(pos);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.partId = IN.color.r;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float pid = IN.partId;
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _TintColor);
                tint = tint.a > 0.01 ? tint : float4(1,1,1,1);

                float4 baseCol;
                if (pid < 0.15)
                    baseCol = _ArmorColor;
                else if (pid < 0.5)
                    baseCol = _SkinColor;
                else
                    baseCol = _BootsColor;

                // Apply tint (multiply armor, keep skin/boots natural)
                if (pid < 0.15)
                    baseCol.rgb *= tint.rgb;

                // Half-Lambert lighting
                Light mainLight = GetMainLight();
                float3 N = normalize(IN.normalWS);
                float NdotL = saturate(dot(N, mainLight.direction)) * 0.6 + 0.4;

                return half4(baseCol.rgb * NdotL * mainLight.color, 1.0);
            }
            ENDHLSL
        }

        // ---- ShadowCaster Pass ----
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv2        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_VATTex);
            SAMPLER(sampler_VATTex);

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _AnimPhase)
                UNITY_DEFINE_INSTANCED_PROP(float, _AnimSpeed)
                UNITY_DEFINE_INSTANCED_PROP(float, _AnimState)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TintColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            CBUFFER_START(UnityPerMaterial)
                float4 _VATTex_TexelSize;
                float  _TotalFrames;
                float  _FPS;
                float  _WalkStart;
                float  _WalkCount;
                float  _AttackStart;
                float  _AttackCount;
                float4 _ArmorColor;
                float4 _SkinColor;
                float4 _BootsColor;
            CBUFFER_END

            float3 SampleVAT(float u, float frame)
            {
                float v = (frame + 0.5) / _TotalFrames;
                return SAMPLE_TEXTURE2D_LOD(_VATTex, sampler_VATTex, float2(u, v), 0).xyz;
            }

            void ApplyVATAnimation(inout float3 pos, float2 uv2, float phase, float animSpeed, float animState)
            {
                float startFrame = animState < 0.5 ? _WalkStart : _AttackStart;
                float frameCount = animState < 0.5 ? _WalkCount : _AttackCount;

                float time = (_Time.y * _FPS * animSpeed + phase * _FPS);
                float localFrame = fmod(abs(time), max(frameCount, 1));
                float frame = startFrame + localFrame;

                float frame0 = floor(frame);
                float nextLocal = fmod(localFrame + 1, max(frameCount, 1));
                float frame1 = startFrame + nextLocal;
                float t = frac(frame);

                float u = uv2.x;
                float3 delta0 = SampleVAT(u, frame0);
                float3 delta1 = SampleVAT(u, frame1);
                float3 delta = lerp(delta0, delta1, t);

                pos += delta;
            }

            float3 _LightDirection;

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 pos = IN.positionOS.xyz;
                float phase = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimPhase);
                float animSpeed = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimSpeed);
                float animState = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimState);
                animSpeed = animSpeed > 0.01 ? animSpeed : 1.0;

                ApplyVATAnimation(pos, IN.uv2, phase, animSpeed, animState);

                float3 positionWS = TransformObjectToWorld(pos);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
