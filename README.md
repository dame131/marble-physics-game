# Marble Physics Game

Unity URP marble-shooter prototype with pooled 3D marbles, match-3 clearing, BFS support collapse, PhysX falling cascades, bank-shot aiming, three scoring baskets, and GPU pixy-dust VFX.

## Core scripts

- `Assets/Scripts/Core/MarbleSupportGraph.cs`
- `Assets/Scripts/Core/MarbleShooter.cs`
- `Assets/Scripts/Core/MarbleBasketsAndPool.cs`
- `Assets/Scripts/VFX/MarblePixyDust.cs`
- `Assets/Scripts/Levels/MarbleLevelBuilder.cs`
- `Assets/Scripts/Core/GameManager.cs`

## Required Unity setup

Use a Unity project configured for **Universal Render Pipeline (URP)**.

`MarblePixyDust.cs` uses `UnityEngine.VFX`, so install **Visual Effect Graph** from Unity Package Manager before compiling the project.

Create these Unity layers:

- `Marble`
- `Wall`
- `Ceiling`

## Recommended hierarchy

```text
GAME
├── GameManager
├── Systems
│   ├── MarbleBasketsAndPool
│   ├── MarblePixyDust
│   └── MarbleLevelBuilder
├── Board
│   ├── MarbleSupportGraph
│   ├── GridOrigin
│   ├── Ceiling
│   ├── LeftWall
│   ├── RightWall
│   └── MarbleLevelBoard
├── Shooter
│   ├── LaunchOrigin
│   ├── AlternateMarbleAnchor
│   └── TrajectoryLine
├── Baskets
│   ├── LeftBasket/Trigger
│   ├── CenterBasket/Trigger
│   └── RightBasket/Trigger
└── Main Camera
```

## Locked gameplay loop

```text
Build mosaic from pool
→ current + alternate marble
→ aim / bank shot
→ PhysX sphere collision
→ hex snap
→ match 3+
→ BFS ceiling-anchor support check
→ unsupported marbles become dynamic rigidbodies
→ velocity-driven pixy dust
→ basket catch
→ score + combo + sound + burst FX
→ return marble to pool
→ win / lose evaluation
```

## Grid settings

Use the same settings in `MarbleShooter` and `MarbleLevelBuilder`:

- Hex layout: `OddRowsShiftRight`
- Horizontal spacing: `1.0`
- Vertical spacing: `0.8660254`

`MarbleSupportGraph`:

- Neighbor Layout: `HexOddRowOffset`
- Ceiling Anchor Rows: `1`
- Minimum Match Count: `3`

## Basket scoring

- Left: **100**
- Center: **250**
- Right: **100**

## Marble palette

Suggested Match IDs:

| ID | Name | Base Color | Metallic | Smoothness |
|---:|---|---|---:|---:|
| 0 | Crimson | `#D60A00` | 0.10 | 0.95 |
| 1 | Gold | `#E8A200` | 0.85 | 0.92 |
| 2 | Amber | `#FF6600` | 0.30 | 0.90 |
| 3 | Emerald | `#00A300` | 0.10 | 0.95 |
| 4 | White Pearl | `#FFF0D0` | 0.05 | 0.98 |
| 5 | Royal Purple | `#6A0DAD` | 0.35 | 0.92 |
| 6 | Royal Blue | `#002366` | 0.40 | 0.95 |
| 7 | Electric Blue | `#00E5FF` | 0.20 | 0.98 |
| 8 | Ice Blue | `#A5F2F3` | 0.15 | 0.96 |

Blue is allowed in this game.

## First test

Use `Assets/Levels/CrownTestData.txt` for the 12×16 crown test and call:

```csharp
gameManager.LoadRuntimeLevel(testCrown, 30);
```

See the setup notes under `Docs/` before pressing Play.
