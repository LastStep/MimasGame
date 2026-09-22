---
id: RESEARCH-2026-09-21-effect-systems
title: Data-driven effect / modifier / overlay systems in deterministic C# rules engines
project: mimas
written: 2026-09-21
by: Claude (Sonnet research subagent), for the boons spec session
for: shared/Mimas.Core boon groundwork (effect vocabulary, stat pipeline, ability overrides, attribution, content tests)
---

# Effect systems: engineering precedents

Facts with sources; **unverified** marks what the subagent could not confirm against a primary source.

## 1. Closed effect vocabulary: dispatch and JSON

- Dispatch without reflection: a dictionary of handler instances keyed by an enum, built once, or an
  exhaustive `switch` on the enum (the compiler flags a missed case). Both are IL2CPP-safe.
  [Registry pattern](https://www.geeksforgeeks.org/system-design/registry-pattern/)
- `[JsonDerivedType]` / `[JsonPolymorphic]` need .NET 7+, so they are unavailable on netstandard2.1.
  [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism)
- Portable alternative: a custom converter that peeks a discriminator, or (simpler, for a closed list of
  four kinds) one flat effect record with all fields nullable plus a `type` string, validated by the
  handler for its kind. [code-maze](https://code-maze.com/csharp-polymorphic-serialization-and-deserialization/),
  [Ben Gribaudo](https://bengribaudo.com/blog/2022/02/22/6569/recursive-polymorphic-deserialization-with-system-text-json)

## 2. Stat pipelines

- The converged order is flat → summed percent → multiplicative, in Kryzarel's Unity stats articles,
  Unreal GAS (`(Base + Additive) * Multiplicative / Division`, Override last) and Path of Exile
  (added → increased summed → more multiplied).
  [Kryzarel pt.1](https://medium.com/@kryzarel/character-stats-attributes-in-unity-pt-1-70f90ade9788),
  [GASDocumentation](https://github.com/Clubbable/GASDocumentation),
  [PoE order](https://mobalytics.gg/poe-2/guides/damage-defence-calc-order)
- Mimas has only flat integers, so the whole pipeline is one commutative sum: order-independent by
  construction. If a percent stage ever arrives, sum all percents into one integer factor and apply it
  once, in a defined order sorted by source id, never by dictionary order.
- On-demand recomputation from immutable base + modifiers is trivially deterministic; caches
  reintroduce invalidation bugs. Design judgment, not a documented pattern (**unverified**).

## 3. Immutable definitions + per-owner overlays

- Unreal GAS: `UGameplayEffect` (asset) is immutable; `FGameplayEffectSpec` is the per-application
  instance carrying `SetByCaller` overrides merged at creation.
  [FGameplayEffectSpec](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayAbilities/FGameplayEffectSpec)
- OpenRA (C#): each trait has an immutable `Info` shared per actor type and a stateful instance per
  actor, built from parsed YAML with no reflection-emit. [OpenRA traits](https://docs.openra.net/en/playtest/traits/)
- Slay the Spire's copy-then-upgrade is community lore only (**unverified**).
- Recommended shape: `AbilityDef` (JSON, shared) + an override list per owner (`(target, field, delta)`)
  → a resolved view computed at the point of use (targeting, cost, damage), never cached, never
  mutating the def. [Decorator pattern](https://en.wikipedia.org/wiki/Decorator_pattern)

## 4. Source-tagged attribution

- GAS aggregators tag each modifier with its source so it can be queried and removed (**partially
  unverified** in detail). [devtricks GAS](https://vorixo.github.io/devtricks/gas/)
- D&D 5e: bonuses from the same named source do not stack; different sources do.
  [EN World](https://www.enworld.org/threads/magic-item-stacking.347420/)
- Recommendation: every damage line and every stat contribution carries a source id (item, boon,
  modifier). Mimas's damage lines already carry the modifier id; extending stats the same way is cheap.

## 5. Tests for data-driven content

- Content lint: load every JSON file, assert every referenced id resolves. Luanti has exactly such a
  test. [Luanti commit](https://github.com/luanti-org/luanti/commit/47c000a2938b63b1b6c12b555d41e88f29f37faa)
- Golden match log: fixed seed, scripted commands, snapshot the event / damage-line output, diff on every
  run. [Golden Master](https://stevenschwenke.de/whatIsTheGoldenMasterTechnique),
  [Snapper](https://github.com/theramis/Snapper), lockstep checksum precedent
  [SnapNet](https://www.snapnet.dev/blog/netcode-architectures-part-1-lockstep/)

## Recommendation for this codebase (agent reading)

1. No STJ polymorphism attributes; Core is netstandard2.1.
2. One flat `BoonEffect` record with a `type` and nullable fields; the loader validates per kind.
3. Dispatch on a closed enum with an exhaustive switch; unknown types fail closed like trajectories do.
4. Defs stay immutable; overrides, added elements and granted abilities live on the unit as an overlay.
5. Resolve the overlay on demand where range, cost and damage are read (the existing override seam).
6. Stats stay one flat sum; if a boon ever needs a percent, that is a design decision first.
7. Source ids on every contribution, which is also what "reveal on first effect" keys on.
8. A content-lint test over `boons/` and `lineages/` on day one.
9. A golden session test: seeded draft offers + a scripted match with boons, snapshotted.
10. Same-source stacking is not in the design yet; propose it there, do not invent it in code.
