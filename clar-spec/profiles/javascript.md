# CLAR Language Profile — JavaScript

> Part of **FORGE EVOLVE for TMPC**. Defines how a JavaScript source module maps into the
> four CLAR layers of [`clar-spec/CLAR.schema.json`](../CLAR.schema.json). The surrogate's
> representative JS artifact is the mission-review client
> `surrogate/tmpc-surrogate-mds/legacy/wwwroot/mission-review.js`.

`sourceLanguage`: **`JavaScript`**

## Layer mapping

| JavaScript construct | CLAR layer | Node `type` |
|---|---|---|
| top-level module / IIFE pipeline | controlFlow | `PIPELINE` |
| `if` / ternary | controlFlow | `BRANCH` |
| `switch` | controlFlow | `SWITCH` |
| `for` / `for…of` / `.forEach` / `.map` | controlFlow | `FOR_LOOP` |
| `while` | controlFlow | `WHILE_LOOP` |
| `do…while` | controlFlow | `DO_UNTIL` |
| `try` / `catch` | controlFlow | `TRY_CATCH` |
| `async`/`await`, `Promise.all` | controlFlow | `COROUTINE` / `PARALLEL` |
| `addEventListener` / `onClick` | controlFlow | `EVENT_HANDLER` |
| function parameter | dataFlow | `PARAMETER` |
| `let` / `var` | dataFlow | `VARIABLE` |
| `const` literal | dataFlow | `CONSTANT` |
| `return` value | dataFlow | `RETURN_VALUE` |
| `+ - * /` expression | dataFlow | `ARITHMETIC` |
| `===` / comparison | dataFlow | `COMPARISON` |
| `Number(x)` / `parseInt` / `\| 0` | dataFlow | `CAST` |
| `.reduce((a,b)=>a+b)` running total | dataFlow | `AGGREGATE` |
| `Array` | dataFlow | `COLLECTION` |
| object literal / record shape | dataFlow | `RECORD` |
| `Date` / epoch-ms math | dataFlow | `DATE_TIME` |
| template-string / `.replace` | dataFlow | `STRING_OP` |
| client-side validation/calculation | businessLogic | `VALIDATION` / `CALCULATION` / `CONSTRAINT` |
| `fetch` / `XMLHttpRequest` | infrastructure | `API_CALL` |
| `localStorage` / file read | infrastructure | `FILE_IO` |
| `console.*` | infrastructure | `LOGGING` |
| config object / `window.__CONFIG__` | infrastructure | `CONFIGURATION` |

## Precision-constrained mapping (load-bearing)

JavaScript has a **single numeric type (IEEE-754 `double`)**, so *every* coordinate, distance,
and time value is implicitly a float — the precision hazard is even sharper than C#. CLAR marks
these `precisionConstrained` so the target generator does not naïvely port `Number` math.

| JS source value | CLAR `clarType` | `precisionConstrained` | Target generator emits |
|---|---|---|---|
| `number` holding **lat/lon** | `PrecisionConstrained` | `true` | `decimal` / `BigInt`-scaled fixed-point + `checked` |
| `number` holding **distance** (nm) | `PrecisionConstrained` | `true` | `decimal`, exact summation |
| `number`/`Date` **epoch-seconds / TOT** | `PrecisionConstrained` | `true` | integer epoch + `decimal` travel time, explicit rounding |
| money / `toFixed`-formatted value | `PrecisionConstrained` | `true` | `decimal` |
| `number` used for UI scaling / pixels | `FloatingPoint` | `false` | `double`/`number` acceptable |
| array index / counter | `Integer` | `false` | native integer |
| `boolean` flag | `Boolean` | `false` | `bool` |
| `string` | `Text` | `false` | `string` |

**Note on `BigInt`:** when a JS value is an epoch in milliseconds or a scaled fixed-point grid
unit, the target may emit `long`/`BigInt` rather than `decimal`; the `precisionConstrained`
flag still applies — the contract is "no silent float coercion", not "always decimal".

## Worked example (`mission-review.js`)

- A `reduce` summing per-leg distances → dataFlow `AGGREGATE`, `PrecisionConstrained`.
- Lat/lon read from the DOM/JSON → dataFlow `PARAMETER`/`VARIABLE`, `PrecisionConstrained`.
- A `fetch('/api/missions/publish')` → infrastructure `API_CALL`.
- Go/No-Go badge rendering off a `boolean` → dataFlow `RETURN_VALUE`, **not** constrained.
