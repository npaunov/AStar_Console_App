# A* versus Dijkstra — an experimental platform

A reproducible comparison of A* against Dijkstra as a baseline on grid
environments, across four grid sizes, five obstacle densities, two movement
models and three heuristics. Every individual execution is recorded, so the
statistics can be done downstream from raw data rather than from averages.

C# 14 on .NET 10, **no third-party libraries** — the .NET base class library
only, including the PNG encoder that writes the figures.

## Requirements

The .NET 10 SDK. Nothing else — there are no packages to restore.

```
dotnet --version        # 10.0.x
```

The project targets `net10.0`, so it needs Visual Studio 2026 or the `dotnet`
CLI; Visual Studio 2022 cannot target it.

## Running

```
dotnet run -c Release
```

The first prompt asks whether to run **the full matrix — all 40
configurations**. Answer `y` and it runs everything unattended; answer `n` (the
default on a bare Enter) and three prompts follow — grid size, obstacle density,
movement model — for one configuration.

The full matrix is 40 configurations × 30 maps × 3 algorithms = **3,600 measured
executions and 1,200 figures**, and takes **under a minute**. Configurations run
smallest grid first, so a problem surfaces on a 50 × 50 grid in seconds rather
than after the 500 × 500 work.

The movement model fixes the algorithm set:

| Movement model | Algorithms compared |
|---|---|
| 4-directional | Dijkstra, A* Manhattan, A* Euclidean |
| 8-directional (diagonal = √2) | Dijkstra, A* Octile, A* Euclidean |

Each configuration runs **30 independently generated maps × 3 algorithms = 90
measured executions** and writes its own `runs.csv`, `runs_excel.csv` and 30
three-panel composite figures into a directory of its own. At 500×500, the worst
case, that takes about 3 seconds including all the figures.

The prompts are a fixed menu, so a run can be scripted on stdin — the full-matrix
answer, then grid size, density and model if it was `n`:

```
printf 'y\n'            | dotnet run -c Release   # everything
printf 'n\n1\n3\n2\n'   | dotnet run -c Release   # 50x50, 20 %, 8-directional
```

**`-c Release` is not optional for anything you intend to measure.** A Debug
build runs roughly twice as slow, and its `execution_time_ms` column is
meaningless. `environment.md` records the build configuration it ran from, so a
Debug-timed data set is identifiable after the fact.

## Reproducing a published number

Every environment is derived from a single master seed, so nothing has to be
shipped alongside the data. The default is `20260915`:

```
dotnet run -c Release -- --seed 20260915
```

Re-running a configuration at the same master seed reproduces every map, every
start/goal pair and every count bit for bit — verified across separate
processes. Only the timings move. Any row of `runs.csv` carries its own
`master_seed`, `map_seed`, `pair_seed`, `grid_size`, `obstacle_density` and
`run`, which is enough to rebuild exactly the environment that row describes.

## Output

Everything a run produces goes to **`Results/`**, beside the project file — so
the data sits next to the code that made it, and never in `bin/`. The directory
is created on the first write and resolved in this order:

1. the first argument that does not start with `--`
2. `Results/` beside the `.csproj`, found by walking up from the binary
3. `Results/` in the working directory, for a published build with no project
   file beside it

```
dotnet run -c Release -- C:\somewhere\else
```

**`Results/` is git-ignored**, so no generated data is ever committed: it is all
reproducible from a master seed, and the figures are large. The one exception is
`methodology.md`, which is written by hand rather than generated and is kept
under version control alongside the data it describes.

**Each configuration gets its own self-contained directory**, named for the
configuration it holds — so a folder can be opened, read and sent on without
anything else beside it:

```
Results/
  size50_density0_4dir/      runs.csv, runs_excel.csv, run01.png … run30.png
  size50_density0_8dir/      runs.csv, runs_excel.csv, run01.png … run30.png
  …                          40 directories after a full-matrix run
  runs.csv                   every configuration in one file
  runs_excel.csv             its spreadsheet view
  environment.md             the measurement environment
  methodology.md             how it was all done
  demo/                      throwaway output of --demo
```

