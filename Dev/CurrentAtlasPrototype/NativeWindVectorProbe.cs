using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using UBOAT.Mods.RadioNavigation;

// Offline parity of the base-wind subset, not a weather simulation.
internal static class NativeWindVectorProbe
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const float Knot = 1852f / 3600f;
    private delegate float Perlin(ref Vector3 value);

    [Serializable]
    private sealed class Input { public WeatherInput weather; }
    [Serializable]
    private sealed class WeatherInput { public Curve windSpeedProbabilityCurve; }
    [Serializable]
    private sealed class Curve { public Key[] m_Curve; }
    [Serializable]
    private sealed class Key
    {
        public float time, value, inSlope, outSlope, inWeight, outWeight;
        public int weightedMode;
        public Keyframe ToUnity() => new Keyframe(time, value, inSlope, outSlope, inWeight, outWeight)
        { weightedMode = (WeightedMode)weightedMode };
    }

    public static void Run(string output)
    {
        new NativeGameBridge(137);
        var weatherType = NativeGameBridge.Game.GetType("UBOAT.Game.Scene.Environment.Weather", true);
        object weather = FormatterServices.GetUninitializedObject(weatherType);
        var next = (Perlin)Delegate.CreateDelegate(typeof(Perlin), weather, weatherType.GetMethod("NextPerlin", Members));
        var source = JsonUtility.FromJson<Input>(File.ReadAllText(Path.Combine(Application.dataPath, "Inputs/weather-scene.json")));
        var curve = new AnimationCurve(Array.ConvertAll(source.weather.windSpeedProbabilityCurve.m_Curve, key => key.ToUnity()));
        var parameter = NativeGameBridge.Game.GetType("UBOAT.Game.Core.Data.Parameter", true);
        object speed = Activator.CreateInstance(parameter, "WindSpeed");
        var add = parameter.GetMethod("AddDeltaModifier", new[] { typeof(string), typeof(bool) });
        object baseModifier = add.Invoke(speed, new object[] { "Base", false });
        object delta = add.Invoke(speed, new object[] { "Weather probe delta", false });
        var value = baseModifier.GetType().GetProperty("Value");
        var resolved = parameter.GetProperty("Value");
        var withOverride = parameter.GetMethod("GetValueWithModifierOverride", Members);
        value.SetValue(delta, 1.75f);
        value.SetValue(baseModifier, 6f);
        float maxAngle = 0, maxKnots = 0;
        int checkedCount = 0;
        foreach (float seed in new[] { 0f, 137f, 445f })
        {
            NativeGameBridge.Set(weather, "weatherSeed", seed);
            for (int i = 0; i < 64; i++)
            {
                float t = i * 0.691f, east = -4000 + i * 29f, north = 5600 - i * 57f;
                var coords = new Vector3(t, east * .00015f + t, north * .00015f - t);
                float originalBase = 40f * curve.Evaluate(next(ref coords));
                for (int k = 1; k < 9; k++) next(ref coords);
                coords *= .25f;
                float originalAngle = 360f * next(ref coords);
                float mirrorAngle = SceneWindTrace.Angle(t, east, north, seed);
                maxAngle = Mathf.Max(maxAngle, Mathf.Abs(Mathf.DeltaAngle(originalAngle, mirrorAngle)));
                foreach (float forced in new[] { -2f, 0f, 0.5f, 32f })
                {
                    value.SetValue(baseModifier, forced > 0f ? forced : originalBase);
                    float stock = (float)resolved.GetValue(speed);
                    value.SetValue(baseModifier, 6f);
                    float baseKnots = forced > 0f ? forced : 40f * curve.Evaluate(SceneWindTrace.SpeedNoise(t, east, north, seed));
                    float candidate = (float)withOverride.Invoke(speed, new[] { baseModifier, (object)baseKnots });
                    maxKnots = Mathf.Max(maxKnots, Mathf.Abs(stock - candidate));
                    if ((float)value.GetValue(baseModifier) != 6f)
                        throw new InvalidOperationException("Wind hypothetical query mutated the current modifier");
                    checkedCount++;
                }
            }
        }
        if (checkedCount != 768 || maxKnots > 0.0001f || maxAngle > 0.0001f)
            throw new InvalidOperationException("Base wind vector parity failed: " + maxKnots + " / " + maxAngle);
        if (Mathf.Abs(40f * Knot - 20.577778f) > 0.00001f)
            throw new InvalidOperationException("Wind knots conversion changed");
        foreach (Vector3 heading in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
        {
            Vector3 converted = CurrentSurfaceDragStep.SceneWind(heading + Vector3.up, 40f);
            if (Vector3.Distance(converted, heading * 20.577778f) > 0.00001f)
                throw new InvalidOperationException("Scene wind lost its world direction or knots scale");
        }
        File.WriteAllText(Path.Combine(output, "wind-vector.csv"), string.Format(CultureInfo.InvariantCulture,
            "cases,maxKnotsError,maxAngleErrorDegrees\n{0},{1:R},{2:R}\n", checkedCount, maxKnots, maxAngle));
    }
}
