# Wonderfold: The Living Storybook

**A foldable match-3 adventure set inside a living pop-up book.**

Wonderfold keeps the familiar clarity of a tile-matching puzzle, then gives the board a physical twist:
parts of the page can be folded over to reveal their reverse side. A fold changes the playable surface,
its tiles, obstacles, and sometimes gravity itself. Set up a match across the crease to create a
**Golden Stitch**—a signature power-up that sews both sides of the page together.

Every repaired level is also a piece of *The Midnight Carnival*, a paper diorama slowly restored from
the storybook world. The puzzle board is not a separate screen that pays for decoration: at the end of a
win, the camera pulls back and the repaired panel takes its place in the scene.

> Unity 6000.5.5f1 · C# · 20-level playable vertical slice

## Why it is different

- **One board, two surfaces.** Fold regions reveal the back of the same page instead of switching to a
  separate puzzle.
- **A new strategic axis.** Each side can carry different tiles, blockers, gravity, objectives, and
  booster opportunities.
- **The Story Seam.** Cross-crease matches create Golden Stitches that affect both faces of the paper.
- **Board-to-world continuity.** Winning restores a live pop-up carnival piece through the same camera
  space used for play.
- **Built for measured iteration.** The exact gameplay core powers Unity, command-line simulation, level
  validation, and deterministic tests from one source of truth.

## Play it

1. Open this folder with **Unity Hub** using Unity **6000.5.5f1**.
2. Open an empty scene and press **Play**. The game builds its camera, UI, board, map, and diorama at
   runtime.
3. Choose an unlocked page from **The Wonderfold Archive**, optionally pack a starter rocket, and enter
   the page.

During a level, drag adjacent tiles to swap them, tap boosters to activate them, and use the Fold buttons
when the meter is full. The tool pouch contains a targeted hammer and ribbon rocket; lives recharge over
time. Dialogue and the carnival presentation are part of the playable flow.

## Verify or explore from the terminal

```bash
# Core tests run without Unity
dotnet test Tools/Wonderfold.sln

# Validate all authored levels
dotnet run --project Tools/Wonderfold.Sim -- validate --all

# Play an ASCII fold level in the terminal
dotnet run --project Tools/Wonderfold.Sim -- play --level 10 --seed 42

# Generate a balance report with deterministic bots
dotnet run --project Tools/Wonderfold.Sim -- simulate --all --runs 500
```

The current suite contains **63 passing tests**. The Unity layer is also validated with a Unity 6000.5.5f1
batch compilation pass.

## Included systems

| Area | Included in this vertical slice |
|---|---|
| Puzzle | Match-3 resolution, cascades, per-face gravity, dead-board detection, deterministic RNG |
| Fold mechanic | Fold meter, fold locks, alternate surfaces, seam matches, Chain Fold upgrades |
| Power-ups | Ribbon Rocket, Ink Bloom, Origami Bird, Prism Bookmark, Golden Stitch, and combinations |
| Level variety | Eight obstacle types, eight goals, portals, walkers, Blank Tide, and a pop-up boss |
| Meta flow | Chapter map, unlocks, lives, pre-level rocket, in-level tools, story choice, saved diorama progress |
| Presentation | Runtime UI, dialogue, paper-style sprites, authored visual overrides, reactive character staging, VFX, match/booster SFX, a Midnight Carnival music loop, haptics, camera pull-back |
| Authoring | JSON level format, Unity Level Laboratory, CLI validation, and balance simulation |

## Architecture

`Assets/Scripts/Core` is plain C# with **no UnityEngine reference**. It is compiled as `netstandard2.1`
and shared by the Unity game, automated tests, and the simulation tools. Unity presentation consumes the
core's ordered event stream; it does not infer gameplay from rendered objects. This keeps play, replay,
testing, and balancing deterministic.

## Documentation

- [Architecture](Docs/ARCHITECTURE.md)
- [Design notes](Docs/DESIGN.md)
- [Level format](Docs/LEVEL_FORMAT.md)

## Next steps

The vertical slice is playable and validated. The highest-value follow-ups are Play Mode test coverage,
a repeatable gameplay capture, frame-by-frame or skeletal character clips, and online live-event systems.
