using Semver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.App.Migrations
{
    public interface IMigrator
    {
        /// <summary>
        /// Migration begins from <see cref="ApplyOnVersionEqualsOrBefore"/> down to last running version.
        /// </summary>
        SemVersion ApplyOnVersionEqualsOrBefore { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Context using in <see cref="AbstractMigrator.MigrateAfterDbMigrationInternal"/></returns>
        Task MigrateBeforeDbMigration();

        Task MigrateAfterDbMigration();
    }
}
