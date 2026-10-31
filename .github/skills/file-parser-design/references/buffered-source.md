# Buffered record-source reference

Use `PocketCsvReader.Testing/BufferedSourceExampleTest.cs` as a compiled example of the storage lifecycle for a new line-based format. Its deliberately small grammar has two pipe-separated fields. It is a test fixture, not a new supported file format or a production base class.

The example:

1. Owns one `BufferedRecordReader` and a fixed-size `FieldSpan[]` per source.
2. Calls `BeginOperation()` only when advancing to the next record. Borrowed memory and metadata from the previous record may become invalid at this point.
3. Reads frames using `ReadLineFrame()` / `ReadLineFrameAsync()`, avoiding a line string. For a format whose records can contain line terminators, implement an `IRecordFramer` and use `ReadFrame()` / `ReadFrameAsync()` instead.
4. Parses field offsets from `ReadOnlyMemory<char>.Span`, overwriting the same field array. Both transports call the same grammar method.
5. Returns `RecordSpan.FromMemory(memory, fields)` and converts that view with `AsMemory()` when an async result needs `RecordMemory`. The latter creates a wrapper, not another character array.
6. Returns buffers on disposal. The caller retains responsibility for the input `StreamReader`, as `BaseDataReader` normally does.

`BufferedRecordReader` handles cross-buffer captures. When an implementation reads or peeks ahead after capturing a record, call `Protect(capture)` before that lookahead so a refill cannot invalidate the current record. Capturing alone does not allow indefinite retention. Consumers that retain materialized records need independently owned values.

For fixed schemas, reusing the metadata array is appropriate only when consumers accept a current-record view. `RecordSpan` being a ref struct does not stop a caller retaining its `FieldSpans` array. State the lifetime explicitly. If a public contract promises persistent metadata, copy that required output rather than exposing scratch storage. Never use a public span constructor as an implicit ownership transfer: it copies characters.

The example's tests cover small buffers, long records, CRLF/LF and unterminated EOF, Unicode, malformed/empty input, async-only non-seekable input, buffer and metadata identity, retained strings, and a warmed raw-allocation budget. New formats should add their own grammar and cancellation/ownership tests. General JSON's variable/nested metadata and escaped-string decoding may justify allocations that this fixed-schema example does not need.

## Source guard

`dotnet run --project .github/tools/ParserAllocationGuard -c Release -- .` parses production C# syntax using Roslyn from the installed SDK, without another NuGet dependency. It discovers source files in root-level `PocketCsvReader*` project directories, including newly added packages and helpers, while excluding test, benchmark, profiler, and build-output directories. It checks all source in that scope, rather than guessing hot paths from filenames.

| Rule | Review trigger |
| --- | --- |
| PA001 | `ReadLine()` or `ReadLineAsync()` calls, including conditional calls |
| PA002 | Explicit non-empty `RecordSpan` / `RecordMemory` construction, plus target-typed construction with an explicit variable declaration |
| PA003 | Per-call `List<FieldSpan>` construction, including explicitly typed `new()` / `[]`; reusable field/property initializers are excluded |
| PA004 | `ToString()` on a directly declared `StringBuilder` variable or an immediately constructed builder |

These are review triggers, not a semantic allocation analyzer. Memory-backed internal constructors can be safe; quoted value decoding and independently owned output can be necessary. Existing reviewed expressions have exact file/expression/count exceptions with reasons. New expressions and duplicate occurrences fail, and stale exceptions fail so resolved debt cannot silently become a reusable allowance. Changes to this list must be reviewed as architecture changes rather than accepted as generated output.

The guard ignores comments and string contents. It does not follow aliases/data flow, fully resolve overloaded constructors, recognize every target-typed assignment, or analyze inactive preprocessor branches. It cannot detect every possible allocation, enforce benchmark budgets for every future format, or certify zero-copy behavior. The mandatory ownership and allocation tests and PR review cover those gaps. Use `--inventory` to inspect findings, not to regenerate exceptions automatically.
