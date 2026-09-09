using System;
using System.Threading;

namespace Bakabase.Infrastructures.Components.App
{
    /// <summary>
    /// The one place an entry point says which <see cref="AppDataPathProfile"/> this
    /// process runs under. Its first statement, before anything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate type from <see cref="AppService"/>, and that is not a style choice. C#
    /// runs a class's static constructor before <b>any</b> of its static members is
    /// touched — including the very call that would set the profile. Since
    /// <c>AppService</c>'s static constructor creates the data directory, runs the legacy
    /// migration and opens the log file, an <c>AppService.UseProfile(...)</c> would have
    /// done all of that against the default profile before its own body ran. Putting the
    /// switch on a type with no static constructor of its own is what makes it settable
    /// at all.
    /// </para>
    /// <para>
    /// The default is <see cref="AppDataPathProfile.AllInOne"/>, so the existing app
    /// never calls this and lands exactly where it always has.
    /// </para>
    /// </remarks>
    public static class AppDataAnchor
    {
        private static AppDataPathProfile _current = AppDataPathProfile.AllInOne;
        private static bool _resolved;
        private static readonly Lock Gate = new();

        /// <summary>
        /// The profile in force. Reading it fixes the choice: anything that consults the
        /// profile is about to act on it, and a later switch would leave the two halves
        /// of startup disagreeing about where the data lives.
        /// </summary>
        public static AppDataPathProfile Current
        {
            get
            {
                lock (Gate)
                {
                    _resolved = true;
                    return _current;
                }
            }
        }

        /// <summary>Whether anything has read <see cref="Current"/> yet.</summary>
        public static bool IsResolved
        {
            get
            {
                lock (Gate)
                {
                    return _resolved;
                }
            }
        }

        /// <summary>
        /// Declares the profile for this process. Throws once the anchor has been read,
        /// because by then a directory has been created and a log file opened under the
        /// old one — failing loudly beats running with two answers.
        /// </summary>
        public static void Use(AppDataPathProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);

            lock (Gate)
            {
                if (_resolved && !ReferenceEquals(_current, profile))
                {
                    throw new InvalidOperationException(
                        $"The AppData anchor was already resolved as '{_current.FolderName}' and cannot be " +
                        $"changed to '{profile.FolderName}'. Call {nameof(AppDataAnchor)}.{nameof(Use)} as the " +
                        "first statement of the entry point, before anything touches AppService.");
                }

                _current = profile;
            }
        }

        /// <summary>
        /// Puts the anchor back to its initial state. For tests only — nothing in the
        /// application may call this, since the directories are already created by then.
        /// </summary>
        internal static void ResetForTests()
        {
            lock (Gate)
            {
                _current = AppDataPathProfile.AllInOne;
                _resolved = false;
            }
        }
    }
}
