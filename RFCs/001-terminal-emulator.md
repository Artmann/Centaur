# RFC-001: Centaur Terminal Emulator

> *A fast, GPU-accelerated terminal for Windows (and eventually macOS)*

## 1. Problem Statement

Existing Windows terminals feel heavy and sluggish. Windows Terminal improved things but still carries Visual Studio energy—dense UI, slow startup, corporate feel. Terminals like Ghostty prove that a terminal can feel instant and delightful, but Ghostty doesn't support Windows.

**Goal**: Build a terminal that feels like Ghostty or the responsiveness of Zed—light, fast, beautiful—but native to Windows with a path to macOS.

## 2. Goals & Non-Goals

### Goals

- Sub-100ms startup time
- GPU-accelerated rendering at 120+ FPS
- PowerShell support with full VT sequence handling
- Clean, minimal UI with good defaults
- Tabs (no split panes in MVP)
- Themeable (dark theme ships first)
- Cross-platform architecture (Windows first, macOS later)

### Non-Goals (for MVP)

- Split panes
- Session persistence
- Plugin system
- Settings UI (config file is fine)
- WSL / SSH support
- Ligatures (nice-to-have later)

## 3. Technical Architecture

### 3.1 UI Framework: Avalonia

**Why Avalonia over WPF/WinUI:**

- True cross-platform (Windows, macOS, Linux)
- GPU-accelerated via Skia
- Modern, actively maintained
- Feels lighter than WPF
- XAML-based (familiar if you know WPF)

**Tradeoff**: Slightly less native feel than WinUI, but the cross-platform story is worth it.

### 3.2 High-Level Component Diagram

```
┌─────────────────────────────────────────────────────────┐
│                     Centaur Terminal                     │
├─────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────┐    │
│  │                   UI Layer                       │    │
│  │            (Avalonia + SkiaSharp)               │    │
│  │  ┌─────────┐  ┌─────────┐  ┌────────────────┐  │    │
│  │  │ Tab Bar │  │ Themes  │  │ Terminal View  │  │    │
│  │  └─────────┘  └─────────┘  └────────────────┘  │    │
│  └─────────────────────┬───────────────────────────┘    │
│                        │                                 │
│  ┌─────────────────────▼───────────────────────────┐    │
│  │              Terminal Emulation Core             │    │
│  │  ┌──────────────┐  ┌─────────────────────────┐  │    │
│  │  │ VT Parser    │  │ Screen Buffer           │  │    │
│  │  │ (ANSI/VT100) │  │ (cells, attrs, cursor)  │  │    │
│  │  └──────────────┘  └─────────────────────────┘  │    │
│  └─────────────────────┬───────────────────────────┘    │
│                        │                                 │
│  ┌─────────────────────▼───────────────────────────┐    │
│  │                  PTY Layer                       │    │
│  │  ┌─────────────────┐  ┌─────────────────────┐   │    │
│  │  │ ConPTY (Win)    │  │ POSIX PTY (macOS)   │   │    │
│  │  └─────────────────┘  └─────────────────────┘   │    │
│  └─────────────────────┬───────────────────────────┘    │
│                        │                                 │
│                        ▼                                 │
│              ┌──────────────────┐                        │
│              │  Shell Process   │                        │
│              │  (PowerShell)    │                        │
│              └──────────────────┘                        │
└─────────────────────────────────────────────────────────┘
```

### 3.3 Core Components

#### PTY Layer (Platform Abstraction)

The PTY (pseudo-terminal) is the bridge between your terminal UI and the shell. It handles:

- Spawning shell processes
- Bidirectional I/O (stdin/stdout)
- Terminal size negotiation
- Signal handling

**Windows: ConPTY**

```
ConPTY (Windows Pseudo Console) - Added in Windows 10 1809

Your Terminal ←→ ConPTY ←→ PowerShell
     │              │            │
     │    Creates a "fake"       │
     │    terminal that the      │
     │    shell thinks is real   │
     │              │            │
     └──── VT sequences ─────────┘
```

ConPTY is essential because:

- PowerShell and CMD expect to talk to a "real" terminal
- It translates Windows console APIs to VT sequences
- Without it, you can't run interactive programs (vim, less, etc.)
- It's what Windows Terminal uses

**Interface design:**

