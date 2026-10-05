using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class KeyingSegment
    {
        public double StartMs;
        public double DurationMs;
        public bool IsOn;
    }

    // Offline envelope detector for rendered keying audio: RMS windows,
    // adaptive threshold (noise floor percentile x4 vs peak fraction),
    // debounce + merge, mirroring Tools/pause_check/analyze_pause.py so
    // in-test assertions and the standalone script agree.
    public static class KeyingEnvelope
    {
        public const int WindowMs = 10;
        private const int MinRunMs = 30;

        public static List<KeyingSegment> Analyze(float[] samples, int sampleRate)
        {
            int win = Math.Max(1, sampleRate * WindowMs / 1000);
            int windowCount = samples.Length / win;
            var env = new float[windowCount];
            for (int w = 0; w < windowCount; w++)
            {
                double acc = 0;
                int baseIndex = w * win;
                for (int i = 0; i < win; i++)
                {
                    float v = samples[baseIndex + i];
                    acc += v * v;
                }
                env[w] = (float)Math.Sqrt(acc / win);
            }

            var ordered = (float[])env.Clone();
            Array.Sort(ordered);
            float floor = windowCount > 20 ? ordered[windowCount / 5] : 0f;
            float peak = ordered[windowCount - 1];
            float threshold = Math.Max(floor * 4f, peak * 0.03f);

            var raw = new List<int[]>();
            if (windowCount > 0)
            {
                bool currentOn = env[0] >= threshold;
                int runStart = 0;
                for (int i = 1; i <= windowCount; i++)
                {
                    bool on = i < windowCount && env[i] >= threshold;
                    if (on != currentOn)
                    {
                        raw.Add(new[] { runStart, i - runStart, currentOn ? 1 : 0 });
                        runStart = i;
                        currentOn = on;
                    }
                }
                raw.Add(new[] { runStart, windowCount - runStart, currentOn ? 1 : 0 });
            }

            int minRun = MinRunMs / WindowMs;
            var merged = new List<int[]>();
            foreach (var s in raw)
            {
                if (merged.Count > 0)
                {
                    var last = merged[merged.Count - 1];
                    if (last[2] == s[2])
                    {
                        last[1] = s[0] + s[1] - last[0];
                        continue;
                    }
                    if (s[1] < minRun)
                    {
                        last[1] = s[0] + s[1] - last[0];
                        continue;
                    }
                }
                merged.Add(new[] { s[0], s[1], s[2] });
            }

            var result = new List<KeyingSegment>();
            foreach (var m in merged)
            {
                if (m[1] < minRun)
                    continue;
                result.Add(new KeyingSegment
                {
                    StartMs = m[0] * (double)WindowMs,
                    DurationMs = m[1] * (double)WindowMs,
                    IsOn = m[2] == 1
                });
            }
            return result;
        }

        public static string Describe(List<KeyingSegment> segments, int maxRows)
        {
            var sb = new StringBuilder();
            foreach (var s in segments.OrderByDescending(x => x.DurationMs).Take(maxRows))
                sb.Append(s.IsOn ? "ON " : "OFF")
                  .Append(" start=").Append(s.StartMs.ToString("F0"))
                  .Append("ms dur=").Append(s.DurationMs.ToString("F0"))
                  .AppendLine("ms");
            return sb.ToString();
        }
    }
}
