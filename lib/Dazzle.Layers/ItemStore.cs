using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Dazzle.Layers
{
    /// <summary>Where a per-item file (a conversation, a rules file) lives, named as dazzle-preservelib names them.</summary>
    public enum ItemStoreMode
    {
        /// <summary>Beside the item: img01.chat.json next to img01.png.</summary>
        Sidecar,
        /// <summary>In a dot-folder at the dataset root, mirroring the item's relative folder: .satt\sub\img01.chat.json.</summary>
        Adjacent,
        /// <summary>In the program's own store, keyed by a hash of the item's full path.</summary>
        Store,
    }

    /// <summary>
    /// A per-item file in one of three places. Reads look in all three (the chosen one first) so changing the
    /// choice loses nothing; writes go to the chosen one only; a move carries whatever exists. Read side and write
    /// side are separate calls, as find_available_manifests / next_manifest_path are in the Python.
    /// </summary>
    public sealed class ItemStore
    {
        public ItemStoreMode Mode { get; }
        public string AdjacentFolderName { get; }
        public string StoreRoot { get; }

        public ItemStore(ItemStoreMode mode, string adjacentFolderName, string storeRoot)
        {
            Mode = mode; AdjacentFolderName = adjacentFolderName; StoreRoot = storeRoot;
        }

        /// <summary>The path this item's file would have in a given mode. kind is the file's suffix before .json, e.g. "chat" or "rules".</summary>
        public string PathFor(ItemStoreMode mode, string itemPath, string kind, string datasetRoot)
        {
            string full = Path.GetFullPath(itemPath);
            switch (mode)
            {
                case ItemStoreMode.Sidecar:
                    return Path.Combine(Path.GetDirectoryName(full), Path.GetFileNameWithoutExtension(full) + "." + kind + ".json");
                case ItemStoreMode.Adjacent:
                    {
                        // the item's relative folder is mirrored inside the dot-folder, so two items can never share a
                        // name (a flattened "sub_img01" collided with a root file of that name); an item outside the
                        // root, or with no root given, uses a dot-folder in its own folder
                        string root = datasetRoot != null ? Path.GetFullPath(datasetRoot) : Path.GetDirectoryName(full);
                        string rel = Path.GetRelativePath(root, full);
                        if (rel.StartsWith("..") || Path.IsPathRooted(rel)) { root = Path.GetDirectoryName(full); rel = Path.GetFileName(full); }
                        return Path.Combine(root, AdjacentFolderName, Path.ChangeExtension(rel, null) + "." + kind + ".json");
                    }
                default:
                    {
                        string hash;
                        using (var sha = SHA1.Create()) hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(full.ToLowerInvariant()))).Substring(0, 16).ToLowerInvariant();
                        return Path.Combine(StoreRoot, hash + "__" + Path.GetFileNameWithoutExtension(full) + "." + kind + ".json");
                    }
            }
        }

        /// <summary>Every place the item's file exists, the chosen mode first.</summary>
        public IReadOnlyList<(ItemStoreMode mode, string path)> Find(string itemPath, string kind, string datasetRoot)
        {
            var order = new[] { Mode }.Concat(((ItemStoreMode[])Enum.GetValues(typeof(ItemStoreMode))).Where(m => m != Mode));
            var list = new List<(ItemStoreMode, string)>();
            foreach (var m in order)
            {
                string p = PathFor(m, itemPath, kind, datasetRoot);
                if (File.Exists(p)) list.Add((m, p));
            }
            return list;
        }

        /// <summary>Where the next write goes: the chosen mode's path, its folder created.</summary>
        public string WritePath(string itemPath, string kind, string datasetRoot)
        {
            string p = PathFor(Mode, itemPath, kind, datasetRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            return p;
        }

        /// <summary>One file's fate in a Move or Migrate.</summary>
        public sealed class Step
        {
            public string From, To;
            /// <summary>"moved", or why not: "target exists" (nothing overwritten) or the error's message.</summary>
            public string Outcome;
            public bool Moved => Outcome == "moved";
            public override string ToString() => Outcome + ": " + From + " -> " + To;
        }

        /// <summary>
        /// After the item was renamed or moved: carry every existing copy to the new item's paths in the same modes.
        /// Never overwrites: a file already at a target is a collision, reported and left alone with its source.
        /// </summary>
        public IReadOnlyList<Step> Move(string oldItemPath, string newItemPath, string kind, string datasetRoot)
        {
            var steps = new List<Step>();
            foreach (var (m, from) in Find(oldItemPath, kind, datasetRoot))
            {
                string to = PathFor(m, newItemPath, kind, datasetRoot);
                if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) continue;
                steps.Add(MoveOne(from, to));
            }
            return steps;
        }

        /// <summary>
        /// Switching modes (the MOVE rule, user 2026-10-02): every item's file found in fromMode moves to toMode, so
        /// exactly one copy exists afterwards and switching back is the same operation reversed. A file already at a
        /// target is a collision: reported, nothing overwritten, the source left where it was. Timestamps are kept,
        /// including across volumes, where a plain move resets the creation and access times.
        /// </summary>
        public IReadOnlyList<Step> Migrate(ItemStoreMode fromMode, ItemStoreMode toMode, IEnumerable<string> itemPaths, string kind, string datasetRoot)
        {
            var steps = new List<Step>();
            if (fromMode == toMode) return steps;
            foreach (var item in itemPaths)
            {
                string from = PathFor(fromMode, item, kind, datasetRoot);
                if (!File.Exists(from)) continue;
                steps.Add(MoveOne(from, PathFor(toMode, item, kind, datasetRoot)));
            }
            // a mode that leaves an empty dot-folder behind should not leave litter
            if (fromMode == ItemStoreMode.Adjacent && datasetRoot != null) RemoveEmptyFolders(Path.Combine(Path.GetFullPath(datasetRoot), AdjacentFolderName));
            return steps;
        }

        private static Step MoveOne(string from, string to)
        {
            var step = new Step { From = from, To = to };
            try
            {
                if (File.Exists(to)) { step.Outcome = "target exists"; return step; }
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                var info = new FileInfo(from);
                DateTime created = info.CreationTimeUtc, written = info.LastWriteTimeUtc, accessed = info.LastAccessTimeUtc;
                File.Move(from, to);
                // a cross-volume move is a copy: put back what it reset (measured 2026-10-02: creation and access times)
                try { File.SetCreationTimeUtc(to, created); File.SetLastWriteTimeUtc(to, written); File.SetLastAccessTimeUtc(to, accessed); }
                catch (Exception) { }
                step.Outcome = "moved";
            }
            catch (Exception e) { step.Outcome = e.Message; }
            return step;
        }

        private static void RemoveEmptyFolders(string dir)
        {
            try
            {
                if (!Directory.Exists(dir) || (new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0) return;
                foreach (var sub in Directory.GetDirectories(dir)) RemoveEmptyFolders(sub);
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir, false);
            }
            catch (Exception) { }
        }
    }
}
