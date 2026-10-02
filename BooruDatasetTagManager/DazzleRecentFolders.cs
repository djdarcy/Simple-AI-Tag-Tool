using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool: the most recently opened dataset folders, newest first.
    /// Kept in its own file next to the exe (not settings.json) so ordinary settings
    /// saves never copy the list around. Writes go over the existing bytes in place,
    /// and Clear() overwrites the file with zeros and then random bytes before deleting
    /// it, so the folder names are not simply left in freed disk space. This is best
    /// effort: SSD wear levelling, NTFS journaling, backups and shadow copies can still
    /// hold older copies, which no application can reach.
    /// </summary>
    public class DazzleRecentFolders
    {
        public const int MaxCount = 5;

        private readonly string path;
        private List<string> folders = new List<string>();

        public DazzleRecentFolders(string appDir)
        {
            path = Path.Combine(appDir, "recent-folders.json");
            Load();
        }

        public IReadOnlyList<string> Folders => folders;

        private void Load()
        {
            try
            {
                if (File.Exists(path))
                    folders = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(path)) ?? new List<string>();
            }
            catch (Exception)
            {
                folders = new List<string>(); // unreadable: start a fresh list
            }
        }

        /// <summary>Move (or add) a folder to the top, keeping at most MaxCount.</summary>
        public void Add(string folder)
        {
            string full = Path.GetFullPath(folder).TrimEnd('\\');
            folders.RemoveAll(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase));
            folders.Insert(0, full);
            if (folders.Count > MaxCount)
                folders.RemoveRange(MaxCount, folders.Count - MaxCount);
            Save();
        }

        private void Save()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(folders, Formatting.Indented));
            // overwrite in place rather than writing a new file beside the old one
            using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                long oldLength = fs.Length;
                fs.Write(bytes, 0, bytes.Length);
                if (oldLength > bytes.Length)
                {
                    // blank the tail of a longer old list before shortening the file
                    var zeros = new byte[oldLength - bytes.Length];
                    fs.Write(zeros, 0, zeros.Length);
                    fs.Flush(true);
                }
                fs.SetLength(bytes.Length);
                fs.Flush(true);
            }
        }

        /// <summary>Forget every folder: overwrite the file with zeros, then random bytes, then delete it.</summary>
        public void Clear()
        {
            folders.Clear();
            if (!File.Exists(path))
                return;
            long length = new FileInfo(path).Length;
            if (length > 0)
            {
                var buffer = new byte[length];
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    fs.Write(buffer, 0, buffer.Length); // zeros
                    fs.Flush(true);
                    RandomNumberGenerator.Fill(buffer);
                    fs.Position = 0;
                    fs.Write(buffer, 0, buffer.Length); // random data
                    fs.Flush(true);
                }
            }
            File.Delete(path);
        }
    }
}
