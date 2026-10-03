using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    // Simple-AI-Tag-Tool: the operations Chat mode's tools perform on the dataset -- rename, move, set caption --
    // each journaled with its inverse so the transcript can undo it. BDTM has no rename or move of its own; the
    // dataset is a dictionary keyed by the image path, so a rename moves the two files and re-keys the item in place,
    // keeping unsaved edits, the Refine proposal and the selection.
    // Design: 2026-10-02__06-20-24__dev-workflow-process__satt-chat-mode-tool-calling.md (unit 2)
    public partial class MainForm
    {
        public sealed class DatasetChange
        {
            public string Kind;             // rename | move | caption
            public string OldPath, NewPath; // image paths (caption file follows)
            public string OldCaption, NewCaption;
            public DateTime When = DateTime.Now;
            public bool Undone;
            public string Describe() => Kind switch
            {
                "caption" => "caption of " + Path.GetFileName(NewPath ?? OldPath) + " set",
                "move" => Path.GetFileName(OldPath) + " moved to " + Path.GetDirectoryName(NewPath),
                _ => Path.GetFileName(OldPath) + " renamed to " + Path.GetFileName(NewPath),
            };
        }

        private readonly List<DatasetChange> changeJournal = new List<DatasetChange>();

        /// <summary>Rename the image (and its caption file) to a new base name in the same folder.</summary>
        private (bool ok, string message) RenameImage(string imagePath, string newBaseName)
        {
            if (string.IsNullOrWhiteSpace(newBaseName)) return (false, "the new name is empty");
            string clean = newBaseName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) clean = clean.Replace(c, '_');
            clean = clean.TrimEnd('.', ' ');
            if (clean.Length == 0) return (false, "the new name has no usable characters");
            string ext = Path.GetExtension(imagePath);
            if (clean.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(0, clean.Length - ext.Length);
            string target = Path.Combine(Path.GetDirectoryName(imagePath), clean + ext);
            var r = RelocateImage(imagePath, target);
            if (!r.ok) return r;
            changeJournal.Add(new DatasetChange { Kind = "rename", OldPath = imagePath, NewPath = target });
            return (true, "renamed to " + Path.GetFileName(target) + (clean != newBaseName.Trim() ? " (characters not allowed in a file name were replaced)" : "") + "; the caption file moved with it");
        }

        /// <summary>Move the image (and its caption file) to a folder, given relative to the dataset's folder or absolute; must stay inside the loaded folder.</summary>
        private (bool ok, string message) MoveImage(string imagePath, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return (false, "the target folder is empty");
            if (dazzleDatasetFolder == null) return (false, "no dataset folder is loaded");
            string root = Path.GetFullPath(dazzleDatasetFolder);
            string target = Path.IsPathRooted(folder) ? Path.GetFullPath(folder) : Path.GetFullPath(Path.Combine(root, folder));
            if (!IsUnder(target, root) && !string.Equals(target.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return (false, "the folder " + target + " is outside the loaded dataset folder " + root + "; moves stay inside it");
            if (!Program.Settings.IncludeSubfolders && !string.Equals(target.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return (false, "subfolders are not included in this dataset (Settings > General), so a file moved into one would leave the list");
            try { Directory.CreateDirectory(target); } catch (Exception e) { return (false, "could not create " + target + ": " + e.Message); }
            string dest = Path.Combine(target, Path.GetFileName(imagePath));
            var r = RelocateImage(imagePath, dest);
            if (!r.ok) return r;
            changeJournal.Add(new DatasetChange { Kind = "move", OldPath = imagePath, NewPath = dest });
            return (true, "moved to " + Path.GetRelativePath(root, dest) + " (relative to the dataset folder); the caption file moved with it");
        }

        /// <summary>Replace an image's caption; for the current image this goes through the caption box, as a typed edit would.</summary>
        private (bool ok, string message) SetCaptionFor(string imagePath, string caption)
        {
            if (Program.DataManager == null || !Program.DataManager.DataSet.TryGetValue(imagePath, out var item)) return (false, "no such image in the dataset: " + imagePath);
            string old = item.Tags.ToString();
            if (IsCurrentImage(imagePath) && textBoxTags != null && textBoxTags.Enabled)
            {
                textBoxTags.Text = caption ?? "";
                CommitTagsTextBox();
            }
            else
            {
                var parsed = PromptParser.ParsePrompt(caption ?? "", Program.Settings.FixTagsOnSaveLoad, Program.Settings.SeparatorOnLoad);
                item.Tags.ReplaceAll(parsed);
            }
            changeJournal.Add(new DatasetChange { Kind = "caption", OldPath = imagePath, NewPath = imagePath, OldCaption = old, NewCaption = item.Tags.ToString() });
            lastRenderKey = null; RenderProposalForCurrentImage();
            return (true, "caption set to: " + item.Tags + "  (unsaved until Ctrl+S)");
        }

        private (bool ok, string message) UndoChange(DatasetChange c)
        {
            if (c.Undone) return (false, "already undone");
            (bool ok, string message) r;
            if (c.Kind == "caption") r = SetCaptionFor(c.NewPath, c.OldCaption);
            else r = RelocateImage(c.NewPath, c.OldPath);
            if (r.ok) { c.Undone = true; changeJournal.RemoveAll(x => ReferenceEquals(x, changeJournal.LastOrDefault()) && x.Kind == "caption" && x.NewCaption == c.OldCaption); }
            return r.ok ? (true, "undone: " + c.Describe()) : r;
        }

        private bool IsCurrentImage(string imagePath) =>
            currentInfo?.Path != null && string.Equals(Path.GetFullPath(currentInfo.Path), Path.GetFullPath(imagePath), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Move the image file and its caption file to a new path and re-key the dataset item in place: the
        /// dictionary key, the item's paths and name, the image cache, the Refine proposal, then the grid and the selection.
        /// </summary>
        private (bool ok, string message) RelocateImage(string oldPath, string newPath)
        {
            if (Program.DataManager == null || !Program.DataManager.DataSet.TryGetValue(oldPath, out var item)) return (false, "no such image in the dataset: " + oldPath);
            if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase)) return (false, "that is already the file's name");
            string oldTxt = item.TextFilePath;
            string newTxt = Path.Combine(Path.GetDirectoryName(newPath), Path.GetFileNameWithoutExtension(newPath) + (string.IsNullOrEmpty(oldTxt) ? "." + Program.Settings.DefaultTagsFileExtension : Path.GetExtension(oldTxt)));
            if (File.Exists(newPath)) return (false, Path.GetFileName(newPath) + " already exists in " + Path.GetDirectoryName(newPath));
            if (File.Exists(newTxt)) return (false, Path.GetFileName(newTxt) + " already exists in " + Path.GetDirectoryName(newTxt));
            if (IsCurrentImage(oldPath)) CommitTagsTextBox();   // an unsaved edit stays with the item and lands in the new caption file on save
            bool txtMoved = false;
            try
            {
                File.Move(oldPath, newPath);
                if (!string.IsNullOrEmpty(oldTxt) && File.Exists(oldTxt)) { File.Move(oldTxt, newTxt); txtMoved = true; }
            }
            catch (Exception e)
            {
                try { if (txtMoved) File.Move(newTxt, oldTxt); if (File.Exists(newPath) && !File.Exists(oldPath)) File.Move(newPath, oldPath); } catch (Exception) { }
                return (false, "could not move the files: " + e.Message);
            }
            // re-key
            Program.DataManager.DataSet.TryRemove(oldPath, out _);
            item.ImageFilePath = newPath;
            item.ImageFilePathHash = newPath.GetHashCode();
            item.Name = Path.GetFileNameWithoutExtension(newPath);
            item.TextFilePath = newTxt;
            Program.DataManager.DataSet.TryAdd(newPath, item);
            Program.DataManager.RemoveFromCache(oldPath);
            if (proposals.TryGetValue(oldPath, out var prop)) { proposals.Remove(oldPath); proposals[newPath] = prop; }
            MoveConversations(oldPath, newPath);   // its chat and Refine files follow it
            // the grid: rebind, restyle, reselect the same item
            bool wasCurrent = IsCurrentImage(oldPath);
            gridViewDS.DataSource = Program.DataManager.GetDataSourceWithLastFilter();
            ApplyDataSetGridStyle();
            StyleDatasetTextColumns();
            for (int i = 0; i < gridViewDS.Rows.Count; i++)
                if (string.Equals((string)gridViewDS.Rows[i].Cells["ImageFilePath"].Value, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (wasCurrent) { lastRenderKey = null; SelectDatasetRow(i); }
                    break;
                }
            return (true, "ok");
        }
    }
}
