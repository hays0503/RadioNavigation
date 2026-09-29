// RadioNavigation Sternspreiz star shader.
//
// FOUR passes, but only ONE of them may ever be matched by a pipeline stage:
//
//  - "PointSky" (LightMode=Sky) is the ONLY pipeline-visible pass. The Deep
//    pipeline's Sky stage (DeepPipeline.RenderSky, ShaderTagId "Sky",
//    RenderQueueRange.opaque) draws it right after DrawSkybox and BEFORE the
//    forward/G-buffer stages. The UniStorm cloud composite is the only shipping
//    Sky-tagged shader (queue 1051), so a celestial material queued below 1051
//    (and below 2500 so the opaque-range filter accepts it) is composited UNDER
//    the clouds - which is the whole point: the game's own composite, not a
//    mask, decides how much starlight a cloud lets through.
//
//  - "Point", "PointForward" and "Streak" are RETAINED COPIES that carry a
//    private LightMode tag no pipeline stage queries. They must stay
//    non-matchable. RenderDoc evidence (main-view capture
//    mainview_frame25072, 2026-09-30): with the material at queue 1020 the
//    sprite mesh was submitted TWICE per frame - once into the 800x452 scene
//    target before the cloud composite (correct) and once into the 1360x768
//    post-process output AFTER it, from a DIFFERENT compiled pass of the same
//    material (the pixel shader hash was identical, the shader object id was
//    not). That second submission is what made the stars shine through the
//    clouds, and it ignored renderQueue, so no queue value can suppress it.
//    Tagging the copy with a name the pipeline never asks for removes it.
//    The lens still draws "Streak" EXPLICITLY through the PIP command buffer
//    (CommandBuffer.DrawMesh with an explicit pass index), which is unaffected
//    by tags.
//
//  - The pipeline-visible behaviour is unchanged by this: an untagged pass
//    falls back to SRPDefaultUnlit, and the SRP Batcher picks the pass by tag,
//    not by name, so the copies below never shadow "PointSky".
//
// Blend comes from the material's _SrcBlend/_DstBlend, which is how StarsMesh
// asks for additive star light.
// The line profile/texture and the band shaping are the mod's tested PSF math.
// NOTE (open question): the Sky stage enables the global keyword TOD_OUTPUT_HDR
// before DrawSkybox/RenderSky; no keyword is declared here on purpose - if the
// UniStorm composite ever needs it, that must be measured, not guessed.
Shader "RadioNav/SternspreizStreak"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / profile", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Softness ("Band softness", Range(0.01, 1)) = 0.35
        _EndTaper ("End taper", Range(0, 0.5)) = 0.25
        _WidthTaper ("Width taper toward the ends", Range(0, 2)) = 1.0
        _MinWidth ("Minimum relative width at the ends", Range(0.02, 1)) = 0.12
        // Cloud occlusion: UniStorm's fullCloudBuff0 dome alpha (modes 1/2) or
        // the screen-space GBuffer3 alpha the cloud composite writes (mode 3)
        // fades the point sprites behind the clouds. Declared here (not just in
        // the passes) so Material.HasProperty sees them; mode 0 = off.
        [HideInInspector] _CloudOcclusionTex ("Cloud occlusion", 2D) = "black" {}
        [HideInInspector] _CloudOcclusionMode ("Cloud occlusion mode", Float) = 0
        [HideInInspector] _CloudOcclusionStrength ("Cloud occlusion strength", Float) = 1
        [HideInInspector] _SrcBlend ("__src", Float) = 5
        [HideInInspector] _DstBlend ("__dst", Float) = 1
        [HideInInspector] _ZWrite ("__zw", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull Off
        Lighting Off

        // 1) Point sprite copy - kept for reference, never matched by a stage.
        Pass
        {
            Name "Point"
            Tags { "LightMode" = "RadioNavPointSpriteReference" }
            CGPROGRAM
            #pragma vertex vertPoint
            #pragma fragment fragPoint
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            sampler2D _CloudOcclusionTex;
            float _CloudOcclusionMode;
            float _CloudOcclusionStrength;

            struct pointAppdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct pointV2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            pointV2f vertPoint(pointAppdata v)
            {
                pointV2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 fragPoint(pointV2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                // Cloud occlusion (mode 0 = untouched default path).
                if (_CloudOcclusionMode > 0.5 && _CloudOcclusionStrength > 0.0)
                {
                    float a;
                    if (_CloudOcclusionMode > 2.5)
                    {
                        // Mode 3: screen-space cloud opacity. The UniStorm
                        // composite writes min(cloudAlpha * 1.5, 1) into the
                        // alpha of the game's GBuffer3 target, so the fragment's
                        // own screen UV addresses exactly its cloud coverage.
                        float2 screenUv = i.pos.xy / _ScreenParams.xy;
                        a = tex2D(_CloudOcclusionTex, screenUv).a;
                    }
                    else
                    {
                        float3 dirW = normalize(i.worldPos - _WorldSpaceCameraPos);
                        float2 domeUv;
                        if (_CloudOcclusionMode < 1.5)
                        {
                            float3 dirView = mul(UNITY_MATRIX_V, float4(dirW, 0.0)).xyz;
                            domeUv = dirView.xy * 0.5 + 0.5;
                        }
                        else
                        {
                            domeUv = normalize(dirW).xy * 0.5 + 0.5;
                        }
                        a = tex2D(_CloudOcclusionTex, domeUv).a;
                    }
                    float occl = saturate(a * 1.5);
                    c.a *= (1.0 - occl * _CloudOcclusionStrength);
                }
                return c;
            }
            ENDCG
        }

        // 2) Same point sprite for the pipeline's forward tag.
        Pass
        {
            Name "PointForward"
            Tags { "LightMode" = "RadioNavPointForwardReference" }
            CGPROGRAM
            #pragma vertex vertPoint
            #pragma fragment fragPoint
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            sampler2D _CloudOcclusionTex;
            float _CloudOcclusionMode;
            float _CloudOcclusionStrength;

            struct pointAppdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct pointV2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            pointV2f vertPoint(pointAppdata v)
            {
                pointV2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 fragPoint(pointV2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                // Cloud occlusion (mode 0 = untouched default path).
                if (_CloudOcclusionMode > 0.5 && _CloudOcclusionStrength > 0.0)
                {
                    float a;
                    if (_CloudOcclusionMode > 2.5)
                    {
                        // Mode 3: screen-space cloud opacity. The UniStorm
                        // composite writes min(cloudAlpha * 1.5, 1) into the
                        // alpha of the game's GBuffer3 target, so the fragment's
                        // own screen UV addresses exactly its cloud coverage.
                        float2 screenUv = i.pos.xy / _ScreenParams.xy;
                        a = tex2D(_CloudOcclusionTex, screenUv).a;
                    }
                    else
                    {
                        float3 dirW = normalize(i.worldPos - _WorldSpaceCameraPos);
                        float2 domeUv;
                        if (_CloudOcclusionMode < 1.5)
                        {
                            float3 dirView = mul(UNITY_MATRIX_V, float4(dirW, 0.0)).xyz;
                            domeUv = dirView.xy * 0.5 + 0.5;
                        }
                        else
                        {
                            domeUv = normalize(dirW).xy * 0.5 + 0.5;
                        }
                        a = tex2D(_CloudOcclusionTex, domeUv).a;
                    }
                    float occl = saturate(a * 1.5);
                    c.a *= (1.0 - occl * _CloudOcclusionStrength);
                }
                return c;
            }
            ENDCG
        }

        // 3) Same point sprite for the pipeline's Sky stage (DeepPipeline
        //    RenderSky, ShaderTagId "Sky", opaque queue range). Drawn BEFORE the
        //    forward/G-buffer stages so the UniStorm cloud composite (queue
        //    1051) blends over the celestial sprites.
        Pass
        {
            Name "PointSky"
            Tags { "LightMode" = "Sky" }
            CGPROGRAM
            #pragma vertex vertPoint
            #pragma fragment fragPoint
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            sampler2D _CloudOcclusionTex;
            float _CloudOcclusionMode;
            float _CloudOcclusionStrength;

            struct pointAppdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct pointV2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            pointV2f vertPoint(pointAppdata v)
            {
                pointV2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 fragPoint(pointV2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                // Cloud occlusion (mode 0 = untouched default path).
                if (_CloudOcclusionMode > 0.5 && _CloudOcclusionStrength > 0.0)
                {
                    float a;
                    if (_CloudOcclusionMode > 2.5)
                    {
                        // Mode 3: screen-space cloud opacity. The UniStorm
                        // composite writes min(cloudAlpha * 1.5, 1) into the
                        // alpha of the game's GBuffer3 target, so the fragment's
                        // own screen UV addresses exactly its cloud coverage.
                        float2 screenUv = i.pos.xy / _ScreenParams.xy;
                        a = tex2D(_CloudOcclusionTex, screenUv).a;
                    }
                    else
                    {
                        float3 dirW = normalize(i.worldPos - _WorldSpaceCameraPos);
                        float2 domeUv;
                        if (_CloudOcclusionMode < 1.5)
                        {
                            float3 dirView = mul(UNITY_MATRIX_V, float4(dirW, 0.0)).xyz;
                            domeUv = dirView.xy * 0.5 + 0.5;
                        }
                        else
                        {
                            domeUv = normalize(dirW).xy * 0.5 + 0.5;
                        }
                        a = tex2D(_CloudOcclusionTex, domeUv).a;
                    }
                    float occl = saturate(a * 1.5);
                    c.a *= (1.0 - occl * _CloudOcclusionStrength);
                }
                return c;
            }
            ENDCG
        }

        // 4) Sternspreiz streak (lens only, drawn with an explicit pass index).
        Pass
        {
            Name "Streak"
            Tags { "LightMode" = "RadioNavStreakReference" }
            Blend SrcAlpha One
            ZWrite Off
            CGPROGRAM
            #pragma vertex vertStreak
            #pragma fragment fragStreak
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Softness;
            float _EndTaper;
            float _WidthTaper;
            float _MinWidth;

            struct streakAppdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct streakV2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            streakV2f vertStreak(streakAppdata v)
            {
                streakV2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 fragStreak(streakV2f i) : SV_Target
            {
                // Ends taper to a very thin tip: the band half-width shrinks
                // along the line, floored so the tip stays visible.
                float along = abs(i.uv.x * 2.0 - 1.0);              // 0 centre, 1 ends
                float shrink = pow(saturate(1.0 - along), max(0.0, _WidthTaper));
                float relWidth = max(_MinWidth, shrink);
                float across = abs(i.uv.y * 2.0 - 1.0) / relWidth;  // normalised across the line
                fixed4 prof = tex2D(_MainTex, float2(saturate(i.uv.x), 0.5));
                float band = 1.0 - smoothstep(0.0, max(0.0001, _Softness), across);
                float taper = smoothstep(0.0, max(0.0001, _EndTaper), i.uv.x)
                            * smoothstep(0.0, max(0.0001, _EndTaper), 1.0 - i.uv.x);
                fixed4 outc;
                outc.rgb = prof.rgb * i.color.rgb;
                outc.a = prof.a * i.color.a * band * taper;
                return outc;
            }
            ENDCG
        }
    }
}
