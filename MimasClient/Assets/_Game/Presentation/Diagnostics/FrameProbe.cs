using System;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mimas.Client.Presentation.Diagnostics
{
    /// <summary>
    /// A frame-time meter that lives inside the game, because nothing outside it could answer the
    /// question.
    ///
    /// <para>Rohan played the Web build and said it "felt like it was dropping frames". A Playwright
    /// harness could not confirm it: headless Chromium is a software rasteriser, and headed Chromium
    /// drives requestAnimationFrame at its own ceiling rather than the display's, so it reproduces
    /// neither his GPU nor his 144 Hz vsync. What it did establish is that an *idle* arena renders in
    /// under 4.17 ms even at 2560x1440 — roughly four times the headroom a 144 Hz frame needs. So
    /// whatever he felt is transient and happens while playing: a shader meeting the GPU for the first
    /// time, a collection, an allocation on an event.</para>
    ///
    /// <para>Off unless asked for. <c>?perf=1</c> on the page URL turns it on, which matters because
    /// keyboard input does not reach the Web build at all until the editor upgrade lands (Unity issue
    /// 4006), so a key toggle would be unusable in the one place this is needed.</para>
    ///
    /// <para>It installs itself from <see cref="RuntimeInitializeOnLoadMethod"/> rather than living on
    /// a GameObject: a scene edit needs the live Editor (golden rule 2), and a diagnostic should not
    /// need one. It survives scene loads, so a lobby-to-arena transition is measured too — which is
    /// exactly where a first-use shader compile would land.</para>
    /// </summary>
    public sealed class FrameProbe : MonoBehaviour
    {
        /// <summary>A frame worth naming. 50 ms is roughly where a human stops calling it smooth.</summary>
        private const float LongFrameMs = 50f;

        /// <summary>The rolling window the overlay reports its worst frame over.</summary>
        private const float WindowSeconds = 5f;

        private const int HistogramBuckets = 6;   // <8, <17, <34, <50, <100, 100+

        private readonly int[] _buckets = new int[HistogramBuckets];
        private float _windowWorstMs;
        private float _windowEndsAt;
        private float _reportedWorstMs;
        private float _smoothedMs = 16.7f;
        private int _longFrames;
        private int _frames;
        private int _gcAtStart;
        private int _lastGcCount;
        private GUIStyle _style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (!Wanted()) return;

            var go = new GameObject("~FrameProbe");
            go.AddComponent<FrameProbe>();
            DontDestroyOnLoad(go);
            Debug.Log("[FrameProbe] on — long frames over " + LongFrameMs + " ms will be logged");
        }

        /// <summary>
        /// <c>?perf=1</c> in the page URL on Web; the <c>MIMAS_PERF</c> environment variable elsewhere,
        /// so the same probe can be switched on in the Editor without editing a scene.
        /// </summary>
        private static bool Wanted()
        {
            string url = Application.absoluteURL;
            if (!string.IsNullOrEmpty(url) && url.IndexOf("perf=1", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

#if !UNITY_WEBGL || UNITY_EDITOR
            string flag = Environment.GetEnvironmentVariable("MIMAS_PERF");
            if (!string.IsNullOrEmpty(flag) && flag != "0") return true;
#endif
            return false;
        }

        private void Awake()
        {
            _gcAtStart = GC.CollectionCount(0);
            _lastGcCount = _gcAtStart;
            _windowEndsAt = Time.unscaledTime + WindowSeconds;
        }

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            _frames++;

            // An exponential average, so the number on screen is readable rather than a blur.
            _smoothedMs += (ms - _smoothedMs) * 0.05f;

            if (ms > _windowWorstMs) _windowWorstMs = ms;
            Bucket(ms);

            if (ms >= LongFrameMs)
            {
                _longFrames++;

                // A collection between this frame and the last is the difference between "the GPU was
                // busy" and "the collector ran", and it is the single most useful bit to log.
                int gc = GC.CollectionCount(0);
                bool collected = gc != _lastGcCount;
                _lastGcCount = gc;

                Debug.LogWarning(string.Format(
                    "[FrameProbe] long frame {0:F1} ms  scene={1}  t={2:F1}s  gc={3}  totalLong={4}",
                    ms, SceneManager.GetActiveScene().name, Time.unscaledTime,
                    collected ? "YES" : "no", _longFrames));
            }
            else
            {
                _lastGcCount = GC.CollectionCount(0);
            }

            if (Time.unscaledTime >= _windowEndsAt)
            {
                _reportedWorstMs = _windowWorstMs;
                _windowWorstMs = 0f;
                _windowEndsAt = Time.unscaledTime + WindowSeconds;
            }
        }

        private void Bucket(float ms)
        {
            int i = ms < 8f ? 0 : ms < 17f ? 1 : ms < 34f ? 2 : ms < 50f ? 3 : ms < 100f ? 4 : 5;
            _buckets[i]++;
        }

        private void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    alignment = TextAnchor.UpperLeft,
                    richText = false,
                };
                _style.normal.textColor = Color.white;
            }

            var text = new StringBuilder(256);
            text.Append("fps ").Append(Mathf.RoundToInt(1000f / Mathf.Max(0.01f, _smoothedMs)));
            text.Append("   frame ").Append(_smoothedMs.ToString("F1")).Append(" ms");
            text.Append("\nworst in ").Append((int)WindowSeconds).Append("s: ")
                .Append(_reportedWorstMs.ToString("F1")).Append(" ms");
            text.Append("\nover ").Append((int)LongFrameMs).Append(" ms: ").Append(_longFrames)
                .Append(" of ").Append(_frames);
            text.Append("\ngc: ").Append(GC.CollectionCount(0) - _gcAtStart);
            text.Append("   mono: ").Append((GC.GetTotalMemory(false) / 1048576f).ToString("F1")).Append(" MB");
            text.Append("\n<8 ").Append(_buckets[0]).Append("  <17 ").Append(_buckets[1])
                .Append("  <34 ").Append(_buckets[2]).Append("  <50 ").Append(_buckets[3])
                .Append("  <100 ").Append(_buckets[4]).Append("  100+ ").Append(_buckets[5]);

            const float w = 260f, h = 92f;
            var box = new Rect(8f, 8f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + 8f, box.y + 6f, w - 16f, h - 12f), text.ToString(), _style);
        }
    }
}
