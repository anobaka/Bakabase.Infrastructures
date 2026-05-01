using System.IO;

namespace Bakabase.Infrastructures.Components.App
{
    /// <summary>
    /// Single source of truth for "given an anchor, where does the user's
    /// <c>app.json</c> + runtime data actually live". Composes <see cref="AnchorRedirect"/>:
    /// when a redirect file is present its target wins, otherwise the anchor itself is the
    /// data dir. Used by both <see cref="Configurations.App.AppOptionsManager"/> (to load
    /// <c>app.json</c>) and <see cref="AppService"/> (to expose the effective dir +
    /// log path), so both observe the same answer at all times.
    /// </summary>
    public static class EffectiveAppDataResolver
    {
        public sealed record Resolved(string DataDir, string AppJsonPath, bool IsRedirected);

        public const string AppOptionsFileName = "app.json";

        public static Resolved Resolve(string anchorDir)
        {
            var redirectTarget = AnchorRedirect.TryRead(anchorDir);
            var dataDir = redirectTarget ?? anchorDir;
            var appJsonPath = Path.Combine(dataDir, AppOptionsFileName);
            return new Resolved(dataDir, appJsonPath, redirectTarget != null);
        }
    }
}
