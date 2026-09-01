using OptiGames.Core.Helpers;

namespace OptiGames.Core.Tweaks;

/// <summary>Shared services handed to every tweak.</summary>
public sealed class TweakContext
{
    public RegistryHelper Registry { get; }
    public ProcessRunner Process { get; }
    public ILogSink Log { get; }

    public TweakContext(ILogSink log)
    {
        Log = log;
        Registry = new RegistryHelper(log);
        Process = new ProcessRunner(log);
    }
}

/// <summary>Result of applying or reverting one tweak.</summary>
public sealed record TweakResult(Tweak Tweak, bool Success, string? Error = null);

/// <summary>
/// Applies and reverts tweaks. Every write is preceded by a read of the current state, so a
/// tweak that is already in the requested state costs nothing and logs nothing.
/// </summary>
public sealed class TweakEngine
{
    private readonly TweakContext _ctx;
    private readonly ILogSink _log;

    public TweakEngine(TweakContext ctx)
    {
        _ctx = ctx;
        _log = ctx.Log;
    }

    public TweakStatus GetStatus(Tweak tweak)
    {
        if (tweak.CustomStatus is not null) return tweak.CustomStatus(_ctx);

        var actions = tweak.RelevantActions.ToList();
        if (actions.Count == 0) return TweakStatus.NotApplied;

        int on = actions.Count(a => a.Matches(_ctx.Registry, a.On));
        if (on == actions.Count) return TweakStatus.Applied;
        return on == 0 ? TweakStatus.NotApplied : TweakStatus.Partial;
    }

    public TweakResult Apply(Tweak tweak) => Run(tweak, on: true);
    public TweakResult Revert(Tweak tweak) => Run(tweak, on: false);

    private TweakResult Run(Tweak tweak, bool on)
    {
        _log.Write($"{(on ? "Applying" : "Reverting")}: {tweak.Name}");
        try
        {
            foreach (var action in tweak.RelevantActions)
            {
                var target = on ? action.On : action.Off;
                if (action.Matches(_ctx.Registry, target)) continue;
                action.Write(_ctx.Registry, target);
            }

            var custom = on ? tweak.CustomApply : tweak.CustomRevert;
            custom?.Invoke(_ctx);

            _log.Write($"  done: {tweak.Name}");
            return new TweakResult(tweak, true);
        }
        catch (Exception ex)
        {
            _log.Write($"  FAILED: {tweak.Name} — {ex.Message}");
            return new TweakResult(tweak, false, ex.Message);
        }
    }

    /// <summary>
    /// Drives a whole staged batch, reporting progress as a 0..1 fraction so the Optimize
    /// page can animate. Never throws — a failing tweak is reported and the batch continues.
    /// </summary>
    public IReadOnlyList<TweakResult> RunBatch(
        IEnumerable<(Tweak Tweak, bool On)> batch,
        IProgress<(double Fraction, string Label)>? progress = null,
        CancellationToken cancel = default)
    {
        var items = batch.ToList();
        var results = new List<TweakResult>(items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var (tweak, on) = items[i];
            progress?.Report(((double)i / items.Count, tweak.Name));
            results.Add(Run(tweak, on));
        }

        progress?.Report((1.0, "Done"));
        return results;
    }
}
