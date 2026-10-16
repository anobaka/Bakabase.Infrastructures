using System;
using System.Collections.Generic;
using System.Diagnostics;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;

namespace Bakabase.Infrastructures.Components.App.Profiles;

/// <summary>Preserves Velopack's own command construction and records the updater's actual OS identity.</summary>
internal sealed class ProfileUpdateLocator(IVelopackLocator inner) : IVelopackLocator
{
    public string AppId => inner.AppId;
    public string RootAppDir => inner.RootAppDir;
    public string PackagesDir => inner.PackagesDir;
    public string AppContentDir => inner.AppContentDir;
    public string AppTempDir => inner.AppTempDir;
    public string UpdateExePath => inner.UpdateExePath;
    public SemanticVersion CurrentlyInstalledVersion => inner.CurrentlyInstalledVersion;
    public string ThisExeRelativePath => inner.ThisExeRelativePath;
    public string Channel => inner.Channel;
    public string AppUserModelId => inner.AppUserModelId;
    public IVelopackLogger Log => inner.Log;
    public bool IsPortable => inner.IsPortable;
    public IProcessImpl Process { get; } = new TrackingProcess(inner.Process);
    public void AddLogger(IVelopackLogger logger) => inner.AddLogger(logger);
    public List<VelopackAsset> GetLocalPackages() => inner.GetLocalPackages();
    public VelopackAsset GetLatestLocalFullPackage() => inner.GetLatestLocalFullPackage();
    public Guid? GetOrCreateStagedUserId() => inner.GetOrCreateStagedUserId();

    private sealed class TrackingProcess(IProcessImpl inner) : IProcessImpl
    {
        public string GetCurrentProcessPath() => inner.GetCurrentProcessPath();
        public uint GetCurrentProcessId() => inner.GetCurrentProcessId();
        public void Exit(int exitCode) => inner.Exit(exitCode);
        public void StartProcess(string exePath, IEnumerable<string> args, string workDir, bool showWindow)
        {
            var info = new ProcessStartInfo(exePath)
            { WorkingDirectory = workDir, UseShellExecute = false, CreateNoWindow = !showWindow };
            foreach (var argument in args) info.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(info)
                                ?? throw new InvalidOperationException("The application updater did not start.");
            DesktopInstallationInstances.RecordUpdater(process);
        }
    }
}
