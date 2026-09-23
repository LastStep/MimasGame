using System.Collections.Generic;
using UnityEngine;

namespace Mimas.Client.UI
{
    /// <summary>
    /// The fades USS cannot draw (it has no gradients): small textures generated once, white with an alpha
    /// ramp, that a stylesheet tints with <c>-unity-background-image-tint-color: var(--mimas-…)</c> — so the
    /// colour stays a token and only the shape of the fade lives here (spec H §3). The one exception is the
    /// lineage painting, whose hues are data. Every texture is cached by its parameters for the life of the
    /// app, which is a handful of small ones; <see cref="Release"/> frees them.
    /// </summary>
    internal static class Ramps
    {
        /// <summary>64 texels of ramp is enough not to band at any window size (spec H §13).</summary>
        private const int RampLength = 64;

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// A vertical fade, opaque at one end: the floors under the bar (86% → 50% → nothing, so
        /// <paramref name="midAlpha"/> 0.58 of the 86% tint) and under the track (a plain fade).
        /// </summary>
        public static Texture2D Vertical(bool opaqueAtTop, float midAlpha = 0.5f)
        {
            string key = "v" + (opaqueAtTop ? "t" : "b") + midAlpha.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            Texture2D texture;
            if (Cache.TryGetValue(key, out texture) && texture != null) return texture;

            texture = New(1, RampLength);
            var pixels = new Color[RampLength];
            for (int y = 0; y < RampLength; y++)
            {
                // Texture rows run bottom-up; t is 0 at the opaque end.
                float fromBottom = (y + 0.5f) / RampLength;
                float t = opaqueAtTop ? 1f - fromBottom : fromBottom;
                float a = t < 0.5f ? Mathf.Lerp(1f, midAlpha, t / 0.5f) : Mathf.Lerp(midAlpha, 0f, (t - 0.5f) / 0.5f);
                pixels[y] = new Color(1f, 1f, 1f, a);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            Cache[key] = texture;
            return texture;
        }

        /// <summary>
        /// A horizontal band that fades to nothing over <paramref name="fade"/> of its width at both ends: the
        /// round moments' band (language §1 <c>band</c>: 22%).
        /// </summary>
        public static Texture2D BothEnds(float fade = 0.22f)
        {
            string key = "h2" + fade.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            Texture2D texture;
            if (Cache.TryGetValue(key, out texture) && texture != null) return texture;

            const int length = RampLength * 2;
            texture = New(length, 1);
            var pixels = new Color[length];
            for (int x = 0; x < length; x++)
            {
                float u = (x + 0.5f) / length;
                float edge = Mathf.Min(u, 1f - u);
                pixels[x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / fade)));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            Cache[key] = texture;
            return texture;
        }

        /// <summary>White with alpha rising from nothing on the left to opaque on the right: the plate's wash on the board.</summary>
        public static Texture2D LeftToRight()
        {
            const string key = "lr";
            Texture2D texture;
            if (Cache.TryGetValue(key, out texture) && texture != null) return texture;

            texture = New(RampLength, 1);
            var pixels = new Color[RampLength];
            for (int x = 0; x < RampLength; x++) pixels[x] = new Color(1f, 1f, 1f, (x + 0.5f) / RampLength);
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            Cache[key] = texture;
            return texture;
        }

        /// <summary>
        /// The lineage painting (docs/ui/examine.md §3.1): three layered radial washes in the lineage's two hues,
        /// the lower 60% fading into <paramref name="ground"/> — the plate's ink, or a draft card's ink-2.
        /// </summary>
        public static Texture2D Painting(Color dark, Color light, Color ground)
        {
            string key = "p" + ColorUtility.ToHtmlStringRGB(dark) + ColorUtility.ToHtmlStringRGB(light) + ColorUtility.ToHtmlStringRGB(ground);
            Texture2D texture;
            if (Cache.TryGetValue(key, out texture) && texture != null) return texture;

            // Big enough that the plate never magnifies it more than ~2x at 1080 (a 96×64 texture was visibly
            // soft at large windows), dithered by half a level so the long dark-to-ground ramp does not band.
            const int w = 224, h = 112;
            texture = New(w, h);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                // Texture rows run bottom-up; v is 0 at the top of the painting.
                float v = 1f - (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    // A light glow left of centre, a dark pool top right, a softer glow low left; then the lower
                    // 60% fades into the ground.
                    float glow = Radial(u, v, 0.36f, 0.30f, 0.62f);
                    float pool = Radial(u, v, 0.92f, 0.05f, 0.55f);
                    float low = Radial(u, v, 0.10f, 0.72f, 0.45f) * 0.5f;
                    Color c = Color.Lerp(dark, light, Mathf.Clamp01(glow * 0.85f + low));
                    c = Color.Lerp(c, dark * 0.85f, pool * 0.6f);
                    float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 1.0f, v));
                    c = Color.Lerp(c, ground, fade);
                    float dither = (Hash(x, y) - 0.5f) / 255f;
                    c.r += dither; c.g += dither; c.b += dither;
                    c.a = 1f;
                    pixels[y * w + x] = c;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            Cache[key] = texture;
            return texture;
        }

        /// <summary>Destroys every cached texture; the next request makes it again.</summary>
        public static void Release()
        {
            foreach (Texture2D texture in Cache.Values)
                if (texture != null) Object.Destroy(texture);
            Cache.Clear();
        }

        private static Texture2D New(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
        }

        /// <summary>A fixed 0..1 value per pixel, for dithering; deterministic so the painting never shimmers.</summary>
        private static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }

        private static float Radial(float u, float v, float cu, float cv, float radius)
        {
            float du = u - cu, dv = (v - cv) * 0.66f;
            float d = Mathf.Sqrt(du * du + dv * dv) / radius;
            return Mathf.Clamp01(1f - d * d);
        }
    }
}
