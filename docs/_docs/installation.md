---
title: Installation of the Library
tags: [installation]
---
This guide explains how to install the library using three common methods:

- NuGet CLI (`nuget.exe`)
- Visual Studio Package Manager Console
- .NET CLI

## 1. Installing with NuGet CLI (`nuget.exe`)

### Prerequisites with NuGet CLI

- Download and install `nuget.exe` from the [NuGet official website](https://www.nuget.org/downloads).
- Ensure `nuget.exe` is accessible via your system's PATH.

### Steps with NuGet CLI

1. Open a command prompt or terminal.
2. Run the following command:

   ```bash
   nuget install PocketCsvReader -Version <VersionNumber>
   ```

   Replace `<VersionNumber>` with the desired version. If no version is specified, the latest version will be installed.

## 2. Installing with Visual Studio Package Manager Console

### Prerequisites with Visual Studio Package Manager Console

- Visual Studio must be installed.
- Your project should be loaded in Visual Studio.

### Steps with Visual Studio Package Manager Console

1. Open the **Package Manager Console** from **Tools > NuGet Package Manager > Package Manager Console**.
2. Run the following command:

   ```powershell
   Install-Package PocketCsvReader -Version <VersionNumber>
   ```

   Replace `<VersionNumber>` with the desired version. If no version is specified, the latest version will be installed.

## 3. Installing with .NET CLI

### Prerequisites with .NET CLI

- .NET SDK must be installed. You can download it from the [official .NET website](https://dotnet.microsoft.com/).
- Ensure the `dotnet` command is accessible from your terminal.

### Steps with .NET CLI

1. Open a terminal or command prompt.
2. Navigate to the directory of your project.
3. Run the following command:

   ```bash
   dotnet add package <PackageName> --version <VersionNumber>
   ```

   Replace `<VersionNumber>` with the desired version. If no version is specified, the latest version will be installed.
