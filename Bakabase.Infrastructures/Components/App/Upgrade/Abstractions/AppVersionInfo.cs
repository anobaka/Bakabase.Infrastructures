using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    public class AppVersionInfo
    {
        public string Version { get; set; } = null!;
        public Installer[] Installers { get; set; } = [];
        public string? Changelog { get; set; }

        public class Installer
        {
            public OSPlatform? OsPlatform { get; set; }
            public Architecture OsArchitecture { get; set; }
            public string Name { get; set; }
            public string Url { get; set; }
            public long Size { get; set; }

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