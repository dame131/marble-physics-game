# Codex Task — Build the Marble Physics Unity Prototype

Work on branch `unity-core-bundle`.

## Goal
Turn the existing six-script marble shooter core into a complete Unity URP prototype scene that can be opened and tested immediately in Unity.

## Existing locked scripts
Do not rewrite their gameplay architecture unless a compile/runtime bug requires a minimal fix:

- `Assets/Scripts/Core/MarbleSupportGraph.cs`
- `Assets/Scripts/Core/MarbleShooter.cs`
- `Assets/Scripts/Core/MarbleBasketsAndPool.cs`
- `Assets/Scripts/VFX/MarblePixyDust.cs`
- `Assets/Scripts/Levels/MarbleLevelBuilder.cs`
- `Assets/Scripts/Core/GameManager.cs`

## Required implementation

1. Create a Unity URP project shell in this repository.
2. Add required Unity package metadata, including Visual Effect Graph support.
3. Create the scene structure described in `Docs/UNITY_SETUP.md`:
   - Board
   - GridOrigin
   - Ceiling
   - LeftWall
   - RightWall
   - Shooter
   - LaunchOrigin
   - AlternateMarbleAnchor
   - Three basket trigger areas
   - Main Camera
   - GameManager/System objects
4. Create a reusable `Marble.prefab` with:
   - Sphere MeshFilter/MeshRenderer
   - SphereCollider radius 0.5
   - Rigidbody mass 1
   - Marble layer
   - child `PixyDustVFX`
5. Create URP/Lit marble materials for match IDs 0–8 using the palette in `Docs/MATERIALS.md`.
6. Blue is allowed in this game. Keep Crimson, Gold, Amber, Emerald, White Pearl, Royal Purple, Royal Blue, Electric Blue, and Ice Blue.
7. Create the VFX Graph required by `MarblePixyDust.cs` with these exposed properties:
   - SpawnRate
   - TrailLength
   - ParticleSize
   - CrimsonColor
   - AmberColor
   - GoldColor
   - ImpactPosition
   - ImpactSpeed
   - ImpactIntensity
   and events:
   - OnTrailStart
   - OnTrailStop
   - OnImpact
   Use world-space simulation.
8. Create the three physical basket areas:
   - Left = 100
   - Center = 250
   - Right = 100
   Connect trigger colliders to `MarbleBasketsAndPool`.
9. Configure layers exactly:
   - Marble
   - Wall
   - Ceiling
10. Match `MarbleShooter` and `MarbleLevelBuilder` grid settings exactly:
   - OddRowsShiftRight
   - HorizontalSpacing = 1.0
   - VerticalSpacing = 0.8660254
11. Use the 12x16 crown test data from `Assets/Levels/CrownTestData.txt` and create a small bootstrap or scene startup path that calls:
   `GameManager.LoadRuntimeLevel(testCrown, 30);`
12. Ensure `GameManager` does not also auto-load a texture level during the crown bootstrap test.
13. Configure the camera and world so the whole test board, shooter, and baskets are visible.
14. Use the recommended transforms from `Docs/UNITY_SETUP.md`.
15. Add a minimal HUD for:
   - Score
   - Shots
   - Level state
   - Win
   - Lose
   - Retry
16. Wire HUD to `GameManager.OnScoreChanged`, `OnShotsChanged`, `OnLevelWon`, and `OnLevelLost`.
17. Add a UI swap button wired to `MarbleShooter.SwapMarbles()`.
18. Keep pooling active. Do not replace gameplay pooling with Destroy/Instantiate loops.
19. Do not remove the BFS support-collapse logic.
20. Do not simplify falling marbles into fake animation; unsupported pieces must use Rigidbody physics.

## Validation

Run all checks available in the environment. At minimum:

- Ensure C# compiles against the chosen Unity version/packages.
- Verify all serialized references in the main test scene are assigned where possible.
- Verify no duplicate conflicting public class names.
- Verify the six core scripts remain present.
- Verify the runtime crown test contains only match IDs configured in the shooter and level builder.
- Verify the line trajectory masks include Wall, Marble, and Ceiling.
- Verify basket triggers are IsTrigger.
- Verify row 0 registers as anchor.
- Verify matched marbles recycle into the pool.
- Verify falling marbles recycle after basket capture.

## Visual direction

The final target is not sparse. Production levels will eventually use 500–1000+ tightly packed marbles forming detailed images, with 100+ marbles able to fall at once. Keep the test small, but do not architect the scene in a way that prevents large dense mosaics later.

Use polished URP lighting, reflections, gold/crimson metallic HUD accents, and the full approved color palette including blue shades.

## Deliverable

Commit all Unity project files needed to open the repo as a Unity project and run the crown prototype. Add a short `BUILD_STATUS.md` explaining:

- what was created
- what was tested
- any Unity-editor-only steps still required
- any remaining compile/runtime issues
