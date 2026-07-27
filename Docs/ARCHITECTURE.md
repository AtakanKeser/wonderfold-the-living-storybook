# Architecture

## The one decision everything else follows from

The gameplay core is plain C# with **no reference to `UnityEngine`**. This is enforced, not merely
intended: `Assets/Scripts/Core/Wonderfold.Core.asmdef` sets `"noEngineReferences": true`, so a stray
`using UnityEngine` is a compile error rather than a code review comment.

Three things fall out of that, and they are the reason for the constraint:

1. **The rules can be tested without the engine.** 103 NUnit tests run in ~8 seconds via `dotnet test`,
   and the *same source files* run inside Unity's Edit Mode Test Runner.
2. **Levels can be measured instead of guessed.** The Level Laboratory plays a level five hundred to
   ten thousand times with bots. That is only affordable because a "playthrough" is a few thousand
   struct operations, not a scene.
3. **The same code compiles three ways** — Unity's assembly definition, a `netstandard2.1` class
   library, and a console tool — from one copy on disk. `Tools/*.csproj` link the files with
   `<Compile Include="../../Assets/Scripts/Core/**/*.cs" />`; nothing is duplicated.

`netstandard2.1` + `LangVersion 9.0` on the headless project is a deliberate handcuff: anything that
builds in `dotnet build` is guaranteed to build in Unity 6.

A fourth consequence arrived later and turned out to be the commercially interesting one:

4. **Content can be generated and measured on the player's device.** `Core/Live` weaves a level from a
   seed and then runs the same bot sweep against it before serving it, which is what makes an infinite
   archive of *fair* levels possible. It also makes a run shareable: a playthrough is a seed plus an
   ordered list of intents, so it fits in a ~70-character code that any device can replay exactly.

---

## Layers

```
            ┌──────────────────────────────────────────┐
            │  Editor: Level Laboratory, batch tools    │
            └───────────────┬──────────────────────────┘
                            │ reads/writes level JSON, runs sweeps
┌───────────────────────────┴──────────────────────────┐
│  Game (Unity)                                         │
│  GameBootstrapper → LevelRunner → BoardView / HUD      │
│                                    ▲                   │
│                     event stream   │  PlayerMove        │
└────────────────────────────────────┼───────────────────┘
                                     │
┌────────────────────────────────────┴───────────────────┐
│  Core (plain C#)                                        │
│                                                          │
│   LevelSession ──owns──► BoardModel                      │
│        │                    ▲                            │
│        │  drives            │ mutates                    │
│        ▼                    │                            │
│   ResolutionEngine ─┬─ MatchFinder                       │
│                     ├─ GravityService                    │
│                     ├─ BoosterEngine                     │
│                     ├─ ObstacleService                   │
│                     └─ ShuffleService                    │
│                                                          │
│   FoldService ──► FoldTopology     Goals ◄── listener     │
│                                                          │
│   Live: PageWeaver ─► PageAudition ─► LevelSimulator      │
│         ReplayCode ◄──────────────── PlayerMove history   │
└──────────────────────────────────────────────────────────┘
```

`Core/Live` sits on top of the rest of the core and depends on nothing outside it. It is the only part
of the core that *creates* levels rather than running them, and it validates and measures everything it
creates before handing it back. The Unity side (`LiveOpsService`) decides which page today is and what
finishing it is worth; the core decides only whether a page is fair.

The arrows only ever point one way. `Core` does not know `Game` exists; `Game` does not reach into
`BoardModel` to work out what happened.

---

## How a turn flows

```
player drag
   ↓
BoardInput                    → PlayerMove.Swap(a, b)
   ↓
LevelRunner.Execute           → blocks further input
   ↓
LevelSession.TryExecute
   ├─ modifiers.OnTurnStarted
   ├─ ResolutionEngine.TrySwap
   │     ├─ MoveValidator.ValidateSwap        ← rejects before mutating anything
   │     ├─ BoardModel.SwapTiles
   │     └─ ResolveUntilStable
   │           repeat until no matches:
   │             GravityService.Settle        → TilesMovedEvent, TilesSpawnedEvent
   │             MatchFinder.FindAll          → MatchFoundEvent
   │             clear tiles                  → TilesClearedEvent
   │             ObstacleService.DamageAround → ObstacleDamaged/ClearedEvent
   │             BoosterFactory.TryCreate     → BoosterCreatedEvent
   │             BoosterEngine (queue)        → BoosterActivatedEvent
   ├─ spend a move
   ├─ modifiers.OnTurnEnded                   → BlankSpreadEvent, WalkerMovedEvent
   ├─ goals.OnTurnEnded                       → GoalProgressEvent
   ├─ EnsurePlayable (shuffle if stuck)       → BoardShuffledEvent
   └─ EvaluateOutcome                         → LevelFinishedEvent
   ↓
BoardEventLog.Drain()         → ordered List<BoardEvent>
   ↓
BoardView.Play(events)        → coroutine plays them as animation
   ↓
BoardView.SyncFromModel()     → reconcile, unblock input
```

