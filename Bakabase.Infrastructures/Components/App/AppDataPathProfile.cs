namespace Bakabase.Infrastructures.Components.App
{
    /// <summary>
    /// Which set of names the AppData anchor is resolved from.
    /// </summary>
    /// <remarks>
    /// The all-in-one app and the thin client are separate builds that a user may well
    /// run on the same machine at the same time — one serving their library, the other
    /// pointed at a server elsewhere. They must not share a data directory, or the
    /// second one to start would adopt the first's database and settings.
    /// <para>
    /// <see cref="AllInOne"/> repeats today's constants exactly. That is the point: the
    /// existing app resolves through this type and has to land where it always has.
    /// </para>
    /// </remarks>
    public sealed record AppDataPathProfile(
        string EnvVarName,
        string FolderName,
        string WindowsAppDataFolderName)
    {
        /// <summary>The app that runs its own server. Every value here is what shipped before profiles existed.</summary>
        public static readonly AppDataPathProfile AllInOne = new(
            "BAKABASE_DATA_DIR",
            "Bakabase",
            "Bakabase.AppData");

        /// <summary>The client that connects to a server elsewhere.</summary>
        public static readonly AppDataPathProfile Client = new(
            "BAKABASE_CLIENT_DATA_DIR",
            "Bakabase.Client",
            "Bakabase.Client.AppData");

        /// <summary>
        /// Whether this is the profile the app has always used. Read by the parts of
        /// startup that only make sense for it — migrating a pre-redirect layout, for
        /// one, which a directory that has never existed cannot have.
        /// </summary>
        public bool IsAllInOne => ReferenceEquals(this, AllInOne);
    }
}
