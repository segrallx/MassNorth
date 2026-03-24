Shader "MassNorth/PartTexUVRemap"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite (unused)", 2D) = "white" {}
        _SkinTex ("Skin", 2D) = "white" {}
        _UVTex ("UV Frame", 2D) = "white" {}
        [ToggleUI] _VFlip ("V Flip", Float) = 0
        [ToggleUI] _SwapRG ("Swap RG (UV axis)", Float) = 0
        [ToggleUI] _TexelCenterSkin ("Skin half-texel center", Float) = 1
        [ToggleUI] _DecodeSrgbUV ("Recover UV if onehand wrongly sRGB", Float) = 0
        _UVScale ("UV Scale (xy)", Vector) = (1, 1, 0, 0)
        _UVOffset ("UV Offset (xy)", Vector) = (0, 0, 0, 0)
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _AlphaCutoff ("Alpha Cutoff", Range(0, 1)) = 0.004
        [HideInInspector] _SkinAtlasDim ("Skin WxH", Vector) = (64, 126, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Pass
        {
            Name "PartTexUVRemap"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SkinTex);
            TEXTURE2D(_UVTex);

            SamplerState PointClampSampler
            {
                Filter = MIN_MAG_MIP_POINT;
                AddressU = Clamp;
                AddressV = Clamp;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _SkinTex_ST;
                float4 _UVTex_ST;
                float4 _SkinAtlasDim;
                float4 _UVScale;
                float4 _UVOffset;
                half4 _Color;
                float _VFlip;
                float _SwapRG;
                float _TexelCenterSkin;
                float _DecodeSrgbUV;
                float _AlphaCutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            // 若 UV 图被误标为 sRGB，GPU 会先解码成线性；用近似 sRGB 编码把「按字节存的 UV」拉回来
            float LinToSRGBChannel(float lin)
            {
                return lin <= 0.0031308f ? lin * 12.92f : 1.055f * pow(lin, 1.0f / 2.4f) - 0.055f;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 uvSample = SAMPLE_TEXTURE2D(_UVTex, PointClampSampler, i.uv);
                float2 su = float2(uvSample.r, uvSample.g);
                if (_DecodeSrgbUV > 0.5)
                {
                    su.x = LinToSRGBChannel(su.x);
                    su.y = LinToSRGBChannel(su.y);
                }
                su = su * _UVScale.xy + _UVOffset.xy;
                if (_SwapRG > 0.5)
                    su = su.yx;
                if (_VFlip > 0.5)
                    su.y = 1.0 - su.y;

                su = saturate(su);

                // 将 0..1 的编码 UV 对齐到皮肤图素中心，减轻点采样错位
                if (_TexelCenterSkin > 0.5)
                {
                    float zw = max(_SkinAtlasDim.x, 1.0);
                    float ww = max(_SkinAtlasDim.y, 1.0);
                    su.x = (su.x * (zw - 1.0) + 0.5) / zw;
                    su.y = (su.y * (ww - 1.0) + 0.5) / ww;
                }

                half4 skin = SAMPLE_TEXTURE2D(_SkinTex, PointClampSampler, su);
                half a = skin.a * uvSample.a * i.color.a * _Color.a;
                clip(a - _AlphaCutoff);
                return half4(skin.rgb * i.color.rgb * _Color.rgb, a);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
