// Explicitly Blit-only optics for the right sextant PIP while inside the boat.
// No LightMode tag: this shader is never submitted to the celestial pipeline,
// so outdoor PointSky/cloud ordering cannot be affected by these passes.
Shader "RadioNav/SternspreizInteriorOptics"
{
    Properties
    {
        _MainTex ("PIP source", 2D) = "white" {}
        _BrightThreshold ("Highlight threshold", Range(0, 2)) = 0.68
        _BrightKnee ("Threshold softness", Range(0.01, 1)) = 0.24
        _CompactnessStart ("Compact highlight contrast start", Range(0, 0.5)) = 0.04
        _CompactnessEnd ("Compact highlight contrast full", Range(0.01, 0.8)) = 0.24
        _CompactnessRadiusPixels ("Extended-source guard radius (pixels)", Float) = 16
        _StreakHalfLengthTexels ("Astigmatic half-length, half-res texels", Float) = 5
        _StreakHalfThicknessTexels ("Astigmatic half-thickness, half-res texels", Float) = 0.65
        _StreakDirection ("Astigmatic direction", Vector) = (1,0,0,0)
        _StreakEdgeFraction ("Soft streak end fraction", Range(0.01, 0.5)) = 0.18
        _SceneBlur ("Subtle scene diffusion", Range(0, 0.25)) = 0.12
        _GlowIntensity ("Astigmatic highlight intensity", Range(0, 4)) = 3.2
        _GlowClamp ("Highlight luminance cap", Range(0, 1)) = 0.14
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        // Pass 0: threshold highlights before downsampling so small lamps
        // survive the half-resolution glow buffer.
        Pass
        {
            Name "BrightDownsample"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragBright
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _BrightThreshold;
            float _BrightKnee;
            float _CompactnessStart;
            float _CompactnessEnd;
            float _CompactnessRadiusPixels;

            float Peak(float3 color)
            {
                return max(color.r, max(color.g, color.b));
            }

            float3 ExtractHighlight(float2 uv)
            {
                // Pick the brightest sample from the source footprint. A local
                // contrast gate keeps a large, nearly uniform sun/moon disc from
                // being turned into a long bar, while a compact lamp core passes.
                float2 d = _MainTex_TexelSize.xy * 0.5;
                float3 c0 = tex2D(_MainTex, uv + float2(-d.x, -d.y)).rgb;
                float3 c1 = tex2D(_MainTex, uv + float2(d.x, -d.y)).rgb;
                float3 c2 = tex2D(_MainTex, uv + float2(-d.x, d.y)).rgb;
                float3 c3 = tex2D(_MainTex, uv + float2(d.x, d.y)).rgb;
                float p0 = Peak(c0);
                float p1 = Peak(c1);
                float p2 = Peak(c2);
                float p3 = Peak(c3);
                float peak = p0;
                float3 source = c0;
                if (p1 > peak) { peak = p1; source = c1; }
                if (p2 > peak) { peak = p2; source = c2; }
                if (p3 > peak) { peak = p3; source = c3; }
                float2 radius = _MainTex_TexelSize.xy * _CompactnessRadiusPixels;
                float localMean = (
                    Peak(tex2D(_MainTex, uv + float2(radius.x, 0.0)).rgb)
                    + Peak(tex2D(_MainTex, uv - float2(radius.x, 0.0)).rgb)
                    + Peak(tex2D(_MainTex, uv + float2(0.0, radius.y)).rgb)
                    + Peak(tex2D(_MainTex, uv - float2(0.0, radius.y)).rgb)) * 0.25;
                float compactness = smoothstep(_CompactnessStart, _CompactnessEnd,
                    max(0.0, peak - localMean));
                float knee = max(_BrightKnee, 0.001);
                float t = saturate((peak - _BrightThreshold + knee) / (2.0 * knee));
                float mask = t * t * (3.0 - 2.0 * t);
                return max(source - _BrightThreshold, 0.0) * mask * compactness;
            }

            float4 fragBright(v2f_img i) : SV_Target
            {
                return float4(ExtractHighlight(i.uv), 1.0);
            }
            ENDCG
        }

        // Pass 1: horizontal Gaussian spread in the reduced-resolution buffer.
        Pass
        {
            Name "BlurHorizontal"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragBlurHorizontal
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _StreakDirection;
            float _StreakHalfLengthTexels;
            float _StreakEdgeFraction;

            float4 fragBlurHorizontal(v2f_img i) : SV_Target
            {
                float2 direction = normalize(_StreakDirection.xy);
                float2 extent = direction * _MainTex_TexelSize.xy * _StreakHalfLengthTexels;
                float3 sum = 0.0;
                float weightSum = 0.0;
                [unroll]
                for (int tap = 0; tap < 25; tap++)
                {
                    float t = (tap / 24.0) * 2.0 - 1.0;
                    float weight = smoothstep(0.0, max(_StreakEdgeFraction, 0.001),
                        1.0 - abs(t));
                    sum += tex2D(_MainTex, i.uv + extent * t).rgb * weight;
                    weightSum += weight;
                }
                return float4(sum / max(weightSum, 0.001), 1.0);
            }
            ENDCG
        }

        // Pass 2: vertical Gaussian spread.
        Pass
        {
            Name "BlurVertical"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragBlurVertical
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _StreakDirection;
            float _StreakHalfThicknessTexels;

            float4 fragBlurVertical(v2f_img i) : SV_Target
            {
                float2 direction = normalize(_StreakDirection.xy);
                float2 perpendicular = float2(-direction.y, direction.x);
                float2 d = perpendicular * _MainTex_TexelSize.xy
                    * max(_StreakHalfThicknessTexels, 0.5);
                float4 c = tex2D(_MainTex, i.uv) * 0.227027;
                c += tex2D(_MainTex, i.uv + d * 0.553) * 0.316216;
                c += tex2D(_MainTex, i.uv - d * 0.553) * 0.316216;
                c += tex2D(_MainTex, i.uv + d * 1.292) * 0.070270;
                c += tex2D(_MainTex, i.uv - d * 1.292) * 0.070270;
                return c;
            }
            ENDCG
        }

        // Pass 3: preserve source cores/discs and add the opt-in astigmatic
        // highlight line with a restrained local diffusion.
        Pass
        {
            Name "Composite"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragComposite
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _RadioNavGlowTex;
            float4 _MainTex_TexelSize;
            float _SceneBlur;
            float _GlowIntensity;
            float _GlowClamp;

            float4 fragComposite(v2f_img i) : SV_Target
            {
                float4 source = tex2D(_MainTex, i.uv);
                float2 d = _MainTex_TexelSize.xy;
                float3 nearby = tex2D(_MainTex, i.uv + float2(d.x, 0.0)).rgb
                    + tex2D(_MainTex, i.uv - float2(d.x, 0.0)).rgb
                    + tex2D(_MainTex, i.uv + float2(0.0, d.y)).rgb
                    + tex2D(_MainTex, i.uv - float2(0.0, d.y)).rgb;
                float3 softened = source.rgb * 0.5 + nearby * 0.125;
                float3 glow = tex2D(_RadioNavGlowTex, i.uv).rgb * _GlowIntensity;
                glow = min(glow, _GlowClamp.xxx);
                float3 result = lerp(source.rgb, softened, saturate(_SceneBlur)) + glow;
                return float4(result, source.a);
            }
            ENDCG
        }
    }
}
