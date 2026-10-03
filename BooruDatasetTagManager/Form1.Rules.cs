using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    // Simple-AI-Tag-Tool: the Rules pane above the Check for box. One row per rule
    // ("If" condition, "Then" tags, read-only "Result"), stored as one
    // "condition => tags" line each in the dataset folder's satt-rules.json.
    // Design: 2026-10-02__02-16-46__dev-workflow-process__satt-conditional-rules.md
    public partial class MainForm
    {
        private const string RulesFileName = "satt-rules.json";
        private SplitContainer splitRules;          // rules grid on top, Check for below
        private DataGridView gridRules;
        private Label labelRules;
        private List<DazzleRules.Rule> folderRules = new List<DazzleRules.Rule>();
        private string rulesFilePath;               // null until a folder is loaded
        private bool rulesDirty, rulesLoading;

        private static readonly Color DormantBack = Color.FromArgb(235, 235, 235);
        private static readonly Color ConflictBack = Color.FromArgb(255, 224, 178);

        /// <summary>Build the grid and the split that puts it above the Check for controls.</summary>
        private SplitContainer BuildRulesPane(Control checkLabel, Control checkBox)
        {
            gridRules = new DataGridView
            {
                Name = "gridRules", Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true,
                AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false, TabStop = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window,
                Font = Program.Settings.GridViewFont.GetFont(), EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            };
            // rows read like the tag grid (same font and height); the header is a band, not a row
            var font = Program.Settings.GridViewFont.GetFont();
            gridRules.DefaultCellStyle.Font = font;
            gridRules.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            gridRules.RowTemplate.Height = Math.Max(Program.Settings.GridViewRowHeight, font.Height + 8);
            gridRules.ColumnHeadersDefaultCellStyle.Font = new Font(font, FontStyle.Bold);
            gridRules.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.ControlLight;
            gridRules.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
            gridRules.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            gridRules.ColumnHeadersHeight = font.Height + 10;
            gridRules.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            gridRules.Columns.Add(new DataGridViewTextBoxColumn { Name = "If", HeaderText = "If", FillWeight = 40 });
            gridRules.Columns.Add(new DataGridViewTextBoxColumn { Name = "Then", HeaderText = "Then", FillWeight = 35 });
            gridRules.Columns.Add(new DataGridViewTextBoxColumn { Name = "Result", HeaderText = "Result", FillWeight = 25, ReadOnly = true });
            gridRules.Columns["Result"].DefaultCellStyle.ForeColor = SystemColors.GrayText;
            gridRules.CellEndEdit += (s, e) => { if (!rulesLoading) { RebuildRulesFromGrid(); ScheduleRecolor(); } };
            gridRules.UserDeletedRow += (s, e) => { if (!rulesLoading) { RebuildRulesFromGrid(); ScheduleRecolor(); } };
            gridRules.CellToolTipTextNeeded += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < gridRules.Rows.Count && gridRules.Rows[e.RowIndex].Tag is DazzleRules.Rule r && r.Error != null)
                    e.ToolTipText = r.Error + " (column " + r.ErrorColumn + ")";
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Add missing tags to caption", null, (s, e) => AddMissingTagsFromRule());
            menu.Items.Add("Delete rule", null, (s, e) => { if (gridRules.CurrentRow != null && !gridRules.CurrentRow.IsNewRow) { gridRules.Rows.Remove(gridRules.CurrentRow); RebuildRulesFromGrid(); ScheduleRecolor(); } });
            gridRules.ContextMenuStrip = menu;

            labelRules = new Label { Text = RulesHint(0, ""), Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2, 4, 2, 2) };

            splitRules = new SplitContainer { Name = "splitRules", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 5 };
            splitRules.Panel1.Controls.Add(gridRules);
            splitRules.Panel1.Controls.Add(labelRules);
            splitRules.Panel2.Controls.Add(checkBox);
            splitRules.Panel2.Controls.Add(checkLabel);
            return splitRules;
        }

        private static string RulesHint(int count, string note)
        {
            return "Rules for this folder (" + RulesFileName + "): " + count + note +
                   "     If: tags with  !  &  |  ( )  and  ~\"regex\"     Then: tag, -tag     (type in an empty row to add one)";
        }

        // --- file ----------------------------------------------------------------------

        /// <summary>Read the folder's rules file (if any) into the grid. Called when a dataset loads.</summary>
        private void LoadFolderRules(string folder)
        {
            if (gridRules == null) return;
            rulesFilePath = Path.Combine(folder, RulesFileName);
            var rules = new List<DazzleRules.Rule>();
            string note = "";
            try
            {
                if (File.Exists(rulesFilePath))
                {
                    var doc = JObject.Parse(File.ReadAllText(rulesFilePath));
                    int version = doc["version"]?.Type == JTokenType.Integer ? (int)doc["version"] : 1;
                    if (version > 1) note = "  (file is version " + version + "; this build knows 1)";
                    foreach (var line in (doc["rules"] as JArray ?? new JArray()).Select(t => t.ToString()))
                        rules.Add(DazzleRules.ParseLine(line));
                }
            }
            catch (Exception e)
            {
                note = "  (could not read " + RulesFileName + ": " + e.Message + ")";
            }
            folderRules = rules;
            FillRulesGrid();
            rulesDirty = false;
            UpdateContextLines();
            labelRules.Text = RulesHint(rules.Count(r => !r.IsEmpty && !r.IsComment), note);
        }

        /// <summary>Write the rules if they changed: atomically, lines verbatim, no file when there are no rules.</summary>
        private void CommitRules()
        {
            if (gridRules == null || !rulesDirty || rulesFilePath == null) return;
            if (gridRules.IsCurrentCellInEditMode) gridRules.EndEdit();
            var lines = folderRules.Select(r => r.Line).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            try
            {
                if (lines.Count == 0)
                {
                    if (File.Exists(rulesFilePath)) File.Delete(rulesFilePath);
                }
                else
                {
                    var doc = new JObject { ["version"] = 1, ["rules"] = new JArray(lines) };
                    string tmp = rulesFilePath + ".tmp";
                    File.WriteAllText(tmp, doc.ToString(Newtonsoft.Json.Formatting.Indented));
                    if (File.Exists(rulesFilePath)) File.Replace(tmp, rulesFilePath, null); else File.Move(tmp, rulesFilePath);
                }
                rulesDirty = false;
                SetStatus("Rules saved to " + RulesFileName);
            }
            catch (Exception e)
            {
                SetStatus("Could not save " + RulesFileName + ": " + e.Message);
            }
        }

        // --- grid <-> rules -------------------------------------------------------------

        private void FillRulesGrid()
        {
            rulesLoading = true;
            gridRules.Rows.Clear();
            foreach (var rule in folderRules)
            {
                int i = gridRules.Rows.Add(rule.ConditionText, rule.ThenText, "");
                gridRules.Rows[i].Tag = rule;
            }
            rulesLoading = false;
        }

        private void RebuildRulesFromGrid()
        {
            var rules = new List<DazzleRules.Rule>();
            foreach (DataGridViewRow row in gridRules.Rows)
            {
                if (row.IsNewRow) continue;
                string cond = row.Cells["If"].Value?.ToString() ?? "", then = row.Cells["Then"].Value?.ToString() ?? "";
                var rule = DazzleRules.ParseLine(DazzleRules.Compose(cond, then));
                row.Tag = rule;
                rules.Add(rule);
            }
            folderRules = rules;
            rulesDirty = true;
            UpdateContextLines();
            labelRules.Text = RulesHint(rules.Count(r => !r.IsEmpty && !r.IsComment), "  (unsaved)");
        }

        // --- evaluation ----------------------------------------------------------------

        /// <summary>Evaluate the folder's rules against the current image's tags and colour the grid. Null when there are none.</summary>
        private DazzleRules.Evaluation EvaluateRulesForImage(DazzleRules.TagSet tags, bool haveImage)
        {
            if (gridRules == null || folderRules.Count == 0) return null;
            var ev = haveImage ? DazzleRules.Evaluate(folderRules, tags) : null;
            foreach (DataGridViewRow row in gridRules.Rows)
            {
                if (row.IsNewRow || !(row.Tag is DazzleRules.Rule rule)) continue;
                var cell = row.Cells["Result"];
                Color back = SystemColors.Window; string text = "";
                if (rule.Error != null) { back = BadBack; text = "error: " + rule.Error; }
                else if (rule.IsComment || rule.IsEmpty) { back = DormantBack; }
                else if (ev != null)
                {
                    var rr = ev.Rules.FirstOrDefault(x => ReferenceEquals(x.Rule, rule));
                    if (rr != null)
                    {
                        text = DazzleRules.Describe(rr);
                        back = !rr.Active ? DormantBack : rr.HasConflict ? ConflictBack : rr.HasProblem ? BadBack : GoodBack;
                    }
                }
                cell.Value = text;
                row.DefaultCellStyle.BackColor = back;
            }
            return ev;
        }

        /// <summary>Right-click action: append the active rule's missing wanted tags to the caption, once, as one edit.</summary>
        private void AddMissingTagsFromRule()
        {
            if (gridRules?.CurrentRow?.Tag is DazzleRules.Rule rule && rule.Error == null && textBoxTags != null && textBoxTags.Enabled)
            {
                var tags = new DazzleRules.TagSet(Tokenize(textBoxTags.Text, TagSeparators()).Select(r => r.Text), Program.Settings.DazzleTagMatch);
                if (!rule.Condition.Eval(tags)) { SetStatus("That rule is dormant on this image"); return; }
                var missing = rule.Consequences.Where(c => c.Wanted && !tags.Has(c.Term)).Select(c => c.Raw).ToList();
                if (missing.Count == 0) { SetStatus("Nothing missing for that rule"); return; }
                string sep = Program.Settings.SeparatorOnSave.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
                string existing = textBoxTags.Text.TrimEnd();
                textBoxTags.Text = (existing.Length == 0 ? "" : existing.TrimEnd(',') + sep) + string.Join(sep, missing);
                CommitTagsTextBox();
            }
        }
    }
}
