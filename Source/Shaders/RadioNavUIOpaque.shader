// Opaque PIP display with an antialiased circular aperture.
// The source RT's alpha belongs to the game's post stack and must not darken
// the scene. Only the SDF edge coverage is used as output alpha.
Shader "RadioNav/UIOpaque"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ApertureEdgePixels ("Aperture edge width (pixels)", Range(0.5, 4.0)) = 3.0

        // "Lune in the scope": the exit-pupil shadow. _LuneOffset is the
        // observer's eye displacement in aperture radii, shared by both halves
        // (their uvRects tile the same full-ocular space).
        [HideInInspector] _LuneOffset ("Eye offset", Vector) = (0,0,0,0)
        _LuneRadius ("Obstruction disc radius", Range(0.5, 1.0)) = 0.985
        _LuneStrength ("Shadow depth", Range(0, 1)) = 0.95
        _LuneRimLight ("Lit tube edge", Range(0, 0.5)) = 0.09

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_CLIP_RECT)] _UseUIClipRect ("Use UI Clip Rect", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

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
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _ApertureEdgePixels;
            float4 _LuneOffset;
            float _LuneRadius;
            float _LuneStrength;
            float _LuneRimLight;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 color = (tex2D(_MainTex, i.texcoord) + _TextureSampleAdd) * i.color;

                // RawImage.uvRect spans [0,.5] and [.5,1] for the two eyes, so
                // these are shared full-ocular coordinates despite separate RTs.
                float2 p = i.texcoord * 2.0 - 1.0;
                float distanceFromAperture = length(p) - 1.0;
                float edge = max(fwidth(distanceFromAperture) * max(_ApertureEdgePixels, 0.5), 1e-5);
                float coverage = 1.0 - smoothstep(-edge, edge, distanceFromAperture);

                #ifdef UNITY_UI_CLIP_RECT
                coverage *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                // Eye-pupil shadow ("lune dans le scope"): an eye displaced
                // from the optic axis loses the field behind the tube's inner
                // wall. The lost part is everything OUTSIDE the obstruction
                // disc centred on _LuneOffset, so a centred eye keeps a thin
                // uniform ring of shade and a displaced one grows a crescent on
                // the opposite side. fwidth gives the shadow a hard-but-not-
                // aliased boundary, like the real tube wall.
                float shadeDistance = length(p - _LuneOffset.xy) - _LuneRadius;
                float shadeEdge = clamp(fwidth(shadeDistance), 1e-5, 0.05);
                float shade = smoothstep(-shadeEdge, shadeEdge, shadeDistance) * _LuneStrength;

                // The lit inner wall just past the shadow boundary is what makes
                // the crescent read as glass rather than a hole. Gated on the
                // scene's own luminance so the black lens backing stays black.
                float rim = 1.0 - saturate(abs(shadeDistance / (shadeEdge * 3.0)));
                rim *= rim * _LuneRimLight * saturate(color.r * 4.0);

                // EVERYTHING the field adds is multiplied by the coverage, the lit rim included: outside the aperture the source alpha is 0, and a canvas that composites premultiplied adds the RGB regardless of it, which paints the rim as a bright arc OUTSIDE the ocular (the author's report, 2026-10-02). Gating the whole colour also covers the shadow term itself - on the near side of a displaced pupil the field beyond the edge is not fully shadowed, so scene * (1 - shade) leaked there too.
                // Ignore RT alpha (it is not coverage); retain full RGB inside
                // the lens and blend only the sub-pixel aperture boundary.
                color.rgb = (color.rgb * (1.0 - shade) + rim) * coverage;
                color.a = i.color.a * coverage;
                return color;
            }
            ENDCG
        }
    }
}
