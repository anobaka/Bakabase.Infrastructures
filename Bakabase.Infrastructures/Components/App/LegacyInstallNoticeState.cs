namespace Bakabase.Infrastructures.Components.App
{
    /// <summary>
    /// Singleton holder for the legacy-install detection result. Set by
    /// <see cref="LegacyInstallAppDataDetector"/> on startup; replayed to newly-connected
    /// hub clients by <c>WebGuiHub.GetInitialData()</c>; cleared when the user dismisses.
    /// Keeping this in process memory (rather than only pushing through SignalR) means
    /// late-connecting clients still see the notice — the timing of detection vs.
    /// SignalR connection is irrelevant.
    /// </summary>
    public sealed class LegacyInstallNoticeState
    {
        public string? PendingPath { get; set; }
    }
}
