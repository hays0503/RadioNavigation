Shader "UI/SextantOcular"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SkyTop ("Sky Top", Color) = (0.18, 0.45, 0.85, 1)
        _SkyHorizon ("Sky Horizon", Color) = (0.55, 0.72, 0.92, 1)
        _Sea ("Sea", Color) = (0.06, 0.14, 0.27, 1)
        _HorizonOffset ("Horizon Offset", Range(-0.5, 0.5)) = 0
        _VignettePower ("Vignette Power", Range(0,4)) = 2.2
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            fixed4 _SkyTop;
            fixed4 _SkyHorizon;
            fixed4 _Sea;
            float _HorizonOffset;
            float _VignettePower;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                // horizon in UV space: 0.5 + offset, offset = -arcAngle / (FOV/2) * 0.5
                float horizonY = 0.5 + _HorizonOffset;
                // sky gradient above horizon, sea below
                float skyT = saturate((uv.y - horizonY) / max(0.001, 1.0 - horizonY));
                float seaT = saturate((horizonY - uv.y) / max(0.001, horizonY));
                fixed4 skyCol = lerp(_SkyHorizon, _SkyTop, skyT);
                fixed4 seaCol = _Sea;
                fixed4 col = uv.y > horizonY ? skyCol : seaCol;
                // natural horizon - no white line (user request)
                // subtle vignette inside shader (outer ring already does, keep light)
                float2 d = uv * 2.0 - 1.0;
                float r = dot(d, d) * 0.5;
                float vig = pow(saturate(1.0 - r), _VignettePower);
                col.rgb *= lerp(0.85, 1.0, vig);

                half alpha = col.a * IN.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif
                return fixed4(col.rgb, alpha);
            }
            ENDCG
        }
    }
}
