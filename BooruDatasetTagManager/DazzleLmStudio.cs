using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool: a streaming client for an OpenAI-compatible local server (LM Studio first).
    /// Own transport rather than the OpenAI SDK upstream's autotagger uses, because the pass needs four
    /// things the SDK cannot say: reasoning_effort "none", the reasoning_content stream, cancellation,
    /// and the server's own error sentences. Everything unusual here is a measured fact
    /// (2026-10-02, tools/lmstudio-probe.py; the design document names each).
    /// </summary>
    public sealed class DazzleLmStudio
    {
        /// <summary>Probe result: three-valued on purpose -- a server that cannot say what is loaded is not "nothing loaded".</summary>
        public sealed class ProbeResult
        {
            public bool Reachable;
            public string Reason;               // one sentence, for the status line
            public string Model;                // the model a run would use, or null
            public List<string> Listed = new List<string>();
            public List<string> Loaded;         // null = the server cannot say
            public int? ContextLength;
            public bool? Vision;                // null = unknown
            public string Warning;              // e.g. an oversized context window
        }

        public sealed class Request
        {
            public string Model;
            public string SystemPrompt;
            public string UserText;
            public byte[] ImageBytes;           // may be null for a text-only run
            public string ImageMime = "image/png";
            public bool Think = true;           // false sends reasoning_effort "none"
            public bool UseSchema = true;       // strict json_schema {caption}; falls back to free text on HTTP 400
            public int MaxTokens = 4096;
            public float Temperature = 0.3f;
        }

        public enum DeltaKind { Reasoning, Content, Status }
        public sealed class Delta { public DeltaKind Kind; public string Text; public Delta(DeltaKind k, string t) { Kind = k; Text = t; } }

        public sealed class RunResult
        {
            public bool Ok;
            public bool Cancelled;
            public string Error;                // a sentence, when !Ok
            public string Content = "";         // the reply (JSON unwrapped when the schema was used)
            public string Reasoning = "";
            public string ModelUsed;
            public bool SchemaUsed;
            public TimeSpan Elapsed;
            public string RequestSummary;       // for the log: endpoint, model, sizes, flags
        }

        public static readonly int RoomyContext = 32768;   // above this the loaded window is the usual reason a run is slow
        public static readonly int ImageLongSide = 1024;

        private readonly HttpClient http;
        public string Endpoint { get; }         // ".../v1"
        public string Root => Endpoint.EndsWith("/v1") ? Endpoint.Substring(0, Endpoint.Length - 3) : Endpoint;

        public DazzleLmStudio(string endpoint, string apiKey, int timeoutSeconds)
        {
            Endpoint = NormalizeEndpoint(endpoint);
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, timeoutSeconds)) };
            if (!string.IsNullOrEmpty(apiKey))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        /// <summary>Trim, drop a trailing slash, and prefer the IPv4 literal: LM Studio binds IPv4-only on Windows and "localhost" can resolve to ::1 first and hang.</summary>
        public static string NormalizeEndpoint(string endpoint)
        {
            string e = (endpoint ?? "").Trim().TrimEnd('/');
            if (e.Length == 0) e = "http://127.0.0.1:1234/v1";
            e = Regex.Replace(e, @"^(https?://)localhost([:/])", "${1}127.0.0.1$2", RegexOptions.IgnoreCase);
            if (!e.EndsWith("/v1")) e += "/v1";
            return e;
        }

        // -- the roster ----------------------------------------------------------------------------------

        public async Task<ProbeResult> ProbeAsync(string preferredModel, CancellationToken ct)
        {
            var r = new ProbeResult();
            JObject listed;
            try { listed = await GetJsonAsync(Endpoint + "/models", ct); }
            catch (Exception e)
            {
                r.Reason = "no model server at " + Endpoint + " -- start LM Studio's server (" + Short(e) + ")";
                return r;
            }
            r.Listed = (listed["data"] as JArray ?? new JArray()).Select(d => (string)d["id"]).Where(s => s != null).ToList();
            if (r.Listed.Count == 0) { r.Reason = Endpoint + " answered with no models"; return r; }
            r.Reachable = true;

            // LM Studio's own API says what is in memory; /v1/models lists what is downloaded. Other servers have neither.
            try
            {
                var native = await GetJsonAsync(Root + "/api/v0/models", ct);
                var rows = (native["data"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                r.Loaded = rows.Where(d => (string)d["state"] == "loaded").Select(d => (string)d["id"]).ToList();
                var first = rows.FirstOrDefault(d => (string)d["state"] == "loaded" && (preferredModel == null || (string)d["id"] == preferredModel))
                            ?? rows.FirstOrDefault(d => (string)d["state"] == "loaded");
                if (first != null)
                {
                    r.ContextLength = (int?)first["loaded_context_length"] ?? (int?)first["max_context_length"];
                    if ((string)first["type"] == "vlm") r.Vision = true;
                }
                // /api/v1/models (newer) says whether a model can see
                try
                {
                    var v1 = await GetJsonAsync(Root + "/api/v1/models", ct);
                    var row = (v1["models"] as JArray ?? v1["data"] as JArray ?? new JArray()).OfType<JObject>()
                        .FirstOrDefault(d => (string)d["key"] == (first != null ? (string)first["id"] : preferredModel));
                    var vision = row?["capabilities"]?["vision"];
                    if (vision != null && vision.Type == JTokenType.Boolean) r.Vision = (bool)vision;
                }
                catch (Exception) { }
            }
            catch (Exception) { r.Loaded = null; }

            // The model a run would use: the preferred one if loaded; else the loaded one; refuse to trigger a load.
            if (!string.IsNullOrEmpty(preferredModel))
            {
                if (r.Loaded != null && !r.Loaded.Contains(preferredModel))
                {
                    r.Reachable = false;
                    r.Reason = "model '" + preferredModel + "' is not loaded (loaded: " + (r.Loaded.Count > 0 ? string.Join(", ", r.Loaded) : "none") + ") -- asking for it would load it from disk; load it in LM Studio first, or clear the model setting to use the loaded one";
                    return r;
                }
                if (r.Loaded == null && !r.Listed.Contains(preferredModel))
                {
                    r.Reachable = false;
                    r.Reason = "model '" + preferredModel + "' is not on " + Endpoint + " -- it has: " + string.Join(", ", r.Listed);
                    return r;
                }
                r.Model = preferredModel;
            }
            else if (r.Loaded != null)
            {
                if (r.Loaded.Count == 0) { r.Reachable = false; r.Reason = Endpoint + " has no model loaded -- load one in LM Studio (asking would load one from disk, which takes minutes)"; return r; }
                r.Model = r.Loaded[0];
            }
            else r.Model = r.Listed[0];

            r.Reason = Endpoint + " -- " + r.Model + (r.ContextLength is int n ? ", " + n.ToString("N0") + "-token context" : "") + (r.Vision == false ? ", NO vision" : "");
            if (r.ContextLength is int c && c > RoomyContext)
                r.Warning = "loaded with a " + c.ToString("N0") + "-token context; an oversized window is the usual reason a run takes minutes (measured: 0.27 tokens/s at 111k vs 4 s per image at 32k on the same model) -- reload it at 16k-32k";
            if (r.Vision == false)
                r.Warning = (r.Warning == null ? "" : r.Warning + "; ") + "this model reports no vision capability -- the image would be ignored";
            return r;
        }

        // -- the run ---------------------------------------------------------------------------------------

        public async Task<RunResult> RunAsync(Request req, IProgress<Delta> progress, CancellationToken ct)
        {
            var res = new RunResult { ModelUsed = req.Model, SchemaUsed = req.UseSchema };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            res.RequestSummary = Endpoint + "/chat/completions  model=" + req.Model + "  think=" + (req.Think ? "on" : "off") + "  schema=" + (req.UseSchema ? "on" : "off")
                + "  image=" + (req.ImageBytes == null ? "none" : (req.ImageBytes.Length / 1024) + " KB " + req.ImageMime) + "  text=" + (req.UserText?.Length ?? 0) + " chars  max_tokens=" + req.MaxTokens;
            try
            {
                var outcome = await PostStreamAsync(req, req.UseSchema, res, progress, ct);
                if (outcome == 400 && req.UseSchema)
                {
                    progress?.Report(new Delta(DeltaKind.Status, "server refused the reply schema (HTTP 400); asking again for free text"));
                    res.SchemaUsed = false; res.Content = ""; res.Reasoning = "";
                    outcome = await PostStreamAsync(req, false, res, progress, ct);
                }
                if (outcome != 0) return res;   // error text already set
            }
            catch (OperationCanceledException)
            {
                res.Cancelled = true; res.Error = "stopped (the server cancels the generation when the connection closes)";
                return res;
            }
            catch (HttpRequestException e)
            {
                res.Error = "cannot reach " + Endpoint + ": " + Short(e);
                return res;
            }
            catch (Exception e)
            {
                res.Error = Short(e);
                return res;
            }
            finally { res.Elapsed = sw.Elapsed; }

            // inline <think> (a server without reasoning parsing) goes to the reasoning channel
            var m = Regex.Match(res.Content, @"<think>(.*?)(?:</think>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (m.Success)
            {
                res.Reasoning += m.Groups[1].Value;
                res.Content = Regex.Replace(res.Content, @"<think>.*?(?:</think>|$)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            }
            res.Content = res.Content.Trim();
            if (res.SchemaUsed)
            {
                try
                {
                    var o = JObject.Parse(res.Content);
                    var cap = (string)o["caption"];
                    if (cap != null) res.Content = cap.Trim();
                }
                catch (Exception) { /* not JSON after all: keep the text, the user sees it in the diff */ }
            }
            if (res.Content.Length == 0)
            {
                res.Error = req.Model + " answered with empty content -- " + (req.Think ? "the reasoning may have consumed the token budget; raise max tokens or turn Think off" : "raise max tokens");
                return res;
            }
            res.Ok = true;
            return res;
        }

        /// <summary>Returns 0 on success (content/reasoning filled), or the HTTP status on an error (res.Error set).</summary>
        private async Task<int> PostStreamAsync(Request req, bool schema, RunResult res, IProgress<Delta> progress, CancellationToken ct)
        {
            var content = new JArray { new JObject { ["type"] = "text", ["text"] = req.UserText ?? "" } };
            if (req.ImageBytes != null)
                content.Add(new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = "data:" + req.ImageMime + ";base64," + Convert.ToBase64String(req.ImageBytes) } });
            var messages = new JArray();
            if (!string.IsNullOrWhiteSpace(req.SystemPrompt)) messages.Add(new JObject { ["role"] = "system", ["content"] = req.SystemPrompt });
            messages.Add(new JObject { ["role"] = "user", ["content"] = content });
            var body = new JObject
            {
                ["model"] = req.Model, ["messages"] = messages, ["stream"] = true,
                ["max_tokens"] = req.MaxTokens, ["temperature"] = req.Temperature,
            };
            if (!req.Think) body["reasoning_effort"] = "none";
            if (schema)
                body["response_format"] = new JObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JObject
                    {
                        ["name"] = "caption", ["strict"] = true,
                        ["schema"] = new JObject { ["type"] = "object", ["properties"] = new JObject { ["caption"] = new JObject { ["type"] = "string" } }, ["required"] = new JArray("caption"), ["additionalProperties"] = false }
                    }
                };

            using var msg = new HttpRequestMessage(HttpMethod.Post, Endpoint + "/chat/completions") { Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json") };
            using var resp = await http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                string detail = "";
                try { detail = (await resp.Content.ReadAsStringAsync(ct)).Trim(); if (detail.Length > 400) detail = detail.Substring(0, 400); } catch (Exception) { }
                res.Error = Endpoint + " answered HTTP " + (int)resp.StatusCode + (detail.Length > 0 ? ": " + detail : "");
                return (int)resp.StatusCode;
            }
            var sbContent = new StringBuilder(); var sbReasoning = new StringBuilder();
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (!line.StartsWith("data:")) continue;
                string payload = line.Substring(5).Trim();
                if (payload == "[DONE]") break;
                JObject obj;
                try { obj = JObject.Parse(payload); } catch (Exception) { continue; }
                var choice = (obj["choices"] as JArray)?.FirstOrDefault() as JObject;
                var delta = choice?["delta"] as JObject;
                if (delta == null) continue;
                string c = (string)delta["content"];
                string r = (string)delta["reasoning_content"] ?? (string)delta["reasoning"];
                if (!string.IsNullOrEmpty(r)) { sbReasoning.Append(r); progress?.Report(new Delta(DeltaKind.Reasoning, r)); }
                if (!string.IsNullOrEmpty(c)) { sbContent.Append(c); progress?.Report(new Delta(DeltaKind.Content, c)); }
                string finish = (string)choice["finish_reason"];
                if (finish == "length") progress?.Report(new Delta(DeltaKind.Status, "the reply hit max tokens (finish_reason = length)"));
            }
            res.Content = sbContent.ToString(); res.Reasoning = sbReasoning.ToString();
            string modelUsed = (string)body["model"];
            res.ModelUsed = modelUsed;
            return 0;
        }

        // -- chat with tools (Chat mode) -------------------------------------------------------------------

        public sealed class ToolCall { public string Id; public string Name; public string Arguments = ""; }

        public sealed class ChatResult
        {
            public bool Ok, Cancelled;
            public string Error;
            public string Content = "", Reasoning = "";
            public List<ToolCall> ToolCalls = new List<ToolCall>();
            public string FinishReason;
            public int? PromptTokens, CompletionTokens, TotalTokens;
            public TimeSpan Elapsed;
        }

        /// <summary>
        /// One turn of a multi-message conversation with optional tools: streams content, reasoning and tool-call
        /// fragments (assembled by index: id and name from the first fragment, arguments concatenated -- measured
        /// 2026-10-02, tools/lmstudio-tools-probe.py), and reads the usage from the final event when the server sends it.
        /// The caller owns the history: append AssistantMessage(result) and one ToolResultMessage per call, then call again.
        /// </summary>
        public async Task<ChatResult> ChatAsync(JArray messages, JArray tools, bool think, int maxTokens, float temperature, IProgress<Delta> progress, CancellationToken ct)
        {
            var res = new ChatResult();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var body = new JObject
            {
                ["model"] = DefaultModel ?? "", ["messages"] = messages, ["stream"] = true,
                ["stream_options"] = new JObject { ["include_usage"] = true },
                ["max_tokens"] = maxTokens, ["temperature"] = temperature,
            };
            if (tools != null && tools.Count > 0) { body["tools"] = tools; body["tool_choice"] = "auto"; }
            if (!think) body["reasoning_effort"] = "none";
            try
            {
                using var msg = new HttpRequestMessage(HttpMethod.Post, Endpoint + "/chat/completions") { Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json") };
                using var resp = await http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    string detail = "";
                    try { detail = (await resp.Content.ReadAsStringAsync(ct)).Trim(); if (detail.Length > 400) detail = detail.Substring(0, 400); } catch (Exception) { }
                    res.Error = Endpoint + " answered HTTP " + (int)resp.StatusCode + (detail.Length > 0 ? ": " + detail : "");
                    return res;
                }
                var sbContent = new StringBuilder(); var sbReasoning = new StringBuilder();
                var calls = new SortedDictionary<int, ToolCall>();
                using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    if (!line.StartsWith("data:")) continue;
                    string payload = line.Substring(5).Trim();
                    if (payload == "[DONE]") break;
                    JObject obj;
                    try { obj = JObject.Parse(payload); } catch (Exception) { continue; }
                    if (obj["usage"] is JObject u)
                    {
                        res.PromptTokens = (int?)u["prompt_tokens"]; res.CompletionTokens = (int?)u["completion_tokens"]; res.TotalTokens = (int?)u["total_tokens"];
                    }
                    var choice = (obj["choices"] as JArray)?.FirstOrDefault() as JObject;
                    if (choice == null) continue;
                    var delta = choice["delta"] as JObject;
                    if (delta != null)
                    {
                        string c = (string)delta["content"];
                        string r = (string)delta["reasoning_content"] ?? (string)delta["reasoning"];
                        if (!string.IsNullOrEmpty(r)) { sbReasoning.Append(r); progress?.Report(new Delta(DeltaKind.Reasoning, r)); }
                        if (!string.IsNullOrEmpty(c)) { sbContent.Append(c); progress?.Report(new Delta(DeltaKind.Content, c)); }
                        if (delta["tool_calls"] is JArray frags)
                            foreach (var f in frags.OfType<JObject>())
                            {
                                int index = (int?)f["index"] ?? 0;
                                if (!calls.TryGetValue(index, out var call)) { call = new ToolCall(); calls[index] = call; }
                                if (f["id"] != null) call.Id = (string)f["id"];
                                var fn = f["function"] as JObject;
                                if (fn != null)
                                {
                                    if (fn["name"] != null && !string.IsNullOrEmpty((string)fn["name"])) call.Name = (string)fn["name"];
                                    if (fn["arguments"] != null) call.Arguments += (string)fn["arguments"];
                                }
                            }
                    }
                    string finish = (string)choice["finish_reason"];
                    if (finish != null) res.FinishReason = finish;
                }
                res.Content = sbContent.ToString(); res.Reasoning = sbReasoning.ToString();
                res.ToolCalls = calls.Values.ToList();
                // inline <think> fallback for a server without reasoning parsing
                var m = Regex.Match(res.Content, @"<think>(.*?)(?:</think>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (m.Success) { res.Reasoning += m.Groups[1].Value; res.Content = Regex.Replace(res.Content, @"<think>.*?(?:</think>|$)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase); }
                res.Content = res.Content.Trim();
                if (res.Content.Length == 0 && res.ToolCalls.Count == 0 && res.FinishReason != "stop")
                { res.Error = "the model answered with nothing" + (res.FinishReason == "length" ? " -- the reply hit max tokens (thinking counts); raise Max reply tokens or turn Think off" : ""); return res; }
                res.Ok = true;
            }
            catch (OperationCanceledException) { res.Cancelled = true; res.Error = "stopped"; }
            catch (HttpRequestException e) { res.Error = "cannot reach " + Endpoint + ": " + Short(e); }
            catch (Exception e) { res.Error = Short(e); }
            finally { res.Elapsed = sw.Elapsed; }
            return res;
        }

        /// <summary>The assistant turn to append to the history: content and the tool calls as the server shaped them.</summary>
        public static JObject AssistantMessage(ChatResult r)
        {
            var m = new JObject { ["role"] = "assistant", ["content"] = r.Content.Length > 0 ? (JToken)r.Content : JValue.CreateNull() };
            if (r.ToolCalls.Count > 0)
                m["tool_calls"] = new JArray(r.ToolCalls.Select(c => new JObject { ["id"] = c.Id, ["type"] = "function", ["function"] = new JObject { ["name"] = c.Name, ["arguments"] = c.Arguments } }));
            return m;
        }

        public static JObject ToolResultMessage(string callId, string content) =>
            new JObject { ["role"] = "tool", ["tool_call_id"] = callId, ["content"] = content };

        /// <summary>A user turn: text, optionally with an image part.</summary>
        public static JObject UserMessage(string text, byte[] imageBytes = null, string mime = "image/jpeg")
        {
            if (imageBytes == null) return new JObject { ["role"] = "user", ["content"] = text };
            return new JObject
            {
                ["role"] = "user",
                ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = text },
                                         new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = "data:" + mime + ";base64," + Convert.ToBase64String(imageBytes) } })
            };
        }

        /// <summary>A function tool definition in the OpenAI shape.</summary>
        public static JObject Tool(string name, string description, JObject parameters) =>
            new JObject { ["type"] = "function", ["function"] = new JObject { ["name"] = name, ["description"] = description, ["parameters"] = parameters } };

        /// <summary>Set the model on a request body built by the caller; ChatAsync fills it from this when the body's model is empty.</summary>
        public string DefaultModel { get; set; }

        // -- images ----------------------------------------------------------------------------------------

        /// <summary>
        /// The bytes to send: PNG/JPEG/WebP/GIF/BMP go as they are when the long side is within the limit; larger
        /// images are downscaled and re-encoded as PNG. A file that cannot be decoded here (WebP without libwebp)
        /// is sent raw -- the server decodes it itself.
        /// </summary>
        public static (byte[] bytes, string mime, string note) PrepareImage(string path, int longSide = 0)
        {
            if (longSide <= 0) longSide = ImageLongSide;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string mime = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".gif" => "image/gif", ".bmp" => "image/bmp", _ => "image/png" };
            byte[] raw = File.ReadAllBytes(path);
            try
            {
                using var ms = new MemoryStream(raw);
                using var img = Image.FromStream(ms, false, false);
                int w = img.Width, h = img.Height;
                if (Math.Max(w, h) <= longSide && (mime == "image/png" || mime == "image/jpeg"))
                    return (raw, mime, w + "x" + h + " sent as is");
                double s = Math.Min(1.0, (double)longSide / Math.Max(w, h));
                int nw = Math.Max(1, (int)Math.Round(w * s)), nh = Math.Max(1, (int)Math.Round(h * s));
                using var bmp = new Bitmap(nw, nh);
                using (var g = Graphics.FromImage(bmp)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(img, 0, 0, nw, nh); }
                // JPEG at 90: a tenth of the PNG's bytes (1.8 MB -> ~180 KB measured) for the same caption
                using var outMs = new MemoryStream();
                var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                bmp.Save(outMs, jpeg, ep);
                return (outMs.ToArray(), "image/jpeg", w + "x" + h + " -> " + nw + "x" + nh + " JPEG");
            }
            catch (Exception e)
            {
                return (raw, mime, "sent raw (" + Short(e) + ")");
            }
        }

        // -- plumbing --------------------------------------------------------------------------------------

        private async Task<JObject> GetJsonAsync(string url, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var text = await http.GetStringAsync(url, cts.Token);
            return JObject.Parse(text);
        }

        private static string Short(Exception e)
        {
            var inner = e; while (inner.InnerException != null) inner = inner.InnerException;
            return inner.Message;
        }
    }
}
