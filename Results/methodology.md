# Methodology — A* versus Dijkstra on grid environments

How the experiments in `runs.csv` were conducted, and how their numbers must be
read. The technical details of the machine are in `environment.md`, which is
machine-generated on every run and cannot drift from it.

Bulgarian version below / Версия на български по-долу.

---

## 1. What the program does

A .NET 10 console program, C# 14, with **no third-party libraries** — the .NET
base class library only, including the PNG encoder that writes the figures.

The program first asks whether to run the **full matrix** — all 40
configurations, unattended. Declining that, it runs exactly one configuration,
prompted for as three fixed choices:

1. **Grid size** — 50×50, 100×100, 250×250 or 500×500 (grids are square)
2. **Obstacle density** — 0 %, 10 %, 20 %, 30 % or 40 %
3. **Movement model** — 4-directional or 8-directional

The movement model then fixes the algorithm set:

| Movement model | Algorithms compared |
|---|---|
| 4-directional | Dijkstra, A* Manhattan, A* Euclidean |
| 8-directional (diagonal = √2) | Dijkstra, A* Octile, A* Euclidean |

Each configuration executes:

```
for run in 1..30
    map  = GenerateMap(mapSeed(size, density, run))
    pair = SampleEndpoints(map, pairSeed(size, density, run))
    for each of the 3 algorithms
        record(Search(map, pair, model, algorithm))
```

**30 independently generated maps × 3 algorithms = 90 measured executions = 90
rows**, plus 30 figures, written into a directory named for that configuration.
The full matrix is 4 sizes × 5 densities × 2 models = 40 configurations =
**3,600 rows and 1,200 figures**, and completes in under a minute.
Configurations run smallest grid first, so a problem surfaces on a 50 × 50 grid
rather than after the 500 × 500 work.

All 90 executions of a run share **one** `(map, start, goal)` triple, so the
three algorithms always solve a byte-identical problem instance. The movement
model is deliberately *not* part of the seed, so the 4-directional and
8-directional results also fall on identical maps and are directly comparable.

**Measurements are Release-build only.** A Debug build runs roughly twice as
slow and its timings are meaningless; `environment.md` records the **build
configuration** it ran from, so a Debug-timed data set is identifiable after
the fact.

## 2. Obstacle generation

Shuffle all cell indices with a seeded generator and block exactly
`floor(density × width × height)` of them.

Implemented as the prefix of a Fisher–Yates shuffle: iterations past the *k*th
never touch the first *k* positions, so stopping early is byte-identical to
shuffling all 250,000 indices and keeping the first 50,000 — same draws, same
map, without the wasted work.

The distribution is uniform and **endpoint-independent**: the map is built
*before* start and goal are chosen, so no cell is ever special-cased and the
obstacle statistics are not biased by carving out a route.

Note on `floor()`: a density must be exactly representable. One third is not —
in double precision `1/3 × 900` evaluates to 299.9999999999999, giving 299
obstacles rather than 300. The five densities used here (0, 10, 20, 30, 40 %)
are unaffected.

## 3. Start and goal generation

Start and goal are drawn uniformly from the **free** cells of the finished map,
using a separate seeded generator. Drawing from a list of the free cells rather
than from all cells keeps the draw uniform without wasting attempts on
obstacles, which at 40 % density would be two draws in five.

A pair is **rejected and redrawn while the two points are closer than
0.5 × the grid diagonal**, up to 10,000 attempts. Without that rule most routes
would be short on every grid size, and grid size would vary together with route
length — which would make the effect of search-space size impossible to
isolate. The rule has a measurable consequence of its own; see caveat 1.

If a map is too fragmented to yield a valid pair, the program **reports that
rather than quietly relaxing the rule**, and the three rows for that run are
written with the environment intact and everything downstream of it empty.

Connectivity is deliberately **not** checked. Whether a route exists is a
result, not a precondition.

## 4. Seed scheme — one integer reproduces everything

```
mapSeed      = H(masterSeed, width, height, density, run)
endpointSeed = H(masterSeed, width, height, density, run, 0x5EED)
```

`H` is the finalising step of splitmix64, written out in the source so it can be
quoted and reimplemented:

```
z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9
z = (z ^ (z >> 27)) * 0x94D049BB133111EB
H = z ^ (z >> 31)
```

Fields are folded in one at a time, each addition offset by the odd constant
0x9E3779B97F4A7C15, so the derivation is order-sensitive and a one-bit change in
any field gives an unrelated seed. Density enters as its **IEEE 754 bit
pattern**, so 33.0 % and 33.3 % cannot collide.

The random stream is splitmix64 as well, not the standard library generator:
Microsoft does not guarantee `System.Random`'s algorithm across major .NET
versions, and the study's reproducibility claim must not rest on someone else's
compatibility promise. Integers in a range are drawn by rejection, so there is
no modulo bias. The language's own hash functions (`GetHashCode`,
`HashCode.Combine`) are **not** used anywhere in the derivation: they are
randomised per process, so the same seed would silently produce different
environments on every launch.

**Verified across processes, not assumed:** two separate invocations at the same
master seed produced identical map seed, endpoint seed, start, goal, expanded
nodes, generated nodes, peak open set, path cost and allocated bytes. Only the
measured times differed. A different master seed produced a different
environment.

Consequently no generated maps need to be shipped. A single row of `runs.csv`
carries `master_seed`, `map_seed`, `pair_seed`, `grid_size`,
`obstacle_density` and `run` — enough to rebuild the exact environment it
describes.

## 5. Movement models and heuristics

**4-directional:** N, E, S, W; every step costs 1.

**8-directional:** the four orthogonal steps cost 1, the four diagonal steps
cost √2.

**Diagonals are permissive.** The only legality test for any step is
"destination in bounds and free". Two cases are therefore allowed: cutting the
corner of a single obstacle, and passing through the zero-width point where two
obstacles touch corner to corner. This makes the model **exactly 8-connectivity
(the Moore neighbourhood)** — see caveat 6 for why that matters and what it
costs.

Admissibility, as the study relies on it:

| Heuristic | 4-directional | 8-directional |
|---|---|---|
| Manhattan `|dx| + |dy|` | **exact**, admissible and consistent | inadmissible — overestimates a diagonal step as 2 rather than √2; not used there |
| Octile `(dx + dy) + (√2 − 2)·min(dx, dy)` | admissible, loose | **exact**, admissible and consistent |
| Euclidean `sqrt(dx² + dy²)` | admissible, weak lower bound | admissible and consistent, weaker than octile |
| Zero (h ≡ 0) | trivially admissible — this is Dijkstra | trivially admissible |

Euclidean is deliberately kept on the 4-directional model despite being a weak
bound there: the contrast between an exact heuristic and a weak one is part of
what the study measures.

All four heuristics are invoked through the same delegate indirection, including
Zero, so none of them gains a measurement advantage from how it is dispatched.

## 6. The search

Both algorithms share one structure, so their accounting is comparable
column for column.

**Open set:** a `SortedSet` of `(f, h, x, y)` tuples. A tuple's default
comparer compares members in order, which states the tie-break policy
**completely and explicitly**: ascending `f`, then lower `h` (the cell closer to
the goal), then lower `x`, then lower `y`. A binary-heap priority queue was
rejected for this: it has no secondary key and no stability guarantee, so ties
beyond `f` would fall to an internal sift order — reproducible for a fixed
insertion sequence, but not *specifiable*, and liable to change on a runtime
upgrade with no diff in our own code. Caveat 2 is why that matters.

**Goal test on pop, not on generate.** Only a popped node is known to have its
final cost.

