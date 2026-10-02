using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Dazzle.Layers
{
    /// <summary>
    /// A location layer: a root folder in which a relative path may or may not exist. Layers are ordered;
    /// the first is the base. Only the base is written to by policy (the "first found" write policy of the
    /// union experiment); a consumer that writes elsewhere does so by an explicit act.
    /// </summary>
    public sealed class Layer
    {
        public string Name { get; }
        public string Root { get; }
        public bool Writable { get; }
        public Layer(string name, string root, bool writable) { Name = name; Root = Path.GetFullPath(root); Writable = writable; }
        public string PathOf(string relative) => Path.Combine(Root, relative);
        public bool Exists => Directory.Exists(Root);
        public override string ToString() => Name + " = " + Root;
    }

    /// <summary>One answer with its provenance.</summary>
    public sealed class Found
    {
        public Layer Layer { get; }
        public string Path { get; }
        public Found(Layer layer, string path) { Layer = layer; Path = path; }
        public override string ToString() => Layer.Name + ": " + Path;
    }

    /// <summary>
    /// An ordered set of location layers with the four resolution policies. Reads walk from the base down;
    /// a delete in a higher layer exposes the lower one; the base receives writes.
    /// </summary>
    public sealed class LayerStack
    {
        public IReadOnlyList<Layer> Layers { get; }
        public Layer Base => Layers[0];

        public LayerStack(IEnumerable<Layer> layers)
        {
            Layers = layers.ToList();
            if (Layers.Count == 0) throw new ArgumentException("a stack needs at least one layer");
        }

        /// <summary>First wins: the highest layer holding the file, or null.</summary>
        public Found Resolve(string relative) => FindAll(relative).FirstOrDefault();

        /// <summary>Every layer holding the file, highest first.</summary>
        public IEnumerable<Found> FindAll(string relative)
        {
            foreach (var l in Layers)
            {
                string p = l.PathOf(relative);
                if (File.Exists(p)) yield return new Found(l, p);
            }
        }

        /// <summary>Where the next write of this relative path goes: the base, its folder created.</summary>
        public string WritePath(string relative)
        {
            string p = Base.PathOf(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            return p;
        }

        /// <summary>
        /// The write path, seeded once from a lower layer when the base lacks the file: the base keeps the copy
        /// from then on, the lower layer is left untouched (so switching the base back finds it as it was).
        /// Returns whether a seed copy was made.
        /// </summary>
        public bool SeedWritePath(string relative, out string path)
        {
            path = WritePath(relative);
            if (File.Exists(path)) return false;
            var lower = FindAll(relative).FirstOrDefault(f => !ReferenceEquals(f.Layer, Base));
            if (lower == null) return false;
            try { File.Copy(lower.Path, path, overwrite: false); }
            catch (IOException) when (File.Exists(path)) { return false; }   // another instance seeded it first: theirs stands
            return true;
        }

        /// <summary>
        /// Union: the files of a relative folder across every layer, one entry per key (the file name without
        /// extension by default), the highest layer's entry kept. Highest layer's entries come first.
        /// </summary>
        public IReadOnlyList<Found> Union(string relativeFolder, string searchPattern = "*", Func<string, string> keyOf = null)
        {
            keyOf ??= p => Path.GetFileNameWithoutExtension(p);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<Found>();
            foreach (var l in Layers)
            {
                string dir = l.PathOf(relativeFolder);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, searchPattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    if (seen.Add(keyOf(f))) result.Add(new Found(l, f));
            }
            return result;
        }

        /// <summary>The highest layer that has the relative folder at all, or null.</summary>
        public Found ResolveFolder(string relativeFolder)
        {
            foreach (var l in Layers)
            {
                string dir = l.PathOf(relativeFolder);
                if (Directory.Exists(dir)) return new Found(l, dir);
            }
            return null;
        }

        /// <summary>Which layers other than the base hold anything at all for the relative path (file or folder) -- the "a newer configuration may exist" check.</summary>
        public IEnumerable<Layer> OtherLayersHolding(string relative) =>
            Layers.Skip(1).Where(l => File.Exists(l.PathOf(relative)) || Directory.Exists(l.PathOf(relative)));
    }

    /// <summary>
    /// Merge with precedence over any entries: every layer's entries all apply; where two entries share a key
    /// and disagree, the nearer (earlier) one wins and the farther is reported as overridden. Entries are given
    /// nearest layer first. The "disagree" test is the consumer's (two rules on the same tag with opposite signs);
    /// entries that share a key but do not disagree both survive.
    /// </summary>
    public static class Merge
    {
        public sealed class Result<T>
        {
            public List<T> Effective = new List<T>();
            public List<(T entry, T overriddenBy)> Overridden = new List<(T, T)>();
        }

        public static Result<T> WithPrecedence<T>(IEnumerable<IEnumerable<T>> layersNearestFirst, Func<T, string> keyOf, Func<T, T, bool> disagree)
        {
            var r = new Result<T>();
            var kept = new List<T>();
            foreach (var layer in layersNearestFirst)
                foreach (var e in layer)
                {
                    string k = keyOf(e);
                    var winner = kept.FirstOrDefault(x => string.Equals(keyOf(x), k, StringComparison.OrdinalIgnoreCase) && disagree(x, e));
                    if (winner != null) r.Overridden.Add((e, winner));
                    else kept.Add(e);
                }
            r.Effective = kept;
            return r;
        }

        /// <summary>Union: every entry from every layer, de-duplicated by key, nearest first.</summary>
        public static List<T> Union<T>(IEnumerable<IEnumerable<T>> layersNearestFirst, Func<T, string> keyOf)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<T>();
            foreach (var layer in layersNearestFirst)
                foreach (var e in layer)
                    if (seen.Add(keyOf(e))) result.Add(e);
            return result;
        }

        /// <summary>
        /// Key merge: a higher document overrides only the keys it names; nested dictionaries merge recursively;
        /// anything else (lists, scalars) is replaced whole. Documents are given nearest first.
        /// </summary>
        public static Dictionary<string, object> Keys(IEnumerable<IDictionary<string, object>> documentsNearestFirst)
        {
            var docs = documentsNearestFirst.ToList();
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (int i = docs.Count - 1; i >= 0; i--) Apply(result, docs[i]);   // farthest first, so nearer overrides
            return result;
        }

        private static void Apply(Dictionary<string, object> target, IDictionary<string, object> overlay)
        {
            foreach (var kv in overlay)
            {
                if (kv.Value is IDictionary<string, object> od && target.TryGetValue(kv.Key, out var existing) && existing is Dictionary<string, object> ed)
                    Apply(ed, od);
                else if (kv.Value is IDictionary<string, object> od2)
                {
                    var copy = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    Apply(copy, od2); target[kv.Key] = copy;
                }
                else target[kv.Key] = kv.Value;
            }
        }
    }
}
