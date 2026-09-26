# Marble Avalanche — Unity foundation

Unity 2022.3.20f1 project for a touch-controlled 3D marble shooter. The starter scene is generated on first opening the project. Aim and release to shoot, swap colors, match three, cut ceiling support to drop marbles, score through three gold baskets, fire a cannon with smoke, uncover a private portrait cell by cell, and unlock a 100-stop procedural level map. The 100 stops are **procedural variations, not 100 handcrafted boards**.

## Open and play

1. Add this repository folder in Unity Hub with Unity 2022.3.20f1 and Android Build Support installed.
2. Open the project; the Editor creates `Assets/Scenes/MarbleAvalanche.unity` on first load. Open that scene and press Play.
3. Select **Marble Avalanche → Import Private Portrait** and choose the supplied photograph on your own computer. The photo stays in `Assets/Resources/Portrait.jpg` and is ignored by Git, so it will not be published to this public repository. The game works without it, but the reveal will be empty.
4. For Android, switch Platform to Android in Build Settings, set package identifier, and build a Development APK. Unity and an Android test device are required to verify the result.

The photo is drawn upright through UV rotation while the source file remains unchanged. Only cells cleared by matches or detached drops display parts of it. The full image appears after the top clears and the level goal is met.

## Current limits

- The Unity project has been checked for code structure but **has not been opened in the Unity Editor or tested on Android** in this workspace. An APK and phone screenshots are not yet available.
- Level map and progress saving are present. Unique world art, boss battles, sound design, reward wheel and optimized stress testing are still pending.
- This repository contains no personal photograph. Import it locally from the copy you own.

## Build menu

The **Marble Avalanche → Build Android Development APK** editor menu generates `Builds/MarbleAvalanche-debug.apk` when Android Build Support is installed. This build has not been run here. The private portrait must be imported locally before building if the reveal is required in that APK.
