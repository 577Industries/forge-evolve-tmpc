# CLAR Language Profile - C# (.NET)

> Part of **FORGE EVOLVE for TMPC**. Defines how a C# source module maps into the four
> CLAR layers of [`clar-spec/CLAR.schema.json`](../CLAR.schema.json). C# is the **primary
> TMPC focus language** (the real MDS is ~1.3M LOC, mostly C#). The reference lift is
> implemented in `src/ForgeEvolve.Clar/ClarLifter.cs` against the synthetic surrogate
> `surrogate/tmpc-surrogate-mds/legacy/MissionProcessor.cs`.

`sourceLanguage`: **`CSharp`**

## Layer mapping

| C# construct | CLAR layer | Node `type` |
|---|---|---|
| Method body / pipeline of stages | controlFlow | `PIPELINE` |
| `if` / `else` | controlFlow | `BRANCH` |
| `switch` | controlFlow | `SWITCH` |
| `for` / `foreach` | controlFlow | `FOR_LOOP` |
| `while` | controlFlow | `WHILE_LOOP` |
| `do { } while` | controlFlow | `DO_UNTIL` |
| `try` / `catch` | controlFlow | `TRY_CATCH` |
| `async`/`await` fan-out, `Parallel.*` | controlFlow | `PARALLEL` / `COROUTINE` |
| event handler / delegate | controlFlow | `EVENT_HANDLER` |
| method parameter | dataFlow | `PARAMETER` |
| local variable | dataFlow | `VARIABLE` |
| `const` / `static readonly` literal | dataFlow | `CONSTANT` |
| `return` value | dataFlow | `RETURN_VALUE` |
| arithmetic expression | dataFlow | `ARITHMETIC` |
| comparison / boolean test | dataFlow | `COMPARISON` |
| explicit/implicit cast (`(long)x`) | dataFlow | `CAST` |
| LINQ `Sum`/`Aggregate`, running total | dataFlow | `AGGREGATE` |
| `List<T>` / array | dataFlow | `COLLECTION` |
| `record` / `struct` / `class` field set | dataFlow | `RECORD` |
| `DateTime`/`DateTimeOffset`/epoch math | dataFlow | `DATE_TIME` |
| validation/calculation/constraint logic | businessLogic | `VALIDATION` / `CALCULATION` / `CONSTRAINT` / `INVARIANT` / `ROUTING` / `CLASSIFICATION` |
| ADO.NET / EF / Dapper query | infrastructure | `DB_QUERY` |
| `File.*` / `Stream` I/O | infrastructure | `FILE_IO` |
| `HttpClient` / gRPC call | infrastructure | `API_CALL` |
| `IConfiguration` / static config singleton | infrastructure | `CONFIGURATION` |
| `ILogger` / message accumulation | infrastructure | `LOGGING` |

## Precision-constrained mapping (load-bearing)

The dataFlow `precisionConstrained` flag tells the target generator it **MUST** emit
`decimal` / `System.Numerics`-backed / `checked` arithmetic for that value and **MUST NOT**
coerce through `double`/`float`. This is what repairs the surrogate's **D1** (anti-meridian
longitude wrap) and **D2** (per-leg rounding / accumulation drift) defect classes.

| C# source type / use | CLAR `clarType` | `precisionConstrained` | Target generator emits |
|---|---|---|---|
| `double` / `float` used for **lat/lon coordinates** | `PrecisionConstrained` | `true` | `decimal` (or fixed-point), `checked` |
| `double` used for **distance** (leg / total nm) | `PrecisionConstrained` | `true` | `decimal`, exact summation (no pre-round) |
| `double`→`long` **travel-time / TOT** cast | `PrecisionConstrained` | `true` | `decimal` math then explicit `Math.Round` (not truncation) + `checked` cast |
| `double`/`float` general scientific scratch (non-coordinate) | `FloatingPoint` | `false` | `double` is acceptable |
| `int` / `long` counters, sequence numbers | `Integer` | `false` | native integer |
| `bool` categorical decision | `Boolean` | `false` | `bool` |
| `string` codes / messages | `Text` | `false` | `string` |

**Rule of thumb:** any `double`/`float` whose value is a *coordinate, a distance, a time
offset, money, or anything later persisted to a SQL `FLOAT` column* is precision-constrained.
See the SQL and VB6 profiles for the cross-language analogs that share this flag.

## Worked example (surrogate `ProcessMission`)

- `latDeg`, `lonDeg` (`double` parameters) → dataFlow `PARAMETER`, `PrecisionConstrained`.
- `legDistanceNm` (`double` arithmetic) → dataFlow `ARITHMETIC`, `PrecisionConstrained`.
- `totalDistanceNm` (running sum) → dataFlow `AGGREGATE`, `PrecisionConstrained`.
- `travelSec` (`(long)(total/0.15)`) → dataFlow `CAST`, `PrecisionConstrained`.
- `estimatedTotEpochSec` (epoch math) → dataFlow `DATE_TIME`, `PrecisionConstrained`.
- `routeValid`, `taskingGoNoGo` (categorical `bool`) → dataFlow `RETURN_VALUE`, **not** constrained.
- inline `SqlConnection` publish → infrastructure `DB_QUERY` (`dbo.Missions` / `dbo.Waypoints`).
- `LegacyConfig` static mutable singleton → infrastructure `CONFIGURATION`.
