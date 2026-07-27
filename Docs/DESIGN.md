# Design — what is implemented, and why it works the way it does

This is the record of the *shipped* design. Where it departs from the original concept document, the
reason is given. For the pitch itself, see the concept doc; for the code, `ARCHITECTURE.md`.

---

## The core loop

```
play a match-3 level
      ↓
earn Story Ink                        (fold meter + goal progress)
      ↓
mend a piece of the pop-up world      (DioramaView.PlayRestoration)
      ↓
the camera pulls back and shows you
the board was a panel of that world
      ↓
next page — a new fold rule, a new obstacle
```

The board **is** the diorama. That is the difference from a decoration metagame, and it is why
`DioramaView` lives behind the board in the same camera rather than in its own scene.

---

## Foldable board

Some cells hide the other side of the page. The player charges a **Fold Meter** by matching, then
turns a marked section over.

The rule that makes it work:

> Folding never moves a tile. It changes which of a coordinate's two faces is in play.

So the player is never learning a new puzzle language. They still drag tiles onto neighbours, still
make fours and fives, still get rockets and bombs. What changes is that half the board just became a
different half-board — with its own tiles, its own obstacles, and possibly its own gravity.

**A hidden face is frozen.** Nothing falls, nothing matches, nothing spawns on a page turned away.
Turn it back and it is exactly as you left it. This is what makes *planning* a fold a real decision
rather than a gamble on a fresh random board — and it is why level start pre-settles both faces.

### Story Seam

Where a folded section meets an unfolded one, adjacent cells disagree about which face they are
showing. That boundary is a **Story Seam**, and a match spanning it mints the game's signature
booster, the **Golden Stitch** — a line that sews *through* the paper, hitting both faces at every
cell it passes.

Measured across the shipped chapter, fold levels average 5–36 seam matches and 1.5–9 Golden Stitches
per playthrough, so the mechanic is genuinely load-bearing rather than decorative.

> **Departure from the concept doc.** The doc mints a stitch on *any* seam-crossing match. A strip
> fold puts a seam underneath a great many ordinary threes, so the shipped default requires four
> (`GameRules.SeamStitchMinLength`). The constant is exposed precisely so the laboratory can retune it.

### Chain Fold

A booster left sitting on a fold's crease is dragged through the paper when the page turns, and comes
out one rung up the ladder:

```
Ribbon Rocket → Ink Bloom → Origami Bird → Prism Bookmark → Golden Stitch
```

If the far face has no room for it (a wax seal is there), the booster is spent. Folding at the wrong
moment costs you something real rather than silently doing nothing.

### Gravity

Each fold region declares how tiles fall on each of its faces. A region that falls `Down` on the front
and `Right` on the back turns into a sideways conveyor the moment you flip it. Gravity is a *local*
rule — every cell has its own direction, and a tile moves into the cell downstream of it — which is
what lets a folded strip pull tiles sideways through the middle of an otherwise ordinary board.

The active surface is one continuous plane: tiles can flow across a seam from a front-facing patch
onto a back-facing one. Physically that is correct — it is one sheet of paper — and it keeps the
gravity rules simple.

---

## Boosters

Familiar maths, unfamiliar dressing. Nobody has to relearn the game.

| match | booster | effect |
|---|---|---|
| 4 in a line | **Ribbon Rocket** | clears the line; curls onto the hidden face when it crosses a seam |
| L or T (5) | **Ink Bloom** | 3×3 ink explosion; breaks sealed obstacles |
| 5 in a line | **Prism Bookmark** | clears a colour, and story-linked twins behind the page |
| 6+ | **Origami Bird** | flies at whatever the level's objective actually needs |
| 4+ across a seam | **Golden Stitch** | sews through the paper, hitting both faces along its line |

Combinations: Ribbon Cross, Ink Comet, Ink Flood, Blank Page, Paper Flock, Prism Cascade, Escort
Flight, Bound in Gold.

**Dragging a booster onto any neighbour is always legal and sets it off where it lands.** This is not
a convenience — it is load-bearing. Wax Seals and Fold Locks take *only* blast damage, so without a
way to aim, every level containing one measured at 0–5% winnable. (See `HANDOFF.md` §5.)

---

## Obstacles

| obstacle | the decision it creates |
|---|---|
| **Torn Page** | two-stage; ordinary matching handles it |
| **Wax Seal** | immune to matching — you must build and aim a booster |
| **Fold Lock** | seals a fold region shut until you blast the clasps |
| **Story Knot** | one knot through both faces; cut it from either side and both let go |
| **Paper Chain** | a chain holds until *every* link is cut |
| **Vanishing Ink** | spreads if ignored |
| **Blank Stain** | The Blank's mark; the thing you scrub in Escape levels |
| **Armour Plate** | the pop-up boss's skin |
| **Misprinted Portal** | swallows a falling tile and spits it out on the face you cannot see |

---

## Level types (the shipped 20)

| # | level | teaches |
|---|---|---|
| 1–2 | The Gate Refuses, Ticket Booth Blues | match, collect |
| 3–4 | The Torn Banner, The Paper Queue | obstacles |
| **5** | **The First Crease** | **folding, free of charge** |
| 6 | Behind the Ticket Stub | the objective lives on the other face |
| 7 | Wax and Wonder | boosters as tools, not fireworks |
| **8** | **Two-Faced Marquee** | one objective split across both faces |
| 9 | The Carousel Tilts | gravity changes when the page turns |
| **10** | **The Story Seam** | seam matches, Golden Stitch |
| 11 | Knotted Pages | Story Knots |
| **12** | **Chain Fold** | leave a booster on the crease |
| 13 | The Locked Leaf | Fold Locks |
| 14 | Paper Chains | grouped obstacles |
| **15** | **Quill's Long Walk** | guide a character; folding strands him |
| 16 | Restore the Illustration | the picture under the board |
| 17 | Misprinted Halls | portals |
| 18 | Escape the Blank | a level that fights back |
| 19 | Reconnect the Story | pairs printed on opposite faces |
| **20** | **The Paper Dragon** | pop-up boss: two wings, three armour phases |

Bold entries are the ones the concept document nominated for the demo reel.

---

## Balance as measurement

Every level declares the win rate it is *meant* to have. The Level Laboratory plays it hundreds of
times with three bots — careless, attentive, and one-ply optimal — and reports win rate, dead-board
rate, moves spent, folds used, seam matches, Golden Stitches, and **which goal was the one that
actually beat the player.**

That last number is the one that changes designs. Nineteen of the twenty shipped levels sit inside
their declared band; the one that does not is flagged in `HANDOFF.md` with the reason.

The laboratory earned its keep during authoring: it found a gravity deadlock that made an entire
sideways-fold level unwinnable, a missing verb (aiming boosters) that broke six levels, and five
levels with a wall parked on the row tiles enter through. None of those would have been obvious from
playing a few hands.

---

## Monetisation shape (not implemented)

No purchase flow is built, but the design assumes it: ad-free free-to-play, lives, coins, pre-level
boosters, a continue offer, a seasonal Story Pass, cosmetic chapter variants.

The one rule the design holds itself to: **monetisation must never make a fold harder to understand
or a board harder to read.** The fold meter is a gameplay resource, not a paywall, and no level is
authored to be unreadable without a purchase.
