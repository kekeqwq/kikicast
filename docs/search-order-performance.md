# Search order stability and local benchmark

## Comparator contract

The field-aware matcher/order remains adapted from Tinycast `LauncherMatch.swift` / `LauncherOrder.swift` at `fa1c2bb` (AGPL-3.0). Exact intentional aliases, longer-query exact titles, learned terms, subtitles, alias/learned prefixes, at-most-three-character extensions, alignment, usage and type/natural-title ties remain separate. Keywords admit a candidate but are not title/intentional-alias matches. Saved commands remain name-only.

One deliberate Windows correction: the upstream-style **pairwise boost exception is not transitive**. For query `term`, these candidates cycle:

- boosted `t_e_r_m`, usage 5, beats exact `term`, usage 1;
- exact `term` beats unboosted `terminal`, usage 10;
- unboosted `terminal` beats the boosted weaker alignment.

The new code revokes boost **once for the complete matching pass**, not during each comparison. A higher real-usage, unboosted matching rival revokes a boost within the same exact-alias/ordinary bucket. Exact intentional aliases always precede the other bucket and cannot revoke its boost. Only then is a stable comparator used. Limit is applied after eligibility/ranking; no candidate changes persisted learning. Invalid/non-finite or below-baseline usage is treated as 1. Production currently has no boost-term source or placeholder switch.

Tests cover the failing three-way cycle over all six permutations, exact-alias isolation, an 80-item mixed-boost catalogue across 40 permutations and limited prefixes, pair antisymmetry/all triple transitivity on three queries with 64 mixed field/learning candidates, numeric/Unicode/malformed-text collation, invalid usage and warmed allocation budgets. This is not proof over every possible language or macOS localized collation.

## Hot-path changes

- No candidate-sized LINQ closure/alternate arrays in the ranking pass, including misses.
- Value facts/term hits instead of per-hit objects; index loops avoid interface enumerator allocation.
- Stack-based comparison arguments; numeric collation runs only if prior rules tie.
- Folded collation title is cached lazily on the per-pass candidate, not repeatedly allocated on every comparison. No global title cache or disk state.
- Alignment still uses bounded pooled arrays returned in `finally`; counting query letters does not allocate a string enumerator.

## Opt-in generated benchmark

`tools/Kikicast.Search.Benchmark` is included for compilation, not run by desktop startup or packaging. It references Core only. It generates application/command-like English, Chinese, diacritic and fullwidth titles, cached fields, aliases, learned terms and up to16 registration keywords; never reads actual applications, registry, configuration, clipboard, scripts, network or desktop.

Example from a PowerShell shell (choose an unused isolated output):

```powershell
dotnet build tools/Kikicast.Search.Benchmark/Kikicast.Search.Benchmark.csproj -c Release -p:BaseOutputPath=C:/temp/KikicastBench/ -warnaserror
dotnet C:/temp/KikicastBench/Release/net10.0/Kikicast.Search.Benchmark.dll --count 5000 --samples 15 --label local
```

Count is 1–30000; samples 5–100. Unknown/missing/invalid options fail with exit2 before generating data. The only output is JSON to stdout: process/OS architecture, runtime, profile build/allocation, query/sensitivity median/p95 milliseconds, per-thread allocated bytes and first five result IDs. All returned IDs are checked for same-input repeatability. No wall-clock assertions are placed in unit tests. Runtime tiering/background activity affect timings; setting `DOTNET_TieredCompilation=0` in the benchmark subprocess permits a controlled algorithm comparison, **not production latency acceptance**.

## Current ARM64 observation

Local .NET10.0.12, workstation GC, 5000 generated cached profiles, 15 samples/query, Medium/High and eight queries. For the paired comparison both subprocesses had tiered compilation disabled. Local ignored JSON evidence is under `artifacts/performance/search/`; before/after first-five IDs matched for all16 cases.

Selected Medium observations (bytes are per complete ranking call, not retained memory):

| Query | Before median ms | After median ms | Before bytes | After bytes |
| --- | ---: | ---: | ---: | ---: |
| `t` | 34.44 | 9.76 | 16793016 | 1057752 |
| `term` | 20.69 | 5.83 | 13740104 | 757744 |
| `wei xin` | 12.04 | 2.04 | 5773968 | 273328 |
| `registryalias` | 8.62 | 1.31 | 3805912 | 151416 |
| no match | 6.15 | 1.30 | 2004696 | 1048 |

All16 allocation measurements decreased (roughly94–99.9%). Latency ratios are local observations, not universal promises.

A separate 20000-item run with **default tiering** had Medium short-query medians around26–38ms, p95 up to65ms for those short queries; a Chinese-query p95 outlier was117ms despite a6ms median. These numbers explicitly **do not establish an end-to-end WPF/indexing/key-to-frame SLO**, first-use cost, mixed-DPI/native-x64 behaviour or idle/load/shutdown release budget. Full sorting and synchronous root refresh still need realistic end-to-end profiling; `security-performance` remains pending. No stable release is authorized by this benchmark.
