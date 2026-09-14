# _Game

All Mimas-specific content lives here (template leftovers like `TutorialInfo/` can be deleted).

| Folder | Assembly | May reference |
|---|---|---|
| Presentation/ | Mimas.Client.Presentation | Mimas.Core, Cinemachine, Input System |
| UI/ | Mimas.Client.UI | Mimas.Core, Presentation (UI Toolkit is built-in) |
| Net/ | Mimas.Client.Net | Mimas.Core, Newtonsoft.Json (+ NativeWebSocket once its asmdef name is confirmed) |
| Audio/ | Mimas.Client.Audio | (add `FMODUnity` reference after importing FMOD) |
| Editor/ | Mimas.Client.Editor | Editor-only tools, WebBuild.cs |
| Tests/EditMode/ | Mimas.Client.Tests.EditMode | NUnit smoke tests |
| Data/ | — | JSON game data (`docs/data.md`) |
| Art/, Scenes/ | — | assets |

Rules: no `UnityEngine` in Core; scenes/prefabs only via the live Editor; `.meta` files are Unity's.
