using System;
using System.IO;
using Newtonsoft.Json;

namespace Bakabase.Infrastructures.Components.App.Relocation
{
    /// <summary>
    /// Marker that survives a process restart and instructs the next launch to perform a
    /// data-path relocation. Lives at <c>&lt;currentDataDir&gt;/.pending_relocate</c>.
    /// </summary>
    public sealed class PendingRelocation
    {
        public const int CurrentSchemaVersion = 2;
        public const string FileName = ".pending_relocate";

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string Target { get; set; } = null!;
        public RelocationMode Mode { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Language { get; set; } = "en-US";
        public string SourceWhenCreated { get; set; } = null!;

        public static string GetMarkerPath(string dataDir) => Path.Combine(dataDir, FileName);

        public static PendingRelocation? TryReadFrom(string dataDir)
        {
            var path = GetMarkerPath(dataDir);
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<PendingRelocation>(json);
            }
            catch
            {
                return null;
            }
        }

        public void WriteTo(string dataDir)
        {
            var path = GetMarkerPath(dataDir);
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public static void Delete(string dataDir)
        {
            var path = GetMarkerPath(dataDir);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public enum RelocationMode
    {
        /// <summary>
        /// Flip the pointer; do not copy. Target already has Bakabase data the user wants to
        /// adopt; the previous data dir is left untouched (the user's own backup).
        /// </summary>
        UseTarget = 1,

        /// <summary>
        /// Copy current → target with merge semantics: same-name files at the target are
        /// overwritten, target-only files (e.g. third-party caches) are preserved. Subsumes
        /// the previous "copy to empty" and "delete-then-copy" modes — the runner does not
        /// have to know whether the target was empty, populated with our data, or populated
        /// with unrelated files. <c>app.json</c> at the source is excluded; the marker itself
        /// is excluded.
        /// </summary>
        MergeOverwrite = 3,
    }
}
