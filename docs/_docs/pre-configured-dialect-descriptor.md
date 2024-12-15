---
title: Pre-configured dialect descriptors
tags: [configuration]
---
The CsvProfile class provides several pre-configured dialect descriptors that define common CSV dialects. These profiles are designed to simplify the setup of common CSV configurations.

## Available Profiles

### CommaDoubleQuote

A profile that uses a comma as the delimiter and enables double quoting. This profile assumes no header in the CSV.

* Delimiter: `,`
* Line Terminator: Platform-specific (e.g., \n or \r\n)
* Quote Character: `"`
* Escape Character: `\`
* Double Quote: Enabled
* Header: Disabled

**Usage:**

```csharp
var profile = CsvProfile.CommaDoubleQuote;
```

### SemiColumnDoubleQuote

A profile that uses a semicolon as the delimiter and enables double quoting. This profile assumes no header in the CSV.

* Delimiter: `;`
* Line Terminator: Platform-specific (e.g., \n or \r\n)
* Quote Character: `"`
* Escape Character: `\`
* Double Quote: Enabled
* Header: Disabled

**Usage:**

```csharp
var profile = CsvProfile.SemiColumnDoubleQuote;
```

### TabDoubleQuote

A profile that uses a tab character as the delimiter and enables double quoting. This profile assumes no header in the CSV.

* Delimiter: `\t` (`tab`)
* Line Terminator: Platform-specific (e.g., \n or \r\n)
* Quote Character: `"`
* Escape Character: `\`
* Double Quote: Enabled
* Header: Disabled

**Usage:**

```csharp
var profile = CsvProfile.TabDoubleQuote;
```

### PipeSingleQuote

A profile that uses a pipe character (`|`) as the delimiter and uses single quoting. This profile assumes no header in the CSV.

* Delimiter: `|`
* Line Terminator: Platform-specific (e.g., \n or \r\n)
* Quote Character: `"`
* Escape Character: `\`
* Double Quote: Enabled
* Header: Disabled

**Usage:**

```csharp
var profile = CsvProfile.PipeSingleQuote;
```
