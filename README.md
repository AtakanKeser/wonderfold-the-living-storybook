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

### Three things the genre cannot currently do

The gameplay core is deterministic, engine-free C# with a bot simulator attached. That was built for
testing — and it turns out to buy three player-facing features the market leaders have no way to ship:

- **A genuinely shared Daily Fold.** Match-3 boards are refilled from whatever random source each
  client has, so a daily puzzle can never be the *same* puzzle for two people. Here the date picks the
  seed, the seed weaves the page, and everybody on Earth plays the identical board — the Wordle
  arrangement, in a genre that has never been able to run it honestly.
- **The Endless Archive: infinite levels that are measured before they are served.** Every generated
  page is played a few hundred times by the same bots that balanced the authored chapter, and the move
  budget and objectives are tuned until the measured win rate lands in its target band. Running out of
  levels is the genre's terminal churn event; this removes it without a content treadmill.
- **Story Threads.** A whole playthrough is a seed plus an ordered list of intents, so it compresses to
  a ~70-character code. Paste a friend's thread and the game rebuilds their page and replays their
  solve move for move — and recomputes the score rather than believing it. No server, no video, no
  trust required.

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

# Weave and audition the first 20 pages of the Endless Archive
dotnet run --project Tools/Wonderfold.Sim -- weave --depth 1 --count 20

# Check a fortnight of shared Daily Fold pages before they reach anyone
dotnet run --project Tools/Wonderfold.Sim -- daily --count 14 --verbose

# Have a bot solve today's page, print its Story Thread, and verify the thread
dotnet run --project Tools/Wonderfold.Sim -- replay --seed 7
```

The current suite contains **103 passing tests**. The Unity layer is also validated with a Unity 6000.5.5f1
batch compilation pass.

## Included systems

| Area | Included in this vertical slice |
|---|---|
| Puzzle | Match-3 resolution, cascades, per-face gravity, dead-board detection, deterministic RNG |
| Fold mechanic | Fold meter, fold locks, alternate surfaces, seam matches, Chain Fold upgrades |
| Power-ups | Ribbon Rocket, Ink Bloom, Origami Bird, Prism Bookmark, Golden Stitch, and combinations |
| Level variety | Eight obstacle types, eight goals, portals, walkers, Blank Tide, and a pop-up boss |
| Meta flow | Chapter map, unlocks, lives, pre-level rocket, in-level tools, story choice, saved diorama progress |
| Living archive | Shared Daily Fold, bot-auditioned Endless Archive, Story Thread share/verify codes, streaks with a paid mend, daily and weekly errands, a coin economy that spends |
| Presentation | Runtime UI, dialogue, paper-style sprites, authored visual overrides, reactive character staging, VFX, match/booster SFX, a Midnight Carnival music loop, haptics, camera pull-back |
| Authoring | JSON level format, Unity Level Laboratory, CLI validation, and balance simulation |

## Architecture

`Assets/Scripts/Core` is plain C# with **no UnityEngine reference**. It is compiled as `netstandard2.1`
and shared by the Unity game, automated tests, and the simulation tools. Unity presentation consumes the
core's ordered event stream; it does not infer gameplay from rendered objects. This keeps play, replay,
testing, and balancing deterministic.

`Assets/Scripts/Core/Live` builds on exactly that determinism: `PageWeaver` invents a page from a seed,
`PageAudition` measures it with the bot simulator until it lands in its win-rate band, and `ReplayCode`
packs a run into a shareable string. Because none of it needs the engine, a shared page can be checked
from the command line before it ever reaches a player.

## Documentation

- [Architecture](Docs/ARCHITECTURE.md)
- [Design notes](Docs/DESIGN.md)
- [Level format](Docs/LEVEL_FORMAT.md)

## Next steps

The vertical slice is playable and validated, and the live archive runs entirely on-device. The
highest-value follow-ups are Play Mode test coverage, a repeatable gameplay capture, frame-by-frame or
skeletal character clips, and an optional server that turns local Daily Fold scores into real
leaderboards — the client already produces verifiable results, so the server only has to rank them.
