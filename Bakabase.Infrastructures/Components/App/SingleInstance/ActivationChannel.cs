using System;
using System.IO;
using System.IO.Pipes;
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
    /// Kept short on purpose: on Unix .NET places the socket at
    /// <c>$TMPDIR/CoreFxPipe_{name}</c>, and a socket path is limited to about a hundred
    /// bytes.
    /// </para>
    /// </remarks>
    public static class ActivationChannel
    {
        public const string ShowMessage = "SHOW";

        private const string Prefix = "Bakabase";

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
        /// Sends <paramref name="message"/> to the instance listening on
        /// <paramref name="channelName"/>.
        /// </summary>
        /// <returns>Whether the message was delivered.</returns>
        public static async Task<bool> TrySendAsync(string channelName, string message, TimeSpan timeout)
        {
            try
            {
                await using var client = new NamedPipeClientStream(".", channelName, PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using var cts = new CancellationTokenSource(timeout);
                await client.ConnectAsync(cts.Token);

                await using var writer = new StreamWriter(client) {AutoFlush = true};
                await writer.WriteLineAsync(message.AsMemory(), cts.Token);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException
                                          or OperationCanceledException)
            {
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
        /// Starts listening on <paramref name="channelName"/>.
        /// </summary>
        /// <param name="onMessage">Called on a pool thread with each line received.</param>
        /// <param name="onError">
        /// Called with each failure and how many have happened in a row, before the delay.
        /// </param>
        public static ActivationServer Start(string channelName, Action<string> onMessage,
            Action<Exception, int>? onError = null)
        {
            var cts = new CancellationTokenSource();
            var loop = Task.Run(() => RunAsync(
                ct => AcceptOneAsync(channelName, ct),
                onMessage,
                (delay, ct) => Task.Delay(delay, ct),
                onError ?? ((_, _) => { }),
                cts.Token));
            return new ActivationServer(loop, cts) {ChannelName = channelName};
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

        private static async Task<string?> AcceptOneAsync(string channelName, CancellationToken ct)
        {
            await using var server = new NamedPipeServerStream(
                channelName,
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
        /// behind: harmless to the next owner (it rebinds the path), but litter in the temp
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
                    // Where .NET puts a pipe with a plain name on Unix.
                    File.Delete(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + ChannelName));
                }
                catch
                {
                    // Litter at worst.
                }
            }
        }
    }
}
