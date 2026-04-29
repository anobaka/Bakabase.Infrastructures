using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Bakabase.Infrastructures.Components.App.Relocation
{
    /// <summary>
    /// Post-copy integrity check used by <see cref="PendingRelocationRunner"/>: confirms file
    /// counts and total bytes match expectations and that any sqlite databases under the
    /// staged directory pass <c>PRAGMA integrity_check</c>.
    /// </summary>
    public static class RelocationIntegrityValidator
    {
        public sealed class Result
        {
            public bool Ok { get; set; }
            public string? FailureReason { get; set; }
            public long ActualFiles { get; set; }
            public long ActualBytes { get; set; }
            public List<string> CorruptedDbFiles { get; } = new();

            public static Result Failure(string reason) => new() { Ok = false, FailureReason = reason };
        }

        public static Result Validate(
            string stagedRoot,
            long expectedFiles,
            long expectedTotalBytes,
            IEnumerable<string>? expectedSqliteRelativePaths = null)
        {
            if (!Directory.Exists(stagedRoot))
            {
                return Result.Failure($"Staged root '{stagedRoot}' does not exist.");
            }

            long fileCount = 0;
            long totalBytes = 0;

            foreach (var file in Directory.EnumerateFiles(stagedRoot, "*", SearchOption.AllDirectories))
            {
                fileCount++;
                try
                {
                    totalBytes += new FileInfo(file).Length;
                }
                catch (FileNotFoundException)
                {
                    // raced with cleanup — treat as 0
                }
            }

            var result = new Result
            {
                ActualFiles = fileCount,
                ActualBytes = totalBytes,
            };

            if (fileCount != expectedFiles)
            {
                result.Ok = false;
                result.FailureReason =
                    $"File count mismatch: expected {expectedFiles}, got {fileCount}.";
                return result;
            }

            // Allow a tolerance of zero — copying is byte-faithful unless mid-flight files
            // truncated. Any drift here means the source changed under us.
            if (totalBytes != expectedTotalBytes)
            {
                result.Ok = false;
                result.FailureReason =
                    $"Total bytes mismatch: expected {expectedTotalBytes}, got {totalBytes}.";
                return result;
            }

            if (expectedSqliteRelativePaths != null)
            {
                foreach (var rel in expectedSqliteRelativePaths)
                {
                    var dbPath = Path.Combine(stagedRoot, rel);
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