**Dijkstra stops as soon as the goal is popped**, exactly as A* does. It is
written as its own class rather than as A* with h ≡ 0, so that the baseline
reads as Dijkstra; the optimality cross-check is the guard against the two
loops drifting apart. A full distance field to every cell would measure a
different problem and would flatter A* unfairly.

**Insertion is add-only.** When a cell's cost improves, the improved entry is
added and the superseded one is left in the open set. This is not a correctness
problem: every heuristic used here is consistent, so a cell's cost is already
optimal when it is first popped, and a stale entry always carries a higher `f`
and therefore arrives later, finding nothing to improve. It has two consequences
that must be stated — the accumulation of dead entries changes what
`peak_open_set` means (section 7), and it is what makes the closed set necessary
(caveat 7).

**Closed set:** a flat `bool` array, checked on pop. A cell already expanded is
discarded without being counted, so "expanded" is one-per-cell by construction.
The same array doubles as the explored set the figures draw, which is why
capturing the explored set costs the measurement nothing.

## 7. What each recorded quantity means

| Quantity | Definition |
|---|---|
| `expanded_nodes` | Cells **popped from the open set and processed**. Counted once per cell, guaranteed by the closed set. This is the study's primary metric. |
| `generated_nodes` | **Entries pushed** onto the open set. Always larger than `expanded_nodes`. |
| `peak_open_set` | The largest the open set ever grew. **Because the open set is add-only, superseded entries are never removed, so this counts ACCUMULATED ENTRIES — not the size of the search frontier.** It must not be read as a frontier size or as a memory bound on the frontier. |
| `path_cost` | Sum of the step costs along the returned path: 1 orthogonally, √2 diagonally. |
| `path_length_cells` | Number of cells in the path, start and goal included. |
| `success` | Whether a route was found at all. |
| `execution_time_ms` | Wall-clock duration of the `Search` call — see section 9. |
| `allocated_bytes` | Bytes allocated during the search — see section 9. |
| `straight_line_distance`, `manhattan_distance` | Distance between the endpoints, for normalising path cost against the problem's difficulty. |
| `optimal_cost_match`, `cost_deviation` | The optimality cross-check — see section 10. |

The distinction between expanded, generated and "visited" is not cosmetic;
"expanded nodes" is ambiguous in the literature, so both counters are recorded
with identical accounting in both algorithms.

## 8. The empty-cell rule in `runs.csv`

**A column is empty only when the quantity was not measured. Never `0`, never
`NaN`.**

- A search that ran and found **no route** reports real `expanded_nodes`,
  `generated_nodes`, `peak_open_set`, `execution_time_ms` and `allocated_bytes`,
  with `path_cost` and `cost_deviation` empty. Those searches did real work and
  the work is the measurement.
- A run whose **endpoints could not be drawn** is empty from `start_x` onward:
  nothing downstream of the environment happened.

Empty is what pandas and R read as NA. A `0` in either case would be a
fabricated measurement — an unreachable goal is not a zero-cost path, and a
search that never ran did not expand zero nodes.

## 9. How execution time and memory are measured

- **`System.Diagnostics.Stopwatch`**, high-resolution, wrapped around **the
  `Search` call and nothing else**. Map generation, endpoint sampling, figure
  rendering and CSV writing are all outside the measured region. There is
  exactly one definition of that region in the program, and both the harness and
  the single-map demo call it.
- **Memory** is the `GC.GetTotalAllocatedBytes(precise: true)` delta across the
  search — deterministic allocation churn, not a noisy working-set reading. Both
  snapshots sit outside the timed region, because `precise: true` is not free,
  and the stopwatch is constructed before the first snapshot so its own
  allocation is not charged to the search.
- **A full blocking garbage collection is forced before each of the 90
  searches**, outside the measured region. The per-search work arrays are all
  far over the 85 KB large-object threshold (at 500×500: 2.0 MB + 1.0 MB +
  0.25 MB), and large-object allocation is what triggers generation-2
  collections. A pause landing inside a timed search would bias the algorithms
  **unevenly**, since they allocate at different rates. Clearing before each of
  the 90 searches, not merely each of the 30 maps, stops the third algorithm
  inheriting the first two's churn.
- **A warmup pass of 32 executions per algorithm** runs on a throwaway 50×50 map
  at the configuration's density before anything is timed, and its results are
  discarded. The map is drawn at run index 0, which the measured runs 1–30 never
  use, so the warmup stays inside the documented seed scheme while never being
  one of the maps under measurement.
- **Tiered compilation is disabled in the project file**, unconditionally.
- **Release build only.**

The last three are all mandatory and none is sufficient alone. Caveat 8 is the
measurement that establishes this.

## 10. Optimality cross-check

Within each `(map, start, goal, movement model)` group, all three algorithms'
path costs are compared against the Dijkstra baseline with a **relative
tolerance of 1e-9**, written per row as `optimal_cost_match` and
`cost_deviation`, and summarised per invocation as one pass/fail plus the worst
observed deviation.

Exact `==` would be the wrong test: in the 8-directional model two routes of
equal cost accumulate `g` by adding √2 and 1 in different orders, so their sums
differ in the last few bits. The worst deviation observed between Dijkstra and
A* on the same map is 1.34e-16 — fifteen orders of magnitude inside the
tolerance, and not zero.

Where one algorithm finds a route and another does not, the check compares
existence instead of cost. Any disagreement is reported and sets a non-zero exit
code.

## 11. Figures

**One three-panel composite PNG per run — 30 per configuration.** All three
algorithms appear side by side on the same map with the same start and goal, so
the comparison is the default view rather than something a reader assembles by
flipping between files.

Panel order is fixed: **Dijkstra** (baseline) left, the movement model's
**primary heuristic** centre (Manhattan for 4-directional, Octile for
8-directional), **Euclidean** right. Each panel is bordered, with its caption
strip underneath, and carries six fixed rows:

```
DIJKSTRA                          A* OCTILE
GRID: 500 X 500 8-DIR             GRID: 500 X 500 8-DIR
EXPANDED: 175626  PATH: 337 CELLS EXPANDED: 14335  PATH: 337 CELLS
OBSTACLES: 20.0 %                 OBSTACLES: 20.0 %
TIME: 104.22 MS                   TIME: 8.71 MS
MEMORY: 9.84 MB                   MEMORY: 1.12 MB
```

The obstacle percentage is counted off the drawn mask rather than taken from the
requested density, so a caption can never drift from its own picture.

Colour legend: free = white, obstacle = near-black, **explored = light blue**,
path = red, start = green, goal = orange. Because the explored set is drawn,
each panel covers the generated environment, the obstacles, the endpoints, the
route and the search space at once.

Scale, overridable with `--scale N`:

| Grid | px per cell | Panel | Composite |
|---|---|---|---|
| 50×50 | 10 | 500×500 | ~1520×560 |
| 100×100 | 6 | 600×600 | ~1820×660 |
| 250×250 | 3 | 750×750 | ~2270×810 |
| 500×500 | 2 | 1000×1000 | ~3020×1060 |

Files are written as `run01.png` … `run30.png`, zero-padded so a directory
listing sorts in run order, inside the configuration's own directory —
`size<N>_density<D>_<M>dir/` — alongside that configuration's CSV pair. A
configuration's directory is therefore self-contained: the data and the pictures
of the same 30 runs, and nothing else.

**The figures are rendered from the timed run itself, not from a re-run.** The
explored set a panel draws *is* the closed-set array the search maintained for
its own guard, so capturing it cost the measurement nothing; only materialising
it as coordinates allocates, and that happens after the clock has stopped. The
composite is encoded after every search of that run has been timed, and the next
run's first search is preceded by a full blocking collection anyway, so
rendering cannot leak into a measurement. At 500×500 — the worst case — a whole
configuration including all 30 composites costs about 3 seconds.

