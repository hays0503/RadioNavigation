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
        // Legacy version marker for the lit-wall change. Kept so its existing
        // runtime diagnostic remains meaningful; this is not a tuning knob and
        // frag() does not read it.
        _LuneWallAnchor ("Aperture-gated lit wall (bundle marker)", Float) = 1.0
        // Version marker for the one-sided aperture coverage revision. Unity
        // strips the shader body from the AssetBundle, so the exact Properties
        // label is the offline bundle proof and the property ID is its runtime
        // proof. Update it whenever the coverage/blending semantics change.
        _ApertureCoverageVersion ("Inward-only aperture coverage (bundle marker)", Float) = 1.0

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
                // Keep the antialias ramp wholly inside the aperture. The old
                // symmetric ramp left nonzero coverage outside the circle, so
                // moving PIP pixels leaked into the surrounding black bezel.
                float coverage = distanceFromAperture < 0.0
                    ? 1.0 - smoothstep(-edge, 0.0, distanceFromAperture)
                    : 0.0;

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
                // the crescent read as glass rather than a hole. It follows the
                // shadow boundary; its response to scene luminance is a smooth
                // ramp (SextantLuneMath.WallResponse, mirrored here so the test
                // can pin the curve). The earlier hard knee at 0.25 luminance
                // broke the wall into dashes on textured clouds.
                float sceneLuma = dot(color.rgb, float3(0.299, 0.587, 0.114));
                float wallResponse = 0.2 + 0.8 * saturate(sceneLuma * 2.0);
                // MEASURED 2026-10-02. The wall belongs to the SHADOW boundary, not
                // to the tube. Anchoring it at ShadowRadiusUnits moved the ring up to
                // r/R 0.985, which sits under the opaque tube where nothing shows,
                // and the crescent lost the highlight that made its edge read as a
                // crisp moving line. The pupil-side arm of that boundary sits at
                // ShadowRadiusUnits - t = 0.835, INSIDE the picture, and that arm is
                // the feature the author sees move. What has to be clipped is the
                // APERTURE, through the one-sided coverage alpha below. The old
                // symmetric ramp still had nonzero coverage outside the circle; the
                // inward-only ramp now makes the outside contribution exactly zero.
                //
                // _LuneWallAnchor is the legacy lit-wall marker checked by
                // ReportBundleVersion; the distinct _ApertureCoverageVersion
                // property identifies this coverage revision.
                float wallDistance = shadeDistance;
                float rim = 1.0 - saturate(abs(wallDistance / (shadeEdge * 3.0)));
                rim *= rim * _LuneRimLight * wallResponse;

                // Blend SrcAlpha expects straight RGB. Coverage belongs in the
                // output alpha only; multiplying RGB by it as well applied edge
                // coverage twice. The one-sided ramp above makes alpha exactly
                // zero outside the aperture, clipping both the shaded scene and
                // the additive rim without a moving-image fringe.
                // Ignore RT alpha: it is post-stack transparency, not aperture
                // coverage. Preserve full scene RGB inside the lens.
                color.rgb = color.rgb * (1.0 - shade) + rim;
                color.a = i.color.a * coverage;
                return color;
            }
            ENDCG
        }
    }
}
