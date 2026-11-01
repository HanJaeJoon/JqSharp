# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial C# port of gojq: the jq lexer, parser, compiler, bytecode interpreter and builtins, with
  `JqQuery.Parse`, `JqQuery.Compile` and `JqCode.Run`, compiler options for variables, the environment,
  `input`/`inputs` and module loading, and `JqValue.FromJson`/`JqValue.ToJson`.
