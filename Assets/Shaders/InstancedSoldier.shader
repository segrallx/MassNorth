Shader "MassNorth/InstancedSoldier"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.6, 0.6, 0.6, 1)
        _SwingSpeed ("Swing Speed", Float) = 5.0
        _SwingAngle ("Swing Angle (degrees)", Float) = 25.0
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
            #include "SoldierAnimation.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR; // R = swing tag, G = part ID
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

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ArmorColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _SkinColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ClothColor)
                UNITY_DEFINE_INSTANCED_PROP(float,  _AnimPhase)
                UNITY_DEFINE_INSTANCED_PROP(float,  _AnimSpeed)
            UNITY_INSTANCING_BUFFER_END(Props)

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _SwingSpeed;
                float  _SwingAngle;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 pos = IN.positionOS.xyz;
                float swingTag = IN.color.r;
                float phase = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimPhase);
                float animSpeed = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimSpeed);
                // Clamp animSpeed to sane range (default 1 if unset)
                animSpeed = animSpeed > 0.01 ? animSpeed : 1.0;

                ApplySoldierAnimation(pos, swingTag, phase, animSpeed, _SwingSpeed, _SwingAngle);

                OUT.positionWS = TransformObjectToWorld(pos);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.partId = IN.color.g;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // Multi-part coloring based on vertex color G channel (partId)
                float pid = IN.partId;
                float4 armorCol = UNITY_ACCESS_INSTANCED_PROP(Props, _ArmorColor);
                float4 skinCol  = UNITY_ACCESS_INSTANCED_PROP(Props, _SkinColor);
                float4 clothCol = UNITY_ACCESS_INSTANCED_PROP(Props, _ClothColor);

                // Fallback if not set
                armorCol = armorCol.a > 0.01 ? armorCol : _BaseColor;
                skinCol  = skinCol.a  > 0.01 ? skinCol  : float4(0.9, 0.75, 0.6, 1);
                clothCol = clothCol.a > 0.01 ? clothCol : float4(0.3, 0.3, 0.4, 1);

                float4 baseCol;
                // 0.0 = armor (body), 0.2 = skin (head/hands), 0.4 = cloth (legs),
                // 0.6 = weapon/shield (dark gray), 0.8 = helmet
                if (pid < 0.1)
                    baseCol = armorCol;
                else if (pid < 0.3)
                    baseCol = skinCol;
                else if (pid < 0.5)
                    baseCol = clothCol;
                else if (pid < 0.7)
                    baseCol = float4(0.35, 0.35, 0.38, 1); // weapon/shield: dark gray
                else
                    baseCol = armorCol * 0.85; // helmet: slightly darker armor

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
            #include "SoldierAnimation.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ArmorColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _SkinColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ClothColor)
                UNITY_DEFINE_INSTANCED_PROP(float,  _AnimPhase)
                UNITY_DEFINE_INSTANCED_PROP(float,  _AnimSpeed)
            UNITY_INSTANCING_BUFFER_END(Props)

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _SwingSpeed;
                float  _SwingAngle;
            CBUFFER_END

            float3 _LightDirection;

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 pos = IN.positionOS.xyz;
                float swingTag = IN.color.r;
                float phase = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimPhase);
                float animSpeed = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimSpeed);
                animSpeed = animSpeed > 0.01 ? animSpeed : 1.0;

                ApplySoldierAnimation(pos, swingTag, phase, animSpeed, _SwingSpeed, _SwingAngle);

                float3 positionWS = TransformObjectToWorld(pos);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                // Apply shadow bias
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
