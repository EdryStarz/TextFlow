# TextFlow

Fast, portable text snippets for Windows.

TextFlow is a native Windows utility for saving reusable text, assigning global
hotkeys, and pasting Unicode content into any application. It also includes a
Spotlight-style search palette and an importer for QuickTextPaste data.

## Highlights

- Global hotkeys without polling
- Unicode-safe clipboard paste with clipboard restoration
- Quick search palette
- File and application shortcuts
- QuickTextPaste `.ini` import
- Portable JSON storage beside the executable
- Single-file, self-contained Windows build
- No third-party NuGet dependencies

## Stack

- C# and .NET 8
- WPF
- Win32 APIs via P/Invoke (`RegisterHotKey`, clipboard, `SendInput`)

## Project structure

```text
TextFlow/
  Models/       snippet data model
  Native/       Win32 declarations
  Services/     hotkeys, paste, storage and import
  ViewModels/   UI state and search
  Assets/       application icon
TextFlow.Tests/
  Program.cs    lightweight behavior checks
```

## Run from source

Requirements: Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project .\TextFlow\TextFlow.csproj
```

## Test

```powershell
dotnet run --project .\TextFlow.Tests\TextFlow.Tests.csproj
```

## Build a portable executable

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\TextFlow\build-portable.ps1
```

The build script produces a self-contained `win-x64` executable. User snippets
are intentionally excluded from this repository.

