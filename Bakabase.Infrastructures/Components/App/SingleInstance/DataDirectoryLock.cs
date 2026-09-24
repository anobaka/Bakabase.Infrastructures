using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Bakabase.Infrastructures.Components.App.SingleInstance
{
    /// <summary>What <see cref="DataDirectoryLock.TryAcquire"/> found.</summary>
    public enum DataDirectoryLockStatus
    {
        /// <summary>This process now owns the directory.</summary>
        Acquired = 1,

        /// <summary>Another process owns it — a running instance.</summary>
        HeldByAnotherProcess = 2,

        /// <summary>
        /// The lock could not be taken for some other reason — a read-only volume, a missing
        /// permission, a path that cannot be created. Nothing is known about other instances.
        /// </summary>
        Unavailable = 3,
    }

    public sealed record DataDirectoryLockAttempt(
        DataDirectoryLockStatus Status,
        DataDirectoryLock? Lock,
        Exception? Error)
    {
        public bool Acquired => Status == DataDirectoryLockStatus.Acquired;
    }

    /// <summary>
    /// An exclusive operating-system lock on <see cref="FileName"/> inside a data directory,
    /// held for as long as this object lives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lock is the file handle itself, opened with <see cref="FileShare.None"/>: a
    /// sharing-mode lock on Windows and <c>flock(LOCK_EX)</c> on macOS and Linux. Both are
    /// owned by the handle, not the file, so the operating system drops them the instant the
    /// process ends, however it ends — a crash, <c>kill -9</c>, a power cut. There is
    /// deliberately no "is the owner still alive?" logic on top: a stale lock cannot exist,
    /// so there is nothing that could misjudge one and lock the user out of their own data.
    /// </para>
    /// <para>
    /// Unlike a named mutex this works across login sessions and users (a Unix named mutex
    /// lives in a per-session namespace, which is how a terminal launch used to slip past a
    /// running app), and follows the directory rather than the executable, so a copy of the
    /// app pointed at another data directory is not mistaken for a duplicate.
    /// </para>
    /// <para>
    /// The file is never deleted while in use. Deleting it would let the next process
    /// create a fresh file and lock that while this one still holds the unlinked original.
    /// Its contents — the owner's process id and when it took the lock — are there for a
    /// human looking at the directory; nothing reads them back, and on Windows the sharing
    /// mode keeps other processes from reading them at all.
    /// </para>
    /// <para>
    /// On a file system without advisory locks (some network shares), .NET opens the file
    /// without locking it and reports success. The guard then degrades to having no guard,
    /// which is what every build before it had — never to refusing to start.
    /// </para>
    /// </remarks>
    public sealed class DataDirectoryLock : IDisposable
    {
        public const string FileName = ".bakabase.lock";

        /// <summary><c>EWOULDBLOCK</c>, which .NET passes through as the exception's HResult.</summary>
        private const int EWouldBlockLinux = 11;
        private const int EWouldBlockDarwin = 35;

        private const int ErrorSharingViolation = 32;
        private const int ErrorLockViolation = 33;

        private FileStream? _stream;

        private DataDirectoryLock(string directory, FileStream stream, bool createdDirectory)
        {
            Directory = directory;
            _stream = stream;
            CreatedDirectory = createdDirectory;
        }

        /// <summary>The directory as it was given.</summary>
        public string Directory { get; }

        /// <summary>
        /// Whether taking the lock is what brought the directory into existence — so that
        /// giving up on it (a relocation that did not go ahead) can leave the disk as it was.
        /// </summary>
        public bool CreatedDirectory { get; }

        public bool IsHeld => _stream != null;

        public static string GetLockFilePath(string directory) => Path.Combine(directory, FileName);

        /// <summary>
        /// Takes the lock on <paramref name="directory"/>, creating the directory when it does
        /// not exist yet. Never blocks and never throws.
        /// </summary>
        public static DataDirectoryLockAttempt TryAcquire(string directory)
        {
            FileStream? stream = null;
            try
            {
                var existed = System.IO.Directory.Exists(directory);
                System.IO.Directory.CreateDirectory(directory);

                stream = new FileStream(GetLockFilePath(directory), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, bufferSize: 1, FileOptions.None);

                TryRecordOwner(stream);

                var acquired = new DataDirectoryLock(directory, stream, createdDirectory: !existed);
                stream = null;
                return new DataDirectoryLockAttempt(DataDirectoryLockStatus.Acquired, acquired, null);
            }
            catch (IOException e) when (IsHeldElsewhere(e))
            {
                return new DataDirectoryLockAttempt(DataDirectoryLockStatus.HeldByAnotherProcess, null, e);
            }
            catch (Exception e)
            {
                return new DataDirectoryLockAttempt(DataDirectoryLockStatus.Unavailable, null, e);
            }
            finally
            {
                stream?.Dispose();
            }
        }

        /// <summary>
        /// Whether <paramref name="e"/> is the operating system saying "someone else has this
        /// file locked", as opposed to any other reason the open failed.
        /// </summary>
        internal static bool IsHeldElsewhere(IOException e)
        {
            if (OperatingSystem.IsWindows())
            {
                var code = e.HResult & 0xFFFF;
                return code is ErrorSharingViolation or ErrorLockViolation;
            }

            return e.HResult == (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()
                ? EWouldBlockLinux
                : EWouldBlockDarwin);
        }

        /// <summary>
        /// Removes the lock file from a directory nobody owns any more — after a relocation
        /// has moved the data out of it. Takes the lock first and deletes on close, so a
        /// directory some other process has since claimed is left alone.
        /// </summary>
        /// <returns>Whether the file is gone.</returns>
        public static bool TryDeleteUnowned(string directory)
        {
            var path = GetLockFilePath(directory);
            if (!File.Exists(path))
            {
                return true;
            }

            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, bufferSize: 1,
                           FileOptions.DeleteOnClose))
                {
                }

                return !File.Exists(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void TryRecordOwner(FileStream stream)
        {
            try
            {
                using var self = Process.GetCurrentProcess();
                var text = string.Create(CultureInfo.InvariantCulture,
                    $"pid={Environment.ProcessId}\nlocked={DateTime.UtcNow:O}\nprocess={self.ProcessName}\n");
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.SetLength(0);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: false);
            }
            catch
            {
                // Cosmetic. The lock is the open handle, not these bytes.
            }
        }

        public void Dispose()
        {
            var stream = _stream;
            _stream = null;
            stream?.Dispose();
        }
    }
}
