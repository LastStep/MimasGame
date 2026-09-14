# Unity templates

Files to copy into `MimasClient/` after the project is created from the Universal 3D template (see `docs/setup-checklist.md` B3–B4):

| File | Destination |
|---|---|
| `manifest.additions.json` | merge into `MimasClient/Packages/manifest.json` |
| `link.xml` | `MimasClient/Assets/link.xml` |
| `Editor/WebBuild.cs` | `MimasClient/Assets/_Game/Editor/WebBuild.cs` |

Planned `_Game` folder tree (each with its own `.asmdef`, `autoReferenced: false`):

```
Assets/_Game/
  Presentation/   Mimas.Client.Presentation  (refs: Mimas.Core, Unity.Cinemachine, Unity.InputSystem)
  UI/             Mimas.Client.UI            (refs: Mimas.Core, Mimas.Client.Presentation)
  Net/            Mimas.Client.Net           (refs: Mimas.Core, NativeWebSocket, Newtonsoft.Json)
  Audio/          Mimas.Client.Audio         (refs: FMODUnity)
  Data/           JSON game data (no code)
  Art/            models, materials, textures, particles
  Scenes/         Boot.unity, Loading.unity, Menu.unity, Match.unity
  Editor/         Mimas.Client.Editor        (Editor-only; WebBuild.cs, map tools)
  Tests/EditMode  Mimas.Client.Tests.EditMode
```
