## JqSharp

[![NuGet](https://img.shields.io/nuget/v/JqSharp)](https://www.nuget.org/packages/JqSharp)
[![Build](https://github.com/HanJaeJoon/JqSharp/actions/workflows/nuget-publish.yml/badge.svg)](https://github.com/HanJaeJoon/JqSharp/actions/workflows/nuget-publish.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

JqSharp is a pure C# implementation of the [jq](https://github.com/jqlang/jq) JSON filter language.
It is a port of [gojq](https://github.com/itchyny/gojq) by itchyny: the lexer, parser, compiler, bytecode
interpreter and builtins follow gojq file by file, and the error messages are the same as gojq's.
It has no dependencies (no System.Text.Json).

Targets `netstandard2.0`, `netstandard2.1`, `net8.0`, and `net10.0`.

## Installation

``` bash
dotnet add package JqSharp
```

## Usage

``` c#
using JqSharp;

JqQuery query = JqQuery.Parse(".items[] | select(.price > 10) | .name");
JqCode code = query.Compile();

object? input = JqValue.FromJson("{\"items\": [{\"name\": \"a\", \"price\": 5}, {\"name\": \"b\", \"price\": 20}]}");
foreach (object? value in code.Run(input))
{
    Console.WriteLine(JqValue.ToJson(value)); // "b"
}
```

Values are `null`, `bool`, numbers (`long`, `double`, `BigInteger`, or `JsonNumber`, which keeps the literal text of a
decoded JSON number), `string`, `List<object?>` for arrays and `Dictionary<string, object?>` for objects. Other
primitive numbers passed as input (`int`, `float`, ...) are converted.

Errors are `JqException`s: `JqParseException` for syntax errors, `JqValueException` for `error/1` (with the value),
`JqHaltException` for `halt` and `halt_error`. Runtime errors are thrown while enumerating the results.

`JqCompilerOptions` provides:

- `Variables`: names of variables (like `$x`) whose values are passed to `Run`.
- `EnvironLoader`: the environment for `$ENV` and `env` (not visible by default).
- `InputIter`: the values `input` and `inputs` read.
- `ModuleLoader`: an `IJqModuleLoader` for `import` and `include`; `JqModuleLoader` loads modules from directories.

## Differences from gojq

- The first runtime error ends the enumeration; gojq's iterator can continue after an error.
- Regular expressions use .NET `System.Text.RegularExpressions` instead of Go's RE2, so rare syntax and error
  messages differ (`\d`, `\w` and `$` follow .NET rules).
- `%Z` in `strflocaltime` prints a numeric zone name (like `+09`) since .NET does not expose zone abbreviations.
- Not ported: the gojq command line program (and with it `debug`, `stderr`, `input_filename`), YAML, `--stream`,
  `--seq`, custom functions (`WithFunction`, `WithIterFunction`), and context cancellation (`RunWithContext`).

## Tests

The tests run the data-driven suite of gojq (`cli/test.yaml`, converted to `tests/JqSharp.Tests/testdata/cli-cases.jsonl`)
through the library, plus the examples of gojq's `query_test.go` and `compiler_test.go`. Cases that need the command
line program are listed in `CliCaseTests.SkippedCases` with the reason.

## License

MIT. JqSharp is derived from gojq, Copyright (c) 2019-2026 itchyny, also under the MIT license; see [LICENSE](LICENSE).
The math functions are ported from the Go standard library (BSD license), and `strftime`/`strptime` from
[timefmt-go](https://github.com/itchyny/timefmt-go) (MIT).
