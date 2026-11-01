# JqSharp round 1 dogfooding findings

Target: `JqSharp 0.1.0` from this branch (`src/JqSharp` as a `ProjectReference`), consumed by a net8.0 console app
in `%TEMP%\dogfood-JqSharp` (`dotnet run -c Release -- 1..8`, `surface`, `depth`).
Library source was not opened while writing the scenarios, only the README and the generated XML docs.
Reference implementation: gojq built from `main` at `806d7bd` (2026-10-31) with Go 1.27.3. The port follows gojq
`main` after v0.12.19: against the v0.12.19 release binary 17 cases differ, 6 of them only because of fixes made on
`main` since (`abs`, `isnan`, `getpath` error text, date range errors).

## Scenarios

- [x] 1. jq-like CLI on the library (`-n`, `-r`, `--arg`, `--argjson`, `input`/`inputs`, exit codes), 18 invocations vs gojq
- [x] 2. System.Text.Json interop (JsonElement/JsonNode/POCO inputs, round trips, output types, mutation and aliasing)
- [x] 3. Error message quality (20 parse errors, 9 compile errors, 30 runtime errors, 26 malformed JSON texts vs STJ)
- [x] 4. Differential run against gojq: 369 edge cases (numbers, unicode, regex, paths, dates, control flow, errors)
- [x] 5. Extreme and degenerate input (deep nesting, runaway recursion, empty queries, 1e6-element ranges, 1 MB strings)
- [x] 6. Performance and allocation (compile, per-run, 100k-record document vs gojq, 8-thread sharing)
- [x] 7. Public surface audit (reflection over exported types, XML doc coverage)
- [x] 8. Modules, variables, environment and API misuse

---

# Verdict: 0.1.0 can ship. Six bugs were found and fixed in this round; the remaining gaps are not blocking.

The jq semantics themselves hold up: outside regular expressions and CLI-only features, every one of the 369
differential cases produces gojq's exact output and error text. Performance is in gojq's range and a shared `JqCode`
is genuinely thread-safe. The defects were all at the .NET boundary - how results are enumerated, how deep input
meets a fixed-size stack, what happens to a non-JqSharp type, UTF-16 vs code points, `Path.Combine` - which is
exactly what a port's own test suite (gojq's cases) cannot see. Two of them (process crash on deep input, silent
skip on re-enumeration) would have been serious in a 0.1.0.

## Bugs found (all fixed in this round)

Regression tests: `tests/JqSharp.Tests/DogfoodRound1Tests.cs` (30 cases, each failed or crashed the test host first).

### 1. [BUG, was critical] Deep nesting killed the process with a stack overflow

| input | before | after |
|---|---|---|
| `JqValue.FromJson` of 10,000 nested arrays (20 KB of text) | process exit `0xC00000FD` | `JqException: exceeded max depth` |
| `ToJson` / `Run(".")` / `. == .` / `[paths]` on a 10,000-deep value | process exit `0xC00000FD` | `JqException` |
| `Compile` of `[[[...]]]` 3,000 deep, or 10,000 `. + 1 \|` pipes | process exit `0xC00000FD` | `JqException` |
| `setpath` with a 100,000-element path | process exit `0xC00000FD` | `JqException` |

A `StackOverflowException` cannot be caught in .NET, so any service decoding untrusted JSON could be taken down
by a 20 KB request. gojq never hits this because Go grows goroutine stacks; its decoder stops at 10,000 levels.
The decoder already had gojq's 10,000 limit, but the recursion overflows a 1 MB stack at about 3,400 levels first.
Fixed with one guard (`RuntimeHelpers.EnsureSufficientExecutionStack`) on entry to every recursion over values and
queries - `Normalize`, the decoder, encoder, comparison, `contains`, `flatten`, path updates, deep merge, the
compiler and `JqQuery.ToString` - turning it into gojq's `exceeded max depth` error (catchable by `try` where gojq's is).
Depth now reachable (`depth` scenario):

| operation | main thread (1 MB) | thread pool | 16 MB thread | gojq |
|---|---|---|---|---|
| `FromJson` | 5,272 | 5,287 | 10,000 (limit) | 10,000 (limit) |
| `ToJson` | 2,715 | 6,420 | 50,000 | unlimited |
| `. == .` (incl. input normalization) | 1,193 | 3,902 | 25,000 | unlimited |
| Parse+Compile `[[...]]` | 1,691 | 4,994 | 57,789 | unlimited |

