namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    /// <summary>
    /// Resolves the Velopack update feed base URL. Pluggable so app-specific deployment
    /// details (default URL, env-var override name) live in the consumer project rather
    /// than in this generic infrastructure library. The runtime identifier suffix
    /// (e.g. <c>win-x64</c>) is appended by <see cref="AppUpdater"/>; implementations
    /// return the bare base URL.
    /// </summary>
    public interface IAppUpdateSource
    {
        /// <summary>
        /// Return the resolved base URL. Implementations should throw on unresolvable
        /// state rather than returning empty — <see cref="AppUpdater"/> trusts a non-empty
        /// return.
        /// </summary>
        string GetBaseUrl();
    }
}
