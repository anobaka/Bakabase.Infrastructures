using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Bakabase.Infrastructures.Components.App.Relocation
{
    /// <summary>
    /// Post-copy integrity check used by <see cref="PendingRelocationRunner"/>. Walks an
    /// explicit list of <see cref="ExpectedFile"/>s recorded during the copy step and verifies
    /// each landed at the expected location with the expected size. Also runs
    /// <c>PRAGMA integrity_check</c> on any SQLite databases included in that list.
    ///
    /// This list-based contract replaced the older "expected file count + total bytes"
    /// pre-flight: it works correctly under merge-overwrite (where the target may legitimately
    /// have extra files) and avoids the O(N) recursive stat of the source that the old
    /// pre-flight required.
    /// </summary>
    public static class RelocationIntegrityValidator
    {
        public readonly record struct ExpectedFile(string RelPath, long Bytes);

        public sealed class Result
        {
            public bool Ok { get; set; }
            public string? FailureReason { get; set; }
            public List<string> CorruptedDbFiles { get; } = new();

            public static Result Failure(string reason) => new() { Ok = false, FailureReason = reason };
        }

        public static Result Validate(
            string root,
            IReadOnlyList<ExpectedFile> expected,
            IEnumerable<string>? sqliteRelativePaths = null)
        {
            if (!Directory.Exists(root))
            {
                return Result.Failure($"Root '{root}' does not exist.");
            }

            foreach (var entry in expected)
            {
                var path = Path.Combine(root, entry.RelPath);
                if (!File.Exists(path))
                {
                    return Result.Failure($"Expected file missing: '{entry.RelPath}'.");
                }

                long actual;
                try
                {
                    actual = new FileInfo(path).Length;
                }
                catch (Exception ex)
                {
                    return Result.Failure($"Could not stat '{entry.RelPath}': {ex.Message}");
                }

                if (actual != entry.Bytes)
                {
                    return Result.Failure(
                        $"Size mismatch for '{entry.RelPath}': expected {entry.Bytes}, got {actual}.");
                }
            }

            var result = new Result();
            if (sqliteRelativePaths != null)
            {
                foreach (var rel in sqliteRelativePaths)
                {
                    var dbPath = Path.Combine(root, rel);
                    if (!File.Exists(dbPath)) continue;
                    if (!IntegrityCheckSqlite(dbPath, out var error))
                    {
                        result.CorruptedDbFiles.Add(rel);
                        result.Ok = false;
                        result.FailureReason =
                            $"sqlite integrity_check failed for '{rel}': {error}";
                        return result;
                    }
                }
            }

            result.Ok = true;
            return result;
        }

        private static bool IntegrityCheckSqlite(string dbPath, out string? error)
        {
            error = null;
            try
            {
                using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check;";
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var value = reader.GetString(0);
                    if (string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    error = value;
                    return false;
                }

                error = "PRAGMA integrity_check returned no rows.";
                return false;
            }
            catch (DbException ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
