using System;
using System.IO;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public static class WavWriter
    {
        public static void WritePcm16Mono(string path, float[] samples, int sampleRate)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                int dataBytes = samples.Length * 2;
                writer.Write(0x46464952);
                writer.Write(36 + dataBytes);
                writer.Write(0x45564157);
                writer.Write(0x20746D66);
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(0x61746164);
                writer.Write(dataBytes);
                foreach (float s in samples)
                {
                    int scaled = (int)Math.Round(s * 32767f);
                    if (scaled > 32767) scaled = 32767;
                    if (scaled < -32768) scaled = -32768;
                    writer.Write((short)scaled);
                }
            }
        }
    }
}
