using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    // Dazzle fork additions to the main window: the [preview | tags | dataset]
    // layout, the comma-separated tags text box, IrfanView-style navigation and
    // the "check for" box that colours wanted / unwanted tags. Kept in its own
    // file so upstream changes to Form1.cs merge with only a few hook lines in the way.
    public partial class MainForm
    {
        private SplitContainer splitMiddle;         // check box (top) over the image tags (bottom)
        private TabControl tabsTags;                // "Text" | "Grid"
        private TabPage tabTagsText;
        private TabPage tabTagsGrid;
        private RichTextBox textBoxTags;
        private EditableTagList textBoxTagsSource;  // the list the text box was filled from
        private bool textBoxTagsFilling;            // true while we set the text ourselves
        private bool textBoxTagsDirty;              // the user typed since the last fill
        private bool textBoxTagsCommitting;
        private bool gridShownForMultiSelect;       // we switched to Grid, not the user

        private RichTextBox textBoxCheck;           // "tag, -unwantedTag" list to check every image against
        private Timer recolorTimer;
        private bool painting;                      // true while colours are applied

        // Our own undo for the two boxes. RichEdit's built-in undo records every
        // colouring pass, and loading text clears it, so Ctrl+Z never reached the typing.
        private readonly TextHistory tagsHistory = new TextHistory();
        private readonly TextHistory checkHistory = new TextHistory();
        private bool applyingUndo;

        private static readonly Color GoodBack = Color.FromArgb(198, 239, 206); // light green
        private static readonly Color BadBack = Color.FromArgb(255, 199, 206);  // light red
        private static readonly Color MarkFore = Color.Black;                   // readable in any colour scheme

        /// <summary>The control occupying the image-tags pane in the current layout.</summary>
        private Control TagsPane => (Control)splitMiddle ?? toolStripContainer2;

        /// <summary>
        /// Dazzle layout: [preview tabs | image tags | dataset] instead of
        /// the classic [dataset | image tags | preview tabs]. Re-parents the
        /// designer's controls at runtime so Form1.Designer.cs stays as upstream has it.
        /// </summary>
        private void ApplyDazzleLayout()
        {
            if (!Program.Settings.DazzleLayout)
                return;
            SuspendLayout();
            splitContainer1.SuspendLayout();
            splitContainer2.SuspendLayout();
            splitContainer1.Panel1.Controls.Remove(toolStripContainer3);
            splitContainer2.Panel2.Controls.Remove(tabControl1);
            splitContainer2.Panel1.Controls.Remove(toolStripContainer2);
            splitContainer1.Panel1.Controls.Add(tabControl1);
            splitContainer2.Panel2.Controls.Add(toolStripContainer3);
            BuildTagsTextPane();
            splitContainer2.Panel1.Controls.Add(splitMiddle);
            BuildInfoPane();
            BuildExplorerBar();
            // the data layout's start-up notes (a seeded settings copy, a newer configuration elsewhere, a junction
            // not made): said once, in the status bar and the AI log, never a dialog
            Program.Settings.DazzleDataPortable = DazzleData.IsPortable;   // the marker file is the switch; the setting mirrors it
            Log("data: " + (DazzleData.IsPortable ? "portable, " : "") + DazzleData.BaseFolder);
            foreach (var note in DazzleData.Notes) Log("data: " + note);
            if (DazzleData.Notes.Count > 0) statusLabel.Text = DazzleData.Notes[DazzleData.Notes.Count - 1];
            // A picture box cannot take focus, so clicking the preview used to leave
            // focus (and Space) in the tags box. Send it to the dataset list instead.
            pictureBoxPreview.MouseDown += (s, e) => gridViewDS.Focus();
            tabPreview.MouseDown += (s, e) => gridViewDS.Focus();
            tabControl1.MouseUp += (s, e) =>
            {
                if (tabControl1.SelectedIndex == 2) // clicked the Preview tab header
                    gridViewDS.Focus();
            };
            tabControl1.SelectedIndex = 2; // Preview tab
            // the preview pane is the point of this layout, so it is always on
            isShowPreview = true;
            MenuShowPreview.Checked = true;
            splitContainer1.SplitterDistance = (int)(splitContainer1.Width * 0.45);
            splitContainer2.SplitterDistance = (int)(splitContainer2.Width * 0.6);
            splitContainer2.ResumeLayout();
            splitContainer1.ResumeLayout();
            ResumeLayout();
            splitMiddle.SplitterDistance = Math.Max(140, splitMiddle.Height * 2 / 5);
            if (splitRules != null && splitRules.Height > 60) splitRules.SplitterDistance = Math.Max(60, splitRules.Height * 3 / 5);
            PlaceInfoPaneSplitter();
        }

        private void BuildTagsTextPane()
        {
            textBoxTags = new RichTextBox();
            textBoxTags.Name = "textBoxTags";
            textBoxTags.Dock = DockStyle.Fill;
            textBoxTags.BorderStyle = BorderStyle.None;
            textBoxTags.DetectUrls = false;
            textBoxTags.ScrollBars = RichTextBoxScrollBars.Vertical;
            textBoxTags.Font = Program.Settings.GridViewFont.GetFont();
            textBoxTags.Enabled = false;
            textBoxTags.TextChanged += (s, e) =>
            {
                if (painting)
                    return;
                if (!textBoxTagsFilling)
                {
                    textBoxTagsDirty = true;
                    if (!applyingUndo)
                        tagsHistory.Record(textBoxTags.Text, textBoxTags.SelectionStart);
                }
                ScheduleRecolor();
            };
            textBoxTags.HandleCreated += (s, e) => DisableNativeUndo(textBoxTags);
            textBoxTags.Leave += (s, e) => CommitTagsTextBox();
            textBoxTags.KeyDown += TextBoxTags_KeyDown;

            tabTagsText = new TabPage("Text");
            tabTagsText.Name = "tabTagsText";
            tabTagsText.Controls.Add(textBoxTags);
            tabTagsGrid = new TabPage("Grid");
            tabTagsGrid.Name = "tabTagsGrid";
            toolStripContainer2.Dock = DockStyle.Fill;
            tabTagsGrid.Controls.Add(toolStripContainer2);

            tabsTags = new TabControl();
            tabsTags.Name = "tabsTags";
            tabsTags.Dock = DockStyle.Fill;
            tabsTags.TabPages.Add(tabTagsText);
            tabsTags.TabPages.Add(tabTagsGrid);
            tabsTags.Selecting += (s, e) =>
            {
                if (e.TabPage == tabTagsGrid)
                    CommitTagsTextBox();
                else
                    gridShownForMultiSelect = false;
            };

            splitMiddle = new SplitContainer();
            splitMiddle.Name = "splitMiddle";
            splitMiddle.Dock = DockStyle.Fill;
            splitMiddle.Orientation = Orientation.Horizontal;
            splitMiddle.SplitterWidth = 5;
            splitMiddle.Panel2.Controls.Add(tabsTags);

            textBoxCheck = new RichTextBox();
            textBoxCheck.Name = "textBoxCheck";
            textBoxCheck.Dock = DockStyle.Fill;
            textBoxCheck.BorderStyle = BorderStyle.None;
            textBoxCheck.DetectUrls = false;
            textBoxCheck.ScrollBars = RichTextBoxScrollBars.Vertical;
            textBoxCheck.Font = Program.Settings.GridViewFont.GetFont();
            textBoxCheck.Text = Program.Settings.DazzleCheckText;
            checkHistory.Reset(textBoxCheck.Text);
            textBoxCheck.TextChanged += (s, e) =>
            {
                if (painting)
                    return;
                if (!applyingUndo)
                    checkHistory.Record(textBoxCheck.Text, textBoxCheck.SelectionStart);
                ScheduleRecolor();
            };
            textBoxCheck.HandleCreated += (s, e) => DisableNativeUndo(textBoxCheck);
            var checkLabel = new Label();
            checkLabel.Text = "Check for:  tag, -unwanted tag";
            checkLabel.Dock = DockStyle.Top;
            checkLabel.AutoSize = true;
            checkLabel.Padding = new Padding(2, 4, 2, 4);
            // the Rules pane sits above the Check for box, both in the middle pane's top half (the Review mode);
            // the Refine mode (LM Studio pass) swaps in for both, with the caption box below shared
            splitMiddle.Panel1.Controls.Add(BuildMiddleTop(BuildRulesPane(checkLabel, textBoxCheck)));

            recolorTimer = new Timer();
            recolorTimer.Interval = 150;
            recolorTimer.Tick += (s, e) =>
            {
                recolorTimer.Stop();
                RecolorDazzle();
            };
        }

        private const string ProjectUrl = "https://github.com/djdarcy/Simple-AI-Tag-Tool";
        private const string UpstreamUrl = "https://github.com/starik222/BooruDatasetTagManager";
        private const string GuideUrl = ProjectUrl + "/blob/dazzle/docs/simple-ai-tag-tool/guide.md";

        /// <summary>A Help menu at the right end of the menu bar: about, keys, and both project links.</summary>
        private void AddHelpMenu()
        {
            var help = new ToolStripMenuItem("Help");
            help.Name = "MenuDazzleHelp";
            help.Alignment = ToolStripItemAlignment.Right;
            help.DropDownItems.Add("User guide", null, (s, e) => OpenUrl(GuideUrl));
            help.DropDownItems.Add("Keyboard shortcuts", null, (s, e) => OpenUrl(GuideUrl + "#keyboard-shortcuts"));
            help.DropDownItems.Add("About Simple-AI-Tag-Tool", null, (s, e) => ShowAbout());
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add("Simple-AI-Tag-Tool on GitHub", null, (s, e) => OpenUrl(ProjectUrl));
            help.DropDownItems.Add("Original project: BooruDatasetTagManager", null, (s, e) => OpenUrl(UpstreamUrl));
            menuStrip1.Items.Add(help);
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                "Simple-AI-Tag-Tool " + Application.ProductVersion + "\n\n" +
                "A caption review tool for LoRA training datasets: step through images with Space and " +
                "Backspace, edit each image's tags as one comma-separated line, and see at a glance which " +
                "tags you want (green) or don't want (red).\n\n" +
                "Project: " + ProjectUrl + "\n\n" +
                "Based on BooruDatasetTagManager by starik222 (MIT License):\n" + UpstreamUrl + "\n" +
                "Everything that tool does is still here; this fork adds the review layout on top.",
                "About Simple-AI-Tag-Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void OpenUrl(string url)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }

        #region recent folders

        private ToolStripMenuItem menuRecentFolders;

        /// <summary>File > Recent folders, rebuilt each time the File menu opens.</summary>
        private void AddRecentFoldersMenu()
        {
            menuRecentFolders = new ToolStripMenuItem("Recent folders");
            menuRecentFolders.Name = "MenuDazzleRecentFolders";
            int at = fileToolStripMenuItem.DropDownItems.IndexOf(loadFolderWithAdditionalSettingsToolStripMenuItem) + 1;
            fileToolStripMenuItem.DropDownItems.Insert(at, menuRecentFolders);
            fileToolStripMenuItem.DropDownOpening += (s, e) => FillRecentFoldersMenu();
            FillRecentFoldersMenu();
        }

        private void FillRecentFoldersMenu()
        {
            menuRecentFolders.DropDownItems.Clear();
            var folders = Program.RecentFolders.Folders;
            if (!Program.Settings.DazzleRememberFolders || folders.Count == 0)
            {
                menuRecentFolders.DropDownItems.Add(new ToolStripMenuItem(Program.Settings.DazzleRememberFolders ? "(none yet)" : "(turned off in Settings > UI)") { Enabled = false });
                return;
            }
            for (int i = 0; i < folders.Count; i++)
            {
                string folder = folders[i];
                // &1..&5 give each entry an Alt-key accelerator, like most Windows MRU lists
                var item = new ToolStripMenuItem("&" + (i + 1) + "  " + folder.Replace("&", "&&"));
                item.ToolTipText = folder;
                item.Click += async (s, e) => await OpenRecentFolder(folder);
                menuRecentFolders.DropDownItems.Add(item);
            }
            menuRecentFolders.DropDownItems.Add(new ToolStripSeparator());
            menuRecentFolders.DropDownItems.Add("Clear recent folders", null, (s, e) =>
            {
                Program.RecentFolders.Clear();
                SetStatus("Recent folders cleared (the stored list was overwritten, then deleted)");
            });
        }

        private async Task OpenRecentFolder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                MessageBox.Show(this, "This folder no longer exists:\n" + folder, "Recent folders", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            await LoadFromFolderAsync(false, folder);
        }

        /// <summary>Called after a folder loads successfully.</summary>
        private void RememberFolder(string folder)
        {
            if (Program.Settings.DazzleRememberFolders)
                Program.RecentFolders.Add(folder);
        }

        /// <summary>The folder to open at startup: the command line first, else the most recent one.</summary>
        private string StartupFolderToOpen()
        {
            if (!string.IsNullOrEmpty(Program.StartupFolder))
                return Program.StartupFolder;
            if (Program.Settings.DazzleRememberFolders && Program.Settings.DazzleReopenLastFolder && Program.RecentFolders.Folders.Count > 0)
                return Program.RecentFolders.Folders[0];
            return null;
        }

        #endregion

        private void ScheduleRecolor()
        {
            recolorTimer.Stop();
            recolorTimer.Start();
        }

        private const int EM_SETUNDOLIMIT = 0x0452;

        /// <summary>Stop RichEdit recording undo steps (it would mostly record our colouring).</summary>
        private static void DisableNativeUndo(RichTextBox box)
        {
            SendMessage(box.Handle, EM_SETUNDOLIMIT, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z inside either box. Returns true if the key was ours.</summary>
        private bool DazzleUndoKey(Keys keyData)
        {
            RichTextBox box = null;
            if (textBoxTags != null && textBoxTags.Focused)
                box = textBoxTags;
            else if (textBoxCheck != null && textBoxCheck.Focused)
                box = textBoxCheck;
            if (box == null)
                return false;
            var history = box == textBoxTags ? tagsHistory : checkHistory;
            bool changed;
            string text;
            int caret;
            if (keyData == (Keys.Control | Keys.Z))
                changed = history.Undo(out text, out caret);
            else if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
                changed = history.Redo(out text, out caret);
            else
                return false;
            if (changed)
            {
                applyingUndo = true;
                try
                {
                    box.Text = text;
                    box.SelectionStart = Math.Min(caret, box.TextLength);
                }
                finally
                {
                    applyingUndo = false;
                }
            }
            return true; // consumed even with nothing to undo, so BDTM's tag undo never fires from here
        }

        /// <summary>
        /// Snapshot undo/redo for a text box. Typing within GroupMs of the previous
        /// edit joins the same step, so Ctrl+Z undoes a burst of typing, not one letter.
        /// </summary>
        private sealed class TextHistory
        {
            private const int GroupMs = 800;
            private readonly List<(string Text, int Caret)> undo = new List<(string, int)>();
            private readonly List<(string Text, int Caret)> redo = new List<(string, int)>();
            private string current = "";
            private int currentCaret;
            private DateTime lastEdit = DateTime.MinValue;

            public void Reset(string text)
            {
                undo.Clear();
                redo.Clear();
                current = text;
                currentCaret = text.Length;
                lastEdit = DateTime.MinValue;
            }

            public void Record(string text, int caret, bool newStep = false)
            {
                if (text == current)
                    return;
                var now = DateTime.UtcNow;
                if (newStep || undo.Count == 0 || (now - lastEdit).TotalMilliseconds > GroupMs)
                    undo.Add((current, currentCaret));
                redo.Clear();
                current = text;
                currentCaret = caret;
                lastEdit = newStep ? DateTime.MinValue : now;
            }

            public bool Undo(out string text, out int caret)
            {
                return Step(undo, redo, out text, out caret);
            }

            public bool Redo(out string text, out int caret)
            {
                return Step(redo, undo, out text, out caret);
            }

            private bool Step(List<(string Text, int Caret)> from, List<(string Text, int Caret)> to, out string text, out int caret)
            {
                text = current;
                caret = currentCaret;
                if (from.Count == 0)
                    return false;
                to.Add((current, currentCaret));
                (current, currentCaret) = from[from.Count - 1];
                from.RemoveAt(from.Count - 1);
                lastEdit = DateTime.MinValue;
                text = current;
                caret = currentCaret;
                return true;
            }
        }

        /// <summary>Called when the window closes: keep the check list for next time.</summary>
        private void SaveDazzleState()
        {
            if (textBoxCheck == null || textBoxCheck.Text == Program.Settings.DazzleCheckText)
                return;
            Program.Settings.DazzleCheckText = textBoxCheck.Text;
            Program.Settings.SaveSettings();
        }

        #region check colouring

        private struct TokenRange
        {
            public int Start;
            public int Length;
            public string Text;
        }

        /// <summary>Split text on any of the separators, keeping each trimmed piece's position.</summary>
        private static List<TokenRange> Tokenize(string text, string[] separators)
        {
            var result = new List<TokenRange>();
            int pos = 0;
            while (pos <= text.Length)
            {
                int end = text.Length;
                int sepLen = 0;
                foreach (var sep in separators)
                {
                    if (string.IsNullOrEmpty(sep))
                        continue;
                    int i = text.IndexOf(sep, pos, StringComparison.Ordinal);
                    if (i >= 0 && i < end)
                    {
                        end = i;
                        sepLen = sep.Length;
                    }
                }
                int s = pos, e = end;
                while (s < e && char.IsWhiteSpace(text[s])) s++;
                while (e > s && char.IsWhiteSpace(text[e - 1])) e--;
                if (e > s)
                    result.Add(new TokenRange { Start = s, Length = e - s, Text = text.Substring(s, e - s) });
                if (sepLen == 0)
                    break;
                pos = end + sepLen;
            }
            return result;
        }

        private static readonly Regex WeightedTag = new Regex(@"^\(+(.*?)(?::\s*[+-]?[\d.]+)?\)+$", RegexOptions.Compiled);

        /// <summary>
        /// The form two tags are compared in: case-insensitive, '_' same as ' ',
        /// weights and escapes removed, so "Long_Hair", "long hair" and
        /// "(long hair:1.2)" all match.
        /// </summary>
        private static string NormalizeTag(string tag)
        {
            string t = tag.Trim();
            var m = WeightedTag.Match(t);
            if (m.Success)
                t = m.Groups[1].Value;
            t = t.Replace("\\(", "(").Replace("\\)", ")").Replace('_', ' ');
            return Regex.Replace(t, @"\s+", " ").Trim().ToLowerInvariant();
        }

        private static string[] TagSeparators()
        {
            string sep = Program.Settings.SeparatorOnLoad.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
            return new[] { sep, "\n" };
        }

        /// <summary>
        /// Colour both boxes. Check box: a wanted tag is green when the image has it and
        /// red when it doesn't; an unwanted (-tag) is green when absent and red when present.
        /// Tags box: tags you want are green, tags you don't want are red.
        /// </summary>
        private void RecolorDazzle()
        {
            if (textBoxCheck == null || textBoxTags == null)
                return;
            var checks = Tokenize(textBoxCheck.Text, new[] { ",", "\n" })
                .Select(r =>
                {
                    bool unwanted = r.Text.StartsWith("-");
                    return (range: r, unwanted, key: NormalizeTag(unwanted ? r.Text.Substring(1) : r.Text));
                })
                .Where(c => c.key.Length > 0)
                .ToList();
            bool haveImage = textBoxTags.Enabled;
            var tagRanges = haveImage ? Tokenize(textBoxTags.Text, TagSeparators()) : new List<TokenRange>();
            // one matcher for everything: the Check for entries, the rules, and the caption colouring
            var mode = Program.Settings.DazzleTagMatch;
            var tagSet = new DazzleRules.TagSet(tagRanges.Select(r => r.Text), mode);
            var wanted = new HashSet<string>(checks.Where(c => !c.unwanted).Select(c => c.key));
            var unwantedKeys = new HashSet<string>(checks.Where(c => c.unwanted).Select(c => c.key));
            var conflicts = new HashSet<string>();
            var ev = EvaluateRulesForImage(tagSet, haveImage);
            if (ev != null)
            {
                wanted.UnionWith(ev.Required);
                unwantedKeys.UnionWith(ev.Forbidden);
                conflicts.UnionWith(ev.Conflicts);
            }

            var checkMarks = new List<(TokenRange, Color)>();
            if (haveImage)
            {
                foreach (var c in checks)
                    checkMarks.Add((c.range, tagSet.Has(c.key) != c.unwanted ? GoodBack : BadBack));
            }
            bool Matches(string term, string key) => term == key || (mode == DazzleRules.TagMatch.Lazy && DazzleRules.TagSet.PhraseIn(term, key) >= 0);
            var tagMarks = new List<(TokenRange, Color)>();
            foreach (var r in tagRanges)
            {
                string key = NormalizeTag(r.Text);
                if (conflicts.Any(t => Matches(t, key)))
                    tagMarks.Add((r, ConflictBack));
                else if (unwantedKeys.Any(t => Matches(t, key)))
                    tagMarks.Add((r, BadBack));
                else if (wanted.Any(t => Matches(t, key)))
                    tagMarks.Add((r, GoodBack));
            }
            PaintMarks(textBoxCheck, checkMarks);
            PaintMarks(textBoxTags, tagMarks);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private const int WM_SETREDRAW = 0x000B;

        private void PaintMarks(RichTextBox box, List<(TokenRange range, Color back)> marks)
        {
            painting = true;
            int selStart = box.SelectionStart, selLength = box.SelectionLength;
            SendMessage(box.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                box.SelectAll();
                box.SelectionBackColor = box.BackColor;
                box.SelectionColor = box.ForeColor;
                foreach (var m in marks)
                {
                    box.Select(m.range.Start, m.range.Length);
                    box.SelectionBackColor = m.back;
                    box.SelectionColor = MarkFore;
                }
                box.Select(selStart, selLength);
            }
            finally
            {
                SendMessage(box.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                box.Invalidate();
                painting = false;
            }
        }

        #endregion

        private void TextBoxTags_KeyDown(object sender, KeyEventArgs e)
        {
            // Enter commits, like a one-line field; Shift+Enter still inserts a line break
            // for files that keep several captions on separate lines.
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                CommitTagsTextBox();
                e.SuppressKeyPress = true;
            }
        }

        /// <summary>
        /// Show one image's tags as the file holds them. Null means several images
        /// (or none) are selected: there is no single comma string, so show the grid.
        /// </summary>
        private void ShowTagsInTextBox(EditableTagList tags)
        {
            if (textBoxTags == null)
                return;
            if (textBoxTagsSource != null)
                textBoxTagsSource.ListChanged -= TextBoxTagsSource_ListChanged;
            textBoxTagsSource = tags;
            if (tags != null)
                tags.ListChanged += TextBoxTagsSource_ListChanged;
            RefreshTagsTextBox(newImage: true);
            textBoxTags.Enabled = tags != null;
            if (tags == null && tabsTags.SelectedTab == tabTagsText)
            {
                tabsTags.SelectedTab = tabTagsGrid;
                gridShownForMultiSelect = true;
            }
            else if (tags != null && gridShownForMultiSelect)
            {
                tabsTags.SelectedTab = tabTagsText;
                gridShownForMultiSelect = false;
            }
        }

        /// <param name="newImage">true when another image was selected: its box starts a fresh undo history</param>
        private void RefreshTagsTextBox(bool newImage = false)
        {
            textBoxTagsFilling = true;
            textBoxTags.Text = textBoxTagsSource?.ToString() ?? "";
            textBoxTagsFilling = false;
            textBoxTagsDirty = false;
            if (newImage)
                tagsHistory.Reset(textBoxTags.Text);
            else
                tagsHistory.Record(textBoxTags.Text, textBoxTags.TextLength, newStep: true);
            textBoxTags.Enabled = textBoxTagsSource != null;
            recolorTimer.Stop();
            RecolorDazzle(); // colour straight away on a new image, no debounce
        }

        private void TextBoxTagsSource_ListChanged(object sender, ListChangedEventArgs e)
        {
            // Changed elsewhere (grid, undo, autotagger, add-to-all): follow it,
            // unless the user has uncommitted typing in the box.
            if (!textBoxTagsCommitting && !textBoxTagsDirty)
                RefreshTagsTextBox();
        }

        /// <summary>
        /// Parse the text box with the same parser used when a file is loaded and
        /// replace the image's tags as one undo step. Called on leave, Enter,
        /// before changing image, saving, opening a folder or closing.
        /// </summary>
        private void CommitTagsTextBox()
        {
            if (textBoxTags == null || textBoxTagsSource == null || !textBoxTagsDirty)
                return;
            if (textBoxTags.Text == textBoxTagsSource.ToString())
            {
                textBoxTagsDirty = false; // typed (or undone) back to what is already committed
                return;
            }
            var parsed = PromptParser.ParsePrompt(textBoxTags.Text, Program.Settings.FixTagsOnSaveLoad, Program.Settings.SeparatorOnLoad);
            textBoxTagsCommitting = true;
            try
            {
                textBoxTagsSource.ReplaceAll(parsed);
            }
            finally
            {
                textBoxTagsCommitting = false;
            }
            int caret = textBoxTags.SelectionStart;
            RefreshTagsTextBox();
            textBoxTags.SelectionStart = Math.Min(caret, textBoxTags.TextLength);
        }

        private bool IsDazzleTextBoxFocused()
        {
            return textBoxTags != null && (textBoxTags.Focused || textBoxCheck.Focused);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        /// <summary>A key that types into a box: no modifier or Shift only, and not a function key.</summary>
        private static bool IsTypedKey(Keys keyData)
        {
            Keys mods = keyData & Keys.Modifiers, key = keyData & Keys.KeyCode;
            if ((mods & (Keys.Control | Keys.Alt)) != 0) return false;
            return !(key >= Keys.F1 && key <= Keys.F24);
        }

        /// <summary>True when keystrokes are going into something you type in.</summary>
        private static bool IsTypingFocus()
        {
            Control focused = Control.FromChildHandle(GetFocus());
            // a read-only box (the info pane's prompt view) is for selecting and copying, so Space still steps images
            return (focused is TextBoxBase tb && !tb.ReadOnly) || focused is ComboBox;
        }

        /// <summary>
        /// IrfanView-style image stepping, driven by the Dazzle* entries in Settings > Hotkeys
        /// (defaults: Space/Right next and Backspace/Left previous when not typing;
        /// Alt+Right/Alt+Left anywhere; PageDown/PageUp inside the tags or check box;
        /// Esc leaves the box). The rule belongs to the entry, not the key, so remapping
        /// keeps it. Returns true if handled.
        /// </summary>
        private bool DazzleNavigationKey(Keys keyData, bool isAutoRepeat)
        {
            if (!Program.Settings.DazzleLayout || Program.DataManager == null)
                return false;
            if (folderDialogOpen)
                return true; // swallow: the end-of-folder dialog is waiting for an answer
            if (ignoreHeldKey)
            {
                // A key held through the folder dialog must not keep stepping in the new
                // folder (held Right landed on its last image); wait for a fresh press.
                if (isAutoRepeat)
                    return true;
                ignoreHeldKey = false;
            }
            // the rules grid uses arrows and Space itself (cell movement, edit); treat it as typing
            bool typing = IsTypingFocus() || (gridRules != null && gridRules.Focused);
            bool inTagsBox = IsDazzleTextBoxFocused();
            Control boxToRefocus = textBoxCheck != null && textBoxCheck.Focused ? textBoxCheck : textBoxTags;
            int step = 0;
            foreach (var item in Program.Settings.Hotkeys.Items)
            {
                if (!item.Id.StartsWith("Dazzle") || item.FullKeyData != keyData)
                    continue;
                switch (item.Id)
                {
                    case "DazzleNextImage":
                    case "DazzleNextImage2":
                        if (!typing) step = 1;
                        break;
                    case "DazzlePrevImage":
                    case "DazzlePrevImage2":
                        if (!typing) step = -1;
                        break;
                    case "DazzleNextImageAnywhere":
                        step = 1;
                        break;
                    case "DazzlePrevImageAnywhere":
                        step = -1;
                        break;
                    case "DazzleNextImageInBox":
                        if (inTagsBox) step = 1;
                        break;
                    case "DazzlePrevImageInBox":
                        if (inTagsBox) step = -1;
                        break;
                    case "DazzleInfoPane":
                        if (!typing)
                        {
                            ToggleInfoPane();
                            return true;
                        }
                        break;
                    case "DazzleZoomIn":
                    case "DazzleZoomIn2":
                    case "DazzleZoomOut":
                    case "DazzleZoomOut2":
                    case "DazzleZoomFit":
                    case "DazzleZoomActual":
                    case "DazzleZoomSelection":
                    case "DazzleClearSelection":
                        // the viewer owns the mouse, the window owns the keyboard: zoom keys live here
                        if (!typing && imageView?.Image != null && !gridViewTags.IsCurrentCellInEditMode)
                        {
                            switch (item.Id)
                            {
                                case "DazzleZoomIn": case "DazzleZoomIn2": imageView.ZoomIn(); break;
                                case "DazzleZoomOut": case "DazzleZoomOut2": imageView.ZoomOut(); break;
                                case "DazzleZoomFit": imageView.ZoomToFit(); break;
                                case "DazzleZoomActual": imageView.ZoomActual(); break;
                                case "DazzleZoomSelection": if (!imageView.HasSelection) return false; imageView.ZoomToSelection(); break;
                                case "DazzleClearSelection": if (!imageView.HasSelection) return false; imageView.ClearSelection(); break;
                            }
                            return true;
                        }
                        break;
                    case "DazzleLeaveBox":
                        if (inTagsBox)
                        {
                            CommitTagsTextBox();
                            gridViewDS.Focus();
                            return true;
                        }
                        break;
                    case "DazzleFirstImage":
                    case "DazzleLastImage":
                        if (!typing)
                        {
                            CommitTagsTextBox();
                            bool first = item.Id == "DazzleFirstImage";
                            int row = NextVisibleRow(first ? -1 : gridViewDS.RowCount, first ? 1 : -1);
                            if (row >= 0)
                                SelectDatasetRow(row);
                            return true;
                        }
                        break;
                }
                if (step != 0)
                    break; // first entry whose rule applies wins
            }
            if (step == 0)
                return false;
            StepImage(step);
            if (inTagsBox)
                boxToRefocus.Focus(); // keep typing on the next image
            return true;
        }

        private void StepImage(int step)
        {
            CommitTagsTextBox();
            if (gridViewDS.RowCount == 0)
                return;
            int current = gridViewDS.CurrentCell?.RowIndex ?? -1;
            int next = NextVisibleRow(current, step);            if (next < 0)
            {
                // ran off the first or last image
                var action = Program.Settings.DazzleEndOfFolder;
                if (action == EndOfFolderAction.Stop)
                    return;
                if (action == EndOfFolderAction.Ask)
                {
                    AskForNextFolder(step); // the first image is the same boundary as the last
                    return;
                }
                next = NextVisibleRow(step > 0 ? -1 : gridViewDS.RowCount, step); // wrap around
                if (next < 0)
                    return;
            }
            SelectDatasetRow(next);
        }

        /// <summary>The next visible row from <paramref name="from"/> in direction <paramref name="step"/>, or -1.</summary>
        private int NextVisibleRow(int from, int step)
        {
            int next = from;
            do
            {
                next += step;
            } while (next >= 0 && next < gridViewDS.RowCount && !gridViewDS.Rows[next].Visible);
            return next >= 0 && next < gridViewDS.RowCount ? next : -1;
        }

        private void SelectDatasetRow(int row)
        {
            var firstColumn = gridViewDS.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
            if (firstColumn == null)
                return;
            // Setting the current cell selects that row alone (FullRowSelect) and fires
            // SelectionChanged, which loads the image, its tags and the preview.
            gridViewDS.CurrentCell = gridViewDS[firstColumn.Index, row];
        }

        private string dazzleDatasetFolder; // the folder last loaded, for the end-of-folder dialog
        private bool folderDialogOpen;      // one end-of-folder dialog at a time
        private bool ignoreHeldKey;         // after the dialog: ignore auto-repeat until a fresh key press

        /// <summary>True for the repeated WM_KEYDOWNs Windows sends while a key is held (lParam bit 30).</summary>
        private static bool IsAutoRepeat(Message msg)
        {
            return (msg.Msg == 0x0100 || msg.Msg == 0x0104) && ((long)msg.LParam & 0x40000000) != 0;
        }

        /// <summary>
        /// IrfanView-style "end (or start) of folder reached": loop, or open another folder.
        /// Going forward (step 1) a folder opens on its first image; going backward
        /// (step -1) it opens on its last image, so you keep moving the same way.
        /// </summary>
        private async void AskForNextFolder(int step)
        {
            if (string.IsNullOrEmpty(dazzleDatasetFolder) || folderDialogOpen)
                return;
            bool backward = step < 0;
            using (var dialog = new Form_DazzleNextFolder(dazzleDatasetFolder, atStart: backward))
            {
                // Keys aimed at the main window while the dialog is up (key repeat, a held
                // Space) must not open another one: found when five stacked up in testing.
                folderDialogOpen = true;
                DialogResult result;
                try
                {
                    result = dialog.ShowDialog(this);
                }
                finally
                {
                    folderDialogOpen = false;
                    ignoreHeldKey = true;
                }
                if (result != DialogResult.OK)
                    return;
                string chosen = dialog.SelectedFolder;
                if (!string.Equals(Path.GetFullPath(chosen).TrimEnd('\\'), Path.GetFullPath(dazzleDatasetFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    await LoadFromFolderAsync(false, chosen);
                    gridViewDS.Focus();
                }
                // same folder: loop round; another folder: start at the end you are heading into
                int row = NextVisibleRow(backward ? gridViewDS.RowCount : -1, backward ? -1 : 1);
                if (row >= 0)
                    SelectDatasetRow(row);
            }
        }

        /// <summary>Text-editing chords that belong to a focused text box, not to BDTM's hotkeys.</summary>
        private static bool IsTextEditingChord(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Z:
                case Keys.Control | Keys.Shift | Keys.Z:
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.A:
                case Keys.Control | Keys.C:
                case Keys.Control | Keys.V:
                case Keys.Control | Keys.X:
                    return true;
                default:
                    return false;
            }
        }
    }
}
