using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.App.SingleInstance
{
    /// <summary>What <see cref="SingleInstanceGuard.Enter"/> decided.</summary>
    public enum SingleInstanceEntry
    {
        /// <summary>This process owns the data directory and may use it.</summary>
        Entered = 1,

        /// <summary>
        /// Another process owns it. The owner has been asked to show its window (when it could
        /// be reached); this process must exit without touching the directory.
        /// </summary>
        Refused = 2,

        /// <summary>
        /// The lock could not be taken for a reason that says nothing about other instances.
        /// The process carries on unguarded rather than refusing to start — see
        /// <see cref="DataDirectoryLock"/>.
        /// </summary>
        Unguarded = 3,

        /// <summary>This build does not guard (a development run, Docker).</summary>
        NotApplicable = 4,
    }

    /// <summary>
    /// One running instance per data directory: the process-wide owner of the
    /// <see cref="DataDirectoryLock"/>s this process holds and of the
    /// <see cref="ActivationServer"/> that answers each one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why per data directory.</b> The database, the in-memory caches, the option files and
    /// the background tasks all assume they have a single owner; two processes on one directory
    /// split-brain (each sees only its own writes) and repeat each other's work. Two processes
    /// on <i>different</i> directories share nothing, so there is nothing to refuse — that is
    /// the <c>BAKABASE_DATA_DIR</c> escape hatch, and it keeps working.
    /// </para>
    /// <para>
    /// <b>When.</b> The entry point calls <see cref="EnterOrHandOff()"/> before anything else
    /// touches the data directory: before the static constructor that opens the log file, the
    /// pending relocation, the tray icon. A refused launch therefore leaves no trace — no log
    /// file, no relocation, no icon — beyond the line it prints to stderr.
    /// <see cref="AppHost"/> calls <see cref="Enter"/> again as a backstop for any entry point
    /// that did not; for one that did, it is a no-op.
    /// </para>
    /// <para>
    /// <b>Relocation.</b> Moving the data to a new directory holds both locks for the whole
    /// move — the source because it is still the effective directory until the pointer flips,
    /// the target because it is about to be, and a launch naming it through
    /// <c>BAKABASE_DATA_DIR</c> must not start on a half-copied database. Each lock answers its
    /// own activation channel, so a launch resolving either directory mid-move reaches this
    /// process. The source is released only after the runner has emptied it; if the move does
    /// not happen, the target is released and put back as it was. See
    /// <see cref="AcquireAdditional"/>, <see cref="Promote"/>, <see cref="Retire"/> and
    /// <see cref="Abandon"/>.
    /// </para>
    /// </remarks>
    public static class SingleInstanceGuard
    {
        /// <summary>How long a refused launch waits for the owner to take its message.</summary>
        public static readonly TimeSpan HandOffTimeout = TimeSpan.FromSeconds(3);

        private static readonly Lock Gate = new();

        /// <summary>
        /// Serialises taking locks. Two threads of this process racing for one directory would
        /// otherwise see each other's lock as another process's, and hand off to themselves.
        /// </summary>
        private static readonly Lock AcquireGate = new();

        private static readonly Dictionary<string, Lease> Leases = new(StringComparer.Ordinal);
        private static string? _primaryKey;
        private static Action? _activationHandler;
        private static bool _activationPending;
        private static string? _lastProblem;

        private sealed class Lease(string directory, DataDirectoryLock @lock, ActivationServer? server)
        {
            public string Directory { get; } = directory;
            public DataDirectoryLock Lock { get; } = @lock;
            public ActivationServer? Server { get; } = server;

            public void Release()
            {
                Server?.Dispose();
                Lock.Dispose();
            }
        }

        /// <summary>The data directory this process owns, as it was given; null when none.</summary>
        public static string? PrimaryDirectory
        {
            get
            {
                lock (Gate)
                {
                    return _primaryKey != null && Leases.TryGetValue(_primaryKey, out var lease)
                        ? lease.Directory
                        : null;
                }
            }
        }

        /// <summary>
        /// For the entry point: owns this process's effective data directory, or hands off to
        /// the process that does.
        /// </summary>
        /// <returns>
        /// False when another instance owns the directory — the caller must return from
        /// <c>Main</c> at once. True otherwise, including when this build does not guard.
        /// </returns>
        /// <remarks>
        /// Safe to call again: after the <see cref="AppService"/> static constructor has run,
        /// a legacy layout may have been converted to a redirect, moving the effective
        /// directory. A second call takes the new directory's lock (and lets go of the old
        /// one); when nothing moved it does nothing.
        /// </remarks>
        public static bool EnterOrHandOff() =>
            EnterOrHandOff(AppRuntime.IsPackagedDesktop, () => AppDataLocator.ResolveEffectiveDataDirectory());

        internal static bool EnterOrHandOff(bool enabled, Func<string> resolveDataDirectory)
        {
            if (!enabled)
            {
                return true;
            }

            string dataDirectory;
            try
            {
                dataDirectory = resolveDataDirectory();
            }
            catch (Exception e)
            {
                // A corrupt redirect, say. Startup reports that itself, into a log file, a
                // moment later; failing here first would lose the report and change nothing.
                NoteProblem($"Could not work out the data directory: {e.Message}");
                return true;
            }

            return Enter(dataDirectory) != SingleInstanceEntry.Refused;
        }

        /// <summary>
        /// Makes <paramref name="dataDirectory"/> the directory this process owns, taking its
        /// lock unless already held. When another process holds it, asks that process to show
        /// its window and returns <see cref="SingleInstanceEntry.Refused"/>.
        /// </summary>
        public static SingleInstanceEntry Enter(string dataDirectory)
        {
            if (!TryNormalize(dataDirectory, out var key))
            {
                return SingleInstanceEntry.Unguarded;
            }

            var (status, problem) = Acquire(dataDirectory, key);
            var delivered = false;
            if (status == DataDirectoryLockStatus.HeldByAnotherProcess)
            {
                // On the pool: the backstop call in AppHost runs on the UI thread, and a
                // continuation posted back to a thread blocked here would never run.
                var channel = ActivationChannel.GetName(key);
                delivered = Task.Run(() => ActivationChannel.TrySendAsync(channel,
                    ActivationChannel.ShowMessage, HandOffTimeout)).GetAwaiter().GetResult();

                if (!delivered)
                {
                    // Nobody answered. The holder may have been on its way out (it lets go
                    // before the process ends), or — on Windows — not an instance at all but
                    // something that briefly opened the file, like a virus scanner. Look once
                    // more before turning the user away.
                    (status, problem) = Acquire(dataDirectory, key);
                }
            }

            switch (status)
            {
                case DataDirectoryLockStatus.Acquired:
                    Lease? previous = null;
                    lock (Gate)
                    {
                        if (_primaryKey != null && _primaryKey != key)
                        {
                            Leases.Remove(_primaryKey, out previous);
                        }

                        _primaryKey = key;
                    }

                    previous?.Release();
                    return SingleInstanceEntry.Entered;

                case DataDirectoryLockStatus.HeldByAnotherProcess:
                    WriteStderr(delivered
                        ? $"Bakabase is already running for {dataDirectory}; asked it to show its window."
                        : $"Bakabase is already running for {dataDirectory}, and did not answer; exiting.");
                    return SingleInstanceEntry.Refused;

                default:
                    NoteProblem($"Could not lock {dataDirectory}: {problem?.Message}");
                    return SingleInstanceEntry.Unguarded;
            }
        }

        /// <summary>
        /// Takes <paramref name="dataDirectory"/>'s lock as well as the one already owned — the
        /// target of a relocation, before anything is copied into it. Does not hand off: a
        /// target owned by someone else means the move must wait, not that this process should
        /// exit.
        /// </summary>
        /// <returns>
        /// <see cref="SingleInstanceEntry.Entered"/> when held (including already),
        /// <see cref="SingleInstanceEntry.Refused"/> when another process has it,
        /// <see cref="SingleInstanceEntry.Unguarded"/> when it could not be locked at all.
        /// </returns>
        public static SingleInstanceEntry AcquireAdditional(string dataDirectory)
        {
            if (!AppRuntime.IsPackagedDesktop && !IsEngaged)
            {
                return SingleInstanceEntry.NotApplicable;
            }

            if (!TryNormalize(dataDirectory, out var key))
            {
                return SingleInstanceEntry.Unguarded;
            }

            var (status, _) = Acquire(dataDirectory, key);
            return status switch
            {
                DataDirectoryLockStatus.Acquired => SingleInstanceEntry.Entered,
                DataDirectoryLockStatus.HeldByAnotherProcess => SingleInstanceEntry.Refused,
                _ => SingleInstanceEntry.Unguarded,
            };
        }

        /// <summary>
        /// Makes an already held <paramref name="dataDirectory"/> the one this process owns —
        /// the relocation's target, once the pointer has flipped to it. The previous primary
        /// stays held until <see cref="Retire"/>.
        /// </summary>
        public static void Promote(string dataDirectory)
        {
            if (!TryNormalize(dataDirectory, out var key))
            {
                return;
            }

            lock (Gate)
            {
                if (Leases.ContainsKey(key))
                {
                    _primaryKey = key;
                }
            }
        }

        /// <summary>
        /// Lets go of a directory this process has finished with — a relocation's source, once
        /// the runner has emptied it — and removes what the lock left behind: the lock file,
        /// and the directory itself when nothing else is left in it. Never the primary.
        /// </summary>
        public static void Retire(string dataDirectory) => Release(dataDirectory, removeEmptyDirectory: _ => true);

        /// <summary>
        /// Lets go of a directory taken with <see cref="AcquireAdditional"/> for a move that did
        /// not happen, putting it back as it was: the lock file goes, and the directory too if
        /// taking the lock is what created it.
        /// </summary>
        public static void Abandon(string dataDirectory) =>
            Release(dataDirectory, removeEmptyDirectory: lease => lease.Lock.CreatedDirectory);

        /// <summary>
        /// Where a request to show the window goes. A request that arrived before this was set
        /// — while a relocation was still copying, say — is delivered now.
        /// </summary>
        public static void SetActivationHandler(Action handler)
        {
            bool pending;
            lock (Gate)
            {
                _activationHandler = handler;
                pending = _activationPending;
                _activationPending = false;
            }

            if (pending)
            {
                SafeInvoke(handler);
            }
        }

        /// <summary>
        /// Releases every lock and stops every channel. For the end of the process; the
        /// operating system would do the same a moment later.
        /// </summary>
        public static void ReleaseAll()
        {
            List<Lease> leases;
            lock (Gate)
            {
                leases = Leases.Values.ToList();
                Leases.Clear();
                _primaryKey = null;
                _activationHandler = null;
                _activationPending = false;
            }

            foreach (var lease in leases)
            {
                lease.Release();
            }
        }

        /// <summary>One line on what the guard is doing, for the startup log.</summary>
        public static string Describe()
        {
            lock (Gate)
            {
                if (_primaryKey != null && Leases.TryGetValue(_primaryKey, out var lease))
                {
                    var others = Leases.Count - 1;
                    return $"Single-instance guard: this process owns {lease.Directory} " +
                           $"(activation channel {lease.Server?.ChannelName ?? "none"}" +
                           (others > 0 ? $", plus {others} more directory lock(s))" : ")");
                }

                return _lastProblem != null
                    ? $"Single-instance guard not engaged: {_lastProblem}"
                    : "Single-instance guard not engaged.";
            }
        }

        private static bool IsEngaged
        {
            get
            {
                lock (Gate)
                {
                    return Leases.Count > 0;
                }
            }
        }

        private static (DataDirectoryLockStatus Status, Exception? Problem) Acquire(string dataDirectory, string key)
        {
            lock (AcquireGate)
            {
                return AcquireSerialised(dataDirectory, key);
            }
        }

        private static (DataDirectoryLockStatus Status, Exception? Problem) AcquireSerialised(string dataDirectory,
            string key)
        {
            lock (Gate)
            {
                if (Leases.ContainsKey(key))
                {
                    return (DataDirectoryLockStatus.Acquired, null);
                }
            }

            var attempt = DataDirectoryLock.TryAcquire(dataDirectory);
            if (!attempt.Acquired)
            {
                return (attempt.Status, attempt.Error);
            }

            // The channel starts only once the lock is ours: a server started first could
            // answer for a directory some other process owns.
            var channel = ActivationChannel.GetName(key);
            ActivationServer? server = null;
            try
            {
                server = ActivationServer.Start(channel, OnMessage, (e, failures) => ReportChannelFailure(channel, e, failures));
            }
            catch (Exception e)
            {
                // The lock is what protects the data; a window that cannot be summoned by a
                // second launch is an inconvenience, not a reason to give the lock back.
                WriteStderr($"Bakabase activation channel {channel} could not start: {e.Message}");
            }

            lock (Gate)
            {
                Leases[key] = new Lease(dataDirectory, attempt.Lock!, server);
            }

            return (DataDirectoryLockStatus.Acquired, null);
        }

        private static void Release(string dataDirectory, Func<Lease, bool> removeEmptyDirectory)
        {
            if (!TryNormalize(dataDirectory, out var key))
            {
                return;
            }

            Lease? lease;
            lock (Gate)
            {
                if (key == _primaryKey || !Leases.Remove(key, out lease))
                {
                    return;
                }
            }

            lease.Release();

            try
            {
                if (DataDirectoryLock.TryDeleteUnowned(lease.Directory) && removeEmptyDirectory(lease) &&
                    Directory.Exists(lease.Directory) && !Directory.EnumerateFileSystemEntries(lease.Directory).Any())
                {
                    Directory.Delete(lease.Directory);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Tidying only. What is left is an empty directory or an unlocked lock file.
            }
        }

        /// <summary>
        /// Everything here runs on the way into or out of startup, where an exception would
        /// cost more than an unguarded directory; a path that cannot even be normalised is
        /// one the rest of startup will report on its own terms.
        /// </summary>
        private static bool TryNormalize(string dataDirectory, out string key)
        {
            try
            {
                key = DataDirectoryIdentity.Normalize(dataDirectory);
                return true;
            }
            catch (Exception e)
            {
                NoteProblem($"Could not normalise {dataDirectory}: {e.Message}");
                key = null!;
                return false;
            }
        }

        private static void NoteProblem(string problem)
        {
            lock (Gate)
            {
                _lastProblem = problem;
            }

            WriteStderr($"Single-instance guard not engaged: {problem}");
        }

        private static void OnMessage(string message)
        {
            if (!string.Equals(message.Trim(), ActivationChannel.ShowMessage, StringComparison.Ordinal))
            {
                return;
            }

            Action? handler;
            lock (Gate)
            {
                handler = _activationHandler;
                if (handler == null)
                {
                    _activationPending = true;
                }
            }

            Serilog.Log.Information(handler != null
                ? "Another launch on this data directory asked for the window; showing it"
                : "Another launch on this data directory asked for the window; it will show once it exists");

            if (handler != null)
            {
                SafeInvoke(handler);
            }
        }

        private static void SafeInvoke(Action handler)
        {
            try
            {
                handler();
            }
            catch (Exception e)
            {
                Serilog.Log.Warning(e, "Showing the window for a second launch failed");
            }
        }

        private static void ReportChannelFailure(string channel, Exception e, int consecutiveFailures)
        {
            // First failure, then 2nd, 4th, 8th… in a row: enough to see a stuck channel in the
            // log without writing a line every thirty seconds for the life of the process.
            if (BitOperations.IsPow2(consecutiveFailures))
            {
                Serilog.Log.Warning(e,
                    "Activation channel {Channel} failed {Failures} time(s) in a row; retrying in {Delay}",
                    channel, consecutiveFailures, ActivationServer.RetryDelay(consecutiveFailures));
            }
        }

        private static void WriteStderr(string line)
        {
            try
            {
                Console.Error.WriteLine($"[Bakabase] {line}");
            }
            catch
            {
                // No console attached (a Windows GUI launch): nothing to write to.
            }
        }

        /// <summary>Back to the initial state. Tests only.</summary>
        internal static void ResetForTests()
        {
            ReleaseAll();
            lock (Gate)
            {
                _lastProblem = null;
            }
        }
    }
}
