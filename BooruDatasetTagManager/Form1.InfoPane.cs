using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    // Simple-AI-Tag-Tool: the image info pane under the preview. Two tabs: "Preview Info"
    // (file and image facts) and "Preview Extracted Info" (EXIF, generation prompts, workflow).
    // Cheap facts are filled when the image is shown; unique colours and metadata arrive from
    // background jobs that check the image is still current before touching the pane.
    public partial class MainForm
    {
        private SplitContainer splitPreview;        // preview on top, info pane below
        private DazzleImageView imageView;          // the zoom / pan / selection viewer
        private string baseTitle;                   // "Simple-AI-Tag-Tool 2.8.0", captured once
        private SplitContainer splitExtracted;      // tree on top, the selected node's full text below
        private TabControl tabsInfo;
        private TextBox textDetail;
        private CheckBox checkCollapseRepeats;
        private ListView listInfo;                  // Preview Info: key / value rows
        private TreeView treeExtracted;             // Preview Extracted Info
        private Button buttonOpenFile, buttonOpenFolder, buttonCopyWorkflow, buttonSaveWorkflow, buttonFingerprint;
        private ToolStripMenuItem menuInfoPane;
        private DazzleImageInfo currentInfo;        // the image the pane describes; null when none
        private int infoGeneration;                 // bumped per image; a job whose generation is stale is dropped
        private readonly Stopwatch previewLoadWatch = new Stopwatch();
        private bool previewFromCache;

        /// <summary>Insert the pane under the preview. Called from ApplyDazzleLayout.</summary>
        private void BuildInfoPane()
        {
            splitPreview = new SplitContainer { Name = "splitPreview", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 5 };

            // the top half is the zoomable viewer; upstream's picture box stays, hidden, because
            // upstream code still assigns its Image (ShowPreview hands the same Image to the viewer)
            tabPreview.Controls.Remove(pictureBoxPreview);
            pictureBoxPreview.Visible = false;
            splitPreview.Panel1.Controls.Add(pictureBoxPreview);
            imageView = new DazzleImageView { Name = "imageView", Dock = DockStyle.Fill };
            imageView.MouseDown += (s, e) => gridViewDS.Focus();   // a click here returns the keyboard to navigation
            imageView.MouseUp += (s, e) => { if (!gridViewDS.Focused) gridViewDS.Focus(); };
            imageView.ViewChanged += (s, e) => OnViewChanged();
            splitPreview.Panel1.Controls.Add(imageView);
            tabPreview.Controls.Add(splitPreview);

            listInfo = new ListView
            {
                Name = "listInfo", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Clickable,
                MultiSelect = false, TabStop = false, ShowItemToolTips = true, Font = Program.Settings.GridViewFont.GetFont()
            };
            listInfo.Columns.Add("Property", 150);
            listInfo.Columns.Add("Value", 600);
            listInfo.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) gridViewDS.Focus(); };
            listInfo.GotFocus += (s, e) => BeginInvoke(new Action(() => { if (listInfo.Focused) gridViewDS.Focus(); }));
            listInfo.ContextMenuStrip = BuildCopyMenu(() => listInfo.SelectedItems.Count > 0 ? listInfo.SelectedItems[0].SubItems[1].Text : null,
                                                      () => string.Join(Environment.NewLine, listInfo.Items.Cast<ListViewItem>().Select(i => i.Text + ": " + i.SubItems[1].Text)));
            listInfo.Resize += (s, e) => { if (listInfo.Columns.Count == 2) listInfo.Columns[1].Width = Math.Max(200, listInfo.ClientSize.Width - listInfo.Columns[0].Width - 4); };
            // the File name row is editable in place: double-click (or the menu); the rename goes through the same
            // journaled operation Chat uses, so the caption file follows and Undo last change reverses it (user, 2026-10-02)
            listInfo.MouseDoubleClick += (s, e) => { var hit = listInfo.HitTest(e.Location); if (hit.Item != null && hit.Item.Text == "File name") BeginRenameInPlace(hit.Item); };
            listInfo.ContextMenuStrip.Items.Insert(0, new ToolStripMenuItem("Rename file...", null, (s, e) => { var row = listInfo.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Text == "File name"); if (row != null) BeginRenameInPlace(row); }));
            listInfo.ContextMenuStrip.Items.Insert(1, new ToolStripSeparator());
            renameBox = new TextBox { Visible = false, BorderStyle = BorderStyle.FixedSingle, Font = listInfo.Font };
            renameBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitRenameInPlace(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; renameBox.Visible = false; gridViewDS.Focus(); }
            };
            renameBox.LostFocus += (s, e) => { if (renameBox.Visible) renameBox.Visible = false; };
            listInfo.Controls.Add(renameBox);

            var infoButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2), TabStop = false };
            buttonOpenFile = new Button { Text = "Open in default app", AutoSize = true, TabStop = false };
            buttonOpenFile.Click += (s, e) => OpenWithShell(currentInfo?.Path);
            buttonOpenFolder = new Button { Text = "Show in folder", AutoSize = true, TabStop = false };
            // the shell's folder handler, so a replacement lister such as Directory Opus opens it; Explorer-with-the-file-selected is on the right-click
            buttonOpenFolder.Click += (s, e) => { if (currentInfo != null) OpenWithShell(currentInfo.Folder); };
            var folderMenu = new ContextMenuStrip();
            folderMenu.Items.Add("Open in Explorer with the file selected", null, (s, e) => { if (currentInfo != null && File.Exists(currentInfo.Path)) Process.Start("explorer.exe", "/select,\"" + currentInfo.Path + "\""); });
            buttonOpenFolder.ContextMenuStrip = folderMenu;
            infoButtons.Controls.AddRange(new Control[] { buttonOpenFile, buttonOpenFolder });

            var tabInfo = new TabPage("Preview Info") { Name = "tabPreviewInfo" };
            tabInfo.Controls.Add(listInfo);
            tabInfo.Controls.Add(infoButtons);

            treeExtracted = new TreeView { Name = "treeExtracted", Dock = DockStyle.Fill, TabStop = false, ShowNodeToolTips = true, Font = Program.Settings.GridViewFont.GetFont() };
            treeExtracted.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) gridViewDS.Focus(); };
            // clicking a node shows its whole text below, selectable for copying into a notepad or the check list
            treeExtracted.AfterSelect += (s, e) => ShowDetail(e.Node);
            // selecting a node focuses the tree, which then swallows Left / Right for expand and
            // collapse; hand the keyboard back to the dataset list once the click has been handled
            treeExtracted.NodeMouseClick += (s, e) => { treeExtracted.SelectedNode = e.Node; ShowDetail(e.Node); BeginInvoke(new Action(() => gridViewDS.Focus())); };
            treeExtracted.GotFocus += (s, e) => BeginInvoke(new Action(() => { if (treeExtracted.Focused) gridViewDS.Focus(); }));
            treeExtracted.ContextMenuStrip = BuildCopyMenu(() => treeExtracted.SelectedNode?.Tag as string ?? treeExtracted.SelectedNode?.Text,
                                                           () => TreeText(treeExtracted.Nodes));
            // Left/Right would expand and collapse; the keyboard belongs to navigation
            treeExtracted.PreviewKeyDown += (s, e) => { if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) e.IsInputKey = false; };

            var extractedButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2), TabStop = false };
            buttonCopyWorkflow = new Button { Text = "Copy workflow JSON", AutoSize = true, TabStop = false, Enabled = false };
            buttonCopyWorkflow.Click += (s, e) => { var j = currentInfo?.EmbeddedWorkflowJson ?? currentInfo?.ComfyPromptsJson; if (j != null) Clipboard.SetText(j); };
            buttonSaveWorkflow = new Button { Text = "Save workflow JSON...", AutoSize = true, TabStop = false, Enabled = false };
            buttonSaveWorkflow.Click += (s, e) => SaveWorkflowJson();
            buttonFingerprint = new Button { Text = "Compare versions with comfydbg", AutoSize = true, TabStop = false, Enabled = false };
            buttonFingerprint.Click += async (s, e) => await RunFingerprintAsync();
            // like `comfydbg prompt --prune`: a stage or side that repeats an earlier one is one line, not the text again
            checkCollapseRepeats = new CheckBox { Text = "Collapse repeats", AutoSize = true, Checked = true, TabStop = false, Margin = new Padding(12, 6, 3, 3) };
            checkCollapseRepeats.CheckedChanged += (s, e) => RenderExtracted(currentInfo);
            extractedButtons.Controls.AddRange(new Control[] { buttonCopyWorkflow, buttonSaveWorkflow, buttonFingerprint, checkCollapseRepeats });

            // the full text of the selected node: read-only so it never looks like it edits the file,
            // but selectable and copyable (Ctrl+A, Ctrl+C, right-click) in its entirety
            textDetail = new TextBox
            {
                Name = "textDetail", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                WordWrap = true, TabStop = false, BackColor = SystemColors.Window, Font = Program.Settings.GridViewFont.GetFont()
            };
            textDetail.ContextMenuStrip = BuildCopyMenu(() => textDetail.SelectionLength > 0 ? textDetail.SelectedText : textDetail.Text, () => textDetail.Text);
            textDetail.ContextMenuStrip.Items.Insert(0, new ToolStripMenuItem("Send to Check for box", null, (s, e) => AppendToCheckBox(textDetail.SelectionLength > 0 ? textDetail.SelectedText : textDetail.Text)));

            splitExtracted = new SplitContainer { Name = "splitExtracted", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 5 };
            splitExtracted.Panel1.Controls.Add(treeExtracted);
            splitExtracted.Panel2.Controls.Add(textDetail);

            var tabExtracted = new TabPage("Preview Extracted Info") { Name = "tabPreviewExtracted" };
            tabExtracted.Controls.Add(splitExtracted);
            tabExtracted.Controls.Add(extractedButtons);

            tabsInfo = new TabControl { Name = "tabsInfo", Dock = DockStyle.Fill, TabStop = false };
            tabsInfo.TabPages.Add(tabInfo);
            tabsInfo.TabPages.Add(tabExtracted);
            tabsInfo.MouseUp += (s, e) => gridViewDS.Focus();
            splitPreview.Panel2.Controls.Add(tabsInfo);
            // a chevron at the tab strip's far right collapses the pane; a slim bar under the preview brings it back
            // (the I key and View > Image info pane do the same; user, 2026-10-02)
            buttonCollapseInfo = new Button { Name = "buttonCollapseInfo", Text = "", Image = ChevronGlyph(true), ImageAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat, TabStop = false, Width = 26, Height = 22, Anchor = AnchorStyles.Top | AnchorStyles.Right, Cursor = Cursors.Hand };
            buttonCollapseInfo.FlatAppearance.BorderSize = 0;
            buttonCollapseInfo.Location = new Point(splitPreview.Panel2.ClientSize.Width - buttonCollapseInfo.Width - 2, 0);
            buttonCollapseInfo.Click += (s, e) => SetInfoPaneVisible(false);
            new ToolTip().SetToolTip(buttonCollapseInfo, "Hide the image info pane (I, or View > Image info pane)");
            splitPreview.Panel2.Controls.Add(buttonCollapseInfo);
            buttonCollapseInfo.BringToFront();
            splitPreview.Panel2.Resize += (s, e) => buttonCollapseInfo.Location = new Point(splitPreview.Panel2.ClientSize.Width - buttonCollapseInfo.Width - 2, 0);
            barRestoreInfo = new Button { Name = "barRestoreInfo", Text = "Image info", Image = ChevronGlyph(false), ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText, Dock = DockStyle.Bottom, Height = 22, FlatStyle = FlatStyle.Flat, TabStop = false, TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand, Visible = !Program.Settings.DazzleInfoPaneVisible };
            barRestoreInfo.FlatAppearance.BorderSize = 0;
            barRestoreInfo.Click += (s, e) => SetInfoPaneVisible(true);
            splitPreview.Panel1.Controls.Add(barRestoreInfo);
            splitPreview.Panel2Collapsed = !Program.Settings.DazzleInfoPaneVisible;
            splitPreview.SplitterMoved += (s, e) => { if (!splitPreview.Panel2Collapsed) Program.Settings.DazzleInfoPaneHeight = splitPreview.Height - splitPreview.SplitterDistance; };

            menuInfoPane = new ToolStripMenuItem("Image info pane") { Name = "MenuDazzleInfoPane", CheckOnClick = true, Checked = Program.Settings.DazzleInfoPaneVisible };
            menuInfoPane.Click += (s, e) => SetInfoPaneVisible(menuInfoPane.Checked);
            viewToolStripMenuItem.DropDownItems.Add(menuInfoPane);
        }

        /// <summary>Hand the preview image to the viewer (null clears it).</summary>
        private void ShowInViewer(Image img)
        {
            imageView?.SetImage(img);
        }

        /// <summary>
        /// Title bar like IrfanView's: "file - Simple-AI-Tag-Tool 2.8.0 (Zoom: 4876 x 6502, 635 %)
        /// (Selection: 10, 9; 55 x 41; 1.341)", and the live Zoom / Selection rows of the info pane.
        /// Cheap on purpose: it runs on every pan and drag.
        /// </summary>
        private void OnViewChanged()
        {
            if (baseTitle == null) baseTitle = Text;
            if (imageView?.Image == null || currentInfo == null)
            {
                Text = baseTitle;
                return;
            }
            var shown = imageView.DisplayedSize;
            string title = $"{currentInfo.FileName} - {baseTitle} (Zoom: {shown.Width} x {shown.Height}, {ZoomPercent()})";
            if (imageView.HasSelection)
            {
                var s = imageView.Selection;
                title += $" (Selection: {s.X}, {s.Y}; {s.Width} x {s.Height}; {imageView.SelectionRatio:0.000})";
            }
            Text = title;
            SetViewRows();
        }

        private string ZoomPercent() => (imageView.Zoom * 100).ToString(imageView.Zoom * 100 < 10 ? "0.0" : "0") + " %";

        /// <summary>Update only the two live rows, not the whole list (this runs on every mouse move).</summary>
        private void SetViewRows()
        {
            if (listInfo == null || splitPreview.Panel2Collapsed) return;
            foreach (ListViewItem item in listInfo.Items)
            {
                if (item.Text == "Zoom")
                    item.SubItems[1].Text = imageView?.Image == null ? "" : $"{ZoomPercent()}  (shown as {imageView.DisplayedSize.Width} x {imageView.DisplayedSize.Height})";
                else if (item.Text == "Selection")
                    item.SubItems[1].Text = imageView != null && imageView.HasSelection
                        ? $"{imageView.Selection.X}, {imageView.Selection.Y};  {imageView.Selection.Width} x {imageView.Selection.Height} px;  ratio {imageView.SelectionRatio:0.000}"
                        : "none  (drag on the image)";
            }
        }

        /// <summary>After the layout has settled: give the pane its saved height.</summary>
        private void PlaceInfoPaneSplitter()
        {
            if (splitPreview == null || splitPreview.Height <= 0) return;
            int paneHeight = Program.Settings.DazzleInfoPaneHeight > 0 ? Program.Settings.DazzleInfoPaneHeight : splitPreview.Height / 3;
            int distance = Math.Max(splitPreview.Panel1MinSize, Math.Min(splitPreview.Height - paneHeight, splitPreview.Height - splitPreview.Panel2MinSize - splitPreview.SplitterWidth));
            if (distance > 0) splitPreview.SplitterDistance = distance;
        }

        private void SetInfoPaneVisible(bool visible)
        {
            if (splitPreview == null) return;
            splitPreview.Panel2Collapsed = !visible;
            menuInfoPane.Checked = visible;
            if (barRestoreInfo != null) barRestoreInfo.Visible = !visible;
            Program.Settings.DazzleInfoPaneVisible = visible;
            if (visible) { PlaceInfoPaneSplitter(); RenderInfoPane(); }
            gridViewDS.Focus();
        }
        private Button buttonCollapseInfo, barRestoreInfo;

        /// <summary>A chevron pointing down (hide) or up (show), drawn for the screen's DPI; the glyph characters are not in the button font.</summary>
        private Bitmap ChevronGlyph(bool down)
        {
            int n = (int)Math.Round(14 * DeviceDpi / 96.0);
            var bmp = new Bitmap(n, n);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(Color.FromArgb(70, 70, 70), Math.Max(1.5f, n / 7f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                float l = n * 0.2f, r = n * 0.8f, m = n / 2f, top = down ? n * 0.35f : n * 0.65f, tip = down ? n * 0.68f : n * 0.32f;
                g.DrawLines(pen, new[] { new PointF(l, top), new PointF(m, tip), new PointF(r, top) });
            }
            return bmp;
        }

        private void ToggleInfoPane() => SetInfoPaneVisible(splitPreview != null && splitPreview.Panel2Collapsed);

        private void ShowDetail(TreeNode node)
        {
            if (textDetail == null) return;
            string text = node?.Tag as string ?? node?.Text ?? "";
            textDetail.Text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            textDetail.SelectionStart = 0;
            textDetail.SelectionLength = 0;
        }

        /// <summary>Append text to the Check for box as comma-separated tags (a prompt becomes a check list in one step).</summary>
        private void AppendToCheckBox(string text)
        {
            if (textBoxCheck == null || string.IsNullOrWhiteSpace(text)) return;
            string existing = textBoxCheck.Text.Trim();
            string addition = text.Replace("\r", " ").Replace("\n", " ").Trim().TrimEnd(',');
            textBoxCheck.Text = existing.Length == 0 ? addition : existing.TrimEnd(',') + ", " + addition;
        }

        private ContextMenuStrip BuildCopyMenu(Func<string> selected, Func<string> all)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Copy value", null, (s, e) => { var t = selected(); if (!string.IsNullOrEmpty(t)) Clipboard.SetText(t); });
            menu.Items.Add("Copy all", null, (s, e) => { var t = all(); if (!string.IsNullOrEmpty(t)) Clipboard.SetText(t); });
            return menu;
        }

        private static string TreeText(TreeNodeCollection nodes, int depth = 0)
        {
            var lines = new List<string>();
            foreach (TreeNode n in nodes)
            {
                lines.Add(new string(' ', depth * 2) + (n.Tag as string ?? n.Text));
                if (n.Nodes.Count > 0) lines.Add(TreeText(n.Nodes, depth + 1));
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static void OpenWithShell(string path)
        {
            if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path))) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show(e.Message, "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        // --- filling ------------------------------------------------------------------

        /// <summary>Called by ShowPreview right after the image is displayed, and by HidePreview with null.</summary>
        private void UpdateInfoPane(string imgPath, Image img)
        {
            if (splitPreview == null) return;
            int generation = ++infoGeneration;
            if (imgPath == null)
            {
                currentInfo = null;
                RenderInfoPane();
                return;
            }
            currentInfo = DazzleImageInfo.Collect(imgPath, img, previewLoadWatch.Elapsed, previewFromCache, 0, 0, Math.Max(1, gridViewDS.SelectedRows.Count));
            SwitchConversationsTo(imgPath);   // this image's AI Chat conversation and AI Refine run (Form1.Conversations.cs)
            RenderProposalForCurrentImage();
            RefreshChatResultPanel();
            currentInfo.DecodeFailed = img == null;
            currentInfo.UniqueColorsNote = img == null ? "not counted (image could not be decoded)" : "counting...";
            currentInfo.ExtractedNote = "reading...";
            RenderInfoPane();
            if (splitPreview.Panel2Collapsed) return; // hidden: no background work until it is shown
            StartInfoJobs(currentInfo, img, generation);
        }

        private void StartInfoJobs(DazzleImageInfo info, Image img, int generation)
        {
            // unique colours: copy the pixels on the UI thread (the Image belongs to the cache), count elsewhere
            Bitmap copy = null;
            try
            {
                if (img != null && (long)img.Width * img.Height <= DazzleImageInfo.MaxPixelsToCount)
                    copy = new Bitmap(img);
            }
            catch (Exception) { }
            if (copy == null)
            {
                info.UniqueColors = null;
                if (img != null) info.UniqueColorsNote = "not counted (image too large)";
            }
            else
            {
                Task.Run(() =>
                {
                    long? count; string note;
                    try { count = DazzleImageInfo.CountUniqueColors(copy, out note); }
                    catch (Exception e) { count = null; note = "not counted: " + e.Message; }
                    finally { copy.Dispose(); }
                    BeginInvoke(new Action(() =>
                    {
                        if (generation != infoGeneration) return; // another image by now
                        info.UniqueColors = count; info.UniqueColorsNote = note;
                        RenderInfoPane();
                    }));
                });
            }

            // metadata, embedded prompt, and the native prompt-graph resolver
            string path = info.Path;
            Task.Run(() =>
            {
                DazzleMetadata.Result meta;
                try { meta = DazzleMetadata.Read(path); }
                catch (Exception e) { meta = new DazzleMetadata.Result { Note = "could not read metadata: " + e.Message }; }
                string promptsJson = null, promptsNote = null;
                JObject fingerprintDoc = null;
                if (meta.PromptGraphJson != null)
                {
                    try
                    {
                        var graph = JObject.Parse(meta.PromptGraphJson);
                        if (DazzleComfyPrompts.is_prompt_graph(graph))
                        {
                            var steps = DazzleComfyPrompts.resolve_prompts(graph);
                            var fallback = steps.Count == 0 ? DazzleComfyPrompts.text_nodes(graph) : null;
                            promptsJson = DazzleComfyPrompts.to_json(path, steps, fallback).ToString(Newtonsoft.Json.Formatting.None);
                        }
                        else promptsNote = "embedded prompt chunk is not a ComfyUI prompt graph";
                    }
                    catch (Exception e) { promptsNote = "could not read the prompt graph: " + e.Message; }
                }
                else promptsNote = "no embedded workflow";
                if (meta.WorkflowJson != null)
                {
                    try { fingerprintDoc = JObject.Parse(meta.WorkflowJson); } catch (Exception) { }
                }
                BeginInvoke(new Action(() =>
                {
                    if (generation != infoGeneration) return;
                    info.Extracted = meta.Rows;
                    info.EmbeddedPrompt = meta.Prompt; info.EmbeddedNegative = meta.NegativePrompt;
                    info.EmbeddedWorkflowJson = meta.WorkflowJson ?? meta.PromptGraphJson;
                    info.ExtractedNote = meta.Note;
                    info.ComfyPromptsJson = promptsJson; info.ComfyNote = promptsNote;
                    if (meta.DpiX is float dx && meta.DpiY is float dy && dx > 0 && dy > 0) { info.DpiX = dx; info.DpiY = dy; info.DpiAssumed = false; }
                    info.ComfyFingerprint = fingerprintDoc != null ? FormatFingerprint(DazzleComfyPrompts.extract_workflow_versions(fingerprintDoc)) : null;
                    RenderInfoPane();
                }));
            });
        }

        private static string FormatFingerprint(DazzleComfyPrompts.Fingerprint fp)
        {
            var lines = new List<string>
            {
                "Frontend: " + fp.FrontendVersion,
                "Backend (comfy-core): " + fp.BackendVersion,
                "Renderer: " + fp.RendererVersion,
                "Nodes: " + fp.TotalNodes + " total, " + fp.NodeTypes.Count + " unique types",
            };
            foreach (var kv in fp.Packages.Where(k => k.Key != "comfy-core"))
                lines.Add(kv.Key + " = " + (kv.Value.Length == 40 && kv.Value.All(Uri.IsHexDigit) ? kv.Value.Substring(0, 10) : kv.Value));
            return string.Join("\n", lines);
        }

        // --- rendering ----------------------------------------------------------------

        private void RenderInfoPane()
        {
            if (listInfo == null || splitPreview.Panel2Collapsed) return;
            var info = currentInfo;
            listInfo.BeginUpdate();
            listInfo.Items.Clear();
            if (info == null)
            {
                Row("", "(no image selected)");
            }
            else
            {
                Row("File name", info.FileName);
                Row("Folder", info.Folder);
                if (info.DecodeFailed)
                    Row("Preview", "could not decode this image (WebP needs libwebp next to the program; video needs ScreenLister)");
                Row("Format", info.Width > 0 ? info.Format + " - " + info.Compression : Path.GetExtension(info.Path).TrimStart('.').ToUpperInvariant());
                Row("Image size", info.Width > 0 ? $"{info.Width} x {info.Height}  ({info.Aspect});  {(long)info.Width * info.Height:N0} pixels" : "");
                Row("Print size", info.PrintSize);
                Row("Colours", info.ColorDepth);
                Row("Unique colours", info.UniqueColors is long c ? c.ToString("N0") : (info.UniqueColorsNote ?? ""));
                Row("Zoom", "");
                Row("Selection", "");
                // computed now, not when the image was collected: the first image is shown while the
                // dataset is still filling, so a stored count would read "1 / 1"
                var (position, total) = DatasetPosition(info.Path);
                Row("Position in dataset", total > 0 ? $"{position} / {total}" + (info.SelectedCount > 1 ? $"  (first of {info.SelectedCount} selected)" : "") : "");
                Row("Load time", info.LoadTime.TotalMilliseconds.ToString("0.0") + " ms" + (info.LoadedFromCache ? "  (cached)" : ""));
                Row("Created", info.FileMissing ? "(file not found)" : info.Created.ToString("yyyy-MM-dd HH:mm:ss"));
                Row("Modified", info.FileMissing ? "" : info.Modified.ToString("yyyy-MM-dd HH:mm:ss"));
                Row("Accessed", info.FileMissing ? "" : info.Accessed.ToString("yyyy-MM-dd HH:mm:ss"));
                Row("File size", info.FileMissing ? "" : DazzleImageInfo.FormatSize(info.SizeBytes));
                Row("Attributes", info.FileMissing ? "" : info.AttributesText);
            }
            listInfo.EndUpdate();
            SetViewRows();
            buttonOpenFile.Enabled = buttonOpenFolder.Enabled = info != null && !info.FileMissing;
            RenderExtracted(info);
        }

        private (int position, int total) DatasetPosition(string imgPath)
        {
            int position = 0, total = 0;
            for (int i = 0; i < gridViewDS.RowCount; i++)
            {
                if (!gridViewDS.Rows[i].Visible) continue;
                total++;
                if (position == 0 && (string)gridViewDS.Rows[i].Cells["ImageFilePath"].Value == imgPath) position = total;
            }
            return (position, total);
        }

        private void Row(string key, string value)
        {
            var item = new ListViewItem(key);
            item.SubItems.Add(value ?? "");
            item.ToolTipText = key == "File name" ? (value ?? "") + "   (double-click to rename; the caption file follows; Undo in Chat)" : (value ?? "");
            listInfo.Items.Add(item);
        }

        private TextBox renameBox;

        private void BeginRenameInPlace(ListViewItem row)
        {
            if (currentInfo == null || string.IsNullOrEmpty(currentInfo.Path) || !File.Exists(currentInfo.Path)) return;
            var b = row.SubItems[1].Bounds;
            renameBox.Bounds = new Rectangle(b.X, b.Y, Math.Max(200, b.Width - 4), b.Height);
            renameBox.Text = Path.GetFileNameWithoutExtension(currentInfo.Path);
            renameBox.Tag = currentInfo.Path;
            renameBox.Visible = true;
            renameBox.BringToFront();
            renameBox.Focus();
            renameBox.SelectAll();
        }

        private void CommitRenameInPlace()
        {
            string path = renameBox.Tag as string; string name = renameBox.Text.Trim();
            renameBox.Visible = false;
            if (path == null || name.Length == 0 || name == Path.GetFileNameWithoutExtension(path)) { gridViewDS.Focus(); return; }
            var r = RenameImage(path, name);
            SetStatus(r.ok ? "File " + r.message : "Rename failed: " + r.message);
            Log("rename (info pane): " + Path.GetFileName(path) + " -> " + name + " : " + r.message);
            if (!r.ok) MessageBox.Show(this, r.message, "Rename", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshChatResultPanel();
            gridViewDS.Focus();
        }

        private void RenderExtracted(DazzleImageInfo info)
        {
            treeExtracted.BeginUpdate();
            treeExtracted.Nodes.Clear();
            bool haveWorkflow = false;
            if (info == null)
                treeExtracted.Nodes.Add("(no image selected)");
            else if (info.ExtractedNote == "reading...")
                treeExtracted.Nodes.Add("reading metadata...");
            else
            {
                // EXIF and other directories, grouped
                foreach (var group in info.Extracted.GroupBy(r => r.Group).OrderBy(g => g.Key == "EXIF" ? 0 : g.Key == "Generation" ? 1 : 2).ThenBy(g => g.Key))
                {
                    var g = new TreeNode(group.Key + "  (" + group.Count() + ")");
                    foreach (var r in group)
                        g.Nodes.Add(new TreeNode(r.Key + ": " + r.Value) { Tag = r.Value, ToolTipText = r.Value });
                    treeExtracted.Nodes.Add(g);
                }
                if (info.Extracted.Count == 0 && info.ExtractedNote == null)
                    treeExtracted.Nodes.Add("EXIF: none");
                if (info.ExtractedNote != null)
                    treeExtracted.Nodes.Add(new TreeNode("Note: " + info.ExtractedNote) { Tag = info.ExtractedNote });

                // the embedded single prompt, from BDTM's reader
                if (!string.IsNullOrEmpty(info.EmbeddedPrompt) || !string.IsNullOrEmpty(info.EmbeddedNegative))
                {
                    var p = new TreeNode("Prompt (embedded)");
                    if (!string.IsNullOrEmpty(info.EmbeddedPrompt)) p.Nodes.Add(new TreeNode("positive: " + OneLine(info.EmbeddedPrompt)) { Tag = info.EmbeddedPrompt, ToolTipText = info.EmbeddedPrompt });
                    if (!string.IsNullOrEmpty(info.EmbeddedNegative)) p.Nodes.Add(new TreeNode("negative: " + OneLine(info.EmbeddedNegative)) { Tag = info.EmbeddedNegative, ToolTipText = info.EmbeddedNegative });
                    treeExtracted.Nodes.Add(p);
                }

                // per-stage prompts from the native resolver
                var w = new TreeNode("Workflow");
                if (info.ComfyPromptsJson != null)
                {
                    haveWorkflow = true;
                    try { AddPromptSteps(w, JObject.Parse(info.ComfyPromptsJson), checkCollapseRepeats == null || checkCollapseRepeats.Checked); }
                    catch (Exception e) { w.Nodes.Add("could not render prompts: " + e.Message); }
                }
                else
                    w.Nodes.Add(new TreeNode(info.ComfyNote ?? "no embedded workflow"));
                if (info.ComfyFingerprint != null)
                {
                    haveWorkflow = true;
                    var f = new TreeNode("Version fingerprint") { Tag = info.ComfyFingerprint };
                    foreach (var line in info.ComfyFingerprint.Split('\n')) f.Nodes.Add(new TreeNode(line) { Tag = line });
                    w.Nodes.Add(f);
                }
                treeExtracted.Nodes.Add(w);
                w.Expand();
            }
            treeExtracted.EndUpdate();
            // land on the first stage's POSITIVE text, the thing a reviewer most often wants to read (user, 2026-10-02)
            var firstPositive = FindFirstPositivePart(treeExtracted.Nodes);
            treeExtracted.SelectedNode = firstPositive;
            ShowDetail(firstPositive);
            // An image with a workflow, a prompt, or descriptive metadata (EXIF, XMP, IPTC) opens on the Extracted tab so
            // what it carries is in view at once; structural chunks every file has (PNG-IHDR, ICC) do not count. (user, 2026-10-02)
            if (info != null && info.ExtractedNote != "reading...")
            {
                bool useful = haveWorkflow || !string.IsNullOrEmpty(info.EmbeddedPrompt)
                    || info.Extracted.Any(r => r.Group == "EXIF" || r.Group == "Generation" || r.Group == "XMP" || r.Group == "IPTC");
                tabsInfo.SelectedTab = tabsInfo.TabPages[useful ? "tabPreviewExtracted" : "tabPreviewInfo"];
            }
            buttonCopyWorkflow.Enabled = buttonSaveWorkflow.Enabled = info?.EmbeddedWorkflowJson != null;
            buttonFingerprint.Enabled = haveWorkflow && info != null && !info.FileMissing;
        }

        /// <summary>
        /// Render the resolver's document. With collapse (comfydbg's default), a stage equal to an
        /// earlier one is one line, a side equal to an earlier side is one line, and fields of one
        /// side that hold the same text (Flux clip_l + t5xxl) are one node labelled with both fields.
        /// </summary>
        /// <summary>The first text part under the first stage's POSITIVE side; failing that, the embedded positive prompt; else null.</summary>
        private static TreeNode FindFirstPositivePart(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Text == "Workflow")
                    foreach (TreeNode step in n.Nodes)
                        foreach (TreeNode side in step.Nodes)
                            if (side.Text.StartsWith("POSITIVE") && side.Nodes.Count > 0)
                                return side.Nodes[0];
            }
            foreach (TreeNode n in nodes)
                if (n.Text == "Prompt (embedded)")
                    foreach (TreeNode part in n.Nodes)
                        if (part.Text.StartsWith("positive:"))
                            return part;
            return null;
        }

        private static void AddPromptSteps(TreeNode parent, JObject doc, bool collapse)
        {
            var steps = doc["steps"] as JArray ?? new JArray();
            if (steps.Count == 0)
            {
                var un = doc["unlabelled"] as JArray;
                if (un != null && un.Count > 0)
                {
                    var n = new TreeNode("No sampler with positive/negative inputs; every prompt text node:");
                    foreach (var p in un.OfType<JObject>()) n.Nodes.Add(PartNode(p, null));
                    parent.Nodes.Add(n); n.Expand();
                }
                else parent.Nodes.Add("No prompt text found in the embedded workflow.");
                return;
            }
            foreach (var step in steps.OfType<JObject>())
            {
                string title = $"STEP{step["step"]}  {step["sampler"]?["class"]} (node {step["sampler"]?["node"]})";
                if (collapse && step["same_as"]?.Type == JTokenType.Integer)
                {
                    parent.Nodes.Add(new TreeNode(title + "  = same as STEP" + step["same_as"]) { Tag = title + " is the same as STEP" + step["same_as"] });
                    continue;
                }
                var sn = new TreeNode(title) { Tag = title };
                foreach (var side in new[] { "positive", "negative" })
                {
                    var s = step[side] as JObject;
                    if (s == null) continue;
                    if (collapse && s["same_as"]?.Type == JTokenType.Integer)
                    {
                        sn.Nodes.Add(new TreeNode(side.ToUpper() + "  = same as STEP" + s["same_as"] + " " + side) { Tag = AllText(s) });
                        continue;
                    }
                    string status = s["status"]?.ToString();
                    bool heuristic = s["heuristic"]?.Type == JTokenType.Boolean && (bool)s["heuristic"];
                    var sideNode = new TreeNode(side.ToUpper() + (heuristic ? "  [heuristic]" : "") + (status != "resolved" ? "  (" + status + ": " + s["note"] + ")" : "")) { Tag = AllText(s) };
                    var parts = (s["parts"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                    if (collapse && parts.Count > 1 && parts.Select(p => p["text"]?.ToString() ?? "").Distinct().Count() == 1)
                        sideNode.Nodes.Add(PartNode(parts[0], string.Join(", ", parts.Select(p => p["field"]?.ToString()))));
                    else
                        foreach (var p in parts) sideNode.Nodes.Add(PartNode(p, null));
                    sideNode.Expand();
                    sn.Nodes.Add(sideNode);
                }
                sn.Expand();
                parent.Nodes.Add(sn);
            }
        }

        /// <summary>Every text of a side joined, so clicking the side node shows the whole prompt.</summary>
        private static string AllText(JObject side)
        {
            var parts = (side["parts"] as JArray ?? new JArray()).OfType<JObject>().Select(p => p["text"]?.ToString() ?? "").Distinct().ToList();
            return parts.Count == 0 ? (side["note"]?.ToString() ?? "") : string.Join("\n\n", parts);
        }

        private static TreeNode PartNode(JObject p, string fieldLabel)
        {
            string text = p["text"]?.ToString() ?? "";
            string label = $"{fieldLabel ?? p["field"]?.ToString()}  ({p["source"]?["class"]} node {p["source"]?["node"]}): {OneLine(text)}";
            return new TreeNode(label) { Tag = text, ToolTipText = text };
        }

        private static string OneLine(string s)
        {
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
        }

        private void SaveWorkflowJson()
        {
            var json = currentInfo?.EmbeddedWorkflowJson;
            if (json == null) return;
            using (var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = Path.GetFileNameWithoutExtension(currentInfo.Path) + ".workflow.json", InitialDirectory = currentInfo.Folder })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try { File.WriteAllText(dlg.FileName, json); SetStatus("Saved " + dlg.FileName); }
                    catch (Exception e) { MessageBox.Show(e.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            }
        }

        /// <summary>The one external call left: comfydbg detect compares the workflow's versions with the ComfyUI install.</summary>
        private async Task RunFingerprintAsync()
        {
            var info = currentInfo;
            if (info == null) return;
            int generation = infoGeneration;
            buttonFingerprint.Enabled = false;
            SetStatus("Running comfydbg detect...");
            string text = await DazzleComfydbg.ReadFingerprintAsync(info.Path);
            if (generation != infoGeneration) return;
            SetStatus("comfydbg detect finished");
            using (var f = new Form { Text = "comfydbg detect: " + info.FileName, Width = 820, Height = 600, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false })
            {
                var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f), Text = text.Replace("\n", Environment.NewLine) };
                f.Controls.Add(box);
                f.ShowDialog(this);
            }
            buttonFingerprint.Enabled = true;
        }
    }
}
