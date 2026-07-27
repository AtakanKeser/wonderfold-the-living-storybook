# Level format

Levels are JSON files in `Assets/Resources/Levels/`. They are meant to be readable in a diff and
editable by hand; the Level Laboratory round-trips the same file, and serialising twice is byte-stable
(asserted by `LevelSerializerTests.SerialisingTwiceIsStable`) so an editor session never churns the file.

Optional keys are omitted when they hold their default. Missing keys are always tolerated on load,
which means adding a field never invalidates the levels already authored.

---

## Shape

```json
{
  "format": 1,
  "id": 10,
  "name": "The Story Seam",
  "book": "midnight-carnival",
  "chapter": 2,
  "width": 8,
  "height": 8,
  "moves": 28,
  "designWinRate": 0.62,
  "dioramaPiece": "mirror-maze-entrance",
  "tutorial": "golden-stitch",
  "intro": ["FOLIO: Where two faces of a page meet, the paper remembers being one sheet."],
  "spawnTables": [ { "colors": ["Crimson","Azure","Meadow","Amber","Violet"] } ],
  "folds":   [ { "id": 1, "rect": [3,0,2,8], "meterCost": 45, "moveCost": 0 } ],
  "front":   { "tiles": ["........", "…"] },
  "back":    { "tiles": ["…"], "obstacles": ["…"] },
  "goals":   [ { "type": "seam", "count": 6 } ]
}
```

`designWinRate` is the win rate this level is *meant* to have, measured by the reference bot. A
tutorial and a chapter finale cannot share a target band, so intent is authored per level and the
laboratory flags drift against it rather than against a global number.

---

## Surfaces and layers

Each face is a stack of character layers, all the same size as the board. **Rows are written top-first**
so the file looks like the board on screen; `LevelBuilder` flips them into bottom-up coordinates —
the one place in the codebase that conversion happens.

| layer | meaning | default |
|---|---|---|
| `tiles` | cell role + any pre-placed piece | `.` |
| `obstacles` | what covers or fills the cell | `.` |
| `groups` | single digit tying obstacles together | `0` |
| `illustration` | `*` marks part of the hidden picture | `.` |
| `spawnGroups` | digit choosing which spawn table refills this cell | `0` |
| `storyLinks` | digit pairing tiles across the page | `0` |

### `tiles` glyphs

| glyph | meaning |
|---|---|
| `.` | playable, filled by gravity |
| `#` or space | hole in the page |
| `S` | spawner (explicit) |
| `r b g y p k` | a fixed Crimson / Azure / Meadow / Amber / Violet / Blush tile |
| `?` | Blank tile — colourless until a match beside it inks it |
| `-` `\|` `*` `^` `@` `=` | pre-placed Rocket-H / Rocket-V / Bloom / Bird / Prism / Stitch |

**Spawners are usually automatic.** If a surface contains no `S` at all, every cell that nothing could
flow into becomes one — off the page, behind a hole, *or facing a different gravity direction*. That
last case is what keeps a sideways-falling fold region alive after it turns over. Write `S` only for
the unusual cases.

### `obstacles` glyphs

| glyph | obstacle | layer | HP | cleared by |
|---|---|---|---|---|
| `T` | Torn Page | blocking | 2 | adjacent match, blast |
| `W` | Wax Seal | blocking | 1 | **blast only** |
| `L` | Fold Lock | blocking | 1 | **blast only**; seals its fold region |
| `A` | Armour Plate | blocking | 2 | adjacent match, blast |
| `K` | Story Knot | overlay | 1 | clearing the tile under it — frees both faces at once |
| `C` | Paper Chain | overlay | 1 | every link in the group, or the chain holds |
| `V` | Vanishing Ink | overlay | 1 | clearing the tile under it; spreads over time |
| `X` | Blank Stain | overlay | 1 | clearing the tile under it |

`groups` ties Paper Chain links together and matches a Fold Lock to the `lockGroup` of the region it
seals.

---

## Fold regions

```json
{
  "id": 1,
  "name": "Tilting Panel",
  "rect": [5, 0, 3, 8],
  "flipAxis": "Vertical",
  "frontGravity": "Down",
  "backGravity": "Right",
  "initialSide": "Front",
  "lockGroup": 0,
  "moveCost": 1,
  "meterCost": 60,
  "maxFolds": 0
}
```

- `rect` is `[x, y, width, height]`, min-inclusive.
- `flipAxis` `Vertical` swings like a book page, `Horizontal` flips like a calendar.
- `backGravity` different from `frontGravity` is where fold levels get their teeth.
- `meterCost` 0 means free; `moveCost` 0 means it does not cost a turn. Tutorials use both.
- `maxFolds` 0 is unlimited. Boss levels cap it to make folding a resource.
- Regions **may not overlap** — `FoldTopology` throws at construction if they do.

---

## Goals

| `type` | extra fields | notes |
|---|---|---|
| `collect` | `color`, `count` | |
| `obstacle` | `obstacle`, `count` | `count` 0 sizes itself to the board |
| `illustration` | `count` | 0 = every cell marked `*` |
| `seam` | `count` | matches spanning a Story Seam |
| `fold` | `count`, `region` | tutorial only |
| `reconnect` | `count` | 0 = every distinct `storyLinks` id |
| `blank` | `count` | Blank Stains scrubbed |
| `walker` | `walker` | id of a walker created by a modifier |
| `boss` | — | wired to the `boss` modifier's phase count |

## Modifiers

| `type` | fields | behaviour |
|---|---|---|
| `vanishing-ink` | `period`, `count`, `startTurn` | ink copies itself onto neighbouring tiles |
| `blank-tide` | `period`, `count`, `startTurn` | The Blank drains cells, leaving stains |
| `walker` | `walkers[]` | characters that BFS toward a target across cleared, correctly-facing cells |
| `boss` | `phases[]`, `period`, `count` | armour layers drawn as `A` grids; the dragon re-armours between them |

---

## Validation

`LevelValidator` runs on every keystroke in the Level Laboratory, on
`wonderfold-sim validate --all`, and from `Wonderfold → Validate All Levels`. It catches, among others:

- a goal asking for a colour no spawn table produces
- a fold region hanging off the page, or overlapping another
- a `lockGroup` no Fold Lock uses — the region could never be folded
- two adjacent cells whose gravity points at each other
- **a wall parked on the cell tiles enter through** (the most common reason an authored level measures
  as unwinnable)
- a walker whose target is sealed off by holes
- a portal pointing at itself or into a cell nothing can occupy
- a Paper Chain group with a single link

Errors block simulation. Warnings do not.
