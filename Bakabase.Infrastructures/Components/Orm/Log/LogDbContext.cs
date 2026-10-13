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
            this.SetSqlitePageCacheBudget(SqliteExtensions.AuxiliaryPageCacheBudgetKiB);
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
