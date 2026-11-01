# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial C# port of gojq: the jq lexer, parser, compiler, bytecode interpreter and builtins, with
  `JqQuery.Parse`, `JqQuery.Compile` and `JqCode.Run`, compiler options for variables, the environment,
  `input`/`inputs` and module loading, and `JqValue.FromJson`/`JqValue.ToJson`.
- Cancellation, the counterpart of gojq's `RunWithContext`: `JqCode.Run(input, cancellationToken, values)` and
  `JqQuery.Run(input, cancellationToken)` check the token before every interpreter instruction and throw
  `OperationCanceledException`, so runaway queries such as `def f: 1 + f; f` can be stopped.

### Fixed

Found in dogfooding round 1 (`docs/dogfooding/round1.md`):

- Enumerating the results of `JqCode.Run` a second time continued the first run instead of starting over
  (`results.Any()` followed by `foreach` skipped the first output).
- Deeply nested JSON, values or queries overflowed the stack and killed the process; they now throw a
  `JqException` with `exceeded max depth`.
- Unsupported input types (`int[]`, `JsonElement`, ...) made `JqException.Message` itself throw, or failed only
  during enumeration; `Run` and `ToJson` now throw an `ArgumentException` naming the type.
- Regular expressions split surrogate pairs: `.` matched half of an emoji, and empty matches were reported inside
  a pair (`gsub(""; "-")` inserted an extra `-`).
- `JqModuleLoader` loaded modules by absolute path from outside its search directories; like gojq, a module name
  is now always resolved under a search directory.
- `new JqModuleLoader(null)` threw `NullReferenceException` instead of `ArgumentNullException`.
- `JqValue.FromJson` errors read like the `fromjson` builtin (`fromjson cannot be applied to: ...`) with no
  reason; they now give the reason and offset (`invalid JSON at offset 2: invalid character '2' after top-level value`).
  The `fromjson` builtin keeps gojq's message.
- A custom `IJqModuleLoader` returning `null` from `LoadModule` caused a `NullReferenceException`; it is now
  reported as `module not found`.