## 12. The data files

`runs.csv` is the study's data: **comma-delimited, dot decimals, every numeric
field written with the invariant culture**, one row per execution, with the
header written once.

It exists at two levels, with identical columns:

- **Per configuration**, in that configuration's own directory: its 90 rows,
  beside the 30 figures of the same runs. This is the record. Re-running a
  configuration rewrites it, so its rows and its pictures always describe the
  same 30 runs.
- **At the results root**, spanning everything on disk: 3,600 rows for the full
  matrix. `grid_size` and `obstacle_density` are columns, so this is the file to
  load for statistics. It is **derived** — rebuilt by concatenating the
  per-configuration files after every run, so it always covers exactly what is
  present and cannot drift. A part whose header does not match is an error
  rather than something skipped quietly, which would understate the data set.

25 columns, in order:

```
master_seed, grid_size, obstacle_density, movement_model, algorithm, heuristic,
run, map_seed, pair_seed, start_x, start_y, goal_x, goal_y,
straight_line_distance, manhattan_distance, path_cost, path_length_cells,
expanded_nodes, generated_nodes, peak_open_set, execution_time_ms,
allocated_bytes, success, optimal_cost_match, cost_deviation
```

Conventions worth knowing before parsing:

- `grid_size` is the side length as a bare integer; grids are square.
- `obstacle_density` is the fraction, `0.00`–`0.40`.
- `path_cost` is written with 17 significant digits, so cost equality can be
  re-checked from the file at the 1e-9 tolerance.
- Booleans are `TRUE` / `FALSE` in upper case — the one spelling Excel, R's
  `read.csv` and pandas all read back as a boolean rather than as text.
- Seeds are `0x` plus 16 upper-case hex digits. The prefix is not decoration:
  bare hex like `0000000000001E50` parses as 1E+50 in a spreadsheet and the
  value is destroyed silently.
- `master_seed` is not part of the twelve required columns; it is first in the
  row because a file spanning configurations run at different times cannot
  assume every one of them used the same seed.
- Empty means not measured — see section 8.

`runs_excel.csv` beside it is a **derived, view-only copy**: semicolon-delimited
with comma decimals, for double-clicking on a locale whose list separator is a
semicolon and whose decimal separator is a comma. It is rewritten in full from
`runs.csv` after every run, so the two can never be out of step, and nothing is
ever read back out of it. Only numeric columns have their decimal separator
rewritten, which is what keeps the `0x…` seeds text. **The canonical file is
`runs.csv`** — comma-delimited with dot decimals is what statistical tools
expect, and it is not changed to suit a spreadsheet.

`environment.md` is machine-generated on every run and records the language,
runtime, build configuration, OS, CPU, cores, RAM, the timing clock's actual
resolution and the runtime settings that affect timings.

## 13. Interpretation caveats

These are not defects. Each one changes how a number in `runs.csv` must be read,
and several are counter-intuitive.

### Caveat 1 — At 40 % density the 4-directional model sits on the percolation threshold

For a 4-connected square lattice a path exists only while the blocked fraction
is below approximately **40.7 %**. At the 40 % density in the design we are
within half a percentage point of that cutoff, so a substantial share of
start–goal pairs genuinely have **no path** — especially distant pairs. The
8-directional model is unaffected; its threshold is approximately 59.3 %
blocked.

Measured at 50×50 / 40 % / 4-directional, 30 runs: a route existed in only
**11 of 30** runs. All three algorithms agreed on non-existence in every one of
the 19 failures **and expanded exactly the same number of cells in each** — a
search that finds no route floods the entire reachable component, and no
heuristic changes which cells those are. The heuristic only changes the *order*
of the flood. That is why expanded-node counts are identical when the answer is
"no route" and differ widely when it is not. Component sizes ranged from 1 cell,
where the start is sealed off by its four neighbours, to 1,138 cells.

The endpoint rule in section 3 **contributes to this rate by design**: pairs
must be at least half the grid diagonal apart, and distant pairs are exactly the
ones that fail near the threshold. The failure rate is therefore a property of
the stated experimental design, not of the environment alone, and the two must
be quoted together.

The density is kept at 40 % deliberately. The collapse in success rate is itself
a result — but it is expected behaviour of the environment, not a limitation of
the program.

### Caveat 2 — Where the heuristic is exact, the tie-break decides the expanded-node count

On an obstacle-free grid the primary heuristic is exact: Manhattan for
4-directional, octile for 8-directional. Every cell on every optimal path then
has the same `f = g + h`. An empty grid has exponentially many equal-cost
optimal paths, so `f` discriminates nothing and the number of expanded nodes is
determined **entirely by how f-ties are broken** — anywhere from O(path length)
to O(rectangle area).

Since `expanded_nodes` is the study's primary metric, a single deterministic
tie-break is applied identically in both algorithms and documented in section 6:
ascending `(f, h, x, y)`. On an empty 10×10 grid corner to corner this expands
10 cells for 8-directional octile and 19 for 4-directional Manhattan — in both
cases only the cells of the path itself, the best possible outcome. A different
tie-break on the same grid could expand the whole rectangle.

### Caveat 3 — "Expanded nodes" needs a pinned definition

Expanded (popped and processed) ≠ generated (pushed) ≠ visited. Both counters
are recorded, with identical accounting in both algorithms, and the closed-set
guard keeps re-pops from being double-counted. Section 7 is the definition that
applies throughout.

### Caveat 4 — Path-cost equality must be tested with a tolerance, not `==`

See section 10. In the 8-directional model √2 accumulates differently depending
on the order in which a route's steps are assembled, so exact equality is the
wrong test even between two provably optimal paths.

### Caveat 5 — Compute budget is not a constraint

The worst case — 500×500 Dijkstra at 0 % density, flooding essentially all
250,000 cells — costs on the order of a hundred milliseconds. No grid size had
to be dropped, and 500×500 including all 30 figures completes in about
3 seconds.

### Caveat 6 — A rendered path can appear to cross a diagonal chain of obstacles

This is a direct consequence of the permissive diagonal rule in section 5. Where
two obstacles touch corner to corner, the opposite diagonal step through that
same point is legal and costs √2 — so **a diagonal wall is not a wall**. In the
figures the red path will sometimes appear to pass straight through a black
diagonal line. **This is correct 8-connectivity behaviour, not a rendering or
search defect.** The sentence belongs in any caption written under a figure, and
it is stated here because a reader who is not told will report it as a bug.

It is worth stating positively as well: permissive diagonals are exactly what
make the model the Moore neighbourhood, which is why the 59.3 % threshold in
caveat 1 holds as a published constant and why octile is the *exact* remaining
cost on an obstacle-free grid, keeping caveat 2's analysis clean.

The cost of the choice is that `success` measures **Moore connectivity**, not
the connectivity seen by an agent of non-zero physical size.

### Caveat 7 — Octile is consistent in exact arithmetic but only to within rounding in `double`, and that perturbs the primary metric

Found by measurement, not by reasoning.

A consistent heuristic guarantees that a cell's cost is final when it is first
popped and that it is never expanded again. In IEEE 754 double precision that
guarantee holds only to about 1e-15. Two equal-cost routes accumulate `g` by
adding √2 and 1 in different orders, so their sums differ in the last few bits —
and an implementation without a closed set reads that difference as a genuine
improvement.

Measured over 40 generated 30×30 grids at 30 % density, 8-directional:
**148 re-expansions of cells that were already settled, none of which discovered
a new cell**, with a largest apparent improvement of 3.553e-15 — rounding noise,
not a better route. Two consequences:

- The choice between several equally optimal routes was being decided by a
  rounding artefact.
