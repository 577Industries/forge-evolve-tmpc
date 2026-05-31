# CLAR Language Profile — VB6

> Part of **FORGE EVOLVE for TMPC**. Defines how a Visual Basic 6 source module maps into the
> four CLAR layers of [`clar-spec/CLAR.schema.json`](../CLAR.schema.json). The surrogate's
> representative VB6 artifact is the fixed-point geo module
> `surrogate/tmpc-surrogate-mds/legacy/GeoFixedPoint.bas`. VB6 → TypeScript is one of FORGE
> EVOLVE's already-validated transformation paths; this profile feeds the pre-existing VB6
> front-end.

`sourceLanguage`: **`Vb6`**

## Layer mapping

| VB6 construct | CLAR layer | Node `type` |
|---|---|---|
| `Sub` / `Function` body | controlFlow | `PIPELINE` / `SEQUENCE` |
| `If…Then…Else` | controlFlow | `BRANCH` |
| `Select Case` | controlFlow | `SWITCH` |
| `For…Next` / `For Each` | controlFlow | `FOR_LOOP` |
| `Do While…Loop` | controlFlow | `WHILE_LOOP` |
| `Do…Loop Until` | controlFlow | `DO_UNTIL` |
| `On Error GoTo` handler | controlFlow | `TRY_CATCH` |
| form/control event (`_Click`, `_Change`) | controlFlow | `EVENT_HANDLER` |
| `ByVal` / `ByRef` argument | dataFlow | `PARAMETER` |
| `Dim` local | dataFlow | `VARIABLE` |
| `Const` | dataFlow | `CONSTANT` |
| function return assignment | dataFlow | `RETURN_VALUE` |
| arithmetic expression | dataFlow | `ARITHMETIC` |
| comparison | dataFlow | `COMPARISON` |
| `CLng` / `CDbl` / `CInt` conversion | dataFlow | `CAST` |
| running total in a loop | dataFlow | `AGGREGATE` |
| array / `Collection` | dataFlow | `COLLECTION` |
| `Type … End Type` UDT | dataFlow | `RECORD` |
| `Currency` (scaled-integer money) | dataFlow | `FIXED_DECIMAL` |
| scaled-integer "mil-grid" `Long` | dataFlow | `FIXED_DECIMAL` |
| `Date` / serial-date math | dataFlow | `DATE_TIME` |
| `Mid$` / `&` concatenation | dataFlow | `STRING_OP` |
| validation/constraint logic | businessLogic | `VALIDATION` / `CONSTRAINT` / `CALCULATION` |
| ADO / DAO recordset | infrastructure | `DB_QUERY` |
| `Open`/`Print #`/`Get #` file I/O | infrastructure | `FILE_IO` |
| module-level `Public` global (e.g. `gLastGeoError`) | infrastructure | `CONFIGURATION` |
| `Debug.Print` / log file | infrastructure | `LOGGING` |

## Precision-constrained mapping (load-bearing)

VB6 legacy mission code typically stores coordinates as **scaled-integer fixed-point** ("mils",
e.g. 1° = 10000 grid units in `GeoFixedPoint.bas`) and money as the `Currency` type (a 64-bit
integer scaled by 10,000). Both are *deliberately* fixed-point because float was distrusted —
that intent must be **preserved**, not flattened to `double` by the target generator.

| VB6 source type / use | CLAR `clarType` | `precisionConstrained` | Target generator emits |
|---|---|---|---|
| `Currency` (money / scaled fixed-point) | `PrecisionConstrained` | `true` | `decimal` (exact), `checked` |
| scaled-integer **mil-grid** `Long` lat/lon | `PrecisionConstrained` | `true` | scaled `decimal` / `long` fixed-point + `checked` overflow |
| `Double` used for **coordinate/distance** | `PrecisionConstrained` | `true` | `decimal`, exact summation |
| `CLng`/`CDbl` conversion of a coordinate | `PrecisionConstrained` | `true` | explicit `decimal`↔integer with banker's-rounding intent preserved |
| `Double` general scientific scratch | `FloatingPoint` | `false` | `double` acceptable |
| `Integer` / `Long` counters, error codes | `Integer` | `false` | native integer |
| `Boolean` | `Boolean` | `false` | `bool` |
| `String` | `Text` | `false` | `string` |

**Overflow note:** VB6 `Long` mil-grid arithmetic silently overflows (or raises a trappable
error) near `±MILGRID_WRAP`. Mark these `precisionConstrained` so the target emits **`checked`**
arithmetic and an explicit anti-meridian wrap, fixing the same class of bug as C# **D1**
(`MilGridLonDelta` has the identical half-hearted-wrap defect).

## Worked example (`GeoFixedPoint.bas`)

- `degValue As Double` argument of `DegreesToMilGrid` → dataFlow `PARAMETER`, `PrecisionConstrained`.
- `DegreesToMilGrid = CLng(degValue * MILGRID_SCALE)` → dataFlow `CAST`, `PrecisionConstrained`
  (the `CLng` banker's-rounding and overflow risk are the load-bearing detail).
- `MilGridLonDelta` longitude delta `Long` → dataFlow `FIXED_DECIMAL`, `PrecisionConstrained`.
- `gLastGeoError As Integer` module global → infrastructure `CONFIGURATION`.
- `LegInsideBox` box check → businessLogic `VALIDATION` (mirrors the C#/SQL degree-box proxy).
