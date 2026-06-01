# Orchestrator transcripts (offline replay cache)

The Offline orchestrator (`ToolOrchestrator` in `OrchestratorMode.Offline`) replays a recorded
`TransformResult` for each task. It NEVER calls a model and NEVER fabricates output — a missing
transcript is a hard error.

## How a transcript is resolved

1. Compute the **deterministic key**:

   ```
   key = lowercase_hex( SHA-256( "{Unit.Id}|{SourceLanguage}|{TargetStack}" ) )
   ```

   `SourceLanguage` is the enum **name** (e.g. `CSharp`), matching the contract's
   `JsonStringEnumConverter`. Reproduce it from a shell with:

   ```bash
   printf '%s' "MyUnit.Id|CSharp|dotnet8" | sha256sum
   ```

   or in C# via `ForgeEvolve.Orchestrator.TranscriptKey.For(task)`.

2. Look the key up in [`index.json`](index.json) (a `key → filename` map).
3. Load that file and deserialize it as a `TransformResult`.

The store reads the filesystem copy first (so transcripts can be dropped in without a rebuild) and
falls back to the copies embedded in the assembly (so replay works from any working directory).

## Adding a transcript

1. Author/record the `TransformResult` JSON (System.Text.Json web defaults) as
   `fixtures/transcripts/<name>.json`.
2. Compute the key for its `(Unit.Id, SourceLanguage, TargetStack)` triple.
3. Add `"<key>": "<name>.json"` to `index.json`.

## Files

| File | Owner | Notes |
|---|---|---|
| `sample-dummy-unit.json` | this workstream (WS-D) | tiny self-test fixture; **not** a real model run |
| `mission-modernization.json` | **Transformation workstream** | the real surrogate transcript; consume whatever is present — do **not** author it here |

`transcripts/cloud/` is git-ignored: live cloud captures are never committed.
