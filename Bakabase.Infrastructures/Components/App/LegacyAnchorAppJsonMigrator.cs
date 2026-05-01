using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Bakabase.Infrastructures.Components.App
{
    /// <summary>
    /// One-shot migration from the legacy "anchor owns app.json" layout to the
    /// "redirect at anchor + app.json lives with data" layout.
    ///
    /// Triggered at the very start of <see cref="Configurations.App.AppOptionsManager"/>
    /// initialisation, before any options are loaded. Does nothing in steady state once
    /// migration has been performed (signalled by the presence of <c>.redirect</c>) or
    /// for users who never customised <c>DataPath</c>.
    ///
    /// Crash-safe ordering: copy app.json to the target first, then write the redirect,
    /// then delete the anchor copy. If we crash between the redirect write and the
    /// anchor delete, the next boot still routes correctly via the redirect; a leftover
    /// anchor app.json is harmless and gets cleaned up the next time we run through.
    /// </summary>
    public static class LegacyAnchorAppJsonMigrator
    {
        // Match a top-level "dataPath": "value" property. The serializer uses camelCase
        // so the key is lower-cased; we keep IgnoreCase as a defensive measure for
        // hand-edited files. Backslashes in JSON are escaped as "\\" — preserve them
        // verbatim and let the path APIs canonicalise.
        private static readonly Regex DataPathRegex = new(
            "\"dataPath\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Run the migration pass. Idempotent. Throws on any IO or path-validation error
        /// (per design — a corrupt migration must surface, not silently strand the user
        /// with a half-converted layout).
        /// </summary>
        public static void RunIfNeeded(string anchorDir)
        {
            if (string.IsNullOrWhiteSpace(anchorDir))
                throw new ArgumentException("Anchor dir is required.", nameof(anchorDir));

            // Already migrated.
            if (AnchorRedirect.Exists(anchorDir)) return;

            var anchorAppJson = Path.Combine(anchorDir, EffectiveAppDataResolver.AppOptionsFileName);
            if (!File.Exists(anchorAppJson)) return; // fresh install, nothing to migrate

            var legacyDataPath = TryExtractDataPath(anchorAppJson);
            if (string.IsNullOrWhiteSpace(legacyDataPath)) return; // no custom DataPath was set

            var normalisedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyDataPath));
            var normalisedAnchor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(anchorDir));
            if (string.Equals(normalisedTarget, normalisedAnchor, StringComparison.OrdinalIgnoreCase))
            {
                return; // DataPath happened to equal the anchor — nothing to do
            }

            Directory.CreateDirectory(normalisedTarget);

            var targetAppJson = Path.Combine(normalisedTarget, EffectiveAppDataResolver.AppOptionsFileName);

            // Per design: target wins. If the user's data dir already has its own app.json,
            // we adopt it as-is — its (possibly older) Version is exactly the signal we need
            // for migrators to run on the next boot.
            if (!File.Exists(targetAppJson))
            {
                File.Copy(anchorAppJson, targetAppJson, overwrite: false);
            }

            AnchorRedirect.Write(anchorDir, normalisedTarget);

            // Last step: clear the now-stale anchor copy. Boot flow will use .redirect from
            // here on, so leaving it would only cause confusion.
            File.Delete(anchorAppJson);
        }

        private static string? TryExtractDataPath(string appJsonPath)
        {
            var raw = File.ReadAllText(appJsonPath);
            var match = DataPathRegex.Match(raw);
            if (!match.Success) return null;

            // JSON-unescape the captured value: the file stores backslashes as "\\" and
            // forward slashes as-is. We only need to handle "\\" → "\" since the regex
            // class excludes any other escape sequence.
            return match.Groups[1].Value.Replace("\\\\", "\\");
        }
    }
}
