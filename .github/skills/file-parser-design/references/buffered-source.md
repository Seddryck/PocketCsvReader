# Buffered record-source reference

Use `PocketCsvReader.Testing/BufferedSourceExampleTest.cs` as a compiled example of the storage lifecycle for a new line-based format. Its deliberately small grammar has two pipe-separated fields. It is a test fixture, not a new supported file format or a production base class.

The example:

1. Owns one `BufferedRecordReader` and a fixed-size `FieldSpan[]` per source.
2. Calls `BeginOperation()` only when advancing to the next record. Borrowed memory and metadata from the previous record may become invalid at this point.
3. Reads frames using `ReadLineFrame()` / `ReadLineFrameAsync()`, avoiding a line string. For a format whose records can contain line terminators, implement an `IRecordFramer` and use `ReadFrame()` / `ReadFrameAsync()` instead.
4. Parses field offsets from `ReadOnlyMemory<char>.Span`, overwriting the same field array. Both transports call the same grammar method.
5. Returns `RecordSpan.FromMemory(memory, fields)` and converts that view with `AsMemory()` when a public async result needs `RecordMemory`. The latter creates a wrapper, not another character array. This compatibility example does not demonstrate the wrapper-free internal reader transport; use `CurrentRecord`/`ICurrentRecordSource` for that path as illustrated by CSV.
6. Returns buffers on disposal. The caller retains responsibility for the input `StreamReader`, as `BaseDataReader` normally does.

`BufferedRecordReader` handles cross-buffer captures. When an implementation reads or peeks ahead after capturing a record, call `Protect(capture)` before that lookahead so a refill cannot invalidate the current record. Capturing alone does not allow indefinite retention. Consumers that retain materialized records need independently owned values.

For fixed schemas, reusing the metadata array is appropriate only when consumers accept a current-record view. `RecordSpan` being a ref struct does not stop a caller retaining its `FieldSpans` array. State the lifetime explicitly. If a public contract promises persistent metadata, copy that required output rather than exposing scratch storage. Never use a public span constructor as an implicit ownership transfer: it copies characters.

The example's tests cover small buffers, long records, CRLF/LF and unterminated EOF, Unicode, malformed/empty input, async-only non-seekable input, buffer and metadata identity, retained strings, and a warmed raw-allocation budget. New formats should add their own grammar and cancellation/ownership tests. General JSON's variable/nested metadata and escaped-string decoding may justify allocations that this fixed-schema example does not need.

## Shared publication and reader transport

`FieldMetadataBuffer` keeps an exact-size reusable result array, separate from the
parser's scratch list/array. `PublishBorrowed` invalidates the previous view at the
next publication; `CopyOwned` gives public consumers independent top-level metadata.
Character memory and nested values retain their separately documented lifetimes.

`CurrentRecord` carries character or UTF-8 memory, metadata, and a decoding-buffer
reference as a value. `BaseRawRecord.SetRecord` stores it without a wrapper;
`GetCurrentFieldMemory` and CSV's direct UTF-8 access read requested fields from it.
The protected `Record` property materializes a compatibility wrapper only if a
caller actually needs that API. `BaseDataReader.ReadSource`/`ReadSourceAsync` select
the internal borrowed transport for opted-in sources and preserve public/custom
read entry points for other sources. Do not change `RecordReadResult`'s public
record-object contract to achieve an internal allocation saving.

Recent fixes show the repeated failure mode: key/value (#383), web logs (#376),
fixed-width (`6f0f970`, `bc9b533`), JSON (#389), and lazy JSON decoding (#392).
The new CSV regression suite measures the difference explicitly: fresh seven-field
metadata (416 bytes) plus a wrapper (80 bytes) used to cost 496 bytes per row even
without accessing a value. Required selected strings belong in a separate budget.
Prefer sharing lifecycle/storage code across formats over copying one more local
optimization; keep grammar and format-specific ownership constraints in their packages.

`WebLogRecordSource` also implements the shared transport: its async data reader
receives a value view, while public async source calls materialize the wrapper.
`PocketCsvReader.WebLogs.Testing/CurrentRecordViewTests.cs` checks Common, Combined,
and W3C in both transports with a budget that rejects an 80-byte wrapper per row.
The W3C benchmark breakdown separately measures raw reads, numeric access, URI
strings, and request reconstruction; a higher projection total alone is not
evidence of fresh parser metadata or eager decoding.

## Source guard

`dotnet run --project .github/tools/ParserAllocationGuard -c Release -- .` parses production C# syntax using Roslyn from the installed SDK, without another NuGet dependency. It discovers source files in root-level `PocketCsvReader*` project directories, including newly added packages and helpers, while excluding test, benchmark, profiler, and build-output directories. It checks all source in that scope, rather than guessing hot paths from filenames.

| Rule | Review trigger |
| --- | --- |
| PA001 | `ReadLine()` or `ReadLineAsync()` calls, including conditional calls |
| PA002 | Explicit non-empty `RecordSpan` / `RecordMemory` construction, plus target-typed construction with an explicit variable declaration |
| PA003 | Per-call `List<FieldSpan>` construction, including explicitly typed `new()` / `[]`; reusable field/property initializers are excluded |
| PA004 | `ToString()` on a directly declared `StringBuilder` variable or an immediately constructed builder |
| PA005 | Assigning a parameterless `AsMemory()` result to `Record`, including qualified member assignments: review an eager per-row wrapper in the reader handoff |

These are review triggers, not a semantic allocation analyzer. Memory-backed internal constructors can be safe; quoted value decoding and independently owned output can be necessary. Existing reviewed expressions have exact file/expression/count exceptions with reasons. New expressions and duplicate occurrences fail, and stale exceptions fail so resolved debt cannot silently become a reusable allowance. Changes to this list must be reviewed as architecture changes rather than accepted as generated output.

The guard ignores comments and string contents. It does not follow aliases/data flow, fully resolve overloaded constructors, recognize every target-typed assignment, or analyze inactive preprocessor branches. It cannot detect every possible allocation, enforce benchmark budgets for every future format, or certify zero-copy behavior. The mandatory ownership and allocation tests and PR review cover those gaps. Use `--inventory` to inspect findings, not to regenerate exceptions automatically.
