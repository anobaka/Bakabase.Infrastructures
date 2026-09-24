using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Bakabase.Infrastructures.Components.App.Ports
{
    /// <summary>
    /// Remembers which ports this data directory's server listened on, so the next launch asks
    /// for the same ones (see <see cref="ListeningPortSelector"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept in the data directory, not with the install or the user profile: the ports belong
    /// to the library they serve. Two data directories run side by side each keep their own,
    /// so the order they start in no longer decides which one gets which port, and a
    /// relocation carries the memory along with the rest of the data.
    /// </para>
    /// <para>
    /// A plain file rather than a field in <c>app.json</c>: the ports are decided while the
    /// host is still being built, and this is bookkeeping the settings page has no business
    /// showing. Every failure is swallowed — a server that cannot read or write this file
    /// still has ports to bind, and refusing to start over a cache would trade a cosmetic
    /// problem for a fatal one. A half-written file reads as no memory at all, which is what a
    /// first launch reads too.
    /// </para>
    /// </remarks>
    public sealed class ListeningPortMemory(string dataDirectory)
    {
        public const string FileName = "listening-ports.json";

        private static readonly JsonSerializerOptions SerializerOptions =
            new() {WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase};

        private sealed record State
        {
            public List<int>? Ports { get; init; }
        }

        public string FilePath => Path.Combine(dataDirectory, FileName);

        /// <summary>The ports last listened on, in order; empty when there is nothing to go on.</summary>
        public IReadOnlyList<int> Read()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return [];
                }

                var ports = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath), SerializerOptions)?.Ports;
                return ports?.Where(p => p is > 0 and <= 65535).Distinct().ToList() ?? [];
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// Records <paramref name="ports"/> for next time. A no-op when that is already what the
        /// file says, so a server that never moves never touches the disk.
        /// </summary>
        public void Write(IReadOnlyList<int> ports)
        {
            try
            {
                if (ports.Count == 0 || Read().SequenceEqual(ports))
                {
                    return;
                }

                Directory.CreateDirectory(dataDirectory);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new State {Ports = ports.ToList()}, SerializerOptions));
            }
            catch
            {
                // See the remarks: by the time this runs the ports are chosen and bound.
            }
        }
    }
}
