using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// One image's AI Refine run or AI Chat conversation (two files per image, the person's choice, 2026-10-02 20:52:
    /// either can be opened in LM Studio on its own).
    /// </summary>
    public sealed class DazzleConversation
    {
        public string Kind = "chat";                 // "chat" | "refine"
        public string ImagePath;
        public string Name;
        public long CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public JArray Messages = new JArray();       // the OpenAI shape the tool sends; image parts replaced by a text note
        public string Model = "";
        public int TokenCount;
        public JObject Extra = new JObject();        // refine: left, right, elapsedMs, schema; chat: skill

        public string SystemText => (string)Messages.OfType<JObject>().FirstOrDefault(m => (string)m["role"] == "system")?["content"] ?? "";
    }

    /// <summary>
    /// The file shape is LM Studio's own .conversation.json (measured 2026-10-02 on 12 files LM Studio wrote: a user turn
    /// is a singleStep with text content; an assistant turn is a multiStep whose steps are contentBlocks -- text,
    /// "thinking"-styled text, toolCallRequest, and toolCallResult with roleOverride "tool"). The tool's own copy of the
    /// messages, exact, goes under a "satt" key, so a file this tool wrote round-trips without loss; a file LM Studio wrote
    /// is converted. Images are not carried (no LM Studio sample had one); a turn that sent one says so in a text note.
    /// No reference to a form: tools\conversation-check compiles this file alone.
    /// </summary>
    public static class DazzleConversationFile
    {
        public const int Version = 1;

        /// <summary>A copy of the messages with every image part replaced by a short note.</summary>
        public static JArray StripImages(JArray messages, string imageName)
        {
            var copy = (JArray)messages.DeepClone();
            foreach (var m in copy.OfType<JObject>())
            {
                if (m["content"] is JArray parts)
                {
                    var texts = new List<string>();
                    foreach (var p in parts.OfType<JObject>())
                    {
                        string t = (string)p["type"];
                        if (t == "text") texts.Add((string)p["text"] ?? "");
                        else if (t == "image_url") texts.Add("[image sent: " + (imageName ?? "the current image") + "]");
                    }
                    m["content"] = string.Join("\n", texts);
                }
            }
            return copy;
        }

        // ------------------------------------------------------------------ write

        public static JObject ToLmStudio(DazzleConversation c)
        {
            var messages = new JArray();
            JArray steps = null;                     // the open assistant multiStep's steps
            var callIds = new Dictionary<string, long>();
            long Num(string id) { if (id == null) id = ""; if (!callIds.TryGetValue(id, out var n)) callIds[id] = n = 1000000L + callIds.Count; return n; }
            int stepNo = 0;
            string Sid() => c.CreatedAt + "-" + (stepNo++);
            JObject Block(JArray content) => new JObject { ["type"] = "contentBlock", ["stepIdentifier"] = Sid(), ["content"] = content, ["defaultShouldIncludeInContext"] = true, ["shouldIncludeInContext"] = true };
            void OpenAssistant()
            {
                if (steps != null) return;
                steps = new JArray();
                messages.Add(new JObject
                {
                    ["versions"] = new JArray(new JObject { ["type"] = "multiStep", ["role"] = "assistant", ["steps"] = steps, ["senderInfo"] = new JObject { ["senderName"] = c.Model ?? "" } }),
                    ["currentlySelected"] = 0,
                });
            }
            foreach (var m in c.Messages.OfType<JObject>())
            {
                string role = (string)m["role"];
                string text = m["content"]?.Type == JTokenType.String ? (string)m["content"] : m["content"] is JArray a ? string.Join("\n", a.OfType<JObject>().Select(p => (string)p["text"] ?? "[image]")) : "";
                if (role == "system") continue;
                if (role == "user")
                {
                    steps = null;
                    messages.Add(new JObject
                    {
                        ["versions"] = new JArray(new JObject { ["type"] = "singleStep", ["role"] = "user", ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = text }) }),
                        ["currentlySelected"] = 0,
                    });
                }
                else if (role == "assistant")
                {
                    OpenAssistant();
                    if (!string.IsNullOrEmpty(text)) steps.Add(Block(new JArray(new JObject { ["type"] = "text", ["text"] = text })));
                    foreach (var call in (m["tool_calls"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        string id = (string)call["id"];
                        JToken parameters;
                        try { parameters = JToken.Parse((string)call["function"]?["arguments"] ?? "{}"); } catch (Exception) { parameters = new JObject { ["raw"] = (string)call["function"]?["arguments"] }; }
                        steps.Add(Block(new JArray(new JObject { ["type"] = "toolCallRequest", ["callId"] = Num(id), ["toolCallRequestId"] = id, ["name"] = (string)call["function"]?["name"], ["parameters"] = parameters })));
                    }
                }
                else if (role == "tool")
                {
                    OpenAssistant();
                    string id = (string)m["tool_call_id"];
                    var b = Block(new JArray(new JObject { ["type"] = "toolCallResult", ["callId"] = Num(id), ["toolCallRequestId"] = id, ["content"] = text, ["name"] = (string)m["name"] ?? "" }));
                    b["roleOverride"] = "tool";
                    steps.Add(b);
                }
            }
            return new JObject
            {
                ["name"] = c.Name ?? "",
                ["pinned"] = false,
                ["createdAt"] = c.CreatedAt,
                ["preset"] = "",
                ["tokenCount"] = c.TokenCount,
                ["systemPrompt"] = c.SystemText,
                ["messages"] = messages,
                ["usePerChatPredictionConfig"] = false,
                ["perChatPredictionConfig"] = new JObject { ["fields"] = new JArray() },
                ["clientInput"] = "",
                ["clientInputFiles"] = new JArray(),
                ["userFilesSizeBytes"] = 0,
                // LM Studio refuses a chat whose lastUsedModel lacks the two config objects ("invalid_type ... Required",
                // seen 2026-10-02 21:25 on the first export); its own files carry them as {"fields": []} when empty
                ["lastUsedModel"] = new JObject
                {
                    ["identifier"] = c.Model ?? "", ["indexedModelIdentifier"] = c.Model ?? "",
                    ["instanceLoadTimeConfig"] = new JObject { ["fields"] = new JArray() },
                    ["instanceOperationTimeConfig"] = new JObject { ["fields"] = new JArray() },
                },
                ["notes"] = new JArray(),
                ["plugins"] = new JArray(),
                ["pluginConfigs"] = new JObject(),
                ["disabledPluginTools"] = new JArray(),
                ["looseFiles"] = new JArray(),
                ["satt"] = new JObject { ["version"] = Version, ["kind"] = c.Kind, ["image"] = c.ImagePath ?? "", ["messages"] = c.Messages.DeepClone(), ["extra"] = c.Extra.DeepClone() },
            };
        }

        /// <summary>Write next to the target and move into place, so a crash never leaves half a file.</summary>
        public static void Save(DazzleConversation c, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, ToLmStudio(c).ToString(Formatting.Indented));
            File.Move(tmp, path, overwrite: true);
        }

        // ------------------------------------------------------------------ read

        public static DazzleConversation Load(string path) => FromLmStudio(JObject.Parse(File.ReadAllText(path)));

        /// <summary>A file this tool wrote: the exact messages under "satt". A file LM Studio wrote: its turns converted.</summary>
        public static DazzleConversation FromLmStudio(JObject o)
        {
            var c = new DazzleConversation
            {
                Name = (string)o["name"] ?? "",
                CreatedAt = (long?)o["createdAt"] ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                TokenCount = (int?)o["tokenCount"] ?? 0,
                Model = (string)o["lastUsedModel"]?["identifier"] ?? "",
            };
            if (o["satt"] is JObject satt && satt["messages"] is JArray mine)
            {
                c.Kind = (string)satt["kind"] ?? "chat";
                c.ImagePath = (string)satt["image"];
                c.Messages = (JArray)mine.DeepClone();
                c.Extra = satt["extra"] as JObject ?? new JObject();
                return c;
            }
            c.Messages = ConvertTurns(o);
            return c;
        }

        /// <summary>LM Studio's turns as OpenAI messages: thinking, debug and tool-status steps are left out.</summary>
        public static JArray ConvertTurns(JObject o)
        {
            var result = new JArray();
            string system = (string)o["systemPrompt"];
            if (!string.IsNullOrWhiteSpace(system)) result.Add(new JObject { ["role"] = "system", ["content"] = system });
            foreach (var m in (o["messages"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var versions = m["versions"] as JArray;
                if (versions == null || versions.Count == 0) continue;
                int sel = Math.Max(0, Math.Min(versions.Count - 1, (int?)m["currentlySelected"] ?? 0));
                var v = versions[sel] as JObject;
                if (v == null) continue;
                if ((string)v["type"] == "singleStep")
                {
                    string text = string.Join("\n", (v["content"] as JArray ?? new JArray()).OfType<JObject>()
                        .Select(p => (string)p["type"] == "text" ? (string)p["text"] : "[" + ((string)p["type"] ?? "attachment") + "]"));
                    result.Add(new JObject { ["role"] = (string)v["role"] ?? "user", ["content"] = text });
                    continue;
                }
                // multiStep: text and tool calls gather into an assistant message; a tool result closes it
                JObject assistant = null;
                void Flush() { if (assistant != null) { result.Add(assistant); assistant = null; } }
                foreach (var s in (v["steps"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    if ((string)s["type"] != "contentBlock") continue;
                    if ((string)s["style"]?["type"] == "thinking") continue;
                    foreach (var part in (s["content"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        string pt = (string)part["type"];
                        if (pt == "toolCallResult" || (string)s["roleOverride"] == "tool")
                        {
                            Flush();
                            result.Add(new JObject { ["role"] = "tool", ["tool_call_id"] = (string)part["toolCallRequestId"] ?? ((long?)part["callId"])?.ToString() ?? "", ["content"] = (string)part["content"] ?? part["content"]?.ToString(Formatting.None) ?? "" });
                            continue;
                        }
                        assistant ??= new JObject { ["role"] = "assistant", ["content"] = "" };
                        if (pt == "text") assistant["content"] = ((string)assistant["content"]).Length == 0 ? (string)part["text"] : (string)assistant["content"] + "\n" + (string)part["text"];
                        else if (pt == "toolCallRequest")
                        {
                            var calls = assistant["tool_calls"] as JArray ?? (JArray)(assistant["tool_calls"] = new JArray());
                            calls.Add(new JObject
                            {
                                ["id"] = (string)part["toolCallRequestId"] ?? ((long?)part["callId"])?.ToString() ?? "",
                                ["type"] = "function",
                                ["function"] = new JObject { ["name"] = (string)part["name"], ["arguments"] = (part["parameters"] ?? new JObject()).ToString(Formatting.None) },
                            });
                        }
                    }
                }
                Flush();
            }
            foreach (var a in result.OfType<JObject>().Where(x => (string)x["role"] == "assistant" && ((string)x["content"]).Length == 0 && x["tool_calls"] != null))
                a["content"] = null;   // the OpenAI shape for a turn that only calls tools
            return result;
        }
    }
}
