using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.App.SingleInstance
{
    /// <summary>
    /// How a launch that found its data directory already owned asks the owner to show its
    /// window: one line down a named pipe (a Unix domain socket on macOS and Linux).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name is derived from the data directory — the same thing the lock is on — so the
    /// refused launch reaches exactly the instance that refused it, and two instances on
    /// different data directories never answer for each other. It also carries the user:
    /// Windows pipe names are machine-wide, and a second user's instance used to collide with
    /// the first user's pipe and spin on it. Both ends pass
    /// <see cref="PipeOptions.CurrentUserOnly"/>, so another account can neither answer nor
    /// send.
    /// </para>
    /// <para>
    /// <b>Where the socket lives on macOS and Linux.</b> Given a plain name, .NET puts the
    /// socket at <c>$TMPDIR/CoreFxPipe_{name}</c> — the <i>caller's</i> <c>TMPDIR</c>. A launch
    /// from ssh, a script or an IDE often has a different one from the running app's, so it
    /// was refused by the lock (correctly) and then knocked on a socket that was not there:
    /// the window never came up. Both ends therefore hand the pipe an absolute path instead
    /// (<see cref="GetEndpoint(string)"/>), in a per-user directory that does not come from
    /// the environment. The name is kept short because a socket path is limited to about a
    /// hundred bytes.
    /// </para>
    /// </remarks>
    public static class ActivationChannel
    {
        public const string ShowMessage = "SHOW";

        private const string Prefix = "Bakabase";

        /// <summary>
        /// <c>sun_path</c> on macOS, the terminating zero included; Linux allows 108. A path that
        /// does not fit is not used.
        /// </summary>
        internal const int MaxSocketPathBytes = 104;

        /// <summary><c>_CS_DARWIN_USER_TEMP_DIR</c> from macOS's <c>unistd.h</c>.</summary>
        private const int DarwinUserTempDirName = 65537;

        private static readonly Lazy<string?> SocketDirectory = new(ResolveSocketDirectory);

        /// <summary>
        /// The channel for <paramref name="normalizedDataDirectory"/> (see
        /// <see cref="DataDirectoryIdentity.Normalize(string)"/>), for the current user.
        /// </summary>
        public static string GetName(string normalizedDataDirectory) =>
            GetName(normalizedDataDirectory, CurrentUser, DataDirectoryIdentity.CurrentRules);

        /// <summary>
        /// The channel for an explicit <paramref name="user"/>, so the naming rule can be
        /// tested without switching accounts.
        /// </summary>
        public static string GetName(string normalizedDataDirectory, string user,
            DataDirectoryIdentity.PathRules rules)
        {
            // Account names ignore case on Windows (and in practice on macOS); a launch through
            // a differently cased shortcut must still find its own instance.
            var userKey = rules == DataDirectoryIdentity.PathRules.CaseSensitive ? user : user.ToUpperInvariant();
            return $"{Prefix}-{DataDirectoryIdentity.Digest(userKey, 8)}-{DataDirectoryIdentity.Hash(normalizedDataDirectory)}";
        }

        /// <summary>
        /// Who this process runs as. On Windows the domain is part of it: two accounts named
        /// alike on different domains are different people.
        /// </summary>
        public static string CurrentUser =>
            OperatingSystem.IsWindows()
                ? $"{Environment.UserDomainName}\\{Environment.UserName}"
                : Environment.UserName;

        /// <summary>
        /// What both ends pass to the pipe constructors for <paramref name="channelName"/>.
        /// </summary>
        /// <remarks>
        /// On Windows the name itself: pipe names there are a machine-wide namespace already. On
        /// macOS and Linux the absolute path of a socket in <see cref="GetSocketDirectory"/>,
        /// which .NET then uses as it is instead of looking in <c>$TMPDIR</c>. Only when no such
        /// directory can be had, or the path would not fit a socket address, the plain name —
        /// .NET's own <c>$TMPDIR</c> placement, which still works between launches that share
        /// a <c>TMPDIR</c>.
        /// </remarks>
        public static string GetEndpoint(string channelName) =>
            OperatingSystem.IsWindows() ? channelName : GetEndpoint(channelName, GetSocketDirectory());

        /// <summary>
        /// <see cref="GetEndpoint(string)"/> on macOS or Linux with the socket directory given, so
        /// the fallbacks can be tested.
        /// </summary>
        internal static string GetEndpoint(string channelName, string? socketDirectory)
        {
            if (string.IsNullOrEmpty(socketDirectory))
            {
                return channelName;
            }

            var path = Path.Combine(socketDirectory, channelName);
            return Encoding.UTF8.GetByteCount(path) < MaxSocketPathBytes ? path : channelName;
        }

        /// <summary>
        /// The per-user directory the channel sockets live in on macOS and Linux, the same for
        /// every process of this user whatever its environment; null when there is none (and
        /// always on Windows).
        /// </summary>
        /// <remarks>
        /// <para>
        /// macOS: the user's own temporary directory as the system assigns it
        /// (<c>confstr(_CS_DARWIN_USER_TEMP_DIR)</c>, what <c>getconf DARWIN_USER_TEMP_DIR</c>
        /// prints) — what <c>TMPDIR</c> is in a normal login, but asked of the system rather than
        /// read from a variable a shell may have changed.
        /// </para>
        /// <para>
        /// Linux: <c>/run/user/{uid}</c>, which is where <c>$XDG_RUNTIME_DIR</c> points wherever
        /// logind (or elogind) runs — looked up by the uid rather than read from the variable, which
        /// an ssh session without <c>pam_systemd</c>, cron or <c>sudo -u</c> do not carry.
        /// Without it, <c>/tmp/bakabase-{uid}</c>, created <c>0700</c> and used only when it is
        /// a real directory this user owns (see <see cref="ResolveUnixSocketDirectory"/>).
        /// </para>
        /// </remarks>
        public static string? GetSocketDirectory() => OperatingSystem.IsWindows() ? null : SocketDirectory.Value;

        private static string? ResolveSocketDirectory()
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    var directory = ReadDarwinUserTempDirectory();
                    return directory != null && Path.IsPathRooted(directory) && Directory.Exists(directory)
                        ? Path.TrimEndingDirectorySeparator(directory)
                        : null;
                }

                if (OperatingSystem.IsLinux())
                {
                    return ResolveUnixSocketDirectory(ReadLinuxEffectiveUid(), "/run/user", "/tmp");
                }
            }
            catch (Exception)
            {
                // Whatever went wrong (a missing libc entry point, an unreadable /proc), this runs
                // on the way into startup: fall back to .NET's own placement; see GetEndpoint.
            }

            return null;
        }

        /// <summary>
        /// The Linux choice, with the uid and the two roots given so it can be tested anywhere:
        /// <c>{runUserRoot}/{uid}</c> when it exists, otherwise <c>{tmpRoot}/bakabase-{uid}</c>
        /// when it can be made or already is a private directory of this user's.
        /// </summary>
        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        internal static string? ResolveUnixSocketDirectory(uint? uid, string runUserRoot, string tmpRoot)
        {
            if (uid is not { } id)
            {
                return null;
            }

            // Made by logind for this uid, under a directory only root can write to.
            var runtime = Path.Combine(runUserRoot, id.ToString());
            if (Directory.Exists(runtime))
            {
                return runtime;
            }

            var shared = Path.Combine(tmpRoot, $"bakabase-{id}");
            try
            {
                Directory.CreateDirectory(shared, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

                // /tmp is everyone's: the name may have been taken first, by a link or by another
                // account's directory. A link is refused outright; setting the mode succeeds only
                // for the owner, so it proves the directory is this user's — and leaves it
                // private whatever it was created with.
                if (new DirectoryInfo(shared).LinkTarget != null)
                {
                    return null;
                }

                File.SetUnixFileMode(shared, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                return shared;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The effective uid, from <c>/proc/self/status</c>; null when it cannot be read.</summary>
        internal static uint? ReadLinuxEffectiveUid()
        {
            try
            {
                return ParseEffectiveUid(File.ReadLines("/proc/self/status"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// The effective uid from the lines of <c>/proc/self/status</c>: the second number of the
        /// <c>Uid:</c> line (real, effective, saved, file system).
        /// </summary>
        internal static uint? ParseEffectiveUid(System.Collections.Generic.IEnumerable<string> statusLines)
        {
            var line = statusLines.FirstOrDefault(l => l.StartsWith("Uid:", StringComparison.Ordinal));
            var fields = line?.Substring(4).Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
            return fields is {Length: >= 2} && uint.TryParse(fields[1], out var uid) ? uid : null;
        }

        private static string? ReadDarwinUserTempDirectory()
        {
            var buffer = new byte[1024];
            var length = (int) Confstr(DarwinUserTempDirName, buffer, (nuint) buffer.Length);
            // The length includes the terminating zero; 0 is failure, more than the buffer means
            // it did not fit (no real temporary directory is that long).
            return length is > 1 and <= 1024 ? Encoding.UTF8.GetString(buffer, 0, length - 1) : null;
        }

        [DllImport("libc", EntryPoint = "confstr")]
        private static extern nuint Confstr(int name, byte[] buffer, nuint length);

        /// <summary>
        /// Sends <paramref name="message"/> to the instance listening on
        /// <paramref name="channelName"/>.
        /// </summary>
        /// <returns>Whether the message was delivered.</returns>
        public static async Task<bool> TrySendAsync(string channelName, string message, TimeSpan timeout)
        {
            try
            {
                await using var client = new NamedPipeClientStream(".", GetEndpoint(channelName), PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using var cts = new CancellationTokenSource(timeout);
                await client.ConnectAsync(cts.Token);

                await using var writer = new StreamWriter(client) {AutoFlush = true};
                await writer.WriteLineAsync(message.AsMemory(), cts.Token);
                return true;
            }
            catch (Exception)
            {
                // Nobody listening, a timeout — or a socket path the platform cannot use at all
                // (.NET throws ArgumentOutOfRangeException for one past sun_path, which a long
                // TMPDIR used to produce). Whatever it is, the message was not delivered, and this
                // runs inside the entry point: an exception here would crash the launch instead
                // of turning it away.
                return false;
            }
        }
    }

    /// <summary>
    /// The owning instance's end of an <see cref="ActivationChannel"/>: accepts one
    /// connection at a time, reads one line, hands it on, and goes back to listening.
    /// </summary>
    /// <remarks>
    /// A failure to create or accept on the pipe is retried after a delay that doubles each
    /// time, up to <see cref="MaxRetryDelay"/>, and resets on the next success. The loop this
    /// replaces retried immediately: when the pipe could never be created — another account's
    /// instance already held the machine-wide name — it spun a core for as long as the app ran.
    /// </remarks>
    public sealed class ActivationServer : IDisposable
    {
        public static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(250);
        public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

        private readonly CancellationTokenSource _cts;
        private readonly Task _loop;

        private ActivationServer(Task loop, CancellationTokenSource cts)
        {
            _cts = cts;
            _loop = loop;
        }

        public string ChannelName { get; private init; } = null!;

        /// <summary>
        /// What the pipe is created on: <see cref="ActivationChannel.GetEndpoint(string)"/> for
        /// <see cref="ChannelName"/> — on macOS and Linux, the socket's path.
        /// </summary>
        public string Endpoint { get; private init; } = null!;

        /// <summary>
        /// Starts listening on <paramref name="channelName"/>.
        /// </summary>
        /// <param name="onMessage">Called on a pool thread with each line received.</param>
        /// <param name="onError">
        /// Called with each failure and how many have happened in a row, before the delay.
        /// </param>
        public static ActivationServer Start(string channelName, Action<string> onMessage,
            Action<Exception, int>? onError = null)
        {
            var endpoint = ActivationChannel.GetEndpoint(channelName);
            var cts = new CancellationTokenSource();
            var loop = Task.Run(() => RunAsync(
                ct => AcceptOneAsync(endpoint, ct),
                onMessage,
                (delay, ct) => Task.Delay(delay, ct),
                onError ?? ((_, _) => { }),
                cts.Token));
            return new ActivationServer(loop, cts) {ChannelName = channelName, Endpoint = endpoint};
        }

        /// <summary>
        /// The delay before the <paramref name="consecutiveFailures"/>th retry in a row.
        /// </summary>
        public static TimeSpan RetryDelay(int consecutiveFailures)
        {
            if (consecutiveFailures <= 0)
            {
                return TimeSpan.Zero;
            }

            // The cap is reached by the eighth failure in a row (2^7 × 250 ms); bounding the
            // exponent keeps the arithmetic finite however long the failures go on.
            var exponent = Math.Min(consecutiveFailures - 1, 16);
            var ms = InitialRetryDelay.TotalMilliseconds * Math.Pow(2, exponent);
            return TimeSpan.FromMilliseconds(Math.Min(ms, MaxRetryDelay.TotalMilliseconds));
        }

        /// <summary>
        /// The listening loop, with its three effects injected so the retry behaviour can be
        /// tested without real pipes or real waiting.
        /// </summary>
        internal static async Task RunAsync(
            Func<CancellationToken, Task<string?>> acceptOne,
            Action<string> onMessage,
            Func<TimeSpan, CancellationToken, Task> delay,
            Action<Exception, int> onError,
            CancellationToken ct)
        {
            var failures = 0;

            while (!ct.IsCancellationRequested)
            {
                string? message;
                try
                {
                    message = await acceptOne(ct);
                    failures = 0;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    failures++;
                    try
                    {
                        onError(e, failures);
                    }
                    catch
                    {
                        // Reporting must not be what stops the retries.
                    }

                    try
                    {
                        await delay(RetryDelay(failures), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }

                if (message == null)
                {
                    continue;
                }

                try
                {
                    onMessage(message);
                }
                catch
                {
                    // A handler that throws has nothing to do with the pipe; keep listening.
                }
            }
        }

        private static async Task<string?> AcceptOneAsync(string endpoint, CancellationToken ct)
        {
            await using var server = new NamedPipeServerStream(
                endpoint,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await server.WaitForConnectionAsync(ct);

            using var reader = new StreamReader(server);
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // A client that connects and never writes must not hold the only instance forever.
            readTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                return await reader.ReadLineAsync(readTimeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }

        /// <summary>
        /// Stops listening. On macOS and Linux also removes the socket file, which .NET leaves
        /// behind: harmless to the next owner (it rebinds the path), but litter in the socket
        /// directory otherwise. The caller still holds the directory's lock at this point, so
        /// no other process can have bound the same name in the meantime.
        /// </summary>
        public void Dispose()
        {
            try
            {
                _cts.Cancel();
                _loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Shutting down; the loop ends with the process either way.
            }

            _cts.Dispose();

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    // A rooted endpoint is the socket's own path; a plain name is where .NET
                    // puts such a pipe on Unix.
                    File.Delete(Path.IsPathRooted(Endpoint)
                        ? Endpoint
                        : Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + Endpoint));
                }
                catch
                {
                    // Litter at worst.
                }
            }
        }
    }
}
