# Mimas.Core

Pure C# rules engine. Consumed two ways:

1. **Unity** — as a local package (`"com.mimas.core": "file:../../shared/Mimas.Core"` in `MimasClient/Packages/manifest.json`). `Runtime/Mimas.Core.asmdef` has `noEngineReferences: true`, so any `UnityEngine` usage fails to compile — on purpose.
2. **.NET** — via `../Mimas.Core.Build/Mimas.Core.csproj`, which compiles `Runtime/**/*.cs` from here. The csproj lives outside this folder so that `bin/` and `obj/` never end up inside the Unity package.

Rules: C# 9 max, netstandard2.1, no floats in rules, all randomness via `Rng`, no `record`, no `System.Threading`.
