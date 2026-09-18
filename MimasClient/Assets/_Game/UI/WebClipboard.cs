#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace Mimas.Client.UI
{
    /// <summary>
    /// Puts text on the clipboard the player can actually paste from.
    ///
    /// <para><c>GUIUtility.systemCopyBuffer</c> is correct everywhere except the one platform Mimas
    /// ships on. On Web it writes to a buffer internal to Unity, so "Copy link" set it, logged the
    /// link and told the player it had copied — while the browser clipboard kept whatever was in it.
    /// Proven by <c>tools/smoke/clipboard-check.mjs</c>, which seeds a sentinel and finds it
    /// untouched afterwards.</para>
    ///
    /// <para>In the Editor and anywhere else, <c>systemCopyBuffer</c> is still the right answer and is
    /// what this uses.</para>
    /// </summary>
    public static class WebClipboard
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int MimasCopyToClipboard(string text);
#endif

        /// <summary>
        /// Copies <paramref name="text"/>. Returns false only when the platform refused outright —
        /// true means a working route was taken, not that the bytes have landed, because the browser's
        /// clipboard write is asynchronous and can still be denied after this returns.
        /// </summary>
        public static bool Copy(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

#if UNITY_WEBGL && !UNITY_EDITOR
            // Kept as well as the jslib: harmless, and it keeps anything that reads systemCopyBuffer
            // back (a test, a future in-game paste) seeing the same value.
            GUIUtility.systemCopyBuffer = text;
            return MimasCopyToClipboard(text) != 0;
#else
            GUIUtility.systemCopyBuffer = text;
            return true;
#endif
        }
    }
}
