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
            Database.OpenConnection();
            // cache_size is working with current connection only.
            Database.ExecuteSqlRaw($"PRAGMA cache_size = {5_000_000}");
        }
    }
}
