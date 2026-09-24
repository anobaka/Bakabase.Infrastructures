using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Bakabase.Infrastructures.Components.App.Ports
{
    /// <summary>
    /// Remembers which ports this data directory's server listens on, so the next launch asks
    /// for the same ones (see <see cref="ListeningPortSelector"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two lists, because a launch can be pushed off its ports without anything having changed
    /// for good. <see cref="Remembered.Preferred"/> is what the directory's first launch settled
    /// on, and nothing ever overwrites it: the main window's origin — and the browser storage
    /// under it — belongs to those ports. <see cref="Remembered.LastUsed"/> is what the latest
    /// launch actually got. When another program briefly holds a preferred port, that launch
    /// falls back to other ports and records them as last used only; the next launch finds
    /// the preferred port free and is back on its own origin. A single list that took the
    /// fallback moved the origin for good after one conflict. The last used ports are the
    /// second choice, so a conflict that persists at least keeps landing on the same fallback.
    /// </para>
    /// <para>
    /// Kept in the data directory, not with the install or the user profile: the ports belong
    /// to the library they serve, and a relocation carries the memory along with the rest of
    /// the data.
    /// </para>
    /// <para>
    /// Known limitation, accepted because one data directory per install is the product rule
    /// and a second directory (<c>BAKABASE_DATA_DIR</c>) is an advanced escape hatch: each
    /// directory's first launch scans from the same start, so two directories that first ran
    /// at different times can both prefer the same ports. Run side by side, the one started
    /// first then gets them every time, and the other falls back. Nothing coordinates
    /// directories across the machine; a per-user registry of preferred ports would.
    /// </para>
    /// <para>
    /// A plain file rather than a field in <c>app.json</c>: the ports are decided while the
    /// host is still being built, and this is bookkeeping the settings page has no business
    /// showing. Every failure is swallowed — a server that cannot read or write this file
    /// still has ports to bind, and refusing to start over a cache would trade a cosmetic
    /// problem for a fatal one. A half-written file reads as no memory at all, which is what a
    /// first launch reads too. To start over (and let the next launch pick new preferred
    /// ports), delete the file.
    /// </para>
    /// </remarks>
    public sealed class ListeningPortMemory(string dataDirectory)
    {
        public const string FileName = "listening-ports.json";

        private static readonly JsonSerializerOptions SerializerOptions =
            new() {WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase};

        /// <summary>What a data directory remembers; both lists empty when it remembers nothing.</summary>
        public sealed record Remembered(IReadOnlyList<int> Preferred, IReadOnlyList<int> LastUsed)
        {
            public static readonly Remembered Nothing = new([], []);

            /// <summary>
            /// The ports to try before scanning, best first: the preferred ones, then the last
            /// used ones not already among them.
            /// </summary>
            public IReadOnlyList<int> Candidates => Preferred.Concat(LastUsed).Distinct().ToList();
        }

        private sealed record State
        {
            public List<int>? Preferred { get; init; }
            public List<int>? LastUsed { get; init; }
        }

        public string FilePath => Path.Combine(dataDirectory, FileName);

        /// <summary>What this data directory remembers.</summary>
        public Remembered Read()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return Remembered.Nothing;
                }

                var state = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath), SerializerOptions);
                return state == null ? Remembered.Nothing : new Remembered(Clean(state.Preferred), Clean(state.LastUsed));
            }
            catch
            {
                return Remembered.Nothing;
            }
        }

        /// <summary>
        /// Records that the server is listening on <paramref name="ports"/>: always as the last
        /// used ports, and as the preferred ones only when there were none yet. A no-op when that
        /// is already what the file says, so a server that never moves never touches the disk.
        /// </summary>
        public void Record(IReadOnlyList<int> ports)
        {
            try
            {
                if (ports.Count == 0)
                {
                    return;
                }

                var before = Read();
                var preferred = before.Preferred.Count > 0 ? before.Preferred : ports;
                if (before.Preferred.SequenceEqual(preferred) && before.LastUsed.SequenceEqual(ports))
                {
                    return;
                }

                Directory.CreateDirectory(dataDirectory);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(
                    new State {Preferred = preferred.ToList(), LastUsed = ports.ToList()}, SerializerOptions));
            }
            catch
            {
                // See the remarks: by the time this runs the ports are chosen and bound.
            }
        }

        private static IReadOnlyList<int> Clean(IEnumerable<int>? ports) =>
            ports?.Where(p => p is > 0 and <= 65535).Distinct().ToList() ?? [];
    }
}
