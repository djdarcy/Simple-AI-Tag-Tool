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
        private Label labelAiTest, labelAiTestHere, labelAiServer, labelAiFolders;
        private System.Windows.Forms.TextBox textAiSkillsFolder, textAiBaseFolder, textAiOtherFolder, textAiDocumentsFolder;

        // every string on this tab comes from Languages\<lang>.txt, keys SettingsAi*, as upstream's strings do (the person, 2026-10-02 23:15)
        private static string T(string key) => I18n.GetText(key);
        private static string T(string key, params object[] args) => string.Format(I18n.GetText(key), args);
        private static string LastUsedSkill => T("SettingsAiLastUsedSkill");
        private static string LoadedModel => T("SettingsAiLoadedModel");
        private static string LoadedSuffix => "   " + T("SettingsAiLoaded");

        private sealed class ModelChoice
        {
            public string Id; public bool Loaded;
            public override string ToString() => Id + (Loaded ? LoadedSuffix : "");
        }

        private void BuildAiTab()
        {
            if (tabAi != null) return;
            tabAi = new Manina.Windows.Forms.Tab { Name = "tabAi", Text = T("SettingsAiTab") };
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
                var h = new Label { Text = text, AutoSize = true, MaximumSize = new Size((int)(boxW * 1.1), 0), ForeColor = SystemColors.GrayText, Margin = new Padding(2, 4, 2, 4) };
                grid.Controls.Add(new Label { AutoSize = true }, 0, r);
                grid.Controls.Add(h, 1, r); grid.SetColumnSpan(h, 2);
            }
            System.Windows.Forms.Button B(string t, EventHandler click) { var b = new System.Windows.Forms.Button { Text = t, AutoSize = true, Margin = new Padding(2) }; b.Click += click; return b; }
            System.Windows.Forms.CheckBox C(string t, bool v) => new System.Windows.Forms.CheckBox { Text = t, Checked = v, AutoSize = true, Margin = new Padding(2, 4, 2, 2) };
            // a folder is shown in a read-only box, so the path can be selected and copied and a long one stays inside the dialog
            System.Windows.Forms.TextBox PathBox(string name) => new System.Windows.Forms.TextBox { Name = name, ReadOnly = true, Width = (int)(boxW * 1.3), Margin = new Padding(2, 3, 2, 2) };

            // ---- Server: configured in one place, the AiApiServer tab's OpenAI block (the person, 2026-10-02 20:14), which
            // gains Model, Load list and Test; this tab names the server and can test it
            BuildServerExtras(boxW);
            var server = Group(T("SettingsAiGroupServer"), "groupAiServer");
            labelAiServer = new Label { Name = "labelAiServer", AutoSize = true, Margin = new Padding(2, 6, 8, 2) };
            void ShowServer() => labelAiServer.Text = T("SettingsAiServerLine", DazzleLmStudio.NormalizeEndpoint(textBoxOpenApiEndpoint.Text), ModelValue().Length == 0 ? T("SettingsAiWhateverLoaded") : ModelValue());
            textBoxOpenApiEndpoint.TextChanged += (s, e) => ShowServer();
            comboAiModel.TextChanged += (s, e) => ShowServer();
            ShowServer();
            labelAiTestHere = new Label { Name = "labelAiTestHere", AutoSize = true, MaximumSize = new Size(boxW * 2, 0), Margin = new Padding(2, 4, 2, 2) };
            Row(server, null, labelAiServer, B(T("SettingsAiBtnTest"), async (s, e) => await AiProbeAsync(false, labelAiTestHere)));
            server.SetColumnSpan(labelAiTestHere, 3);
            server.Controls.Add(labelAiTestHere, 0, server.RowCount++);

            // ---- Requests: moved from the UI tab (the same controls, so the save code reads them as before)
            var req = Group(T("SettingsAiGroupRequests"), "groupAiRequests");
            foreach (var c in new Control[] { labelRefine, labelRefineMaxTokens, numericRefineMaxTokens, labelRefineTemperature, numericRefineTemperature, labelRefineImageSide, numericRefineImageSide })
                c.Parent?.Controls.Remove(c);
            labelRefine.Visible = false;
            foreach (var (lbl, num) in new[] { (labelRefineMaxTokens, numericRefineMaxTokens), (labelRefineTemperature, numericRefineTemperature), (labelRefineImageSide, numericRefineImageSide) })
            {
                lbl.AutoSize = true; lbl.Anchor = AnchorStyles.Left; lbl.Margin = new Padding(2, 6, 8, 2);
                int r = req.RowCount++; req.Controls.Add(lbl, 0, r); req.Controls.Add(num, 1, r);
            }
            checkAiThink = C(T("SettingsAiThink"), Program.Settings.DazzleRefineThink);
            checkAiSchema = C(T("SettingsAiSchema"), Program.Settings.DazzleRefineSchema);
            Row(req, null, checkAiThink); Row(req, null, checkAiSchema);

            // ---- AI Refine and AI Chat defaults
            var refine = Group(T("SettingsAiGroupRefine"), "groupAiRefine");
            comboAiRefineDefault = SkillCombo("comboAiRefineDefault", "refine", Program.Settings.DazzleRefineDefaultSkill, boxW);
            Row(refine, T("SettingsAiStartSkill"), comboAiRefineDefault);
            Hint(refine, T("SettingsAiRefineHint"));
            var chat = Group(T("SettingsAiGroupChat"), "groupAiChat");
            comboAiChatDefault = SkillCombo("comboAiChatDefault", "chat", Program.Settings.DazzleChatDefaultSkill, boxW);
            checkAiTools = C(T("SettingsAiTools"), Program.Settings.DazzleChatTools);
            checkAiAskFiles = C(T("SettingsAiAskFiles"), Program.Settings.DazzleChatAskFiles);
            numAiTrim = new NumericUpDown { Name = "numAiTrim", Minimum = 30, Maximum = 95, Value = Math.Max(30, Math.Min(95, Program.Settings.DazzleChatTrimPercent)), Width = 70 };
            Row(chat, T("SettingsAiStartSkill"), comboAiChatDefault);
            checkAiChatFromRefine = C(T("SettingsAiChatFromRefine"), Program.Settings.DazzleChatFromRefine);
            Row(chat, null, checkAiTools); Row(chat, null, checkAiAskFiles); Row(chat, null, checkAiChatFromRefine);
            Row(chat, T("SettingsAiTrim"), numAiTrim);
            Hint(chat, T("SettingsAiChatHint"));

            // ---- Skills
            var skills = Group(T("SettingsAiGroupSkills"), "groupAiSkills");
            textAiSkillsFolder = PathBox("textAiSkillsFolder"); textAiSkillsFolder.Text = Path.Combine(DazzleData.BaseFolder, "skills");
            checkAiShowHouse = C(T("SettingsAiShowHouse"), Program.Settings.DazzleShowHouseSkills);
            Row(skills, T("SettingsAiYourSkills"), textAiSkillsFolder, B(T("SettingsAiBtnOpen"), (s, e) => OpenFolder(textAiSkillsFolder.Text)));
            Row(skills, null, checkAiShowHouse);

            // ---- Where data lives: one folder per line, in the order they are read, each with its own Open (the person, 2026-10-02 23:15)
            var data = Group(T("SettingsAiGroupData"), "groupAiData");
            checkAiPortable = C(T("SettingsAiPortable"), DazzleData.IsPortable);
            // each label on its own line, the folder under it with its Open beside it
            var folders = new TableLayoutPanel { Name = "tableAiFolders", ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 2, 0, 2) };
            for (int k = 0; k < 2; k++) folders.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            textAiBaseFolder = PathBox("textAiBaseFolder"); textAiOtherFolder = PathBox("textAiOtherFolder"); textAiDocumentsFolder = PathBox("textAiDocumentsFolder");
            foreach (var (label, box) in new[] { (T("SettingsAiDataBase"), textAiBaseFolder), (T("SettingsAiDataAlso"), textAiOtherFolder), (T("SettingsAiDataAnd"), textAiDocumentsFolder) })
            {
                var lbl = L(label); lbl.Margin = new Padding(2, 6, 2, 0);
                folders.Controls.Add(lbl, 0, folders.RowCount); folders.SetColumnSpan(lbl, 2); folders.RowCount++;
                int r = folders.RowCount++;
                folders.Controls.Add(box, 0, r);
                folders.Controls.Add(B(T("SettingsAiBtnOpen"), (s, e) => OpenFolder(box.Text)), 1, r);
            }
            labelAiFolders = new Label { Name = "labelAiFolders", AutoSize = true, MaximumSize = new Size(boxW * 2, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(2, 4, 2, 4) };
            checkAiPortable.CheckedChanged += (s, e) => ShowFolders();
            ShowFolders();
            comboAiStore = new System.Windows.Forms.ComboBox { Name = "comboAiStore", DropDownStyle = ComboBoxStyle.DropDownList, Width = boxW };
            comboAiStore.Items.AddRange(DazzleData.StoreChoices);
            comboAiStore.SelectedIndex = Math.Max(0, Math.Min(2, Program.Settings.DazzleConversationStore));
            Row(data, null, checkAiPortable);
            data.SetColumnSpan(folders, 3); data.Controls.Add(folders, 0, data.RowCount++);
            data.SetColumnSpan(labelAiFolders, 3); data.Controls.Add(labelAiFolders, 0, data.RowCount++);
            checkAiKeepConversations = C(T("SettingsAiKeepConversations"), Program.Settings.DazzleKeepConversations);
            Row(data, null, checkAiKeepConversations);
            Row(data, T("SettingsAiPerImageFiles"), comboAiStore);
            Hint(data, T("SettingsAiStoreHint"));

            // ---- Context sent with a request: off unless chosen; each piece framed by an editable sentence
            var ctx = Group(T("SettingsAiGroupContext"), "groupAiContext");
            checkAiRulesRefine = C(T("SettingsAiGroupRefine"), Program.Settings.DazzleSendRulesRefine); checkAiRulesChat = C(T("SettingsAiGroupChat"), Program.Settings.DazzleSendRulesChat);
            checkAiChecksRefine = C(T("SettingsAiGroupRefine"), Program.Settings.DazzleSendChecksRefine); checkAiChecksChat = C(T("SettingsAiGroupChat"), Program.Settings.DazzleSendChecksChat);
            textAiRulesFraming = Framing("textAiRulesFraming", Program.Settings.DazzleRulesFraming, boxW);
            textAiChecksFraming = Framing("textAiChecksFraming", Program.Settings.DazzleChecksFraming, boxW);
            var rulesRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) }; rulesRow.Controls.AddRange(new Control[] { checkAiRulesRefine, checkAiRulesChat });
            var checksRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) }; checksRow.Controls.AddRange(new Control[] { checkAiChecksRefine, checkAiChecksChat });
            Row(ctx, T("SettingsAiRules"), rulesRow);
            Row(ctx, T("SettingsAiFramedBy"), textAiRulesFraming, B(T("SettingsAiBtnReset"), (s, e) => textAiRulesFraming.Text = DazzleContext.DefaultRulesFraming));
            Row(ctx, T("SettingsAiChecks"), checksRow);
            Row(ctx, T("SettingsAiFramedBy"), textAiChecksFraming, B(T("SettingsAiBtnReset"), (s, e) => textAiChecksFraming.Text = DazzleContext.DefaultChecksFraming));
            Hint(ctx, T("SettingsAiContextHint"));

            // names, so each control has an automation id (UI probes find them by it)
            foreach (var (c, n) in new (Control, string)[] { (checkAiThink, "checkAiThink"), (checkAiSchema, "checkAiSchema"), (checkAiTools, "checkAiTools"), (checkAiAskFiles, "checkAiAskFiles"),
                (checkAiShowHouse, "checkAiShowHouse"), (checkAiPortable, "checkAiPortable"), (checkAiRulesRefine, "checkAiRulesRefine"), (checkAiRulesChat, "checkAiRulesChat"),
                (checkAiChecksRefine, "checkAiChecksRefine"), (checkAiChecksChat, "checkAiChecksChat"),
                (checkAiKeepConversations, "checkAiKeepConversations"), (checkAiChatFromRefine, "checkAiChatFromRefine") })
                c.Name = n;
            // AiApiServer goes last but one, beside AI, so the two read as one pair (the person, 2026-10-02 23:20)
            SettingFrame.Tabs.Remove(tabInterrogator);
            SettingFrame.Tabs.Add(tabInterrogator);
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
            labelAiTest = new Label { Name = "labelAiTest", AutoSize = true, MaximumSize = new Size(Math.Max(300, block.ClientSize.Width - 20), 0), Margin = new Padding(2, 4, 2, 2), Text = T("SettingsAiTestIntro") };
            var load = new System.Windows.Forms.Button { Text = T("SettingsAiBtnLoadList"), AutoSize = true, Margin = new Padding(4, 0, 2, 0) };
            load.Click += async (s, e) => await AiProbeAsync(true, labelAiTest);
            var test = new System.Windows.Forms.Button { Name = "buttonAiTestServer", Text = T("SettingsAiBtnTest"), AutoSize = true, Margin = new Padding(2, 0, 2, 0) };
            test.Click += async (s, e) => await AiProbeAsync(false, labelAiTest);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Controls.AddRange(new Control[] { comboAiModel, load, test });
            var panel = new TableLayoutPanel { Name = "panelAiServerExtras", ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Location = new Point(labelOpenAiTimeout.Left, numericUpDownOpenAiTimeout.Bottom + 6) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, textBoxOpenApiEndpoint.Left - labelOpenAiTimeout.Left - 3));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label { Text = T("SettingsAiModel"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 2) }, 0, 0);
            panel.Controls.Add(row, 1, 0);
            panel.SetColumnSpan(labelAiTest, 2);
            panel.Controls.Add(labelAiTest, 0, 1);
            block.Controls.Add(panel);
            void Fit() => block.Height = Math.Max(block.Height, panel.Bottom + 10);
            panel.SizeChanged += (s, e) => Fit();
            Fit();
        }

        /// <summary>The fork's controls on upstream's tabs take their text from the language file too; called at the end of SwitchLanguage.
        /// Combo entries are replaced in place, so the selection the load code made is kept.</summary>
        private void SwitchLanguageDazzle()
        {
            checkBoxIncludeSubfolders.Text = T("SettingsIncludeSubfolders");
            checkBoxDazzleLayout.Text = T("SettingsDazzleLayout");
            labelTagMatch.Text = T("SettingsTagMatch");
            labelEndOfFolder.Text = T("SettingsEndOfFolder");
            checkBoxRememberFolders.Text = T("SettingsRememberFolders");
            checkBoxReopenLastFolder.Text = T("SettingsReopenLastFolder");
            labelComfydbgPath.Text = T("SettingsComfydbgPath");
            labelRefineMaxTokens.Text = T("SettingsRefineMaxTokens");
            labelRefineTemperature.Text = T("SettingsRefineTemperature");
            labelRefineImageSide.Text = T("SettingsRefineImageSide");
            string[] match = { T("SettingsTagMatchStrict"), T("SettingsTagMatchLazy") };
            for (int i = 0; i < match.Length && i < comboBoxTagMatch.Items.Count; i++) comboBoxTagMatch.Items[i] = match[i];
            string[] end = { T("SettingsEndLoop"), T("SettingsEndStop"), T("SettingsEndAsk") };
            for (int i = 0; i < end.Length && i < comboBoxEndOfFolder.Items.Count; i++) comboBoxEndOfFolder.Items[i] = end[i];
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
            textAiBaseFolder.Text = baseDir; textAiOtherFolder.Text = other; textAiDocumentsFolder.Text = DazzleData.DocumentsRoot;
            string note = "";
            if (portable != DazzleData.IsPortable)
                note = T(File.Exists(Path.Combine(baseDir, "settings.json")) ? "SettingsAiSwitchNowKept" : "SettingsAiSwitchNow");
            else if (portable && File.Exists(Path.Combine(DazzleData.HomeRoot, "settings.json")))
                note = T("SettingsAiNewerConfig", DazzleData.HomeRoot);
            labelAiFolders.Text = note;
            labelAiFolders.Visible = note.Length > 0;
        }

        private static void OpenFolder(string path)
        {
            try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception) { }
        }

        private async System.Threading.Tasks.Task AiProbeAsync(bool fill, Label answer)
        {
            answer.Text = T("SettingsAiAsking", DazzleLmStudio.NormalizeEndpoint(textBoxOpenApiEndpoint.Text));
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
                string vision = T(p.Vision == true ? "SettingsAiSeesImages" : p.Vision == false ? "SettingsAiNoVision" : "SettingsAiVisionUnknown");
                answer.Text = !p.Reachable ? p.Reason
                    : (p.Model + (p.ContextLength is int n ? ", " + T("SettingsAiContextTokens", n.ToString("N0")) : "") + ", " + vision
                       + (fill ? "; " + T("SettingsAiListed", p.Listed.Count, p.Loaded?.Count ?? 0) : "")
                       + (p.Warning != null ? "\n" + p.Warning : "")
                       + "\n" + T("SettingsAiSuits"));
            }
            catch (Exception e) { answer.Text = T("SettingsAiUnreachable", e.Message); }
        }

        private string ModelValue()
        {
            if (comboAiModel.SelectedItem is ModelChoice m) return m.Id;
            string t = (comboAiModel.Text ?? "").Trim();
            int cut = t.IndexOf(LoadedSuffix, StringComparison.Ordinal);
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