The whole turn resolves **synchronously** in the core; only the playback is asynchronous. That is why
input is closed for the duration of a batch — a second move would otherwise be applied to a board the
player has not seen yet.

---

## Key types

### `BoardModel`
State container and query surface. Holds two full grids — a Front cell and a Back cell for every
coordinate — plus the fold state and the RNG. **It contains no resolution logic.** Matching, gravity,
boosters and obstacles are separate services that read and mutate it. `Clone()` is a true deep copy,
which is what makes one-ply bot look-ahead possible.

### `FoldTopology` — the central invariant
> **Folding never moves a tile between coordinates.**

A logical coordinate always addresses the same square of the puzzle. What a fold changes is *which of
that square's two faces is in play*. Matching, gravity and adjacency therefore stay ordinary match-3
rules, and the "two boards in one" feeling comes entirely from `SideAt()` switching under them.

Two consequences worth knowing:

- **A Story Seam is an edge between two adjacent cells whose faces disagree** (`IsSeamEdge`). It only
  exists while something is folded, which is why seam matches are a reward for engaging with the
  mechanic rather than a passive bonus.
- **A hidden face is frozen.** Gravity and matching only run on the surface in play. Turn a page away
  and it stays exactly as you left it — that is what makes planning a fold meaningful.

### `ResolutionEngine`
The only class allowed to run the cascade loop. Everything it calls is a small single-purpose service,
and everything it does is reported as events, so a turn is reproducible from a seed and replayable in
the view without the view reading board state.

### `IBoardEvent` / `BoardEventLog`
~25 event types. The view never asks "what changed?" — it is told, in order. `NullEventSink` throws
them away for bot look-ahead; `BoardEventLog.Recording = false` keeps the sequence counter running
without allocating, which is what the simulator uses.

### `IResolutionListener`
How the board tells the level about things that happened to it. Goals, the fold meter and the boss
health bar all subscribe through this one interface, so adding an objective type is a new file and
nothing else — the resolution pipeline never learns what the level is asking for.

### `LevelSession`
The entire public surface of the core. The Unity view, the bots and the editor preview all drive a
session through `TryExecute` and read back `Events`. Nothing above this class touches the board.

---

## Determinism

`DeterministicRandom` is xorshift64* with a splitmix64 seed finaliser. `System.Random` is deliberately
avoided: its sequence is not contractually stable across runtimes, and replays, save/restore and the
simulation harness all depend on "same seed + same inputs ⇒ same board". `PropertyTests` asserts this
directly by playing the same scripted game twice and comparing both the final board *and* the entire
event stream.

Every RNG consumer is ordered deterministically. `BoosterEngine.PickTargets` sorts by
`(score desc, y, x)` rather than relying on dictionary order, for exactly this reason.

---

## Safety valves

The core must never be able to hang the app. Every unbounded loop has a cap in `GameRules`:
`MaxCascadeDepth`, `MaxGravityPasses`, `MaxShuffleAttempts`, `MaxBoosterChainDepth`. Booster chains
are processed through an explicit queue rather than recursion — a rocket setting off a bloom setting
off three more rockets is the moment a match-3 is supposed to feel great, and also the moment a naive
implementation blows the stack.

---

## Where to make a change

| You want to… | Touch |
|---|---|
| add an obstacle | `ObstacleType` enum + `ObstacleCatalog` table + `LevelGlyphs` |
| add a booster | `BoosterType` enum + `BoosterEngine.Collect*` + `BoosterFactory` + combo table |
| add an objective | one new file in `Core/Goals` + a case in `GoalSpec.Create` |
| add a level behaviour | one new `ILevelModifier` + a case in `ModifierSpec.Create` |
| change a tuning number | `GameRules` — no rule constant is allowed to hide inside a service |
| change how something looks | `Assets/Scripts/Game` only |
| add a level | Level Laboratory, then **Simulate** before saving |
