using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Bakabase.Infrastructures.Components.App;

public static class OsShell
{
    /// <param name="path">Target file or directory.</param>
    /// <param name="openInDirectory">True: reveal <paramref name="path"/> in its parent directory; false: open <paramref name="path"/> itself.</param>
    public static void Open(string path, bool openInDirectory)
    {
        var quotedPath = $"\"{path}\"";

        string command;
        string arguments;
        var useShellExecute = true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            command = "explorer";
            var windowsPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var windowsQuotedPath = $"\"{windowsPath}\"";
            arguments = openInDirectory ? $"/select,{windowsQuotedPath}" : windowsQuotedPath;
            useShellExecute = false;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            command = "open";
            arguments = openInDirectory ? $"-R {quotedPath}" : quotedPath;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            command = "xdg-open";
            arguments = quotedPath;
        }
        else
        {
            throw new PlatformNotSupportedException();
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = useShellExecute
        });
    }
}
