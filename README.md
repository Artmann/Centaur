# Centaur

A fast, GPU-accelerated terminal emulator for Windows built with C# and Avalonia.

## Features

- **GPU-accelerated rendering** via SkiaSharp with HarfBuzz text shaping
- **Windows ConPTY** integration for native shell support (PowerShell, cmd, WSL)
- **VT100/ANSI escape sequence** parsing (cursor movement, colors, text attributes, scrolling)
- **Mouse text selection** with double-click word select and triple-click line select
- **Clipboard copy** with Ctrl+C
- **JetBrains Mono** embedded as the default font
- **Timer-based render coalescing** for smooth output at ~60 FPS

## Requirements

- Windows 10 version 1809 or later
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Getting Started

```bash
git clone <repo-url>
cd Centaur
dotnet build
dotnet run --project src/Centaur.App
```

## Running Tests

```bash
dotnet test
```

## License

[MIT](LICENSE)
