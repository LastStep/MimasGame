---
id: RESEARCH-2026-09-18-web-text-input
title: Keyboard text never reaches a UI Toolkit TextField in the Web build
project: mimas
lane: director
status: open
author: researcher
created: 2026-09-18
blocks: M2-1, M2-7, the 10 Oct playtest
---

# Keyboard text never reaches a UI Toolkit TextField in the Web build

## The answer, in one paragraph

**This is not our bug and it is not a design problem — it is a Unity regression that Unity already
fixed, in our own release stream, four months ago.** Unity issue **4006 / UUM-137597, "Input fields do
not maintain focus when they are selected in a Web build"**, category *UI Toolkit Controls*, was
introduced at **6000.4.0b11** and fixed in **6000.4.6f1**, released **5 May 2026**. Mimas is on
**6000.4.4f1** (18 Apr–5 May window), which sits squarely inside the affected range. The failure mode is
**focus loss, not text delivery**: the TextField takes focus on click and drops it in the same frame, so
every keystroke is delivered to nothing. That is why pointer input works, why the Join button produces
its error, and why the field stays empty rather than showing wrong characters. The best fix is to
**upgrade the Editor to 6000.4.6f1** — a two-patch-version bump inside the same minor stream, whose
release note reads *"Editor: Fixed WebGL input field focusing out after typing. (UUM-137597)"*. Every
other route on the table — the `WebGLInput` DOM-overlay package, an HTML lobby outside the canvas,
`captureAllKeyboardInput`, adding an EventSystem — is either a permanent maintenance burden carried for
a bug that no longer exists, or cargo cult. There is **no one-line workaround**; I looked hard and found
nobody who reported one working. The one genuinely free thing available today is that Mimas already
ships the escape hatch: `?room=CODE` links auto-join (`LobbyView.cs:194-201`), so nobody *has* to type a
room code on 10 October even if the upgrade slips.

---

## Reading the confidence tags

Rohan cannot check my sources, so every claim below carries one of:

- **[documented]** — Unity's own documentation, release notes, or the issue-tracker record itself.
- **[reported]** — several independent people say the same thing.
- **[single report]** — one person on one forum. Treat as a lead, not a fact.
- **[inference]** — my reasoning from the above. Not quoted by anyone.

---

## 1. The upstream bug, pinned

The issue tracker is a single-page app, so the human URL shows nothing. Its JSON API does. You can check
this yourself in one line:

```bash
curl -s https://issuetracker.unity.com/api/v1.0/issues/4006
```

**[documented]** — the record, verbatim in its fields:

| Field | Value |
|---|---|
| Issue | **4006**, internal key **UUM-137597** |
| Title | *Input fields do not maintain focus when they are selected in a Web build* |
| Category | **UI Toolkit Controls** (Unity main, not a package) |
| Reproducible with | `6000.4.0b11, 6000.4.1f1, 6000.5.0b1, 6000.6.0a1` |
| **Not** reproducible with | `6000.0.71f1, 6000.3.12f1, 6000.4.0b10` |
| First affected in 6000.4.X | **6000.4.0b11** |
| **Fixed in 6000.4.X** | **6000.4.6f1** — `isReleasedFixedInVersion: true` |
| Fixed in other streams | 6000.3.13f1, 6000.5.0b2, 6000.6.0a2 |
| Browsers tested by Unity | Edge, Firefox (Rohan reproduced in Chromium) |
| Repro platform | Windows 11 |
| Record last updated | 2026-04-27 |
| Public discussion | https://discussions.unity.com/t/1725637 |

The repro text, verbatim: *"Actual result: Input fields lose focused almost instantly / Expected result:
Input fields remain focused"*, with the notes *"Issue applies to all input fields from UI Builder"* and
*"Attempting to use 'Tab' to move to other elements refocuses on the first input field."*

**The fix, confirmed independently of the tracker record. [documented]** The Unity 6000.4.6f1 release
page (released **5 May 2026**) lists:

> "Editor: Fixed WebGL input field focusing out after typing. (UUM-137597)"

— https://unity.com/releases/editor/whats-new/6000.4.6f1

**Is 6000.4.6f1 real and downloadable? [documented]** Yes. Unity's release API returns exactly one
match: `{"version":"6000.4.6f1","releaseDate":"2026-05-05T19:06:48.637Z"}` with full installer
downloads —
`https://services.api.unity.com/unity/editor/release/v1/releases?limit=3&version=6000.4.6`.

**Are we affected? [inference, but a strong one]** 6000.4.4f1 (released 2026-04-22) sits between
`firstAffectedVersion: 6000.4.0b11` and `fixedInVersion: 6000.4.6f1`. The inference is corroborated
twice: Rohan reproduced the exact symptom, and on the public thread **manuelgoellnitz (17 Apr 2026)**
wrote *"it's also broken in 6000.4.3f1"* — the release immediately below ours. **[reported]**

**The Apr 2026 thread you pointed me at.** https://discussions.unity.com/t/ui-toolkit-textfield-webgl-u6-4-pc-chrome-is-dont-move/1717836

