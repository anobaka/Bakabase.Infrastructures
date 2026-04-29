namespace Bakabase.Infrastructures.Components.App.Models.Constants
{
    /// <summary>
    /// Origin of the effective <c>AppDataDirectory</c> value, surfaced in <c>AppInfo</c>
    /// so the UI can explain why the path is what it is.
    /// </summary>
    public enum DataPathSource
    {
        /// <summary>
        /// User has not customised the path; falls back to the platform default anchor.
        /// </summary>
        Default = 0,

        /// <summary>
        /// User explicitly set <c>AppOptions.DataPath</c> via the settings UI.
        /// </summary>
        UserConfigured = 1,

        /// <summary>
        /// <c>BAKABASE_DATA_DIR</c> environment variable overrode both anchor and data path.
        /// </summary>
        Environment = 2,
    }
}
