using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool (#5, #8; slice 2): each image keeps its own AI Chat conversation and its own AI Refine run, as two
    /// files in LM Studio's conversation shape (DazzleConversationFile), stored where Settings > AI says (beside the image,
    /// the dataset's .satt folder, or the program's store). Several conversations are alive at once: moving to another
    /// image puts the current one away and brings that image's back. A chat on an image that was refined starts from the
    /// Refine run. Export puts either file into LM Studio's own folder; Import takes one back as this image's chat.
    /// </summary>
    public partial class MainForm
    {
        private sealed class ChatSlot { public JArray History; public string SystemText; public string ImageSentFor; public int Tokens; public string Model = ""; public string Root; }

        private readonly Dictionary<string, ChatSlot> chatSlots = new Dictionary<string, ChatSlot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DazzleConversation> refineRuns = new Dictionary<string, DazzleConversation>(StringComparer.OrdinalIgnoreCase);
        private string chatSlotPath;        // the image the chat fields (chatHistory, ...) belong to now
        private string chatSlotRoot;        // the dataset folder that image was opened in (adjacent files are relative to it)
        private string pendingSlotPath;     // an image change that arrived during a chat run; applied when the run ends
        private string chatLastModel = "";

        private static bool KeepConversations => Program.Settings.DazzleKeepConversations;
        private Dazzle.Layers.ItemStore ConversationStore => DazzleData.ItemStore(Program.Settings.DazzleConversationStore);

        private string FindConversationFile(string image, string kind)
        {
            if (!KeepConversations || string.IsNullOrEmpty(image)) return null;
            try { return ConversationStore.Find(image, kind, dazzleDatasetFolder).Select(f => f.path).FirstOrDefault(); } catch (Exception) { return null; }
        }

        // ---------------------------------------------------------------- the image changed

        /// <summary>Called when the current image changes (the info pane's hook). Puts the current chat away and brings the new image's.</summary>
        private void SwitchConversationsTo(string image)
        {
            if (string.IsNullOrEmpty(image) || string.Equals(image, chatSlotPath, StringComparison.OrdinalIgnoreCase)) { LoadRefineRun(image); return; }
            if (chatCts != null) { pendingSlotPath = image; return; }   // a turn is running for the old image: switch when it ends
            StashChat();
            chatSlotPath = image; chatSlotRoot = dazzleDatasetFolder;
            LoadRefineRun(image);
            if (chatSlots.TryGetValue(image, out var slot)) RestoreChat(slot);
            else
            {
                var file = FindConversationFile(image, "chat");
                DazzleConversation c = null;
                if (file != null) { try { c = DazzleConversationFile.Load(file); } catch (Exception e) { Log("could not read " + file + ": " + e.Message); } }
                if (c != null && c.Messages.Count > 0)
                    RestoreChat(new ChatSlot { History = c.Messages, SystemText = c.SystemText, ImageSentFor = null, Tokens = c.TokenCount, Model = c.Model });
                else { chatHistory = null; chatSystemText = null; chatImageSentFor = null; chatLastTotalTokens = 0; if (transcript != null) transcript.Clear(); }
            }
            if (panelChat != null && panelChat.Visible && chatHistory == null) StartChatSession();
            UpdateContextMeter();
        }

        /// <summary>The current chat into the per-image table, and to disk.</summary>
        private void StashChat()
        {
            if (string.IsNullOrEmpty(chatSlotPath)) return;
            if (chatHistory != null)
                chatSlots[chatSlotPath] = new ChatSlot { History = chatHistory, SystemText = chatSystemText, ImageSentFor = chatImageSentFor, Tokens = chatLastTotalTokens, Model = chatLastModel, Root = chatSlotRoot };
            SaveChat(chatSlotPath);
        }

        private void RestoreChat(ChatSlot s)
        {
            chatHistory = s.History; chatSystemText = s.SystemText; chatImageSentFor = s.ImageSentFor; chatLastTotalTokens = s.Tokens; chatLastModel = s.Model ?? ""; chatSlotRoot = s.Root ?? dazzleDatasetFolder;
            RenderTranscript();
        }

        /// <summary>Write the image's chat when it holds at least one exchange. Called after every turn and when the image changes.</summary>
        private void SaveChat(string image)
        {
            if (!KeepConversations || string.IsNullOrEmpty(image)) return;
            bool current = string.Equals(image, chatSlotPath, StringComparison.OrdinalIgnoreCase);
            chatSlots.TryGetValue(image, out var s);
            JArray history = current ? chatHistory : s?.History;
            string root = current ? chatSlotRoot : s?.Root;
            root ??= dazzleDatasetFolder;
            if (string.IsNullOrEmpty(root)) return;
            if (history == null || !history.OfType<JObject>().Any(m => (string)m["role"] == "user")) return;
            try
            {
                var c = new DazzleConversation
                {
                    Kind = "chat", ImagePath = image, Name = Path.GetFileName(image) + " -- chat",
                    Messages = DazzleConversationFile.StripImages(history, Path.GetFileName(image)),
                    Model = chatLastModel, TokenCount = chatLastTotalTokens,
                };
                c.Extra["skill"] = loadedChatSkillName ?? "";
                string path = ConversationStore.WritePath(image, "chat", root);
                if (File.Exists(path)) { try { c.CreatedAt = DazzleConversationFile.Load(path).CreatedAt; } catch (Exception) { } }
                DazzleConversationFile.Save(c, path);
            }
            catch (Exception e) { Log("could not save the chat for " + Path.GetFileName(image) + ": " + e.Message); }
        }

        /// <summary>
        /// After a chat turn: write it, then follow the image that is selected NOW. Not the image change that arrived
        /// during the turn: a rename rebinds the list, which selects its first row for a moment before the renamed image is
        /// selected again, and replaying that moment put another image's chat under the renamed one (the probe,
        /// 2026-10-02 21:12).
        /// </summary>
        private void AfterChatTurn()
        {
            SaveChat(chatSlotPath);
            pendingSlotPath = null;
            string now = currentInfo?.Path;
            if (now != null && !string.Equals(now, chatSlotPath, StringComparison.OrdinalIgnoreCase)) SwitchConversationsTo(now);
        }

        /// <summary>The transcript drawn again from the messages: what the person said, what the model said, the tools it used.</summary>
        private void RenderTranscript()
        {
            if (transcript == null) return;
            transcript.Clear();
            if (chatHistory == null) return;
            foreach (var m in chatHistory.OfType<JObject>())
            {
                string role = (string)m["role"];
                string text = m["content"]?.Type == JTokenType.String ? (string)m["content"] : m["content"] is JArray a ? string.Join("\n", a.OfType<JObject>().Where(p => (string)p["type"] == "text").Select(p => (string)p["text"])) : "";
                if (role == "user")
                {
                    if ((bool?)m["satt_from_refine"] == true) { AppendTranscript("(this chat starts from the image's AI Refine run)", ToolColor, true); continue; }
                    // the facts the tool adds before the person's words are not the person's words
                    int cut = text.IndexOf("\n\n", StringComparison.Ordinal);
                    if ((text.StartsWith("Current image:") || text.StartsWith("The current image is now:")) && cut > 0) text = text.Substring(cut + 2);
                    AppendTranscript("You: " + text, UserColor, false);
                }
                else if (role == "assistant")
                {
                    if ((bool?)m["satt_from_refine"] == true) { AppendTranscript("AI Refine proposed: " + text, AssistantColor, false); continue; }
                    if (!string.IsNullOrEmpty(text)) AppendTranscript("AI: " + text, AssistantColor, false);
                    foreach (var call in (m["tool_calls"] as JArray ?? new JArray()))
                        AppendTranscript("[" + (string)call["function"]?["name"] + "] " + (string)call["function"]?["arguments"], ToolColor, true);
                }
            }
        }

        // ---------------------------------------------------------------- AI Refine runs

        /// <summary>Keep a finished Refine run with its image: the exchange (instruction, request, reply) in LM Studio's shape.</summary>
        private void SaveRefineRun(string image, DazzleLmStudio.Request req, DazzleLmStudio.RunResult result, string leftCaption)
        {
            var c = new DazzleConversation { Kind = "refine", ImagePath = image, Name = Path.GetFileName(image) + " -- AI Refine", Model = result.ModelUsed ?? req.Model ?? "" };
            c.Messages.Add(new JObject { ["role"] = "system", ["content"] = req.SystemPrompt });
            c.Messages.Add(new JObject { ["role"] = "user", ["content"] = req.UserText + (req.ImageBytes != null ? "\n[image sent: " + Path.GetFileName(image) + "]" : "") });
            c.Messages.Add(new JObject { ["role"] = "assistant", ["content"] = result.Content });
            c.Extra["left"] = leftCaption; c.Extra["right"] = result.Content; c.Extra["elapsedMs"] = (long)result.Elapsed.TotalMilliseconds;
            c.Extra["schema"] = result.SchemaUsed; c.Extra["skill"] = Program.Settings.DazzleRefineSkill ?? "";
            refineRuns[image] = c;
            // a chat on this image with no turns yet starts over, so it picks the new run up (the probe, 2026-10-02 21:07:
            // moving onto the image in Chat mode had begun an empty session before Refine ran)
            if (string.Equals(image, chatSlotPath, StringComparison.OrdinalIgnoreCase) && chatHistory != null && !chatHistory.OfType<JObject>().Any(m => (string)m["role"] == "user"))
                chatHistory = null;
            else if (chatSlots.TryGetValue(image, out var slot) && slot.History != null && !slot.History.OfType<JObject>().Any(m => (string)m["role"] == "user"))
                chatSlots.Remove(image);
            if (!KeepConversations || string.IsNullOrEmpty(dazzleDatasetFolder)) return;
            try { DazzleConversationFile.Save(c, ConversationStore.WritePath(image, "refine", dazzleDatasetFolder)); }
            catch (Exception e) { Log("could not save the Refine run for " + Path.GetFileName(image) + ": " + e.Message); }
        }

        /// <summary>A Refine run kept on disk comes back as the image's proposal when the folder is opened again.</summary>
        private void LoadRefineRun(string image)
        {
            if (string.IsNullOrEmpty(image) || refineRuns.ContainsKey(image)) return;
            var file = FindConversationFile(image, "refine");
            if (file == null) return;
            try
            {
                var c = DazzleConversationFile.Load(file);
                refineRuns[image] = c;
                if (!proposals.ContainsKey(image))
                {
                    string right = (string)c.Extra["right"] ?? (string)c.Messages.OfType<JObject>().LastOrDefault(m => (string)m["role"] == "assistant")?["content"] ?? "";
                    var rr = new DazzleLmStudio.RunResult { Ok = true, Content = right, ModelUsed = c.Model, SchemaUsed = (bool?)c.Extra["schema"] ?? false, Elapsed = TimeSpan.FromMilliseconds((long?)c.Extra["elapsedMs"] ?? 0) };
                    proposals[image] = ((string)c.Extra["left"] ?? "", right, rr);
                    if (IsCurrentImage(image)) { lastRenderKey = null; RenderProposalForCurrentImage(); }
                }
            }
            catch (Exception e) { Log("could not read " + file + ": " + e.Message); }
        }

        /// <summary>Messages that open a chat with the image's Refine run, when there is one and the setting is on.</summary>
        private IEnumerable<JObject> RefineOpening(string image)
        {
            if (!Program.Settings.DazzleChatFromRefine || string.IsNullOrEmpty(image) || !refineRuns.TryGetValue(image, out var run)) yield break;
            var user = (string)run.Messages.OfType<JObject>().FirstOrDefault(m => (string)m["role"] == "user")?["content"] ?? "";
            var reply = (string)run.Messages.OfType<JObject>().LastOrDefault(m => (string)m["role"] == "assistant")?["content"] ?? "";
            if (reply.Length == 0) yield break;
            yield return new JObject { ["role"] = "user", ["satt_from_refine"] = true, ["content"] = "Before this chat, AI Refine ran on this image. Its instruction was:\n" + run.SystemText + "\n\nIts request was:\n" + user };
            yield return new JObject { ["role"] = "assistant", ["satt_from_refine"] = true, ["content"] = reply };
        }

        // ---------------------------------------------------------------- rename and move

        /// <summary>The image moved or was renamed: its two files follow, and the in-memory entries are re-keyed.</summary>
        private void MoveConversations(string oldPath, string newPath)
        {
            if (chatSlots.TryGetValue(oldPath, out var s)) { chatSlots.Remove(oldPath); chatSlots[newPath] = s; }
            if (refineRuns.TryGetValue(oldPath, out var r)) { refineRuns.Remove(oldPath); refineRuns[newPath] = r; r.ImagePath = newPath; }
            if (string.Equals(chatSlotPath, oldPath, StringComparison.OrdinalIgnoreCase)) chatSlotPath = newPath;
            if (!KeepConversations || string.IsNullOrEmpty(dazzleDatasetFolder)) return;
            foreach (var kind in new[] { "chat", "refine" })
                foreach (var step in ConversationStore.Move(oldPath, newPath, kind, dazzleDatasetFolder))
                    if (!step.Moved) Log("conversation file not moved: " + step);
        }

        // ---------------------------------------------------------------- LM Studio: export and import

        /// <summary>LM Studio's conversations folder (SATT_LMSTUDIO_DIR overrides it).</summary>
        private static string LmStudioConversations =>
            Environment.GetEnvironmentVariable("SATT_LMSTUDIO_DIR") is string d && d.Trim().Length > 0 ? d.Trim()
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lmstudio", "conversations");

        /// <summary>Where exports go (the person, 2026-10-02 20:52): LM Studio's folder, a simple-ai-tag-tool folder, and one per dataset root.</summary>
        private string ExportFolder() =>
            Path.Combine(LmStudioConversations, "simple-ai-tag-tool", SafeName(Path.GetFileName((dazzleDatasetFolder ?? "dataset").TrimEnd('\\', '/'))));

        private static string SafeName(string s) { foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Length == 0 ? "dataset" : s; }

        private ToolStripDropDownButton BuildExportImportButtons(out ToolStripDropDownButton import)
        {
            var export = new ToolStripDropDownButton("Export") { Name = "chatExport", ToolTipText = "Export: write this image's AI Chat conversation, or its AI Refine run, into LM Studio's conversations folder (simple-ai-tag-tool\\<dataset folder>), where LM Studio lists it." };
            export.DropDownItems.Add(new ToolStripMenuItem("This image's chat", null, (s, e) => ExportConversation("chat")) { Name = "exportChat" });
            export.DropDownItems.Add(new ToolStripMenuItem("This image's AI Refine run", null, (s, e) => ExportConversation("refine")) { Name = "exportRefine" });
            var imp = new ToolStripDropDownButton("Import") { Name = "chatImport", ToolTipText = "Import: make a conversation from LM Studio this image's chat; the next message continues it." };
            imp.DropDownOpening += (s, e) => FillImportMenu(imp);
            imp.DropDownItems.Add("(loading)");
            import = imp;
            return export;
        }

        private void ExportConversation(string kind)
        {
            string image = currentInfo?.Path;
            if (image == null) { AppendTranscript("select an image first", ErrorColor, true); return; }
            DazzleConversation c;
            if (kind == "refine")
            {
                if (!refineRuns.TryGetValue(image, out c)) { LoadRefineRun(image); refineRuns.TryGetValue(image, out c); }
                if (c == null) { AppendTranscript("this image has no AI Refine run to export", ErrorColor, true); return; }
            }
            else
            {
                if (chatHistory == null || !chatHistory.OfType<JObject>().Any(m => (string)m["role"] == "user")) { AppendTranscript("this image's chat is empty -- nothing to export", ErrorColor, true); return; }
                c = new DazzleConversation { Kind = "chat", ImagePath = image, Messages = DazzleConversationFile.StripImages(chatHistory, Path.GetFileName(image)), Model = chatLastModel, TokenCount = chatLastTotalTokens };
                c.Extra["skill"] = loadedChatSkillName ?? "";
            }
            c.Name = Path.GetFileName(image) + (kind == "refine" ? " -- AI Refine" : " -- chat");
            c.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string path = Path.Combine(ExportFolder(), c.CreatedAt + ".conversation.json");
            try
            {
                DazzleConversationFile.Save(c, path);
                AppendTranscript("exported " + (kind == "refine" ? "the AI Refine run" : "this chat") + " to LM Studio: " + path, ToolColor, true);
                Log("exported " + kind + ": " + path);
            }
            catch (Exception e) { AppendTranscript("could not export: " + e.Message, ErrorColor, true); }
        }

        private void FillImportMenu(ToolStripDropDownButton menu)
        {
            menu.DropDownItems.Clear();
            string root = LmStudioConversations;
            var files = new List<(string path, string name, DateTime when)>();
            try
            {
                if (Directory.Exists(root))
                    foreach (var f in Directory.EnumerateFiles(root, "*.conversation.json", SearchOption.AllDirectories))
                    {
                        string name;
                        try { name = (string)JObject.Parse(File.ReadAllText(f))["name"]; } catch (Exception) { continue; }
                        string folder = Path.GetRelativePath(root, Path.GetDirectoryName(f));
                        files.Add((f, (string.IsNullOrWhiteSpace(name) ? "(untitled)" : name) + (folder == "." ? "" : "   [" + folder + "]"), File.GetLastWriteTime(f)));
                    }
            }
            catch (Exception e) { menu.DropDownItems.Add(new ToolStripMenuItem("could not list LM Studio's conversations: " + e.Message) { Enabled = false }); }
            foreach (var f in files.OrderByDescending(x => x.when).Take(15))
            {
                var path = f.path;
                menu.DropDownItems.Add(new ToolStripMenuItem(f.name, null, (s, e) => ImportConversation(path)) { ToolTipText = path });
            }
            if (files.Count == 0) menu.DropDownItems.Add(new ToolStripMenuItem("(no conversations in " + root + ")") { Enabled = false });
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(new ToolStripMenuItem("From a file...", null, (s, e) =>
            {
                using var dlg = new OpenFileDialog { Title = "Import a conversation", Filter = "LM Studio conversations|*.conversation.json;*.json|All files|*.*", InitialDirectory = Directory.Exists(root) ? root : "" };
                if (dlg.ShowDialog(this) == DialogResult.OK) ImportConversation(dlg.FileName);
            }));
        }

        /// <summary>Make a conversation file this image's chat: its turns become the history, and the next message continues it.</summary>
        private void ImportConversation(string file)
        {
            if (currentInfo?.Path == null) { AppendTranscript("select an image first", ErrorColor, true); return; }
            if (chatCts != null) { AppendTranscript("wait for the current turn to finish", ErrorColor, true); return; }
            try
            {
                var c = DazzleConversationFile.Load(file);
                var history = (JArray)c.Messages.DeepClone();
                if (!history.OfType<JObject>().Any(m => (string)m["role"] == "system"))
                {
                    StartChatSession();   // this image's instruction as it stands, then the imported turns after it
                    foreach (var m in history) chatHistory.Add(m);
                }
                else { chatHistory = history; chatSystemText = c.SystemText; }
                chatImageSentFor = null; chatLastTotalTokens = c.TokenCount; chatLastModel = c.Model ?? "";
                chatSlotPath = currentInfo.Path; chatSlotRoot = dazzleDatasetFolder;
                RenderTranscript();
                AppendTranscript("imported '" + (string.IsNullOrWhiteSpace(c.Name) ? Path.GetFileName(file) : c.Name) + "' (" + history.Count + " messages); the next message continues it", ToolColor, true);
                SaveChat(chatSlotPath);
                UpdateContextMeter();
            }
            catch (Exception e) { AppendTranscript("could not import " + Path.GetFileName(file) + ": " + e.Message, ErrorColor, true); }
        }
    }
}