- `expanded_nodes` differed on 4 of the 40 grids, from **−9 to +9** cells — in
  **both** directions, because the noise perturbs `f` and `f` sets the pop
  order. So this is not "extra work" that could be bounded above.

**The closed-set guard is what removes it. It is therefore load-bearing for the
reproducibility of `expanded_nodes`, not merely an optimisation.** Running the
same search with the guard and without it, changing nothing else, makes the two
agree cell for cell — which isolates the guard as the single cause.

For reading the data: in the 8-directional model `expanded_nodes` is sensitive
to floating-point accumulation order, and **the tie-break policy alone does not
fully determine it** — the closed-set policy is what makes it reproducible.
This is also the strongest justification for the tolerance in caveat 4:
floating-point noise does not merely affect cost *comparisons between
algorithms*, it affects the *search itself*.

### Caveat 8 — Timing is contaminated by JIT compilation in two separate ways, and the second is worse

C# compiles to intermediate bytecode at build time; the runtime compiles each
method to native code on its first call. Fully measured by a 2×2 A/B — warmup
on/off × tiered compilation on/off — at 50×50 / 0 % / 8-directional, medians
over runs 2–30:

| | Dijkstra run 1 | Dijkstra median | measured A* octile speedup |
|---|---|---|---|
| warmup + tiering off (**used**) | 0.355 | 0.347 | **13.4×** |
| no warmup + tiering off | 7.901 | 0.355 | 13.8× |
| no warmup + tiering on | 13.653 | 0.408 | 3.4× |
| warmup + tiering on | 0.401 | 0.374 | 3.2× |

*(milliseconds)*

**The predicted step change did not appear.** .NET promotes a method to fully
optimised code after roughly 30 calls, and the experiment runs exactly 30 runs
per configuration, so that threshold should have fallen inside the measurement
set. It did not: every series is flat from run 2 onward, because the search loop
is long-running and on-stack replacement promotes it mid-call during run 1
rather than at run 30.

Two other distortions did appear, and both are worse than a step change, because
a step change is at least visible:

1. **First-call JIT is large.** Without a warmup pass, run 1 cost Dijkstra
   7.901 ms against a 0.347 ms median (23×) and A* octile 1.471 ms against
   0.026 ms (51×) — inflating a configuration's mean by 1.7× and 2.8×
   respectively. The warmup pass removes it completely.
2. **Tiered compilation inflates the *fast* algorithms most, and a 32-execution
   warmup does not fix it.** Median time with tiering on versus off: Dijkstra
   1.08×, A* Euclidean 2.57×, A* octile **4.47×**. The cheaper the search, the
   larger the share of it spent in unoptimised or instrumented code, while
   Dijkstra's long flood is promoted mid-call and escapes. **The measured
   A*-over-Dijkstra advantage collapses from 13.4× to 3.2×.**

That second bias runs *against* the study's own hypothesis — leaving tiering
enabled would have understated the benefit of A* by a factor of four — and it is
invisible in the data, because those timings are flat, plausible and perfectly
reproducible. They simply measure the wrong program.

Both mitigations are therefore required, for different reasons: the warmup
removes the first-call spike, and disabling tiered compilation restores the
level.

**Scope:** measured at 50×50, where one search takes from tens of microseconds
to a third of a millisecond. The *direction* of the bias follows from which code
the runtime has optimised and holds at any size, but **4.47× is specific to that
configuration and must not be quoted as a general constant.**

## 14. What the study does not claim

- The absolute times are properties of this machine, this runtime and these data
  structures, not of the algorithms. `expanded_nodes` is the portable metric;
  `execution_time_ms` is the one that needed section 9's five mitigations before
  it meant anything at all.
- The open set is a balanced binary tree, chosen so the tie-break is
  *specifiable* rather than merely reproducible. An array-backed heap would be
  perhaps 2–4× faster in absolute terms. That affects all three algorithms in
  the same direction.
- `success` measures Moore connectivity, not the connectivity an agent of
  physical size would see (caveat 6).
- Obstacles are uniform random, which is the documented generation principle.
  Structured environments — rooms, corridors, mazes — are a different question
  and are not addressed here.

---
---

# Методика — A* спрямо Dijkstra в решетъчни среди

Как са проведени експериментите в `runs.csv` и как трябва да се разчитат
техните числа. Техническите данни за машината са в `environment.md`, който се
генерира автоматично при всяко изпълнение и затова не може да се разминава с
действителността.

## 1. Какво прави програмата

Консолна програма на .NET 10, C# 14, **без никакви външни библиотеки** — само
стандартната библиотека на .NET, включително и за кодирането на PNG
изображенията.

Програмата първо пита дали да бъде изпълнена **пълната матрица** — всичките 40
конфигурации, без намеса. При отказ се обработва точно една конфигурация, зададена
чрез три фиксирани въпроса:

1. **Размер на решетката** — 50×50, 100×100, 250×250 или 500×500 (решетките са
   квадратни)
2. **Плътност на препятствията** — 0 %, 10 %, 20 %, 30 % или 40 %
3. **Модел на движение** — четири или осем направления

Моделът на движение определя набора от алгоритми:

| Модел на движение | Сравнявани алгоритми |
|---|---|
| четири направления | Dijkstra, A* Manhattan, A* Euclidean |
| осем направления (диагонал = √2) | Dijkstra, A* Octile, A* Euclidean |

Всяка конфигурация изпълнява:

```
за изпълнение 1..30
    среда = ГенерирайСреда(mapSeed(размер, плътност, изпълнение))
    двойка = ИзберийТочки(среда, pairSeed(размер, плътност, изпълнение))
    за всеки от 3-та алгоритъма
        запиши(Търсене(среда, двойка, модел, алгоритъм))
```

**30 независимо генерирани среди × 3 алгоритъма = 90 измервани изпълнения = 90
реда**, плюс 30 фигури, записани в директория, наречена на самата конфигурация.
Пълната матрица е 4 размера × 5 плътности × 2 модела = 40 конфигурации =
**3600 реда и 1200 фигури**, и завършва за по-малко от минута. Конфигурациите се
изпълняват от най-малката решетка нататък, така че евентуален проблем се
проявява при 50 × 50, а не след работата при 500 × 500.

И трите алгоритъма в едно изпълнение работят върху **една и съща** тройка
`(среда, начало, цел)`, тоест решават напълно идентична задача. Моделът на
движение умишлено **не** участва в началното число, така че резултатите при
четири и при осем направления също попадат върху еднакви среди и са пряко
сравними.

**Измерванията се правят само с построяване в конфигурация Release.**
Построяването в Debug работи приблизително два пъти по-бавно и времената му са
безсмислени; `environment.md` записва **конфигурацията на построяване**, с която
е бил изпълнен, така че набор от данни, измерен в Debug, може да бъде разпознат
впоследствие.

## 2. Генериране на препятствията

Индексите на всички клетки се разбъркват със seeded генератор и се блокират
точно `floor(плътност × широчина × височина)` от тях.

Реализирано е като началната част от разбъркване по Fisher–Yates: итерациите
след *k*-тата никога не докосват първите *k* позиции, така че спирането по-рано
е байт по байт същото като разбъркване на всички 250 000 индекса и запазване на
първите 50 000 — същите избори, същата среда, без излишната работа.

Разпределението е равномерно и **независимо от началната и крайната точка**:
средата се изгражда, *преди* те да бъдат избрани, така че нито една клетка не се
третира по особен начин и статистиките на препятствията не са изместени от
разчистването на маршрут.

Забележка за `floor()`: плътността трябва да е точно представима. Една трета не
е — при двойна точност `1/3 × 900` дава 299.9999999999999, тоест 299 препятствия
вместо 300. Използваните тук пет плътности (0, 10, 20, 30, 40 %) не са засегнати.