```csharp
public interface IPtyConnection : IAsyncDisposable
{
    Task<bool> StartAsync(PtyOptions options);
    Task WriteAsync(ReadOnlyMemory<byte> data);
    event Action<ReadOnlyMemory<byte>> DataReceived;
    event Action<int> ProcessExited;
    void Resize(int columns, int rows);
}

public record PtyOptions(
    string ExecutablePath,
    string[] Arguments,
    string WorkingDirectory,
    int InitialColumns,
    int InitialRows,
    Dictionary<string, string>? Environment
);
```

#### VT Parser (Terminal Emulation)

Parses ANSI/VT escape sequences from shell output and updates the screen buffer.

**What it handles:**

- Cursor movement (`\e[H`, `\e[10;5H`)
- Colors (`\e[31m` = red, `\e[38;2;255;100;0m` = RGB)
- Text attributes (bold, underline, italic)
- Screen manipulation (clear, scroll)
- Mouse reporting (if enabled later)

**State machine approach:**

```
Input bytes → State Machine → Actions → Screen Buffer
                   │
                   ├── Ground (normal text)
                   ├── Escape (got \e)
                   ├── CSI Entry (got \e[)
                   ├── CSI Param (parsing numbers)
                   └── ... other states
```

Consider using a library like `Vt100.net` or porting from a reference implementation rather than writing from scratch—VT parsing has many edge cases.

#### Screen Buffer

Stores the terminal state: a grid of cells plus metadata.

```csharp
public record struct Cell(
    char Character,
    Color Foreground,
    Color Background,
    CellAttributes Attributes
);

[Flags]
public enum CellAttributes : byte
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8,
    Inverse = 16
}

public class ScreenBuffer
{
    private Cell[] _cells;
    public int Columns { get; }
    public int Rows { get; }
    public int CursorX { get; set; }
    public int CursorY { get; set; }
    public int ScrollbackLines { get; }
    
    // Ring buffer for scrollback efficiency
}
```

**Performance considerations:**

- Use a flat array, not `Cell[,]`—better cache locality
- Ring buffer for scrollback (avoid copying on scroll)
- Dirty region tracking (only re-render changed areas)

#### GPU Renderer

Renders the screen buffer to the display using SkiaSharp (Skia bindings for .NET).

**Rendering strategy:**

```
Screen Buffer → Texture Atlas → GPU Draw Calls → Display
                    │
         Pre-rendered glyphs
         for each font/size/style
```

**Key optimizations:**

1. **Glyph atlas**: Pre-render characters to a texture, draw by sampling
2. **Batching**: One draw call for all cells with same attributes
3. **Dirty rectangles**: Only redraw changed regions
4. **Double buffering**: Prevent tearing

```csharp
public class TerminalRenderer
{
    private readonly GlyphAtlas _atlas;
    private SKSurface _surface;
    
    public void Render(ScreenBuffer buffer, SKCanvas canvas)
    {
        // 1. Clear background (or only dirty regions)
        // 2. Batch cells by attribute
        // 3. Draw background colors
        // 4. Draw glyphs from atlas
        // 5. Draw cursor
    }
}
```

### 3.4 Threading Model

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   UI Thread     │     │   PTY Thread    │     │  Render Thread  │
│                 │     │                 │     │   (optional)    │
│  Input handling │     │  Read from PTY  │     │                 │
│  Event dispatch │────▶│  Parse VT seqs  │────▶│  GPU rendering  │
│                 │     │  Update buffer  │     │                 │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

- **UI Thread**: Handles keyboard/mouse, dispatches to PTY
- **PTY Thread**: Reads shell output, parses VT, updates buffer
- **Render Thread**: Optional—can render on UI thread if fast enough

Use channels or a lock-free queue between threads for communication.

### 3.5 Input Handling

```
Keyboard Event → Translate to VT → Write to PTY

Examples:
  Enter      → \r
  Backspace  → \x7f
  Arrow Up   → \e[A
  Ctrl+C     → \x03
  Ctrl+V     → (paste from clipboard, write text to PTY)
```

## 4. Project Structure

