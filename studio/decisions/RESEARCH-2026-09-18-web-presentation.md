---
id: RESEARCH-2026-09-18
title: How should the Unity 6 Web build be embedded and presented?
project: mimas
lane: director
status: open
author: researcher
created: 2026-09-18
decided:
chosen:
---

# How should the Unity 6 Web build be embedded and presented?

> Research, not a plan. No code was written. Every factual claim below carries a URL or a path on this
> machine. Where I could not find an authoritative answer I have said so in bold rather than guessed —
> there are seven such places and they are all marked **Negative result**.

## The answer in one paragraph

The "premade box" is not a Unity limitation and it is not a Player Setting — it is **one file**:
`index.html` in Unity's built-in **Default** Web template, which hard-codes
`canvas.style.width = "960px"` on desktop, centres the canvas in a white page, and draws a footer bar
containing the Unity wordmark, the product name and a fullscreen button. I read that file on this
machine, at
`C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\PlaybackEngines\WebGLSupport\BuildTools\WebGLTemplates\Base\Default\index.html`.
Replacing it is the supported, documented path: copy it to `Assets/WebGLTemplates/<Name>/`, delete the
footer, give the canvas `width:100%; height:100%` in a full-viewport container, and point
`PlayerSettings.WebGL.template` at `"PROJECT:<Name>"`. Unity then keeps the WebGL drawing buffer in
sync with the canvas's CSS size automatically — you do **not** need a JS resize listener — but it does
so **multiplied by `window.devicePixelRatio`, with high-DPI on by default and no Editor opt-out**
([Unity 6.4 build configuration](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-build-configuration.html)).
That is the trap: going full-viewport on a 4K or retina laptop takes the drawing buffer from 0.58
megapixels to between 8 and 25, and the only lever is a JavaScript line in your own template
(`config.devicePixelRatio`) or URP's Render Scale. Separately, and new since this project started:
**the Made with Unity splash screen became optional on a Personal licence in Unity 6**
([Unity 6000.0.0f1 release notes](https://unity.com/releases/editor/whats-new/6000.0.0f1)), and this
project still has it switched on (`MimasClient/ProjectSettings/ProjectSettings.asset:20`,
`m_ShowUnitySplashScreen: 1`). A custom template, a DPR cap and the splash toggle are, together, the
whole of the difference between "a Unity demo" and "a web game", and none of them touches game code.

---

## 1. Unity 6 Web templates

### There are three built-ins in 6000.4, not two

The Unity 6.4 manual names **Default**, **Minimal** and **PWA**
([web-templates-intro](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-intro.html)):

| Template | Unity's own description |
|---|---|
| Default | "A white page with a loading bar on a grey canvas." |
| Minimal | "A minimal Web template that includes the necessary boilerplate code to run the Web content." |
| PWA | "A Progressive Web App including a web manifest file and service worker code." |

Verified on disk in the installed editor:
`…\6000.4.4f1\Editor\Data\PlaybackEngines\WebGLSupport\BuildTools\WebGLTemplates\Base\{Default,Minimal,PWA}`.
(Several third-party guides and one older Unity page still say "two" — they predate PWA.)

### What Default actually does, line by line

This is ground truth from the installed 6000.4.4f1 template, not from a blog. The parts that produce
the box Mimas renders in today:

```html
<title>Unity Web Player | {{{ PRODUCT_NAME }}}</title>
<link rel="shortcut icon" href="TemplateData/favicon.ico">
…
<div id="unity-container" class="unity-desktop">
  <canvas id="unity-canvas" width={{{ WIDTH }}} height={{{ HEIGHT }}} tabindex="-1"></canvas>
  <div id="unity-loading-bar"> … </div>
  <div id="unity-footer">
    <div id="unity-logo-title-footer"></div>
    <div id="unity-fullscreen-button"></div>
    <div id="unity-build-title">{{{ PRODUCT_NAME }}}</div>
  </div>
</div>
```

and, in the script block:

```js
} else {
  // Desktop style: Render the game canvas in a window that can be maximized to fullscreen:
  canvas.style.width  = "{{{ WIDTH }}}px";
  canvas.style.height = "{{{ HEIGHT }}}px";
}
```

with `TemplateData/style.css`:

```css
body { padding: 0; margin: 0 }
#unity-container.unity-desktop { position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%) }
#unity-footer { position: relative }
#unity-logo-title-footer { float:left; width: 102px; height: 38px; background: url('unity-logo-title-footer.png') … }
#unity-build-title { float: right; … font-family: arial; font-size: 18px }
```

So the four things that read as "Unity demo" are all in the template:

1. the browser tab says **"Unity Web Player | MimasClient"**;
2. the favicon is Unity's;
3. the canvas is pinned to `960 × 600` CSS pixels and centred in a white page;
4. the footer bar carries the Unity wordmark, the literal string `MimasClient`, and the fullscreen arrows.

`WIDTH`/`HEIGHT` come from Player Settings: this repo has
`defaultScreenWidthWeb: 960` / `defaultScreenHeightWeb: 600`
(`MimasClient/ProjectSettings/ProjectSettings.asset:46-47`), and `PRODUCT_NAME` is `MimasClient`
with `companyName: DefaultCompany` (same file, lines 15–16).

Note the mobile branch: Default already does the right thing on phones
(`width:100%; height:100%; position:fixed`, viewport meta injected, footer hidden). Only the **desktop**
branch is boxed. That is a deliberate Unity choice, not an oversight.

### Authoring a custom template

- Location: **`Assets/WebGLTemplates/<Name>/`**, and it must be in the *root* `Assets` folder — unlike
  `Editor` or `Plugins`, it is not found at arbitrary depth
  ([web-templates-add](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-add.html);
  the "root only" point is spelled out in
  [this Unity Discussions thread](https://discussions.unity.com/t/webgltemplates-folder-and-location/814387)).
  For Mimas this means `Assets/WebGLTemplates/…`, **not** `Assets/_Game/…`, which is a break from the
  repo's convention and worth knowing before someone "tidies" it.
- Required files: `index.html`, plus `thumbnail.png` at 128×128 for the Player Settings dropdown.
  Anything else the page needs (CSS, images, fonts) sits alongside.
- Start by copying a built-in from
  `<Unity Installation>/PlaybackEngines/WebGLSupport/BuildTools/WebGLTemplates/Base`
  ([same page](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-add.html)).
- Unity pre-processes `.html`, `.php`, `.css`, `.js` and `.json` in the template folder
  ([web-templates-intro](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-intro.html)).
- **Meta files:** the folder lives under `Assets`, so Unity generates `.meta` files for every file in
  it. That is fine under golden rule 1 — let Unity create them, commit each asset with its `.meta`,
  never hand-write one.

### The macro language

Syntax is **triple braces**: `{{{ PRODUCT_NAME }}}`
([web-templates-variables, 6000.4](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-variables.html)).
Conditionals are `#if EXPRESSION` / `#else` / `#endif`. The documented variable table:

| Variable | Type | Meaning |
|---|---|---|
| `COMPANY_NAME`, `PRODUCT_NAME`, `PRODUCT_VERSION` | String | from Player Settings |
| `WIDTH`, `HEIGHT` | Integer | Default Canvas Width / Height |
| `SPLASH_SCREEN_STYLE` | String | `"Dark"` or `"Light"` |
| `BACKGROUND_COLOR` | String | hex triplet |
| `UNITY_VERSION` | String | build's editor version |
| `DEVELOPMENT_PLAYER` | Boolean | Development Build on |
| `DECOMPRESSION_FALLBACK` | String | `"Gzip"`, `"Brotli"` or empty |
| `USE_WASM`, `USE_THREADS`, `USE_WEBGL_1_0`, `USE_WEBGL_2_0`, `USE_DATA_CACHING` | Boolean | build flags |
| `LOADER_FILENAME`, `DATA_FILENAME`, `FRAMEWORK_FILENAME`, `CODE_FILENAME`, `MEMORY_FILENAME`, `SYMBOLS_FILENAME`, `BACKGROUND_FILENAME` | String | generated file names |

**Three things the shipped templates use that this table does not list** (found by reading the
installed `Default/index.html` and `Minimal/index.html`, so treat the docs table as incomplete rather
than authoritative):

- `WORKER_FILENAME`, used inside `#if USE_THREADS`;
- `SHOW_DIAGNOSTICS`, used as a conditional and as a `<<<TemplateData/diagnostics.css>>>` **file
  include** — the `<<< >>>` include syntax is not documented on the variables page at all;
- **arbitrary JavaScript inside a macro**: `{{{ JSON.stringify(PRODUCT_NAME) }}}`,
  `{{{ SPLASH_SCREEN_STYLE.toLowerCase() }}}`,
  `{{{ BACKGROUND_FILENAME.replace(/'/g, '%27') }}}`. The manual says macros "can include multiple
  operators, loops, functions" but shows no example this concrete.

**Negative result — custom user variables.** Older manuals (2020.2–2022.3) document that any
JavaScript identifier you use in a macro that Unity does not recognise becomes a *custom user
variable*, surfaced as an editable field in Player Settings → Resolution and Presentation
([2021.2 manual](https://docs.unity3d.com/2021.2/Documentation/Manual/webgl-templates.html)). **The
6000.4 variables page does not mention this mechanism at all.** I could not confirm whether it still
works in Unity 6.4. If a template wants a configurable value, hard-code it or read it from the page
URL rather than betting on this.

### Selecting the template from a build script

`PlayerSettings.WebGL.template` is a plain `public static string`
([ScriptReference](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/PlayerSettings.WebGL-template.html)).
The docs describe it only as "path to the WebGL template asset" and give no format, but the format is
confirmed twice over:

- from this repo's own settings file — `MimasClient/ProjectSettings/ProjectSettings.asset:806` reads
  **`webGLTemplate: APPLICATION:Default`**;
- from [Unity Discussions](https://discussions.unity.com/t/setting-webgl-template-programmatically/641448),
  where the working form for a custom template is `PlayerSettings.WebGL.template = "PROJECT:MyTemplateName"`.

So: `"APPLICATION:Default"` / `"APPLICATION:Minimal"` / `"APPLICATION:PWA"` for built-ins,
`"PROJECT:<FolderName>"` for anything in `Assets/WebGLTemplates/`.

**This matters more than it looks for Mimas.** `MimasClient/Assets/_Game/Editor/WebBuild.cs` states
every build setting that matters in code (compression, decompression fallback, hashes, data caching,
threads, exceptions, stripping) precisely so no Editor is needed and no `.asset` gets hand-edited. It
does **not** set `template`. And the `Web Release` build profile carries its own snapshot —
`MimasClient/Assets/Settings/Build Profiles/Web Release.asset:833` also reads
`webGLTemplate: APPLICATION:Default`, alongside the `webGLExceptionSupport: 0` trap already recorded
in `STATE.md`. Setting the template only in ProjectSettings would leave ladder rung 9's
`--profile "Web Release"` building the stock template. One line in `WebBuild.cs` is the robust route.

---

## 2. Full-viewport and responsive canvas

### The distinction that decides everything

Unity states it directly
([Configure a Web Canvas size, 6000.4](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-canvas-size.html)):

> "The Canvas element CSS size specifies the visible area on the web page that the canvas takes up" …
> "WebGL render target size specifies the pixel resolution that the GPU renders your game to."

and

> "By default, Unity keeps the canvas element CSS size and the WebGL render target size in sync and
> provides 1:1 pixel perfect rendering." … "By default, this match is done to implement high DPI
> rendering."

The exact rule, from the config reference
([web-templates-build-configuration, 6000.4](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-build-configuration.html)),
verbatim:

> `matchWebGLToCanvasSize` — "By default (if set to true or unset), Unity synchronizes the WebGL canvas
> render target size with the Document Object Model (DOM) size of the canvas element (scaled by
> `window.devicePixelRatio`). Set this to false if you want to set the canvas DOM size and WebGL render
> target sizes manually."

So the drawing buffer is **CSS size × devicePixelRatio**, maintained by the engine.

### The correct 2026 way to fill the window

**Leave `matchWebGLToCanvasSize` alone and do it in CSS.** Unity's own page gives the markup:

```html
<div id="unity-container" style="position: absolute; width: 100%; height: 100%">
  <canvas id="unity-canvas" width={{{ WIDTH }}} height={{{ HEIGHT }}} style="width: 100%; height: 100%"></canvas>
</div>
```

and confirms resize tracking comes free: "If the web page in JavaScript modifies the canvas CSS size,
Unity will automatically adjust the WebGL render target size to match it." A `window.resize` listener
is not required. This is also what the built-in Default template already does on *mobile* — so the
pattern is Unity's, not a community hack.

**Negative result — no Player Settings resize policy.** Unity 6.4's Web Player settings expose only
*Default Canvas Width*, *Default Canvas Height* and *Run In Background* under Resolution and
Presentation ([class-PlayerSettingsWebGL](https://docs.unity3d.com/6000.4/Documentation/Manual/class-PlayerSettingsWebGL.html)).
There is no "fit to window", no DPI toggle, and no scaling mode. Presentation is the template's job by
design.

### What breaks if you get it wrong

| Mistake | Symptom | Source |
|---|---|---|
| CSS-stretch the canvas without letting Unity resize the buffer (i.e. `matchWebGLToCanvasSize=false` and forget to resize) | Blurry upscaled output; **and input coordinates drift** — "the game is rendered correctly but the touch/input layer is smaller" | [Unity Discussions](https://discussions.unity.com/t/webgl-matchwebgltocanvassize-false-how-to-resize/817874) |
| `matchWebGLToCanvasSize=false` **plus** fullscreen | Backbuffer destroyed on entering fullscreen and never recreated. Unity's own words: "when the canvas goes into fullscreen but matchWebGLToCanvasSize isn't true, then the backbuffer got destroyed … but it didn't think the resolution changed so it didn't get re-created." UUM-127494, **fixed in 6000.3.1f1** (Dec 2025) — 6000.4 has the fix, but it is a standing argument for not hand-rolling this | [Unity Discussions](https://discussions.unity.com/t/webgl-config-setting-matchwebgltocanvassize-false-is-incompatible-with-fullscreen/1689915) |
| Full-viewport at native DPR on a 4K / retina display | Enormous drawing buffer, frame rate collapse — see §3 | [build configuration](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-build-configuration.html) |
| Nothing — the default is *correct* for sharpness | Text and UI are crisp at 1:1; this is why Unity ships it this way | [webgl-canvas-size](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-canvas-size.html) |

### Two things I could not settle

**Negative result — what `Screen.width` returns on Web.** It matters for the HUD: if `Screen.width`
reports drawing-buffer pixels (CSS × DPR) rather than CSS pixels, a `CanvasScaler` set to
*Scale With Screen Size* will make the whole HUD render at half its intended size on a 2× display. I
found no authoritative Unity statement either way, and the community threads on this contradict each
other. **This needs measuring in a browser, not researching**, and the existing
`tools/smoke/browser-smoke.mjs` can do it in one run by logging `Screen.width` next to
`canvas.clientWidth` and `devicePixelRatio`.

**`devicePixelRatio` is not constant.** Jukka Jylänki (Unity), on the Khronos public_webgl list:
"`window.devicePixelRatio` is not an immutable constant. In particular, it changes when you zoom the
browser view in or out"
([Khronos, 18 Jul 2017](https://www.khronos.org/webgl/public-mailing-list/public_webgl/1707/msg00029.php)).
A `config.devicePixelRatio` set once at load will not follow browser zoom, and per Unity staff,
changing it at runtime is "very costly" and causes stuttering
([Unity Discussions, 26 Oct 2020](https://discussions.unity.com/t/dynamic-resolution-render-scale-in-webgl-and-urp/808364)).

---

## 3. Resolution and device pixel ratio

### The default, restated because it is the whole problem

> `devicePixelRatio` — "This field enables forcing the DPI scaling ratio for the rendered page. Set to 1
> to force rendering to 'standard DPI' (or non-Retina DPI), which can help performance on lower-end
> mobile devices. **By default, this field is unset, meaning the rendered page uses the browser DPR
> scaling ratio, resulting in High DPI rendering.**"
> — [web-templates-build-configuration, 6000.4](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-build-configuration.html)

Unity's support article is blunt that there is no in-editor alternative: "Unity does not have an
internal option or setting to set this property, but we can fix it using JS"
([Unity Support, 20 Feb 2024](https://support.unity.com/hc/en-us/articles/214948483-WebGL-looks-wrong-on-High-resolutions-Retina)).

Today Mimas gets away with this because the canvas is 960×600. Going full-viewport is what makes DPR a
decision rather than a detail.

### The arithmetic (mine, clearly labelled as arithmetic)

Pixels scale with **DPR squared**, which is the part people get wrong.

| Case | Drawing buffer | Megapixels | vs today |
|---|---|---|---|
| Today: 960×600, DPR 1 | 960×600 | 0.58 | 1.0× |
| Full-viewport 1920×1080, DPR 1 | 1920×1080 | 2.07 | 3.6× |
| Full-viewport 1920×1080, DPR 2 (retina MacBook, 4K at 200%) | 3840×2160 | 8.29 | **14.4×** |
| Full-viewport 2560×1440, DPR 2 | 5120×2880 | 14.75 | **25.6×** |

The only measured anchor I could find for what a modest GPU can push through WebGL is Gregg Tavares's:
"on my late 2018 Macbook Air with Intel UHD Graphics 617 I can only draw about 5 million pixels per
frame at 60fps in WebGL"
([webglfundamentals](https://webglfundamentals.org/webgl/lessons/webgl-qna-optimize-drawing-lots-of-large-images.html)).
**Inference, flagged:** on a machine like that, a single full-screen opaque pass at DPR 2 on a 1080p
display already exceeds the 60 fps budget before URP's depth prepass, blit and any post. Mimas has HDR
on in `PC_RPAsset.asset` (`m_SupportsHDR: 1`), which means the colour target is likely 16-bit per
channel — double the bandwidth of an 8-bit target — so the real cost is worse than the pixel count
alone suggests. I did not verify the HDR format for the Web quality level; treat that sentence as a
reason to measure, not as a measurement.

**Negative result — nobody has published Unity-WebGL 1× vs 2× DPR numbers.** I looked through the
Unity forum threads, Unity blog, and shipped-game writeups on this topic and found **zero** benchmarks.
The delta for Mimas will have to be measured locally. The untracked `tools/smoke/browser-perf.mjs` in
the working tree is exactly the right instrument.

### What experienced developers actually do

Three levers, and the community does not agree on which:

1. **Cap DPR in the template** — `config.devicePixelRatio = 1`, or a clamp like
   `Math.min(window.devicePixelRatio, 1.5)`. One line, no C#, no Editor. Endorsed by Unity staff
   (jukka_j) as the sanctioned fix
   ([Unity Discussions, 6 Oct 2020](https://discussions.unity.com/t/webgl-retina-scaling/782203)).
   **Cost: everything softens, including uGUI and TMP text**, which is the thing retina users notice.
2. **URP Render Scale.** Also from jukka_j: "Dynamic Resolution is not supported on WebGL/OpenGL/GLES"
   (no GPU memory reallocation) but "URP RenderScale feature works, since it is based on a blitting
   path that upscales smaller render targets"
   ([Unity Discussions, 26 Oct 2020](https://discussions.unity.com/t/dynamic-resolution-render-scale-in-webgl-and-urp/808364)).
   Unity 6.4 describes it as "This slider scales the render target resolution (not the resolution of
   your current device). Use this when you want to render at a smaller resolution for performance
   reasons"
   ([URP Asset reference](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/universalrp-asset.html)),
   set in the URP Asset's Quality section or at runtime via `renderScale`
   ([change URP asset settings](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/quality/change-urp-asset-settings.html)).
   Mimas's `PC_RPAsset.asset` currently has `m_RenderScale: 1`, `m_MSAA: 1` (off).
3. **Both** — keep high DPI so the UI stays crisp, drop Render Scale so the 3D costs less. A developer
   in the retina thread reported exactly this: "UI obviously much better, super crisp!"
   ([Unity Discussions, 1 Apr 2020](https://discussions.unity.com/t/webgl-retina-scaling/782203)).
   This works because a **Screen Space – Overlay** canvas draws to the backbuffer after URP's passes
   and so is not downscaled. ⚠️ The clearest write-up of that mechanism is a third-party troubleshooting
   site, not Unity ([unity-trouble-atlas](https://unity-trouble-atlas.7colorsgame.com/en/article/unity-urp-render-scale-blurry-ui/)),
   and it notes the exception: URP camera stacking with an Overlay UI camera forces the UI into the
   base camera's downscaled target. **Treat as unverified against Unity docs.** It also matters that
   the design page specifies a **UI Toolkit** HUD (`docs/design/index.html#presentation`), not uGUI, and
   I did not establish whether UI Toolkit's runtime panel behaves like a Screen Space – Overlay canvas
   under Render Scale. That is a second thing to measure.

**Negative result — Unity does not recommend a lever for the web.** I checked all three "Optimize your
Web build" sub-pages
([index](https://docs.unity3d.com/6000.4/Documentation/Manual/web-optimization.html),
[quality](https://docs.unity3d.com/6000.4/Documentation/Manual/web-optimization-quality.html),
[graphics](https://docs.unity3d.com/6000.4/Documentation/Manual/web-optimization-graphics.html)) and
[Web performance considerations](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-performance.html).
None mentions render scale, resolution or DPI. The web guidance is only "If you set your Quality Level
to a lower setting … it results in faster load times and better performance", plus "the CPU side
dispatch of WebGL operations is slower than in native OpenGL". "Render Scale is the web lever" is a
Unity staff opinion in a 2020 forum thread, not documentation.

For context on how the wider WebGL community sees native DPR — Gregg Tavares: "Blindly using
`devicePixelRatio` can really slow down your performance … 99% of the time I don't use
`devicePixelRatio`"
([webglfundamentals](https://webglfundamentals.org/webgl/lessons/webgl-resizing-the-canvas.html)); and
on the Khronos list, "WebGL demos which render at Retina resolution almost always have very poor
performance unless the fragment shader is extremely simple"
([Khronos, 18 Jul 2017](https://www.khronos.org/webgl/public-mailing-list/public_webgl/1707/msg00029.php)).
Unity's default is the minority position among WebGL developers.

---

## 4. Fullscreen

### The two APIs

**`unityInstance.SetFullscreen(1)`** is documented on
[web-templates-build-configuration](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-build-configuration.html):
"You need to use a user interaction like a button or key press to activate fullscreen mode. You can't
activate fullscreen mode on startup." It is what the built-in Default template's footer button calls.
Reading Unity's own implementation on this machine
(`…\WebGLSupport\BuildTools\prejs\FullScreen.js`):

```js
Module["SetFullscreen"] = function (fullscreen) {
  …
  var tmp = JSEvents.canPerformEventHandlerRequests;
  JSEvents.canPerformEventHandlerRequests = function () { return 1; };
  Module.ccall("SetFullscreen", null, ["number"], [fullscreen]);
  JSEvents.canPerformEventHandlerRequests = tmp;
};
```

— it temporarily suppresses Emscripten's own user-gesture deferral so the request goes straight
through. The *browser* still enforces the gesture requirement; Unity is only getting out of the way.

**From C#**, `Screen.fullScreen = true`
([Input in Web, 6000.4](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-input.html)), which
uses `Element.requestFullscreen` underneath.

### Which element goes fullscreen, and why it decides the DOM question

`SetFullscreen` fullscreens **the `<canvas>` element**. Per the
[Fullscreen spec](https://fullscreen.spec.whatwg.org), the fullscreen element and its subtree paint
over a backdrop that covers the rest of the document — so anything that is a *sibling* of the canvas
vanishes. This is not theoretical: Vuplex ships a build-time patch for exactly this, documenting that
`SetFullscreen(1)` "prevents 2D WebView's `<iframe>` elements from being visible" and that their fix
"makes the canvas element's parent go fullscreen instead so that 2D WebView's iframe elements remain
visible" ([Vuplex support](https://support.vuplex.com/articles/webgl-fullscreen/)).

**So: if there is ever DOM UI over the canvas, call `requestFullscreen()` on `#unity-container`, not
`SetFullscreen`.** That is a three-line difference in the template and it is much cheaper to get right
on day one than to retrofit.

### Browser behaviour in 2026

Unity 6.4's [Input in Web](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-input.html) is
the current statement:

- "Due to security concerns, browsers only allow you to lock your cursor and enable full-screen mode
  after a user-initiated event such as a mouse-click or key press."
- Because Unity defers event handling, "use mouse/key down events to trigger responses instead of
  mouse/key up events."
- "As browser support for full-screen mode varies, refer to the Mozilla documentation on the Element:
  requestFullscreen() method."
- On Esc: **Safari blocks Esc for fullscreen switching; Chrome's behaviour is unpredictable.** Only Esc
  exits once fullscreen is active.

**Negative result — no 2026-specific browser breakage found.** I looked for recent Chrome/Firefox/Safari
regressions around `requestFullscreen` on a WebGL canvas and found none. The API has been stable; the
2026 answer is the same as the 2022 answer.

**Pointer lock** is `Cursor.lockState = CursorLockMode.Locked`, with
`WebGLInput.stickyCursorLock` (default `true`) keeping the lock when the browser releases it
(same page). **Mimas does not need it** — hex tiles clicked with a visible cursor, no mouselook. Worth
saying explicitly so nobody builds it.

### Does fullscreen change the drawing buffer? Yes, and it is the expensive moment

With `matchWebGLToCanvasSize` at its default, fullscreen sets the canvas CSS size to the screen size,
so the buffer becomes **screen × DPR**. A player on a 1440p display at DPR 1 who presses the fullscreen
button moves from 0.58 to 3.7 megapixels (6.4×); at DPR 2 it is 14.75 (25.6×). That is the single
largest performance cliff available to a user in one click, and it is the reason a DPR cap belongs in
the same change as a fullscreen button, not after it.

One portal constraint, if portals ever matter: **CrazyGames explicitly prohibits custom in-game
fullscreen buttons** — see §7.

---

## 5. The loading experience

### Replacing Unity's loading bar

The hook is the third argument to `createUnityInstance`
([web-templates-structure, 6000.4](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-structure.html)):

```js
createUnityInstance(canvas, config, onProgress).then(onSuccess).catch(onError);
```

> "The Web loader calls the `onProgress` callback object every time the download progress updates." …
> "The `progress` argument that comes with the `onProgress` callback determines the loading progress as
> a value between 0.0 and 1.0."

Your template owns the DOM it updates. There is nothing to defeat — the Default template's bar is
`<div id="unity-progress-bar-full">` in that same file, and deleting it deletes it.

**Negative result — what `progress` measures is undocumented.** Unity says "download progress, 0.0 to
1.0" and nothing more. There is no published breakdown of how it weights `dataUrl` vs `frameworkUrl` vs
`codeUrl`, nor whether wasm instantiation is included. I searched specifically for this and found no
authoritative source.

Two known behaviours, both worth designing around:

- **Non-linearity with Brotli.** Unity's issue tracker recorded progress jumping "from 0 to 90%" with
  Brotli-compressed builds, reproduced on 2020.2/2021.1/2021.2 and **fixed in 2023.1.0a20**. ⚠️ The
  tracker URL now 302s to a 404 after Unity's migration, so I have this from the search index only —
  **treat the version numbers as second-hand**. The practical read for a 6000.4 Brotli build: the fix
  predates you, *provided nginx sends `Content-Encoding: br`* so the browser can count bytes. This ties
  §5 to §8.
- **Chunky updates regardless.** Long-standing community observation that the value "doesn't update
  live, instead jumping in big chunks"
  ([ocias.com](https://ocias.com/blog/unity-webgl-custom-progress-bar/)). The two standard mitigations
  are to clamp it monotonic and to CSS-transition the bar width over ~500 ms so jumps read as motion.
  Community practice, not Unity guidance.
- **The black gap.** Multiple developers report a black frame between the HTML loading bar being hidden
  and Unity's first frame, across all built-in templates, with no engine fix and no Unity reply
  ([Unity Discussions](https://discussions.unity.com/t/black-screen-before-splash-screen-webgl/1506073)).
  The remedy is to keep your own overlay up *past* `onSuccess` and fade it on a signal from the game
  itself. Mimas already has the right signal: `browser-smoke.mjs` waits on
  `[ContentBootstrap] Content loaded`.

### The Unity splash screen — the answer changed, and this repo has not caught up

**Yes, it can be disabled on Personal, from Unity 6 (6000.0) onward.** Three independent sources:

1. Unity blog, **12 September 2024**: "The Made with Unity splash screen will become optional for Unity
   Personal games made with Unity 6 when it launches later this year."
   ([Unity is Canceling the Runtime Fee](https://unity.com/blog/unity-is-canceling-the-runtime-fee))
2. **Unity 6000.0.0f1 release notes**, Changes → License: "Added the option on Unity Personal to disable
   or customize the Made with Unity splash screen."
   ([6000.0.0f1 what's new](https://unity.com/releases/editor/whats-new/6000.0.0f1)) — this is the
   definitive "from which version".
3. Unity pricing, Personal plan: "Splash screen optional with Unity 6."
   ([pricing updates](https://unity.com/products/pricing-updates))

The documentation corroborates by omission. The 2022.3 Splash Image page lists the Personal
restrictions verbatim — "You can't disable the Show Splash Screen setting", "You can't disable the Show
Unity Logo setting", "The Overlay Opacity setting has a minimum value of 0.5"
([2022.3](https://docs.unity3d.com/2022.3/Documentation/Manual/class-PlayerSettingsSplashScreen.html)).
The [6000.4 page](https://docs.unity3d.com/6000.4/Documentation/Manual/class-PlayerSettingsSplashScreen.html)
has **none** of them. The API is `PlayerSettings.SplashScreen.show`
([ScriptReference](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/PlayerSettings.SplashScreen-show.html)),
with no licence-tier note — so it is settable from `WebBuild.cs` the same way every other setting there
is.

**State of this repo:** `MimasClient/ProjectSettings/ProjectSettings.asset:20` reads
`m_ShowUnitySplashScreen: 1`. The splash is on. `tools/smoke/browser-smoke.mjs` says so in its own
comments: "Unity's splash screen is still on the canvas for a few seconds after that, so booted is NOT
playable."

**One gotcha to expect:** the only Unity 6 thread reporting "can't customize splash screen on Personal"
resolved as user error — **Build Profiles were overriding Player Settings**; the reporter marked it
`[SOLVED]` on 29 Oct 2024
([Unity Discussions](https://discussions.unity.com/t/solved-cant-customize-splash-screen-on-unity-6/1544245)).
Given that `Web Release.asset` already overrides `webGLExceptionSupport`, assume it will do the same
here and set it in code.

### What a good Unity web game shows during a ~12 MB download

The clearest published thresholds are CrazyGames'
([Getting to the first frame](https://docs.crazygames.com/resources/getting-to-the-first-frame/)):

> "<100 ms: Perceived as instant. No feedback needed." / "1 second: Feels fine, does not break flow." /
> "10 seconds: Attention starts to wander. You must show feedback or preload activities."

and the framing that matters: "The goal is not necessarily to load instantly … The goal is to make
loading feel instant by providing immediate feedback."

Poki's guidance is blunter still
([Easy access](https://developers.poki.com/guide/easy-access)): "a loading bar gives players a sense of
progress"; "A visually engaging screen with your logo or game art prevents early drop-offs"; and "Skip
splash screens, title screens, and level selects. Let them jump straight into the good part."

**Inference, flagged:** the only way to hit the "<100 ms immediate feedback" bar is an HTML/CSS
pre-loader that paints *before* `loader.js` is even requested — inline CSS, background colour or art,
game title, no JS dependency — because `onProgress` cannot fire until the loader script itself has
downloaded. I found no Unity page or writeup that states this explicitly; it follows from the
thresholds plus the template structure.

**Size context.** Johannes Deml's UnityWebGL-LoadingTest benchmarks 700+ builds across 2018.4–6000.6
([GitHub](https://github.com/JohannesDeml/UnityWebGL-LoadingTest)). A near-empty **URP WebGL2** build is
**7.52 MB** Brotli on 6000.6 against 3.76 MB for built-in, with the note "URP adds additional ~2.5 MB
file size compared to the builtin render pipeline". **Inference:** Mimas's 12.38 MB is squarely in the
expected band for a real URP game, roughly 7–8 MB of it engine you cannot strip. The win available here
is in the loading *experience*, not in shaving the download.

---

## 6. The HTML shell around the game

### Is there a case? Yes, and it is narrow and specific

Unity endorses the hybrid pattern in its own learning material: "Modifying a custom template, or
embedding the content into an existing website brings the possibility of the Unity game connecting with
other elements on the web page … a game might be running in a website alongside a HTML text chat with
other players" ([Unity Learn, Getting started with Unity Web](https://learn.unity.com/tutorial/getting-started-with-unity-web),
modified 17 Oct 2024).

The candidates in Mimas — a lobby, a four-letter room-code box, a share link — are text-entry and
link-shaped, which is precisely where a WebGL canvas is weakest: paste, autofill, IME, soft keyboards
and "copy this link" are all things the browser does for free and Unity has to reimplement.

### The interop, in Unity 6.4

Unity 6.4 renamed the page *titles* from "WebGL" to "Web" but kept most file names; new child pages use
a `web-` prefix. Parent index:
[Interaction with browser scripting](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-interactingwithbrowserscripting.html).

- **C# → JS**: a `.jslib` plugin with `mergeInto(LibraryManager.library, { … })` plus
  `[DllImport("__Internal")] private static extern void Hello();`
  ([setup](https://docs.unity3d.com/6000.4/Documentation/Manual/web-interacting-browser-js.html),
  [calling](https://docs.unity3d.com/6000.4/Documentation/Manual/web-interacting-browser-js-to-unity.html)).
  Documented constraint, verbatim: **"Unity currently supports ECMAScript 5 (ES5) syntax in .jslib and
  .jspre files. ES6 syntax isn't yet supported."** Strings arrive as pointers (`UTF8ToString`).
- **JS → C#**: `MyGameInstance.SendMessage(objectName, methodName, value)`
  ([docs](https://docs.unity3d.com/6000.4/Documentation/Manual/web-interacting-browser-unity-to-js.html)).
  Verbatim restrictions: "You can only call methods of a GameObject, not general C# methods attached to
  other objects", and "Methods with more than one parameter or with parameters of other types can't be
  called using SendMessage" — no-arg, one `string`, or one number only.
- **Deprecated, so ignore older blog code**: `Application.ExternalCall` is obsolete in 6.4
  ([ScriptReference](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Application.ExternalCall.html));
  `dynCall()` → `makeDynCall()`, `Pointer_stringify()` → `UTF8ToString()`, `gameInstance` →
  `unityInstance` ([deprecated APIs](https://docs.unity3d.com/6000.4/Documentation/Manual/web-interacting-browser-deprecated.html)).

Practical pitfall worth writing down: **`SendMessage` calls made before Unity finishes loading vanish
silently**, and booleans/objects fail silently too
([dev.to, Dec 2025](https://dev.to/raw-fun-gaming/building-a-web-unity-webgl-bridge-a-practical-guide-3nbe)).
Any DOM→Unity channel needs a readiness queue.

### Keyboard input — the one switch, and its side effects

Unity 6.4's [Input in Web](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-input.html), verbatim:

> "By default, Unity Web processes all keyboard input the web page receives, regardless of whether the
> Web canvas has focus or not… If you introduce HTML elements (such as text fields) in a web page
> that's meant to receive keyboard inputs, it can cause errors. Unity consumes the input events before
> the rest of the page can receive them. To make HTML elements receive a keyboard input, set
> `WebGLInput.captureAllKeyboardInput` to `false`."

`UnityEngine.WebGLInput.captureAllKeyboardInput` is a `public static bool`, **default `true`**
([ScriptReference](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/WebGLInput-captureAllKeyboardInput.html)).
The common pairing is to set it `false` *and* give the canvas a `tabIndex` so it can be focused
([react-unity-webgl docs](https://react-unity-webgl.dev/docs/api/tab-index) — note Unity's Default
template already sets `tabindex="-1"`, which is focusable by script but not by Tab).

Two documented costs:

- Keys can **stick down** when the canvas loses focus with the new Input System
  ([Unity Discussions](https://discussions.unity.com/t/keyboard-input-gets-stuck-when-webgl-canvas-loses-focus/862905),
  reported Nov 2021, still being asked about Oct 2024, `InputSystem.TryResetDevice` reported not to fix
  it). **No documented fix found.**
- **Negative result:** Unity documents no toggle-on-focus/blur recipe. That pattern is community-only.

### The bug that decides it for Mimas

**The new Input System registers its mouse listeners on `document`, not the canvas.** Verbatim from the
thread: "this event fires regardless if the user clicks on the Unity canvas or not. If the user is
clicking on an HTML element that is on top of the Unity canvas, the input system will still detect it
as a click"
([Unity Discussions](https://discussions.unity.com/t/new-input-system-listens-for-click-event-on-whole-document-instead-of-canvas/915544)).
Root cause identified in-thread as `emscripten_set_mousedown_callback_on_thread` being given target `2`
(document) rather than the canvas selector. Bug IDs **IN-41716** (May 2023) and **IN-23815**; IN-23815
was closed for lack of a repro ([feature request thread](https://discussions.unity.com/t/webgl-be-able-to-configure-click-event-listener/900808));
the thread is unresolved as of Jul 2024. The **legacy** `Input` class does not have this problem.

Mimas is on the new Input System exclusively — `activeInputHandler: 1` in
`MimasClient/ProjectSettings/ProjectSettings.asset:928`, `com.unity.inputsystem 1.19.0` in
`MimasClient/Packages/manifest.json`. **Negative result:** I checked the Input System changelog through
1.14.2 and found no entry fixing document-vs-canvas targeting, and could find no "fixed in X" statement
anywhere. **Inference, flagged:** it is probably still live in 6.4 with 1.19.0, but nobody has said so.
Any DOM button placed over the canvas must be tested for click pass-through before it is trusted.

### Overlay technique

**Negative result — Unity documents none of this.** I searched the 6000.x manual for `pointer-events`,
z-index or canvas focus handoff and found nothing. The standard recipe is community-sourced and simple:
absolutely position the overlay above the canvas by `z-index`; put `pointer-events: none` on the
overlay *container* and `pointer-events: auto` only on the genuinely interactive children
([LearnWebGL overlays](http://learnwebgl.brown37.net/11_advanced_rendering/overlays.html)). Combined
with the Input System bug above, `pointer-events: none` on the container is doing more work than it
looks — it is the only thing preventing stray clicks, and it does not help for the children that must
be clickable.

A directly relevant precedent exists: [rehanlabs/Unity-WebGL-HTML-InputFix](https://github.com/rehanlabs/Unity-WebGL-HTML-InputFix)
is a custom Web template plus `.jslib` that shows a native `<input>` over the canvas when a TMP field
is selected and returns the value via `unityInstance.SendMessage()`, tested on Unity 6.0.51f1. That is,
almost exactly, a room-code box. A Unity-side alternative is
[kou-yeung/WebGLInput](https://github.com/kou-yeung/WebGLInput) (IME support, TMP since 2018.2).

### react-unity-webgl — what it actually buys you

- [GitHub](https://github.com/jeffreylanters/react-unity-webgl) ·
  [docs](https://react-unity-webgl.dev) ·
  [npm](https://www.npmjs.com/package/react-unity-webgl), Apache-2.0.
- **Current version 10.2.0, published 2026-05-08**; 10.1.6 on 2025-10-14; 10.1.5 on 2025-07-30. Alive,
  but two or three releases a year.
- Unity 6 is tracked: 10.2.0 exposes `showBanner`, `print` and `printErr` on the config, described as
  available since **Unity 6.3**. **Negative result:** no statement anywhere naming 6000.4 specifically.
- What it gives you: `useUnityContext()` returning `unityProvider`, `loadingProgression`, `isLoaded`,
  `sendMessage`, `addEventListener`/`removeEventListener`; a `<Unity>` component; helpers for
  fullscreen, pointer lock, canvas styles, `tabIndex`, screenshots
  ([API index](https://react-unity-webgl.dev/docs/api/introduction)).
- **Honest verdict for Mimas: no.** It is a React lifecycle wrapper — mount/unmount safety, re-render
  safety, a typed event bus. Without React you get none of that, and everything it wraps is three lines
  of vanilla JS. The one idea worth stealing is its event-listener registry: Unity calls **one** global
  dispatcher, which fans out to named handlers. Adopting React to get a `<canvas>` would be the largest
  possible change for the smallest possible gain 22 days before a playtest.

### The case against DOM UI

1. **Fullscreen hides DOM siblings** (§4) unless you fullscreen the container.
2. **Two UI systems** to style, animate and keep consistent. The design page specifies a UI Toolkit HUD
   (`docs/design/index.html#presentation`); a DOM lobby means a second stylesheet that must not look
   like a different game.
3. **Input focus fights**: sticky keys, canvas focus loss
   ([Unity Discussions](https://discussions.unity.com/t/loosing-focus-on-webgl-when-interacting-with-ui-elements/801934)),
   and the document-level click bug.
4. **`SendMessage` is stringly-typed and fire-and-forget**, with GameObject names as a brittle global
   namespace.
5. **Negative result:** I found no first-hand postmortem from a shipped 1v1 web game that moved its
   lobby to DOM and reported back. Nobody has run this experiment in public.

---

## 7. What the web-game portals require

Portals are not Mimas's distribution channel on 10 October, and `pillars.md` says "Not a mobile game"
outright. I include this because the question asked, and because their hard requirements are the
closest thing to an industry definition of "does not feel like a premade box" — and two of them
disqualify the stock template by name.

### Poki — strictest on canvas, and ships a Unity template

[Requirements & quality](https://developers.poki.com/guide/requirements-quality). Under its own **"Hard
requirements"** heading:

- **"16:9 aspect ratio. Your game must scale to cover the full canvas. Scale proportionally to 640x360,
  836x470, or 1031x580."**
- "Desktop, mobile, and tablet support. On mobile, cover the full screen in portrait or landscape…"
- Incognito support (wrap `localStorage` in try/catch), no external requests, playable with an ad
  blocker.

Under Technical standards: "Small file size. Players tend to move to another game if loading takes more
than 10 seconds." **No hard MB cap is stated.** Their
[engine page](https://developers.poki.com/guide/web-game-engines) gives the target: "Without
optimisation applied, a Unity build is around 11MB in size" against a recommended 5–8 MB, and calls
Unity "overall less well-suited for the web".

**Poki ships a Unity WebGL template** ([SDK: Unity](https://developers.poki.com/guide/sdk-unity)) —
`https://game-cdn.poki.com/wrappers/v3/unity2022.zip`, which is also what the page lists for **Unity
6**. Install is "copy the `WebGLTemplates` directory from the downloaded zip into your Unity game's
`Assets` directory, then select the template in Player Settings". `gameLoadingFinished()` is marked
required. ⚠️ *Inference:* selecting Poki's template necessarily replaces Default, so the Unity footer
goes; the page does not say so in words and the zip's contents were not inspected.

**Fullscreen is not mentioned** in Poki's requirements — explicit negative, checked for.

### CrazyGames — hard numbers, and a fullscreen prohibition

[Technical requirements](https://docs.crazygames.com/requirements/technical/): max **250 MB** total and
**1500 files**; **initial download ≤ 50 MB**, **≤ 20 MB to be eligible for the mobile homepage**;
time-to-gameplay **≤ 20 s**; relative paths only; must run on a 4 GB Chromebook. **Unity games are
disabled on iOS by default** pending performance evaluation.

[Gameplay requirements](https://docs.crazygames.com/requirements/gameplay/) is the one that names the
box: the game must stay legible at nine specific 16:9 iframe sizes — desktop **907×510, 1216×684,
1077×606, 821×462**; fullscreen **1366×768, 1920×1080, 1536×864, 1280×720**; mobile 800×450; tablet
1080×607 — and, verbatim, **"Text and images must be legible on devices with a `devicePixelRatio:1`"**.
Also verbatim: **"Custom in-game fullscreen buttons are prohibited, as they can interfere with other
features (e.g. monetization)."** Their quality guidelines add: avoid the `Escape` key, because it exits
their fullscreen ([quality](https://docs.crazygames.com/requirements/quality/)).

[Optimization tips](https://docs.crazygames.com/resources/optimization-tips/) *recommends* "Disable the
Unity splash screen (Unity 6+)" — and reports their own measured 14.7 MB → 12.5 MB from *Disk Size with
LTO* plus *Faster (smaller) builds*.

**CrazyGames ships no WebGL template** — explicit negative, checked across their SDK, custom-build and
optimizer pages. They ship a C# SDK and a server-side build service.

### itch.io — no requirements, only limits and switches

[HTML5 docs](https://itch.io/docs/creators/html5). Limits: **1000 files**, **500 MB extracted**, **200 MB
per file**, paths ≤ 240 chars. Everything else is a switch you choose: *Embed in page* (you give
dimensions) vs **"Click to launch in fullscreen"**; *Click to Play* on by default ("This will ensure
your game doesn't slow the viewer's browser when the itch.io page initially loads"); itch **generates a
fullscreen button** for in-page embeds; the *Mobile Friendly* flag forces fullscreen launch on mobile
regardless of your desktop embed setting.

itch's own docs recommend a **third-party** Unity template for responsive sizing:
[Better Minimal WebGL Template](https://seansleblanc.itch.io/better-minimal-webgl-template) (MIT;
scales the canvas to fit the window while maintaining aspect ratio, centres it, custom background
colour, loading bar). Last substantive update **Dec 2021**, targets 2020.2+; comments report it working
on Unity 6 (6000.x). Useful as a reference for how short a good template can be — **not** as a
dependency, given the age.

### Newgrounds — **Negative result, stated plainly**

I could not reach any authoritative Newgrounds page. `newgrounds.com` is behind a bot wall ("NG Guard")
and returned **403** for every wiki path tried, including the submission-guidelines and game-guidelines
URLs. What is reachable is the sibling API site: [newgrounds.io](https://www.newgrounds.io/) ("the
successor to the popular Newgrounds Flash API"), whose
[downloads page](https://www.newgrounds.io/downloads/) lists three **community-maintained** Unity
libraries and no first-party one, and whose [get-started](https://www.newgrounds.io/get-started/) covers
medals and scoreboards only — not canvas size, scaling, fullscreen or hosting. **Newgrounds publishes no
canvas or presentation requirement that I was able to verify.** The "10 MB limit" figure circulating in
search snippets traces to Wikigrounds and community tutorials, not to Newgrounds' own docs; I would not
act on it.

### What this is worth to Mimas

Two free disciplines that cost nothing and are good design regardless of portals:

- **Cover a 16:9 canvas, and stay legible down to ~820×460 at `devicePixelRatio: 1`.** That is
  CrazyGames' bar and it is a good proxy for "readable on a friend's cramped laptop", which is exactly
  the 10 October scenario.
- **Do not bind `Escape`.** It is a fullscreen exit everywhere and a portal exit on CrazyGames.

One thing to *not* do if portals are ever a target: an in-game fullscreen button. A button in the HTML
shell is fine; one drawn inside Unity is prohibited on CrazyGames and would have to be removed.

---

## 8. Serving

### Compression and MIME types

[Deploy a Web application, 6000.4](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-deploying.html):
`Content-Encoding: br` for `.br` files, `Content-Encoding: gzip` for `.gz`; **"Chrome and Firefox only
natively support Brotli compression over HTTPS"**; `Content-Type: application/wasm` for `.wasm`, which
"allows the browser to compile the WebAssembly code while it's still downloading"; and "WebAssembly
streaming doesn't work together with JavaScript decompression when the Decompression Fallback option is
enabled" — which is why "Enabling decompression fallback results in a large loader size and a less
efficient loading scheme."

`docs/hosting.md` and `WebBuild.cs` already have all of this right: Brotli on, decompression fallback
off, `application/wasm` + `br` in `tools/deploy/nginx-mimas.conf`, HTTPS via Let's Encrypt. **Nothing to
change.** The one thing to keep in mind is the link to §5: if `Content-Encoding: br` is ever dropped,
the loading bar degrades along with the download.

### COOP / COEP / SharedArrayBuffer — **not needed, and that is a deliberate choice**

The cross-origin isolation headers (`Cross-Origin-Opener-Policy: same-origin`,
`Cross-Origin-Embedder-Policy: require-corp`, `Cross-Origin-Resource-Policy: cross-origin`) are required
**only** when the build has *Enable Native C/C++ Multithreading* on, because that is what needs
`SharedArrayBuffer`
([server configuration guidance, Unity manual](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-deploying.html)
and the platform-specific pages, e.g.
[Apache](https://docs.unity3d.com/6000.3/Documentation/Manual/web-server-config-apache.html)).

`WebBuild.cs:74` sets `PlayerSettings.WebGL.threadsSupport = false` with the comment "no
SharedArrayBuffer, so no COOP/COEP headers". That is correct and it is worth keeping: enabling
cross-origin isolation would break any third-party embed and any cross-origin asset. **For a
single-threaded WebGL2 build, none of it matters.** Note the deploy page itself does not discuss these
headers at all — they live on the per-server pages.

### Caching

[Cache behavior in Web, 6000.4](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-caching.html):
*Data Caching* lets the browser cache `.data` files in **IndexedDB**, "above the browser limit", since
the HTTP cache has limited space. The `cacheControl(url)` callback in the template config returns one of
`must-revalidate`, `immutable` or `no-store`
([JavaScript interface](https://docs.unity3d.com/6000.4/Documentation/Manual/web-js-interface.html)).
**Negative result:** Unity recommends no specific HTTP `Cache-Control` values; it just points at MDN.
`WebBuild.cs` already sets `nameFilesAsHashes = true` and `dataCaching = true`, and `docs/hosting.md`
already plans an immutable cache rule for `/Build/*`, which is the correct pairing — hashed names are
what make `immutable` safe.

A custom template is also where `cacheControl` would live, if it were ever wanted. It is not needed for
10 October.

### WebGPU — no, not for this build, and the reason is a date

- **In Unity 6000.4, WebGPU is experimental.** The 6.4 manual page is titled *WebGPU (Experimental)* and
  says "WebGPU is experimental and not supported by all browsers and devices"
  ([docs](https://docs.unity3d.com/6000.4/Documentation/Manual/WebGPU.html)).
- **It graduated in Unity 6000.6.** Unity's Web Graphics team (brendanduncan_u3d), **24 August 2026**:
  "as of Unity 6000.6, the WebGPU graphics API is no longer experimental — it is now a fully supported
  feature"
  ([Unity Discussions](https://discussions.unity.com/t/webgpu-out-of-experimental-in-unity-6-6/1734694)).
  Unity 6.6 shipped around **1 September 2026**
  ([GameFromScratch](https://gamefromscratch.com/unity-6-6-released/),
  [AlternativeTo](https://alternativeto.net/news/2026/9/unity-6-6-adds-webgpu-build-analysis-and-coreclr-prep/)).
  Even there it is **not enabled by default**, requires a **secure context (HTTPS)** — builds from
  `file://` or plain HTTP silently fall back to WebGL 2 — and ships with a compatibility mode plus
  Graphics Device Filtering for fallback.
- **For Mimas in 2026: no.** Using it means upgrading the editor from 6000.4.4f1 to 6000.6 twenty-two
  days before the playtest, re-validating every WebGL-specific path that T-0003 just finished proving
  (jslib socket, IndexedDB `PlayerPrefs`, Brotli, shader stripping), and re-testing on a graphics
  backend nobody has run this game on. Mimas is a turn-based hex game with an unlit-ish URP scene; it is
  not fill-rate-starved in a way that a new graphics API fixes. The cheap version of "more headroom" is
  in §3, not here.
- Worth a note in the roadmap, not in M2: WebGPU would unlock compute shaders and VFX Graph on the web,
  both of which golden rule 9 currently forbids.

---

## What I would do for Mimas

### What matters here

Three criteria, named before the options.

1. **Does it stop feeling like a demo, for a friend opening a link on 10 October?** That is pillar 5 —
   "It opens in a browser and it is immediately legible." A page whose tab says *Unity Web Player |
   MimasClient* and whose game sits in a 960×600 box under a Unity wordmark answers that question
   badly before the first turn.
2. **What does it cost against the 10 October date?** M2-7 (deploy) is "not started" and `STATE.md`
   calls it "the whole of what stands in the way". Presentation work that eats deploy days is a bad
   trade no matter how good it looks. Studio PLAN.md §10 puts the fallback trigger at **Fri 2 Oct**.
3. **What does it make harder later, and how fast can it be undone?**

And one fact that sits underneath all of them: **a full-viewport canvas is not free.** Going from
960×600 to a 1080p window is 3.6× the pixels; at DPR 2 it is 14.4× (§3). Anything that fills the
viewport must decide what to do about DPR in the same breath, or it will trade a cosmetic win for a
frame-rate loss on the exact machines friends own.

### The options

#### A. Ship the stock Default template

Change nothing. Build, deploy, play.

- **Gets us:** every hour between now and 10 October goes to deploy and to the two-seat test that
  M2-1 still needs. Zero risk to a build path that took four bugs to get working.
- **Costs:** the tab reads *Unity Web Player | MimasClient*, the favicon is Unity's, there is a Unity
  wordmark under the board, and the game plays in a small centred box on a white page with the Made
  with Unity splash before it. Against pillar 5 this is not neutral — it is the first impression, and
  it says "engine demo". It also quietly costs the *design*: the aiming slice's two range circles and
  the HUD's ghosted hp deltas are being judged at 960×600 on a 1440p laptop.
- **Reversible?** Completely. It is the status quo; every other option remains available.

#### B. A custom template that is Default minus the box

One new folder, `Assets/WebGLTemplates/Mimas/`, copied from the built-in Default, with: the footer
`<div>` deleted; the canvas in a `position:absolute; width:100%; height:100%` container with
`style="width:100%; height:100%"`; a real `<title>` and favicon; the page background set to the game's
dark colour instead of white. One line in `WebBuild.cs` —
`PlayerSettings.WebGL.template = "PROJECT:Mimas";` — so the profile cannot override it.

- **Gets us:** the entire "premade box" complaint, gone, in the smallest change that removes it. The
  board fills the window; the tab says *Mimas*; no Unity wordmark. Resize tracking comes free from
  `matchWebGLToCanvasSize` — no JS, no resize listener
  ([Unity's own markup](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-canvas-size.html)).
- **Costs:** half a day, most of it verification rather than typing. Three concrete second-order costs:
  (i) **it creates the DPR problem** — full-viewport at native DPR is the 3.6×–14.4× fill-rate jump
  above, and B does not address it; (ii) `tools/smoke/browser-smoke.mjs` clicks at canvas-relative
  coordinates (`--do "click:480,420"`, resolved against `#unity-canvas`'s bounding box at line 172) and
  waits on `#unity-loading-bar` (lines 103, 110) — every recorded coordinate changes and the loading-bar
  id must be kept or the script updated; (iii) the Unity splash still plays, so the first three seconds
  still say Unity. It also forecloses nothing but does not finish the job.
- **Reversible?** In minutes. Set `template` back to `"APPLICATION:Default"`, rebuild. The folder can
  stay. Nothing is stranded.

#### C. B, plus own the first ten seconds and cap the pixels

B, and then the three things that turn a full-viewport canvas into a finished page:

- **Splash off.** `PlayerSettings.SplashScreen.show = false` in `WebBuild.cs`, now legal on Personal in
  Unity 6 ([6000.0.0f1 release notes](https://unity.com/releases/editor/whats-new/6000.0.0f1)).
- **Our own loading screen.** An HTML/CSS panel that paints before `loader.js` is even requested — dark
  background, the word *Mimas*, a bar driven by `onProgress`, clamped monotonic and CSS-transitioned so
  its jumps read as motion — held up past `onSuccess` until the game itself signals ready, which kills
  the documented black gap
  ([Unity Discussions](https://discussions.unity.com/t/black-screen-before-splash-screen-webgl/1506073)).
  Mimas already logs the signal: `[ContentBootstrap] Content loaded`.
- **A DPR cap.** `config.devicePixelRatio = Math.min(window.devicePixelRatio, 1.5)` in the template, or
  `1` if measurement says so — measured with `tools/smoke/browser-perf.mjs`, not guessed, because **no
  published Unity WebGL DPR benchmark exists** (§3).

- **Gets us:** a page that is Mimas from the first painted frame to the first turn. It also removes the
  single worst thing about the current experience that nobody has named yet: on a ~12 MB Brotli
  download the player currently stares at a grey box with a Unity logo for several seconds, then a
  black gap, then a Unity splash. CrazyGames' published threshold for "attention starts to wander" is
  10 seconds ([docs](https://docs.crazygames.com/resources/getting-to-the-first-frame/)).
- **Costs:** one and a half to two days including measurement, which is real money against a 22-day
  budget whose critical path is deploy. Everything in B's cost list, plus: the DPR cap makes text
  slightly softer on retina (that is the trade, and the alternative — high DPI with URP Render Scale
  below 1 — is a second lever whose interaction with a **UI Toolkit** HUD I could not establish from
  any source and would have to be measured). It also takes `WebBuild.cs` from "states every build
  setting" to "states every build setting and owns the page", which is a slightly bigger surface for
  the deploy script to get wrong.
- **Reversible?** The template and DPR cap, in minutes, as in B. The splash toggle is one boolean. The
  loading screen is the only thing that would be thrown away, and it is one HTML file.

#### D. C, plus an HTML shell for the lobby and room code

Lobby, room-code entry and the share link as real DOM around the canvas, talking to Unity over
`SendMessage` and a `.jslib`.

- **Gets us:** paste, autofill and a real shareable link behave the way the browser makes them behave
  rather than the way Unity reimplements them. A four-letter code is exactly where paste matters, and
  there is a working precedent
  ([rehanlabs/Unity-WebGL-HTML-InputFix](https://github.com/rehanlabs/Unity-WebGL-HTML-InputFix), Unity
  6.0.51f1).
- **Costs:** several days, a second UI system to style against a UI Toolkit HUD, and three live hazards:
  `captureAllKeyboardInput = false` with its documented sticky-key side effect; the **unresolved**
  Input System `document`-level click bug (IN-41716), which lands squarely on Mimas because
  `activeInputHandler: 1`; and fullscreen, which hides DOM siblings unless the *container* is
  fullscreened rather than the canvas. It would also mean rewriting a lobby and room-code flow that
  `STATE.md` records as **done** and that has already been played.
- **Reversible?** Weeks, not minutes, once the lobby lives in two places. This is the only option here
  that is genuinely hard to undo.

### Comparison

| | A. Stock | B. Template only | C. Template + loading + DPR | D. + DOM shell |
|---|---|---|---|---|
| Stops feeling like a demo | no | mostly — first 3 s still Unity | yes | yes, plus better text entry |
| Days before 10 Oct | 0 | ~0.5 | ~1.5–2 | 4+ |
| Risk to the deploy critical path | none | negligible | small, and schedulable after deploy | real |
| What it makes harder later | keeps the demo feel through every future playtest | nothing | slightly larger build-script surface | two UI systems; a rewritten lobby |
| Reversibility | n/a | minutes | minutes (one HTML file discarded) | weeks |
| Needs a live Unity Editor? | no | no — new files + `WebBuild.cs`, no `.asset` edits | no | no |
| Open questions it forces | none | `Screen.width` semantics; smoke-test coordinates | those, plus Render Scale vs UI Toolkit | those, plus IN-41716 |

### Recommendation

**C, sequenced so that B lands first and is independently shippable, and only after deploy (M2-7) is
green.**

The single reason: the three things in C are the same change. Making the canvas full-viewport is what
*creates* the DPR problem, and removing the Unity footer without removing the Unity splash leaves the
first three seconds still saying Unity — so doing B alone means opening the same file twice and
measuring the same thing twice. The work is one file, one boolean and two lines of `WebBuild.cs`, it
touches no game code, needs no live Editor, and violates no golden rule.

The sequencing is the important half of the recommendation. **Nothing here should be started before
M2-7 is green.** `STATE.md` is unambiguous that deploy is the only thing standing between this project
and 10 October, and a beautiful page nobody can reach is worth nothing.

**What would change my mind:**

- **If deploy is not green by Fri 2 Oct** — drop to **B** and stop. Half a day, removes the footer and
  the box, cap DPR at 1 without measuring (softer text is a far smaller sin than a dropped frame), and
  leave the splash and the loading screen for after the playtest.
- **If measurement shows the DPR cap is unnecessary** — if `browser-perf.mjs` says a full-viewport
  canvas at native DPR holds frame rate on the machines friends actually own, drop the cap and keep the
  crisp text. This is the one part of C I am recommending on arithmetic rather than evidence, and I
  would rather be corrected by a measurement than trusted.
- **If `Screen.width` turns out to report drawing-buffer pixels** and the UI Toolkit HUD halves in size
  on a retina display, C grows a fourth piece and becomes a day longer. That is worth knowing before
  committing, and it is a ten-minute check.
- **D changes nothing about this recommendation for 10 October.** If room-code entry proves painful in
  the playtest, that is a reason to open D as its own decision afterwards — with the Input System click
  bug tested first.

### What happens next, either way

| If you choose | The first task, the same morning |
|---|---|
| **A** | Nothing. Close this and put the day into M2-7. Add one line to `STATE.md` recording that the stock template is a deliberate choice for 10 Oct, so no agent "fixes" it mid-flight. |
| **B** | A **light**-lane task: create `Assets/WebGLTemplates/Mimas/` from the built-in Default, delete `#unity-footer`, full-viewport the canvas, set title/favicon/background, add `PlayerSettings.WebGL.template = "PROJECT:Mimas";` to `WebBuild.cs`. Update `browser-smoke.mjs`'s click coordinates and confirm `#unity-loading-bar` still exists or change what it waits on. Ladder rungs 9 and 10. |
| **C** | The same task as B, then a second **light** task for the loading screen, `PlayerSettings.SplashScreen.show = false`, and the DPR cap — with `browser-perf.mjs` run at DPR 1, 1.5 and native on a 1440p window before the cap value is chosen. Ladder rung 10 plus the perf numbers in the run report. |
| **D** | Do not start it. Open it as its own options write-up after the 10 Oct playtest, and gate it on one experiment: put a plain `<button>` over the canvas in a scratch build and confirm whether Input System 1.19.0 still sees the click through it (IN-41716). If it does, D is much more expensive than it looks. |

### One loose end for the design page

`docs/design/index.html#presentation` covers camera, HUD, examine, audio/VFX, art direction and
targeting. **It says nothing about the web page the game is embedded in** — no anchor for the shell,
the loading screen or the canvas policy. Under golden rule 11 the page shell is not a game rule, so
this is not drift. But whichever option is chosen, the decision should get a short `proposed`
subsection under `#presentation` (or a new `#web-shell`) so that the next agent to open a browser has
somewhere to read "the canvas fills the viewport, the splash is off, DPR is capped at N" rather than
inferring it from a template file.

---

## Sources

**Unity 6.4 (6000.4) manual**
- [Using Web templates](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-intro.html)
- [Add a custom Web template](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-add.html)
- [Web template structure and instantiation](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-structure.html)
- [Web template variables](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-variables.html)
- [Web template build configuration and interaction](https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-build-configuration.html)
- [JavaScript interface in Unity Web builds](https://docs.unity3d.com/6000.4/Documentation/Manual/web-js-interface.html)
- [Configure a Web Canvas size](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-canvas-size.html)
- [Web Player settings](https://docs.unity3d.com/6000.4/Documentation/Manual/class-PlayerSettingsWebGL.html)
- [Input in Web](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-input.html)
- [Web performance considerations](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-performance.html)
- [Optimize your Web build](https://docs.unity3d.com/6000.4/Documentation/Manual/web-optimization.html) · [quality](https://docs.unity3d.com/6000.4/Documentation/Manual/web-optimization-quality.html) · [graphics](https://docs.unity3d.com/6000.4/Documentation/Manual/web-optimization-graphics.html)
- [Deploy a Web application](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-deploying.html)
- [Cache behavior in Web](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-caching.html)
- [WebGPU (Experimental)](https://docs.unity3d.com/6000.4/Documentation/Manual/WebGPU.html)
- [Splash Screen player settings](https://docs.unity3d.com/6000.4/Documentation/Manual/class-PlayerSettingsSplashScreen.html)
- [Interaction with browser scripting](https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-interactingwithbrowserscripting.html) · [.jslib setup](https://docs.unity3d.com/6000.4/Documentation/Manual/web-interacting-browser-js.html) · [JS→C#](https://docs.unity3d.com/6000.4/Documentation/Manual/web-interacting-browser-unity-to-js.html) · [C#→JS](https://docs.unity3d.com/6000.4/Documentation/Manual/web-interacting-browser-js-to-unity.html) · [deprecated APIs](https://docs.unity3d.com/6000.4/Documentation/Manual/web-interacting-browser-deprecated.html)
- [URP Asset reference](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/universalrp-asset.html) · [change URP asset settings](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/quality/change-urp-asset-settings.html)
- ScriptReference: [PlayerSettings.WebGL.template](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/PlayerSettings.WebGL-template.html) · [PlayerSettings.SplashScreen.show](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/PlayerSettings.SplashScreen-show.html) · [WebGLInput.captureAllKeyboardInput](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/WebGLInput-captureAllKeyboardInput.html) · [Screen.SetResolution](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Screen.SetResolution.html) · [Application.ExternalCall (obsolete)](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Application.ExternalCall.html)
- Older, cited for contrast: [2022.3 Splash Image](https://docs.unity3d.com/2022.3/Documentation/Manual/class-PlayerSettingsSplashScreen.html) · [2021.2 WebGL templates (custom user variables)](https://docs.unity3d.com/2021.2/Documentation/Manual/webgl-templates.html) · [6000.3 Apache server config](https://docs.unity3d.com/6000.3/Documentation/Manual/web-server-config-apache.html)

**Unity, non-manual**
- [Unity is Canceling the Runtime Fee (12 Sep 2024)](https://unity.com/blog/unity-is-canceling-the-runtime-fee)
- [Unity 6000.0.0f1 release notes](https://unity.com/releases/editor/whats-new/6000.0.0f1)
- [Unity pricing updates](https://unity.com/products/pricing-updates)
- [Unity Support: WebGL looks wrong on High resolutions (Retina), 20 Feb 2024](https://support.unity.com/hc/en-us/articles/214948483-WebGL-looks-wrong-on-High-resolutions-Retina)
- [Unity Learn: Getting started with Unity Web (17 Oct 2024)](https://learn.unity.com/tutorial/getting-started-with-unity-web)

**Unity Discussions / forums**
- [Setting WebGL template programmatically](https://discussions.unity.com/t/setting-webgl-template-programmatically/641448)
- [WebGLTemplates folder and location](https://discussions.unity.com/t/webgltemplates-folder-and-location/814387)
- [matchWebGLToCanvasSize = false, how to resize?](https://discussions.unity.com/t/webgl-matchwebgltocanvassize-false-how-to-resize/817874)
- [matchWebGLToCanvasSize=false incompatible with fullscreen (UUM-127494, fixed 6000.3.1f1)](https://discussions.unity.com/t/webgl-config-setting-matchwebgltocanvassize-false-is-incompatible-with-fullscreen/1689915)
- [Dynamic resolution / render scale in WebGL and URP](https://discussions.unity.com/t/dynamic-resolution-render-scale-in-webgl-and-urp/808364)
- [WebGL retina scaling](https://discussions.unity.com/t/webgl-retina-scaling/782203)
- [Unity WebGL and HiDPI screens](https://discussions.unity.com/t/unity-webgl-and-hidpi-screens/639188)
- [Black screen before splash screen (WebGL)](https://discussions.unity.com/t/black-screen-before-splash-screen-webgl/1506073)
- [[SOLVED] Can't customize splash screen on Unity 6](https://discussions.unity.com/t/solved-cant-customize-splash-screen-on-unity-6/1544245)
- [New Input System listens for click on whole document instead of canvas (IN-41716)](https://discussions.unity.com/t/new-input-system-listens-for-click-event-on-whole-document-instead-of-canvas/915544) · [related feature request](https://discussions.unity.com/t/webgl-be-able-to-configure-click-event-listener/900808)
- [Keyboard input gets stuck when WebGL canvas loses focus](https://discussions.unity.com/t/keyboard-input-gets-stuck-when-webgl-canvas-loses-focus/862905)
- [Losing focus on WebGL when interacting with UI elements](https://discussions.unity.com/t/loosing-focus-on-webgl-when-interacting-with-ui-elements/801934)
- [Add a preloader in Unity loading screen](https://discussions.unity.com/t/add-a-preloader-in-unity-loading-screen/914152)
- [WebGPU out of experimental in Unity 6.6 (24 Aug 2026)](https://discussions.unity.com/t/webgpu-out-of-experimental-in-unity-6-6/1734694)

**Portals**
- Poki: [requirements & quality](https://developers.poki.com/guide/requirements-quality) · [web game engines](https://developers.poki.com/guide/web-game-engines) · [easy access](https://developers.poki.com/guide/easy-access) · [SDK overview](https://developers.poki.com/guide/sdk-overview) · [SDK: Unity](https://developers.poki.com/guide/sdk-unity)
- CrazyGames: [technical requirements](https://docs.crazygames.com/requirements/technical/) · [gameplay requirements](https://docs.crazygames.com/requirements/gameplay/) · [quality guidelines](https://docs.crazygames.com/requirements/quality/) · [optimization tips](https://docs.crazygames.com/resources/optimization-tips/) · [getting to the first frame](https://docs.crazygames.com/resources/getting-to-the-first-frame/)
- itch.io: [HTML5 creator docs](https://itch.io/docs/creators/html5)
- Newgrounds: [newgrounds.io](https://www.newgrounds.io/) · [downloads](https://www.newgrounds.io/downloads/) · [get started](https://www.newgrounds.io/get-started/) — main site unreachable (403, NG Guard)

**Third party**
- [Better Minimal WebGL Template (MIT)](https://seansleblanc.itch.io/better-minimal-webgl-template)
- [greggman/better-unity-webgl-template](https://github.com/greggman/better-unity-webgl-template) (2019–2020 only; cited for the complaint, not as a dependency)
- [rehanlabs/Unity-WebGL-HTML-InputFix](https://github.com/rehanlabs/Unity-WebGL-HTML-InputFix)
- [kou-yeung/WebGLInput](https://github.com/kou-yeung/WebGLInput)
- [JohannesDeml/UnityWebGL-LoadingTest](https://github.com/JohannesDeml/UnityWebGL-LoadingTest)
- [react-unity-webgl](https://github.com/jeffreylanters/react-unity-webgl) · [docs](https://react-unity-webgl.dev) · [npm](https://www.npmjs.com/package/react-unity-webgl)
- [Marinerer/unity-webgl](https://github.com/Marinerer/unity-webgl)
- [webglfundamentals: resizing the canvas](https://webglfundamentals.org/webgl/lessons/webgl-resizing-the-canvas.html) · [fill rate](https://webglfundamentals.org/webgl/lessons/webgl-qna-optimize-drawing-lots-of-large-images.html)
- [Khronos public_webgl, 18 Jul 2017 — Retina and WebGL performance](https://www.khronos.org/webgl/public-mailing-list/public_webgl/1707/msg00029.php)
- [Vuplex: WebGL fullscreen modifications](https://support.vuplex.com/articles/webgl-fullscreen/)
- [Fullscreen API specification](https://fullscreen.spec.whatwg.org)
- [ocias.com: custom WebGL progress bar](https://ocias.com/blog/unity-webgl-custom-progress-bar/)
- [LearnWebGL: overlays](http://learnwebgl.brown37.net/11_advanced_rendering/overlays.html)
- [dev.to: building a Web–Unity WebGL bridge (Dec 2025)](https://dev.to/raw-fun-gaming/building-a-web-unity-webgl-bridge-a-practical-guide-3nbe)
- [unity-trouble-atlas: URP render scale blurry UI](https://unity-trouble-atlas.7colorsgame.com/en/article/unity-urp-render-scale-blurry-ui/) (unverified against Unity docs)
- [GameFromScratch: Unity 6.6 released](https://gamefromscratch.com/unity-6-6-released/) · [AlternativeTo: Unity 6.6 adds WebGPU](https://alternativeto.net/news/2026/9/unity-6-6-adds-webgpu-build-analysis-and-coreclr-prep/)

**Read on this machine (ground truth, not a URL)**
- `C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\PlaybackEngines\WebGLSupport\BuildTools\WebGLTemplates\Base\{Default,Minimal,PWA}` — `index.html`, `TemplateData/style.css`
- `C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\PlaybackEngines\WebGLSupport\BuildTools\prejs\FullScreen.js`
- `MimasClient/ProjectSettings/ProjectSettings.asset` — lines 20, 46–47, 806, 928
- `MimasClient/Assets/Settings/Build Profiles/Web Release.asset` — lines 70–71, 826, 833
- `MimasClient/Assets/Settings/PC_RPAsset.asset` — `m_RenderScale: 1`, `m_MSAA: 1`, `m_SupportsHDR: 1`
- `MimasClient/Assets/_Game/Editor/WebBuild.cs` · `tools/smoke/browser-smoke.mjs` · `docs/hosting.md` · `studio/pillars.md` · `studio/STATE.md`