- **oukaichimon, 25 Apr 2026**: *"When I run the WebGL build in Chrome on a PC and click the `TextField`
  to enter text, the `TextField` briefly receives focus, but it immediately loses focus."* Unity 6.4.
  Works in the Editor; works on **mobile** web browsers, where the software keyboard opens normally.
- **Schnatze, 6 May 2026**: same behaviour *"across all major browsers"*; says he built a custom
  TextField workaround rather than wait.
- **oukaichimon, 8 May 2026**: posts the tracker link, *"May be, it is Fixed"*.
- **Schnatze, 8 May 2026**: *"Yes. That was faster than I thought."*

**[inference]** That exchange is dated three days after 6000.4.6f1 shipped, which is the closest thing to
a user-side confirmation I found. It is two people agreeing the tracker says *fixed*, not two people
saying they tested the build. Treat the release note as the evidence; treat the thread as corroboration.

### Which 6000.4 should we land on?

**[documented]** 6000.4 has **27 releases total**, the last being **6000.4.12f1, 17 June 2026**. There
has been nothing new in that stream for three months. Counting the API's own list: 3 alphas, 11 betas,
13 `f1` releases (`0f1` through `12f1`). So .12 is currently the terminal 6000.4.

Two caveats worth knowing before choosing .6 over .12:

- **6000.4.5f1 (28 Apr 2026) changes Web audio behaviour. [documented]** *"Audio: Compressed In Memory
  setting defaults to Decompress On Load on Chromium-Based Web Browsers to avoid Memory Leaks
  (UUM-136929)."* We hit a memory-pressure stop during T-0003; this is a change we inherit either way
  (it is below .6), and it trades download-time decompression for runtime memory.
  — https://unity.com/releases/editor/whats-new/6000.4.5f1
- **6000.4.7f1 and 6000.4.12f1 contain no reversal of the fix. [documented]** I read both release pages.
  .7 has UI Toolkit texture/Painter2D fixes; .12 has four UI Toolkit fixes (ListView reorder,
  GroupTransform, ScrollView padding, UI Builder selectors), none touching web input focus.
  — https://unity.com/releases/editor/whats-new/6000.4.7f1 ·
  https://unity.com/releases/editor/whats-new/6000.4.12f1

