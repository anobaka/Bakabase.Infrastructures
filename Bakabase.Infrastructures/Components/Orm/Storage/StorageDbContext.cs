using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using System;

namespace Bakabase.Infrastructures.Components.Orm.Storage
{
    [Obsolete]
    public class StorageDbContext : Components.Storage.StorageDbContext
    {
        public StorageDbContext([NotNull] DbContextOptions<StorageDbContext> options) : base(options)
        {
            Database.OpenConnection();
            // cache_size is working with current connection only.
            Database.ExecuteSqlRaw($"PRAGMA cache_size = {5_000_000}");
        }
    }
}
