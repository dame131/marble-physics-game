# Pixy Dust VFX Graph Setup

`MarblePixyDust.cs` uses `UnityEngine.VFX`, so install **Visual Effect Graph** through Unity Package Manager.

Create a Visual Effect Graph and assign it to the `VisualEffect` component under each marble prefab.

Simulation space: **World**.

Expose these exact properties:

```text
SpawnRate       Float
TrailLength     Float
ParticleSize    Float
CrimsonColor    Vector4
AmberColor      Vector4
GoldColor       Vector4
ImpactPosition  Vector3
ImpactSpeed     Float
ImpactIntensity Float
```

Create these exact events:

```text
OnTrailStart
OnTrailStop
OnImpact
```

Recommended behavior:

- `OnTrailStart` starts continuous sparkling emission.
- `SpawnRate` controls particle count from marble velocity.
- `TrailLength` controls lifetime/streak duration.
- `ParticleSize` controls sparkle size.
- Use `CrimsonColor`, `AmberColor`, and `GoldColor` for the current locked pixy-dust gradient.
- `OnImpact` creates a short burst at `ImpactPosition`, scaled by `ImpactIntensity`.
- `OnTrailStop` stops continuous emission.

Blue is allowed in the overall game palette. The current locked pixy-dust controller still uses crimson/amber/gold until that FX palette is intentionally expanded.
