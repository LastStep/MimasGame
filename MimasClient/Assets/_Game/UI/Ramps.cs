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

        /// <summary>
        /// A vertical line that fades to nothing over <paramref name="fade"/> of its height at both ends: the room's
        /// divider between the two seats (docs/ui/lobby.md §3 <c>room.divider</c>: 20%).
        /// </summary>
        public static Texture2D BothEndsVertical(float fade = 0.2f)
        {
            string key = "v2" + fade.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            Texture2D texture;
            if (Cache.TryGetValue(key, out texture) && texture != null) return texture;

            const int length = RampLength * 2;
            texture = New(1, length);
            var pixels = new Color[length];
            for (int y = 0; y < length; y++)
            {
                float v = (y + 0.5f) / length;
                float edge = Mathf.Min(v, 1f - v);
                pixels[y] = new Color(1f, 1f, 1f, Mathf.Clamp01(edge / fade));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            Cache[key] = texture;
            return texture;
        }

        /// <summary>
        /// The lobby's ground (docs/ui/lobby.md §2 <c>lb.wash</c>): up to three soft elliptical washes in the lineages'
        /// dark hues — the first low left at 35%, the second high right at 28%, the third low centre-right at 30% —
        /// each fading to nothing halfway to the farthest corner. The hues are data, in catalogue order; only the
        /// placement lives here.
        /// <para>
        /// Mixed onto <paramref name="ground"/> here and opaque, not laid over it translucent: the project renders in
        /// linear space, where a 35% wash blends half as bright again as the canvas drew it (measured 24 Sep 2026,
        /// T-0014). Mixing the gamma values the way the canvas does keeps the lobby the colour Rohan chose.
        /// </para>
        /// </summary>
        public static Texture2D LobbyWash(IList<Color> hues, Color ground)
        {
            var key = new System.Text.StringBuilder("w" + ColorUtility.ToHtmlStringRGB(ground));
            for (int i = 0; i < hues.Count && i < Washes.Length; i++) key.Append(ColorUtility.ToHtmlStringRGB(hues[i]));
            Texture2D texture;
            if (Cache.TryGetValue(key.ToString(), out texture) && texture != null) return texture;

            // Soft enough that 256×144 stretched over the window never shows a texel.
            const int w = 256, h = 144;
            int count = Mathf.Min(hues.Count, Washes.Length);
            texture = New(w, h);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                // Texture rows run bottom-up; v is 0 at the top of the screen.
                float v = 1f - (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    // Premultiplied "over", the last layer first, as CSS stacks background layers.
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int i = count - 1; i >= 0; i--)
                    {
                        Vector3 wash = Washes[i];
                        float alpha = wash.z * EllipseFade(u, v, wash.x, wash.y);
                        Color hue = hues[i];
                        r = hue.r * alpha + r * (1f - alpha);
                        g = hue.g * alpha + g * (1f - alpha);
                        b = hue.b * alpha + b * (1f - alpha);
                        a = alpha + a * (1f - alpha);
                    }
                    // Then the whole stack over the ground, still in gamma values, and dithered against banding.
                    float dither = (Hash(x, y) - 0.5f) / 255f;
                    pixels[y * w + x] = new Color(
                        r + ground.r * (1f - a) + dither,
                        g + ground.g * (1f - a) + dither,
                        b + ground.b * (1f - a) + dither,
                        1f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            Cache[key.ToString()] = texture;
            return texture;
        }

        /// <summary>Where the lobby's washes sit (x, y from the top left, 0..1) and how strong each is (z).</summary>
        private static readonly Vector3[] Washes =
        {
            new Vector3(0.22f, 0.70f, 0.35f),
            new Vector3(0.80f, 0.25f, 0.28f),
            new Vector3(0.60f, 0.90f, 0.30f),
        };

        /// <summary>
        /// A CSS <c>radial-gradient(ellipse at cx cy, colour, transparent 50%)</c>: 1 at the centre, 0 halfway to the
        /// ellipse through the farthest corner (whose axes keep the farthest sides' ratio).
        /// </summary>
        private static float EllipseFade(float u, float v, float cu, float cv)
        {
            float rx = Mathf.Max(cu, 1f - cu) * 1.41421356f;
            float ry = Mathf.Max(cv, 1f - cv) * 1.41421356f;
            float du = (u - cu) / rx, dv = (v - cv) / ry;
            float d = Mathf.Sqrt(du * du + dv * dv) / 0.5f;
            return Mathf.Clamp01(1f - d);
        }

        /// <summary>
        /// A lineage's swatch (docs/ui/lobby.md §3 <c>room.lineage[i]</c>, the 52 square): the light hue glowing from
        /// a third of the way in, into the dark hue at three quarters of the way to the far corner.
        /// </summary>
        public static Texture2D Swatch(Color dark, Color light)
        {
            string key = "s" + ColorUtility.ToHtmlStringRGB(dark) + ColorUtility.ToHtmlStringRGB(light);
            Texture2D texture;
            if (Cache.TryGetValue(key, out texture) && texture != null) return texture;

            const int size = 64;
            const float cu = 0.35f, cv = 0.35f;
            float far = Mathf.Sqrt((1f - cu) * (1f - cu) + (1f - cv) * (1f - cv)) * 0.75f;
            texture = New(size, size);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = 1f - (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float t = Mathf.Clamp01(Mathf.Sqrt((u - cu) * (u - cu) + (v - cv) * (v - cv)) / far);
                    Color c = Color.Lerp(light, dark, t);
                    float dither = (Hash(x, y) - 0.5f) / 255f;
                    c.r += dither; c.g += dither; c.b += dither;
                    c.a = 1f;
                    pixels[y * size + x] = c;
                }
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