## 3. Генериране на началната и крайната точка

Началната и крайната точка се избират равномерно измежду **свободните** клетки
на готовата среда, с отделен seeded генератор. Изборът се прави от списък със
свободните клетки, а не от всички, за да остане равномерен, без да се хабят
опити върху препятствия — при плътност 40 % това би било два от всеки пет избора.

Двойката се **отхвърля и се избира наново, докато двете точки са по-близо от
0.5 × диагонала на решетката**, с ограничение от 10 000 опита. Без това правило
повечето маршрути биха били къси при всеки размер на решетката и размерът на
решетката би се променял заедно с дължината на маршрута — което би направило
невъзможно отделянето на влиянието на размера на пространството за търсене.
Правилото има и собствено измеримо следствие; виж уговорка 1.

Ако средата е прекалено фрагментирана, за да даде допустима двойка, програмата
**съобщава това, вместо мълчаливо да занижи правилото**, и трите реда за това
изпълнение се записват с непокътнати данни за средата и празни полета за всичко
след нея.

Свързаността умишлено **не** се проверява. Дали съществува маршрут е резултат, а
не предварително условие.

## 4. Схема на началните числа — едно цяло число възпроизвежда всичко

```
mapSeed      = H(главно, широчина, височина, плътност, изпълнение)
endpointSeed = H(главно, широчина, височина, плътност, изпълнение, 0x5EED)
```

`H` е завършващата стъпка на splitmix64, изписана в изходния код, за да може да
бъде цитирана и реализирана наново:

```
z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9
z = (z ^ (z >> 27)) * 0x94D049BB133111EB
H = z ^ (z >> 31)
```

Полетата се вплитат едно по едно, като всяко събиране е отместено с нечетната
константа 0x9E3779B97F4A7C15, така че извеждането зависи от реда и промяна на
един бит в което и да е поле дава напълно несвързано начално число. Плътността
участва като своя **побитов запис по IEEE 754**, така че 33.0 % и 33.3 % не могат
да се преплетат.

Самият поток от случайни числа също е splitmix64, а не стандартният генератор на
езика: Microsoft не гарантира алгоритъма на `System.Random` между основните
версии на .NET, а твърдението за възпроизводимост на изследването не бива да
зависи от чуждо обещание за съвместимост. Целите числа в даден интервал се
избират чрез отхвърляне, така че няма изместване от остатъка. Вградените хеш
функции на езика (`GetHashCode`, `HashCode.Combine`) **не** се използват никъде
при извеждането: те са рандомизирани за всеки процес, така че едно и също
начално число би давало различни среди при всяко стартиране.

**Проверено между процеси, а не прието наум:** две отделни изпълнения с едно и
също главно начално число дадоха еднакви mapSeed, endpointSeed, начална точка,
крайна точка, разширени възли, генерирани възли, максимален размер на отвореното
множество, цена на маршрута и заделена памет. Различаваха се само измерените
времена. Различно главно начално число даде различна среда.

Затова няма нужда генерираните среди да се прилагат отделно. Един ред от
`runs.csv` съдържа `master_seed`, `map_seed`, `pair_seed`, `grid_size`,
`obstacle_density` и `run` — достатъчно, за да бъде възстановена точно средата,
която описва.

## 5. Модели на движение и евристики

**Четири направления:** N, E, S, W; всяка стъпка струва 1.

**Осем направления:** четирите ортогонални стъпки струват 1, четирите диагонални
струват √2.

**Диагоналното движение е без ограничения.** Единствената проверка за
допустимост на стъпка е „целевата клетка е в границите и е свободна“. Затова са
позволени два случая: „срязване“ на ъгъла на едно препятствие и преминаване през
точката с нулева широчина, където две препятствия се докосват с ъглите си. Така
моделът е **точно 8-свързаност (обкръжение на Moore)** — виж уговорка 6 защо
това е важно и какво струва.

Допустимост на евристиките, така както изследването се опира на нея:

| Евристика | четири направления | осем направления |
|---|---|---|
| Manhattan `|dx| + |dy|` | **точна**, допустима и консистентна | недопустима — оценява диагоналната стъпка като 2 вместо √2; не се използва там |
| Octile `(dx + dy) + (√2 − 2)·min(dx, dy)` | допустима, но слаба | **точна**, допустима и консистентна |
| Euclidean `sqrt(dx² + dy²)` | допустима, слаба долна граница | допустима и консистентна, по-слаба от octile |
| Zero (h ≡ 0) | тривиално допустима — това е Dijkstra | тривиално допустима |

Euclidean е умишлено запазена и при четири направления, въпреки че там е слаба
граница: контрастът между точна и слаба евристика е част от това, което
изследването измерва.

И четирите евристики се извикват през една и съща делегатна индирекция,
включително Zero, така че нито една не получава предимство при измерването от
начина, по който се извиква.

## 6. Самото търсене

Двата алгоритъма споделят една и съща структура, така че отчитането им е
сравнимо колона по колона.

**Отворено множество:** `SortedSet` от наредени четворки `(f, h, x, y)`.
Подразбиращият се компаратор на четворка сравнява членовете по ред, което
изразява правилото за разрешаване на равенства **напълно и изрично**: нарастващо
`f`, след това по-малко `h` (клетката, по-близка до целта), след това по-малко
`x`, след това по-малко `y`. Приоритетна опашка върху двоична пирамида беше
отхвърлена именно заради това: тя няма втори ключ и не гарантира устойчивост,
така че равенствата след `f` биха се решавали от вътрешен ред на пресяване —
възпроизводим при фиксирана последователност на вмъкване, но не *описуем*, и
подлежащ на промяна при обновяване на средата за изпълнение без никаква промяна
в нашия код. Уговорка 2 обяснява защо това е важно.

**Проверката за цел е при изваждане, а не при генериране.** Само за изваден
възел се знае, че цената му е окончателна.

**Dijkstra спира веднага щом целта бъде извадена**, точно както прави и A*.
Написан е като отделен клас, а не като A* с h ≡ 0, за да се чете базовият
алгоритъм като Dijkstra; проверката за оптималност е защитата срещу разминаване
между двата цикъла. Пълно поле на разстоянията до всяка клетка би измервало друга
задача и би преувеличило несправедливо предимството на A*.

**Вмъкването е само добавящо.** Когато цената на една клетка се подобри,
подобреният елемент се добавя, а заместеният остава в отвореното множество. Това
не е проблем за коректността: всяка използвана тук евристика е консистентна, така
че цената на една клетка вече е оптимална при първото ѝ изваждане, а остарелият
елемент винаги носи по-голямо `f` и затова идва по-късно, без да намери какво да
подобри. Има две следствия, които трябва да бъдат заявени — натрупването на
мъртви елементи променя смисъла на `peak_open_set` (раздел 7) и е причината
затвореното множество да е необходимо (уговорка 7).

**Затворено множество:** плосък масив от булеви стойности, проверяван при
изваждане. Клетка, която вече е разширена, се отхвърля, без да бъде преброена,
така че „разширена“ означава веднъж на клетка по самата конструкция. Същият масив
служи и като множеството от обходени клетки, което фигурите изчертават — затова
записването му не струва нищо на измерването.

## 7. Какво означава всяка записвана величина

