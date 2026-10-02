using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dazzle.Links;

namespace Dazzle.Layers
{
    /// <summary>
    /// Where a program's own data lives, as an overlay of three tiers (user, 2026-10-02):
    /// Portable -- beside the executable, when the person enables it (a marker file beside the exe); the base then.
    /// Home -- a dot-folder in the user profile, the base when installed; the program's own outputs go here.
    /// Documents -- a folder under the user's Documents, the person's own files; created on first run, read last,
    /// never written by the program except by an explicit "save here".
    /// The base decides which direction is checked next: the other base is the second layer, Documents the third.
    /// </summary>
    public sealed class DataLayout
    {
        public string AppFolder { get; }
        public string HomeRoot { get; }
        public string DocumentsRoot { get; }
        public bool IsPortable { get; }
        public LayerStack Stack { get; }
        public Layer Base => Stack.Base;
        /// <summary>Notes a consumer may show once: a newer configuration elsewhere, a junction that could not be made.</summary>
        public List<string> Notes { get; } = new List<string>();

        public static readonly string[] PortableMarkers = { "portable", "portable.txt" };

        /// <param name="appFolder">the folder holding the executable (and, today, settings.json)</param>
        /// <param name="homeDirName">the dot-folder under the user profile, e.g. ".satt"</param>
        /// <param name="documentsDirName">the folder under Documents, e.g. "Simple-AI-Tag-Tool"</param>
        /// <param name="portable">null = decide from the marker file beside the exe; true/false forces it</param>
        public DataLayout(string appFolder, string homeDirName, string documentsDirName, bool? portable = null)
            : this(appFolder, Path.Combine(ProfileFolder(), homeDirName), Path.Combine(DocumentsFolder(), documentsDirName), portable, true) { }

        /// <summary>Explicit roots: for a consumer with its own idea of home, and for checks that must never touch the real profile.</summary>
        public DataLayout(string appFolder, string homeRoot, string documentsRoot, bool? portable, bool explicitRoots)
        {
            AppFolder = Path.GetFullPath(appFolder);
            HomeRoot = Path.GetFullPath(homeRoot);
            DocumentsRoot = Path.GetFullPath(documentsRoot);
            IsPortable = portable ?? PortableMarkers.Any(m => File.Exists(Path.Combine(AppFolder, m)));
            var layers = IsPortable
                ? new[] { new Layer("portable", AppFolder, true), new Layer("home", HomeRoot, false), new Layer("documents", DocumentsRoot, false) }
                : new[] { new Layer("home", HomeRoot, true), new Layer("portable", AppFolder, false), new Layer("documents", DocumentsRoot, false) };
            Stack = new LayerStack(layers);
        }

        private static string ProfileFolder() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static string DocumentsFolder()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrEmpty(docs) ? Path.Combine(ProfileFolder(), "Documents") : docs;
        }

        /// <summary>The marker file that makes the layout portable; create or delete it to switch (takes effect at the next start).</summary>
        public string PortableMarkerPath => Path.Combine(AppFolder, PortableMarkers[0]);

        /// <summary>
        /// First run, idempotent: the base's folders and, when profileFolders, the Documents folder with the same
        /// skills layout plus the junction pair between the base and Documents so either folder leads to the other.
        /// A portable run passes false: it creates nothing outside its own folder (Documents is still read if it
        /// exists). Nothing here throws: what could not be done is a note.
        /// </summary>
        public void EnsureFirstRun(string[] subfolders, bool profileFolders = true, string junctionNameInBase = "Documents", string junctionNameInDocuments = null)
        {
            try
            {
                Directory.CreateDirectory(Base.Root);
                foreach (var s in subfolders) Directory.CreateDirectory(Path.Combine(Base.Root, s));
                if (profileFolders)
                {
                    Directory.CreateDirectory(DocumentsRoot);
                    foreach (var s in subfolders.Where(x => x.StartsWith("skills", StringComparison.OrdinalIgnoreCase))) Directory.CreateDirectory(Path.Combine(DocumentsRoot, s));
                }
            }
            catch (Exception e) { Notes.Add("could not create the data folders: " + e.Message); }
            if (!profileFolders) return;
            junctionNameInDocuments ??= Path.GetFileName(HomeRoot.TrimEnd('\\'));
            var record = Path.Combine(Base.Root, "junctions.json");
            foreach (var line in Junctions.EnsurePair(Base.Root, DocumentsRoot, junctionNameInBase, junctionNameInDocuments, record))
                if (line.StartsWith("could not", StringComparison.OrdinalIgnoreCase)) Notes.Add(line);
        }

        /// <summary>
        /// The settings file for this run: the base's, seeded once from the other base when the base has none
        /// (the installed default inherits a portable install's settings; the original is left alone). In
        /// portable mode a home copy that also exists earns a note rather than a merge.
        /// </summary>
        public string SettingsFile(string fileName = "settings.json")
        {
            if (Stack.SeedWritePath(fileName, out var path)) Notes.Add("copied " + fileName + " from " + Stack.Layers[1].Root + " to " + Base.Root + " (the original was left in place)");
            var elsewhere = Stack.OtherLayersHolding(fileName).FirstOrDefault();
            if (IsPortable && elsewhere != null) Notes.Add("a newer configuration may exist in " + elsewhere.Root + " (portable mode uses " + Base.Root + ")");
            return path;
        }

        /// <summary>The folders a kind of user file is read from, highest first: the base's, then every other tier's that exists.</summary>
        public IReadOnlyList<Found> ReadFolders(string relativeFolder)
        {
            var list = new List<Found>();
            foreach (var l in Stack.Layers)
            {
                string dir = l.PathOf(relativeFolder);
                if (Directory.Exists(dir)) list.Add(new Found(l, dir));
            }
            return list;
        }

        /// <summary>The folder a kind of user file is written to: the base's, created.</summary>
        public string WriteFolder(string relativeFolder)
        {
            string dir = Base.PathOf(relativeFolder);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
