---
title: Stream ownership and reader lifetimes
tags: [usage]
---

Opening, buffering, enumeration, and disposal are different events. Keep supplied input alive until the owning reader or enumerator finishes, and dispose it on early exit. Filename overloads stream input too.

## Ownership table

| API/path | Creation and reading | Supplied stream after completion/disposal |
| --- | --- | --- |
| `ToDataReader` / `ToDbDataReader` | Filename opens when called; rows read on advancement | Closed by reader/adapter |
| CSV `To<T>` | Lazy object enumeration | Left open; caller owns it |
| CSV `ToAsync<T>` | Lazy async enumeration | Left open; caller owns it |
| Shared arrays/dictionaries/dynamic outputs | Reader created during enumeration | Closed when enumeration completes, fails, or is disposed |
| Extension-reader `To<T>` | Data-reader mapper; filename may open when called | Closed by its enumerator |
| `ToCommonLog` / `ToCombinedLog` | Typed extension-reader enumeration | Closed by its enumerator |
| `ToDataTable` | Entire table read during the call | Closed during the call |
| CSV batch data reader | First input opened during construction | Inputs closed as batch advances/disposes |
| Arrow batches | Reader owned by batch enumeration | Follow underlying data-reader ownership; supplied input closes |

Explicit reader constructors that expose `leaveOpen` allow a different ownership choice. Check the actual overload; a general claim that all stream overloads leave input open is incorrect. An enumerator that is abandoned without disposal cannot reliably release its reader.

## Non-seekable input and cancellation

Readers with forward-only framing support non-seekable streams. Encoding policy and inference have separate requirements: see [initialization](../fluent-api-resource/) and [inference](../schema-inference/#streams). Buffering can consume beyond the returned row, so `Stream.Position` is not a reliable current-record location. Use [source positions](../source-positions/).

`ReadAsync` forwards cancellation to supported input operations. Do not resume consumption after interruption during I/O unless the API explicitly guarantees recovery. Open a fresh reader over replayable input to restart. Cancellation does not undo bytes already read or callback side effects.

## Record memory versus output values

Internal record spans, memory views, and reused field metadata expire on the next read or disposal. Parse/copy values synchronously when a custom mapper receives them. Returned strings and ordinary materialized row outputs remain valid afterwards. A custom mapper that returns borrowed storage must establish its own ownership; the library cannot make that output independent automatically.

Keep any [Arrow RecordBatch](../apache-arrow/) alive until its consumer finishes, then dispose it separately. Reader disposal does not replace consumer disposal of a retained batch.
