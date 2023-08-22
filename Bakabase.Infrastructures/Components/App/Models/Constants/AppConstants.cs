using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Semver;

namespace Bakabase.Infrastructures.Components.App.Models.Constants
{
    public class AppConstants
    {
        public const string InitialVersion = "0.0.0";
        public static SemVersion InitialSemVersion = SemVersion.Parse(InitialVersion, SemVersionStyles.Any);
    }
}
