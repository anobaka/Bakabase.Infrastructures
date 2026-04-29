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
        public const int CurrentSchemaVersion = 1;
        public const string FileName = ".pending_relocate";

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string Target { get; set; } = null!;
        public RelocationMode Mode { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Language { get; set; } = "en-US";
        public string SourceWhenCreated { get; set; } = null!;
        public long ExpectedFiles { get; set; }
        public long ExpectedTotalBytes { get; set; }

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
        /// Flip the pointer; do not copy. Target already has Bakabase data the user wants to use.
        /// </summary>
        UseTarget = 1,

        /// <summary>
        /// Target is empty / nonexistent; copy current → target.
        /// </summary>
        CopyToEmpty = 2,

        /// <summary>
        /// Target has data that gets deleted before copy.
        /// </summary>
        OverwriteTarget = 3,
    }
}
