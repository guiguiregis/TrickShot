# TrickShot 🏀

> **Programmeur Squido — Test Gameplay**

---

## Test Brief

**Objectif:** Évaluer les compétences en tant que développeur gameplay.

**Description:** Faire un jeu en 3D sur **Unity 6000.0.58f2** où le joueur lance un ballon de basketball dans un panier et le jeu compte des points. L'accent est mis sur le **game feel** — l'action, l'interaction et la rétroaction — plutôt que sur les graphiques ou l'UI.

**Contrainte:** Aucun package d'interaction externe (ex: XR Interaction Toolkit) pour gérer les déplacements / grab.

**Temps alloué:** 4 heures  
**Date limite:** Lundi 11 mai 2026 — 17h00

---

## Overview

TrickShot is a first-person basketball trick-shot game built in Unity. The player navigates a low-poly court, aims and launches a basketball using drag-based input, and scores points by sinking the ball through the hoop. The experience is centered around tight game feel: satisfying shot mechanics, slow-motion rim hits, screen-shake, VFX bursts, and audio feedback.

---

## Features

- **First-Person Controller** — Custom character controller with mouse-look and WASD movement, built entirely with Unity's Input System (no third-party movement packages)
- **Drag-to-Shoot Mechanic** — Click and drag to aim; power and angle are calculated from drag distance and direction
- **Ball Physics** — Custom `BasketballBounce` physics material for realistic bounce behavior + cloth net simulation
- **Scoring System** — Trigger-based hoop detector counts valid baskets and updates the score in real time
- **Hit-Stop / Slow-Motion** — A trigger volume around the rim requests a slow-mo hit-stop from the Game Manager when the ball grazes the hoop, amplifying the tension of close shots
- **Game Feel Polish**
  - Post-processing effects tied to ball state (`BallFeel.cs`)
  - Camera shake and HUD rect-shake on score (`CameraFeel.cs`, `UiRectShake.cs`)
  - Cartoon FX particle bursts on score and miss
  - Charge-up, whoosh, swoosh, impact, and victory audio SFX
- **Pause Menu** — Full pause / resume / restart flow integrated with the Input System UI

---

## Project Structure

```
Assets/
├── Scripts/              # All custom C# gameplay scripts
├── Scenes/               # SampleScene.unity (single scene)
├── Prefabs/              # VFX prefabs, character prefabs
├── Audio/                # 9 SFX clips (charge-up, whoosh, impact, victory…)
├── Materials/            # Court materials
├── Physics/              # BasketballBounce.physicMaterial
├── Sprites/              # UI sprites (logo, progress bar…)
├── Fonts/                # Crash-A-Like font + TMP SDF asset
├── Settings/             # URP render pipeline assets (PC + Mobile)
├── Synty/                # Polygon Generic + Starter low-poly art packs
├── JMO Assets/           # Cartoon FX Remaster VFX library
└── MarioParadiso/        # Custom basketball hoop model
```

---

## Scripts

| Script | Description |
|---|---|
| `BasketballShoot.cs` | Core shooting system — drag/aim input, launch physics, power/angle calculation |
| `GameManager.cs` | Singleton — score, timer, VFX/audio triggers, slow-mo hit-stop, game over |
| `BallFeel.cs` | Post-processing and rendering effects tied to ball state |
| `FpsLookAndMove.cs` | First-person look & move via Input System (no third-party packages) |
| `PauseController.cs` | Pause menu — pause/resume/reload, Input System UI integration |
| `ShotSlowMoTrigger.cs` | Rim trigger volume that requests slow-mo from GameManager |
| `HoopScoreDetector.cs` | Collider on hoop that detects valid basket scores |
| `NetBallRegistrar.cs` | Registers the ball's SphereCollider with the net's Cloth component |
| `CameraFeel.cs` | Camera feel/polish (shake on events) |
| `UiRectShake.cs` | Animates RectTransform anchor shake for HUD feedback (unscaled time) |

---

## Tech Stack

- **Engine:** Unity 6000.0.58f2
- **Render Pipeline:** Universal Render Pipeline (URP) 17.3
- **Input:** Unity Input System 1.18
- **UI:** TextMeshPro
- **Animation:** Animation Rigging 1.4
- **Post-Processing:** Post Processing 3.4
- **Art:** Synty Polygon packs (low-poly), JMO Cartoon FX Remaster
- **Platform:** PC (Non-VR)

---

## Build

Platform: **PC (Non-VR)**

A standalone PC build is included alongside this repository.

### Téléchargement (Google Drive)

Archive du projet / build : [**TrickShot.zip**](https://drive.google.com/file/d/1JjTUZkJ5_ztDgGdsi-GxaSOBD4WD9zqx/view?usp=share_link)

### Running from Source

1. Open the project in **Unity 6000.0.58f2**
2. Open `Assets/Scenes/SampleScene.unity`
3. Press **Play** or build via **File → Build Settings → PC, Mac & Linux Standalone**

### Controls

| Action | Input |
|---|---|
| Bounce | Q |
| Look | Mouse |
| Aim & Shoot | Click + Drag |
| Pause | Escape |

---

## Author

Built by **GUILLAUME SENOU** for the Squido Gameplay Programmer test.
