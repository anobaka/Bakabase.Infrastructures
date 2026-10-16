using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using System;

namespace Bakabase.Infrastructures.Components.Orm.SystemProperty
{
    [Obsolete]
    public class SystemPropertyDbContext : global::Bootstrap.Components.Configuration.SystemProperty.SystemPropertyDbContext
    {
        public SystemPropertyDbContext([NotNull] DbContextOptions<SystemPropertyDbContext> options) : base(options)
        {
            this.SetSqlitePageCacheBudget(SqliteExtensions.AuxiliaryPageCacheBudgetKiB);
        }
    }
}
