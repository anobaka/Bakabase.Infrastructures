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
        /// Migration begins from <see cref="MaxVersion"/> down to last running version.
        /// </summary>
        SemVersion MaxVersion { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Context using in <see cref="AbstractMigrator.MigrateAfterDbMigrationInternal"/></returns>
        Task MigrateBeforeDbMigration();

        Task MigrateAfterDbMigration();
    }
}