| Величина | Определение |
|---|---|
| `expanded_nodes` | Клетки, **извадени от отвореното множество и обработени**. Броят се по един път за клетка, което се гарантира от затвореното множество. Това е основният показател на изследването. |
| `generated_nodes` | **Елементи, добавени** в отвореното множество. Винаги повече от `expanded_nodes`. |
| `peak_open_set` | Най-големият размер, до който отвореното множество е достигало. **Тъй като вмъкването е само добавящо и заместените елементи никога не се премахват, това отчита НАТРУПАНИ ЕЛЕМЕНТИ — а не размера на фронта на търсене.** Не бива да се чете като размер на фронта или като граница за паметта, която фронтът заема. |
| `path_cost` | Сума от цените на стъпките по намерения маршрут: 1 по ортогонала, √2 по диагонала. |
| `path_length_cells` | Брой клетки в маршрута, включително началната и крайната. |
| `success` | Дали изобщо е намерен маршрут. |
| `execution_time_ms` | Продължителността на извикването на `Search` — виж раздел 9. |
| `allocated_bytes` | Байтове, заделени по време на търсенето — виж раздел 9. |
| `straight_line_distance`, `manhattan_distance` | Разстояние между двете точки, за нормиране на цената на маршрута спрямо трудността на задачата. |
| `optimal_cost_match`, `cost_deviation` | Проверката за оптималност — виж раздел 10. |

Разликата между разширени, генерирани и „посетени“ не е козметична;
„разширени възли“ е двусмислен термин в литературата, затова се записват и двата
брояча, с еднакво отчитане при двата алгоритъма.

## 8. Правилото за празните полета в `runs.csv`

**Едно поле е празно само когато величината не е била измерена. Никога `0`,
никога `NaN`.**

- Търсене, което е било изпълнено и не е намерило маршрут, отчита истински
  `expanded_nodes`, `generated_nodes`, `peak_open_set`, `execution_time_ms` и
  `allocated_bytes`, а `path_cost` и `cost_deviation` са празни. Тези търсения са
  свършили истинска работа и точно тази работа е измерването.
- Изпълнение, за което **не е могла да бъде избрана двойка точки**, е празно от
  `start_x` нататък: нищо след средата не се е случило.

Празното поле е това, което pandas и R разчитат като NA. `0` и в двата случая би
било изфабрикувано измерване — недостижима цел не е маршрут с нулева цена, а
търсене, което никога не е било изпълнено, не е разширило нула възела.

## 9. Как се измерват времето и паметта

- **`System.Diagnostics.Stopwatch`**, с висока разделителна способност, обхващащ
  **извикването на `Search` и нищо друго**. Генерирането на средата, избирането
  на точките, изчертаването на фигурите и записът в CSV са извън измерваната
  област. В програмата има точно едно определение на тази област и както
  експерименталната част, така и демонстрацията с една среда извикват него.
- **Паметта** е разликата в `GC.GetTotalAllocatedBytes(precise: true)` около
  търсенето — детерминирано заделяне, а не шумен работен набор. Двете отчитания
  са извън измерваното време, защото `precise: true` не е безплатно, а
  хронометърът се създава преди първото отчитане, за да не се начисли собственото
  му заделяне на търсенето.
- **Преди всяко от 90-те търсения се извиква пълно блокиращо събиране на
  неизползваната памет**, извън измерваната област. Работните масиви на едно
  търсене надхвърлят прага от 85 KB за големи обекти (при 500×500: 2.0 MB +
  1.0 MB + 0.25 MB), а заделянето на големи обекти е точно това, което
  предизвиква събиране от второ поколение. Пауза, попаднала в измервано търсене,
  би изместила алгоритмите **неравномерно**, тъй като те заделят памет с различна
  скорост. Изчистването преди всяко от 90-те търсения, а не само преди всяка от
  30-те среди, не позволява третият алгоритъм да наследи отпадъка от първите два.
- **Загряващо изпълнение от 32 повторения на всеки алгоритъм** се прави върху
  еднократна среда 50×50 при плътността на конфигурацията, преди да бъде измерено
  каквото и да е, а резултатите му се отхвърлят. Средата се извежда при индекс на
  изпълнение 0, който измерваните изпълнения 1–30 никога не използват, така че
  загряването остава в описаната схема на началните числа, без никога да е една
  от измерваните среди.
- **Многонивовата компилация е изключена в проектния файл**, безусловно.
- **Само построяване в Release.**

Последните три мерки са задължителни и нито една не е достатъчна сама.
Уговорка 8 е измерването, което установява това.

## 10. Проверка за оптималност

В рамките на всяка група `(среда, начало, цел, модел на движение)` цените на
маршрутите и на трите алгоритъма се сравняват с базовия Dijkstra при
**относителна толерантност 1e-9**, записва се на всеки ред като
`optimal_cost_match` и `cost_deviation`, и се обобщава за цялото изпълнение като
едно „премина/не премина“ плюс най-голямото наблюдавано отклонение.

Точното `==` би било грешната проверка: при осем направления два маршрута с
еднаква цена натрупват `g` чрез събиране на √2 и 1 в различен ред, така че
сумите им се различават в последните няколко бита. Най-голямото наблюдавано
отклонение между Dijkstra и A* върху една и съща среда е 1.34e-16 — петнадесет
порядъка вътре в толерантността, но не нула.

Когато един алгоритъм намери маршрут, а друг не, проверката сравнява
съществуването вместо цената. Всяко несъответствие се съобщава и води до
ненулев код на изход.

## 11. Фигури

**Едно съставно изображение от три панела за всяко изпълнение — 30 за
конфигурация.** И трите алгоритъма са един до друг върху една и съща среда с
едни и същи начална и крайна точка, така че сравнението е основният изглед, а не
нещо, което читателят трябва да сглоби, прелиствайки файлове.

Редът на панелите е фиксиран: **Dijkstra** (базов) отляво, основната евристика на
модела в средата (Manhattan при четири направления, Octile при осем),
**Euclidean** отдясно. Всеки панел е ограден, с надпис под него, който съдържа
шест фиксирани реда:

```
DIJKSTRA                          A* OCTILE
GRID: 500 X 500 8-DIR             GRID: 500 X 500 8-DIR
EXPANDED: 175626  PATH: 337 CELLS EXPANDED: 14335  PATH: 337 CELLS
OBSTACLES: 20.0 %                 OBSTACLES: 20.0 %
TIME: 104.22 MS                   TIME: 8.71 MS
MEMORY: 9.84 MB                   MEMORY: 1.12 MB
```

Процентът на препятствията се преброява от самата изчертана маска, а не се взема
от заявената плътност, така че надписът не може да се разминава със собствената
си картина.

Легенда на цветовете: свободна клетка = бяло, препятствие = почти черно,
**обходена клетка = светлосиньо**, маршрут = червено, начало = зелено,
цел = оранжево. Тъй като обходените клетки се изчертават, всеки панел показва
едновременно генерираната среда, препятствията, двете точки, намерения маршрут и
пространството на търсене.

Мащаб, който може да бъде заменен с `--scale N`:

| Решетка | пиксела на клетка | Панел | Съставно изображение |
|---|---|---|---|
| 50×50 | 10 | 500×500 | ~1520×560 |
| 100×100 | 6 | 600×600 | ~1820×660 |
| 250×250 | 3 | 750×750 | ~2270×810 |
| 500×500 | 2 | 1000×1000 | ~3020×1060 |

Файловете се записват като `run01.png` … `run30.png`, с водеща нула, за да се
подреждат по реда на изпълненията, в собствената директория на конфигурацията —
`size<N>_density<D>_<M>dir/` — заедно с двойката CSV файлове на същата
конфигурация. Така директорията на една конфигурация е самостойна: данните и
картините на едни и същи 30 изпълнения, и нищо друго.

