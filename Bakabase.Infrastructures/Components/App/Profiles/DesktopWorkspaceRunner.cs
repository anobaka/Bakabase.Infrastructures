using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.App.Profiles;

public sealed record DesktopWorkspaceContext(string? ProfileId, string AnchorDirectory, bool EnvironmentOverride, string[] Arguments);

/// <summary>
/// Serializes desktop workspaces without changing an initialized business process's data root.
/// The old child fully exits before the next run starts. Failed/cancelled startup returns to the
/// prior workspace; the desktop coordinator and its user-level admission lease remain alive.
/// </summary>
public sealed class DesktopWorkspaceRunner
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private TaskCompletionSource<DesktopWorkspaceContext> _switch = NewRequest();
    private bool _ready;
    private bool _runWasReady;
    private DesktopWorkspaceContext? _prepared;
    private DateTimeOffset _preparedUntil;
    private bool _busy;
    private bool _switching;
    private bool _exitReserved;
    private Exception? _restoredError;
    public DesktopWorkspaceContext Current { get; private set; }
    public event Action<Exception>? RestoredAfterFailure;
    public DesktopWorkspaceRunner(DesktopWorkspaceContext initial, TimeProvider? clock = null)
    { Current = initial; _clock = clock ?? TimeProvider.System; }
    private static TaskCompletionSource<DesktopWorkspaceContext> NewRequest() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Ready()
    {
        Exception? error;
        lock (_gate)
        {
            _ready = true; _runWasReady = true; _busy = false; _switching = _prepared != null || _switch.Task.IsCompleted;
            error = _restoredError; _restoredError = null;
        }
        if (error != null) RestoredAfterFailure?.Invoke(error);
    }
    public void Unavailable() { lock (_gate) _ready = false; }
    public void Busy() { lock (_gate) _busy = true; }
    public void BeginSetup()
    {
        lock (_gate)
        {
            if (_prepared != null && _clock.GetUtcNow() > _preparedUntil)
            { _prepared = null; _switching = false; }
            if (_switching || _exitReserved) throw new InvalidOperationException("A workspace switch is already being prepared. Wait for it to finish before opening Setup.");
            _busy = true;
        }
    }

    public void PrepareSwitch(DesktopWorkspaceContext target, Action validate)
    {
        lock (_gate)
        {
            if (_prepared != null && _clock.GetUtcNow() > _preparedUntil)
            { _prepared = null; _switching = false; }
            if (!_ready || _busy || _switching || _exitReserved)
                throw new InvalidOperationException("Wait for startup, Setup, or the current workspace switch to finish before switching workspaces.");
            validate(); // The original workspace is still usable and no shutdown has begun.
            _prepared = target;
            _preparedUntil = _clock.GetUtcNow().AddSeconds(30);
            _switching = true;
        }
    }

    public Action PrepareCommit(string id)
    {
        lock (_gate)
        {
            if (_prepared?.ProfileId != id || _clock.GetUtcNow() > _preparedUntil)
                throw new InvalidOperationException("The workspace switch request expired. Please try again.");
            if (!_ready || _busy)
                throw new InvalidOperationException("Finish Setup or the current data operation before switching workspaces.");
            var target = _prepared;
            // Remain reserved while the IPC success reply is flushed. Setup cannot enter
            // between validation and the returned shutdown signal.
            return () =>
            {
                lock (_gate)
                {
                    if (!ReferenceEquals(_prepared, target)) return;
                    _prepared = null;
                    _switch.TrySetResult(target);
                }
            };
        }
    }

    public void ReserveRestart(bool release)
    {
        lock (_gate)
        {
            if (release) { _exitReserved = false; return; }
            if (_prepared != null && _clock.GetUtcNow() > _preparedUntil)
            { _prepared = null; _switching = false; }
            if (!_ready || _busy || _switching || _exitReserved)
                throw new InvalidOperationException("Wait for Setup or the workspace switch to finish before restarting or updating.");
            _exitReserved = true;
        }
    }

    public void CommitSwitch(string id) => PrepareCommit(id)();

    public void RequestSwitch(DesktopWorkspaceContext target, Action validate)
    { PrepareSwitch(target, validate); CommitSwitch(target.ProfileId!); }

    public async Task<int> RunAsync(Func<DesktopWorkspaceContext, CancellationToken, Task<int>> run,
        CancellationToken cancellationToken = default)
    {
        DesktopWorkspaceContext? fallback = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<int> running;
            try { running = run(Current, lifetime.Token); }
            catch (Exception error) { running = Task.FromException<int>(error); }
            var requested = _switch.Task;
            var completed = await Task.WhenAny(running, requested);
            if (completed == requested && !cancellationToken.IsCancellationRequested)
            {
                var target = await requested;
                var previous = Current;
                Unavailable();
                lifetime.Cancel();
                // A slow shutdown never grants permission to start a second database owner.
                try { await running; }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception error)
                {
                    // The run delegate has finished disposing its child. A shutdown error
                    // is not permission to proceed to the requested target: reopen the source.
                    if (cancellationToken.IsCancellationRequested) return 0;
                    lock (_gate)
                    {
                        _switch = NewRequest(); _ready = false; _runWasReady = false; _busy = false;
                        _restoredError = error;
                    }
                    fallback = null;
                    continue;
                }
                if (cancellationToken.IsCancellationRequested) return 0;
                lock (_gate) { _switch = NewRequest(); _ready = false; _runWasReady = false; _busy = false; }
                fallback = previous;
                Current = target;
                continue;
            }
            Exception? failure = null;
            var result = 0;
            try { result = await running; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 0; }
            catch (Exception error) { failure = error; }
            if (cancellationToken.IsCancellationRequested) return 0;
            bool ready;
            lock (_gate) ready = _runWasReady;
            if (fallback != null && !ready)
            {
                _restoredError = failure ?? new InvalidOperationException(result == 0
                    ? "The selected workspace was closed before startup completed. The previous workspace has been reopened."
                    : "The selected workspace could not start. The previous workspace has been reopened.");
                Current = fallback; fallback = null;
                lock (_gate) { _switch = NewRequest(); _ready = false; _runWasReady = false; _busy = false; }
                continue;
            }
            if (failure != null) throw failure;
            return result;
        }
        return 0;
    }
}
