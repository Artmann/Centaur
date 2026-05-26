using System.Diagnostics;

namespace Centaur.Core.Terminal;

/// <summary>One speculatively-drawn character at an absolute grid position.</summary>
public readonly record struct PredictedCell(int col, int row, char character);

/// <summary>An immutable copy of the prediction state, safe to read on the render thread.</summary>
public readonly record struct PredictionSnapshot(
    IReadOnlyList<PredictedCell> Cells,
    int CursorCol,
    int CursorRow,
    bool HasCursor
);

/// <summary>
/// Mosh-style predictive local echo: when the user types, we speculatively record the glyphs
/// (and where the cursor should land) so the overlay can draw them the same frame, before the
/// shell echoes them back. The PTY read thread later <see cref="Reconcile"/>s against the real
/// cursor (ground truth) and the render thread sweeps stale predictions via
/// <see cref="ExpireIfStale"/>. Fully self-locked: written on the UI thread (predict) and the
/// PTY read thread (reconcile), read on the render thread (overlay). Never nest its lock with
/// the buffer lock — read buffer values first, then call in here.
/// </summary>
public class PredictionState
{
    readonly object syncLock = new();
    readonly List<PredictedCell> predictions = [];
    int baseCol;
    int baseRow;
    int predictedCursorCol;
    long lastUpdateTicks;
    bool enabled;

    public bool HasPending
    {
        get
        {
            lock (syncLock)
            {
                return predictions.Count > 0;
            }
        }
    }

    public void SetEnabled(bool value)
    {
        lock (syncLock)
        {
            enabled = value;
            if (!enabled)
            {
                ClearNoLock();
            }
        }
    }

    /// <summary>
    /// Predict a typed character on <paramref name="row"/>. The first prediction of a run anchors
    /// at <paramref name="startCol"/> (the real cursor column); subsequent ones continue from the
    /// predicted cursor and ignore <paramref name="startCol"/>, since the real cursor lags behind
    /// while the echo is in flight. Never predicts into the last column (would force a line wrap).
    /// </summary>
    public void PredictType(char character, int row, int startCol, int columns)
    {
        lock (syncLock)
        {
            if (!enabled)
            {
                return;
            }

            if (predictions.Count == 0)
            {
                baseRow = row;
                baseCol = startCol;
                predictedCursorCol = startCol;
            }
            else if (row != baseRow)
            {
                ClearNoLock();
                return;
            }

            // Refuse to predict at the last column — typing there triggers wrap behavior we can't
            // safely model without the buffer.
            if (predictedCursorCol >= columns - 1)
            {
                ClearNoLock();
                return;
            }

            predictions.Add(new PredictedCell(predictedCursorCol, baseRow, character));
            predictedCursorCol++;
            lastUpdateTicks = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// Predict a backspace by erasing the most recent predicted glyph. Only operates inside an
    /// active run on the same row; at the prompt boundary (nothing of ours left to erase) it
    /// clears instead of guessing at a real character's position.
    /// </summary>
    public void PredictBackspace(int row)
    {
        lock (syncLock)
        {
            if (!enabled || predictions.Count == 0)
            {
                return;
            }

            if (row != baseRow || predictedCursorCol <= baseCol)
            {
                ClearNoLock();
                return;
            }

            predictedCursorCol--;
            predictions.RemoveAt(predictions.Count - 1);
            lastUpdateTicks = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// Reconcile against the real cursor after the shell's output is parsed. The real cursor is
    /// ground truth: confirmed predictions are trimmed from the front, and any inconsistency
    /// (row jump, overshoot, full confirmation, alt screen) clears the whole run.
    /// </summary>
    public void Reconcile(int realCol, int realRow, bool altScreen, long nowTicks)
    {
        lock (syncLock)
        {
            if (predictions.Count == 0)
            {
                return;
            }

            if (altScreen || realRow != baseRow || realCol < baseCol)
            {
                ClearNoLock();
                return;
            }

            if (realCol == baseCol)
            {
                // Shell hasn't echoed anything yet — keep waiting.
                return;
            }

            if (realCol >= predictedCursorCol)
            {
                // Fully confirmed (==) or overshot by autosuggest/completion (>) — done either way.
                ClearNoLock();
                return;
            }

            // Partial confirm: the shell echoed (realCol - baseCol) chars. Drop those from the
            // front; the remaining predictions stay ahead of the real cursor.
            var confirmed = realCol - baseCol;
            predictions.RemoveRange(0, confirmed);
            baseCol = realCol;
            lastUpdateTicks = nowTicks;
        }
    }

    /// <summary>Clear predictions that have lingered longer than <paramref name="staleMs"/> without
    /// confirmation — a wrong prediction self-corrects within a frame or two of the render loop.</summary>
    public void ExpireIfStale(long nowTicks, double staleMs)
    {
        lock (syncLock)
        {
            if (predictions.Count == 0)
            {
                return;
            }

            if (Stopwatch.GetElapsedTime(lastUpdateTicks, nowTicks).TotalMilliseconds > staleMs)
            {
                ClearNoLock();
            }
        }
    }

    public PredictionSnapshot Read()
    {
        lock (syncLock)
        {
            return new PredictionSnapshot(
                predictions.ToArray(),
                predictedCursorCol,
                baseRow,
                predictions.Count > 0
            );
        }
    }

    public void ClearAll()
    {
        lock (syncLock)
        {
            ClearNoLock();
        }
    }

    void ClearNoLock()
    {
        predictions.Clear();
    }
}
