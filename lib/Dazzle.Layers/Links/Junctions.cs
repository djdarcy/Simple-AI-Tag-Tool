using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Dazzle.Links
{
    /// <summary>
    /// Junction pairs between two folders, so that either folder leads to the other -- the pattern of
    /// directory_manager.py's create_junction_pair: one junction each way, "already exists" is success, every
    /// junction made is recorded so it can be removed later, created with mklink /J (no privilege needed on
    /// Windows), and nothing here throws -- a failure is a sentence in the returned lines.
    /// </summary>
    public static class Junctions
    {
        public static bool IsJunction(string path)
        {
            try
            {
                var info = new DirectoryInfo(path);
                return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Create folderA\nameInA -> folderB and folderB\nameInB -> folderA. Returns one line per link: made, already there, or why not.</summary>
        public static IReadOnlyList<string> EnsurePair(string folderA, string folderB, string nameInA, string nameInB, string recordFile = null)
        {
            var lines = new List<string>();
            lines.Add(Ensure(Path.Combine(folderA, nameInA), folderB, recordFile));
            lines.Add(Ensure(Path.Combine(folderB, nameInB), folderA, recordFile));
            return lines;
        }

        /// <summary>Create one junction at linkPath pointing to target. Idempotent.</summary>
        public static string Ensure(string linkPath, string target, string recordFile = null)
        {
            try
            {
                if (!Directory.Exists(target)) return "could not link " + linkPath + ": the target " + target + " does not exist";
                if (IsJunction(linkPath))
                {
                    // already a link: fine only when it leads where we want; one that leads elsewhere is the person's, left alone
                    string now = TargetOf(linkPath);
                    if (now == null || SamePath(now, target)) return "already there: " + linkPath;
                    return "could not link " + linkPath + ": a link is already there, leading to " + now;
                }
                if (Directory.Exists(linkPath) || File.Exists(linkPath)) return "could not link " + linkPath + ": something else is already at that path";
                if (!OperatingSystem.IsWindows()) return "could not link " + linkPath + ": junctions are a Windows feature";
                var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + linkPath + "\" \"" + target + "\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi);
                string err = p.StandardError.ReadToEnd(); p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                if (!IsJunction(linkPath)) return "could not link " + linkPath + ": " + (err.Trim().Length > 0 ? err.Trim() : "mklink made nothing");
                Record(recordFile, linkPath, target);
                return "linked " + linkPath + " -> " + target;
            }
            catch (Exception e) { return "could not link " + linkPath + ": " + e.Message; }
        }

        /// <summary>Where a junction or symbolic link leads, or null when that cannot be read.</summary>
        public static string TargetOf(string linkPath)
        {
            try
            {
                string t = new DirectoryInfo(linkPath).LinkTarget;
                if (t == null) return null;
                if (t.StartsWith(@"\??\")) t = t.Substring(4);
                return Path.GetFullPath(t, Path.GetDirectoryName(Path.GetFullPath(linkPath)));
            }
            catch (Exception) { return null; }
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        public sealed class RecordEntry { public string Link; public string Target; public string Created; }

        /// <summary>
        /// The links a record lists (a JSON array). No reader for the tab-separated lines the first build wrote: one
        /// such file existed (this machine, 2026-10-02 18:18) and was converted by hand.
        /// </summary>
        public static List<RecordEntry> ReadRecord(string recordFile)
        {
            if (recordFile == null || !File.Exists(recordFile)) return new List<RecordEntry>();
            try { return System.Text.Json.JsonSerializer.Deserialize<List<RecordEntry>>(File.ReadAllText(recordFile), JsonOptions) ?? new List<RecordEntry>(); }
            catch (Exception) { return new List<RecordEntry>(); }
        }

        private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IncludeFields = true };

        /// <summary>Remove every link the record names (and only links, never a real folder), then the record. Returns one line per entry.</summary>
        public static IReadOnlyList<string> RemoveRecorded(string recordFile)
        {
            var lines = new List<string>();
            foreach (var entry in ReadRecord(recordFile))
            {
                string link = entry.Link;
                try
                {
                    if (IsJunction(link)) { Directory.Delete(link, false); lines.Add("removed " + link); }
                    else lines.Add("skipped " + link + " (not a link now)");
                }
                catch (Exception e) { lines.Add("could not remove " + link + ": " + e.Message); }
            }
            try { if (recordFile != null && File.Exists(recordFile)) File.Delete(recordFile); } catch (Exception) { }
            return lines;
        }

        private static void Record(string recordFile, string linkPath, string target)
        {
            if (recordFile == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(recordFile));
                var list = ReadRecord(recordFile);
                if (list.Any(e => SamePath(e.Link, linkPath))) return;
                list.Add(new RecordEntry { Link = linkPath, Target = target, Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
                File.WriteAllText(recordFile, System.Text.Json.JsonSerializer.Serialize(list, JsonOptions));
            }
            catch (Exception) { }
        }
    }
}