**A negative result worth stating: [documented]** Unity's own support page
(https://unity.com/releases/unity-6/support) lists **6.0 LTS ("supported through October 2026")** and
**6.3 LTS ("supported until December 2027")** and does **not mention 6.4, 6.5 or 6.6 at all**. It says
Update releases are *"Supported until the next release is published."* I could not establish whether
6000.4 still gets patches. Issue 11458 (below) targets `6000.4.13f1` with
`isReleasedFixedInVersion: false`, so Unity intends at least one more — but none has shipped since June.
**This is a real unknown, not something I am hiding.**

### One more Web input bug, still open for us

**[documented]** Issue **11458 / UUM-136581**, *"An error is thrown in Web builds when the UI Toolkit
'Email Address' Input Field is focused with a touch"* — `Uncaught InvalidStateError: Failed to execute
'setSelectionRange' on 'HTMLInputElement'`. Reproducible in `6000.4.0b12`; the 6000.4 fix targets
**6000.4.13f1**, *not yet released*. Chrome, Edge, Firefox, touchscreen only. Mimas has no email field,
so this does not bite us — but it tells you the Web text-input path in 6000.4 is actively being repaired
and that **touchscreen** is the weakest part of it. `curl -s https://issuetracker.unity.com/api/v1.0/issues/11458`

---

## 2. Focus, or text delivery? — It is focus.

**It is (a), focus loss. The Enter-as-"Enter" bug is a different, much older, already-fixed problem.**

**Why focus and not delivery. [documented + inference]**

1. The tracker's own repro sentence is about focus, not characters: *"Input fields lose focused almost
   instantly."* Its category is **UI Toolkit Controls**, not Input System.
2. The release note describes the symptom as *"input field focusing out after typing"*.
3. **[inference]** A delivery bug produces *wrong* characters in the field. Ours produces an *empty*
   field. That is what you see when nothing is focused: UI Toolkit routes key events to the focused
   element, and there isn't one.
4. Rohan's own observation fits: pointer works, buttons fire, only the caret fails to stick.

**The Enter-as-"Enter" bug is a red herring here. [documented]** `Keyboard.onTextInput` on WebGL used to
deliver the Return key as the five literal characters `E n t e r`. It is real, it is on the tracker as
*"[WebGL] Return key is captured as the string 'Enter' when using Keyboard.onTextInput"*, and it was
fixed back in **2019.4.40f1, 2020.3.39f1, 2021.3.5f1, 2022.1.4f1 and 2022.2.0a16** — years before
anything in the 6000 line.
— https://issuetracker.unity3d.com/issues/webgl-return-key-is-captured-as-the-string-enter-when-using-keyboard-dot-ontextinput ·
https://discussions.unity.com/t/keyboard-ontextinput-is-broken-in-webgl-new-input-system/873294

### Does having no EventSystem matter?

**No, and Unity documents that explicitly. [documented]**
https://docs.unity3d.com/6000.4/Documentation/Manual/UIE-Runtime-Event-System.html

> "When you enter Play mode, UI Toolkit creates a default event system that is not part of any scene, and
> provides basic support for most input devices." … "That default event system is used if there is no
> other active event system to replace it."

I verified our side: **there is no `EventSystem` anywhere under `MimasClient/Assets/_Game`** (grep over
the whole tree, zero hits) and `activeInputHandler: 1` in `ProjectSettings.asset:928`. That is the
*documented supported configuration* for a UI-Toolkit-only project, not a misconfiguration.

**EventSystem + InputSystemUIInputModule vs. DefaultEventSystem vs. InputForUI. [reported, partly
inference]** `InputForUI` with its `InputSystemProvider` is an internal Unity module that gives UI
Toolkit **one** input dependency regardless of which input system is active; the `EventSystem` +
`InputSystemUIInputModule` route is what you add when you also need **uGUI** elements to receive the
same events. Mimas has no uGUI. I found no Unity documentation, and no bug report, tying the presence or
absence of an EventSystem to this focus bug. **[inference]** Adding one is very unlikely to help, and it
drags `com.unity.ugui`'s event plumbing into a build we currently keep clean.
— https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-faq-event-and-input-system.html

### Does Active Input Handling = "Both" change anything?

**[documented]** The same manual page describes "Both" as: *uses Input System if available, otherwise
falls back to Input Manager.* **[inference]** With `com.unity.inputsystem@1.19.0` installed and enabled,
"Both" resolves to the same Input System path we already take. It would not change the code that loses
focus, and it would re-enable a legacy input path in a WebGL2 build for no gain. **I found no report of
anyone fixing this bug by switching to "Both".**

---

## 3. Is there a one-line workaround? — No. Here is what each claim is worth.

| Claim | Verdict | Evidence |
|---|---|---|
| Re-focus the field on `FocusOutEvent` | **Not confirmed by anyone.** Unity Discussions has a standing report that *calling focus inside FocusIn/FocusOut callbacks does nothing*. The one person on the affected thread who needed it working built a whole custom TextField instead (Schnatze, 6 May 2026) — which is what you do when a one-liner did not work. | **[single report]** for the "does nothing" claim; **[inference]** for reading Schnatze's choice as a negative result |
| `WebGLInput.captureAllKeyboardInput = false` | **Cargo cult here, and actively harmful.** It does the *opposite* of what we need. Unity documents that Web builds capture all keyboard *regardless of canvas focus* by default, and that you set this false **so that HTML elements on the page can get keys**. Our field is inside the canvas. Multiple people report that setting it false alone makes Unity ignore all keyboard until you also add `tabindex` to the canvas element. | **[documented]** for the semantics (https://docs.unity3d.com/Manual/webgl-input.html, https://docs.unity3d.com/ScriptReference/WebGLInput-captureAllKeyboardInput.html); **[reported]** for the tabindex trap |
| Add an EventSystem to the scene | **Cargo cult.** See §2 — Unity documents that the default event system covers this case. No bug report links the two. | **[documented]** + **[inference]** |
| `PanelInputConfiguration` | **Cargo cult.** It is documented as configuring *how input is routed to panels* for **World Space UI** and **UI Toolkit ↔ uGUI interop** (`Auto Create Panel Components`, `World Document Raycaster`, `Panel Raycaster`). Nothing to do with browser keyboard events reaching a screen-space panel. | **[documented]** https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.PanelInputConfiguration.html |

**The honest summary: the only workaround anyone reports actually working is replacing the field with a
real DOM `<input>` — which is §4, and is not a one-liner.**

---

## 4. The HTML `<input>` overlay pattern

### kou-yeung/WebGLInput — the standard community answer

**[documented]**, from the GitHub API and the README:

| Fact | Value |
|---|---|
| Licence | **MIT** |
| Stars / forks | 962 / 131 |
| Last push | **2026-06-23** (3 months ago) — actively maintained, not archived |
| Latest tagged release | **1.4.5, 2025-12-05** |
| Open issues | **34** |
| Repo size | **~13 MB** — but that is the repo including demo assets; the shippable part is `Assets/WebGLSupport` |
| Install | UPM git URL: `https://github.com/kou-yeung/WebGLInput.git?path=Assets/WebGLSupport`, **or** drop-in `WebGLSupport.unitypackage` |
| Demo built with | Unity **2023.2.2f1** |

**UI Toolkit support is labelled "Experiment". [documented]** The README says, verbatim: *"support UI
Toolkit. (Experiment) (Support from Unity2022)"*. It landed in **May 2024** (issue #140, *"Experimental:
support uitoolkit"*). It is **not automatic** — unlike uGUI, where the README says *"no need to setting
anything"*, UI Toolkit requires you to attach a manipulator per field:

```csharp
uiDocument.rootVisualElement.Query<TextField>().ForEach(v =>
    v.AddManipulator(new WebGLSupport.WebGLInputManipulator()));
```

For Mimas that is two fields — `name` and `join-code` (`Lobby.uxml:12,18`).

**Unity 6 compatibility: unstated. [inference]** Nothing in the README, releases or issue titles claims
Unity 6.x, let alone 6000.4. The demo is 2023.2. Nobody has published a "works on 6.4" result I could
find. This would be the first thing to test, not something to assume.

**Known issues, from the open issue list. [documented]** These are the DOM-overlay failure modes, and
they are exactly the hard parts:

- *"HTML Inputfield can have wrong size and position when window is resized or zoomed in"* (2025-12-01)
- *"HTML Input element stays on screen even when gameobject is disabled"* (2025-12-01)
- *"Mobile bottom input does not always show up on iOS (safari and chrome)"* (2025-11-28)
- *"Input Field Not Showing in Landscape Mode on iPhone Safari"* (2025-10-14)
- *"Webview in mobile keyboard bug"* (2026-04-23)
- caret-position complaints, repeatedly (2025-02-12, 2025-05-26)
- *"Plugin breaks non-editable Rich Text formatting"* (2025-10-15)

**It does buy two things we otherwise cannot have:** real OS copy/paste (README: *"support 'copy and
paste'"*) and a real mobile soft keyboard.

### Maintained alternatives — I found none that fit

- **Trisibo/unity-webgl-copy-and-paste** (the live fork of greggman's archived repo): BSD-3-Clause, 56
  stars, last push **2025-03-04**, tested on Firefox/Chrome/Edge/Safari including **Unity 6.0.34**. But
  its README states plainly: *"At the moment there is only support for `InputField` and
  `TMPro.TMP_InputField`."* **No UI Toolkit.** It even points you at kou-yeung as the better option.
  **[documented]** https://github.com/Trisibo/unity-webgl-copy-and-paste
- **greggman/unity-webgl-copy-and-paste**: **archived 12 Dec 2023**, redirects to the above.
  **[documented]**
- **rehanlabs/Unity-WebGL-HTML-InputFix**: 3 stars, Unity 6.0.51f1, **TMP only, explicitly not UI
  Toolkit**, ~200–300 lines. Interesting as a reference implementation, not as a dependency.
  **[documented]** https://github.com/rehanlabs/Unity-WebGL-HTML-InputFix

### What a hand-rolled ~100 lines would actually have to get right

**[inference, evidenced by the issue lists above]** People do write this themselves. The reason the
most-used implementation has 34 open issues after eight years is that the list is long:

1. **Position and size** the `<input>` over the element's world-space rect — and re-sync it on window
   resize, browser zoom, devicePixelRatio change and canvas scaling.
2. **Focus/blur round trip** — DOM focus in, Unity focus out, without a loop, and hide the element when
   the VisualElement is disabled or its panel closes (the #1 reported failure).
3. **Caret and selection mapping** back into the Unity field, including click-to-position (the #2
   reported failure).
4. **IME composition** for CJK — the whole reason the package is called "IME for Unity WebGL".
5. **Mobile virtual keyboard**, including iOS Safari landscape and the bottom-docked input.
6. **Paste** — free, if the element is a real `<input>`; that is the whole trick.
7. **`pointer-events` and `z-index`** so the element takes clicks over the canvas but does not eat the
   rest of the UI.
8. **Value validation** (`max-length="4"`, our upper-casing callback at `LobbyView.cs:184-188`) has to
   survive the round trip.

**~100 lines gets you a desktop-Chrome demo. It does not get you 1–8.**

---

## 5. "Don't type in Unity at all" — put the lobby in the page

**Yes, it is a real pattern. [reported]** Unity's manual documents both halves of the bridge.

**JS → Unity. [documented]**
https://docs.unity3d.com/6000.3/Documentation/Manual/web-interacting-browser-js-to-unity.html

> "If you are planning to call the internal JavaScript functions from the global scope of the embedding
> page, you must use the `unityInstance` variable in your WebGL template index.html. Do this after the
> Unity engine instantiation succeeds, and then you can send a message to the build using
> `myGameInstance.SendMessage()`, or access the build module object using `myGameInstance.Module`."

**Unity → JS. [documented]** *"place files with JavaScript code using the `.jslib` extension in the
Assets folder > Plugins sub-folder"* and call them via `DllImport("__Internal")`. Mimas already does
this — the NativeWebSocket jslib socket.

**`Application.ExternalCall` / `ExternalEval`: I could not confirm they are removed. [negative result]**
The 6000.4 manual pages I read do not mention them at all, and every current page routes you to `.jslib`
plugins and `unityInstance.SendMessage`. Treat them as dead by omission, not by a citation I can give
you. Likewise, I did **not** find the `SendMessage` argument-type limits stated on the 6000.4 manual
page — the widely-repeated rule is "object name, method name, and one `string`, `int` or `float`", but
**[inference]** is the honest tag for that until someone checks the Scripting API page.

**The `captureAllKeyboardInput` gotcha is the real cost of this route. [documented + reported]** Unity
documents that *"By default, Unity Web processes all keyboard input the web page receives, regardless of
whether the Web canvas has focus or not"* and that you must set
`WebGLInput.captureAllKeyboardInput = false` for page elements to receive keys. But multiple people
report the second half: setting it false alone makes the **game** deaf, and you must also put
`tabindex="1"` on the `<canvas>` so the canvas can hold focus. Mimas has keyboard bindings through
`Keyboard.current`, so we would be taking that risk to the board, not just the lobby.
— https://docs.unity3d.com/Manual/webgl-input.html ·
https://www.tangledrealitystudios.com/development-tips/prevent-unity-webgl-from-stopping-all-keyboard-input/ ·
https://react-unity-webgl.dev/docs/api/tab-index

**Mimas-specific friction.** `ProjectSettings.asset:809` says `webGLTemplate: APPLICATION:Default` and
there is **no `Assets/WebGLTemplates` folder**. Any HTML lobby requires creating a custom template first
— and once we have one, it is the thing we build every release with, forever.

**Mimas-specific *bonus*, and it is a large one.** We already have the zero-code version of this option.
`LobbyView.cs:194-201`:

```
// A ?room=CODE link: the whole point of a code is that it can be pasted.
```
…prefills `join-code` from the URL and **auto-joins** if we are not already in a match. STATE.md records
`?room=` as one of the WebGL paths that has run at least once (T-0003). Clicking a link needs no
keyboard at all.

---

## 6. Clipboard on Web

**Can a Unity Web build read the OS clipboard? Effectively, no — not through `GUIUtility`.**

**[documented — by omission]** The scripting page for `GUIUtility.systemCopyBuffer` says only *"Get
access to the system-wide clipboard"* and calls out exactly one platform limitation: *"tvOS does not
support this feature."* **It says nothing about Web.** That silence is the whole problem.
— https://docs.unity3d.com/ScriptReference/GUIUtility-systemCopyBuffer.html

**[reported, with a Unity staff reply]** On
https://discussions.unity.com/t/copy-paste-in-textfields-in-webgl-builds/883970 — a thread explicitly
about **UI Toolkit** text input in WebGL builds — the consistent account across Unity 2021.3 through
2022.3 is that **Unity WebGL has its own enclosed clipboard**: Ctrl+C/Ctrl+V can move text *within* the
player, but pasting from an external application does not arrive. **HugoBD-Unity (Unity), 7 Jun 2022**:
*"Clipboard copy-paste (CTRL+C and CTRL+V) should work, if it doesn't it's a bug."* **HugoBD-Unity, 14
Jun 2023**: *"copy-pasting using the context menu is not yet supported and it's been asked for a very
long time."* The thread runs to Oct 2023 with no permanent fix.

**⚠ This lands directly on code we already shipped.** `LobbyView.cs:348-358` (`HandleCopyLink`) does
`GUIUtility.systemCopyBuffer = link;` and then reports **"Link copied."** to the player. That is the
*write* direction, which is the less broken of the two — but **[inference]** if the write goes into
Unity's enclosed clipboard rather than the OS one, the host presses the button, sees "Link copied.", and
pastes nothing into Discord. **Test this in a browser before the playtest depends on it.** It is a
two-minute check and it decides whether Option D below is real.

**`navigator.clipboard.readText()` — what it costs. [documented]**
https://developer.mozilla.org/en-US/docs/Web/API/Clipboard_API#security_considerations and
https://developer.mozilla.org/en-US/docs/Web/API/Clipboard/readText

- **Secure context (HTTPS) only.** *"This feature is available only in secure contexts (HTTPS)."*
  M2-7 gives us HTTPS anyway, so this is not an extra cost.
- The spec requires either **transient user activation** (recent interaction) or a genuine browser/OS
  "paste element".
- **Chromium**: if not allowed by spec and the document has focus, it *"triggers a request to use
  `clipboard-read` permission"* — a permission prompt, then it sticks.
- **Firefox & Safari**: they *"trigger a user prompt with an ephemeral context menu with a single 'Paste'
  option (enabled after 1 second)"*, and — this is the part that matters — *"`clipboard-read` permission
  is not supported (and not planned)"*. So on Firefox and Safari a "Paste code" button shows a prompt
  **every single time**, with a one-second delay before the option is clickable. The prompt is suppressed
  only for same-origin clipboard content, which a code copied from Discord is not.

**The route that avoids all of this. [reported]** The community plugins do not call `readText()` — they
attach a DOM **`paste` event listener**, because on a real paste event the browser hands you the data
with no permission and no prompt. That is how greggman's/Trisibo's plugin works. But those plugins
support **legacy `InputField` and `TMP_InputField` only, not UI Toolkit** — so we cannot just take it.
A real `<input>` overlay (§4) gets the same behaviour for free, which is the strongest argument in
favour of WebGLInput if we ever need paste badly.

---

## What decides this

Three things, and reversibility.

1. **Does a friend get into a room on Saturday 10 October?** Four friends, two pairs, 23 days away. That
   is the only test any of this faces.
2. **What does it cost out of those 23 days — and what does it take away from M2-7?** Deploy is *"the
   gap to 10 Oct"* per STATE.md and nothing else in M2 is unstarted. Every day spent on text input is a
   day not spent on the VPS.
3. **What are we still maintaining in November?** A third-party jslib, a custom WebGL template and a
   parallel HTML lobby are all things that have to keep working through every future build. An Editor
   version number is not.
4. **How long to back out, and what is stranded.** Stated per option below.

---

## Options

### A. Upgrade the Editor to 6000.4.6f1

Change `ProjectVersion.txt` from `6000.4.4f1` to `6000.4.6f1`, let Unity reimport, rebuild.

- **Gets us:** the actual fix, from the vendor, for exactly this bug, released and sitting on Unity's
  download servers for four months. Both TextFields work — `join-code` **and** `name`. Nothing new to
  maintain; the diff is one line and a `Library/` rebuild. It is the smallest possible delta that
  contains the fix: two patch releases, whose only notable side effect is the documented 6000.4.5f1
  change of Web audio to *Decompress On Load* on Chromium.
- **Costs:** an Editor install (multi-GB) and a full asset reimport — call it an afternoon on a machine
  that ran out of memory during T-0003. A version bump edits `ProjectSettings/ProjectVersion.txt`, which
  **golden rule 7 says needs Rohan's yes**. Every ladder rung must be re-run: `dotnet test` ×2, EditMode,
  the Web Release build (watch the 13 MB ratchet — 12.38 MB now), and the browser smoke. There is
  irreducible risk that two patch releases break something else in a build that only just started
  working; I cannot rule that out from release notes alone.
- **Forecloses:** nothing. It removes the *reason* for B and C rather than blocking them.
- **Reversible?** **Very.** Revert one line, reopen in 6000.4.4f1, and the old Editor is still installed.
  What is stranded: the `Library/` cache (rebuilt, ~20 min) and any `.meta`/asset upgrades Unity performs
  on import — which is why the revert should be a `git checkout`, not a hand edit. Call it **under an
  hour to back out**, same day.

### B. Upgrade the Editor to 6000.4.12f1 (the stream's last release)

Same as A, but land on the newest 6000.4 instead of the first fixed one.

- **Gets us:** the fix, plus six further patch releases of fixes, and no second upgrade later. 6000.4.12f1
  (17 Jun 2026) is currently the terminal release of the stream — nothing has shipped in three months —
  so this is where 6000.4 ends up regardless.
- **Costs:** eight patch releases of delta instead of two, 23 days before a playtest, on a build that has
  run in a browser exactly once. I read the .7 and .12 notes and saw nothing alarming, but I did not read
  .8, .9, .10 and .11, and "nothing alarming in the notes" is not the same as "no regression". This is the
  option where you lose three days to something unrelated.
- **Forecloses:** nothing, but it makes "was it the upgrade?" much harder to answer if the Web build
  misbehaves after.
- **Reversible?** Same mechanism as A, same hour — but **diagnosing** whether to revert is materially
  harder with eight releases of change in the diff.

### C. Add `kou-yeung/WebGLInput` and attach `WebGLInputManipulator` to both TextFields

Stay on 6000.4.4f1; replace the two fields' input path with real DOM `<input>` elements.

- **Gets us:** typing without an Editor upgrade, plus two things no other option gives: **real OS
  copy/paste** into the room-code field, and a **real mobile soft keyboard**. MIT, actively maintained
  (last push June 2026), installable as a UPM git URL, about five lines of C# in `LobbyView.OnEnable`.
- **Costs:** its UI Toolkit support is labelled **"Experiment"** by its own author, has never been
  reported working on 6000.4, and its open issue list is a catalogue of the exact failures we would be
  signing up for — wrong size and position on resize/zoom, the element staying visible when the
  GameObject is disabled, iOS Safari landscape. It adds a third-party `.jslib` to a build under
  **Managed Stripping High**, so `link.xml` becomes our problem. It is permanent maintenance carried for
  a bug Unity has already fixed. And editing `Packages/manifest.json` is **golden rule 7** — same gate as
  A, for a worse reason.
- **Forecloses:** the simple story. Once the lobby's text goes through a DOM overlay, every future text
  field in Mimas (chat, profile, a rematch message) either joins it or behaves differently from the
  others. And we would still be on an Editor with a known Web input regression, which is a landmine for
  whatever we build next.
- **Reversible?** **Medium.** Removing the package and five lines takes an hour — *unless* we have by
  then started relying on the paste or mobile-keyboard behaviour it brings, in which case removing it
  silently loses those. Stranded: the mobile keyboard, which we would not otherwise have.

### D. Ship 10 October with no typing: join by link, keep the generated name

Change nothing in the client. The host presses **Create room**, presses **Copy link**, pastes it into the
Discord call; the friend clicks it and `LobbyView.cs:194-201` fills the code and joins automatically. The
name stays the `"Guest-" + Random.Range(1000, 10000)` default from `LobbyView.cs:166-168`.

- **Gets us:** a working playtest for **zero days of work and zero risk**, using a path that is already
  built and already ran once. Four friends in a Discord call will paste links anyway — OPT-0001 chose
  room codes precisely because *"a link is how anyone who is not in the call ever tries the game"*.
- **Costs:** nobody can join without a link, so the room code on screen is decoration; nobody can set a
  name, so the playtest feedback says "Guest-4471 resigned"; and **it rests on `HandleCopyLink` actually
  reaching the OS clipboard, which §6 says is exactly the thing WebGL breaks.** If the copy is fake, the
  host has to read the URL off the address bar and retype it — which still works, because the browser's
  address bar is not a Unity TextField. Nothing here is fixed; it is deferred.
- **Forecloses:** nothing at all. It is a deferral.
- **Reversible?** **Free.** There is nothing to back out.

---

## Recommendation

**Take A: upgrade to 6000.4.6f1. And do D today anyway, because it costs nothing.**

The single reason: **Unity has already fixed this exact bug in our exact stream, and the fix has been
publicly released for four months.** Every other option on this page is us building and then maintaining
a workaround for a defect that does not exist two patch versions ahead of where we are standing. Between
A and B, take **.6f1**: with 23 days to a playtest and a browser build that has run once, the smallest
delta that provably contains the fix beats the largest delta that might contain others.

**Do D in parallel and immediately** — not as a fallback plan on paper, but as the actual instruction in
the playtest invite. It is free, it needs no code, and it means the upgrade is allowed to go wrong
without costing us the 10th.

**What would change my mind:**

- **If 6000.4.6f1 does not fix it in our build.** Then we have a second, different bug, the four months
  of assumed safety are worthless, and the answer becomes D for the playtest and C investigated properly
  after it. *This is the one thing that must be checked first — before the reimport is even finished,
  build and type into the field.*
- **If the upgrade breaks the Web build in a way that costs more than two days.** The 13 MB ratchet, the
  Brotli path, the NativeWebSocket jslib and the memory-pressure ceiling from T-0003 are all things a
  version bump can disturb. Two days is the budget; past that, revert to 6000.4.4f1, ship D on the 10th,
  and reopen this after the playtest with the whole of M3 to absorb it.
- **If Mimas needs to work on phones at the playtest.** Then C stops being a workaround and starts being
  a feature: 6000.4.6f1 fixes desktop focus, but issue 11458 (touchscreen `setSelectionRange` error) is
  **still unfixed in every released 6000.4**, and WebGLInput's whole reason to exist is the mobile soft
  keyboard. Nothing in STATE.md says phones are in scope for 10 Oct — if that is wrong, this changes.
- **If a verifier finds `HandleCopyLink` genuinely reaches the OS clipboard** — that strengthens D enough
  that slipping the upgrade past the playtest becomes defensible rather than merely survivable.

---

## What happens the same morning, either way

| If Rohan picks | The first task |
|---|---|
| **A** | `unity install 6000.4.6f1`; open Mimas in it (expect a long reimport); **before anything else**, build Web Release and type four letters into `join-code` in Chromium. Then the full ladder: `dotnet test` ×2, `unity command console`, EditMode, Web Release build against the 13 MB ratchet, `tools/smoke/browser-smoke.mjs`. Commit `ProjectVersion.txt` on its own so the revert is one `git revert`. **Needs Rohan's yes first — golden rule 7.** |
| **B** | Identical to A, substituting `6000.4.12f1`, plus read the .8–.11 release notes for Web and UI Toolkit entries before starting, so a regression is recognisable when it appears. |
| **C** | Add `https://github.com/kou-yeung/WebGLInput.git?path=Assets/WebGLSupport` to `Packages/manifest.json` (**golden rule 7 — needs Rohan's yes**); attach `WebGLInputManipulator` to `name` and `join-code` in `LobbyView.BindUI`; check `link.xml` survives Managed Stripping High; rebuild and test resize and browser zoom, not just typing. |
| **D** | No client change. Write the room-joining instruction into the 10 Oct playtest brief ("host presses Create room → Copy link → paste in Discord"), **and verify in a browser that Copy link actually reaches the Windows clipboard** — if it does not, the brief says "copy the address bar after you create a room" instead. |

---

## Two process notes for the run report

1. **The Trinetra asset guard has two false positives that blocked read-only work in this session.**
   (a) The session scratchpad lives under `C:\Users\droha\AppData\Local\Temp\claude\...`, which matches
   the guard's `**/Temp/**` pattern — so *any* file written to the directory the harness designates for
   temporary files is refused. (b) `grep -rl "EventSystem" MimasClient/Assets/_Game/Scenes/*.unity` was
   blocked as "writes to a protected path" because the `*.unity` glob appears in the command; it is a
   read. Both were worked around (pipe-only curl, and the `Grep` tool), but they cost calls.
2. **The Unity issue tracker's JSON API is the way to read it.**
   `https://issuetracker.unity.com/api/v1.0/issues/<id>` returns status, affected versions, per-stream
   `fixedInVersion` and `isReleasedFixedInVersion` — all invisible on the SPA page. Similarly
   `https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.4&limit=25` (max
   `limit` is 25; 30 returns HTTP 400) enumerates a stream's releases with dates. Worth keeping.

---

## Sources

**Primary — Unity**

- Unity Issue Tracker 4006 / UUM-137597, *Input fields do not maintain focus when they are selected in a Web build* — https://issuetracker.unity.com/issues/4006 (JSON: `https://issuetracker.unity.com/api/v1.0/issues/4006`), record last updated 2026-04-27
- Unity Issue Tracker 11458 / UUM-136581, *An error is thrown in Web builds when the UI Toolkit "Email Address" Input Field is focused with a touch* — https://issuetracker.unity.com/issues/11458, record last updated 2026-06-23
- Unity 6000.4.6f1 release notes (5 May 2026) — https://unity.com/releases/editor/whats-new/6000.4.6f1
- Unity 6000.4.5f1 release notes (28 Apr 2026) — https://unity.com/releases/editor/whats-new/6000.4.5f1
- Unity 6000.4.7f1 release notes (13 May 2026) — https://unity.com/releases/editor/whats-new/6000.4.7f1
- Unity 6000.4.12f1 release notes (17 Jun 2026) — https://unity.com/releases/editor/whats-new/6000.4.12f1
- Unity Editor release API — https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.4&limit=25 (queried 2026-09-18)
- Unity 6 releases & support — https://unity.com/releases/unity-6/support (queried 2026-09-18)
- Manual, *Runtime UI event system and input handling* (6000.4) — https://docs.unity3d.com/6000.4/Documentation/Manual/UIE-Runtime-Event-System.html
- Manual, *FAQ for input and event systems with UI Toolkit* — https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-faq-event-and-input-system.html
- Manual, *Input in Web* — https://docs.unity3d.com/Manual/webgl-input.html
- Manual, *Call Unity C# from browser JavaScript* (6000.3) — https://docs.unity3d.com/6000.3/Documentation/Manual/web-interacting-browser-js-to-unity.html
- Scripting API, `WebGLInput.captureAllKeyboardInput` — https://docs.unity3d.com/ScriptReference/WebGLInput-captureAllKeyboardInput.html
- Scripting API, `GUIUtility.systemCopyBuffer` — https://docs.unity3d.com/ScriptReference/GUIUtility-systemCopyBuffer.html
- Scripting API, `PanelInputConfiguration` — https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.PanelInputConfiguration.html
- Unity Issue Tracker, *[WebGL] Return key is captured as the string "Enter" when using Keyboard.onTextInput* — https://issuetracker.unity3d.com/issues/webgl-return-key-is-captured-as-the-string-enter-when-using-keyboard-dot-ontextinput

**Unity Discussions (forum — dated, named)**

- *UI TOOLKIT + TextField + WEBGL + U6.4 + PC Chrome is don't move*, 25 Apr – 8 May 2026 — https://discussions.unity.com/t/ui-toolkit-textfield-webgl-u6-4-pc-chrome-is-dont-move/1717836
- Discussion thread attached to issue 4006, Mar–Apr 2026 — https://discussions.unity.com/t/1725637
- *Copy Paste in TextFields in WebGL builds* (incl. HugoBD-Unity replies 7 Jun 2022 and 14 Jun 2023) — https://discussions.unity.com/t/copy-paste-in-textfields-in-webgl-builds/883970
- *Keyboard.onTextInput is broken in WebGL (new Input System)* — https://discussions.unity.com/t/keyboard-ontextinput-is-broken-in-webgl-new-input-system/873294
- *How do I set WebGLInput.captureAllKeyboardInput to false?* — https://discussions.unity.com/t/how-do-i-set-webglinput-captureallkeyboardinput-to-false/808397

**Community packages**

- kou-yeung/WebGLInput — https://github.com/kou-yeung/WebGLInput (MIT; 962★; last push 2026-06-23; release 1.4.5, 2025-12-05; 34 open issues)
- Trisibo/unity-webgl-copy-and-paste — https://github.com/Trisibo/unity-webgl-copy-and-paste (BSD-3; 56★; last push 2025-03-04; **no UI Toolkit**)
- greggman/unity-webgl-copy-and-paste — https://github.com/greggman/unity-webgl-copy-and-paste (**archived 12 Dec 2023**)
- rehanlabs/Unity-WebGL-HTML-InputFix — https://github.com/rehanlabs/Unity-WebGL-HTML-InputFix (3★; TMP only)

**Web platform**

- MDN, *Clipboard API — Security considerations* — https://developer.mozilla.org/en-US/docs/Web/API/Clipboard_API#security_considerations
- MDN, *Clipboard.readText()* — https://developer.mozilla.org/en-US/docs/Web/API/Clipboard/readText
- Tangled Reality Studios, *Prevent Unity WebGL from Stopping All Keyboard Input* — https://www.tangledrealitystudios.com/development-tips/prevent-unity-webgl-from-stopping-all-keyboard-input/
- react-unity-webgl, *Tab Index and Input Keyboard Capturing* — https://react-unity-webgl.dev/docs/api/tab-index

**Local ground truth verified while writing this**

- `MimasClient/ProjectSettings/ProjectVersion.txt` — `6000.4.4f1`
- `MimasClient/ProjectSettings/ProjectSettings.asset:928` — `activeInputHandler: 1`; `:809` — `webGLTemplate: APPLICATION:Default`; `:812` — `webGLCompressionFormat: 0`
- No `EventSystem` anywhere under `MimasClient/Assets/_Game` (grep, zero hits); no `Assets/WebGLTemplates` folder
- `MimasClient/Assets/_Game/UI/Lobby.uxml:12,18` — the two `TextField`s, `name` and `join-code`
- `MimasClient/Assets/_Game/UI/LobbyView.cs:166-168` (default guest name), `:184-188` (upper-casing), `:194-201` (`?room=` prefill and auto-join), `:348-358` (`HandleCopyLink` → `GUIUtility.systemCopyBuffer`)
- `MimasClient/Packages/manifest.json` — `com.unity.inputsystem: 1.19.0`
