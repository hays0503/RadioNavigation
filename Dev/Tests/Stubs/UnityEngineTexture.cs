namespace UnityEngine
{
    public struct Color32
    {
        public byte r;
        public byte g;
        public byte b;
        public byte a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }

    public enum TextureFormat
    {
        RGBA32 = 4
    }

    public enum FilterMode
    {
        Point = 0,
        Bilinear = 1,
        Trilinear = 2
    }

    public enum TextureWrapMode
    {
        Repeat = 0,
        Clamp = 1
    }

    public class Texture2D
    {
        public int width;
        public int height;
        public TextureFormat format;
        public bool mipChain;
        public Color32[] pixels;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;

        public Texture2D(int width, int height, TextureFormat format, bool mipChain)
        {
            this.width = width;
            this.height = height;
            this.format = format;
            this.mipChain = mipChain;
        }

        public void SetPixels32(Color32[] colors)
        {
            pixels = colors;
        }

        public void Apply()
        {
        }
    }
}
