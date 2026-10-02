using Dazzle.Layers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// The application's side of the vendored lib\Dazzle.Layers: one DataLayout for the tool's own data, the
    /// skills folders read through it, and the per-image store choice. Every consumer that used to compose a path
    /// under Program.AppPath asks here instead; nothing here knows a form.
    ///
    /// Tiers (user, 2026-10-02): Portable (beside the exe, enabled by a "portable" marker file beside it), Home
    /// (~\.satt, the installed default), Documents (%USERPROFILE%\Documents\Simple-AI-Tag-Tool, the person's own
    /// files, read last). The base is Portable or Home; the other is read when the base lacks something.
    /// </summary>
    public static class DazzleData
    {
        public const string HomeDirName = ".satt";
        public const string DocumentsDirName = "Simple-AI-Tag-Tool";
        public const string AdjacentFolderName = ".satt";

        public static DataLayout Layout { get; private set; }
        public static string AppFolder { get; private set; }

        /// <summary>The folder settings.json and recent-folders.json live in this run: the base.</summary>
        public static string BaseFolder => Layout.Base.Root;
        public static bool IsPortable => Layout.IsPortable;
        /// <summary>One-time notes from start-up (a seeded settings copy, a newer configuration elsewhere, a junction that could not be made).</summary>
        public static IReadOnlyList<string> Notes => Layout.Notes;

        private static readonly string[] Subfolders = { @"skills\refine", @"skills\chat", "conversations", "logs" };

        /// <summary>Call once before AppSettings is built. Never throws: what could not be done is a note.</summary>
        public static void Initialize(string appFolder)
        {
            AppFolder = appFolder;
            try
            {
                Layout = new DataLayout(appFolder, HomeDirName, DocumentsDirName);
                Layout.EnsureFirstRun(Subfolders, profileFolders: !Layout.IsPortable);   // a portable run creates nothing outside its own folder
                Layout.SettingsFile();                                              // seeds the base's settings.json once from the other base
                Layout.Stack.SeedWritePath("recent-folders.json", out _);
            }
            catch (Exception e)
            {
                // the layout could not be built at all (an unwritable profile?): fall back to beside the exe, as before 2.14
                Layout = new DataLayout(appFolder, HomeDirName, DocumentsDirName, portable: true);
                Layout.Notes.Add("data folders unavailable, using the program folder: " + e.Message);
            }
        }

        // ------------------------------------------------------------ skills

        /// <summary>The house skills beside the program: skills\ for refine, skills\chat for chat. Read-only by convention; the defaults are written here.</summary>
        public static string HouseSkillsFolder(string kind) =>
            kind == "chat" ? Path.Combine(AppFolder, "skills", "chat") : Path.Combine(AppFolder, "skills");

        /// <summary>Where Save as... writes: the base's skills\<kind>, created.</summary>
        public static string SkillsWriteFolder(string kind) => Layout.WriteFolder(Path.Combine("skills", kind));

        public sealed class SkillFile
        {
            public string Name; public string Path; public string Source;
            public override string ToString() => Name;
        }

        /// <summary>
        /// Every skill visible for a kind, one per name, the nearer source winning: the base's folder, then the other
        /// tiers' skills\<kind>, then the house folder (when shown). Union across layers, first wins within a name.
        /// </summary>
        public static List<SkillFile> SkillFiles(string kind, bool showHouse)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<SkillFile>();
            void Take(string folder, string source)
            {
                if (!Directory.Exists(folder)) return;
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(folder).Where(IsSkillFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(); }
                catch (Exception) { return; }
                foreach (var f in files)
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (seen.Add(name)) list.Add(new SkillFile { Name = name, Path = f, Source = source });
                }
            }
            foreach (var found in Layout.ReadFolders(Path.Combine("skills", kind))) Take(found.Path, found.Layer.Name);
            if (showHouse) Take(HouseSkillsFolder(kind), "house");
            return list;
        }

        public static SkillFile FindSkill(string kind, string name, bool showHouse) =>
            SkillFiles(kind, showHouse).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        private static bool IsSkillFile(string f) => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------ per-image files

        /// <summary>The store for per-image files (conversations, per-file rules) by the person's choice: 0 sidecar, 1 adjacent (.satt in the dataset), 2 the program's store.</summary>
        public static ItemStore ItemStore(int choice) =>
            new ItemStore(choice == 0 ? ItemStoreMode.Sidecar : choice == 1 ? ItemStoreMode.Adjacent : ItemStoreMode.Store,
                          AdjacentFolderName, Path.Combine(BaseFolder, "conversations"));
    }
}
