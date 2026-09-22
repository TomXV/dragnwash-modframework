using System;
using System.IO;
using System.Text;

namespace DragNWash.ModFramework
{
    // Writing a file by truncating it and streaming into it costs the player
    // their data when anything throws partway through: the previous content is
    // already gone and only half the new content is there.
    //
    // Write() puts the new content in a temporary file beside the target and
    // moves it into place once the write has finished, so the target holds
    // either the old content or the new one, never a truncated mixture. From
    // the localization mod, where it protects a translator's saved work.
    /// <summary>
    /// Writes a file so a crash or a sharing violation partway through never
    /// leaves it truncated: the new content goes into a temporary file beside
    /// the target first, which is then moved into place.
    /// </summary>
    public static class SafeFile
    {
        // Names tried for the temporary file, in order. The first is the plain
        // one; the rest exist because a leftover ".tmp" that some other program
        // still holds open must not stop the write.
        private const int TempAttempts = 4;

        /// <summary>
        /// Writes <paramref name="path"/> atomically: <paramref name="write"/> fills
        /// a temporary file beside the target, which is then moved into place, so
        /// the target holds either the old content or the new one, never a
        /// truncated mixture. Falls back to writing the target directly (losing
        /// only the atomicity, not the content) when no temporary file can be
        /// opened beside it.
        /// </summary>
        public static void Write(string path, Encoding encoding, Action<StreamWriter> write)
        {
            StreamWriter writer = OpenTemp(path, encoding, out string temp);
            if (writer == null)
            {
                // No temporary file could be opened beside the target at all.
                // Writing the target in place is what plain File.WriteAllText
                // does, so do that rather than refuse to save; the atomicity is
                // what is lost here, not the caller's content.
                using (var direct = new StreamWriter(path, append: false, encoding))
                {
                    write(direct);
                }
                return;
            }
            try
            {
                using (writer)
                {
                    write(writer);
                }
            }
            catch
            {
                Discard(temp);
                throw;
            }
            Commit(temp, path);
        }

        // Beside the target on purpose: the move is only cheap and atomic when
        // both are on the same volume, which a temp directory cannot promise.
        private static StreamWriter OpenTemp(string path, Encoding encoding, out string temp)
        {
            for (int i = 0; i < TempAttempts; i++)
            {
                string candidate = i == 0 ? path + ".tmp" : path + "." + i + ".tmp";
                try
                {
                    StreamWriter writer = new StreamWriter(candidate, append: false, encoding);
                    temp = candidate;
                    return writer;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            temp = null;
            return null;
        }

        private static void Commit(string temp, string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    File.Move(temp, path);
                    return;
                }
                try
                {
                    // One step from the old content to the new one: a reader
                    // opening the path sees one or the other, never a partial
                    // file. It does not preserve the target's identity - the
                    // directory entry ends up pointing at the temporary file's
                    // data, so its file index changes and a handle opened
                    // beforehand keeps reading the old content until it is
                    // reopened. That was already true of the plain write this
                    // replaces, which is why it is acceptable here.
                    File.Replace(temp, path, null, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    // Some Mono builds do not implement File.Replace. The window
                    // here is a single delete rather than a whole file write.
                    File.Delete(path);
                    File.Move(temp, path);
                }
                catch (IOException)
                {
                    // Another program holds the target open and does not allow
                    // it to be deleted or renamed, which both File.Replace and
                    // the delete above need. Writing into the file that is
                    // already there still works, and it is what a plain write
                    // did before, so do that rather than refuse to save. The
                    // content is already complete in the temporary file, so the
                    // copy is short.
                    Overwrite(temp, path);
                    Discard(temp);
                }
            }
            catch
            {
                // Never leave a stray .tmp beside the target.
                Discard(temp);
                throw;
            }
        }

        private static void Overwrite(string temp, string path)
        {
            // FileShare.Read matches what StreamWriter asks for, so this
            // succeeds exactly where the previous, non-atomic write did.
            using (var source = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                source.CopyTo(target);
            }
        }

        private static void Discard(string temp)
        {
            try
            {
                if (temp != null && File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // A leftover .tmp is untidy, not harmful; never mask the
                // original failure with a cleanup failure.
            }
        }
    }
}
