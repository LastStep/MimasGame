// Copying a room-code link to the *browser* clipboard.
//
// Unity's GUIUtility.systemCopyBuffer does not reach it on Web. It writes to an enclosed buffer of
// Unity's own, so the lobby could set it, log the link, tell the player "Link copied." and copy
// nothing at all. That was true of every build until now and it was checked, not assumed:
// tools/smoke/clipboard-check.mjs seeds the real clipboard with a sentinel, presses Copy link, and
// reads navigator.clipboard back to find the sentinel still there.
//
// Two routes, because neither is universal:
//   navigator.clipboard.writeText  - the modern one. Needs a secure context (https, or localhost)
//                                    and transient user activation. It is asynchronous, so its
//                                    success is not knowable when this function returns.
//   document.execCommand('copy')   - deprecated, synchronous, and still works everywhere. It is the
//                                    fallback and the answer when the promise rejects.
//
// Transient activation is why this must be called from the click handler's frame. Unity processes
// input inside requestAnimationFrame, a few milliseconds after the browser's pointerup, which is
// comfortably inside Chrome's activation window - but a copy triggered by a timer would fail.
//
// Return: 1 if a copy route was entered, 0 if neither was available. 1 is not proof the write
// landed; the promise may still reject, and the fallback below is what runs when it does.
mergeInto(LibraryManager.library, {
  MimasCopyToClipboard: function (textPtr) {
    var text = UTF8ToString(textPtr);

    // The synchronous fallback, also used from the promise's catch.
    function execCommandCopy() {
      try {
        var area = document.createElement('textarea');
        area.value = text;
        // Off-screen rather than display:none - a hidden element cannot be selected.
        area.setAttribute('readonly', '');
        area.style.position = 'fixed';
        area.style.top = '-9999px';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.select();
        area.setSelectionRange(0, text.length);
        var ok = document.execCommand('copy');
        document.body.removeChild(area);
        return ok;
      } catch (e) {
        console.warn('[MimasClipboard] execCommand fallback failed: ' + e);
        return false;
      }
    }

    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).catch(function (e) {
          console.warn('[MimasClipboard] writeText rejected (' + e + '); falling back');
          execCommandCopy();
        });
        return 1;
      }
    } catch (e) {
      console.warn('[MimasClipboard] writeText threw: ' + e);
    }

    return execCommandCopy() ? 1 : 0;
  },
});
