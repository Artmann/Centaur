# Contributing to Centaur

## Project Structure

```
src/
  Centaur.Core/          Cross-platform core (VT parser, screen buffer, PTY abstraction)
  Centaur.Pty.Windows/   Windows ConPTY implementation via P/Invoke
  Centaur.Rendering/     SkiaSharp-based GPU rendering, text selection utilities
  Centaur.App/           Avalonia application entry point and terminal control
tests/
  Centaur.Tests/         Unit tests (xUnit)
```

## Building and Testing

```bash
dotnet build
dotnet test
dotnet run --project src/Centaur.App
```

## Formatting and Linting

This project uses [CSharpier](https://csharpier.com/) for formatting and Roslyn Analyzers for linting.

```bash
dotnet csharpier format .          # Format all files
dotnet csharpier check .           # Check formatting without writing changes
dotnet build                       # Roslyn analyzers run as part of the build
```

CSharpier is installed as a local dotnet tool. Run `dotnet tool restore` after cloning to install it.

## Architecture

- **Rendering**: Avalonia uses immediate-mode rendering. Each frame receives a fresh canvas that is not preserved between frames. Custom draw operations must always do full redraws. Timer-based update coalescing (~16ms) batches rapid PTY output into single renders.
- **PTY**: `ConPtyConnection` wraps the Windows ConPTY API. The `IPtyConnection` interface allows future platform implementations. PTY reads happen on a background thread; buffer updates are protected by `bufferLock`.
- **VT Parser**: State-machine parser (`VtParser`) handles ANSI/VT100 escape sequences. States: Ground, Escape, CSI, CsiParam.
- **Screen Buffer**: Flat `Cell[]` array indexed as `y * columns + x` for cache locality. Each cell stores character, foreground color, and background color.

## Conventions

- Don't include Co-Authored-By lines in commit messages
- Keep rendering logic as full redraws (no incremental/dirty-region rendering)
- Target .NET 9.0

## How to Contribute

1. Fork the repository
2. Create a feature branch (`git checkout -b my-feature`)
3. Make your changes and add tests
4. Run `dotnet test` to verify
5. Commit and push your branch
6. Open a pull request
