using System;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    /// <summary>
    /// Lets an HTTP update request hand the final Velopack launch to the desktop shell.
    /// The shell runs its graceful wind-down before invoking the callback.
    /// </summary>
    public interface IUpdateRestartCoordinator
    {
        /// <summary>
        /// Claim the exit before the HTTP response is sent. Returns false if the app is
        /// already closing or another update restart has been reserved.
        /// </summary>
        bool TryReserveUpdateRestart(Action launchUpdater);

        /// <summary>Start the reserved exit after the HTTP response has completed.</summary>
        void BeginReservedUpdateRestart();
    }
}
