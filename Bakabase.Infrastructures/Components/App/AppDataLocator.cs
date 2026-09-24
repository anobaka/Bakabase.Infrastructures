using System;
using System.Diagnostics;
using System.IO;

namespace Bakabase.Infrastructures.Components.App
{
    /// <summary>
    /// Where this process's data lives, worked out without creating, migrating or opening
    /// anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule has two steps. The <i>anchor</i> is the platform default for the current
    /// <see cref="AppDataAnchor"/> profile, or the profile's environment variable
    /// (<c>BAKABASE_DATA_DIR</c> for the app) when set. The <i>effective</i> directory is the
    /// anchor itself under the environment variable — the operator named the directory, and
    /// nothing may send it elsewhere — and otherwise whatever the anchor's
    /// <see cref="AnchorRedirect"/> points at, or the anchor when there is none.
    /// </para>
    /// <para>
    /// <see cref="AppService"/> exposes the same two answers, and reads them from here. They
    /// are also needed <i>before</i> <see cref="AppService"/> may be touched: its static
    /// constructor creates the anchor, converts a legacy layout and opens the log file, and
    /// the single-instance guard has to know whose data directory this is before any of that
    /// runs, or a launch that is about to be refused would already have written to it.
    /// </para>
    /// </remarks>
    public static class AppDataLocator
    {
        /// <summary>
        /// Whether the profile's data-directory environment variable is set.
        /// </summary>
        public static bool IsEnvironmentOverride =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppDataAnchor.Current.EnvVarName));

        /// <summary>
        /// The anchor: where <see cref="AnchorRedirect"/> lives, and the data itself unless the
        /// redirect sends it elsewhere. Pure — creates nothing.
        /// </summary>
        public static string ResolveAnchor()
        {
            var entryAssemblyName = Path.GetFileNameWithoutExtension(
                Process.GetCurrentProcess().MainModule?.FileName) ?? "Bakabase";
            bool isDebug;
#if DEBUG
            isDebug = true;
#else
            isDebug = false;
#endif
            return DefaultAppDataPathResolver.Resolve(
                AppDataAnchor.Current,
                DefaultAppDataPathResolver.GetCurrentOsPlatform(),
                Environment.GetEnvironmentVariable,
                Environment.GetFolderPath,
                entryAssemblyName,
                isDebug);
        }

        /// <summary>
        /// The directory that owns <c>app.json</c>, the databases and everything else written
        /// at runtime, for the given <paramref name="anchor"/>. Reads the redirect file; writes
        /// nothing.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The redirect file is corrupt — by design, the same failure the rest of startup
        /// raises for it (see <see cref="AnchorRedirect.TryRead"/>).
        /// </exception>
        public static string ResolveEffectiveDataDirectory(string anchor) =>
            IsEnvironmentOverride ? anchor : EffectiveAppDataResolver.Resolve(anchor).DataDir;

        /// <summary>
        /// <see cref="ResolveEffectiveDataDirectory(string)"/> for this process's own anchor.
        /// </summary>
        public static string ResolveEffectiveDataDirectory() => ResolveEffectiveDataDirectory(ResolveAnchor());
    }
}