**Фигурите се изчертават от самото измерено изпълнение, а не от повторно
изпълнение.** Множеството от обходени клетки, което се изчертава, *е* масивът на
затвореното множество, който търсенето поддържа за собствената си проверка,
затова записването му не е струвало нищо на измерването; заделяне има само при
превръщането му в списък от координати, а това става след спирането на
хронометъра. Съставното изображение се кодира, след като всички търсения на това
изпълнение са измерени, а първото търсене на следващото изпълнение и без това е
предшествано от пълно блокиращо събиране на паметта — така че изчертаването не
може да попадне в измерване. При 500×500, най-тежкия случай, цяла конфигурация
заедно с всичките 30 съставни изображения отнема около 3 секунди.

## 12. Файловете с данните

`runs.csv` е данните на изследването: **разделител запетая, десетичен знак точка,
всяко числово поле записано с инвариантната култура**, по един ред на изпълнение,
като заглавният ред се записва само веднъж.

Файлът съществува на две нива, с еднакви колони:

- **За всяка конфигурация**, в собствената ѝ директория: нейните 90 реда, до
  30-те фигури на същите изпълнения. Това е записът. Повторното изпълнение на
  конфигурацията го пренаписва, така че редовете и картините винаги описват едни
  и същи 30 изпълнения.
- **В корена на резултатите**, обхващащ всичко на диска: 3600 реда при пълната
  матрица. `grid_size` и `obstacle_density` са колони, затова именно този файл
  се зарежда за статистическа обработка. Той е **производен** — сглобява се
  наново от файловете на отделните конфигурации след всяко изпълнение, така че
  винаги покрива точно наличното и не може да се разминава. Съставна част с
  различен заглавен ред е грешка, а не нещо, което се пропуска мълчаливо, тъй
  като това би занижило набора от данни.

25 колони, в следния ред:

```
master_seed, grid_size, obstacle_density, movement_model, algorithm, heuristic,
run, map_seed, pair_seed, start_x, start_y, goal_x, goal_y,
straight_line_distance, manhattan_distance, path_cost, path_length_cells,
expanded_nodes, generated_nodes, peak_open_set, execution_time_ms,
allocated_bytes, success, optimal_cost_match, cost_deviation
```

Уговорки, които е добре да се знаят преди разчитането:

- `grid_size` е дължината на страната като просто цяло число; решетките са
  квадратни.
- `obstacle_density` е частта, `0.00`–`0.40`.
- `path_cost` е записана със 17 значещи цифри, така че равенството на цените може
  да бъде проверено наново от файла при толерантност 1e-9.
- Булевите стойности са `TRUE` / `FALSE` с главни букви — единственият запис,
  който Excel, `read.csv` на R и pandas разчитат като булева стойност, а не като
  текст.
- Началните числа са `0x` плюс 16 шестнадесетични цифри с главни букви.
  Префиксът не е украса: чисти шестнадесетични цифри като `0000000000001E50` се
  разчитат от електронна таблица като 1E+50 и стойността се губи безшумно.
- `master_seed` не е една от дванадесетте задължителни колони; тя е първа в реда,
  защото файл, обхващащ конфигурации, изпълнени по различно време, не може да
  предполага, че всички те са използвали едно и също главно начално число.
- Празно означава „не е измерено“ — виж раздел 8.

`runs_excel.csv` до него е **производно копие само за преглед**: разделител
точка и запетая, десетичен знак запетая, за отваряне с двойно щракване при
локал, чийто списъчен разделител е точка и запетая, а десетичният знак е запетая.
Пренаписва се изцяло от `runs.csv` след всяко изпълнение, така че двата файла не
могат да се разминат, и от него никога не се чете нищо обратно. Десетичният знак
се пренаписва само в числовите колони, което е причината началните числа `0x…` да
останат текст. **Каноничният файл е `runs.csv`** — разделител запетая с
десетична точка е това, което очакват статистическите инструменти, и той не се
променя, за да угоди на електронна таблица.

`environment.md` се генерира автоматично при всяко изпълнение и записва езика,
средата за изпълнение, конфигурацията на построяване, операционната система,
процесора, ядрата, паметта, действителната разделителна способност на
хронометъра и настройките на средата, които влияят върху времената.

## 13. Уговорки при разчитането на резултатите

Това не са дефекти. Всяка от тях променя начина, по който трябва да се чете
дадено число в `runs.csv`, а някои са в противоречие с интуицията.

### Уговорка 1 — При плътност 40 % моделът с четири направления е на перколационния праг

За 4-свързана квадратна решетка маршрут съществува само докато блокираната част
е под приблизително **40.7 %**. При заложената в постановката плътност от 40 % се
намираме на по-малко от половин процентен пункт от тази граница, така че
значителна част от двойките начало-цел наистина **няма** да имат маршрут — особено
по-отдалечените. Моделът с осем направления не е засегнат; неговият праг е
приблизително 59.3 % блокирани клетки.

Измерено при 50×50 / 40 % / четири направления, 30 изпълнения: маршрут
съществуваше само при **11 от 30** изпълнения. И при трите алгоритъма
заключението, че маршрут няма, съвпадна при всяко едно от 19-те неуспешни
изпълнения, **като разшириха точно еднакъв брой клетки при всяко** — търсене,
което не намира маршрут, залива цялата достижима компонента, а никоя евристика не
променя кои клетки са това. Евристиката променя само *реда* на заливането. Затова
броят на разширените възли е еднакъв, когато отговорът е „няма маршрут“, и се
различава силно, когато маршрут има. Размерът на компонентата се движеше от
1 клетка, когато началото е запечатано от четирите си съседа, до 1138 клетки.

Правилото за точките от раздел 3 **допринася за този дял по замисъл**: двойките
трябва да са на поне половин диагонал една от друга, а точно отдалечените двойки
са тези, които се провалят близо до прага. Затова делът на неуспехите е свойство
на заявената експериментална постановка, а не само на средата, и двете трябва да
се цитират заедно.

Плътността 40 % е запазена умишлено. Спадът в дела на успешните търсения сам по
себе си е резултат — но е очаквано поведение на средата, а не ограничение на
програмата.

### Уговорка 2 — Където евристиката е точна, правилото за равенства определя броя на разширените възли

В среда без препятствия основната евристика е точна: Manhattan при четири
направления, octile при осем. Тогава всяка клетка от всеки оптимален маршрут има
една и съща стойност `f = g + h`. В празна решетка съществуват експоненциално
много оптимални маршрути с еднаква цена, така че `f` не различава нищо и броят на
разширените възли се определя **изцяло от правилото за разрешаване на равенства
по `f`** — от O(дължина на маршрута) до O(площ на правоъгълника).

Тъй като `expanded_nodes` е основният показател на изследването, се прилага едно
детерминирано правило, еднакво при двата алгоритъма и описано в раздел 6:
нарастващо `(f, h, x, y)`. В празна решетка 10×10 от ъгъл до ъгъл това разширява
10 клетки при осем направления с octile и 19 при четири направления с Manhattan —
и в двата случая само клетките на самия маршрут, възможно най-добрият резултат.
Различно правило за равенства върху същата решетка би могло да разшири целия
правоъгълник.

### Уговорка 3 — „Разширени възли“ изисква фиксирано определение

Разширени (извадени и обработени) ≠ генерирани (добавени) ≠ посетени. Записват се
и двата брояча, с еднакво отчитане при двата алгоритъма, а проверката на
затвореното множество не позволява повторните изваждания да бъдат преброени
двойно. Раздел 7 е определението, което важи навсякъде.

### Уговорка 4 — Равенството на цените трябва да се проверява с толерантност, а не с `==`

Виж раздел 10. При осем направления √2 се натрупва различно в зависимост от реда,
в който се сглобяват стъпките на маршрута, така че точното равенство е грешната
проверка дори между два доказуемо оптимални маршрута.

### Уговорка 5 — Изчислителният ресурс не е ограничение

