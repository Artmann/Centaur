# Centaur Project Guidelines

## Commit Messages

- Don't include Claude as the author in commit messages

## Avalonia Rendering

- Avalonia uses **immediate-mode rendering** - each frame receives a fresh canvas
- The canvas is NOT preserved between frames (starts cleared/transparent)
- Incremental/dirty-region rendering doesn't work with `ICustomDrawOperation.Render()` because unchanged areas won't be redrawn
- Always do full redraws in custom draw operations
- Timer-based update coalescing (batching multiple updates into one render) is still useful for reducing flicker from rapid PTY output
