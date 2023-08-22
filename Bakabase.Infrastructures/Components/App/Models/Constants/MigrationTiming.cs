using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.App.Models.Constants
{
    public enum MigrationTiming
    {
        BeforeDbMigration = 1,
        AfterDbMigration = 2,
    }
}