Най-тежкият случай — Dijkstra при 500×500 и 0 % плътност, който залива на
практика всичките 250 000 клетки — отнема от порядъка на сто милисекунди. Не се
наложи да бъде изпуснат нито един размер, а 500×500 заедно с всичките 30 фигури
завършва за около 3 секунди.

### Уговорка 6 — Изчертаният маршрут може да изглежда като пресичащ диагонална верига от препятствия

Това е пряко следствие от правилото за диагоналите без ограничения от раздел 5.
Където две препятствия се докосват с ъглите си, противоположната диагонална
стъпка през същата тази точка е допустима и струва √2 — тоест **диагоналната
стена не е стена**. На фигурите червеният маршрут понякога ще изглежда като
минаващ право през черна диагонална линия. **Това е коректно поведение при
8-свързаност, а не дефект на изчертаването или на търсенето.** Мястото на това
изречение е във всеки надпис под фигура, а тук то е заявено, защото читател, на
когото не е казано, ще го съобщи като дефект.

Заслужава да се каже и положително: диагоналите без ограничения са точно това,
което прави модела обкръжение на Moore, което е и причината прагът от 59.3 % в
уговорка 1 да е валиден като публикувана константа и octile да е *точната*
остатъчна цена в среда без препятствия, което запазва анализа в уговорка 2 чист.

Цената на този избор е, че `success` измерва **свързаност по Moore**, а не
свързаността, която вижда агент с ненулев физически размер.

### Уговорка 7 — Octile е консистентна в точна аритметика, но само до закръгляването в `double`, и това изкривява основния показател

Открито чрез измерване, а не чрез разсъждение.

Консистентната евристика гарантира, че цената на една клетка е окончателна при
първото ѝ изваждане и че тя никога не се разширява повторно. При двойна точност по
IEEE 754 тази гаранция е в сила само до около 1e-15. Два маршрута с еднаква цена
натрупват `g` чрез събиране на √2 и 1 в различен ред, така че сумите им се
различават в последните няколко бита — и реализация без затворено множество
разчита тази разлика като истинско подобрение.

Измерено върху 40 генерирани решетки 30×30 при плътност 30 %, осем направления:
**148 повторни разширявания на клетки, които вече са били приключени, нито едно от
които не откри нова клетка**, при най-голямо привидно подобрение 3.553e-15 — шум
от закръгляване, а не по-добър маршрут. Две следствия:

- Изборът между няколко еднакво оптимални маршрута се определяше от грешка при
  закръгляване.
- `expanded_nodes` се различаваше при 4 от 40-те решетки, в интервала от **−9 до
  +9** клетки — и в **двете** посоки, защото шумът променя `f`, а `f` определя
  реда на изваждане. Следователно това не е „допълнителна работа“, която може да
  се ограничи отгоре.

**Затвореното множество е това, което го премахва. Затова то е носещо за
възпроизводимостта на `expanded_nodes`, а не просто оптимизация.** Изпълнението
на едно и същото търсене със и без тази проверка, без никаква друга промяна,
води до пълно съвпадение между двете клетка по клетка — което изолира
затвореното множество като единствената причина.

За разчитането на данните: при осем направления `expanded_nodes` е чувствителен
към реда на натрупване при изчисленията с плаваща запетая и **правилото за
равенства само по себе си не го определя напълно** — затвореното множество е
това, което го прави възпроизводим. Това е и най-силното основание за
толерантността в уговорка 4: шумът от плаващата запетая не влияе само на
*сравненията на цени между алгоритмите*, а на *самото търсене*.

### Уговорка 8 — Измерването на времето е замърсено от JIT компилацията по два отделни начина и вторият е по-лошият

C# се компилира до междинен байт-код при построяването; средата за изпълнение
компилира всеки метод до машинен код при първото му извикване. Напълно измерено
чрез сравнение 2×2 — със и без загряване × с и без многонивова компилация — при
50×50 / 0 % / осем направления, медиани по изпълнения 2–30:

| | Dijkstra изп. 1 | Dijkstra медиана | измерено ускорение на A* octile |
|---|---|---|---|
| загряване + без нива (**използвано**) | 0.355 | 0.347 | **13.4×** |
| без загряване + без нива | 7.901 | 0.355 | 13.8× |
| без загряване + с нива | 13.653 | 0.408 | 3.4× |
| загряване + с нива | 0.401 | 0.374 | 3.2× |

*(всички времена в милисекунди)*

**Предвидената стъпаловидна промяна не се появи.** .NET прехвърля метод към
напълно оптимизиран код след около 30 извиквания, а експериментът прави точно 30
изпълнения за конфигурация, така че този праг би трябвало да попадне вътре в
измерваното множество. Не попадна: всички редици са равни от второто изпълнение
нататък, защото цикълът на търсенето е дълъг и заместването в хода на изпълнението
го прехвърля още по време на първото изпълнение, а не при тридесетото.

Появиха се две други изкривявания и двете са по-лоши от стъпаловидна промяна,
защото стъпаловидната промяна поне се вижда:

1. **Компилацията при първото извикване е голяма.** Без загряващо изпълнение
   първото изпълнение отне на Dijkstra 7.901 ms срещу медиана 0.347 ms (23 пъти),
   а на A* octile 1.471 ms срещу 0.026 ms (51 пъти) — което завишава средната
   стойност на конфигурацията съответно 1.7 и 2.8 пъти. Загряващото изпълнение го
   премахва напълно.
2. **Многонивовата компилация завишава най-много *бързите* алгоритми и загряване
   от 32 изпълнения не го поправя.** Медианно време с включени нива спрямо същото
   измерване с изключени: Dijkstra 1.08 пъти, A* Euclidean 2.57 пъти, A* octile
   **4.47 пъти**. Колкото по-евтино е търсенето, толкова по-голяма част от него
   минава в неоптимизиран или инструментиран код, докато дългото заливане на
   Dijkstra се прехвърля в хода на изпълнението и се измъква. **Измереното
   предимство на A* спрямо Dijkstra спада от 13.4 на 3.2 пъти.**

Това второ изкривяване работи *срещу* собствената хипотеза на изследването — ако
многонивовата компилация беше оставена включена, ползата от A* щеше да бъде
занижена четири пъти — и е невидимо в данните, защото тези времена са равни,
правдоподобни и напълно възпроизводими. Просто измерват друга програма.

Затова и двете мерки са необходими, по различни причини: загряването премахва
скока при първото извикване, а изключването на многонивовата компилация
възстановява нивото.

**Обхват:** измерено при 50×50, където едно търсене отнема от десетки
микросекунди до една трета от милисекундата. *Посоката* на изкривяването следва
от това кой код средата е оптимизирала и е в сила при всеки размер, но
**стойността 4.47 пъти е специфична за тази конфигурация и не бива да се цитира
като обща константа.**

## 14. Какво изследването не твърди

- Абсолютните времена са свойство на тази машина, тази среда за изпълнение и
  тези структури от данни, а не на алгоритмите. `expanded_nodes` е преносимият
  показател; `execution_time_ms` е този, който се нуждаеше от петте мерки в
  раздел 9, преди да означава нещо изобщо.
- Отвореното множество е балансирано двоично дърво, избрано така, че правилото за
  равенства да е *описуемо*, а не само възпроизводимо. Пирамида върху масив би
  била може би 2–4 пъти по-бърза в абсолютни стойности. Това се отнася и до трите
  алгоритъма в една и същата посока.
- `success` измерва свързаност по Moore, а не свързаността, която би видял агент
  с физически размер (уговорка 6).
- Препятствията са равномерно случайни, което е описаният принцип на генериране.
  Структурирани среди — помещения, коридори, лабиринти — са друг въпрос и не се
  разглеждат тук.
