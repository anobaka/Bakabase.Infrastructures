using System;
using System.IO;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.SingleInstance;

namespace Bakabase.Infrastructures.Components.App.Profiles;

/// <summary>One desktop coordinator per user registry, independent of the selected data directory.</summary>
public sealed class DesktopWorkspaceSession : IDisposable
{
    public const string SwitchPrefix = "WORKSPACE:";
    private readonly FileStream _lease;
    private DesktopWorkspaceSession(FileStream lease) => _lease = lease;
    public static string Channel(string registryDirectory) => ActivationChannel.GetName(
        DataDirectoryIdentity.Normalize(registryDirectory) + "|desktop-workspace");

    public static DesktopWorkspaceSession? TryAcquire(string registryDirectory)
    {
        DesktopProfileRegistry.PrivateDirectory(registryDirectory);
        var path = Path.Combine(registryDirectory, ".desktop-workspace.lock");
        if (new FileInfo(path).LinkTarget != null) throw new IOException("The desktop workspace lock cannot be a symbolic link.");
        try { return new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
        catch (IOException error) when (DataDirectoryLock.IsHeldElsewhere(error)) { return null; }
    }

    public static Task<bool> ActivateAsync(string registryDirectory, string message) =>
        ActivationChannel.TrySendAsync(Channel(registryDirectory), message, TimeSpan.FromSeconds(15));

    public void Dispose() => _lease.Dispose();
}
