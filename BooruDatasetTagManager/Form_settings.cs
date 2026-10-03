using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace BooruDatasetTagManager
{
    public partial class Form_settings : Form
    {
        public Form_settings()
        {
            InitializeComponent();
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            Program.ColorManager.SchemeChanded += ColorManager_SchemeChanded;
            Program.Settings.TranslationLanguage ??= "en-US";
            textBoxOpenApiEndpoint.AutoCompleteSource = AutoCompleteSource.CustomSource;
        }
        private FontSettings gridFontSettings = null;
        private FontSettings autocompleteFontSettings = null;
        private void Form_settings_Load(object sender, EventArgs e)
        {
            comboBox1.DataSource = Program.Settings.AvaibleLanguages;
            comboBox1.DisplayMember = "Name";
            comboBox1.ValueMember = "Code";
            comboBox1.SelectedValue = Program.Settings.TranslationLanguage;
            checkBoxLoadOnlyManual.Checked = Program.Settings.OnlyManualTransInAutocomplete;
            comboBox2.Items.AddRange(Extensions.GetFriendlyEnumValues<TranslationService>());
            comboBox2.SelectedIndex = Extensions.GetEnumIndexFromValue<TranslationService>(Program.Settings.TransService.ToString());
            comboAutocompMode.Items.AddRange(Extensions.GetFriendlyEnumValues<AutocompleteMode>());
            comboAutocompMode.SelectedIndex = Extensions.GetEnumIndexFromValue<AutocompleteMode>(Program.Settings.AutocompleteMode.ToString());
            comboAutocompSort.Items.AddRange(Extensions.GetFriendlyEnumValues<AutocompleteSort>());
            comboAutocompSort.SelectedIndex = Extensions.GetEnumIndexFromValue<AutocompleteSort>(Program.Settings.AutocompleteSort.ToString());
            comboBoxColorScheme.Items.AddRange(Program.ColorManager.Items.Select(a => a.ToString()).ToArray());
            comboBoxColorScheme.SelectedItem = Program.Settings.ColorScheme;
            textBox1.Text = Program.Settings.SeparatorOnLoad;
            textBox2.Text = Program.Settings.SeparatorOnSave;
            textBox3.Text = Program.Settings.DefaultTagsFileExtension;
            textBox4.Text = Program.Settings.CaptionFileExtensions;
            numericUpDown1.Value = Program.Settings.PreviewSize;
            numericUpDown2.Value = Program.Settings.ShowAutocompleteAfterCharCount;
            CheckAskChange.Checked = Program.Settings.AskSaveChanges;
            checkBoxFixOnLoad.Checked = Program.Settings.FixTagsOnSaveLoad;
            AutoSortCheckBox.Checked = Program.Settings.AutoSort;
            checkBoxIncludeSubfolders.Checked = Program.Settings.IncludeSubfolders;
            comboBoxTagMatch.SelectedIndex = (int)Program.Settings.DazzleTagMatch;
            checkBoxDazzleLayout.Checked = Program.Settings.DazzleLayout;
            //UI
            checkBoxCacheImages.Checked = Program.Settings.CacheOpenImages;
            comboBoxEndOfFolder.SelectedIndex = (int)Program.Settings.DazzleEndOfFolder;
            checkBoxRememberFolders.Checked = Program.Settings.DazzleRememberFolders;
            checkBoxReopenLastFolder.Checked = Program.Settings.DazzleReopenLastFolder;
            textBoxComfydbgPath.Text = Program.Settings.DazzleComfydbgPath;
            // the fork's rows run past the dialog's height; the tab (a third-party Tab, no AutoScroll of its own) gets a
            // scrolling panel that takes over its controls, so the person scrolls rather than resizes (djdarcy, 2026-10-02)
            if (!(tabUI.Controls.Count == 1 && tabUI.Controls[0].Name == "panelUiScroll"))
            {
                var scroller = new System.Windows.Forms.Panel { Name = "panelUiScroll", Dock = System.Windows.Forms.DockStyle.Fill, AutoScroll = true };
                var items = tabUI.Controls.Cast<System.Windows.Forms.Control>().ToList();
                foreach (var c in items) { tabUI.Controls.Remove(c); scroller.Controls.Add(c); }
                tabUI.Controls.Add(scroller);
            }
            numericRefineMaxTokens.Value = Math.Max(numericRefineMaxTokens.Minimum, Math.Min(numericRefineMaxTokens.Maximum, Program.Settings.DazzleRefineMaxTokens));
            numericRefineTemperature.Value = Math.Max(0, Math.Min(numericRefineTemperature.Maximum, (decimal)Program.Settings.DazzleRefineTemperature));
            numericRefineImageSide.Value = Math.Max(numericRefineImageSide.Minimum, Math.Min(numericRefineImageSide.Maximum, Program.Settings.DazzleRefineImageLongSide));
            numericUpDown3.Value = Program.Settings.GridViewRowHeight;
            label11.Text = Program.Settings.GridViewFont.ToString();
            gridFontSettings = Program.Settings.GridViewFont;
            label14.Text = Program.Settings.AutocompleteFont.ToString();
            autocompleteFontSettings = Program.Settings.AutocompleteFont;
            comboBoxLanguage.Items.AddRange(I18n.GetLanguages().ToArray());
            comboBoxLanguage.SelectedItem = Program.Settings.Language;
            comboBoxPreviewType.Items.AddRange(Extensions.GetFriendlyEnumValues<ImagePreviewType>());
            comboBoxPreviewType.SelectedIndex = Extensions.GetEnumIndexFromValue<ImagePreviewType>(Program.Settings.PreviewType.ToString());
            //hotkeys
            foreach (var item in Program.Settings.Hotkeys.Items)
            {
                dataGridViewHotkeys.Rows.Add(item.Id, item.Text, item.GetHotkeyString());
            }
            //Interrogator
            textBoxAiApiEndpoint.Text = Program.Settings.AutoTagger.ConnectionAddress;
            if (string.IsNullOrEmpty(Program.Settings.AutoTagger.CustomSystemPrompt))
            {
                checkBoxCustomPrompt.Checked = false;
                textBoxCustomPrompt.Enabled = false;
            }
            else
            {
                checkBoxCustomPrompt.Checked = true;
                textBoxCustomPrompt.Enabled = true;
                textBoxCustomPrompt.Text = Program.Settings.AutoTagger.CustomSystemPrompt;
            }
            textBoxOpenApiEndpoint.Text = Program.Settings.OpenAiAutoTagger.ConnectionAddress;
            textBoxOpenAiApiKey.Text = Program.Settings.OpenAiAutoTagger.ApiKey;
            numericUpDownOpenAiTimeout.Value = Program.Settings.OpenAiAutoTagger.RequestTimeout;
            BuildAiTab();   // Simple-AI-Tag-Tool: Settings > AI (Form_settings.Ai.cs)

            SwitchLanguage();

        }

        private void ColorManager_SchemeChanded(object sender, EventArgs e)
        {
            if (Program.ColorManager.SelectedScheme != null)
            {
                Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
                Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            }
        }

        /// <summary>Simple-AI-Tag-Tool: set when Save changed the subfolder option, so the main window reloads the dataset.</summary>
        public bool SubfoldersChanged { get; private set; }

        private async void BtnSave_Click(object sender, EventArgs e)
        {
            // Simple-AI-Tag-Tool: upstream showed the "restart" notice for every save. Only the
            // settings that really need a restart warn now; the subfolder option is applied by
            // reloading the dataset instead.
            bool restartNeeded = (string)comboBoxLanguage.SelectedItem != Program.Settings.Language
                                 || checkBoxDazzleLayout.Checked != Program.Settings.DazzleLayout
                                 || (int)numericUpDown1.Value != Program.Settings.PreviewSize;
            SubfoldersChanged = checkBoxIncludeSubfolders.Checked != Program.Settings.IncludeSubfolders;
            if (restartNeeded)
                MessageBox.Show(I18n.GetText("TipSaveSettings"));
            Program.Settings.PreviewSize = (int)numericUpDown1.Value;
            Program.Settings.ShowAutocompleteAfterCharCount = (int)numericUpDown2.Value;
            Program.Settings.TranslationLanguage = (string)comboBox1.SelectedValue;
            //Program.Settings.TransService = (TranslationService)Enum.Parse(typeof(TranslationService), comboBox2.SelectedItem.ToString(), true);
            Program.Settings.TransService = Extensions.GetEnumItemFromFriendlyText<TranslationService>(comboBox2.SelectedItem.ToString());
            Program.Settings.OnlyManualTransInAutocomplete = checkBoxLoadOnlyManual.Checked;
            Program.Settings.AutocompleteMode = Extensions.GetEnumItemFromFriendlyText<AutocompleteMode>(comboAutocompMode.SelectedItem.ToString());
            Program.Settings.AutocompleteSort = Extensions.GetEnumItemFromFriendlyText<AutocompleteSort>(comboAutocompSort.SelectedItem.ToString());
            Program.Settings.FixTagsOnSaveLoad = checkBoxFixOnLoad.Checked;
            Program.Settings.SeparatorOnLoad = textBox1.Text;
            Program.Settings.SeparatorOnSave = textBox2.Text;
            Program.Settings.DefaultTagsFileExtension = textBox3.Text;
            Program.Settings.CaptionFileExtensions = textBox4.Text;
            Program.Settings.AskSaveChanges = CheckAskChange.Checked;
            Program.Settings.AutoSort = AutoSortCheckBox.Checked;
            Program.Settings.IncludeSubfolders = checkBoxIncludeSubfolders.Checked;
            Program.Settings.DazzleTagMatch = (DazzleRules.TagMatch)Math.Max(0, comboBoxTagMatch.SelectedIndex);
            Program.Settings.DazzleLayout = checkBoxDazzleLayout.Checked;
            //UI
            Program.Settings.CacheOpenImages = checkBoxCacheImages.Checked;
            Program.Settings.DazzleEndOfFolder = (EndOfFolderAction)Math.Max(0, comboBoxEndOfFolder.SelectedIndex);
            Program.Settings.DazzleRememberFolders = checkBoxRememberFolders.Checked;
            Program.Settings.DazzleReopenLastFolder = checkBoxReopenLastFolder.Checked;
            Program.Settings.DazzleComfydbgPath = string.IsNullOrWhiteSpace(textBoxComfydbgPath.Text) ? "comfydbg" : textBoxComfydbgPath.Text.Trim();
            Program.Settings.DazzleRefineMaxTokens = (int)numericRefineMaxTokens.Value;
            Program.Settings.DazzleRefineTemperature = (float)numericRefineTemperature.Value;
            Program.Settings.DazzleRefineImageLongSide = (int)numericRefineImageSide.Value;
            if (!checkBoxRememberFolders.Checked)
                Program.RecentFolders?.Clear(); // "don't remember" means forget what is already stored
            Program.Settings.GridViewRowHeight = (int)numericUpDown3.Value;
            Program.Settings.GridViewFont = gridFontSettings;
            Program.Settings.AutocompleteFont = autocompleteFontSettings;
            Program.Settings.Language = (string)comboBoxLanguage.SelectedItem;
            Program.Settings.ColorScheme = (string)comboBoxColorScheme.SelectedItem;
            Program.ColorManager.SelectScheme(Program.Settings.ColorScheme);
            Program.Settings.PreviewType = Extensions.GetEnumItemFromFriendlyText<ImagePreviewType>(comboBoxPreviewType.SelectedItem.ToString());
            //hotkeys
            if (tempHotkeys.Count > 0)
            {
                foreach (var item in tempHotkeys)
                {
                    Program.Settings.Hotkeys[item.Key] = item.Value;
                }
            }
            //Interrogator
            Program.Settings.AutoTagger.ConnectionAddress = textBoxAiApiEndpoint.Text;
            if (checkBoxCustomPrompt.Checked)
            {
                Program.Settings.AutoTagger.CustomSystemPrompt = textBoxCustomPrompt.Text;
            }
            else
            {
                Program.Settings.AutoTagger.CustomSystemPrompt = "";
            }
            if (Program.AutoTagger != null && Program.AutoTagger.IsConnected)
            {
                await Program.AutoTagger.SetCustomSystemPrompt(Program.Settings.AutoTagger.CustomSystemPrompt);
            }
            Program.Settings.OpenAiAutoTagger.ConnectionAddress = textBoxOpenApiEndpoint.Text;
            Program.Settings.OpenAiAutoTagger.ApiKey = textBoxOpenAiApiKey.Text;
            Program.Settings.OpenAiAutoTagger.RequestTimeout = (int)numericUpDownOpenAiTimeout.Value;
            if (!string.IsNullOrEmpty(Program.Settings.OpenAiAutoTagger.ConnectionAddress) && !string.IsNullOrEmpty(Program.Settings.OpenAiAutoTagger.ApiKey))
            {
                try
                {
                    Program.OpenAiAutoTagger = new AiApi.AiOpenAiClient(Program.Settings.OpenAiAutoTagger.ConnectionAddress, Program.Settings.OpenAiAutoTagger.ApiKey, Program.Settings.OpenAiAutoTagger.RequestTimeout);
                }
                catch { }
            }

            SaveAiTab();   // Simple-AI-Tag-Tool: Settings > AI (Form_settings.Ai.cs)
            Program.Settings.SaveSettings();
            DialogResult = DialogResult.OK;
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void BtnGridviewFontChange_Click(object sender, EventArgs e)
        {
            FontDialog dialog = new FontDialog();
            dialog.Font = gridFontSettings.GetFont();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                gridFontSettings = FontSettings.Create(dialog.Font);
                label11.Text = gridFontSettings.ToString();
            }
            dialog.Dispose();
        }

        private void BtnAutocompFontChange_Click(object sender, EventArgs e)
        {
            FontDialog dialog = new FontDialog();
            dialog.Font = autocompleteFontSettings.GetFont();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                autocompleteFontSettings = FontSettings.Create(dialog.Font);
                label14.Text = autocompleteFontSettings.ToString();
            }
            dialog.Dispose();
        }

        private void SwitchLanguage()
        {
            this.Text = I18n.GetText("MenuLabelSettings");
            SettingFrame.Tabs[0].Text = I18n.GetText("SettingTabGeneral");
            SettingFrame.Tabs[1].Text = I18n.GetText("SettingTabUI");
            SettingFrame.Tabs[2].Text = I18n.GetText("SettingTabTranslations");
            LabelPreviewImageSize.Text = I18n.GetText("SettingPreviewImageSize");
            LabelAutocompMode.Text = I18n.GetText("SettingAutocompMode");
            LabelAutocompFont.Text = I18n.GetText("SettingAutocompFont");
            LabelAutocompSort.Text = I18n.GetText("SettingAutocompSort");
            LabelAutocompAfter.Text = I18n.GetText("SettingAutocompPrefix");
            LabelChars.Text = I18n.GetText("SettingAutocompChars");
            LabelLanguage.Text = I18n.GetText("SettingUILanguage");
            LabelSeparatorLoad.Text = I18n.GetText("SettingSeperatorLoad");
            LabelSeparatorSave.Text = I18n.GetText("SettingSeperatorSave");
            LabelTagFont.Text = I18n.GetText("SettingUITagFont");
            LabelTagHeight.Text = I18n.GetText("SettingUIRowHeight");
            CheckAskChange.Text = I18n.GetText("SettingPromptToSave");
            checkBoxFixOnLoad.Text = I18n.GetText("SettingFixTagLoad");
            AutoSortCheckBox.Text = I18n.GetText("SettingAutoSortCheck");
            BtnSave.Text = I18n.GetText("SettingBtnSave");
            BtnCancel.Text = I18n.GetText("SettingBtnCancel");
            BtnGridviewFontChange.Text = I18n.GetText("SettingBtnChange");
            BtnAutocompFontChange.Text = I18n.GetText("SettingBtnChange");
            labelDelExt.Text = I18n.GetText("SettingDefExt");
            labelCaptionFileExt.Text = I18n.GetText("SettingCaptionExt");
            labelColorScheme.Text = I18n.GetText("SettingColorScheme");
            labelPreviewLocation.Text = I18n.GetText("SettingPreviewLocation");
            tabHotkeys.Text = I18n.GetText("SettingHotkeysTab");
            labelHotkeysHelp.Text = I18n.GetText("SettingHotkeysHelpText");
            dataGridViewHotkeys.Columns["Command"].HeaderText = I18n.GetText("SettingHotkeysCommandColumn");
            dataGridViewHotkeys.Columns["Hotkey"].HeaderText = I18n.GetText("SettingHotkeysHotkeyColumn");
            labelTransLang.Text = I18n.GetText("SettingTranslationLang");
            labelTranslService.Text = I18n.GetText("SettingTranslationSrv");
            checkBoxLoadOnlyManual.Text = I18n.GetText("SettingLoadOnlyManualAutocomplete");
            checkBoxCacheImages.Text = I18n.GetText("SettingsCheckBoxCacheImages");
            LabelApApiEndpoint.Text = I18n.GetText("SettingsInterrogatorAddress");
            labelOpenAiEndpoint.Text = I18n.GetText("SettingsInterrogatorAddress");
            checkBoxCustomPrompt.Text = I18n.GetText("SettingsCheckBoxCustomPrompt");
            labelOpenAiApiKey.Text = I18n.GetText("SettingsOpenAiApiKey");
            labelOpenAiTimeout.Text = I18n.GetText("SettingsOpenAiRequestTimeout");

            comboAutocompMode.Items.Clear();
            comboAutocompSort.Items.Clear();
            comboBox2.Items.Clear();

            comboBox2.Items.AddRange(Extensions.GetFriendlyEnumValues<TranslationService>());
            comboBox2.SelectedIndex = Extensions.GetEnumIndexFromValue<TranslationService>(Program.Settings.TransService.ToString());
            comboAutocompMode.Items.AddRange(Extensions.GetFriendlyEnumValues<AutocompleteMode>());
            comboAutocompMode.SelectedIndex = Extensions.GetEnumIndexFromValue<AutocompleteMode>(Program.Settings.AutocompleteMode.ToString());
            comboAutocompSort.Items.AddRange(Extensions.GetFriendlyEnumValues<AutocompleteSort>());
            comboAutocompSort.SelectedIndex = Extensions.GetEnumIndexFromValue<AutocompleteSort>(Program.Settings.AutocompleteSort.ToString());

            Program.Settings.Hotkeys.ChangeLanguage();
            SwitchLanguageDazzle();   // Simple-AI-Tag-Tool: the fork's labels on these tabs (Form_settings.Ai.cs)
        }
        bool isControlKeyPressed = false;
        Dictionary<string, HotkeyItem> tempHotkeys = new Dictionary<string, HotkeyItem>();
        private void dataGridView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (dataGridViewHotkeys.SelectedRows.Count == 0)
                return;
            if (e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.Menu)
            {
                string id = (string)dataGridViewHotkeys.SelectedRows[0].Cells["CmdId"].Value;
                var hkItem = (HotkeyItem)Program.Settings.Hotkeys[id].Clone();
                hkItem.KeyData = e.KeyCode;
                hkItem.IsCtrl = e.Control;
                hkItem.IsAlt = e.Alt;
                hkItem.IsShift = e.Shift;
                tempHotkeys[id] = hkItem;
                dataGridViewHotkeys.SelectedRows[0].Cells["Hotkey"].Value = hkItem.GetHotkeyString();
            }
            e.SuppressKeyPress = true;
        }

        private void checkBoxCustomPrompt_CheckedChanged(object sender, EventArgs e)
        {
            textBoxCustomPrompt.Enabled = checkBoxCustomPrompt.Checked;
        }
    }
}
