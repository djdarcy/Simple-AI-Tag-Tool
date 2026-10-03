using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool: Settings > AI (#4, 2.15.0). One page for the AI modes, built here at runtime so the Designer
    /// file is not touched: the server (shared with the AutoTagger tab's OpenAI block), the request settings moved from
    /// the UI tab, the Refine and Chat defaults, the skills folders, where data lives, and the context sent with a
    /// request. The main window applies what changed when the dialog closes (Form1.AiSettings.cs).
    /// </summary>
    public partial class Form_settings
    {
        private Manina.Windows.Forms.Tab tabAi;
        private System.Windows.Forms.TextBox textAiRulesFraming, textAiChecksFraming;
        private NumericUpDown numAiTrim;
        private System.Windows.Forms.ComboBox comboAiModel, comboAiRefineDefault, comboAiChatDefault, comboAiStore;
        private System.Windows.Forms.CheckBox checkAiThink, checkAiSchema, checkAiTools, checkAiAskFiles, checkAiShowHouse, checkAiPortable,
            checkAiRulesRefine, checkAiRulesChat, checkAiChecksRefine, checkAiChecksChat, checkAiKeepConversations, checkAiChatFromRefine;
        private Label labelAiTest, labelAiTestHere, labelAiServer, labelAiFolders, labelAiSkillsFolder;

        private const string LastUsedSkill = "(the skill used last)";
        private const string LoadedModel = "(whatever model is loaded)";

        private sealed class ModelChoice
        {
            public string Id; public bool Loaded;
            public override string ToString() => Id + (Loaded ? "   (loaded)" : "");
        }

        private void BuildAiTab()
        {
            if (tabAi != null) return;
            tabAi = new Manina.Windows.Forms.Tab { Name = "tabAi", Text = "AI" };
            var scroll = new Panel { Name = "panelAiScroll", Dock = DockStyle.Fill, AutoScroll = true };
            var stack = new TableLayoutPanel { Name = "tableAi", ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(6) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scroll.Controls.Add(stack);
            tabAi.Controls.Add(scroll);

            TableLayoutPanel Group(string title, string name)
            {
                var g = new GroupBox { Text = title, Name = name, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 6), Margin = new Padding(0, 0, 0, 8) };
                var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                g.Controls.Add(grid);
                stack.Controls.Add(g);
                return grid;
            }
            Label L(string t) => new Label { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(2, 6, 8, 2) };
            void Row(TableLayoutPanel grid, string label, Control a, Control b = null)
            {
                int r = grid.RowCount++;
                grid.Controls.Add(label == null ? new Label { AutoSize = true } : L(label), 0, r);
                grid.Controls.Add(a, 1, r);
                if (b != null) grid.Controls.Add(b, 2, r);
            }
            int boxW = Math.Max(300, Font.Height * 22);
            void Hint(TableLayoutPanel grid, string text)
            {
                int r = grid.RowCount++;
                var h = new Label { Text = text, AutoSize = true, MaximumSize = new Size((int)(boxW * 1.6), 0), ForeColor = SystemColors.GrayText, Margin = new Padding(2, 4, 2, 4) };
                grid.Controls.Add(new Label { AutoSize = true }, 0, r);
                grid.Controls.Add(h, 1, r); grid.SetColumnSpan(h, 2);
            }
            System.Windows.Forms.Button B(string t, EventHandler click) { var b = new System.Windows.Forms.Button { Text = t, AutoSize = true, Margin = new Padding(2) }; b.Click += click; return b; }
            System.Windows.Forms.CheckBox C(string t, bool v) => new System.Windows.Forms.CheckBox { Text = t, Checked = v, AutoSize = true, Margin = new Padding(2, 4, 2, 2) };

            // ---- Server: configured in one place, the AiApiServer tab's OpenAI block (the person, 2026-10-02 20:14), which
            // gains Model, Load list and Test; this tab names the server and can test it
            BuildServerExtras(boxW);
            var server = Group("Server", "groupAiServer");
            labelAiServer = new Label { Name = "labelAiServer", AutoSize = true, Margin = new Padding(2, 6, 8, 2) };
            void ShowServer() => labelAiServer.Text = DazzleLmStudio.NormalizeEndpoint(textBoxOpenApiEndpoint.Text) + "   model: " + (ModelValue().Length == 0 ? "whatever is loaded" : ModelValue()) + "   (set on the AiApiServer tab)";
            textBoxOpenApiEndpoint.TextChanged += (s, e) => ShowServer();
            comboAiModel.TextChanged += (s, e) => ShowServer();
            ShowServer();
            labelAiTestHere = new Label { Name = "labelAiTestHere", AutoSize = true, MaximumSize = new Size(boxW * 2, 0), Margin = new Padding(2, 4, 2, 2) };
            Row(server, null, labelAiServer, B("Test", async (s, e) => await AiProbeAsync(false, labelAiTestHere)));
            server.SetColumnSpan(labelAiTestHere, 3);
            server.Controls.Add(labelAiTestHere, 0, server.RowCount++);

            // ---- Requests: moved from the UI tab (the same controls, so the save code reads them as before)
            var req = Group("Requests", "groupAiRequests");
            foreach (var c in new Control[] { labelRefine, labelRefineMaxTokens, numericRefineMaxTokens, labelRefineTemperature, numericRefineTemperature, labelRefineImageSide, numericRefineImageSide })
                c.Parent?.Controls.Remove(c);
            labelRefine.Visible = false;
            foreach (var (lbl, num) in new[] { (labelRefineMaxTokens, numericRefineMaxTokens), (labelRefineTemperature, numericRefineTemperature), (labelRefineImageSide, numericRefineImageSide) })
            {
                lbl.AutoSize = true; lbl.Anchor = AnchorStyles.Left; lbl.Margin = new Padding(2, 6, 8, 2);
                int r = req.RowCount++; req.Controls.Add(lbl, 0, r); req.Controls.Add(num, 1, r);
            }
            checkAiThink = C("Think: let the model reason first (on by default)", Program.Settings.DazzleRefineThink);
            checkAiSchema = C("Ask AI Refine's reply as strict JSON (Schema)", Program.Settings.DazzleRefineSchema);
            Row(req, null, checkAiThink); Row(req, null, checkAiSchema);

            // ---- AI Refine and AI Chat defaults
            var refine = Group("AI Refine", "groupAiRefine");
            comboAiRefineDefault = SkillCombo("comboAiRefineDefault", "refine", Program.Settings.DazzleRefineDefaultSkill, boxW);
            Row(refine, "Start with skill", comboAiRefineDefault);
            Hint(refine, "Suits a 16k-32k context: one image, one answer.");
            var chat = Group("AI Chat", "groupAiChat");
            comboAiChatDefault = SkillCombo("comboAiChatDefault", "chat", Program.Settings.DazzleChatDefaultSkill, boxW);
            checkAiTools = C("Tools: the model may set the caption, rename and move", Program.Settings.DazzleChatTools);
            checkAiAskFiles = C("Ask before each rename or move", Program.Settings.DazzleChatAskFiles);
            numAiTrim = new NumericUpDown { Name = "numAiTrim", Minimum = 30, Maximum = 95, Value = Math.Max(30, Math.Min(95, Program.Settings.DazzleChatTrimPercent)), Width = 70 };
            Row(chat, "Start with skill", comboAiChatDefault);
            checkAiChatFromRefine = C("Start a chat from the image's AI Refine run, when it has one", Program.Settings.DazzleChatFromRefine);
            Row(chat, null, checkAiTools); Row(chat, null, checkAiAskFiles); Row(chat, null, checkAiChatFromRefine);
            Row(chat, "Drop the oldest turns at (% of context)", numAiTrim);
            Hint(chat, "A real conversation wants about 100k tokens of context loaded in LM Studio.");

            // ---- Skills
            var skills = Group("Skills", "groupAiSkills");
            labelAiSkillsFolder = new Label { Name = "labelAiSkillsFolder", AutoSize = true, Margin = new Padding(2, 6, 2, 2), Text = Path.Combine(DazzleData.BaseFolder, "skills") };
            checkAiShowHouse = C("Show the skills that ship with the program", Program.Settings.DazzleShowHouseSkills);
            Row(skills, "Your skills", labelAiSkillsFolder, B("Open", (s, e) => OpenFolder(Path.Combine(DazzleData.BaseFolder, "skills"))));
            Row(skills, null, checkAiShowHouse);

            // ---- Where data lives
            var data = Group("Where your data lives", "groupAiData");
            checkAiPortable = C("Portable: keep everything beside the program", DazzleData.IsPortable);
            labelAiFolders = new Label { Name = "labelAiFolders", AutoSize = true, MaximumSize = new Size(boxW * 2, 0), Margin = new Padding(2, 4, 2, 2) };
            checkAiPortable.CheckedChanged += (s, e) => ShowFolders();
            ShowFolders();
            comboAiStore = new System.Windows.Forms.ComboBox { Name = "comboAiStore", DropDownStyle = ComboBoxStyle.DropDownList, Width = boxW };
            comboAiStore.Items.AddRange(DazzleData.StoreChoices);
            comboAiStore.SelectedIndex = Math.Max(0, Math.Min(2, Program.Settings.DazzleConversationStore));
            Row(data, null, checkAiPortable);
            data.SetColumnSpan(labelAiFolders, 3); data.Controls.Add(labelAiFolders, 0, data.RowCount++);
            var openRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            openRow.Controls.Add(B("Open data folder", (s, e) => OpenFolder(DazzleData.BaseFolder)));
            openRow.Controls.Add(B("Open Documents folder", (s, e) => OpenFolder(DazzleData.DocumentsRoot)));
            Row(data, null, openRow);
            checkAiKeepConversations = C("Keep each image's AI Refine run and AI Chat conversation on disk", Program.Settings.DazzleKeepConversations);
            Row(data, null, checkAiKeepConversations);
            Row(data, "Per-image files", comboAiStore);
            Hint(data, "Changing this moves the open dataset's files to the new place.");

            // ---- Context sent with a request: off unless chosen; each piece framed by an editable sentence
            var ctx = Group("Context sent with a request (besides the image, the skill and the caption)", "groupAiContext");
            checkAiRulesRefine = C("AI Refine", Program.Settings.DazzleSendRulesRefine); checkAiRulesChat = C("AI Chat", Program.Settings.DazzleSendRulesChat);
            checkAiChecksRefine = C("AI Refine", Program.Settings.DazzleSendChecksRefine); checkAiChecksChat = C("AI Chat", Program.Settings.DazzleSendChecksChat);
            textAiRulesFraming = Framing("textAiRulesFraming", Program.Settings.DazzleRulesFraming, boxW);
            textAiChecksFraming = Framing("textAiChecksFraming", Program.Settings.DazzleChecksFraming, boxW);
            var rulesRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) }; rulesRow.Controls.AddRange(new Control[] { checkAiRulesRefine, checkAiRulesChat });
            var checksRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) }; checksRow.Controls.AddRange(new Control[] { checkAiChecksRefine, checkAiChecksChat });
            Row(ctx, "The folder's rules", rulesRow);
            Row(ctx, "  framed by", textAiRulesFraming, B("Reset", (s, e) => textAiRulesFraming.Text = DazzleContext.DefaultRulesFraming));
            Row(ctx, "The Check for list", checksRow);
            Row(ctx, "  framed by", textAiChecksFraming, B("Reset", (s, e) => textAiChecksFraming.Text = DazzleContext.DefaultChecksFraming));
            Hint(ctx, "A skill that writes {rules} or {checks} places them itself, and the automatic copy is then left out.");

            // names, so each control has an automation id (UI probes find them by it)
            foreach (var (c, n) in new (Control, string)[] { (checkAiThink, "checkAiThink"), (checkAiSchema, "checkAiSchema"), (checkAiTools, "checkAiTools"), (checkAiAskFiles, "checkAiAskFiles"),
                (checkAiShowHouse, "checkAiShowHouse"), (checkAiPortable, "checkAiPortable"), (checkAiRulesRefine, "checkAiRulesRefine"), (checkAiRulesChat, "checkAiRulesChat"),
                (checkAiChecksRefine, "checkAiChecksRefine"), (checkAiChecksChat, "checkAiChecksChat"),
                (checkAiKeepConversations, "checkAiKeepConversations"), (checkAiChatFromRefine, "checkAiChatFromRefine") })
                c.Name = n;
            SettingFrame.Tabs.Add(tabAi);
            if (Program.ColorManager.SelectedScheme != null)
                Program.ColorManager.ChangeColorSchemeInConteiner(tabAi.Controls, Program.ColorManager.SelectedScheme);
            // reopen on the tab used last (and the AI tab is reachable without a click, which UI probes cannot make on this tab control)
            var last = SettingFrame.Tabs.Cast<Manina.Windows.Forms.Tab>().FirstOrDefault(t => t.Name == Program.Settings.DazzleSettingsTab);
            if (last != null) { try { SettingFrame.SelectedTab = last; } catch (Exception) { } }
            FormClosing += (s, e) => { if (SettingFrame.SelectedTab != null) Program.Settings.DazzleSettingsTab = SettingFrame.SelectedTab.Name; };
        }

        /// <summary>
        /// The OpenAI block on the AiApiServer tab (Designer-placed: address, key, timeout) gains a row below its last field:
        /// Model with Load list and Test, and the test's answer; the block grows to fit.
        /// </summary>
        private void BuildServerExtras(int boxW)
        {
            var block = textBoxOpenApiEndpoint.Parent;
            comboAiModel = new System.Windows.Forms.ComboBox { Name = "comboAiModel", Width = Math.Max(200, textBoxOpenApiEndpoint.Width - 180), DropDownStyle = ComboBoxStyle.DropDown };
            comboAiModel.Items.Add(LoadedModel);
            string model = Program.Settings.OpenAiAutoTagger.Model ?? "";
            if (model.Length == 0) comboAiModel.SelectedIndex = 0; else comboAiModel.Text = model;
            labelAiTest = new Label { Name = "labelAiTest", AutoSize = true, MaximumSize = new Size(Math.Max(300, block.ClientSize.Width - 20), 0), Margin = new Padding(2, 4, 2, 2), Text = "Test asks the server what is loaded." };
            var load = new System.Windows.Forms.Button { Text = "Load list", AutoSize = true, Margin = new Padding(4, 0, 2, 0) };
            load.Click += async (s, e) => await AiProbeAsync(true, labelAiTest);
            var test = new System.Windows.Forms.Button { Name = "buttonAiTestServer", Text = "Test", AutoSize = true, Margin = new Padding(2, 0, 2, 0) };
            test.Click += async (s, e) => await AiProbeAsync(false, labelAiTest);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Controls.AddRange(new Control[] { comboAiModel, load, test });
            var panel = new TableLayoutPanel { Name = "panelAiServerExtras", ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Location = new Point(labelOpenAiTimeout.Left, numericUpDownOpenAiTimeout.Bottom + 6) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, textBoxOpenApiEndpoint.Left - labelOpenAiTimeout.Left - 3));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label { Text = "Model", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 2) }, 0, 0);
            panel.Controls.Add(row, 1, 0);
            panel.SetColumnSpan(labelAiTest, 2);
            panel.Controls.Add(labelAiTest, 0, 1);
            block.Controls.Add(panel);
            void Fit() => block.Height = Math.Max(block.Height, panel.Bottom + 10);
            panel.SizeChanged += (s, e) => Fit();
            Fit();
        }

        private System.Windows.Forms.ComboBox SkillCombo(string name, string kind, string current, int width)
        {
            var c = new System.Windows.Forms.ComboBox { Name = name, DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
            c.Items.Add(LastUsedSkill);
            foreach (var s in DazzleData.SkillFiles(kind, true)) c.Items.Add(s.Name);
            int i = string.IsNullOrEmpty(current) ? 0 : c.Items.IndexOf(current);
            if (i < 0) { c.Items.Add(current); i = c.Items.Count - 1; }   // a default whose file is gone stays visible rather than silently becoming "last used"
            c.SelectedIndex = i;
            return c;
        }

        private static System.Windows.Forms.TextBox Framing(string name, string text, int width) =>
            new System.Windows.Forms.TextBox { Name = name, Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Width = (int)(width * 1.4), Height = 64, Text = text ?? "" };

        private void ShowFolders()
        {
            bool portable = checkAiPortable.Checked;
            string baseDir = portable ? DazzleData.AppFolder : DazzleData.HomeRoot;
            string other = portable ? DazzleData.HomeRoot : DazzleData.AppFolder;
            string text = "Your settings, skills and conversations: " + baseDir + "\nAlso read: " + other + " (for what the first lacks) and " + DazzleData.DocumentsRoot;
            if (portable != DazzleData.IsPortable)
                text += "\nSaving switches now: the settings in use are written there" + (File.Exists(Path.Combine(baseDir, "settings.json")) ? "; the settings already there are kept as a dated copy" : "") + ".";
            else if (portable && File.Exists(Path.Combine(DazzleData.HomeRoot, "settings.json")))
                text += "\nNote: " + DazzleData.HomeRoot + " also holds settings (a newer configuration may be there).";
            labelAiFolders.Text = text;
        }

        private static void OpenFolder(string path)
        {
            try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception) { }
        }

        private async System.Threading.Tasks.Task AiProbeAsync(bool fill, Label answer)
        {
            answer.Text = "asking " + DazzleLmStudio.NormalizeEndpoint(textBoxOpenApiEndpoint.Text) + "...";
            string want = ModelValue();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var client = new DazzleLmStudio(textBoxOpenApiEndpoint.Text, textBoxOpenAiApiKey.Text, (int)numericUpDownOpenAiTimeout.Value);
                var p = await client.ProbeAsync(fill || string.IsNullOrEmpty(want) ? null : want, cts.Token);
                if (fill && p.Listed.Count > 0)
                {
                    string keep = comboAiModel.Text;
                    comboAiModel.Items.Clear();
                    comboAiModel.Items.Add(LoadedModel);
                    foreach (var id in p.Listed.OrderByDescending(id => p.Loaded?.Contains(id) == true).ThenBy(id => id, StringComparer.OrdinalIgnoreCase))
                        comboAiModel.Items.Add(new ModelChoice { Id = id, Loaded = p.Loaded?.Contains(id) == true });
                    comboAiModel.Text = keep;
                }
                string vision = p.Vision == true ? "sees images" : p.Vision == false ? "NO vision" : "vision unknown";
                answer.Text = !p.Reachable ? p.Reason
                    : (p.Model + (p.ContextLength is int n ? ", " + n.ToString("N0") + "-token context" : "") + ", " + vision
                       + (fill ? "; " + p.Listed.Count + " models listed, " + (p.Loaded?.Count ?? 0) + " loaded" : "")
                       + (p.Warning != null ? "\n" + p.Warning : "")
                       + "\nAI Refine suits 16k-32k; AI Chat wants about 100k.");
            }
            catch (Exception e) { answer.Text = "could not reach the server: " + e.Message; }
        }

        private string ModelValue()
        {
            if (comboAiModel.SelectedItem is ModelChoice m) return m.Id;
            string t = (comboAiModel.Text ?? "").Trim();
            int cut = t.IndexOf("   (loaded)", StringComparison.Ordinal);
            if (cut > 0) t = t.Substring(0, cut);
            return t == LoadedModel ? "" : t;
        }

        /// <summary>Called by Save before the settings are written. The portable switch and the store move are applied by the main window afterwards.</summary>
        private void SaveAiTab()
        {
            if (tabAi == null) return;
            Program.Settings.OpenAiAutoTagger.Model = ModelValue();
            Program.Settings.DazzleRefineThink = checkAiThink.Checked;
            Program.Settings.DazzleRefineSchema = checkAiSchema.Checked;
            Program.Settings.DazzleRefineDefaultSkill = comboAiRefineDefault.SelectedIndex <= 0 ? "" : (string)comboAiRefineDefault.SelectedItem;
            Program.Settings.DazzleChatDefaultSkill = comboAiChatDefault.SelectedIndex <= 0 ? "" : (string)comboAiChatDefault.SelectedItem;
            Program.Settings.DazzleChatTools = checkAiTools.Checked;
            Program.Settings.DazzleChatAskFiles = checkAiAskFiles.Checked;
            Program.Settings.DazzleChatTrimPercent = (int)numAiTrim.Value;
            Program.Settings.DazzleShowHouseSkills = checkAiShowHouse.Checked;
            Program.Settings.DazzleDataPortable = checkAiPortable.Checked;          // the request; the main window performs the switch
            Program.Settings.DazzleConversationStore = comboAiStore.SelectedIndex;  // likewise the move
            Program.Settings.DazzleKeepConversations = checkAiKeepConversations.Checked;
            Program.Settings.DazzleChatFromRefine = checkAiChatFromRefine.Checked;
            Program.Settings.DazzleSendRulesRefine = checkAiRulesRefine.Checked;
            Program.Settings.DazzleSendRulesChat = checkAiRulesChat.Checked;
            Program.Settings.DazzleSendChecksRefine = checkAiChecksRefine.Checked;
            Program.Settings.DazzleSendChecksChat = checkAiChecksChat.Checked;
            Program.Settings.DazzleRulesFraming = string.IsNullOrWhiteSpace(textAiRulesFraming.Text) ? DazzleContext.DefaultRulesFraming : textAiRulesFraming.Text.Trim();
            Program.Settings.DazzleChecksFraming = string.IsNullOrWhiteSpace(textAiChecksFraming.Text) ? DazzleContext.DefaultChecksFraming : textAiChecksFraming.Text.Trim();
        }
    }
}