```
Centaur/
├── src/
│   ├── Centaur.Core/              # Cross-platform core
│   │   ├── Pty/
│   │   │   ├── IPtyConnection.cs
│   │   │   ├── PtyOptions.cs
│   │   │   └── ...
│   │   ├── Terminal/
│   │   │   ├── VtParser.cs
│   │   │   ├── ScreenBuffer.cs
│   │   │   ├── Cell.cs
│   │   │   └── ...
│   │   └── Centaur.Core.csproj
│   │
│   ├── Centaur.Pty.Windows/       # ConPTY implementation
│   │   ├── ConPtyConnection.cs
│   │   ├── NativeMethods.cs       # P/Invoke
│   │   └── Centaur.Pty.Windows.csproj
│   │
│   ├── Centaur.Pty.Unix/          # POSIX PTY (future)
│   │   └── ...
│   │
│   ├── Centaur.Rendering/         # GPU rendering
│   │   ├── GlyphAtlas.cs
│   │   ├── TerminalRenderer.cs
│   │   └── Centaur.Rendering.csproj
│   │
│   └── Centaur.App/               # Avalonia application
│       ├── Views/
│       │   ├── MainWindow.axaml
│       │   ├── TerminalView.axaml
│       │   └── TabBar.axaml
│       ├── ViewModels/
│       ├── Themes/
│       ├── App.axaml
│       ├── Program.cs
│       └── Centaur.App.csproj
│
├── tests/
│   ├── Centaur.Core.Tests/
│   └── ...
│
└── Centaur.sln
```

## 5. Configuration

Settings use JSONC (JSON with comments) for familiarity and editor support.

**Location**: `%APPDATA%\Centaur\settings.jsonc`

```jsonc
{
  // Font settings
  "font": {
    "family": "JetBrains Mono",
    "size": 14,
    "lineHeight": 1.2
  },
  
  // Theme
  "theme": "dark",
  
  // Shell configuration
  "shell": {
    "windows": "pwsh.exe",
    "macOS": "/bin/zsh"
  },
  
  // Window
  "window": {
    "opacity": 1.0,
    "padding": 12
  }
}
```

## 6. MVP Scope

### Must Have

1. **Launch**: Open window, spawn PowerShell via ConPTY
2. **I/O**: Type commands, see output with colors
3. **VT Parsing**: Basic sequences (colors, cursor, clear)
4. **Rendering**: GPU-accelerated text at 60+ FPS
5. **Resize**: Handle window resize, update PTY
6. **Tabs**: Multiple terminal instances in tabs
7. **Theme**: One good dark theme

### Won't Have (Yet)

- Settings UI
- Multiple themes
- Font selection in UI
- Split panes
- Search
- Copy/paste UI (basic clipboard works)
- Scrollback (add after MVP)

## 7. Dependencies

| Dependency | Purpose |
|------------|---------|
| Avalonia | UI framework |
| SkiaSharp | GPU rendering |
| SkiaSharp.HarfBuzz | Advanced font shaping (ligatures, kerning) |
| System.IO.Pipelines | Efficient PTY I/O |
| Microsoft.Windows.CsWin32 | ConPTY P/Invoke generation |

Consider for VT parsing:

- Write custom (more control, learning opportunity)
- Port from existing (Ghostty's parser is MIT, but in Zig)

## 8. Risks & Open Questions

| Risk | Mitigation |
|------|------------|
| ConPTY complexity | Start with reference implementations (Windows Terminal is open source) |
| VT parsing edge cases | Extensive testing with real shells, vttest |
| Performance on large output | Dirty rect rendering, flow control |
| Avalonia maturity | Active community, good docs, acceptable risk |

**Open questions:**

1. Font rendering: SkiaSharp.HarfBuzz should handle shaping—verify ligature support
2. Should scrollback be in MVP? Might be expected behavior

## 9. Success Metrics

For personal use, you'll *feel* success, but concrete markers:

- Cold start under 200ms (target 100ms)
- Smooth scrolling at 120 FPS
- `cat large_file.txt` doesn't hang the UI
- Running Claude Code works correctly (streaming output, colors, interactive prompts)

## 10. References

- [Windows Terminal source](https://github.com/microsoft/terminal) - ConPTY reference
- [Ghostty](https://ghostty.org/) - Design inspiration
- [Avalonia docs](https://docs.avaloniaui.net/)
- [ConPTY documentation](https://docs.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session)
- [VT100 escape sequences](https://vt100.net/docs/vt100-ug/chapter3.html)
