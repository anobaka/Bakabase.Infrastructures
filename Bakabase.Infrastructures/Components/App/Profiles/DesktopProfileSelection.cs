using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Bakabase.Infrastructures.Components.App.Profiles;

/// <summary>Resolved once, before any application data is read. Only registry IDs are launch arguments.</summary>
public static class DesktopProfileSelection
{
    public const string ArgumentPrefix = "--profile=";
    public static string? CurrentId { get; private set; }
    public static bool Enabled { get; private set; }

    public static void Initialize(string[] args, DesktopProfileRegistry? registry = null)
    {
        if (Enabled) throw new InvalidOperationException("Desktop data-space selection was already initialized.");
        registry ??= DesktopProfileRegistry.ForCurrentUser();
        var requested = ParseId(args);
        Enabled = true;
        // Existing advanced launchers keep their anchor and fixed-path semantics. The user can
        // register it later; a normal profile launch removes the inherited environment override.
        if (requested == null && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DefaultAppDataPathResolver.EnvVarName))) return;
        var catalog = registry.Read();
        var profile = registry.Get(requested ?? catalog.DefaultId);
        var data = AppDataLocator.ResolveEffectiveDataDirectory(profile.AnchorDirectory);
        if (!profile.RequiresSetup && !Directory.Exists(data))
            throw new DirectoryNotFoundException("This data space is unavailable. Reconnect its storage before opening it: " + data);
        CurrentId = profile.Id;
        // Preserve the historical migration path only for the unchanged default anchor.
        AppDataAnchor.Use(profile.Id == "default" &&
                          DesktopProfileRegistry.SamePath(profile.AnchorDirectory, AppDataLocator.ResolvePlatformDefaultAnchor()) &&
                          string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DefaultAppDataPathResolver.EnvVarName))
            ? AppDataPathProfile.AllInOne
            : AppDataPathProfile.AllInOne with { AnchorDirectory = profile.AnchorDirectory });
    }

    public static string? ParseId(string[] args)
    {
        var values = args.Where(a => a.StartsWith(ArgumentPrefix, StringComparison.Ordinal)).ToArray();
        if (values.Length == 0) return null;
        if (values.Length != 1 || !DesktopProfileRegistry.ValidId(values[0][ArgumentPrefix.Length..]))
            throw new ArgumentException("Choose a registered data-space ID; paths cannot be passed as profile arguments.");
        return values[0][ArgumentPrefix.Length..];
    }

    public static string WindowTitle(string title)
    {
        if (!Enabled || CurrentId == null) return title;
        var catalog = DesktopProfileRegistry.ForCurrentUser().Read();
        var profile = catalog.Profiles.FirstOrDefault(p => p.Id == CurrentId);
        if (profile == null || catalog.Profiles.Count == 1 && string.IsNullOrEmpty(profile.Name)) return title;
        var name = string.IsNullOrEmpty(profile.Name)
            ? System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "默认数据空间" : "Default data space"
            : profile.Name;
        return title + " — " + name;
    }

    public static void EnsureDataTargetAllowed(string target)
    {
        if (!Enabled) return;
        DesktopProfileRegistry.ForCurrentUser().ValidateDataTarget(target, AppDataLocator.ResolveEffectiveDataDirectory());
    }

    public static void PreserveForRestart(ProcessStartInfo startInfo)
    {
        if (CurrentId == null) return;
        startInfo.ArgumentList.Add(ArgumentPrefix + CurrentId);
        startInfo.Environment.Remove(DefaultAppDataPathResolver.EnvVarName);
    }

    public static ProcessStartInfo CreateStartInfo(string profileId)
    {
        if (!DesktopProfileRegistry.ValidId(profileId)) throw new ArgumentException("Invalid data-space ID.");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the desktop application.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = System.AppContext.BaseDirectory };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        info.ArgumentList.Add(ArgumentPrefix + profileId);
        info.Environment.Remove(DefaultAppDataPathResolver.EnvVarName);
        info.Environment.Remove("BAKABASE_SETUP_PIPE");
        info.Environment.Remove("BAKABASE_SETUP_SECRET");
        return info;
    }
}
