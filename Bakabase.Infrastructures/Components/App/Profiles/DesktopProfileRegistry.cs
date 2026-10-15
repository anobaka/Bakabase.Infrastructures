using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Infrastructures.Components.App.Relocation;

namespace Bakabase.Infrastructures.Components.App.Profiles;

public sealed record DesktopProfile(string Id, string Name, string AnchorDirectory, bool RequiresSetup = false);
public sealed record DesktopProfileCatalog(int Version, string DefaultId, List<DesktopProfile> Profiles);

/// <summary>Machine-local launch control, deliberately outside every importable data directory.</summary>
public sealed class DesktopProfileRegistry(string rootDirectory, string legacyAnchor)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string RootDirectory { get; } = Path.GetFullPath(rootDirectory);
    public string LegacyAnchor { get; } = Path.GetFullPath(legacyAnchor);
    private string CatalogPath => Path.Combine(RootDirectory, "profiles.json");
    public static DesktopProfileRegistry ForCurrentUser()
    {
        var anchor = AppDataLocator.ResolvePlatformDefaultAnchor();
        return new DesktopProfileRegistry(anchor + ".profiles", anchor);
    }

    public DesktopProfileCatalog Read()
    {
        if (!File.Exists(CatalogPath)) return new(1, "default", [new("default", "", LegacyAnchor, true)]);
        try
        {
            if (new FileInfo(CatalogPath).Length > 1024 * 1024) throw new InvalidOperationException("Data-space registry is too large.");
            var state = JsonSerializer.Deserialize<DesktopProfileCatalog>(File.ReadAllText(CatalogPath), Json);
            if (state == null || state.Version != 1 || state.Profiles == null || state.Profiles.Count is < 1 or > 100 ||
                state.Profiles.Any(p => p == null || p.Id == null || p.Name == null || p.AnchorDirectory == null) ||
                state.Profiles.Select(p => p.Id).Distinct().Count() != state.Profiles.Count ||
                !state.Profiles.Any(p => p.Id == state.DefaultId) || state.Profiles.Any(p =>
                    !ValidId(p.Id) || p.Name.Length > 100 || !Path.IsPathFullyQualified(p.AnchorDirectory) ||
                    (p.Id == "default" ? !SamePath(p.AnchorDirectory, LegacyAnchor) :
                        !SamePath(p.AnchorDirectory, Path.Combine(RootDirectory, "anchors", p.Id)))))
                throw new InvalidOperationException("Invalid data-space registry.");
            return state;
        }
        catch (JsonException e) { throw new InvalidOperationException("Cannot read the data-space registry. Restore profiles.json from a backup; data directories have not been changed.", e); }
    }

    public DesktopProfile Get(string id) => Read().Profiles.SingleOrDefault(p => p.Id == id)
        ?? throw new InvalidOperationException("This data space is no longer registered.");

    public DesktopProfile Add(string name, string directory, bool create)
    {
        name = ValidateName(name);
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Choose an absolute data directory.");
        var target = AppDataLocator.ResolveEffectiveDataDirectory(Path.GetFullPath(directory));
        ValidateTarget(target);
        using var gate = AcquireWriteLock();
        var state = Read();
        var duplicate = state.Profiles.FirstOrDefault(p => SamePath(AppDataLocator.ResolveEffectiveDataDirectory(p.AnchorDirectory), target));
        if (duplicate != null)
        {
            if (create) throw new InvalidOperationException("This directory already belongs to a data space. Open that space instead.");
            return duplicate;
        }
        if (state.Profiles.Count >= 100) throw new InvalidOperationException("At most 100 data spaces may be registered.");
        foreach (var profile in state.Profiles)
        {
            var existing = AppDataLocator.ResolveEffectiveDataDirectory(profile.AnchorDirectory);
            if (ContainsPath(existing, target) || ContainsPath(target, existing))
                throw new InvalidOperationException("Data spaces cannot contain one another. Choose a separate directory.");
        }
        if (create && Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new InvalidOperationException("A new data space requires an empty directory. To keep existing data, add its directory instead.");
        if (!create && !Directory.Exists(target)) throw new DirectoryNotFoundException("The existing data directory is not available.");
        // Do not create/lock/initialize the target: its first launch must still run Setup.
        var id = Guid.NewGuid().ToString("N");
        var anchor = Path.Combine(RootDirectory, "anchors", id);
        PrivateDirectory(anchor);
        AnchorRedirect.Write(anchor, target);
        var added = new DesktopProfile(id, name, anchor, create);
        state.Profiles.Add(added);
        Write(state);
        return added;
    }

    public DesktopProfile Update(string id, string name, bool makeDefault)
    {
        name = ValidateName(name);
        using var gate = AcquireWriteLock();
        var state = Read();
        var index = state.Profiles.FindIndex(p => p.Id == id);
        if (index < 0) throw new InvalidOperationException("This data space is no longer registered.");
        var updated = state.Profiles[index] with { Name = name };
        state.Profiles[index] = updated;
        Write(makeDefault ? state with { DefaultId = id } : state);
        return updated;
    }

    public void MarkInitialized(string id)
    {
        using var gate = AcquireWriteLock();
        var state = Read();
        var index = state.Profiles.FindIndex(p => p.Id == id);
        if (index < 0 || !state.Profiles[index].RequiresSetup) return;
        state.Profiles[index] = state.Profiles[index] with { RequiresSetup = false };
        Write(state);
    }

    public void Remove(string id, string? currentProfileId)
    {
        if (id == currentProfileId) throw new InvalidOperationException("Switch to another data space before removing this registration.");
        using var gate = AcquireWriteLock();
        var state = Read();
        if (state.Profiles.Count == 1) throw new InvalidOperationException("Keep at least one registered data space.");
        state.Profiles.RemoveAll(p => p.Id == id);
        if (state.DefaultId == id) state = state with { DefaultId = state.Profiles[0].Id };
        // Anchors also hold browser data. Retain them, as well as all user data; removal is registration only.
        Write(state);
    }

    public void ValidateDataTarget(string target, string? currentDirectory = null)
    {
        ValidateTarget(target);
        foreach (var profile in Read().Profiles)
        {
            var existing = AppDataLocator.ResolveEffectiveDataDirectory(profile.AnchorDirectory);
            if (currentDirectory != null && SamePath(existing, currentDirectory)) continue;
            if (ContainsPath(existing, target) || ContainsPath(target, existing))
                throw new InvalidOperationException("This directory belongs to another registered data space. Choose a separate directory.");
        }
    }

    private void ValidateTarget(string target)
    {
        if (ContainsPath(RootDirectory, target) || ContainsPath(target, RootDirectory))
            throw new InvalidOperationException("The launch-control directory cannot be used as a data directory.");
        if (ContainsPath(DataPathValidator.FindVelopackInstallRootDefault(System.AppContext.BaseDirectory) ?? System.AppContext.BaseDirectory, target))
            throw new InvalidOperationException("Choose a data directory outside the application installation.");
        for (var parent = Directory.GetParent(target); parent != null; parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, "app.json")))
                throw new InvalidOperationException("A data space cannot be placed inside an existing data directory.");
    }
    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 100 || name.Any(char.IsControl)) throw new ArgumentException("Use a data-space name of 1–100 characters.");
        return name;
    }
    internal static bool ValidId(string id) => id == "default" || Guid.TryParseExact(id, "N", out _);
    internal static bool SamePath(string a, string b) => DataDirectoryIdentity.Normalize(a) == DataDirectoryIdentity.Normalize(b);
    private static bool ContainsPath(string parent, string child)
    {
        parent = DataDirectoryIdentity.Normalize(parent);
        child = DataDirectoryIdentity.Normalize(child);
        return child == parent || child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
    internal static void PrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private FileStream AcquireWriteLock()
    {
        PrivateDirectory(RootDirectory);
        var timeout = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(Path.Combine(RootDirectory, ".registry.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timeout.Elapsed < TimeSpan.FromSeconds(5)) { Thread.Sleep(25); }
        }
    }
    private void Write(DesktopProfileCatalog state)
    {
        var temp = CatalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, CatalogPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