No measurable cost: the 100k-record benchmarks below moved within run-to-run noise. The README now documents the
limit and the larger-stack workaround.

### 2. [BUG, was critical] Enumerating `Run`'s results twice resumed the first run

`var r = code.Run(input); if (r.Any()) foreach (var x in r) ...` silently skipped the first output: the `Env` was
created once in `Run`, so every `GetEnumerator` shared one interpreter state. For `1, error("late")`, the second
`First()` threw `error: late`. Any LINQ consumer (`Any`, `Count`, then iterate) got wrong results with no error.
Fixed by creating the `Env` inside the iterator; each enumeration now reruns the query (documented on `Run`).

### 3. [BUG] Unsupported input types made `JqException.Message` throw

`code.Run(new[] { 1, 2 })` with `length` threw a `JqException` whose `Message` getter threw
`InvalidOperationException` - inside the consumer's `catch` block. `List<int>`, `object[]` and a variable bound to
`int[]` did the same; `JsonElement`, `DateTime`, `char` and anonymous objects failed only later, during enumeration.
Fixed at the trust boundary: `Run` (input and variable values) and `ToJson` now throw
`ArgumentException: invalid type: System.Int32[]; arrays must be List<object?> and objects Dictionary<string, object?>`
immediately.

### 4. [BUG] Regular expressions split surrogate pairs

| query on `"😀a"` and friends | gojq | before | after |
|---|---|---|---|
| `[scan(".")]` | `["😀","a"]` | `["\ufffd","\ufffd","a"]` | `["😀","a"]` |
| `"😀" \| gsub(""; "-")` | `"-😀-"` | `"-😀--"` | `"-😀-"` |
| `"é😀" \| [match(""; "g") \| .offset]` | `[0,1,2]` | `[0,1,2,2]` | `[0,1,2]` |
| `"😀" \| test("^.$")` | `true` | `false` | `true` |
| `"😀" \| capture("(?<a>.)")` | `{"a":"😀"}` | `{"a":"\ufffd"}` | `{"a":"😀"}` |

.NET regexes step over UTF-16 code units, RE2 over code points, so any emoji in user data plus `.` produced
replacement characters. Fixed by translating an unescaped `.` outside a class to "a surrogate pair or one code unit"
and skipping matches that start inside a pair. Negated classes (`[^a]`, `\W`) still match half a pair; documented.

### 5. [BUG, security] `JqModuleLoader` loaded modules by absolute path

`import "C:/any/where/lib" as l` loaded `lib.jq` from outside the search directories (`"/etc/x"` likewise on Linux);
gojq reports `module not found`. Cause: `Path.Combine(dir, name)` discards `dir` when `name` is rooted, unlike Go's
`filepath.Join`. Matters when the query is untrusted. Fixed by joining like Go; `../` keeps working, as in gojq.

### 6. [polish] `new JqModuleLoader(null)` threw `NullReferenceException`

Now `ArgumentNullException`, like `JqQuery.Parse(null)` and `JqValue.FromJson(null)` already did.

## What was verified as working

**Differential (scenario 4).** 369 cases: 349 identical to gojq before the fixes, 356 after. The 13 remaining:
7 are the documented .NET regex differences (`\d`, `\w`, `\s` match Unicode, `$` before a final newline, regex
syntax error text), 3 are CLI-only behavior (`input` without `InputIter`, `input_filename`, `$ENV` hidden by
default), 3 are the CLI's caret formatting of the same error. Big integers (`100000000000000000000001`), literal
preservation (`10.0`, `1E2`), `-0`, `1e1000`, int64 overflow into `BigInteger`, `@sh`/`@csv`/`@tsv`/`@base32d`,
`ltrimstr`/`trim`, `getpath`/`setpath`/`delpaths`/`pick`/`tostream`, `?//` destructuring, `limit(0)`, `label`,
`foreach`, dates and every runtime error message checked match gojq exactly.

**CLI (scenario 1).** A 40-line jq CLI over the library matches gojq's stdout and exit code in 17 of 18 invocations,
including `input`/`inputs` sharing one stream with the main loop, `--arg`/`--argjson`, `-r` with astral and NUL
characters, `halt_error` exit codes. The one difference is mine: a compile error and a runtime error are both
plain `JqException`s, so the CLI used exit 5 where gojq uses 3 (only the call site, `Compile` vs enumeration, tells them apart).