| File | What it is |
|---|---|
| `<configuration>/runs.csv` | **The data**, 90 rows for that configuration. 25 columns, one row per execution, comma-delimited, dot decimals, invariant culture. Rewritten on a re-run, so its rows always describe the same 30 runs as the figures beside it. |
| `<configuration>/runs_excel.csv` | Derived, **view-only** copy: semicolon-delimited with comma decimals, for double-clicking on a comma-decimal locale. Never read back. |
| `<configuration>/run01.png …` | 30 three-panel composites: Dijkstra, the model's primary heuristic and Euclidean, side by side on the same map. |
| `runs.csv` at the root | Every configuration found on disk, concatenated into one file with the header once — 3,600 rows after a full matrix. `grid_size` and `obstacle_density` are columns, so this is the file a statistics tool loads directly. **Derived:** rebuilt from the per-configuration files on every run, so it cannot drift. |
| `runs_excel.csv` at the root | The same, as a spreadsheet view. |
| `environment.md` | Machine-generated technical report: language, runtime, build configuration, OS, CPU, cores, RAM, the timing clock's actual resolution, and the runtime settings that affect timings. |
| `methodology.md` | How the experiments are conducted and how the numbers must be read, in English and Bulgarian. Hand-written, and the only file here that is committed. |

## Flags

| Flag | Effect |
|---|---|
| `--seed N`, `--seed=N` | Master seed every environment derives from. An unreadable value is an error, never a fallback. |
| `--no-figures` | Run the experiment without drawing anything. |
| `--scale N` | Override the pixels-per-cell the figure-scale table would pick. |
| `--no-warmup` | Skip the JIT warmup pass, so its effect can be measured. The timings this produces are not measurements. |
| `--excel-only` | Rebuild every spreadsheet view on disk — one per configuration, plus the root pair — and run nothing else. |
| `--environment` | Rewrite `environment.md` and run nothing else. |
| `--demo` | The single-map walkthrough: one environment, three searches, the ASCII grid and one composite figure. A diagnostic, not a measurement — it has no warmup pass by design, so its `TIME` row measures JIT compilation as much as the algorithm. |
| `--selftest` | 38 known-answer checks. No output files. |

Exit codes: `0` ok, `1` the optimality cross-check failed (data was produced and
it is wrong), `2` bad arguments, `3` no endpoint pair could be drawn, `4` stdin
closed before a choice was made, `5` an output file could not be written.

## Self-test

```
dotnet run -c Release -- --selftest
```

Hand-verified cases with no test framework and no dependencies: empty grid
corner to corner, a wall with a single gap, a fully enclosed goal, the diagonal
squeeze (asserted **traversable**, which pins the permissive diagonal rule so it
cannot be "fixed" by accident), seeded-generation reproducibility, and an A/B of
the search with and without the closed-set guard — which is what demonstrates
that the guard is load-bearing for reproducibility rather than a mere
optimisation.

## Layout

```
Core/            Grid, MovementModel, Heuristic, SearchResult, IPathfinder
Algorithms/      AStarPathfinder, DijkstraPathfinder — peer classes
Generation/      SeedScheme (splitmix64), MapGenerator, EndpointSampler
Experiments/     ExperimentMatrix, ExperimentRunner, CsvRecorder,
                 FigureWriter, EnvironmentProbe, RunRecord
Rendering/       PngEncoder, BitmapFont, GridImageRenderer, ConsoleGridRenderer
Program.cs       Console driver: the harness, the demo, argument parsing
SelfTest.cs      Known-answer checks
Results/         Experiment output — git-ignored except methodology.md
```

## Things worth knowing before reading the data

These are documented in full, with their measurements, in `methodology.md`.
Four of them are counter-intuitive enough to be worth repeating here:

- **`peak_open_set` counts accumulated open-set entries, not frontier size.**
  Insertion is add-only, so superseded entries are never removed.
- **A column is empty only when the quantity was not measured** — never `0`,
  never `NaN`. A search that found no route still reports real
  `expanded_nodes`, `execution_time_ms` and `allocated_bytes`, with an empty
  `path_cost`.
- **The closed-set guard is load-bearing for reproducibility**, not merely an
  optimisation. Octile is consistent in exact arithmetic but only to within
  rounding in `double`, and without the guard that noise perturbs
  `expanded_nodes` in both directions.
- **Diagonals are permissive**, so a rendered path can appear to cross a
  diagonal chain of obstacles. That is correct 8-connectivity behaviour, not a
  rendering or search defect.
