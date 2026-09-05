# Unity Inspector Wiring

## 1. Marble prefab

Create `Assets/Prefabs/Marble.prefab`.

Root:
- Layer: `Marble`
- Sphere mesh
- MeshRenderer
- SphereCollider radius `0.5`
- Rigidbody
  - Mass `1`
  - Interpolate
  - Continuous Dynamic
  - Is Kinematic = true
  - Use Gravity = false

Child:
```text
Marble
└── PixyDustVFX
    └── VisualEffect
```

The scripts add/configure `MarbleProjectileRelay` and `MarblePixyDustEmitter` as needed.

## 2. Scene transforms for the 16-column test

```text
GridOrigin      (0, 0, 0)
Ceiling         (7.5, 1.0, 0) scale (18.5, 1.0, 2.0), layer Ceiling
LeftWall        (-1.0, -8.0, 0) scale (1.0, 20.0, 2.0), layer Wall
RightWall       (16.5, -8.0, 0) scale (1.0, 20.0, 2.0), layer Wall
Shooter         (7.5, -16.0, 0)
Left Basket     (3.0, -15.0, 0)
Center Basket   (7.5, -15.0, 0)
Right Basket    (12.0, -15.0, 0)
```

Each basket needs a trigger collider.

## 3. MarbleSupportGraph

Attach to `Board`.

- Rows/Columns large enough for the level
- Ceiling Anchor Rows = `1`
- Neighbor Layout = `HexOddRowOffset`
- Minimum Match Count = `3`

## 4. MarbleBasketsAndPool

Attach to `GAME` or `Systems`.

Assign:
- MarbleSupportGraph
- Marble prefab
- Left trigger
- Center trigger
- Right trigger
- Optional AudioSource / hit clips
- Optional score-burst ParticleSystems

Defaults:
- Left = 100
- Center = 250
- Right = 100
- Prewarm = 128 for crown test
- Use 512+ for dense production mosaics

## 5. MarbleShooter

Attach to `Shooter`.

Assign:
- MarbleSupportGraph
- MarbleBasketsAndPool
- Main Camera
- LaunchOrigin
- AlternateMarbleAnchor
- GridOrigin
- LineRenderer

Masks:
- Wall Mask = `Wall`
- Marble Mask = `Marble`
- Ceiling Mask = `Ceiling`

Grid:
- OddRowsShiftRight
- Horizontal Spacing = `1.0`
- Vertical Spacing = `0.8660254`

## 6. MarblePixyDust

Attach to `GAME` or `Systems`.

Assign:
- MarbleSupportGraph
- MarbleBasketsAndPool

Install Visual Effect Graph first.

## 7. MarbleLevelBuilder

Attach to `GAME` or `Systems`.

Assign:
- MarbleSupportGraph
- MarbleBasketsAndPool
- MarbleShooter
- GridOrigin
- optional BoardRoot
- same active MarbleStyles used by MarbleShooter

Its hex layout and spacing must exactly match MarbleShooter.

## 8. GameManager

Attach to `GameManager`.

Assign:
- MarbleSupportGraph
- MarbleShooter
- MarbleBasketsAndPool
- MarblePixyDust
- MarbleLevelBuilder

For the runtime crown test, disable `Load First Level On Start` and call `LoadRuntimeLevel(testCrown, 30)`.

## 9. First Play Mode verification

1. Crown mosaic builds.
2. Current + alternate marbles show.
3. Swap works.
4. Aim line works.
5. Wall bank reflection works.
6. Shot snaps to hex grid.
7. Match 3+ clears.
8. BFS releases unsupported pieces.
9. Released marbles fall with PhysX gravity.
10. Pixy dust appears.
11. Hard impacts burst sparks.
12. Baskets score 100 / 250 / 100.
13. Combo rises within a collapse wave.
14. Marbles return to pool.
15. Ammo drops once per shot.
16. Empty board wins.
17. Zero ammo with attached marbles loses.