**System.Text.Json (scenario 2).** 20,000 POCO orders (1.7 MB): `JsonSerializer.Serialize` -> `FromJson` -> filter ->
`JsonNode.Parse` takes 110.6 ms / 34 MB; walking a `JsonElement` into `List`/`Dictionary` by hand is slower
(125.6 ms / 38 MB), so the text route is the one to recommend. `decimal` sums came out exact (`12050.00` vs `12050`).
`int`/`byte`/`ulong.MaxValue`/`float`/`decimal` inside a `Dictionary<string, object?>` are converted. Updates never
mutate the caller's input (`.a = 2`, `sort`, `reduce ... setpath`).

**Errors (scenario 3).** All 20 malformed queries raise `JqParseException` with gojq's message and token; `Offset` is
the UTF-8 byte offset of the token end, exactly gojq's `ParseError.Offset`. Compile errors (`function not defined:
foo/1`, `variable not defined: $x`, `module not found`) and runtime errors match gojq text, with previews truncated
at 30 bytes (`[0,1,2,3,4,5,6,7,8,9,10,1 ...]`). `JqValueException.Value` carries `error/1` objects intact.

**Performance (scenario 6).** First parse+compile including `builtin.jq`: 7.2 ms. A typical 90-character filter:
7 us parse + 9 us compile. Per run on a small object: 0.62 us / 1,448 B for `.`, 1.4 us for an object construction.
On a 5.9 MB, 100,000-record document (gojq time measured as wall time minus its own decode):

| query | JqSharp | gojq |
|---|---|---|
| `map(select(.a)) \| length` | 22 ms | 21-30 ms |
| `group_by(.k) \| map({...})` | 232-301 ms | 117-196 ms |
| `map(.v) \| sort \| .[50000]` | 347-391 ms | 437-494 ms |
| `[paths] \| length` | 494-517 ms | 478-491 ms |
| `tojson \| length` | 31-32 ms | 34-45 ms |
| `map(.v \|= . * 2) \| length` | 159-175 ms | 129-131 ms |

All 10 results identical to gojq. One `JqCode` shared by 8 threads: 16 runs in 3.0-3.7 s sequential vs 0.87-0.91 s
parallel, 0 mismatches; a shared `InputIter` of 1,000 values drained by 200 parallel runs delivered exactly 1,000.

**Surface (scenario 7).** 11 exported types; 42 of 43 public members have XML docs (the implicit
`JqCompilerOptions` constructor is the gap). No internal type is reachable except through `GetType().Name` on
exceptions (`Func0TypeError`, `SimpleError`, ...), which consumers can only catch as `JqException`.

**Modules and options (scenario 8).** `import`/`include`/data modules/`modulemeta`/nested imports/`.jq` init
modules match gojq; a custom in-memory `IJqModuleLoader` works. Variable count mismatches give gojq's messages,
names without `$` are rejected, `EnvironLoader` runs once at compile time, the process environment stays hidden.

## Recommended changes (not done in this round)

### P0

None left. Everything that blocked 0.1.0 was fixed above.

### P1

1. **Decide the Go-flavoured names before 0.1.0 (needs your call; renaming later is breaking).**
   `JqCompilerOptions.InputIter` (gojq `WithInputIter`; .NET would say `Inputs`), `EnvironLoader` returning
   `NAME=value` strings (Go's `os.Environ`; a .NET caller has to project `Environment.GetEnvironmentVariables()`
   into that format), and `IJqModuleLoader` taking a concrete `Dictionary<string, object?>` for `meta`.
2. **No way to stop a runaway query.** `def f: 1 + f; f` grew to 4.5 GB in 2.2 s and never yields, so the caller
   cannot even break out of the loop. gojq has `RunWithContext`, listed in the README as not ported. For a service
   running user-supplied filters this is the remaining denial-of-service vector after bug 1; a `CancellationToken`
   overload of `Run` is the .NET shape.
3. **`JqValue.FromJson` error messages read as jq-language errors.** `FromJson("1 2")` says
   `fromjson cannot be applied to: string ("1 2")` - no reason, no position, and it names a builtin the caller never
   used (`"01"` and `"{} {}"` likewise). STJ says `'2' is invalid after a single JSON value ... BytePositionInLine: 2`.
   A message of its own for the .NET entry point, with the reason and offset, would fix it; the `fromjson` builtin should keep gojq's.
4. **No multi-value JSON input.** `FromJson` decodes exactly one value. A CLI-like consumer splits NDJSON itself
   and has no way to read concatenated pretty-printed documents, the normal jq input. The internal decoder
   (`Decoder.DecodeAll`/`TryDecode`) already does it; exposing it would be new API beyond gojq's library surface.
5. **Mutating an output corrupts later runs.** After the caller added to the list returned by `{a: [1]}`, every
   later run (on any thread) returned `{"a":[1,99],"x":"mutated"}`. Same aliasing as gojq, but .NET callers mutate
   `List`/`Dictionary` far more readily than Go callers mutate maps. Now documented on `Run` and in the README;
   copying constants on output would remove the trap at some allocation cost.

### P2

1. Make the decoder, encoder and comparison iterative so 10,000-level JSON works on a 1 MB stack as in gojq
   (currently 1,193-5,272 levels on the main thread, see bug 1).
2. `JsonNumber`'s constructor does not validate: `new JsonNumber("abc")` makes `ToJson` emit `abc`, invalid JSON.
   The XML doc says the literal "must be" valid; throwing `ArgumentException` would enforce it.
3. `FromJson` costs 141-158 ms / 89 MB for a 5.9 MB document vs 75-85 ms / 23 MB for `JsonNode.Parse`
   (3.9x the allocation). `Run` also walks the whole input to normalize it on every call, so `length` on that
   document takes 3.3-4.2 ms where gojq takes 1 ms.
4. `JqParseException.Offset` is a UTF-8 byte offset of the token end (gojq's semantics). Highlighting the error in
   a C# string needs `Offset - Utf8Length(Token)` and a byte-to-char conversion; a char-based position would help.
5. `decimal` input is converted to `double` (`1.10m` -> `1.1`, more than 15-17 digits lost); a `JsonNumber` with
   the decimal's literal would keep it.
6. A custom `IJqModuleLoader` returning `null` causes a `NullReferenceException` in `Compile`; the XML doc now says to
   throw instead, a null check could report `module not found`.
7. Quadratic paths inherited from gojq: `[splits("a")]` over a 100,000-character string takes 11.2 s (gojq 10.6 s),
   `reduce .[] as $x ({}; .[$x | tostring] = $x)` over 50,000 keys 11.1 s (gojq 47.7 s). In JqSharp the first is
   `MatchObject` counting code points from the start of the string for every match, a cheap fix.
8. The README does not say which gojq revision the port follows; it took a build of gojq `main` to tell port
   differences from upstream changes after v0.12.19. One line with the commit would settle it for the next round.
9. Document the implicit `JqCompilerOptions` constructor (the only public member without XML docs).

## Resolved after round 1

Applied after the round, as decided by the maintainer (regression tests at the end of `DogfoodRound1Tests.cs`):

- P1-1: the Go-flavoured names (`InputIter`, `EnvironLoader`, the `meta` dictionary) stay as they are.
- P1-2: `JqCode.Run(input, cancellationToken, values)` and `JqQuery.Run(input, cancellationToken)` check the token
  before every interpreter instruction, where gojq checks `ctx.Done()`, and throw `OperationCanceledException`.
  `def f: 1 + f; f`, `[range(1e9)]` and `last(repeat(1))` stop within a 100 ms timeout. Cost: none measurable with
  `CancellationToken.None`; about 10% on an interpreter-bound `reduce range(3000000)` with a live token.
- P1-3: `JqValue.FromJson("1 2")` says `invalid JSON at offset 2: invalid character '2' after top-level value`;
  the `fromjson` builtin keeps gojq's text.
- P2-6: a custom `IJqModuleLoader` returning `null` is reported as `module not found`.
- P2-8: the README names the gojq revision the port follows (`main` at `806d7bd`).
- P2-9: `JqCompilerOptions` has an explicit, documented constructor.

P1-4, P1-5 and the other P2 items stay open.

## Assets

`%TEMP%\dogfood-JqSharp` holds the scenario app (`S1Cli.cs` ... `S8Modules.cs`, `Depth.cs`, `Surface.cs`) and
`s4-after.txt` with the differential output after the fixes. gojq was built from
`https://github.com/itchyny/gojq` at `806d7bd` into `%TEMP%\gojq-bin\gojq-main.exe`; the v0.12.19 release binary
is next to it.
