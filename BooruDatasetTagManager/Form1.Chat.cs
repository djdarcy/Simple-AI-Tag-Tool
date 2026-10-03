using Newtonsoft.Json.Linq;
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
    // Simple-AI-Tag-Tool: Chat mode -- the third mode of the middle pane. Top half: the skill strip, the system
    // instruction, and a Result panel (file name, folder, caption, the last change and its Undo, a context meter).
    // Bottom half, where the caption Text/Grid box sits in the other modes: the transcript and an input box.
    // The model acts on the current image through five tools; every act is journaled (Form1.DatasetOps.cs).
    // Design: 2026-10-02__06-20-24__dev-workflow-process__satt-chat-mode-tool-calling.md (units 3-5)
    public partial class MainForm
    {
        private Panel panelChat, panelChatBottom;
        private ToolStripComboBox comboChatSkills;
        private ToolStripButton chatThink, chatAskFiles, chatToolsOn, chatLogs, chatNewSession, chatSaveSkill;
        private TextBox textChatInstruction;
        private SplitContainer splitChatInstruction;
        private Label labelResultFile, labelResultFolder, labelResultCaption, labelResultChange, labelContext;
        private Button buttonUndoLast;
        private RichTextBox transcript;
        private TextBox textChatInput;
        private Button buttonSend, buttonChatStop;
        private JArray chatHistory;
        private string chatImageSentFor;        // the image path the model last saw (image bytes sent)
        private string chatSystemText;
        private CancellationTokenSource chatCts;
        private int chatContextLength = 32000, chatLastTotalTokens;
        private bool chatInstructionSplitPlaced;

        private static readonly Color UserColor = Color.FromArgb(20, 60, 160), AssistantColor = Color.FromArgb(20, 110, 50), ToolColor = Color.FromArgb(110, 110, 110), ErrorColor = Color.FromArgb(180, 30, 30);

        private const string ToolParagraph = "\n\nYou are inside Simple-AI-Tag-Tool, a caption editor for image training datasets. You act on the CURRENT image only, through these tools: get_image (its name, folder, caption, the latest AI proposal, and its position in the dataset), set_caption, rename_image, move_image, list_images. When the user asks for a change, make it with the tool and then confirm in one short line; do not describe a change instead of making it, and do not change anything the user did not ask for. The caption is a comma-separated tag line unless told otherwise. File names: no extension, no folder.";

        // ---------------------------------------------------------------- layout

        private void BuildChatPane()
        {
            var gridFont = Program.Settings.GridViewFont.GetFont();
            panelChat = new Panel { Dock = DockStyle.Fill, Name = "panelChat", Visible = false };
            var strip = new ToolStrip { Name = "chatStrip", GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, ShowItemToolTips = false };
            comboChatSkills = new ToolStripComboBox("comboChatSkills") { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 190, ToolTipText = "Chat skills: the system instruction, one file each in skills\\chat (yours in the data folder, the shipped ones beside the program). Placeholders {caption} {refined} {file} {folder} {rules} {checks} are filled in when a session starts." };
            comboChatSkills.SelectedIndexChanged += ChatSkillsSelectionChanged;
            chatSaveSkill = new ToolStripButton("Save as...") { ToolTipText = "Save the instruction text as a new chat skill file" };
            chatSaveSkill.Click += (s, e) => SaveChatSkillAs();
            chatNewSession = new ToolStripButton("New session") { ToolTipText = "Start the conversation again with the instruction as it is now; the journal of changes is kept" };
            chatNewSession.Click += (s, e) => { StartChatSession(); AppendTranscript("new session", ToolColor, true); };
            chatThink = new ToolStripButton("Think") { CheckOnClick = true, Checked = Program.Settings.DazzleRefineThink, ToolTipText = "Think: let the model reason before it answers or calls a tool. The reasoning is shown folded in the transcript and in full in Logs." };
            chatAskFiles = new ToolStripButton("Ask before file changes") { CheckOnClick = true, Checked = Program.Settings.DazzleChatAskFiles, ToolTipText = "Ask before file changes: a rename or move waits for your Yes in a prompt. Off, it happens at once and Undo reverses it." };
            chatAskFiles.CheckedChanged += (s, e) => Program.Settings.DazzleChatAskFiles = chatAskFiles.Checked;
            chatToolsOn = new ToolStripButton("Tools") { CheckOnClick = true, Checked = Program.Settings.DazzleChatTools, ToolTipText = "Tools: let the model act (set the caption, rename, move). Off, it can only talk about the image." };
            chatToolsOn.CheckedChanged += (s, e) => Program.Settings.DazzleChatTools = chatToolsOn.Checked;
            chatLogs = new ToolStripButton("Logs") { ToolTipText = "Logs: every request, the model's reasoning, each tool call and result, and errors" };
            chatLogs.Click += (s, e) => ShowLog();
            var help = new ToolStripButton { Name = "buttonChatHelp", Text = "Help", Image = HelpGlyph(), DisplayStyle = ToolStripItemDisplayStyle.Image, ImageScaling = ToolStripItemImageScaling.None, Alignment = ToolStripItemAlignment.Right, ToolTipText = "Help: the Chat section of the user guide" };
            help.Click += (s, e) => OpenUrl(GuideUrl + "#ai-chat-an-assistant-that-acts");
            var chatExport = BuildExportImportButtons(out var chatImport);   // LM Studio (Form1.Conversations.cs)
            strip.Items.AddRange(new ToolStripItem[] { new ToolStripLabel("AI skill:"), comboChatSkills, chatSaveSkill, new ToolStripSeparator(), chatNewSession, new ToolStripSeparator(), chatThink, chatToolsOn, chatAskFiles, new ToolStripSeparator(), chatExport, chatImport, chatLogs, help });
            foreach (ToolStripItem it in strip.Items) if (!string.IsNullOrEmpty(it.ToolTipText)) AttachBlockTip(strip, it, it.ToolTipText);

            textChatInstruction = new TextBox { Name = "textChatInstruction", Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Font = gridFont };

            // the Result panel: what the tools can change, as it stands, and the last change with its Undo
            var result = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(4, 2, 4, 2) };
            result.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); result.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Label Key(string t) => new Label { Text = t, AutoSize = true, Font = new Font(gridFont, FontStyle.Bold), Margin = new Padding(2, 4, 8, 2) };
            Label Val(string name) => new Label { Name = name, AutoSize = true, Font = gridFont, Margin = new Padding(2, 4, 2, 2), MaximumSize = new Size(900, 0) };
            labelResultFile = Val("labelResultFile"); labelResultFolder = Val("labelResultFolder"); labelResultCaption = Val("labelResultCaption"); labelResultChange = Val("labelResultChange");
            labelResultChange.ForeColor = ToolColor;
            buttonUndoLast = new Button { Name = "buttonUndoLast", Text = "Undo last change", AutoSize = true, Enabled = false, Margin = new Padding(2) };
            buttonUndoLast.Click += (s, e) => UndoLastChange();
            labelContext = new Label { Name = "labelContext", AutoSize = true, ForeColor = ToolColor, Margin = new Padding(2, 6, 2, 2) };
            result.Controls.Add(Key("File"), 0, 0); result.Controls.Add(labelResultFile, 1, 0);
            result.Controls.Add(Key("Folder"), 0, 1); result.Controls.Add(labelResultFolder, 1, 1);
            result.Controls.Add(Key("Caption"), 0, 2); result.Controls.Add(labelResultCaption, 1, 2);
            result.Controls.Add(Key("Last change"), 0, 3); result.Controls.Add(labelResultChange, 1, 3);
            result.Controls.Add(buttonUndoLast, 1, 4);
            result.Controls.Add(labelContext, 1, 5);
            result.Resize += (s, e) => { int w = Math.Max(200, result.ClientSize.Width - 120); foreach (var l in new[] { labelResultFile, labelResultFolder, labelResultCaption, labelResultChange }) l.MaximumSize = new Size(w, 0); };

            splitChatInstruction = new SplitContainer { Name = "splitChatInstruction", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 5, Panel1MinSize = gridFont.Height * 2, Panel2MinSize = 60, FixedPanel = FixedPanel.Panel1 };
            splitChatInstruction.Panel1.Controls.Add(textChatInstruction);
            splitChatInstruction.Panel2.Controls.Add(result);
            splitChatInstruction.Resize += (s, e) => { if (!chatInstructionSplitPlaced && splitChatInstruction.Height >= 100) { chatInstructionSplitPlaced = true; int want = Program.Settings.DazzleChatInstructionHeight > 0 ? Program.Settings.DazzleChatInstructionHeight : gridFont.Height * 4 + 10; try { splitChatInstruction.SplitterDistance = Math.Max(splitChatInstruction.Panel1MinSize, Math.Min(want, splitChatInstruction.Height - splitChatInstruction.Panel2MinSize - 5)); } catch (Exception) { } } };
            splitChatInstruction.SplitterMoved += (s, e) => { if (chatInstructionSplitPlaced && panelChat.Visible) Program.Settings.DazzleChatInstructionHeight = splitChatInstruction.SplitterDistance; };
            panelChat.Controls.Add(splitChatInstruction);
            panelChat.Controls.Add(strip);
            foreach (var c in new Control[] { panelChat, result }) typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(c, true);

            // the bottom half: transcript over an input line
            panelChatBottom = new Panel { Dock = DockStyle.Fill, Name = "panelChatBottom", Visible = false };
            transcript = new RichTextBox { Name = "transcript", Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Window, Font = gridFont, DetectUrls = false, HideSelection = false };
            transcript.GotFocus += (s, e) => BeginInvoke(new Action(() => textChatInput.Focus()));
            var inputRow = new Panel { Dock = DockStyle.Bottom, Height = gridFont.Height * 2 + 14, Padding = new Padding(2) };
            textChatInput = new TextBox { Name = "textChatInput", Multiline = true, Dock = DockStyle.Fill, Font = gridFont, AcceptsReturn = false };
            textChatInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; _ = SendChatAsync(); } };
            buttonSend = new Button { Name = "buttonSend", Text = "Send", Dock = DockStyle.Right, Width = 70 };
            buttonSend.Click += async (s, e) => await SendChatAsync();
            buttonChatStop = new Button { Name = "buttonChatStop", Text = "Stop", Dock = DockStyle.Right, Width = 60, Enabled = false };
            buttonChatStop.Click += (s, e) => chatCts?.Cancel();
            inputRow.Controls.Add(textChatInput); inputRow.Controls.Add(buttonSend); inputRow.Controls.Add(buttonChatStop);
            panelChatBottom.Controls.Add(transcript);
            panelChatBottom.Controls.Add(inputRow);
            splitMiddle.Panel2.Controls.Add(panelChatBottom);
        }

        private void ShowChatMode(bool on)
        {
            panelChat.Visible = on;
            panelChatBottom.Visible = on;
            tabsTags.Visible = !on;
            if (on)
            {
                LoadChatSkillsList();
                if (chatHistory == null) StartChatSession();
                RefreshChatResultPanel();
                BeginInvoke(new Action(() => textChatInput.Focus()));
            }
        }

        // ---------------------------------------------------------------- skills (chat)

        private const string ChatKind = "chat";
        private string loadedChatSkillText = "";

        private void ChatSkillsSelectionChanged(object sender, EventArgs e) => LoadSelectedChatSkill();

        /// <summary>Refill the chat dropdown without firing the selection handler; load text only when the skill changed (as LoadSkillsList).</summary>
        /// <param name="select">the skill to select, e.g. the one just saved</param>
        private void LoadChatSkillsList(string select = null)
        {
            try
            {
                string house = DazzleData.HouseSkillsFolder(ChatKind);
                // with the shipped skills hidden, none is written anywhere (#6)
                if (Program.Settings.DazzleShowHouseSkills && !DazzleData.SkillFiles(ChatKind, true).Any())
                {
                    Directory.CreateDirectory(house);
                    File.WriteAllText(Path.Combine(house, "Assistant.md"),
                        "You are a careful assistant for a person refining an image dataset. You can see the current image. Answer questions about it plainly, and when asked to change its caption, name or folder, do it with the tools and confirm in one line.\n");
                    File.WriteAllText(Path.Combine(house, "Name from template.md"),
                        "Rename the current image from its caption using this template, parts joined with double underscores, words inside a part joined with hyphens, all lower-case, no spaces:\n\n<subject>__<scene>__<objects>\n\nsubject = the main thing in the image (one or two words); scene = where it is; objects = up to three notable objects or features. Use the current caption and the latest AI proposal (get_image) as your source; look at the image only to settle what the caption leaves unclear. If a part cannot be found, ask instead of guessing. Current caption: {caption}\nLatest proposal: {refined}\n");
                }
            }
            catch (Exception e) { AppendTranscript("skills folder: " + e.Message, ErrorColor, true); }
            string startWith = Program.Settings.DazzleChatDefaultSkill.Length > 0 ? Program.Settings.DazzleChatDefaultSkill : Program.Settings.DazzleChatSkill;
            string keep = select ?? loadedChatSkillName ?? startWith;
            comboChatSkills.SelectedIndexChanged -= ChatSkillsSelectionChanged;
            try
            {
                comboChatSkills.Items.Clear();
                try
                {
                    foreach (var s in DazzleData.SkillFiles(ChatKind, Program.Settings.DazzleShowHouseSkills))
                        comboChatSkills.Items.Add(s.Name);
                }
                catch (Exception) { }
                if (comboChatSkills.Items.Count == 0) return;
                int idx = string.IsNullOrEmpty(keep) ? -1 : comboChatSkills.Items.IndexOf(keep);
                comboChatSkills.SelectedIndex = idx >= 0 ? idx : 0;
            }
            finally { comboChatSkills.SelectedIndexChanged += ChatSkillsSelectionChanged; }
            string now = comboChatSkills.SelectedItem as string;
            if (now != null && !string.Equals(now, loadedChatSkillName, StringComparison.OrdinalIgnoreCase)) LoadSelectedChatSkill();
        }

        private void LoadSelectedChatSkill()
        {
            string name = comboChatSkills.SelectedItem as string;
            if (name == null) return;
            bool changed = !string.Equals(name, loadedChatSkillName, StringComparison.OrdinalIgnoreCase);
            loadedChatSkillName = name;
            Program.Settings.DazzleChatSkill = name;
            var skill = DazzleData.FindSkill(ChatKind, name, Program.Settings.DazzleShowHouseSkills);
            try { loadedChatSkillText = skill != null ? File.ReadAllText(skill.Path).Trim() : ""; } catch (Exception) { loadedChatSkillText = ""; }
            textChatInstruction.Text = loadedChatSkillText;
            // a list refresh re-selects the same skill; only a real change is worth a line (user's session, 2026-10-02)
            if (changed && chatHistory != null && chatHistory.Count > 1) AppendTranscript("skill changed to '" + name + "' -- takes effect at the next New session", ToolColor, true);
        }
        private string loadedChatSkillName;

        private void SaveChatSkillAs()
        {
            string name = Microsoft.VisualBasic.Interaction.InputBox("Name for this chat skill (a file in skills\\chat):", "Save chat skill", comboChatSkills.SelectedItem as string ?? "");
            if (string.IsNullOrWhiteSpace(name)) return;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            try
            {
                name = name.Trim();
                File.WriteAllText(Path.Combine(DazzleData.SkillsWriteFolder(ChatKind), name + ".md"), textChatInstruction.Text.TrimEnd() + "\n");
                Program.Settings.DazzleChatSkill = name;
                loadedChatSkillName = name;    // the box already holds this skill's text: the refresh selects it without reloading
                loadedChatSkillText = textChatInstruction.Text.Trim();
                LoadChatSkillsList(name);
            }
            catch (Exception e) { AppendTranscript("could not save the skill: " + e.Message, ErrorColor, true); }
        }

        /// <summary>{caption} {refined} {file} {folder} {rules} {checks}, filled from the current image and folder.</summary>
        private string ExpandPlaceholders(string text)
        {
            string caption = textBoxTags != null && textBoxTags.Enabled ? textBoxTags.Text.Trim() : "";
            string refined = currentInfo?.Path != null && proposals.TryGetValue(currentInfo.Path, out var p) ? p.right : "";
            string rules = string.Join("\n", folderRules.Where(r => r.Error == null && !r.IsComment && !r.IsEmpty).Select(r => r.Line));
            return text.Replace("{caption}", caption.Length > 0 ? caption : "(none)")
                       .Replace("{refined}", refined.Length > 0 ? refined : "(none yet)")
                       .Replace("{file}", currentInfo != null ? Path.GetFileName(currentInfo.Path) : "(none)")
                       .Replace("{folder}", currentInfo != null ? Path.GetDirectoryName(currentInfo.Path) : "(none)")
                       .Replace("{rules}", rules.Length > 0 ? rules : "(none)")
                       .Replace("{checks}", textBoxCheck?.Text?.Trim().Length > 0 ? textBoxCheck.Text.Trim().Replace("\n", ", ") : "(none)");
        }

        // ---------------------------------------------------------------- session

        private void StartChatSession()
        {
            string context = ChatContextBlock();   // only what the person chose (Settings > AI, or the strip's Context)
            chatSystemText = ExpandPlaceholders(textChatInstruction.Text.Trim()) + (context.Length > 0 ? "\n\n" + context : "") + ToolParagraph;
            chatHistory = new JArray(new JObject { ["role"] = "system", ["content"] = chatSystemText });
            chatImageSentFor = null; chatLastTotalTokens = 0;
            if (currentInfo?.Path != null && !string.Equals(currentInfo.Path, chatSlotPath, StringComparison.OrdinalIgnoreCase)) { chatSlotPath = currentInfo.Path; chatSlotRoot = dazzleDatasetFolder; }   // a session belongs to the image it starts on (#5)
            foreach (var m in RefineOpening(chatSlotPath)) chatHistory.Add(m);   // continue from the image's Refine run
            RenderTranscript();
            UpdateContextMeter();
        }

        private JArray ChatTools()
        {
            JObject Obj(string props = null) => JObject.Parse("{\"type\":\"object\",\"properties\":{" + (props ?? "") + "},\"required\":[" + (props == null ? "" : "\"" + props.Substring(1, props.IndexOf('"', 1) - 1) + "\"") + "],\"additionalProperties\":false}");
            return new JArray(
                DazzleLmStudio.Tool("get_image", "The current image's facts: file name, folder, caption as it stands, the latest AI proposal from Refine, and its position in the dataset.", Obj()),
                DazzleLmStudio.Tool("set_caption", "Replace the current image's caption. The text goes into the caption box as if typed; it is saved to the file when the user saves.", Obj("\"caption\":{\"type\":\"string\",\"description\":\"the complete new caption, normally a comma-separated tag line\"}")),
                DazzleLmStudio.Tool("rename_image", "Rename the current image file; its caption file is renamed with it. Give the new base name only: no extension, no folder.", Obj("\"new_name\":{\"type\":\"string\",\"description\":\"the new base name without extension\"}")),
                DazzleLmStudio.Tool("move_image", "Move the current image and its caption file to a folder inside the dataset folder, given relative to it (for example _rejects or sub/portraits). The folder is created if needed.", Obj("\"folder\":{\"type\":\"string\",\"description\":\"target folder, relative to the dataset folder\"}")),
                DazzleLmStudio.Tool("list_images", "The dataset's images: file names with their captions, up to 200. An optional filter keeps only captions containing the text.", JObject.Parse("{\"type\":\"object\",\"properties\":{\"filter\":{\"type\":\"string\"}},\"additionalProperties\":false}")));
        }

        private async Task SendChatAsync()
        {
            if (chatCts != null) return;
            string text = textChatInput.Text.Trim();
            if (text.Length == 0) return;
            if (currentInfo == null || string.IsNullOrEmpty(currentInfo.Path)) { AppendTranscript("select an image first", ErrorColor, true); return; }
            if (chatHistory == null) StartChatSession();
            CommitTagsTextBox();
            var settings = Program.Settings.OpenAiAutoTagger;
            var client = new DazzleLmStudio(settings.ConnectionAddress, settings.ApiKey, settings.RequestTimeout);
            chatCts = new CancellationTokenSource();
            buttonSend.Enabled = false; buttonChatStop.Enabled = true; textChatInput.Clear();
            AppendTranscript("You: " + text, UserColor, false);
            try
            {
                var probe = await client.ProbeAsync(string.IsNullOrWhiteSpace(settings.Model) ? null : settings.Model.Trim(), chatCts.Token);
                Log("chat probe: " + probe.Reason);
                if (!probe.Reachable) { AppendTranscript(probe.Reason, ErrorColor, true); return; }
                if (probe.ContextLength is int cl) chatContextLength = cl;
                client.DefaultModel = probe.Model;

                // the image and its facts travel once, and again when the current image changes
                string imagePath = currentInfo.Path;
                var sb = new StringBuilder();
                if (!string.Equals(chatImageSentFor, imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(chatImageSentFor == null ? "Current image: " : "The current image is now: ").Append(Path.GetFileName(imagePath))
                      .Append("  (folder: ").Append(Path.GetDirectoryName(imagePath)).Append(")\nCaption: ").Append(textBoxTags.Text.Trim().Length > 0 ? textBoxTags.Text.Trim() : "(none)").Append("\n\n");
                }
                sb.Append(text);
                byte[] bytes = null; string mime = "image/jpeg";
                if (!string.Equals(chatImageSentFor, imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    var (b, m, note) = DazzleLmStudio.PrepareImage(imagePath, Program.Settings.DazzleRefineImageLongSide);
                    bytes = b; mime = m; chatImageSentFor = imagePath;
                    Log("chat image: " + note);
                }
                chatHistory.Add(DazzleLmStudio.UserMessage(sb.ToString(), bytes, mime));
                TrimHistoryIfNeeded();

                var tools = chatToolsOn.Checked ? ChatTools() : null;
                for (int round = 0; round < 8; round++)
                {
                    int reasoningChars = 0; var contentSoFar = new StringBuilder();
                    var progress = new Progress<DazzleLmStudio.Delta>(d =>
                    {
                        if (d.Kind == DazzleLmStudio.DeltaKind.Reasoning) reasoningChars += d.Text.Length;
                        else if (d.Kind == DazzleLmStudio.DeltaKind.Content) contentSoFar.Append(d.Text);
                        labelContext.Text = ContextText() + "   " + probe.Model + ": thinking " + reasoningChars + ", reply " + contentSoFar.Length;
                    });
                    var r = await client.ChatAsync(ForTheServer(chatHistory), tools, chatThink.Checked, Program.Settings.DazzleRefineMaxTokens, Program.Settings.DazzleRefineTemperature, progress, chatCts.Token);
                    if (r.TotalTokens is int tt) chatLastTotalTokens = tt;
                    chatLastModel = probe.Model ?? chatLastModel;
                    if (r.Reasoning.Length > 0) Log("reasoning:\n" + r.Reasoning);
                    if (!r.Ok) { AppendTranscript(r.Cancelled ? "stopped" : r.Error, ErrorColor, true); Log("ERROR " + r.Error); break; }
                    chatHistory.Add(DazzleLmStudio.AssistantMessage(r));
                    if (r.Reasoning.Length > 0) AppendTranscript("(thought " + r.Reasoning.Length + " chars -- see Logs)", ToolColor, true);
                    if (r.Content.Length > 0) AppendTranscript("AI: " + r.Content, AssistantColor, false);
                    Log("assistant: " + r.Content + (r.ToolCalls.Count > 0 ? "\n  tool calls: " + string.Join("; ", r.ToolCalls.Select(c => c.Name + " " + c.Arguments)) : ""));
                    if (r.ToolCalls.Count == 0) break;
                    foreach (var call in r.ToolCalls)
                    {
                        string result = await ExecuteToolAsync(call);
                        chatHistory.Add(DazzleLmStudio.ToolResultMessage(call.Id, result));
                        Log("tool " + call.Name + " " + call.Arguments + " -> " + result);
                    }
                    if (chatCts.IsCancellationRequested) break;
                }
                UpdateContextMeter();
                RefreshChatResultPanel();
            }
            catch (Exception e) { AppendTranscript(e.Message, ErrorColor, true); Log("ERROR " + e.Message); }
            finally
            {
                chatCts.Dispose(); chatCts = null;
                buttonSend.Enabled = true; buttonChatStop.Enabled = false;
                textChatInput.Focus();
                AfterChatTurn();   // write this image's chat; apply an image change that came in meanwhile
            }
        }

        /// <summary>The history as the server takes it: the tool's own markers (satt_*) are not OpenAI fields.</summary>
        private static JArray ForTheServer(JArray history)
        {
            if (!history.OfType<JObject>().Any(m => m.Properties().Any(p => p.Name.StartsWith("satt_")))) return history;
            var copy = (JArray)history.DeepClone();
            foreach (var m in copy.OfType<JObject>()) foreach (var p in m.Properties().Where(p => p.Name.StartsWith("satt_")).ToList()) p.Remove();
            return copy;
        }

        private async Task<string> ExecuteToolAsync(DazzleLmStudio.ToolCall call)
        {
            JObject args;
            try { args = string.IsNullOrWhiteSpace(call.Arguments) ? new JObject() : JObject.Parse(call.Arguments); }
            catch (Exception e) { return "{\"ok\":false,\"error\":\"arguments are not valid JSON: " + e.Message.Replace("\"", "'") + "\"}"; }
            string path = currentInfo?.Path;
            if (path == null) return "{\"ok\":false,\"error\":\"no image is selected\"}";
            (bool ok, string message) r;
            switch (call.Name)
            {
                case "get_image":
                    {
                        string refined = proposals.TryGetValue(path, out var p) ? p.right : "";
                        var o = new JObject
                        {
                            ["file"] = Path.GetFileName(path), ["folder"] = Path.GetDirectoryName(path),
                            ["caption"] = textBoxTags != null && textBoxTags.Enabled ? textBoxTags.Text.Trim() : "",
                            // the folder's rules and the Check for list are left out (user, 2026-10-02): sent only when a
                            // skill places {rules} or {checks}, until Settings > AI makes automatic sending a choice
                            ["latest_proposal"] = refined, ["position"] = DatasetPosition(path).position + " of " + DatasetPosition(path).total,
                        };
                        AppendTranscript("[get_image]", ToolColor, true);
                        return o.ToString(Newtonsoft.Json.Formatting.None);
                    }
                case "set_caption":
                    r = SetCaptionFor(path, (string)args["caption"] ?? "");
                    break;
                case "rename_image":
                    if (chatAskFiles.Checked && !await ConfirmFileChangeAsync("Rename " + Path.GetFileName(path) + " to '" + (string)args["new_name"] + "'?")) { r = (false, "the user declined the rename"); break; }
                    r = RenameImage(path, (string)args["new_name"] ?? "");
                    break;
                case "move_image":
                    if (chatAskFiles.Checked && !await ConfirmFileChangeAsync("Move " + Path.GetFileName(path) + " to '" + (string)args["folder"] + "'?")) { r = (false, "the user declined the move"); break; }
                    r = MoveImage(path, (string)args["folder"] ?? "");
                    break;
                case "list_images":
                    {
                        string filter = ((string)args["filter"] ?? "").Trim();
                        var rows = Program.DataManager.DataSet.Values.OrderBy(i => i.ImageFilePath, StringComparer.OrdinalIgnoreCase)
                            .Where(i => filter.Length == 0 || i.Tags.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))
                            .Take(200).Select(i => new JObject { ["file"] = Path.GetRelativePath(dazzleDatasetFolder ?? Path.GetDirectoryName(i.ImageFilePath), i.ImageFilePath), ["caption"] = i.Tags.ToString() });
                        AppendTranscript("[list_images" + (filter.Length > 0 ? " '" + filter + "'" : "") + "]", ToolColor, true);
                        return new JObject { ["count"] = Program.DataManager.DataSet.Count, ["images"] = new JArray(rows) }.ToString(Newtonsoft.Json.Formatting.None);
                    }
                default:
                    return "{\"ok\":false,\"error\":\"unknown tool " + call.Name + "\"}";
            }
            AppendTranscript("[" + call.Name + "] " + r.message, r.ok ? ToolColor : ErrorColor, true);
            RefreshChatResultPanel();
            var res = new JObject { ["ok"] = r.ok, [r.ok ? "result" : "error"] = r.message };
            if (r.ok && currentInfo != null) { res["file"] = Path.GetFileName(currentInfo.Path); res["folder"] = Path.GetDirectoryName(currentInfo.Path); }
            return res.ToString(Newtonsoft.Json.Formatting.None);
        }

        private Task<bool> ConfirmFileChangeAsync(string question)
        {
            var tcs = new TaskCompletionSource<bool>();
            BeginInvoke(new Action(() => tcs.SetResult(MessageBox.Show(this, question, "Chat -- file change", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)));
            return tcs.Task;
        }

        private void UndoLastChange()
        {
            var last = changeJournal.LastOrDefault(c => !c.Undone);
            if (last == null) return;
            var r = UndoChange(last);
            AppendTranscript(r.message, r.ok ? ToolColor : ErrorColor, true);
            if (r.ok && chatHistory != null) chatHistory.Add(DazzleLmStudio.UserMessage("(The user undid the last change: " + last.Describe() + ". The file is " + Path.GetFileName(currentInfo?.Path ?? "") + " again.)"));
            RefreshChatResultPanel();
        }

        // ---------------------------------------------------------------- the window

        private string ContextText() => chatLastTotalTokens > 0 ? chatLastTotalTokens.ToString("N0") + " / " + chatContextLength.ToString("N0") + " tokens" : "context " + chatContextLength.ToString("N0") + " tokens";

        private void UpdateContextMeter() { if (labelContext != null) labelContext.Text = ContextText(); }

        /// <summary>Above 75% of the loaded window, drop the oldest turns (never the system message) until under half.</summary>
        private void TrimHistoryIfNeeded()
        {
            if (chatHistory == null || chatLastTotalTokens < chatContextLength * Program.Settings.DazzleChatTrimPercent / 100.0) return;
            int before = chatHistory.Count;
            // estimate: tokens are spread over the messages; remove from index 1 until the estimate is under half
            int estimate = chatLastTotalTokens;
            while (chatHistory.Count > 3 && estimate > chatContextLength * 0.5)
            {
                var m = (JObject)chatHistory[1];
                int len = m.ToString(Newtonsoft.Json.Formatting.None).Length;
                estimate -= Math.Max(50, len / 4);
                chatHistory.RemoveAt(1);
                // a tool result must not be left without its call
                while (chatHistory.Count > 1 && (string)((JObject)chatHistory[1])["role"] == "tool") chatHistory.RemoveAt(1);
            }
            if (chatHistory.Count < before)
            {
                chatImageSentFor = null;   // the image may have been dropped; send it again next turn
                AppendTranscript("(the oldest " + (before - chatHistory.Count) + " messages were dropped to stay within the model's context)", ToolColor, true);
                Log("trimmed history: " + before + " -> " + chatHistory.Count + " messages");
            }
        }

        // ---------------------------------------------------------------- panels

        private void RefreshChatResultPanel()
        {
            if (panelChat == null || !panelChat.Visible) return;
            labelResultFile.Text = currentInfo != null ? Path.GetFileName(currentInfo.Path) : "(no image)";
            labelResultFolder.Text = currentInfo != null && dazzleDatasetFolder != null ? "." + (Path.GetRelativePath(dazzleDatasetFolder, Path.GetDirectoryName(currentInfo.Path)) is string rp && rp != "." ? "\\" + rp : "") + "   (" + Path.GetDirectoryName(currentInfo.Path) + ")" : "";
            labelResultCaption.Text = textBoxTags != null && textBoxTags.Enabled ? (textBoxTags.Text.Trim().Length > 0 ? textBoxTags.Text.Trim() : "(none)") : "";
            var last = changeJournal.LastOrDefault(c => !c.Undone);
            labelResultChange.Text = last != null ? last.Describe() + "  (" + last.When.ToString("HH:mm:ss") + ")" : "none yet";
            labelResultChange.ForeColor = last != null ? Color.FromArgb(160, 90, 0) : ToolColor;
            buttonUndoLast.Enabled = last != null;
            UpdateContextMeter();
        }

        /// <summary>Append a line; the model's light Markdown -- **bold** and `code` -- is rendered rather than shown raw (user's session, 2026-10-02).</summary>
        private void AppendTranscript(string text, Color color, bool italic)
        {
            if (transcript == null) return;
            var baseFont = italic ? new Font(transcript.Font, FontStyle.Italic) : transcript.Font;
            var boldFont = new Font(transcript.Font, FontStyle.Bold | (italic ? FontStyle.Italic : FontStyle.Regular));
            var codeFont = new Font(FontFamily.GenericMonospace, transcript.Font.Size, italic ? FontStyle.Italic : FontStyle.Regular);
            transcript.SelectionStart = transcript.TextLength; transcript.SelectionLength = 0;
            int i = 0;
            while (i < text.Length)
            {
                int bold = text.IndexOf("**", i, StringComparison.Ordinal);
                int code = text.IndexOf('`', i);
                int next = Math.Min(bold < 0 ? int.MaxValue : bold, code < 0 ? int.MaxValue : code);
                if (next == int.MaxValue) { Run(text.Substring(i), baseFont, color); break; }
                if (next > i) Run(text.Substring(i, next - i), baseFont, color);
                if (next == bold)
                {
                    int end = text.IndexOf("**", bold + 2, StringComparison.Ordinal);
                    if (end < 0) { Run(text.Substring(bold), baseFont, color); break; }
                    Run(text.Substring(bold + 2, end - bold - 2), boldFont, color); i = end + 2;
                }
                else
                {
                    int end = text.IndexOf('`', code + 1);
                    if (end < 0) { Run(text.Substring(code), baseFont, color); break; }
                    Run(text.Substring(code + 1, end - code - 1), codeFont, Color.FromArgb(90, 40, 120)); i = end + 1;
                }
            }
            Run("\n", baseFont, color);
            transcript.SelectionColor = transcript.ForeColor;
            transcript.ScrollToCaret();
        }

        private void Run(string s, Font font, Color color)
        {
            transcript.SelectionStart = transcript.TextLength; transcript.SelectionLength = 0;
            transcript.SelectionFont = font; transcript.SelectionColor = color;
            transcript.AppendText(s);
        }
    }
}
