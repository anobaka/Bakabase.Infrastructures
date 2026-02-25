using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;

namespace Bakabase.Infrastructures.Components.Orm.Log
{
    [Obsolete]
    public class LogDbContext : global::Bootstrap.Components.Logging.LogService.LogDbContext
    {
        public LogDbContext([NotNull] DbContextOptions<LogDbContext> options) : base(options)
        {
            Database.OpenConnection();
            // cache_size is working with current connection only.
            Database.ExecuteSqlRaw($"PRAGMA cache_size = {5_000_000}");
            Database.ExecuteSqlRaw("PRAGMA incremental_vacuum(200)");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<global::Bootstrap.Components.Logging.LogService.Models.Entities.Log>()
                .HasIndex(l => l.DateTime);
        }
    }
}