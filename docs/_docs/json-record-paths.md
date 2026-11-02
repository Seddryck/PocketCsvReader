---
title: Select JSON nodes as rows
tags: [input]
---

## Select nested nodes as records

Both builders support `WithRecordPath(string path)`:

```csharp
using PocketCsvReader.Json.Configuration;
using PocketCsvReader.Ndjson.Configuration;

var json = new JsonReaderBuilder()
    .WithRecordPath("$.child[*].grandchildren[*]")
    .Build();

var ndjson = new NdjsonReaderBuilder()
    .WithRecordPath("$.child[*].grandchildren[*]")
    .Build();

using var reader = json.ToDataReader(stream);
while (reader.Read())
{
    var grandchild = reader.GetString(0);
}
```

For `{"child":[{"grandchildren":["Ada","Grace"]},{"grandchildren":["Linus"]}]}`, this path produces three rows: `Ada`, `Grace`, and `Linus`. Using `$.child[*].grandchildren` instead produces two rows, each with one array field; call `GetArray(0)` to read each array. Arrays are never implicitly flattened.

The supported subset of [RFC 9535 JSONPath](https://www.rfc-editor.org/rfc/rfc9535.html) is:

- `$` identifies the complete JSON document, or each complete value in an NDJSON stream. `$` alone selects that value as one row, including a root array.
- `.name` selects an object member. Names start with an ASCII letter, underscore, or non-ASCII Unicode scalar, followed by those characters or ASCII digits.
- `['name']` or `["name"]` selects a literal member name, including empty names and punctuation, such as `$['child.with.dot']`. Quoted names support `\\`, `\/`, the matching escaped quote, `\b`, `\f`, `\n`, `\r`, `\t`, and `\uXXXX` (including surrogate pairs).
- `[*]` selects array elements in document order and may be repeated. It selects nothing from objects or scalar values.

No whitespace is accepted outside quoted member names. Indexes, filters, recursive descent, unions, slices, and `.*` are unsupported. Member comparisons are case-sensitive and do not normalize Unicode. Invalid or unsupported syntax throws `ArgumentException`, including the path, when the builder's `Build()` method or profile constructor runs. Profiles also accept the optional `recordPath` constructor argument.

Each match becomes exactly one row. Objects expose their members as labeled fields; strings, numbers, booleans, arrays, and null expose one unnamed field at ordinal zero. Strings are decoded by the existing getters and null retains database-null semantics. Schema, custom parsers, and projection apply to each selected row; properties outside the selected node are absent. NDJSON's stable-object-shape option, when enabled, applies to the selected objects.

Missing members and non-array wildcard inputs produce no matches; a path with no matches gives an empty reader. Duplicate occurrences of a member name used by the path throw `InvalidDataException` when that object is visited. Malformed JSON throws `InvalidDataException` with position information, including in unselected branches.

Selection uses the existing JSON parser and buffered framing. It captures and parses the complete document for JSON, or one complete framed value at a time for NDJSON, then lazily enumerates matches without collecting a result list. The captured input stays alive until its matches have been consumed. `ReadAsync` uses asynchronous stream reads and follows the same selection rules as `Read`.

Omitting `WithRecordPath` preserves the existing root-object and root-array behavior, including streaming top-level JSON array elements.

See [JSON/JSONC](../json-and-ndjson/) and [NDJSON](../ndjson/) for their independent record framing rules.
