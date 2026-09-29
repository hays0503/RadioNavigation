// RadioNavigation Sternspreiz star shader.
//
// Two roles, three passes:
//  - "Point" (untagged, SRPDefaultUnlit) and "PointForward" (LightMode=ForwardSRP)
//    draw the mod's point-star sprites on scene renderers. The game's Deep
//    pipeline draws scene renderers by shader tag, so a sprite shader without a
//    matching pass never appears (live 2026-09-28: StarsMesh contributed zero
//    pixels). Blend comes from the material's _SrcBlend/_DstBlend, which is how
//    StarsMesh asks for additive star light.
//  - "Streak" draws the Sternspreiz line. The lens draws this pass EXPLICITLY
//    through the PIP command buffer (DrawMesh pass index 2), so the scene
//    passes above never need to know about it.
// The line profile/texture and the band shaping are the mod's tested PSF math.
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

        // 1) Scene point sprite, default pass name.
        Pass
        {
            Name "Point"
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
            Tags { "LightMode" = "ForwardSRP" }
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

        // 3) Sternspreiz streak (lens only, drawn with an explicit pass index).
        Pass
        {
            Name "Streak"
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
