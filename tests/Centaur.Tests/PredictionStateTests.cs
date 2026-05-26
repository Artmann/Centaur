using System.Diagnostics;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class PredictionStateTests
{
    const int columns = 80;

    static PredictionState Enabled()
    {
        var s = new PredictionState();
        s.SetEnabled(true);
        return s;
    }

    static long Ms(double ms) => (long)(ms / 1000.0 * Stopwatch.Frequency);

    [Fact]
    public void PredictType_AppendsCell_AndAdvancesCursor()
    {
        var s = Enabled();

        s.PredictType('h', row: 3, startCol: 10, columns);

        var snap = s.Read();
        Assert.True(s.HasPending);
        var cell = Assert.Single(snap.Cells);
        Assert.Equal(new PredictedCell(10, 3, 'h'), cell);
        Assert.True(snap.HasCursor);
        Assert.Equal(11, snap.CursorCol);
        Assert.Equal(3, snap.CursorRow);
    }

    [Fact]
    public void PredictType_ContinuesRun_AppendingFromPredictedCursor()
    {
        var s = Enabled();

        s.PredictType('h', 3, 10, columns);
        // Real cursor still at 10 (echo hasn't arrived); passing stale startCol must be ignored.
        s.PredictType('i', 3, 10, columns);

        var snap = s.Read();
        Assert.Equal(2, snap.Cells.Count);
        Assert.Equal(new PredictedCell(10, 3, 'h'), snap.Cells[0]);
        Assert.Equal(new PredictedCell(11, 3, 'i'), snap.Cells[1]);
        Assert.Equal(12, snap.CursorCol);
    }

    [Fact]
    public void PredictBackspace_RemovesLastCell_AndRetreatsCursor()
    {
        var s = Enabled();
        s.PredictType('h', 3, 10, columns);
        s.PredictType('i', 3, 10, columns);

        s.PredictBackspace(3);

        var snap = s.Read();
        var cell = Assert.Single(snap.Cells);
        Assert.Equal(new PredictedCell(10, 3, 'h'), cell);
        Assert.Equal(11, snap.CursorCol);
    }

    [Fact]
    public void PredictBackspace_AtPromptBoundary_Clears()
    {
        var s = Enabled();
        s.PredictType('h', 3, 10, columns); // base col 10, cursor 11

        s.PredictBackspace(3); // removes the only cell -> cursor back to base 10
        Assert.False(s.HasPending);

        // A further backspace has nothing of ours to erase -> stay cleared, predict nothing.
        s.PredictBackspace(3);
        Assert.False(s.HasPending);
    }

    [Fact]
    public void Disabled_IgnoresPredict()
    {
        var s = new PredictionState(); // not enabled

        s.PredictType('x', 1, 1, columns);

        Assert.False(s.HasPending);
        Assert.Empty(s.Read().Cells);
    }

    [Fact]
    public void SetEnabled_False_ClearsPending()
    {
        var s = Enabled();
        s.PredictType('x', 1, 1, columns);
        Assert.True(s.HasPending);

        s.SetEnabled(false);

        Assert.False(s.HasPending);
    }

    [Fact]
    public void Reconcile_ClearsOnCursorMatch()
    {
        var s = Enabled();
        s.PredictType('h', 3, 10, columns); // cursor predicted at 11

        s.Reconcile(realCol: 11, realRow: 3, altScreen: false, Stopwatch.GetTimestamp());

        Assert.False(s.HasPending);
    }

    [Fact]
    public void Reconcile_ClearsOnRowMismatch()
    {
        var s = Enabled();
        s.PredictType('h', 3, 10, columns);

        s.Reconcile(11, realRow: 5, altScreen: false, Stopwatch.GetTimestamp());

        Assert.False(s.HasPending);
    }

    [Fact]
    public void Reconcile_ClearsOnAltScreen()
    {
        var s = Enabled();
        s.PredictType('h', 3, 10, columns);

        s.Reconcile(10, 3, altScreen: true, Stopwatch.GetTimestamp());

        Assert.False(s.HasPending);
    }

    [Fact]
    public void Reconcile_NoEchoYet_KeepsPredictions()
    {
        var s = Enabled();
        s.PredictType('h', 3, 10, columns);
        s.PredictType('i', 3, 10, columns); // cursor predicted at 12, base 10

        // Real cursor still at the base — shell hasn't echoed anything yet.
        s.Reconcile(10, 3, false, Stopwatch.GetTimestamp());

        Assert.True(s.HasPending);
        Assert.Equal(2, s.Read().Cells.Count);
    }

    [Fact]
    public void Reconcile_PartialConfirm_TrimsConfirmedFromFront()
    {
        var s = Enabled();
        s.PredictType('h', 3, 10, columns);
        s.PredictType('i', 3, 10, columns); // cells at 10,11 ; cursor 12 ; base 10

        // Shell echoed one char: real cursor advanced to 11.
        s.Reconcile(11, 3, false, Stopwatch.GetTimestamp());

        var snap = s.Read();
        var cell = Assert.Single(snap.Cells);
        Assert.Equal(new PredictedCell(11, 3, 'i'), cell);
        Assert.Equal(12, snap.CursorCol); // predicted cursor unchanged
    }

    [Fact]
    public void Reconcile_Overshoot_Clears()
    {
        var s = Enabled();
        s.PredictType('h', 3, 10, columns); // cursor predicted 11

        // Autosuggest/completion pushed the real cursor past our prediction.
        s.Reconcile(20, 3, false, Stopwatch.GetTimestamp());

        Assert.False(s.HasPending);
    }

    [Fact]
    public void ExpireIfStale_ClearsAfterTimeout()
    {
        var s = Enabled();
        s.PredictType('h', 3, 10, columns);

        s.ExpireIfStale(Stopwatch.GetTimestamp() + Ms(500), staleMs: 120);

        Assert.False(s.HasPending);
    }

    [Fact]
    public void ExpireIfStale_KeepsWhenFresh()
    {
        var s = Enabled();
        s.PredictType('h', 3, 10, columns);

        s.ExpireIfStale(Stopwatch.GetTimestamp(), staleMs: 120);

        Assert.True(s.HasPending);
    }

    [Fact]
    public void PredictType_NearLineWrap_Clears()
    {
        var s = Enabled();

        // Last usable column would force a wrap — never predict it.
        s.PredictType('x', row: 0, startCol: columns - 1, columns);

        Assert.False(s.HasPending);
    }

    [Fact]
    public async Task Concurrent_PredictReconcileRead_DoNotThrowOrTear()
    {
        var s = Enabled();
        var tasks = new List<Task>();

        for (var t = 0; t < 8; t++)
        {
            tasks.Add(
                Task.Run(() =>
                {
                    for (var i = 0; i < 5000; i++)
                    {
                        s.PredictType('a', 2, 10, columns);
                        var snap = s.Read();
                        // Reading the snapshot must never observe a torn/invalid cursor.
                        Assert.True(snap.CursorCol >= 0);
                        s.Reconcile(10, 2, false, Stopwatch.GetTimestamp());
                        s.ExpireIfStale(Stopwatch.GetTimestamp(), 120);
                    }
                })
            );
        }

        await Task.WhenAll(tasks);
    }
}
