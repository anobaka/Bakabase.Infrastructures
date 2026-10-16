using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Bakabase.Infrastructures.Components.App.SingleInstance;

namespace Bakabase.Infrastructures.Components.App.Profiles;

/// <summary>Only public coordinators count as instances. Durable update tickets bridge their exit and the updater.</summary>
public sealed class DesktopInstallationInstances
{
    internal sealed record ProcessIdentity(int Id, long StartedAt);
    internal sealed record UpdateTicket(string BuildId, ProcessIdentity Owner, ProcessIdentity Coordinator,
        string UpdaterPath, ProcessIdentity? Updater = null);
    private static UpdateReservation? _updateReservation;
    private static bool _updaterStarted;
    private readonly string _directory;
    private readonly string _buildId;
    private readonly Func<ProcessIdentity, bool> _isAlive;
    private readonly Func<string, bool> _updaterAtPathRunning;
    private string TicketPath => Path.Combine(_directory, "update.json");

    public DesktopInstallationInstances(string directory) : this(directory,
        Assembly.GetEntryAssembly()?.ManifestModule.ModuleVersionId.ToString() ?? "unknown", IsAlive, UpdaterAtPathRunning) { }
    internal DesktopInstallationInstances(string directory, string buildId, Func<ProcessIdentity, bool> isAlive,
        Func<string, bool> updaterAtPathRunning)
    { _directory = directory; _buildId = buildId; _isAlive = isAlive; _updaterAtPathRunning = updaterAtPathRunning; }

    public static DesktopInstallationInstances Current => new(Path.Combine(
        DesktopProfileRegistry.ForCurrentUser().RootDirectory, "installations",
        DataDirectoryIdentity.Hash(DataDirectoryIdentity.Normalize(System.AppContext.BaseDirectory))));
    public static IDisposable RegisterCoordinator() => Current.Register(Environment.ProcessId);

    public IDisposable Register(int processId)
    {
        using var admission = Admission();
        var file = Path.Combine(_directory, processId + ".running");
        var handle = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        handle.SetLength(0);
        handle.Write(Encoding.UTF8.GetBytes(DesktopProfileSelection.CurrentId ?? "environment"));
        handle.Flush();
        return handle;
    }
    public void AssertOnlyCoordinator(int processId)
    {
        foreach (var file in Directory.EnumerateFiles(_directory, "*.running"))
        {
            if (Path.GetFileNameWithoutExtension(file) == processId.ToString()) continue;
            try { using var inactive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (FileNotFoundException) { }
            catch (IOException)
            {
                throw new InvalidOperationException("Quit the other Bakabase workspaces from this installation before updating. Their data is safe; no process will be forcibly closed.");
            }
        }
    }

    public static void ReserveUpdate(int coordinatorProcessId, string updaterPath)
    {
        if (!DesktopProfileSelection.Enabled) return;
        if (_updateReservation != null) throw new InvalidOperationException("An update is already being prepared.");
        _updateReservation = Current.Reserve(coordinatorProcessId, updaterPath, Identity(Environment.ProcessId), Identity(coordinatorProcessId));
    }
    internal UpdateReservation Reserve(int coordinatorId, string updaterPath, ProcessIdentity owner, ProcessIdentity coordinator)
    {
        var lease = Admission();
        try
        {
            AssertOnlyCoordinator(coordinatorId);
            var ticket = new UpdateTicket(_buildId, owner, coordinator, Path.GetFullPath(updaterPath));
            WriteTicket(ticket);
            return new UpdateReservation(this, lease, ticket);
        }
        catch { lease.Dispose(); throw; }
    }
    public static void RecordUpdater(Process updater)
    {
        _updaterStarted = true;
        _updateReservation?.RecordUpdater(Identity(updater.Id));
    }
    public static void CancelUpdate()
    {
        var reservation = Interlocked.Exchange(ref _updateReservation, null);
        if (_updaterStarted) reservation?.ReleaseLease(); // keep the durable spawn intent even if PID persistence failed
        else reservation?.Cancel();
    }

    internal sealed class UpdateReservation(DesktopInstallationInstances installation, FileStream lease, UpdateTicket ticket)
    {
        private FileStream? _lease = lease;
        internal void RecordUpdater(ProcessIdentity updater)
        {
            ticket = ticket with { Updater = updater };
            installation.WriteTicket(ticket);
            // The persisted process identity, not this soon-to-exit process's FileStream,
            // blocks old binaries until Update.exe has finished replacing the installation.
            Interlocked.Exchange(ref _lease, null)?.Dispose();
        }
        internal void Cancel()
        {
            File.Delete(installation.TicketPath);
            Interlocked.Exchange(ref _lease, null)?.Dispose();
        }
        internal void ReleaseLease() => Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
    private FileStream Admission()
    {
        DesktopProfileRegistry.PrivateDirectory(_directory);
        FileStream lease;
        try { lease = new FileStream(Path.Combine(_directory, ".update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw Updating(e); }
        try
        {
            if (!File.Exists(TicketPath)) return lease;
            if (new FileInfo(TicketPath).Length > 65536) throw new InvalidOperationException("The desktop update ticket is too large.");
            var ticket = JsonSerializer.Deserialize<UpdateTicket>(File.ReadAllText(TicketPath))
                         ?? throw new InvalidOperationException("The desktop update ticket is unreadable. Restore it before launching this installation.");
            if (string.IsNullOrWhiteSpace(ticket.BuildId) || ticket.Owner is not { Id: > 0, StartedAt: > 0 } ||
                ticket.Coordinator is not { Id: > 0, StartedAt: > 0 } || string.IsNullOrWhiteSpace(ticket.UpdaterPath) ||
                !Path.IsPathFullyQualified(ticket.UpdaterPath) ||
                ticket.Updater is { } updater && (updater.Id <= 0 || updater.StartedAt <= 0))
                throw new InvalidOperationException("The desktop update ticket is invalid; no new workspace was opened.");
            // A new loaded assembly proves the atomic release swap has finished. Velopack
            // can still be alive momentarily while it launches the upgraded application.
            if (ticket.BuildId != _buildId) { File.Delete(TicketPath); return lease; }
            if (_isAlive(ticket.Owner) || _isAlive(ticket.Coordinator) ||
                ticket.Updater != null && _isAlive(ticket.Updater) ||
                ticket.Updater == null && _updaterAtPathRunning(ticket.UpdaterPath)) throw Updating();
            // Preparation failed, or Update.exe exited without installing. No live owner or
            // updater can replace files now; normal launching is safe without manual cleanup.
            File.Delete(TicketPath);
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    private void WriteTicket(UpdateTicket ticket)
    {
        var temporary = TicketPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(ticket));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, TicketPath, true);
    }
    private static InvalidOperationException Updating(Exception? cause = null) => new(
        "Bakabase is applying an update. Wait until it has restarted before opening another workspace.", cause);
    private static ProcessIdentity Identity(int id)
    {
        using var process = Process.GetProcessById(id);
        return new(id, process.StartTime.ToUniversalTime().Ticks);
    }
    private static bool IsAlive(ProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.Id);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == identity.StartedAt;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; } // cannot prove it exited
    }
    private static bool UpdaterAtPathRunning(string path)
    {
        // Covers a crash between StartProcess and recording its PID. Never terminate it.
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && process.MainModule?.FileName is { } executable &&
                        DesktopProfileRegistry.SamePath(executable, path)) return true;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { return true; }
            }
        }
        return false;
    }
}
