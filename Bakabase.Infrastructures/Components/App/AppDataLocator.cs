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
    /// (<c>BAKABASE_DATA_DIR</c> for the app) when set. The <i>effective</i> directory is
    /// whatever the anchor's <see cref="AnchorRedirect"/> points at, or the anchor itself when
    /// there is none — the same with the environment variable as without it.
    /// </para>
    /// <para>
    /// This is the only answer to "which directory is this process's data directory". The
    /// database (<c>AppStartup</c>), <c>app.json</c> (<c>AppOptionsManager</c>), the option
    /// files and the port memory (<see cref="AppHost"/>), <see cref="AppService.AppDataDirectory"/>,
    /// the log file and the single-instance guard all ask here, so the directory the guard
    /// locks is by construction the one the database is opened in. When they disagreed — the
    /// guard stopping at the variable's directory, the database following a redirect inside
    /// it — a launch that pointed the variable at an anchor with a redirect locked one
    /// directory and opened another running instance's database. The database is the side
    /// that was kept: it has always followed the user's chosen location, so no existing
    /// library moves.
    /// </para>
    /// <para>
    /// The answer is also needed <i>before</i> <see cref="AppService"/> may be touched: its
    /// static constructor creates the anchor, converts a legacy layout and opens the log file,
    /// and the single-instance guard has to know whose data directory this is before any of
    /// that runs, or a launch that is about to be refused would already have written to it.
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
            EffectiveAppDataResolver.Resolve(anchor).DataDir;

        /// <summary>
        /// <see cref="ResolveEffectiveDataDirectory(string)"/> for this process's own anchor.
        /// </summary>
        public static string ResolveEffectiveDataDirectory() => ResolveEffectiveDataDirectory(ResolveAnchor());
    }
}
