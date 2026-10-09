using System;
using System.Linq;

namespace Bakabase.Infrastructures.Components.App;

/// <summary>External activation opens a page only. No file, server, query or action payload is accepted.</summary>
public static class DesktopToolLink
{
    public const string Argument = "--desktop-tool";
    public const string FileProcessor = "bakabase://tools/file-processor";
    public const string FileNameModifier = "bakabase://tools/file-name-modifier";

    public static string? GetRoute(string? link) => link switch
    {
        FileProcessor => "/file-processor",
        FileNameModifier => "/file-name-modifier",
        _ => null
    };

    public static string? FromArguments(string[] args) => args.FirstOrDefault(arg => GetRoute(arg) != null);

    /// <summary>
    /// Windows substitutes an untrusted URI into the registered command. Reject extra arguments
    /// (including quote injection) before any setup/child or configuration arguments are consumed.
    /// </summary>
    public static bool TryNormalizeArguments(string[] args, out string[] normalized)
    {
        normalized = args;
        if (!args.Contains(Argument, StringComparer.Ordinal)) return true;
        if (args.Length != 2 || args[0] != Argument || GetRoute(args[1]) == null) return false;
        normalized = [args[1]];
        return true;
    }

    public static string LocalPage(string localAddress, string route)
    {
        if (route is not ("/file-processor" or "/file-name-modifier"))
            throw new ArgumentException("Unknown desktop tool page.", nameof(route));
        var address = new Uri(localAddress, UriKind.Absolute);
        if (address.Scheme is not ("http" or "https") || !address.IsLoopback || address.UserInfo.Length != 0)
            throw new ArgumentException("Desktop tools require this application's loopback server.", nameof(localAddress));
        return new UriBuilder(address) { Fragment = route, Query = "", Path = "/" }.Uri.AbsoluteUri;
    }
}
