# Centaur Documentation

All project documentation lives in this folder — architecture reference, RFCs and feature
specs, previously split between a top-level `RFCs/` folder and `docs/specs/`.

## Naming

Every document is named `<type>-<identifier>-<slug>.md`, so the folder sorts by type and
a filename says what kind of document it is:

| Type           | Pattern                       | Example                                    |
| -------------- | ----------------------------- | ------------------------------------------ |
| `architecture` | `architecture-<slug>.md`      | `architecture-extensions-and-providers.md` |
| `rfc`          | `rfc-<NNN>-<slug>.md`         | `rfc-001-terminal-emulator.md`             |
| `spec`         | `spec-<YYYY-MM-DD>-<slug>.md` | `spec-2026-03-22-reverse-search.md`        |

RFCs are numbered in sequence, specs are dated by when they were written, and
architecture docs are named for their topic. Each document's title follows the same
shape: `# Type: Title`.

## Architecture

How the code is put together today. Reference, kept in sync with `src`.

- [Architecture: Extensions & Providers](architecture-extensions-and-providers.md) — `ExtensionHost`, the
  `IExtension` / `IProvider` split, the typed event bus, and how features are wired into
  the app.

## RFCs

Proposals: why we build something and the shape it should take. Numbered,
`NNN-short-title.md`.

- [RFC-001: Centaur Terminal Emulator](rfc-001-terminal-emulator.md)

## Specs

Designs for a single feature, written before implementation. Dated,
`YYYY-MM-DD-short-title.md`.

- [Spec: Reverse Search (Ctrl+R)](spec-2026-03-22-reverse-search.md)

## Writing a new document

- **RFC** when the decision is open — problem statement, goals and non-goals, options,
  risks. Take the next number in sequence.
- **Spec** when the decision is made and you need the implementation plan — behaviour,
  components, error handling, files to create and modify.
- **Architecture doc** when the code has settled and someone new needs the map. Describe
  what exists, not what is planned, and link the source files it documents.

Link new documents from this index.
