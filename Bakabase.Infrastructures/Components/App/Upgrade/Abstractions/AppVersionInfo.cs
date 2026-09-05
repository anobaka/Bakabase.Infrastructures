using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    public class AppVersionInfo
    {
        /// <summary>
        /// The newer version found in <see cref="Channel"/>, or null when there is none. The
        /// rest of the properties are filled in either way, so the UI can explain a "no update"
        /// answer instead of just showing a green tick.
        /// </summary>
        public string? Version { get; set; }

        public Installer[] Installers { get; set; } = [];
        public string? Changelog { get; set; }

        /// <summary>
        /// Version of the assembly that is actually executing — what the app displays as its
        /// own version.
        /// </summary>
        public string? RunningVersion { get; set; }

        /// <summary>
        /// Version Velopack considers installed, read from the install manifest
        /// (<c>current/sq.version</c>). This — not <see cref="RunningVersion"/> — is the
        /// baseline every update check compares the feed against. Null when the app was not
        /// installed by Velopack.
        /// <para>
        /// The two diverge as soon as a build is dropped into an existing install directory,
        /// which is the ordinary local dev loop: the app then reports its own build's version
        /// while updates are decided against whatever Velopack installed last.
        /// </para>
        /// </summary>
        public string? InstalledVersion { get; set; }

        /// <summary>The update channel that was queried.</summary>
        public string? Channel { get; set; }

        /// <summary>
        /// Newest full release offered by <see cref="Channel"/>. Only resolved when no update
        /// was found, which is the case that needs explaining.
        /// </summary>
        public string? ChannelLatestVersion { get; set; }

        /// <summary>
        /// True when that channel's newest release is older than <see cref="InstalledVersion"/>,
        /// so the check cannot ever report an update until the channel changes — e.g. a
        /// pre-release build with the pre-release channel switched off, where the stable feed
        /// is by definition behind.
        /// </summary>
        public bool ChannelBehindInstalled { get; set; }

        /// <summary>
        /// True when no update check could run at all because the app was not installed by
        /// Velopack (a development build, or an unpacked copy).
        /// </summary>
        public bool UpdateCheckUnavailable { get; set; }

        public class Installer
        {
            public OSPlatform? OsPlatform { get; set; }
            public Architecture OsArchitecture { get; set; }
            public string Name { get; set; }
            public string Url { get; set; }

            public string ToCommand()
            {
                var segments = new object[]
                {
                    OsPlatform,
                    OsArchitecture,
                    Name,
                    Url
                };

                return string.Join('|', segments.Select(s => s.ToString()!.Replace('|', '_')));
            }

            public static Installer FromCommand(string str)
            {
                var segments = str?.Split('|');
                if (segments?.Any() == true)
                {
                    var p0 = segments[0];
                    var p1 = segments.Length > 1 ? segments[1] : null;
                    var p2 = segments.Length > 2 ? segments[2] : null;
                    var p3 = segments.Length > 3 ? segments[3] : null;

                    var installer = new Installer
                    {
                        Name = p2,
                        Url = p3
                    };

                    if (!string.IsNullOrEmpty(p0))
                    {
                        installer.OsPlatform = OSPlatform.Create(p0);
                    }

                    if (Enum.TryParse<Architecture>(p1, out var a))
                    {
                        installer.OsArchitecture = a;
                    }

                    return installer;
                }

                return null;
            }
        }
    }
}