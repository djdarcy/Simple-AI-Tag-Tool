using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>What Space does after the last image (Dazzle fork).</summary>
    public enum EndOfFolderAction
    {
        Loop,
        Stop,
        Ask
    }

    /// <summary>
    /// IrfanView-style "End of folder reached" chooser: the current folder (selected,
    /// so pressing Space or Right again just loops), the parent, and each subfolder, with
    /// image counts. Space/Right/Enter/double-click uses the highlighted folder, Left lists
    /// the folder one level up.
    /// Built in code, without a designer file, to stay a single small file in the fork.
    /// </summary>
    public class Form_DazzleNextFolder : Form
    {
        private readonly TextBox textBoxFolder;
        private readonly ListView listFolders;
        private string browseFolder;

        /// <summary>The folder the user chose; valid when the dialog returns OK.</summary>
        public string SelectedFolder { get; private set; }

        /// <param name="atStart">true when the user went back past the first image, not forward past the last</param>
        public Form_DazzleNextFolder(string currentFolder, bool atStart = false)
        {
            Text = "Browse folders";
            FormBorderStyle = FormBorderStyle.Sizable;   // resizable; the size is remembered
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            MinimumSize = new Size(420, 300);
            ClientSize = Program.Settings.DazzleBrowseWidth > 0 && Program.Settings.DazzleBrowseHeight > 0
                ? new Size(Program.Settings.DazzleBrowseWidth, Program.Settings.DazzleBrowseHeight) : new Size(560, 380);
            FormClosing += (s, e) => { Program.Settings.DazzleBrowseWidth = ClientSize.Width; Program.Settings.DazzleBrowseHeight = ClientSize.Height; };

            var labelTop = new Label
            {
                Text = (atStart ? "Start" : "End") + " of folder reached. Do you want to continue in another folder?",
                AutoSize = true,
                Location = new Point(10, 10)
            };
            var labelIn = new Label { Text = "You are in folder:", AutoSize = true, Location = new Point(10, 36) };
            textBoxFolder = new TextBox { ReadOnly = true, Location = new Point(10, 54), Width = ClientSize.Width - 20, TabStop = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            listFolders = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Location = new Point(10, 86),
                Size = new Size(ClientSize.Width - 130, ClientSize.Height - 130),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            listFolders.Columns.Add("Select folder:", 320);
            listFolders.Columns.Add("Images", 80, HorizontalAlignment.Right);
            listFolders.KeyDown += ListFolders_KeyDown;
            listFolders.DoubleClick += (s, e) => UseSelected();

            var buttonUse = new Button { Text = "Use folder", Location = new Point(ClientSize.Width - 110, 86), Size = new Size(100, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            buttonUse.Click += (s, e) => UseSelected();
            var buttonCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(ClientSize.Width - 110, 120), Size = new Size(100, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var labelKeys = new Label
            {
                Text = "Space / Right / Backspace / Enter = use folder     Up / Down = choose     Left = go up a folder level",
                AutoSize = true,
                Location = new Point(10, ClientSize.Height - 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            Controls.AddRange(new Control[] { labelTop, labelIn, textBoxFolder, listFolders, buttonUse, buttonCancel, labelKeys });
            AcceptButton = buttonUse;
            CancelButton = buttonCancel;

            Browse(currentFolder);
            // keys must land on the list, not a button, so Space / Right act on the highlighted folder
            Shown += (s, e) => listFolders.Focus();
        }

        private void Browse(string folder)
        {
            browseFolder = Path.GetFullPath(folder);
            textBoxFolder.Text = browseFolder;
            listFolders.BeginUpdate();
            listFolders.Items.Clear();
            AddEntry("(.) - Current folder", browseFolder);
            var parent = Directory.GetParent(browseFolder);
            if (parent != null)
                AddEntry("(..)", parent.FullName);
            try
            {
                foreach (var dir in Directory.GetDirectories(browseFolder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    AddEntry("(" + Path.GetFileName(dir) + ")", dir);
            }
            catch (Exception)
            {
                // unreadable folder: still offer current and parent
            }
            listFolders.Items[0].Selected = true;
            listFolders.Items[0].Focused = true;
            listFolders.EndUpdate();
            listFolders.Select();
        }

        private void AddEntry(string label, string path)
        {
            var item = new ListViewItem(new[] { label, CountImages(path) });
            item.Tag = path;
            listFolders.Items.Add(item);
        }

        private static string CountImages(string folder)
        {
            try
            {
                var allowed = Extensions.ImageExtensions.Concat(Extensions.VideoExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return Directory.EnumerateFiles(folder).Count(f => allowed.Contains(Path.GetExtension(f))).ToString();
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private string SelectedPath()
        {
            return listFolders.SelectedItems.Count == 1 ? (string)listFolders.SelectedItems[0].Tag : null;
        }

        private void UseSelected()
        {
            string path = SelectedPath();
            if (path == null)
                return;
            SelectedFolder = path;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // A key still held from the main window (the one that opened this dialog)
            // must not answer it: only a fresh press of Space / Right / Backspace / Enter counts.
            bool autoRepeat = msg.Msg == 0x0100 && ((long)msg.LParam & 0x40000000) != 0;
            if (autoRepeat && (keyData == Keys.Space || keyData == Keys.Right || keyData == Keys.Back || keyData == Keys.Enter))
                return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ListFolders_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Space:
                case Keys.Enter:
                case Keys.Right: // "keep going": Right, Right, Right loops like Space does
                case Keys.Back:  // ...and Backspace, Backspace, Backspace loops the other way
                    UseSelected();
                    e.Handled = e.SuppressKeyPress = true;
                    break;
                case Keys.Left:
                    var parent = Directory.GetParent(browseFolder);
                    if (parent != null)
                        Browse(parent.FullName);
                    e.Handled = true;
                    break;
            }
        }
    }
}
