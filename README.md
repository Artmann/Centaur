<div align="center">

# Centaur

### A fast, GPU-accelerated terminal emulator for Windows

[![Build](https://github.com/Artmann/Centaur/actions/workflows/ci.yml/badge.svg)](https://github.com/Artmann/Centaur/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

---

Centaur is a modern terminal emulator built for speed. It uses GPU-accelerated rendering to keep your shell feeling snappy, even when scrolling through thousands of lines of build output or log files.

## Features

**Fast rendering** — SkiaSharp-powered GPU rendering with HarfBuzz text shaping delivers smooth, flicker-free output at 60 FPS.

**Native Windows shell support** — Works with PowerShell, cmd, and WSL out of the box via Windows ConPTY.

**Full terminal emulation** — VT100/ANSI escape sequences, 256 colors, text attributes, alternate screen buffer, and mouse reporting.

**Text selection** — Click and drag to select, double-click for words, triple-click for lines. Copy with Ctrl+C.

**Beautiful defaults** — Ships with JetBrains Mono and the Catppuccin color theme. Looks great without any configuration.

**Command suggestions** — Fish-style inline ghost text suggests commands from your history as you type. Press Tab to accept.

**Dynamic resize** — The terminal adapts seamlessly when you resize the window.

## Hotkeys

| Key | Action |
|-----|--------|
| Tab | Accept command suggestion |
| Ctrl+C | Copy selection (or send interrupt if no selection) |
| Ctrl+V | Paste from clipboard |
| Shift+Insert | Paste from clipboard |
| Shift+PageUp | Scroll up |
| Shift+PageDown | Scroll down |

## Requirements

- Windows 10 version 1809 or later

## Installation

Download the latest release from the [Releases](https://github.com/Artmann/Centaur/releases) page.

## Building from source

Requires [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
git clone https://github.com/Artmann/Centaur.git
cd Centaur
dotnet run --project src/Centaur.App
```

## License

[MIT](LICENSE) — Christoffer Artmann
