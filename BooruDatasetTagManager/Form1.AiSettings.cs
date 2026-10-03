using Dazzle.Layers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool: the main window's side of Settings > AI (2.15.0) -- applying what changed when the dialog closes
    /// (the portable switch, live; the per-image store move; skills, defaults, toggles), and the context that may go with a
    /// request: a Context dropdown on each AI strip for this session, and a line under each instruction saying what will be
    /// sent besides the image, the skill and the caption.
    /// </summary>
    public partial class MainForm
    {
        // ---------------------------------------------------------------- after the settings dialog

        private sealed class AiSnapshot
        {
            public bool Portable; public int Store; public bool House; public string RefineDefault, ChatDefault;
        }

        private AiSnapshot TakeAiSnapshot() => new AiSnapshot
        {
            Portable = DazzleData.IsPortable, Store = Program.Settings.DazzleConversationStore, House = Program.Settings.DazzleShowHouseSkills,
            RefineDefault = Program.Settings.DazzleRefineDefaultSkill, ChatDefault = Program.Settings.DazzleChatDefaultSkill,
        };

        /// <summary>Called when Settings closed with Save. Nothing here needs a restart.</summary>
        private void ApplyAiSettings(AiSnapshot before)
        {
            var notes = new List<string>();
            // the base, live (the person, 2026-10-02 20:02: "Live")
            if (Program.Settings.DazzleDataPortable != before.Portable)
                notes.AddRange(DazzleData.SwitchBase(Program.Settings.DazzleDataPortable, Program.Settings, Program.RecentFolders));
            Program.Settings.DazzleDataPortable = DazzleData.IsPortable;   // a refused switch leaves the setting telling the truth
            // the per-image store: move the open dataset's files so one copy exists (the MOVE rule, plan addendum 18:46)
            if (Program.Settings.DazzleConversationStore != before.Store)
            {
                string moved = MoveItemFiles(before.Store, Program.Settings.DazzleConversationStore);
                if (moved == null) Program.Settings.DazzleConversationStore = before.Store;   // the person said no: nothing moves, the choice stays
                else if (moved.Length > 0) notes.Add(moved);
            }
            // toggles that live on the strips too
            if (checkThink != null) { checkThink.Checked = Program.Settings.DazzleRefineThink; checkSchema.Checked = Program.Settings.DazzleRefineSchema; }
            if (chatThink != null) { chatThink.Checked = Program.Settings.DazzleRefineThink; chatToolsOn.Checked = Program.Settings.DazzleChatTools; chatAskFiles.Checked = Program.Settings.DazzleChatAskFiles; }
            ResetContextChoices();
            // skills: the lists follow the house toggle and a moved base; a changed default is selected now
            if (comboSkills != null)
            {
                string pick = Program.Settings.DazzleRefineDefaultSkill != before.RefineDefault && Program.Settings.DazzleRefineDefaultSkill.Length > 0 ? Program.Settings.DazzleRefineDefaultSkill : null;
                LoadSkillsList(pick);
            }
            if (comboChatSkills != null)
            {
                string pick = Program.Settings.DazzleChatDefaultSkill != before.ChatDefault && Program.Settings.DazzleChatDefaultSkill.Length > 0 ? Program.Settings.DazzleChatDefaultSkill : null;
                LoadChatSkillsList(pick);
            }
            UpdateContextLines();
            Program.Settings.SaveSettings();
            foreach (var n in notes) Log("settings: " + n);
            if (notes.Count > 0) statusLabel.Text = notes[notes.Count - 1];
        }

        /// <summary>
        /// Move the open dataset's per-image files from one store to another. Returns "" when there was nothing to move,
        /// the outcome sentence when files moved, or null when the person declined (asked only when there are files).
        /// Files of datasets that are not open stay where they are and are still found (ItemStore.Find reads every place).
        /// </summary>
        private string MoveItemFiles(int fromChoice, int toChoice)
        {
            if (Program.DataManager?.DataSet == null || Program.DataManager.DataSet.Count == 0 || string.IsNullOrEmpty(dazzleDatasetFolder)) return "";
            var from = DazzleData.ItemStore(fromChoice); var to = DazzleData.ItemStore(toChoice);
            var images = Program.DataManager.DataSet.Keys.ToList();
            int count = 0;
            foreach (var kind in DazzleData.ItemKinds)
                foreach (var img in images)
                    if (File.Exists(from.PathFor(from.Mode, img, kind, dazzleDatasetFolder))) count++;
            if (count == 0) return "";
            var answer = MessageBox.Show(this, "Move " + count + " per-image file" + (count == 1 ? "" : "s") + " of this dataset\nfrom: " + DazzleData.StoreChoices[fromChoice] + "\nto: " + DazzleData.StoreChoices[toChoice] + "?\n\nNo leaves the setting as it was.",
                "Move per-image files", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return null;
            int moved = 0; var problems = new List<string>();
            foreach (var kind in DazzleData.ItemKinds)
                foreach (var step in from.Migrate(from.Mode, to.Mode, images, kind, dazzleDatasetFolder))
                {
                    if (step.Moved) moved++; else problems.Add(Path.GetFileName(step.From) + ": " + step.Outcome);
                    Log("store move: " + step);
                }
            return "moved " + moved + " of " + count + " per-image files to " + DazzleData.StoreChoices[toChoice].ToLowerInvariant()
                   + (problems.Count > 0 ? "; not moved (" + problems.Count + "): " + string.Join("; ", problems.Take(3)) + (problems.Count > 3 ? "; ..." : "") : "");
        }

        // ---------------------------------------------------------------- context sent with a request

        // this session's choices: start from the settings, changed from the strip's Context dropdown, reset when Settings saves
        private bool refineSendRules, refineSendChecks, chatSendRules, chatSendChecks;
        private ToolStripDropDownButton refineContextButton, chatContextButton;
        private ToolStripMenuItem refineCtxRules, refineCtxChecks, chatCtxRules, chatCtxChecks;
        private Label labelRefineContext, labelChatContext;

        private void ResetContextChoices()
        {
            refineSendRules = Program.Settings.DazzleSendRulesRefine; refineSendChecks = Program.Settings.DazzleSendChecksRefine;
            chatSendRules = Program.Settings.DazzleSendRulesChat; chatSendChecks = Program.Settings.DazzleSendChecksChat;
            if (refineCtxRules != null) { refineCtxRules.Checked = refineSendRules; refineCtxChecks.Checked = refineSendChecks; }
            if (chatCtxRules != null) { chatCtxRules.Checked = chatSendRules; chatCtxChecks.Checked = chatSendChecks; }
        }

        private ToolStripDropDownButton MakeContextButton(string name, out ToolStripMenuItem rules, out ToolStripMenuItem checks, Action<bool, bool> changed)
        {
            var b = new ToolStripDropDownButton("Context") { Name = name, ToolTipText = "Context: what goes to the model besides the image, the skill and the caption -- the folder's rules and the Check for list, each with the framing sentence set on Settings > AI. Ticks here hold for this session; the defaults are on Settings > AI." };
            var r = new ToolStripMenuItem("Send the folder's rules") { CheckOnClick = true };
            var c = new ToolStripMenuItem("Send the Check for list") { CheckOnClick = true };
            r.CheckedChanged += (s, e) => { changed(r.Checked, c.Checked); UpdateContextLines(); };
            c.CheckedChanged += (s, e) => { changed(r.Checked, c.Checked); UpdateContextLines(); };
            b.DropDownItems.AddRange(new ToolStripItem[] { r, c });
            rules = r; checks = c;
            return b;
        }

        /// <summary>Adds the Context dropdowns and the "also sending" lines to the two AI panes; called once both exist.</summary>
        private void BuildContextControls()
        {
            ResetContextChoices();
            var refineStrip = panelRefine?.Controls.OfType<ToolStrip>().FirstOrDefault(t => t.Name == "refineStrip");
            if (refineStrip != null)
            {
                refineContextButton = MakeContextButton("refineContext", out refineCtxRules, out refineCtxChecks, (r, c) => { refineSendRules = r; refineSendChecks = c; });
                int at = refineStrip.Items.IndexOf(checkSchema) + 1;
                refineStrip.Items.Insert(at, refineContextButton);
                AttachBlockTip(refineStrip, refineContextButton, refineContextButton.ToolTipText);
                labelRefineContext = ContextLine("labelRefineContext");
                splitInstruction.Panel1.Controls.Add(labelRefineContext);
                textInstruction.TextChanged += (s, e) => UpdateContextLines();
            }
            var chatStrip = panelChat?.Controls.OfType<ToolStrip>().FirstOrDefault(t => t.Name == "chatStrip");
            if (chatStrip != null)
            {
                chatContextButton = MakeContextButton("chatContext", out chatCtxRules, out chatCtxChecks, (r, c) => { chatSendRules = r; chatSendChecks = c; });
                int at = chatStrip.Items.IndexOf(chatAskFiles) + 1;
                chatStrip.Items.Insert(at, chatContextButton);
                AttachBlockTip(chatStrip, chatContextButton, chatContextButton.ToolTipText);
                labelChatContext = ContextLine("labelChatContext");
                splitChatInstruction.Panel1.Controls.Add(labelChatContext);
                textChatInstruction.TextChanged += (s, e) => UpdateContextLines();
            }
            ResetContextChoices();
            if (textBoxCheck != null) textBoxCheck.TextChanged += (s, e) => UpdateContextLines();
            UpdateContextLines();
        }

        private Label ContextLine(string name) =>
            new Label { Name = name, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(3, 2, 3, 2), ForeColor = SystemColors.GrayText };

        private List<string> ActiveRuleLines() =>
            folderRules.Where(r => r.Error == null && !r.IsComment && !r.IsEmpty).Select(r => r.Line).ToList();

        private void UpdateContextLines()
        {
            int rules = ActiveRuleLines().Count, checks = DazzleContext.CheckCount(textBoxCheck?.Text);
            if (labelRefineContext != null)
                labelRefineContext.Text = DazzleContext.Describe(refineSendRules, rules, refineSendChecks, checks,
                    DazzleContext.SkillPlaces(textInstruction.Text, "rules"), DazzleContext.SkillPlaces(textInstruction.Text, "checks"));
            if (labelChatContext != null)
                labelChatContext.Text = DazzleContext.Describe(chatSendRules, rules, chatSendChecks, checks,
                    DazzleContext.SkillPlaces(textChatInstruction.Text, "rules"), DazzleContext.SkillPlaces(textChatInstruction.Text, "checks")).Replace("the caption", "the conversation");
        }

        /// <summary>The automatic context for a request, framed; "" when nothing is chosen. A piece the skill places itself is left out.</summary>
        private string ContextBlock(bool rules, bool checks, string skillText)
        {
            var parts = new List<string>();
            if (rules && !DazzleContext.SkillPlaces(skillText, "rules"))
            {
                string b = DazzleContext.RulesBlock(ActiveRuleLines(), Program.Settings.DazzleRulesFraming);
                if (b.Length > 0) parts.Add(b);
            }
            if (checks && !DazzleContext.SkillPlaces(skillText, "checks"))
            {
                string b = DazzleContext.ChecksBlock(textBoxCheck?.Text, Program.Settings.DazzleChecksFraming);
                if (b.Length > 0) parts.Add(b);
            }
            return string.Join("\n\n", parts);
        }

        private string RefineContextBlock() => ContextBlock(refineSendRules, refineSendChecks, textInstruction?.Text);
        private string ChatContextBlock() => ContextBlock(chatSendRules, chatSendChecks, textChatInstruction?.Text);
    }
}
