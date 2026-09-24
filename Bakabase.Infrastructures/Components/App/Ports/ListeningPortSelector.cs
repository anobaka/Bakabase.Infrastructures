using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Bakabase.Infrastructures.Components.App.Ports
{
    /// <summary>
    /// Chooses the ports the app picks for itself (<c>AutoListeningPortCount</c> of them),
    /// preferring the ones it had last time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first of these ports is the main window's origin, and the embedded browser keys
    /// localStorage, IndexedDB and its cache to the origin — port included. A port that drifts
    /// between launches therefore looks to the user like the app forgot their settings. Two
    /// things used to make it drift: nothing remembered the ports, and "free" was decided by
    /// reading the connection table, where the previous run's connections sit in
    /// <c>TIME_WAIT</c> for a while after a plain restart, so its own port read as taken.
    /// </para>
    /// <para>
    /// Now the remembered ports (see <see cref="ListeningPortMemory"/>: the directory's
    /// preferred ports, then the ones it last used) are tried first, in that order, and a
    /// candidate is judged by what the server will actually do with it (<see cref="IsFree"/>).
    /// Only a port that is really taken is skipped, and only then does the scan run. A port
    /// skipped once is not forgotten: it stays preferred, and is taken again as soon as it is
    /// free.
    /// </para>
    /// <para>
    /// The scan stays inside <see cref="WindowStart"/>..<see cref="WindowEnd"/>, below every
    /// other port this app hands out on the machine: the retired thin client's loopback port
    /// (34600, walking up at most 32) and the relays that open other servers in this window
    /// (from 34650, 256 of them). A main port that wandered into either range would change
    /// which program an origin — and the browser storage under it — belongs to. Past a full
    /// window the scan continues from <see cref="OverflowStart"/>, beyond both, rather than
    /// fail to start.
    /// </para>
    /// <para>
    /// Explicitly configured ports (<c>AppOptions.ListeningPorts</c>, the Docker
    /// <c>API_LISTENING_PORTS</c> variable, a host's own override) never come through here.
    /// They are passed to <see cref="Select"/> only as <c>reserved</c>, so an automatic port
    /// is never a duplicate of one of them.
    /// </para>
    /// </remarks>
    public static class ListeningPortSelector
    {
        /// <summary>Where the main window's ports have always started.</summary>
        public const int WindowStart = 34567;

        /// <summary>
        /// One past the last port of the window: the thin client's preferred loopback port
        /// (<c>LoopbackPortAllocator.PreferredPort</c>), below the relays'
        /// (<c>RemoteConsoleOptions.DefaultFirstRelayPort</c>). Tests pin these together,
        /// since this assembly can see neither.
        /// </summary>
        public const int WindowEnd = 34600;

        /// <summary>Past the relays' default range (<c>34650 + 256</c>).</summary>
        public const int OverflowStart = 34906;

        /// <summary>How long a candidate gets to accept a probe connection on loopback.</summary>
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// <paramref name="count"/> ports: the remembered ones that are still free, in their
        /// remembered order, then the first free ones in the window.
        /// </summary>
        /// <param name="remembered">
        /// The ports to try first, best first: <see cref="ListeningPortMemory.Remembered.Candidates"/>.
        /// </param>
        /// <param name="reserved">Ports already spoken for by explicit configuration.</param>
        /// <param name="isFree">Injected for tests; defaults to <see cref="IsFree"/> on every interface.</param>
        /// <exception cref="IOException">No free port anywhere — not a state a real machine reaches.</exception>
        public static IReadOnlyList<int> Select(int count, IEnumerable<int>? remembered, IEnumerable<int>? reserved,
            Func<int, bool>? isFree = null)
        {
            if (count <= 0)
            {
                return [];
            }

            isFree ??= port => IsFree(port, IPAddress.Any);
            var taken = new HashSet<int>(reserved ?? []);
            var chosen = new List<int>(count);

            bool TryTake(int port)
            {
                if (chosen.Count >= count || taken.Contains(port) || !isFree(port))
                {
                    return false;
                }

                chosen.Add(port);
                taken.Add(port);
                return true;
            }

            foreach (var port in (remembered ?? []).Where(IsAutomaticPort).Distinct())
            {
                TryTake(port);
            }

            for (var port = WindowStart; port < WindowEnd && chosen.Count < count; port++)
            {
                TryTake(port);
            }

            for (var port = OverflowStart; port <= IPEndPoint.MaxPort && chosen.Count < count; port++)
            {
                TryTake(port);
            }

            if (chosen.Count < count)
            {
                throw new IOException($"Found only {chosen.Count} of {count} free listening ports.");
            }

            return chosen;
        }

        /// <summary>
        /// Whether <paramref name="port"/> is one this selector could have handed out — so a
        /// remembered value that was edited, or came from a time the window was different, is
        /// ignored instead of honoured.
        /// </summary>
        public static bool IsAutomaticPort(int port) =>
            port is >= WindowStart and < WindowEnd || port is >= OverflowStart and <= IPEndPoint.MaxPort;

        /// <summary>
        /// Whether the server can listen on <paramref name="port"/> at
        /// <paramref name="listeningAddress"/> and would be the one answering it on this machine.
        /// </summary>
        /// <remarks>
        /// <para>
        /// First a bind at exactly the address the server will use. That is the same call
        /// Kestrel makes, with the same options — on macOS and Linux .NET sets
        /// <c>SO_REUSEADDR</c>, so a port whose last connections are in <c>TIME_WAIT</c> binds,
        /// as it will for Kestrel a moment later — and a port another program listens on at
        /// that address refuses it.
        /// </para>
        /// <para>
        /// Then a connection attempt to loopback, over IPv4 and IPv6. The bind alone is not
        /// enough on macOS: a program listening on 127.0.0.1 or ::1 only does not stop a bind to
        /// 0.0.0.0, and the more specific address then wins — the window, which opens
        /// <c>localhost</c>, would load that program instead. A port nothing listens on refuses
        /// the connection at once; a <c>TIME_WAIT</c> leftover is not a listener and refuses
        /// too.
        /// </para>
        /// </remarks>
        public static bool IsFree(int port, IPAddress listeningAddress) =>
            CanBind(port, listeningAddress) &&
            !Accepts(IPAddress.Loopback, port) &&
            !(Socket.OSSupportsIPv6 && Accepts(IPAddress.IPv6Loopback, port));

        private static bool CanBind(int port, IPAddress address)
        {
            try
            {
                using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(address, port));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static bool Accepts(IPAddress address, int port)
        {
            try
            {
                using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                var connect = socket.ConnectAsync(new IPEndPoint(address, port));

                // Loopback answers a refusal immediately either way; a connect that neither
                // completes nor is refused in time counts as nobody there, so one odd port
                // cannot hold up every port behind it.
                return connect.Wait(ProbeTimeout) && socket.Connected;
            }
            catch (AggregateException e) when (e.InnerException is SocketException)
            {
                return false;
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }
}
