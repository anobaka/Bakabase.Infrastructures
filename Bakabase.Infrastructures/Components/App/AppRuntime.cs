using Bootstrap.Models.Constants;

namespace Bakabase.Infrastructures.Components.App
{
    /// <summary>
    /// Which build this is, readable before anything has touched the data directory.
    /// </summary>
    /// <remarks>
    /// <see cref="AppService.RuntimeMode"/> answers the same question, but reading any static
    /// member of <see cref="AppService"/> runs its static constructor — which creates the data
    /// directory, migrates a legacy layout and opens the log file. The single-instance guard
    /// has to decide whether it applies before any of that happens, so the answer lives on a
    /// type with no static constructor of its own, and <see cref="AppService"/> forwards here.
    /// </remarks>
    public static class AppRuntime
    {
#if RUNTIME_MODE_WINFORMS
        public static RuntimeMode Mode => RuntimeMode.WinForms;
#elif RUNTIME_MODE_DOCKER
        public static RuntimeMode Mode => RuntimeMode.Docker;
#elif RUNTIME_MODE_MACOS
        public static RuntimeMode Mode => RuntimeMode.MacOS;
#else
        public static RuntimeMode Mode => RuntimeMode.Dev;
#endif

        /// <summary>
        /// A packaged desktop build: a window a user opens by double-clicking, which is what
        /// "launch it again" and the single-instance guard are about.
        /// </summary>
        public static bool IsPackagedDesktop => Mode is RuntimeMode.WinForms or RuntimeMode.MacOS;
    }
}
