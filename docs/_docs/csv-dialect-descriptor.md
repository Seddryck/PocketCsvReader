---
title: CSV dialect descriptor
tags: [configuration]
---
The `CsvDialectDescriptor` class provides extensive configuration options for tuning the behavior of CSV parsing operations. Below is an explanation of each property and its potential impact on CSV processing. 

The description of PocketCsvReader is aligned with the [CSV Dialect Specification](https://specs.frictionlessdata.io/csv-dialect/#specification) provided by Frictionless Data.

---

## `Delimiter`

- **Description:** The character used to separate fields in a CSV file.
- **Default Value:** `','` (comma)
- **Tuning:**
  - Change this value to match the delimiter used in your CSV files.
  - Examples:
    - `';'` for semicolon-separated values (common in Europe).
    - `'\t'` for tab-separated values (TSV files).

## `LineTerminator`

- **Description:** The string used to terminate lines in a CSV file.
- **Default Value:** `\r\n` (carriage return and newline).
- **Tuning:**
  - Adjust for files that use different line endings.
  - Examples:
    - `\n` for Unix-style line endings.
    - `\r` for older Mac-style line endings.

## `QuoteChar`

- **Description:** The character used to quote fields containing special characters (e.g., delimiters or line terminators).
- **Default Value:** `"` (double quotes).
- **Tuning:**
  - Change this value if the CSV uses a different quoting character.
  - Example: `'` for single-quoted CSV fields.

## `DoubleQuote`

- **Description:** Specifies whether double quotes inside quoted fields should be escaped by doubling them.
- **Default Value:** `false`.
- **Tuning:**
  - Set to `true` if your CSV files use double-quote escaping (e.g., `field with ""quotes""`).
  - Set to `false` if another escaping method is used (e.g., backslash).

## `EscapeChar`

- **Description:** The character used to escape special characters within fields.
- **Default Value:** `null` (null, indicating no escape character).
- **Tuning:**
  - Set to a specific character if your CSV uses an escape mechanism (e.g., `\` for backslashes).

## `NullSequence`

- **Description:** A string used to represent null values in the CSV.
- **Default Value:** `null` (no explicit null representation).
- **Tuning:**
  - Define a specific string to represent nulls, such as `(null)`, `N/A`, or `-`.

## `SkipInitialSpace`

- **Description:** Specifies whether to skip whitespace following the delimiter.
- **Default Value:** `false`.
- **Tuning:**
  - Set to `true` if your CSV has spaces after delimiters (e.g., `   value1, "value2"`).

## `Header`

- **Description:** Specifies whether the first row(s) of the CSV contains column headers.
- **Default Value:** `true`.
- **Tuning:**
  - Set to `false` if your CSV does not include headers.
  - Useful for datasets where all rows are data.

## `HeaderRows`

- **Description:** Specifies the row indexes that contain the headers.
- **Default Value:** `[1]`.
- **Tuning:**
  - Set to `[1,2]` if your CSV defines headers across the first two rows.
  - Set to `[2,3]` if your CSV defines headers across the second and third two rows. The first row is ignored.
  - Useful for datasets where multiple rows combine to define the headers.

## `HeaderJoin`

- **Description:** Specifies the separator used to combine fields when headers span multiple rows.
- **Default Value:** ` ` (concatenates headers without a space as separator).
- **Tuning:**
  - Set to . to produce fields like `fruit.id` and `fruit.name`.
  - Useful for datasets with multi-line headers that need to be merged into a single row of header fields.

## `CommentChar`

- **Description:** The character used to denote comments in the CSV file. Must be the first character of the row.
- **Default Value:** `null` (null, indicating no comments).
- **Tuning:**
  - Specify a comment character (e.g., `'#'` or `';'`) to skip lines starting with that character.

## `CommentRows`

- **Description:** Specifies row indexes that are treated as comments, regardless of whether `commentChar` is set.
- **Default Value:** `[]` (no specific rows are treated as comments).
- **Tuning:**
  - Specify indexes like `[1,3,4]` to treat the first, third, and fourth rows as comments.
  - Useful for skipping predefined rows that do not contain data.

## `CaseSensitiveHeader`

- **Description:** Indicates whether header names should be treated as case-sensitive.
- **Default Value:** `false`.
- **Tuning:**
  - Set to `true` if your application differentiates between header names like `"Name"` and `"name"`.

## `CsvDdfVersion`

- **Description:** The version of the CSV dialect descriptor format.
- **Default Value:** `"1.0"`.
- **Tuning:**
  - Adjust to a specific version if required by compatibility standards.
