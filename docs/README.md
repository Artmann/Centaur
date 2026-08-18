# Centaur Documentation

All project documentation lives here — RFCs, feature specs and architecture reference
were previously split between a top-level `RFCs/` folder and `docs/specs/`, and are now
combined under this folder.

## Layout

| Folder                              | Contains                                                             |
| ----------------------------------- | -------------------------------------------------------------------- |
| [`architecture/`](architecture)     | How the code is put together today. Reference, kept in sync with `src`. |
| [`rfcs/`](rfcs)                     | Proposals: why we build something and the shape it should take.       |
| [`specs/`](specs)                   | Designs for a single feature, written before implementation.          |

## Architecture

- [Extensions & Providers](architecture/extensions-and-providers.md) — `ExtensionHost`,
  the `IExtension` / `IProvider` split, the typed event bus, and how features are wired
  into the app.

## RFCs

Numbered, `NNN-short-title.md`.

- [RFC-001: Centaur Terminal Emulator](rfcs/001-terminal-emulator.md)

## Specs

Dated, `YYYY-MM-DD-short-title.md`.

- [Reverse Search (Ctrl+R)](specs/2026-03-22-reverse-search-design.md)

## Writing a new document

- **RFC** when the decision is open — problem statement, goals and non-goals, options,
  risks. Take the next number in sequence.
- **Spec** when the decision is made and you need the implementation plan — behaviour,
  components, error handling, files to create and modify.
- **Architecture doc** when the code has settled and someone new needs the map. Describe
  what exists, not what is planned, and link the source files it documents.

Link new documents from this index.
