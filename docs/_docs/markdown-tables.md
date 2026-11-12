---
title: Markdown tables
tags: [quick-start, markdown]
---

Install `PocketCsvReader.Markdown` to read one standalone [GitHub Flavored Markdown pipe table](https://github.github.com/gfm/#tables-extension) per reader.

```sh
dotnet add package PocketCsvReader.Markdown
```

<!-- example: markdown-table -->
```csharp
using System;
using System.IO;
using System.Text;
using PocketCsvReader.Markdown.Configuration;

const string source = """
    ## Results

    | Id | Name |
    | --: | :--- |
    | 1 | Ada |
    """;

var markdown = new MarkdownReaderBuilder().WithTableAfter("## Results").Build();
using var input = new MemoryStream(Encoding.UTF8.GetBytes(source));
using var reader = markdown.ToDataReader(input);
while (await reader.ReadAsync())
    Console.WriteLine($"{reader.GetInt32(0)} {reader.GetString(1)}");
```

<!-- output: markdown-table -->
```text
1 Ada
```

## Select a table

- By default the reader selects the first valid table. Use `WithSkipTables(1)` to read the second valid table. Header-only tables count toward this setting. `WithSkip(count)` skips data rows in the selected table.
- Use `WithTableAfter("## Results")` to select a table beginning on the next nonblank line after the first matching marker. The comparison is ordinal after trimming surrounding whitespace. An absent marker or an invalid immediate table produces no rows. Configure a table skip count or a text marker when selecting a table.
- The header, delimiter, and body lines start with `|` after optional whitespace. A closing pipe is optional. The header and delimiter must be adjacent and have the same width. The scanner ignores other nonblank lines. A blank line or another valid header and delimiter pair ends the selected table.

The reader uses the header cells as column names and accepts alignment markers in the delimiter row. It trims cell padding, decodes `\|` to a literal pipe, and preserves other inline Markdown as text. `GetRawString` returns the trimmed cell with pipe escapes intact. Short rows are padded with empty strings.

Rows with surplus cells throw `InvalidDataException` by default. Use `WithBadDataPolicy` to skip the row, stop reading, or explicitly return the cells that fit the header.

Use `Read()` or `ReadAsync()` on files and non-seekable streams. The normal data reader and [materialization APIs](../output-models/) expose the selected table.
