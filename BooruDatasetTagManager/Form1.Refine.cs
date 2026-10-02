using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    // Simple-AI-Tag-Tool: the Refine group -- the middle pane's top half can show either Review
    // (rules over the Check for box) or Refine (a skill and instruction, Play / Stop, the current
    // caption beside the model's proposal as coloured chips, per-chip accept, logs). The caption
    // Text box below is shared: accepting composes into it, and Ctrl+S remains the only write.
    // Design: 2026-10-02__04-30-47__dev-workflow-process__satt-lm-studio-refinement-pass.md
    public partial class MainForm
    {
        private Panel panelMiddleTop;
        private ToolStrip modeStrip;
        private ToolStripButton modeReview, modeRefine;
        private Control reviewPane;
        private Panel panelRefine;
        private ToolStripComboBox comboSkills;
        private ToolStripButton buttonPlay, buttonStop, buttonLogs, buttonSaveSkill;
        private ToolStripButton checkThink, checkSchema;
        private TextBox textInstruction;
        private Label labelRefineStatus;
        private SplitContainer splitSides;
        private FlowLayoutPanel flowLeft, flowRight;
        private Label labelLeft, labelRight;
        private Button buttonTakeLeft, buttonTakeRight, buttonApplyChips;
        private Form logForm; private TextBox logBox;
        private readonly StringBuilder refineLog = new StringBuilder();
        private CancellationTokenSource runCts;
        private string runImagePath;
        private readonly Dictionary<string, (string left, string right, DazzleLmStudio.RunResult result)> proposals = new Dictionary<string, (string, string, DazzleLmStudio.RunResult)>(StringComparer.OrdinalIgnoreCase);
        private DazzleCaptionDiff.Result currentDiff;
        private readonly HashSet<int> droppedRight = new HashSet<int>();
        private readonly HashSet<int> keptLeft = new HashSet<int>();
        private string loadedSkillText = "";

        // Format only: what the tool needs to read the reply. What to keep, drop or add is the skill's to say (user,
        // 2026-10-02: "Keep tags that are still true, drop ... add what is missing" turned every skill into a captioning pass).
        private const string OutputContract = "\n\nReply with the complete new caption only, as one line of comma-separated items. A short descriptive sentence may be one of the items, but it must not contain commas (use 'and' instead), because commas separate items. No labels such as 'Tags:' or 'Caption:', no preamble, no explanation.";

        /// <summary>
        /// Tidy what a model adds despite the contract: a label ("Tags: a, b"), and a sentence that runs straight into
        /// the first tag with no comma ("... dark clothing. demon") -- a short tail after the last sentence end becomes its own item.
        /// </summary>
        private static string CleanProposal(string reply)
        {
            var items = new List<string>();
            foreach (var raw in DazzleCaptionDiff.SplitItems(reply))
            {
                string i = System.Text.RegularExpressions.Regex.Replace(raw, @"^\s*(tags?|caption|description|keywords?)\s*:\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                if (i.Length == 0) continue;
                var m = System.Text.RegularExpressions.Regex.Match(i, @"^(.*[.!?])\s+([^.!?]{1,40})$");
                if (m.Success && m.Groups[2].Value.Split(' ').Length <= 4 && DazzleCaptionDiff.LooksLikeSentence(m.Groups[1].Value))
                {
                    items.Add(m.Groups[1].Value.Trim());
                    items.Add(m.Groups[2].Value.Trim());
                }
                else items.Add(i);
            }
            return string.Join(", ", items);
        }

        // ---------------------------------------------------------------- layout

        /// <summary>Wraps the Review pane with a Review | Refine strip and the Refine pane; the caller docks the result.</summary>
        private Control BuildMiddleTop(Control review)
        {
            reviewPane = review;
            reviewPane.Dock = DockStyle.Fill;
            panelMiddleTop = new Panel { Dock = DockStyle.Fill, Name = "panelMiddleTop" };
            modeStrip = new ToolStrip { Name = "modeStrip", GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
            modeReview = new ToolStripButton("Review") { CheckOnClick = false, Checked = true, ToolTipText = "Rules and the Check for list" };
            modeRefine = new ToolStripButton("AI Refine") { CheckOnClick = false, ToolTipText = "AI Refine: ask a local vision model (LM Studio) for a new caption and review it as a diff" };
            modeChat = new ToolStripButton("AI Chat") { CheckOnClick = false, ToolTipText = "AI Chat: talk with the model about the image; it can set the caption, rename or move the file through tools, every change undoable" };
            modeReview.Click += (s, e) => SetMiddleMode(0);
            modeRefine.Click += (s, e) => SetMiddleMode(1);
            modeChat.Click += (s, e) => SetMiddleMode(2);
            modeStrip.Items.AddRange(new ToolStripItem[] { modeReview, modeRefine, modeChat });
            modeStrip.MouseUp += (s, e) => gridViewDS.Focus();
            BuildRefinePane();
            BuildChatPane();
            panelMiddleTop.Controls.Add(panelChat);
            panelMiddleTop.Controls.Add(panelRefine);
            panelMiddleTop.Controls.Add(reviewPane);
            panelMiddleTop.Controls.Add(modeStrip);
            SetMiddleMode(Program.Settings.DazzleMiddleMode);
            return panelMiddleTop;
        }

        private ToolStripButton modeChat;

        /// <summary>0 = Review (rules + Check for), 1 = Refine, 2 = Chat. Chat also swaps the bottom half (transcript for the caption box).</summary>
        private void SetMiddleMode(int mode)
        {
            mode = Math.Max(0, Math.Min(2, mode));
            modeReview.Checked = mode == 0; modeRefine.Checked = mode == 1; modeChat.Checked = mode == 2;
            reviewPane.Visible = mode == 0; panelRefine.Visible = mode == 1;
            ShowChatMode(mode == 2);
            Program.Settings.DazzleMiddleMode = mode;
            Program.Settings.DazzleRefineMode = mode == 1;
            if (mode == 1) { LoadSkillsList(); RenderProposalForCurrentImage(); BeginInvoke(new Action(PlaceInstructionSplitter)); }
        }

        private void BuildRefinePane()
        {
            panelRefine = new Panel { Dock = DockStyle.Fill, Name = "panelRefine", Visible = false };
            var strip = new ToolStrip { Name = "refineStrip", GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
            comboSkills = new ToolStripComboBox("comboSkills") { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 190, ToolTipText = "AI skills: one file each in skills\\refine in your data folder, plus the shipped ones beside the program" };
            comboSkills.SelectedIndexChanged += SkillsSelectionChanged;
            buttonSaveSkill = new ToolStripButton("Save as...") { ToolTipText = "Save the instruction text as a new skill file" };
            buttonSaveSkill.Click += (s, e) => SaveSkillAs();
            // the strip's own tips are one long line that hides after a few seconds (user, 2026-10-02): a wrapped block
            // shown for 20 s instead, attached per item below once the items exist
            strip.ShowItemToolTips = false;
            // Play and Stop as the glyphs everyone knows, drawn here so they follow the DPI and need no resource file (user, 2026-10-02)
            buttonPlay = new ToolStripButton { Name = "buttonPlay", Text = "Play", Image = PlayGlyph(), DisplayStyle = ToolStripItemDisplayStyle.Image, ImageScaling = ToolStripItemImageScaling.None, ToolTipText = "Play: send the image, the instruction and the current caption to the model" };
            buttonPlay.Click += async (s, e) => await RunRefineAsync();
            buttonStop = new ToolStripButton { Name = "buttonStop", Text = "Stop", Image = StopGlyph(), DisplayStyle = ToolStripItemDisplayStyle.Image, ImageScaling = ToolStripItemImageScaling.None, Enabled = false, ToolTipText = "Stop: cancel the run (the server stops generating when the connection closes)" };
            buttonStop.Click += (s, e) => runCts?.Cancel();
            // refresh the left side from the caption box by hand; it also follows typing (a comma or a space, or a pause) -- user, 2026-10-02
            buttonRefreshCaption = new ToolStripButton { Name = "buttonRefreshCaption", Text = "Refresh", Image = RefreshGlyph(), DisplayStyle = ToolStripItemDisplayStyle.Image, ImageScaling = ToolStripItemImageScaling.None, ToolTipText = "Refresh: redraw the Current caption chips from the caption box below. They also follow as you type, after each comma or space." };
            buttonRefreshCaption.Click += (s, e) => { lastRenderKey = null; RenderProposalForCurrentImage(); };
            captionFollowTimer = new System.Windows.Forms.Timer { Interval = 700 };
            captionFollowTimer.Tick += (s, e) => { captionFollowTimer.Stop(); RenderProposalForCurrentImage(); };
            checkThink = new ToolStripButton("Think") { CheckOnClick = true, Checked = Program.Settings.DazzleRefineThink, ToolTipText = "Think: let the model reason before it answers. Slower (a few seconds), but it follows conditions and instructions more carefully; the reasoning is shown in Logs. Off sends reasoning_effort = none for a quick answer." };
            checkThink.CheckedChanged += (s, e) => Program.Settings.DazzleRefineThink = checkThink.Checked;
            checkSchema = new ToolStripButton("Schema") { CheckOnClick = true, Checked = Program.Settings.DazzleRefineSchema, ToolTipText = "Schema: ask the server for the reply as strict JSON {\"caption\": ...} so the model cannot wrap the caption in chatter. If the server refuses the format, the run is repeated for plain text automatically. Turn off only for a server that answers badly to it." };
            checkSchema.CheckedChanged += (s, e) => Program.Settings.DazzleRefineSchema = checkSchema.Checked;
            buttonLogs = new ToolStripButton("Logs") { ToolTipText = "Logs: every run's request, the model's reasoning, the reply and any error, with Copy" };
            buttonLogs.Click += (s, e) => ShowLog();
            var buttonHelp = new ToolStripButton { Name = "buttonRefineHelp", Text = "Help", Image = HelpGlyph(), DisplayStyle = ToolStripItemDisplayStyle.Image, ImageScaling = ToolStripItemImageScaling.None, Alignment = ToolStripItemAlignment.Right, ToolTipText = "Help: the Refine section of the user guide (opens in your browser)" };
            buttonHelp.Click += (s, e) => OpenUrl(GuideUrl + "#ai-refine-a-local-ai-pass");
            strip.Items.AddRange(new ToolStripItem[] { new ToolStripLabel("AI skill:"), comboSkills, buttonSaveSkill, new ToolStripSeparator(), buttonPlay, buttonStop, buttonRefreshCaption, new ToolStripSeparator(), checkThink, checkSchema, new ToolStripSeparator(), buttonLogs, buttonHelp });
            foreach (ToolStripItem it in strip.Items) if (!string.IsNullOrEmpty(it.ToolTipText)) AttachBlockTip(strip, it, it.ToolTipText);

            var gridFont = Program.Settings.GridViewFont.GetFont();
            // sizes follow the font, not pixel constants: at 144 dpi a 64-px box showed two lines of a five-line skill (user, 2026-10-02)
            textInstruction = new TextBox { Name = "textInstruction", Multiline = true, Dock = DockStyle.Top, Height = gridFont.Height * 5 + 10, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Font = gridFont };
            textInstruction.TextChanged += (s, e) => { if (textInstruction.Text != loadedSkillText) labelRefineStatus.Text = "instruction edited (Save as... to keep it as a skill)"; };
            // auto-sized, docked labels wrap to the pane's width instead of clipping
            labelRefineStatus = new Label { Name = "labelRefineStatus", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(3, 3, 3, 3), Text = "Pick a skill, then Play." };

            splitSides = new SplitContainer { Name = "splitSides", Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 4 };
            labelLeft = new Label { Dock = DockStyle.Top, AutoSize = true, Text = "Current caption", Padding = new Padding(3, 3, 3, 2), Font = new Font(gridFont, FontStyle.Bold) };
            labelRight = new Label { Dock = DockStyle.Top, AutoSize = true, Text = "Proposal", Padding = new Padding(3, 3, 3, 2), Font = new Font(gridFont, FontStyle.Bold) };
            flowLeft = new FlowLayoutPanel { Name = "flowLeft", Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = SystemColors.Window, Padding = new Padding(2) };
            flowRight = new FlowLayoutPanel { Name = "flowRight", Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = SystemColors.Window, Padding = new Padding(2) };
            splitSides.Panel1.Controls.Add(flowLeft); splitSides.Panel1.Controls.Add(labelLeft);
            splitSides.Panel2.Controls.Add(flowRight); splitSides.Panel2.Controls.Add(labelRight);
            flowLeft.Resize += (s, e) => FitChipWidths(flowLeft);
            flowRight.Resize += (s, e) => FitChipWidths(flowRight);
            // double-buffer the chip columns and their panels: re-laying out dozens of labels flickered (user, 2026-10-02)
            foreach (var c in new Control[] { flowLeft, flowRight, splitSides.Panel1, splitSides.Panel2, panelRefine })
                typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(c, true);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
            buttonTakeLeft = new Button { Text = "Keep current", AutoSize = true, Enabled = false };
            buttonTakeRight = new Button { Text = "Take proposal", AutoSize = true, Enabled = false };
            buttonApplyChips = new Button { Text = "Apply chip choices", AutoSize = true, Enabled = false };
            buttonTakeLeft.Click += (s, e) => { if (currentDiff != null) AcceptCaption(string.Join(", ", currentDiff.Left.Select(i => i.Text)), "kept the current caption"); };
            buttonTakeRight.Click += (s, e) => { if (currentDiff != null) AcceptCaption(string.Join(", ", currentDiff.Right.Select(i => i.Text)), "took the proposal"); };
            buttonApplyChips.Click += (s, e) => { if (currentDiff != null) AcceptCaption(DazzleCaptionDiff.Compose(currentDiff, droppedRight, keptLeft), "applied the chip choices"); };
            buttons.Controls.AddRange(new Control[] { buttonTakeRight, buttonApplyChips, buttonTakeLeft });
            // a click on the chip area or the buttons hands the keyboard back to the dataset list
            foreach (var c in new Control[] { flowLeft, flowRight, buttons }) c.MouseUp += (s, e) => gridViewDS.Focus();

            // the instruction box over the rest, with a draggable splitter so it can be pulled taller (user, 2026-10-02); remembered
            textInstruction.Dock = DockStyle.Fill;
            // FixedPanel.Panel1: the instruction keeps its height and the chip area takes the rest when the pane resizes
            splitInstruction = new SplitContainer { Name = "splitInstruction", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 5, Panel1MinSize = gridFont.Height * 2, Panel2MinSize = 80, FixedPanel = FixedPanel.Panel1 };
            splitInstruction.Resize += (s, e) => { if (!instructionSplitPlaced && splitInstruction.Height >= 120) { instructionSplitPlaced = true; PlaceInstructionSplitter(); } };
            splitInstruction.Panel1.Controls.Add(textInstruction);
            splitInstruction.Panel2.Controls.Add(splitSides);
            splitInstruction.Panel2.Controls.Add(buttons);
            splitInstruction.Panel2.Controls.Add(labelRefineStatus);
            splitInstruction.SplitterMoved += (s, e) => { if (!placingInstructionSplit && panelRefine.Visible) Program.Settings.DazzleRefineInstructionHeight = splitInstruction.SplitterDistance; };
            panelRefine.Controls.Add(splitInstruction);
            panelRefine.Controls.Add(strip);
            // an edit to the caption box shows on the left side as it is typed: at once after a comma or a space, else after a pause
            if (textBoxTags != null)
            {
                textBoxTags.Leave += (s, e) => RenderProposalForCurrentImage();
                textBoxTags.TextChanged += (s, e) =>
                {
                    if (panelRefine == null || !panelRefine.Visible || !textBoxTags.Focused) return;
                    int at = textBoxTags.SelectionStart;
                    char last = at > 0 && at <= textBoxTags.TextLength ? textBoxTags.Text[at - 1] : '\0';
                    captionFollowTimer.Stop();
                    if (last == ',' || last == ' ' || last == '\n') RenderProposalForCurrentImage();
                    else captionFollowTimer.Start();
                };
            }
            splitMiddle.SplitterMoved += (s, e) => { if (!autoFittingMiddle && panelRefine.Visible) userMovedMiddle = true; };
        }

        private SplitContainer splitInstruction;
        private bool placingInstructionSplit, instructionSplitPlaced;
        private bool autoFittingMiddle, userMovedMiddle;

        /// <summary>
        /// Two phases (user, 2026-10-02). Before a proposal: the chip area is just tall enough for the current
        /// caption's chips and the text box below gets everything else, so there is room to type. After a run: the
        /// chip area grows to fit both sides as room permits, the text box keeping the height its lines need (four
        /// at least). Runs when the Refine pane renders; a drag by the user holds until the next image.
        /// </summary>
        private void AutoFitMiddle()
        {
            if (splitMiddle == null || panelRefine == null || !panelRefine.Visible || userMovedMiddle || splitMiddle.Height < 200 || textBoxTags == null) return;
            int chipsNeed = 0;
            foreach (var f in new[] { flowLeft, flowRight })
                chipsNeed = Math.Max(chipsNeed, f.GetPreferredSize(new Size(Math.Max(50, f.ClientSize.Width), 0)).Height);
            int chipRow = ChipFont().Height + 10;                       // one row of chips: text + padding + margin
            chipsNeed = Math.Max(chipsNeed + chipRow / 2, 2 * chipRow);   // slack for the panel's padding; never under two rows
            // everything in the top half except the chip columns: the Review | Refine strip, the Refine strip, the
            // instruction box, the status line, the buttons -- measured from the pane, not from panelRefine alone
            // (that left out the mode strip and collapsed the columns to half a row; user, 2026-10-02)
            int chrome = splitMiddle.Panel1.Height - splitSides.Height + Math.Max(labelLeft.Height, labelRight.Height);
            int topNeed = chrome + chipsNeed + 8;
            int lines = textBoxTags.GetLineFromCharIndex(Math.Max(0, textBoxTags.TextLength)) + 1;
            int captionNeed = tabsTags.ItemSize.Height + 16 + Math.Max(4, lines + 1) * textBoxTags.Font.Height;
            int available = splitMiddle.Height - splitMiddle.SplitterWidth;
            int target = currentDiff == null
                ? topNeed                                       // before: only what the caption's chips need
                : Math.Min(topNeed, available - captionNeed);   // after: grow for both sides, as room permits
            target = Math.Max(splitMiddle.Panel1MinSize, Math.Min(target, available - Math.Max(splitMiddle.Panel2MinSize, captionNeed)));
            if (Math.Abs(target - splitMiddle.SplitterDistance) < 8) return;
            autoFittingMiddle = true;
            try { splitMiddle.SplitterDistance = target; } catch (Exception) { }
            finally { autoFittingMiddle = false; }
        }

        private void PlaceInstructionSplitter()
        {
            if (splitInstruction == null || splitInstruction.Height < 120) return;
            int want = Program.Settings.DazzleRefineInstructionHeight > 0 ? Program.Settings.DazzleRefineInstructionHeight : textInstruction.Font.Height * 5 + 10;
            want = Math.Max(splitInstruction.Panel1MinSize, Math.Min(want, splitInstruction.Height - splitInstruction.Panel2MinSize - splitInstruction.SplitterWidth));
            placingInstructionSplit = true;
            try { splitInstruction.SplitterDistance = want; } catch (Exception) { }
            finally { placingInstructionSplit = false; }
        }

        private ToolTip blockTip;
        private System.Windows.Forms.Timer blockTipShow, blockTipHide;

        /// <summary>
        /// A tooltip for a strip item as a wrapped block (about 60 characters a line): appears after half a second,
        /// stays up to 15 seconds, and goes as soon as the cursor leaves the item. (A tip shown with a duration
        /// ignores Hide() until the duration ends -- user, 2026-10-02 -- so the duration is a timer of our own.)
        /// </summary>
        private void AttachBlockTip(ToolStrip strip, ToolStripItem item, string text)
        {
            blockTip ??= new ToolTip { ShowAlways = true, UseAnimation = true, UseFading = true };
            blockTipShow ??= new System.Windows.Forms.Timer { Interval = 500 };
            blockTipHide ??= new System.Windows.Forms.Timer { Interval = 15000 };
            blockTipHide.Tick += (s, e) => { blockTipHide.Stop(); blockTip.Hide(strip); };
            string wrapped = WrapText(text, 60);
            item.ToolTipText = null;
            item.MouseEnter += (s, e) =>
            {
                blockTipShow.Stop();
                if (pendingTipShow != null) blockTipShow.Tick -= pendingTipShow;
                pendingTipShow = (s2, e2) =>
                {
                    blockTipShow.Stop();
                    var r = item.Bounds;
                    blockTip.Show(wrapped, strip, r.Left, r.Bottom + 4);
                    blockTipHide.Stop(); blockTipHide.Start();
                };
                blockTipShow.Tick += pendingTipShow;
                blockTipShow.Start();
            };
            void hideNow(object s, EventArgs e)
            {
                blockTipShow.Stop();
                if (pendingTipShow != null) { blockTipShow.Tick -= pendingTipShow; pendingTipShow = null; }
                blockTipHide.Stop();
                blockTip.Hide(strip);
            }
            item.MouseLeave += hideNow;
            // a click means the person is done reading: the tip must not sit over a dropdown or a dialog (user, 2026-10-02)
            item.MouseDown += (s, e) => hideNow(s, e);
            if (item is ToolStripComboBox combo) combo.DropDown += hideNow;
        }
        private EventHandler pendingTipShow;

        private static string WrapText(string text, int width)
        {
            var sb = new StringBuilder(); int col = 0;
            foreach (var word in text.Split(' '))
            {
                if (col > 0 && col + 1 + word.Length > width) { sb.Append('\n'); col = 0; }
                else if (col > 0) { sb.Append(' '); col++; }
                sb.Append(word); col += word.Length;
            }
            return sb.ToString();
        }

        /// <summary>A green right-pointing triangle, sized for this screen's DPI.</summary>
        private Bitmap PlayGlyph()
        {
            int n = (int)Math.Round(16 * DeviceDpi / 96.0);
            var bmp = new Bitmap(n, n);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float m = n * 0.18f;
                using var brush = new SolidBrush(Color.FromArgb(34, 139, 34));
                g.FillPolygon(brush, new[] { new PointF(m, m * 0.8f), new PointF(n - m * 0.8f, n / 2f), new PointF(m, n - m * 0.8f) });
            }
            return bmp;
        }

        /// <summary>A question mark in a circle.</summary>
        private Bitmap HelpGlyph()
        {
            int n = (int)Math.Round(16 * DeviceDpi / 96.0);
            var bmp = new Bitmap(n, n);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float m = n * 0.08f;
                using var pen = new Pen(Color.FromArgb(40, 90, 160), Math.Max(1f, n / 10f));
                g.DrawEllipse(pen, m, m, n - 2 * m, n - 2 * m);
                using var font = new Font("Segoe UI", n * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Color.FromArgb(40, 90, 160));
                var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("?", font, brush, new RectangleF(0, 0, n, n), fmt);
            }
            return bmp;
        }

        private ToolStripButton buttonRefreshCaption;
        private System.Windows.Forms.Timer captionFollowTimer;

        /// <summary>A circular arrow.</summary>
        private Bitmap RefreshGlyph()
        {
            int n = (int)Math.Round(16 * DeviceDpi / 96.0);
            var bmp = new Bitmap(n, n);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float m = n * 0.18f, w = Math.Max(1.5f, n / 9f);
                var color = Color.FromArgb(40, 90, 160);
                using var pen = new Pen(color, w) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                g.DrawArc(pen, m, m, n - 2 * m, n - 2 * m, -60, 290);          // most of a circle, open at the top right
                using var brush = new SolidBrush(color);
                float cx = n / 2f, r = (n - 2 * m) / 2f;                      // arrowhead at the arc's end (angle -60 degrees)
                double a = -60 * Math.PI / 180;
                var tip = new PointF(cx + (float)(r * Math.Cos(a)), cx + (float)(r * Math.Sin(a)));
                float h = n * 0.26f;
                g.FillPolygon(brush, new[] { new PointF(tip.X - h * 0.1f, tip.Y - h * 0.9f), new PointF(tip.X + h * 0.7f, tip.Y + h * 0.1f), new PointF(tip.X - h * 0.6f, tip.Y + h * 0.35f) });
            }
            return bmp;
        }

        /// <summary>A red square.</summary>
        private Bitmap StopGlyph()
        {
            int n = (int)Math.Round(16 * DeviceDpi / 96.0);
            var bmp = new Bitmap(n, n);
            using (var g = Graphics.FromImage(bmp))
            {
                float m = n * 0.2f;
                using var brush = new SolidBrush(Color.FromArgb(200, 30, 30));
                g.FillRectangle(brush, m, m, n - 2 * m, n - 2 * m);
            }
            return bmp;
        }

        // ---------------------------------------------------------------- skills

        // skills are read through the data layers (DazzleData): the person's folder first, then the other tiers, then the house set beside the program
        private const string RefineKind = "refine";

        /// <summary>
        /// Refill the dropdown. The selection is put back without firing SkillsSelectionChanged, and the skill's text is
        /// loaded only when the selected skill is a different one: a refresh (entering the mode, Save as...) must not
        /// overwrite the instruction box. User, 2026-10-02: after Save as... the list jumped to the previously selected
        /// skill and loaded its text over the one just saved.
        /// </summary>
        /// <param name="select">the skill to select, e.g. the one just saved; otherwise the current one, then the setting</param>
        private void LoadSkillsList(string select = null)
        {
            try
            {
                Directory.CreateDirectory(DazzleData.HouseSkillsFolder(RefineKind));
                if (!DazzleData.SkillFiles(RefineKind, true).Any())
                    WriteDefaultSkills();
            }
            catch (Exception e) { labelRefineStatus.Text = "skills folder: " + e.Message; }
            string keep = select ?? lastSkillName ?? Program.Settings.DazzleRefineSkill;
            comboSkills.SelectedIndexChanged -= SkillsSelectionChanged;
            try
            {
                comboSkills.Items.Clear();
                try
                {
                    foreach (var s in DazzleData.SkillFiles(RefineKind, Program.Settings.DazzleShowHouseSkills))
                        comboSkills.Items.Add(s.Name);
                }
                catch (Exception) { }
                comboSkills.Items.Add(LoadFileEntry);
                comboSkills.Items.Add(OpenFolderEntry);
                int idx = string.IsNullOrEmpty(keep) ? -1 : comboSkills.Items.IndexOf(keep);
                if (idx < 0 && comboSkills.Items.Count > 2) idx = 0;
                if (idx >= 0) comboSkills.SelectedIndex = idx;
            }
            finally { comboSkills.SelectedIndexChanged += SkillsSelectionChanged; }
            string now = comboSkills.SelectedItem as string;
            if (now != null && !string.Equals(now, lastSkillName, StringComparison.OrdinalIgnoreCase)) LoadSelectedSkill();
        }

        // the two actions at the end of the dropdown (user, 2026-10-02: a way to load any text file, and to reach the folder for a real editor)
        private const string LoadFileEntry = "...  Load a file...";
        private const string OpenFolderEntry = "...  Open skills folder...";
        private string lastSkillName;

        private void LoadInstructionFromFile()
        {
            using var dlg = new OpenFileDialog { Title = "Load an instruction file", Filter = "Text and Markdown|*.txt;*.md|All files|*.*", InitialDirectory = DazzleData.SkillsWriteFolder(RefineKind) };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                textInstruction.Text = File.ReadAllText(dlg.FileName).Trim();
                loadedSkillText = "";   // not a saved skill: Save as... keeps it
                labelRefineStatus.Text = "loaded " + Path.GetFileName(dlg.FileName) + " (Save as... to keep it as a skill)";
            }
            catch (Exception e) { labelRefineStatus.Text = "could not read the file: " + e.Message; }
        }

        private void WriteDefaultSkills()
        {
            string house = DazzleData.HouseSkillsFolder(RefineKind);
            File.WriteAllText(Path.Combine(house, "Describe the subject.md"),
                "You caption images for a training dataset. Look at the image and describe the primary subject in one or two plain sentences, then list tags: what the subject is, its notable features (colour, clothing, expression, pose), the setting, the lighting and the style or medium. Keep every current tag that is still true; remove tags that are not true for this image; use the same style of tag names the current caption uses.\n");
            File.WriteAllText(Path.Combine(house, "Inventory the objects.md"),
                "You caption images for a training dataset. Make an inventory of everything visible in the image: every distinct object, creature, piece of clothing, prop and background element, one tag each, specific nouns (\"wooden crate\", not \"object\"). Keep the current tags that are still true and add what is missing. Do not describe mood or style unless the current caption already does.\n");
        }

        private void SkillsSelectionChanged(object sender, EventArgs e) => LoadSelectedSkill();

        private void LoadSelectedSkill()
        {
            string name = comboSkills.SelectedItem as string;
            if (name == null) return;
            if (name == LoadFileEntry || name == OpenFolderEntry)
            {
                // an action, not a skill: do it, then put the dropdown back on the skill it showed
                int back = lastSkillName != null ? comboSkills.Items.IndexOf(lastSkillName) : -1;
                comboSkills.SelectedIndexChanged -= SkillsSelectionChanged;
                try { comboSkills.SelectedIndex = back; } finally { comboSkills.SelectedIndexChanged += SkillsSelectionChanged; }
                if (name == LoadFileEntry) LoadInstructionFromFile();
                else { try { OpenWithShell(DazzleData.SkillsWriteFolder(RefineKind)); } catch (Exception e) { labelRefineStatus.Text = "could not open the folder: " + e.Message; } }
                return;
            }
            lastSkillName = name;
            Program.Settings.DazzleRefineSkill = name;
            var skill = DazzleData.FindSkill(RefineKind, name, Program.Settings.DazzleShowHouseSkills);
            try { loadedSkillText = skill != null ? File.ReadAllText(skill.Path).Trim() : ""; }
            catch (Exception e) { loadedSkillText = ""; labelRefineStatus.Text = "could not read the skill: " + e.Message; return; }
            textInstruction.Text = loadedSkillText;
            labelRefineStatus.Text = "skill: " + name + (skill != null ? " (" + skill.Source + ")" : "");
        }

        private void SaveSkillAs()
        {
            string name = Microsoft.VisualBasic.Interaction.InputBox("Name for this skill (becomes a file in the skills folder):", "Save skill", comboSkills.SelectedItem as string ?? "");
            if (string.IsNullOrWhiteSpace(name)) return;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            try
            {
                name = name.Trim();
                File.WriteAllText(Path.Combine(DazzleData.SkillsWriteFolder(RefineKind), name + ".md"), textInstruction.Text.TrimEnd() + "\n");
                loadedSkillText = textInstruction.Text.Trim();
                Program.Settings.DazzleRefineSkill = name;
                lastSkillName = name;          // the box already holds this skill's text: the refresh selects it without reloading
                LoadSkillsList(name);
                var saved = DazzleData.FindSkill(RefineKind, name, Program.Settings.DazzleShowHouseSkills);
                labelRefineStatus.Text = "saved skill: " + name + (saved != null ? " (" + saved.Source + ")" : "");
            }
            catch (Exception e) { labelRefineStatus.Text = "could not save the skill: " + e.Message; }
        }

        // ---------------------------------------------------------------- the run

        private async Task RunRefineAsync()
        {
            if (runCts != null) return;
            if (currentInfo == null || string.IsNullOrEmpty(currentInfo.Path) || textBoxTags == null || !textBoxTags.Enabled) { labelRefineStatus.Text = "select an image first"; return; }
            CommitTagsTextBox();
            string imagePath = currentInfo.Path;
            string leftCaption = textBoxTags.Text.Trim();
            string instruction = textInstruction.Text.Trim();
            if (instruction.Length == 0) { labelRefineStatus.Text = "write an instruction or pick a skill first"; return; }

            var settings = Program.Settings.OpenAiAutoTagger;
            var client = new DazzleLmStudio(settings.ConnectionAddress, settings.ApiKey, settings.RequestTimeout);
            runCts = new CancellationTokenSource(); runImagePath = imagePath;
            buttonPlay.Enabled = false; buttonStop.Enabled = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log("---- " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + Path.GetFileName(imagePath));
            try
            {
                labelRefineStatus.Text = "probing " + client.Endpoint + "...";
                var probe = await client.ProbeAsync(string.IsNullOrWhiteSpace(settings.Model) ? null : settings.Model.Trim(), runCts.Token);
                Log("probe: " + probe.Reason + (probe.Warning != null ? "\nWARNING: " + probe.Warning : ""));
                if (!probe.Reachable) { labelRefineStatus.Text = probe.Reason; return; }
                if (probe.Warning != null) labelRefineStatus.Text = "warning: " + probe.Warning;

                var (bytes, mime, note) = DazzleLmStudio.PrepareImage(imagePath, Program.Settings.DazzleRefineImageLongSide);
                // Only the skill and the caption go with the image. The folder's rules and the Check for list are no longer
                // sent automatically (user, 2026-10-02: the model read the Check for list as tags to add); sending them
                // becomes the person's choice on Settings > AI, or a placeholder the skill places itself.
                var user = new StringBuilder();
                user.Append("Current caption:\n").Append(leftCaption.Length > 0 ? leftCaption : "(none)");
                var req = new DazzleLmStudio.Request
                {
                    Model = probe.Model, SystemPrompt = instruction + OutputContract, UserText = user.ToString(),
                    ImageBytes = bytes, ImageMime = mime, Think = checkThink.Checked, UseSchema = checkSchema.Checked,
                    MaxTokens = Program.Settings.DazzleRefineMaxTokens, Temperature = Program.Settings.DazzleRefineTemperature,
                };
                Log("image: " + note + "  (" + bytes.Length / 1024 + " KB)\nsystem:\n" + req.SystemPrompt + "\nuser:\n" + req.UserText);
                int reasoningChars = 0, contentChars = 0; bool finished = false;
                var reasoning = new StringBuilder();
                var progress = new Progress<DazzleLmStudio.Delta>(d =>
                {
                    if (d.Kind == DazzleLmStudio.DeltaKind.Reasoning) { reasoningChars += d.Text.Length; reasoning.Append(d.Text); }
                    else if (d.Kind == DazzleLmStudio.DeltaKind.Content) contentChars += d.Text.Length;
                    else Log("status: " + d.Text);
                    if (finished) return;   // Progress<T> posts to the UI queue: a late tick must not overwrite the result line
                    labelRefineStatus.Text = probe.Model + "  " + sw.Elapsed.TotalSeconds.ToString("F0") + " s  thinking " + reasoningChars + "  reply " + contentChars;
                });
                var result = await client.RunAsync(req, progress, runCts.Token);
                finished = true;
                if (result.Ok) result.Content = CleanProposal(result.Content);
                if (reasoning.Length > 0) Log("reasoning:\n" + reasoning);
                Log("reply (" + result.Elapsed.TotalSeconds.ToString("F1") + " s, schema " + (result.SchemaUsed ? "on" : "off") + "):\n" + (result.Ok ? result.Content : "ERROR " + result.Error));
                if (!result.Ok) { labelRefineStatus.Text = result.Cancelled ? "stopped" : result.Error; return; }
                proposals[imagePath] = (leftCaption, result.Content, result);
                if (!string.Equals(currentInfo?.Path, imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    Log("the result is kept for " + Path.GetFileName(imagePath) + "; current is " + (currentInfo?.Path ?? "(none)"));
                    labelRefineStatus.Text = "done: " + Path.GetFileName(imagePath) + " (go back to it to see the proposal; current: " + (currentInfo == null ? "none" : Path.GetFileName(currentInfo.Path)) + ")";
                    return;
                }
                // after the queued progress ticks have drained, so the result line is the last word
                var l = leftCaption; var r = result;
                BeginInvoke(new Action(() => { if (string.Equals(currentInfo?.Path, imagePath, StringComparison.OrdinalIgnoreCase)) { lastRenderKey = null; RenderProposalForCurrentImage(); } }));
            }
            catch (Exception e) { Log("ERROR " + e.Message); labelRefineStatus.Text = e.Message; }
            finally
            {
                runCts.Dispose(); runCts = null; runImagePath = null;
                buttonPlay.Enabled = true; buttonStop.Enabled = false;
            }
        }

        // ---------------------------------------------------------------- the two sides

        /// <summary>
        /// Called when the current image changes, when the caption box is left, and after an accept: the left side
        /// is always the caption as it stands now; the right side is the stored proposal for this image, if any.
        /// </summary>
        private void RenderProposalForCurrentImage()
        {
            if (panelRefine == null || !panelRefine.Visible) return;
            string caption = textBoxTags != null && textBoxTags.Enabled ? textBoxTags.Text.Trim() : "";
            if (!string.Equals(lastFittedPath, currentInfo?.Path, StringComparison.OrdinalIgnoreCase)) { userMovedMiddle = false; lastFittedPath = currentInfo?.Path; }
            (string left, string right, DazzleLmStudio.RunResult result) p = default;
            bool have = currentInfo?.Path != null && proposals.TryGetValue(currentInfo.Path, out p);
            // nothing changed since the last render (the caption box was merely left for the strip): leave the chips alone
            string key = (currentInfo?.Path ?? "") + "\n" + caption + "\n" + (have ? p.right : "");
            if (key == lastRenderKey) return;
            lastRenderKey = key;
            panelRefine.SuspendLayout();
            try
            {
                if (have) RenderProposal(caption, p.right, p.result);
                else ShowCaptionOnly(caption);
            }
            finally { panelRefine.ResumeLayout(true); }
            BeginInvoke(new Action(AutoFitMiddle));   // after the flow panels have laid out their chips
        }
        private string lastFittedPath, lastRenderKey;

        /// <summary>No proposal yet: the current caption as plain chips on the left, the right side empty.</summary>
        private void ShowCaptionOnly(string caption)
        {
            currentDiff = null; droppedRight.Clear(); keptLeft.Clear();
            flowLeft.SuspendLayout();
            flowLeft.Controls.Clear(); flowRight.Controls.Clear();
            var items = DazzleCaptionDiff.SplitItems(caption);
            for (int i = 0; i < items.Count; i++)
                flowLeft.Controls.Add(MakeChip(new DazzleCaptionDiff.Item { Text = items[i], Kind = DazzleCaptionDiff.Kind.Same }, i, false));
            FitChipWidths(flowLeft);
            flowLeft.ResumeLayout();
            buttonTakeLeft.Enabled = buttonTakeRight.Enabled = buttonApplyChips.Enabled = false;
            labelLeft.Text = "Current caption  (" + items.Count + ")"; labelRight.Text = "Proposal  (press Play)";
            if (runCts == null) labelRefineStatus.Text = currentInfo == null ? "select an image, then Play" : "no proposal for this image yet -- Play";
        }

        private void RenderProposal(string left, string right, DazzleLmStudio.RunResult result)
        {
            var mode = Program.Settings.DazzleTagMatch;
            currentDiff = DazzleCaptionDiff.Compare(left, right, mode);
            droppedRight.Clear(); keptLeft.Clear();
            flowLeft.SuspendLayout(); flowRight.SuspendLayout();
            flowLeft.Controls.Clear(); flowRight.Controls.Clear();
            for (int i = 0; i < currentDiff.Left.Count; i++) flowLeft.Controls.Add(MakeChip(currentDiff.Left[i], i, false));
            for (int i = 0; i < currentDiff.Right.Count; i++) flowRight.Controls.Add(MakeChip(currentDiff.Right[i], i, true));
            FitChipWidths(flowLeft); FitChipWidths(flowRight);
            flowLeft.ResumeLayout(); flowRight.ResumeLayout();
            labelLeft.Text = "Current caption  (" + currentDiff.Left.Count + ")";
            labelRight.Text = "Proposal  (" + currentDiff.Right.Count + ")  " + currentDiff.Summary;
            buttonTakeLeft.Enabled = buttonTakeRight.Enabled = buttonApplyChips.Enabled = true;
            // the proposal is checked by the same rules engine the caption is, before anyone accepts it
            string rulesNote = "";
            if (folderRules.Count > 0)
            {
                var ev = DazzleRules.Evaluate(folderRules, new DazzleRules.TagSet(currentDiff.Right.Select(i => i.Text), mode));
                var problems = ev.Rules.Where(rr => rr.Active && (rr.HasProblem || rr.HasConflict)).Select(rr => DazzleRules.Describe(rr)).ToList();
                rulesNote = problems.Count == 0 ? "; rules: ok" : "; rules: " + string.Join("; ", problems);
            }
            labelRefineStatus.Text = currentDiff.Summary + " in " + result.Elapsed.TotalSeconds.ToString("F1") + " s" + (result.Reasoning.Length > 0 ? " (thought " + result.Reasoning.Length + " chars)" : "") + rulesNote + ". Click a chip to drop or keep it, then Apply.";
        }

        /// <summary>A chip may be as wide as its column; wider text wraps at spaces. Called when the column is laid out or resized.</summary>
        private static void FitChipWidths(FlowLayoutPanel flow)
        {
            int width = flow.ClientSize.Width - 12;
            if (width < 60) return;   // not laid out yet
            flow.SuspendLayout();
            foreach (Control c in flow.Controls)
                if (c.MaximumSize.Width != width) c.MaximumSize = new Size(width, 0);
            flow.ResumeLayout();
        }

        private Font chipFont;

        /// <summary>A size and a half under the grid font, tight padding: a real caption can run to hundreds of items (user, 2026-10-02).</summary>
        private Font ChipFont()
        {
            var g = Program.Settings.GridViewFont.GetFont();
            float size = Math.Max(6f, g.Size - 1.5f);
            if (chipFont == null || Math.Abs(chipFont.Size - size) > 0.01f || chipFont.FontFamily.Name != g.FontFamily.Name)
                chipFont = new Font(g.FontFamily, size, FontStyle.Regular);
            return chipFont;
        }

        private Control MakeChip(DazzleCaptionDiff.Item item, int index, bool rightSide)
        {
            var chip = new Label
            {
                AutoSize = true, Margin = new Padding(2, 1, 2, 1), Padding = new Padding(4, 1, 4, 1), BorderStyle = BorderStyle.FixedSingle,
                Text = item.Text, Tag = index, ForeColor = MarkFore, Font = ChipFont(),
            };
            // the wrap width is set by FitChipWidths from the column's real width, not here: at creation the column
            // may not be laid out yet, and a cap taken then wrapped "sherlock-holmes" mid-word (user, 2026-10-02)
            chip.BackColor = item.Kind switch
            {
                DazzleCaptionDiff.Kind.Added => GoodBack,
                DazzleCaptionDiff.Kind.Removed => BadBack,
                DazzleCaptionDiff.Kind.Changed => ConflictBack,
                _ => DormantBack,
            };
            string tip = item.Kind.ToString();
            if (item.Words != null)
                tip += ": " + string.Join(" ", item.Words.Select(w => w.Kind == DazzleCaptionDiff.Kind.Same ? w.Word : (w.Kind == DazzleCaptionDiff.Kind.Added ? "[+" : "[-") + w.Word + "]"));
            bool toggleable = rightSide ? item.Kind != DazzleCaptionDiff.Kind.Same || true : item.Kind == DazzleCaptionDiff.Kind.Removed;
            if (toggleable)
            {
                chip.Cursor = Cursors.Hand;
                tip += rightSide ? "  (click to drop this item from the result)" : "  (click to keep this item in the result)";
                chip.Click += (s, e) =>
                {
                    if (rightSide) { if (!droppedRight.Remove(index)) droppedRight.Add(index); chip.Font = new Font(ChipFont(), droppedRight.Contains(index) ? FontStyle.Strikeout : FontStyle.Regular); }
                    else { if (!keptLeft.Remove(index)) keptLeft.Add(index); chip.Font = new Font(ChipFont(), keptLeft.Contains(index) ? FontStyle.Bold : FontStyle.Regular); }
                    gridViewDS.Focus();
                };
            }
            new ToolTip().SetToolTip(chip, tip);
            return chip;
        }

        private void AcceptCaption(string caption, string what)
        {
            if (textBoxTags == null || !textBoxTags.Enabled) return;
            textBoxTags.Text = caption;
            CommitTagsTextBox();
            lastRenderKey = null;
            RenderProposalForCurrentImage();   // the diff is now against the accepted text, so it reads "no change proposed"
            labelRefineStatus.Text = what + " -- applied to the caption (Ctrl+S to save the file)";
            Log(what + ": " + caption);
            gridViewDS.Focus();
        }

        // ---------------------------------------------------------------- log

        private void Log(string line)
        {
            refineLog.Append(line).Append('\n');
            if (refineLog.Length > 400000) refineLog.Remove(0, refineLog.Length - 300000);
            if (logBox != null && !logBox.IsDisposed) { logBox.AppendText(line + "\r\n"); }
        }

        private void ShowLog()
        {
            if (logForm == null || logForm.IsDisposed)
            {
                logForm = new Form { Text = "Refine -- logs", Width = 820, Height = 600, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false };
                logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = true, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9f), Text = refineLog.ToString().Replace("\n", "\r\n") };
                var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
                strip.Items.Add(new ToolStripButton("Copy all", null, (s, e) => { if (logBox.TextLength > 0) Clipboard.SetText(logBox.Text); }));
                strip.Items.Add(new ToolStripButton("Clear", null, (s, e) => { refineLog.Clear(); logBox.Clear(); }));
                strip.Items.Add(new ToolStripLabel("server: " + DazzleLmStudio.NormalizeEndpoint(Program.Settings.OpenAiAutoTagger.ConnectionAddress) + "  (Settings > AutoTagger > OpenAI for the address, key, model and timeout)"));
                logForm.Controls.Add(logBox); logForm.Controls.Add(strip);
                logForm.Show(this);
            }
            else logForm.Activate();
            logBox.SelectionStart = logBox.TextLength; logBox.ScrollToCaret();
        }
    }
}
