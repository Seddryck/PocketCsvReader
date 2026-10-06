---
name: file-parser-design
description: Design, implement, or review a PocketCsvReader parser for a new file format while preserving the repository's buffered span-based architecture, memory ownership, bounded allocations, and sync/async behavior. Use for new formats and substantial record-source rewrites; do not use for small parsing fixes that leave those architectural concerns unchanged.
---

# File parser design

Build new format readers as extensions of PocketCsvReader's parsing architecture, not as standalone stream-processing implementations. Preserve correctness first, then preserve the zero-copy and bounded-allocation behavior that existing abstractions make possible.

## Establish the design from repository evidence

Before writing a new parser, inspect the current versions of:

- `IRecordSource<TProfile>` and `BaseRecordParser<TProfile>`;
- `IBufferReader`, `StreamBuffer`, `SingleBuffer`, and `DoubleBuffer`;
- `RecordSpan` and `RecordMemory`, including their ownership-oriented internal constructors;
- `BaseDataReader<TProfile>` and the closest existing format implementation;
- parser optimization settings, tests, benchmarks, and package boundaries relevant to the new format.

Trace one record from the input stream to the consumer. Record these decisions in working notes or the pull-request description:

- how record boundaries are detected;
- which component owns each character buffer;
- how long returned memory remains valid;
- what happens when a record fits in one buffer;
- what happens when a record crosses buffers;
- which values genuinely require decoding or materialization;
- how synchronous and asynchronous reads share parsing state.

Do not copy the architecture of the closest format blindly. Treat it as evidence, and compare it with the core buffered parser before adopting its tradeoffs.

## Preserve the parsing invariants

### Read chunks, not characters

Read input into reusable `Memory<char>` buffers. Parse each chunk through `ReadOnlySpan<char>` or `ReadOnlyMemory<char>`.

Do not call `StreamReader.Read()` or `ReadAsync()` once per character on the normal path. An asynchronous parser should normally await buffer refills, not individual characters.

### Keep a zero-copy fast path

When a complete record is contained in parser-owned memory, return a `ReadOnlyMemory<char>` slice over that memory and construct the record through the ownership-preserving path.

Do not pass an existing record through a public `ReadOnlySpan<char>` constructor when that constructor copies with `ToArray()`. Inspect the actual constructor behavior rather than inferring allocation behavior from its type name.

### Copy at most once when a record spans buffers

Records may cross input-buffer boundaries. Accumulate only in that case, using pooled or parser-owned character memory where practical. Preserve the accumulated memory until the consumer advances to the next record.

Avoid full-record pipelines such as:

```text
StringBuilder -> string -> char[]/ReadOnlyMemory
```

If a complete-record `string`, `StringBuilder`, or second full-size buffer is necessary, document why the format or public contract requires it and verify that no equivalent parser-owned memory already exists.

Memory must remain bounded by the active record and parser buffers, not by the complete input document.

### Separate framing from field parsing

The framing state machine identifies record boundaries across chunks. The field parser interprets a completed record and produces `FieldSpan` metadata.

It is acceptable to scan a record once for framing and again for fields when that keeps the implementation clear and allocation-free. Do not materialize the record merely to pass it between those phases.

Prefer a field parser over `ReadOnlySpan<char>` or `ReadOnlyMemory<char>`. A class cannot retain a `ReadOnlySpan<char>` field; use a `ref struct`, a static parse operation, or retained `ReadOnlyMemory<char>` according to the required lifetime.

### Decode only when semantics require it

Formats with quoted strings must track quote, escape, delimiter, and nesting state. That syntax handling is required; allocating a managed string for every token is not.

Represent unescaped values as spans into the record. Allocate decoded text only when escapes, normalization, or an existing public result type requires it. A temporary builder limited to an escaped value can be reasonable; a builder for every complete record is not the default.

### Share grammar between sync and async paths

Keep framing and token state independent of the input transport so synchronous and asynchronous entry points feed the same state machine.

Avoid:

- duplicated sync and async grammar;
- a Boolean mode threaded through complex parsing logic;
- sync-over-async as the permanent design;
- different validation or error-position behavior between sync and async reads.

Small transport-specific refill loops are preferable to duplicating the parser.

### Preserve ownership explicitly

Returned `RecordSpan` or `RecordMemory` data must remain valid for the lifetime promised by the owning reader. Do not overwrite or return pooled buffers while the current record can still be consumed.

Use parser-owned memory and internal ownership-preserving constructors when the record is valid until the next read. If longer-lived records are required by a public API, make the ownership transfer or copy explicit.

### Keep format code at the correct package boundary

Put format-specific framing, grammar, profiles, and builders in the format package. Change the core package only for a genuinely reusable abstraction needed by more than one format.

Do not move format grammar into the core merely to obtain internal access. Prefer a small reusable core primitive or an explicit friend-assembly boundary when that matches existing package contracts.

## Review the allocation path

Before declaring the implementation complete, search the new parser path for:

- `StringBuilder`;
- `ToString()` and range/string slicing;
- `ToArray()`;
- `new string` and complete-record strings;
- one-character buffers or stream reads;
- `List<T>` growth for fields or nesting;
- separate complete-record storage in consecutive layers.

For each occurrence, decide whether it is:

- required by the public result;
- limited to escaped or transformed data;
- the cross-buffer slow path;
- reusable parser storage;
- or an avoidable full-record allocation.

Typical justified allocations include copied field metadata, decoded escaped strings, and one owned buffer for a record that crosses chunks. A whole-record string followed by another whole-record copy is not justified solely because a downstream parser currently accepts `string`; change that parser boundary.

## Validate behavior and architecture

Add focused tests appropriate to the format, including:

- records wholly contained in one small input buffer;
- records and delimiters split across buffer boundaries;
- a record larger than the configured buffer;
- empty input and empty records or containers where valid;
- nested structures, quoted delimiters, escapes, and Unicode when supported;
- malformed and truncated input with useful absolute positions;
- non-seekable streams;
- asynchronous-only streams proving that the async path uses async I/O;
- large multi-record documents proving that the first record is returned before the complete document is consumed.

Run focused tests for every supported target framework before broader validation. When the implementation introduces a new hot path or replaces an established parser, add or run an allocation/performance benchmark with representative small, cross-buffer, and large records. Report benchmarks as comparative evidence, not as a universal performance guarantee.

## Pre-PR review gate

Do not open or finalize the pull request until the review can answer yes to all applicable questions:

- Does normal parsing read chunks rather than one character at a time?
- Is there a zero-copy path for records already contained in parser-owned memory?
- Is a cross-buffer record copied no more than once before consumption?
- Does the field parser accept span- or memory-backed input rather than requiring a complete-record string?
- Are escaped strings the only ordinary values that require decoding allocations?
- Do sync and async entry points use the same framing and grammar state?
- Is current-record memory valid until the reader advances?
- Is memory bounded by the active record rather than the whole document?
- Are format-specific concerns confined to the format package?
- Do focused tests cover buffer boundaries, malformed input, streaming, and async I/O?

If an answer is no, either revise the design or explain the concrete format/API constraint in the pull request. Convenience or similarity to another allocation-heavy parser is not sufficient evidence.
