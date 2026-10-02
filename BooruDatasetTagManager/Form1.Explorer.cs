using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    // Simple-AI-Tag-Tool: "explorer mode" for the Dataset pane. A folder bar shows the
    // loaded folder relative to the root you opened, with Root / Up / Subfolders buttons
    // for mouse users; the Browse-folders dialog remains the keyboard route.
    public partial class MainForm
    {
        private ToolStrip explorerBar;
        private ToolStripLabel explorerPath;
        private ToolStripButton explorerRoot, explorerUp;
        private ToolStripDropDownButton explorerSubfolders;
        private string dazzleRootFolder;        // the folder opened by the user; navigation keeps it as the base
        private bool navigatingFromBar;

        private void BuildExplorerBar()
        {
            explorerBar = new ToolStrip { Name = "explorerBar", GripStyle = ToolStripGripStyle.Hidden };
            explorerRoot = new ToolStripButton("Root") { ToolTipText = "Back to the folder you opened", Enabled = false };
            explorerRoot.Click += async (s, e) => { if (dazzleRootFolder != null) await NavigateTo(dazzleRootFolder); };
            explorerUp = new ToolStripButton("Up") { ToolTipText = "Parent folder", Enabled = false };
            explorerUp.Click += async (s, e) => { var parent = Directory.GetParent(dazzleDatasetFolder ?? ""); if (parent != null) await NavigateTo(parent.FullName); };
            explorerSubfolders = new ToolStripDropDownButton("Subfolders") { ToolTipText = "Open a subfolder of the current folder", Enabled = false };
            explorerPath = new ToolStripLabel("(no folder)") { ToolTipText = "" };
            explorerBar.Items.AddRange(new ToolStripItem[] { explorerRoot, explorerUp, explorerSubfolders, new ToolStripSeparator(), explorerPath });
            toolStripContainer3.TopToolStripPanel.Controls.Add(explorerBar);   // BDTM's own dataset toolbar is in the bottom panel
            gridViewDS.CellFormatting += GridViewDS_RelativePathFormatting;
            gridViewDS.CellToolTipTextNeeded += GridViewDS_FullPathToolTip;
        }

        // The path column keeps the full path as its value (it is the dataset's key); only the text shown changes:
        // the folder relative to the loaded one (. or .\sub), since the Name column already carries the file name.
        private void GridViewDS_RelativePathFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (dazzleDatasetFolder == null || e.RowIndex < 0 || gridViewDS.Columns[e.ColumnIndex].Name != "ImageFilePath") return;
            if (e.Value is string full && full.Length > 0)
            {
                try
                {
                    string rel = Path.GetRelativePath(dazzleDatasetFolder, Path.GetDirectoryName(full) ?? full);
                    e.Value = rel == "." ? "." : ".\\" + rel;
                    e.FormattingApplied = true;
                }
                catch (ArgumentException) { }
            }
        }

        private void GridViewDS_FullPathToolTip(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex >= 0 && gridViewDS.Columns[e.ColumnIndex].Name == "ImageFilePath")
                e.ToolTipText = gridViewDS[e.ColumnIndex, e.RowIndex].Value as string;
        }

        private async System.Threading.Tasks.Task NavigateTo(string folder)
        {
            navigatingFromBar = true;
            try { await LoadFromFolderAsync(false, folder); }
            catch (Exception ex)
            {
                // an async Click handler has no caller to catch for it; without this the error is the WinForms crash dialog
                MessageBox.Show(this, "Could not open the folder:\n" + folder + "\n\n" + ex.Message, "Simple-AI-Tag-Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { navigatingFromBar = false; }
            gridViewDS.Focus();
        }

        /// <summary>Called after a folder loads: shows the relative path and rebuilds the subfolder list.</summary>
        private void UpdateExplorerBar(string folder)
        {
            if (explorerBar == null) return;
            StyleDatasetTextColumns();
            if (!navigatingFromBar || dazzleRootFolder == null || !IsUnder(folder, dazzleRootFolder))
                dazzleRootFolder = folder;   // a fresh open (dialog, recent list, command line) starts a new root
            string full = Path.GetFullPath(folder).TrimEnd('\\');
            string rel = string.Equals(full, Path.GetFullPath(dazzleRootFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(full) + "  (root)"
                : Path.GetFileName(dazzleRootFolder.TrimEnd('\\')) + "\\" + Path.GetRelativePath(dazzleRootFolder, full);
            explorerPath.Text = rel;
            explorerPath.ToolTipText = full;
            explorerRoot.Enabled = !string.Equals(full, Path.GetFullPath(dazzleRootFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            explorerUp.Enabled = Directory.GetParent(full) != null;
            explorerSubfolders.DropDownItems.Clear();
            try
            {
                var allowed = Extensions.ImageExtensions.Concat(Extensions.VideoExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var dir in Directory.EnumerateDirectories(full).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Take(60))
                {
                    var info = new DirectoryInfo(dir);
                    if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    int count;
                    try { count = Directory.EnumerateFiles(dir).Count(f => allowed.Contains(Path.GetExtension(f))); } catch (Exception) { count = -1; }
                    var item = new ToolStripMenuItem(info.Name + "   (" + (count < 0 ? "?" : count.ToString()) + ")") { Tag = dir, ToolTipText = dir };
                    item.Click += async (s, e) => await NavigateTo((string)((ToolStripMenuItem)s).Tag);
                    explorerSubfolders.DropDownItems.Add(item);
                }
            }
            catch (Exception) { }
            explorerSubfolders.Enabled = explorerSubfolders.DropDownItems.Count > 0;
            explorerSubfolders.Text = explorerSubfolders.DropDownItems.Count > 0 ? "Subfolders (" + explorerSubfolders.DropDownItems.Count + ")" : "Subfolders";
        }

        // The grid's columns are generated from DataItem on every load: a short header for the path column
        // (it is usually just "."), and a smaller font for the file name and path so they take less width.
        private Font datasetSmallFont;
        private void StyleDatasetTextColumns()
        {
            var path = gridViewDS.Columns["ImageFilePath"];
            if (path != null) path.HeaderText = "Path";
            var baseFont = gridViewDS.DefaultCellStyle.Font ?? gridViewDS.Font;
            float size = Math.Max(6f, baseFont.Size - 1.5f);
            if (datasetSmallFont == null || Math.Abs(datasetSmallFont.Size - size) > 0.01f || datasetSmallFont.FontFamily.Name != baseFont.FontFamily.Name)
                datasetSmallFont = new Font(baseFont.FontFamily, size, baseFont.Style);
            foreach (var name in new[] { "Name", "ImageFilePath" })
                if (gridViewDS.Columns[name] != null) gridViewDS.Columns[name].DefaultCellStyle.Font = datasetSmallFont;
        }

        private static bool IsUnder(string folder, string root)
        {
            string f = Path.GetFullPath(folder).TrimEnd('\\') + "\\", r = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            return f.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }
    }
}
