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

This project uses [CSharpier](https://csharpier.com/) for formatting, Roslyn Analyzers for linting, and [roe](https://github.com/Artmann/roe) for codebase intelligence (dead code, duplication, complexity).

```bash
dotnet csharpier format .          # Format all files
dotnet csharpier check .           # Check formatting without writing changes
dotnet build                       # Roslyn analyzers run as part of the build
dotnet roe .                       # Dead code, duplication and health checks
```

Individual roe analyses:

```bash
dotnet roe dead-code .             # Unreferenced files, types and members
dotnet roe dupes .                 # Copy-pasted blocks
dotnet roe health .                # Complexity, method/file/type size
dotnet roe health . --hotspots     # Rank files by complexity x recent churn
```

roe exits non-zero on any finding and runs in CI. False positives are suppressed in `roe.json` at the
repo root - prefer a scoped `deadCode.ignore` entry with a comment in the PR over a top-level `ignore`,
so the file keeps its duplication and health coverage.

`health.maxTypeMembers` is raised to 40, above roe's default of 20. Two types sit legitimately above
the default and neither decomposes further without harm: `TerminalControl` is an Avalonia `Control`
whose ten framework overrides (arrange, attach/detach, the pointer and keyboard handlers, render) have
to live on the control itself, and `VtParser` is the dispatch table for the VT escape sequences, where
one method per sequence family is the point. Everything else about them - the buffers, the tokenizer,
the selection, the clipboard, the key encoding - has been split into collaborators. Raising this one
threshold keeps every other health check at its default rather than exempting the two files wholesale
via `health.ignore`, which would also drop their complexity and method-length coverage.

CSharpier and roe are installed as local dotnet tools. Run `dotnet tool restore` after cloning to install them.

## Architecture

Full documentation lives in [`docs/`](docs) — architecture reference, RFCs and feature specs.
Start with [Extensions & Providers](docs/architecture-extensions-and-providers.md) for how
features are composed via `ExtensionHost`.

- **Extensions & Providers**: Features plug in as `IExtension` (lifecycle: activate/dispose,
  subscribes to events) or `IProvider` (passive capability, resolved by priority), both managed
  by `ExtensionHost` and wired up in `App.ConfigureServices()`.
- **Rendering**: Avalonia uses immediate-mode rendering. Each frame receives a fresh canvas that is not preserved between frames. Custom draw operations must always do full redraws. Timer-based update coalescing (~16ms) batches rapid PTY output into single renders.
- **PTY**: `ConPtyConnection` wraps the Windows ConPTY API. The `IPtyConnection` interface allows future platform implementations. PTY reads happen on a background thread; buffer updates are protected by `bufferLock`.
- **VT Parser**: State-machine parser (`VtParser`) handles ANSI/VT100 escape sequences. States: Ground, Escape, CSI, CsiParam.
- **Screen Buffer**: Flat `Cell[]` array indexed as `y * columns + x` for cache locality. Each cell stores character, foreground color, and background color.

## Conventions

- Don't include Co-Authored-By lines in commit messages
- Keep rendering logic as full redraws (no incremental/dirty-region rendering)
- Target .NET 9.0

### Commit Messages

Commits follow [Conventional Commits](https://www.conventionalcommits.org/), because release-please
derives both the version bump and the changelog from them:

```
<type>(<optional scope>): <subject>

feat(tabs): give the tab strip room, and a double-click to rename
fix(vt): save the whole pen across DECSC/DECRC, not just the colours
```

Keep the prose subject style the repo already uses - the prefix goes in front of it, it doesn't
replace it. Types in use: `feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `build`, `ci`, `chore`.
`feat` bumps the minor, `fix` and `perf` bump the patch, and a `!` after the type or a
`BREAKING CHANGE:` footer bumps the major.

Nothing in CI checks this. A commit with no recognised prefix is skipped by release-please entirely -
it neither bumps the version nor shows up in [`CHANGELOG.md`](CHANGELOG.md).

## Documentation

Everything lives flat in [`docs/`](docs), named `<type>-<identifier>-<slug>.md`:

- `architecture-<slug>.md` — how the code is put together today
- `rfc-<NNN>-<slug>.md` — proposals
- `spec-<YYYY-MM-DD>-<slug>.md` — per-feature designs

## How to Contribute

1. Fork the repository
2. Create a feature branch (`git checkout -b my-feature`)
3. Make your changes and add tests
4. Run `dotnet test` to verify
5. Commit and push your branch
6. Open a pull request

## Releases

Releases are automated with [release-please](https://github.com/googleapis/release-please) - nothing
is tagged by hand.

Once conventional commits land on `main`, release-please keeps a `chore(main): release x.y.z` pull
request open, accumulating them into a changelog entry and bumping `<Version>` in
`Directory.Build.props` (the line carrying the `x-release-please-version` comment). Merging that pull
request tags the release, publishes the GitHub Release, and builds and attaches the self-contained
win-x64 exe.

So: to cut a release, merge the release pull request. To hold one back, leave it open.
