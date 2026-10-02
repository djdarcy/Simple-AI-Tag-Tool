using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool: recover the positive and negative prompt text from a
    /// ComfyUI prompt graph. A line-for-line port of comfydbg/prompts.py
    /// (https://github.com/djdarcy/comfydbg, 2026-10-01); function names and
    /// comments follow the Python so the two can be diffed. Keep them in step.
    ///
    /// ComfyUI saves the API-format graph that produced an output inside the file.
    /// Each node is {"class_type": ..., "inputs": {...}}, and an input fed by another
    /// node is a [source_node_id, output_slot] link. The prompt for a sampler is found
    /// by walking back from its positive/negative input to the node that holds the
    /// text. Following every link does not work: pass-through nodes carry BOTH a
    /// positive and a negative input, so the walk here is side-aware, and anything it
    /// cannot resolve is reported as unresolved -- never guessed.
    /// </summary>
    public static class DazzleComfyPrompts
    {
        // Encoder inputs that hold prompt text, in display order.
        static readonly string[] ENCODER_TEXT_KEYS = { "text", "text_g", "text_l", "clip_l", "t5xxl", "caption", "lyrics", "tags", "prompt" };
        // Inputs that carry pieces of text into a string or concatenation node, in join order.
        static readonly string[] STRING_KEYS = { "text", "string", "value", "prompt", "text_a", "text_b", "text_c", "text_d",
                                                 "text1", "text2", "text3", "text4", "string_a", "string_b", "string_c", "string_d" };
        static readonly string[] JOINER_KEYS = { "delimiter", "separator" };
        // These zero their conditioning, so the text behind them never reaches the model.
        static readonly HashSet<string> ZEROING_CLASSES = new HashSet<string> { "ConditioningZeroOut" };
        // An encoder that also takes a negative prompt emits BOTH conditionings, positive on slot 0 and negative on slot 1.
        static readonly string[] NEGATIVE_TEXT_KEYS = { "negative_prompt" };
        // Nodes that hold a labelled prompt pair themselves instead of feeding a sampler.
        static readonly string[] PAIR_KEYS = { "positive_prompt", "negative_prompt" };
        // rgthree context pipes: output slot k carries field k.
        static readonly string[] _RGTHREE_CTX_ORIGINAL = { "base_ctx", "model", "clip", "vae", "positive", "negative", "latent", "images", "seed" };
        static readonly string[] _RGTHREE_CTX_ALL = _RGTHREE_CTX_ORIGINAL.Concat(new[] {
            "steps", "step_refiner", "cfg", "ckpt_name", "sampler", "scheduler", "clip_width", "clip_height",
            "text_pos_g", "text_pos_l", "text_neg_g", "text_neg_l", "mask", "control_net" }).ToArray();
        static readonly Dictionary<string, string[]> RGTHREE_CONTEXT_FIELDS = new Dictionary<string, string[]>
        {
            { "Context (rgthree)", _RGTHREE_CTX_ORIGINAL }, { "Context Big (rgthree)", _RGTHREE_CTX_ALL }
        };
        const int MAX_HOPS = 32;
        static readonly Regex _DAZZLE_INPUT = new Regex(@"^input_(\d{2})$", RegexOptions.Compiled);
        static readonly Regex _RGTHREE_ANY = new Regex(@"^any_(\d{2})$", RegexOptions.Compiled);

        public class TextPart
        {
            public string Field, Text, SourceClass, SourceNode;
            public List<string> Pieces; // set when the text was joined from several string nodes
        }

        public class Side
        {
            public string Status; // "resolved", "empty" or "unresolved"
            public List<TextPart> Parts = new List<TextPart>();
            public string Note;
            public bool Heuristic;
            public Side(string status, string note = null, bool heuristic = false) { Status = status; Note = note; Heuristic = heuristic; }
        }

        public class Step
        {
            public int Index;
            public string SamplerClass, SamplerNode;
            public Side Positive, Negative;
        }

        class Unresolved : Exception { public Unresolved(string m) : base(m) { } }

        // --- JSON helpers (the Python relies on dict/list duck typing) ---------------

        public static bool IsLink(JToken value)
        {
            return value is JArray a && a.Count == 2
                && (a[0].Type == JTokenType.String || a[0].Type == JTokenType.Integer)
                && a[1].Type == JTokenType.Integer;
        }

        static string LinkNode(JToken link) => ((JArray)link)[0].ToString();
        static int LinkSlot(JToken link) => (int)((JArray)link)[1];
        static JObject Node(JObject graph, string nid) => graph[nid] as JObject;
        static JObject Inputs(JObject node) => node?["inputs"] as JObject ?? new JObject();
        static string ClassType(JObject node) => node?["class_type"]?.Type == JTokenType.String ? (string)node["class_type"] : "?";
        static bool IsStr(JToken t) => t != null && t.Type == JTokenType.String;

        /// <summary>Sort key for node ids, which are "6" or subgraph-flattened "37:13".</summary>
        static object[] NodeKey(string nodeId)
        {
            return nodeId.Split(':').Select(p => p.All(char.IsDigit) && p.Length > 0 ? (object)long.Parse(p) : p).ToArray();
        }

        static int CompareKeys(object[] a, object[] b)
        {
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                int c;
                if (a[i] is long la && b[i] is long lb) c = la.CompareTo(lb);
                else if (a[i] is long) c = -1;           // Python: int sorts before str would raise; ids are consistent in practice
                else if (b[i] is long) c = 1;
                else c = string.CompareOrdinal((string)a[i], (string)b[i]);
                if (c != 0) return c;
            }
            return a.Length.CompareTo(b.Length);
        }

        static IEnumerable<string> SortedIds(IEnumerable<string> ids) => ids.OrderBy(NodeKey, Comparer<object[]>.Create(CompareKeys));

        // --- text ---------------------------------------------------------------------

        /// <summary>Read one field from an rgthree context, following base_ctx for fields set upstream.</summary>
        static (string, List<string>) _context_field(JObject graph, JObject node, string fieldName, int depth)
        {
            var seen = new HashSet<string>();
            while (true)
            {
                var ins = Inputs(node);
                var value = ins[fieldName];
                if (IsStr(value))
                    return ((string)value, new List<string> { (string)value });
                if (IsLink(value))
                    return _resolve_string(graph, value, depth + 1);
                var baseCtx = ins["base_ctx"];
                if (!IsLink(baseCtx) || seen.Contains(LinkNode(baseCtx)))
                    throw new Unresolved($"rgthree context field {fieldName} is not set");
                seen.Add(LinkNode(baseCtx));
                node = Node(graph, LinkNode(baseCtx));
                if (node == null || !RGTHREE_CONTEXT_FIELDS.ContainsKey(ClassType(node)))
                    throw new Unresolved($"rgthree context field {fieldName} comes from an unreadable base context");
            }
        }

        /// <summary>Resolve a text input that arrives as a link. Returns (text, top-level pieces).</summary>
        static (string, List<string>) _resolve_string(JObject graph, JToken link, int depth = 0)
        {
            if (depth > MAX_HOPS)
                throw new Unresolved("text chain too deep");
            string nid = LinkNode(link);
            var node = Node(graph, nid);
            if (node == null)
                throw new Unresolved($"text links to missing node {nid}");
            if (RGTHREE_CONTEXT_FIELDS.TryGetValue(ClassType(node), out var fields))
            {
                if (LinkSlot(link) >= fields.Length)
                    throw new Unresolved($"text comes from output {LinkSlot(link)} of {ClassType(node)} node {nid}, which it doesn't have");
                return _context_field(graph, node, fields[LinkSlot(link)], depth);
            }
            var ins = Inputs(node);
            var pieces = new List<string>();
            foreach (var key in STRING_KEYS)
            {
                var value = ins[key];
                if (IsStr(value))
                    pieces.Add((string)value);
                else if (IsLink(value))
                    pieces.Add(_resolve_string(graph, value, depth + 1).Item1);
            }
            if (pieces.Count == 0)
                throw new Unresolved($"text comes from {ClassType(node)} node {nid}, which holds no readable text");
            if (string.Equals(ins["clean_whitespace"]?.ToString() ?? "", "true", StringComparison.OrdinalIgnoreCase))
                pieces = pieces.Select(p => p.Trim()).ToList();
            string joiner = JOINER_KEYS.Select(k => ins[k]).FirstOrDefault(IsStr)?.ToString() ?? "";
            return (string.Join(joiner, pieces), pieces);
        }

        static TextPart _text_part(JObject graph, string key, JToken value, string nodeClass, string nodeId)
        {
            if (IsStr(value))
                return new TextPart { Field = key, Text = (string)value, SourceClass = nodeClass, SourceNode = nodeId };
            var (text, pieces) = _resolve_string(graph, value);
            return new TextPart { Field = key, Text = text, SourceClass = nodeClass, SourceNode = nodeId, Pieces = pieces.Count > 1 ? pieces : null };
        }

        static List<string> _present(JObject ins, string[] keys) => keys.Where(k => IsStr(ins[k]) || IsLink(ins[k])).ToList();

        /// <summary>The TextParts an encoder feeds into output `slot`, or null if it holds no prompt text. slot=null lists every text field.</summary>
        static List<TextPart> _encoder_parts(JObject graph, string nodeId, JObject node, int? slot = null)
        {
            var ins = Inputs(node); string ct = ClassType(node);
            var keys = _present(ins, ENCODER_TEXT_KEYS); var negKeys = _present(ins, NEGATIVE_TEXT_KEYS);
            if (keys.Count == 0 && negKeys.Count == 0)
                return null;
            if (negKeys.Count > 0 && slot != null)
            {
                if (slot != 0 && slot != 1)
                    throw new Unresolved($"output {slot} of {ct} node {nodeId} is not a conditioning output");
                keys = slot == 0 ? keys : negKeys;
            }
            else if (slot == null)
                keys = keys.Concat(negKeys).ToList();
            return keys.Select(k => _text_part(graph, k, ins[k], ct, nodeId)).ToList();
        }

        // --- switches: which branch did the saved graph select? -------------------------

        /// <summary>Mirror DazzleSwitch.switch()'s resolution order (comfyui-dazzlenodes) on a saved graph.</summary>
        static (string key, string why, bool guessed) _dazzle_switch(JObject ins)
        {
            var connected = ins.Properties().Where(p => _DAZZLE_INPUT.IsMatch(p.Name) && IsLink(p.Value)).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (connected.Count == 0)
                return (null, "no inputs connected", false);
            var select = ins["select"]; var modeTok = ins["mode"]; var overrideTok = ins["select_override"];
            string mode = IsStr(modeTok) ? (string)modeTok : "priority";
            if (IsLink(select) || IsLink(overrideTok))
                return (null, "selection comes from another node", false);
            int ovr = overrideTok != null && overrideTok.Type == JTokenType.Integer ? (int)overrideTok : 0;
            string selectStr = IsStr(select) ? (string)select : null;
            bool dropdown = selectStr != "(none)" && selectStr != "(none connected)";

            if (ovr < 0 && -ovr <= connected.Count)
                return (connected[connected.Count + ovr], null, false);
            if (ovr > 0 && connected.Contains($"input_{ovr:00}"))
                return ($"input_{ovr:00}", null, false);
            if (dropdown && selectStr != null && connected.Contains(selectStr))
                return (selectStr, null, false);

            string requested = ovr > 0 ? $"input_{ovr:00}" : (dropdown && ovr == 0 ? selectStr : null);
            if (mode == "strict")
                return (null, "strict mode and the selected input is not connected", false);
            if (mode == "sequential")
            {
                var slots = connected.Select(k => int.Parse(_DAZZLE_INPUT.Match(k).Groups[1].Value)).ToList();
                if (requested != null && _DAZZLE_INPUT.IsMatch(requested))
                    slots.Add(int.Parse(_DAZZLE_INPUT.Match(requested).Groups[1].Value));
                var allSlots = Enumerable.Range(1, slots.Max()).Select(i => $"input_{i:00}").ToList();
                int start = requested != null && allSlots.Contains(requested) ? allSlots.IndexOf(requested) : 0;
                for (int i = requested == null ? 0 : 1; i < allSlots.Count; i++)
                {
                    string candidate = allSlots[(start + i) % allSlots.Count];
                    if (connected.Contains(candidate))
                        return (candidate, null, false);
                }
                return (null, "sequential mode found no connected input", false);
            }
            return (connected[0], null, false); // priority, and DazzleSwitch's own fallback for unknown modes
        }

        static (string, string, bool) _comfy_switch(JObject ins, JObject graph)
        {
            var flag = ins["switch"];
            if (IsLink(flag))
            {
                var source = Node(graph, LinkNode(flag));
                flag = source != null ? Inputs(source)["value"] : null;
            }
            if (flag == null || flag.Type != JTokenType.Boolean)
                return (null, "switch value comes from another node", false);
            string key = (bool)flag ? "on_true" : "on_false";
            return IsLink(ins[key]) ? (key, null, false) : (null, $"{key} is not connected", false);
        }

        static (string, string, bool) _rgthree_any_switch(JObject ins)
        {
            // rgthree picks the first input that is not None at runtime, which a saved graph
            // cannot show, so the first connected one is the best available answer.
            var connected = ins.Properties().Where(p => _RGTHREE_ANY.IsMatch(p.Name) && IsLink(p.Value)).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            return connected.Count > 0 ? (connected[0], null, true) : (null, "no inputs connected", false);
        }

        static (string, string, bool)? _switch_branch(JObject node, JObject graph)
        {
            string ct = ClassType(node); var ins = Inputs(node);
            if (ct == "DazzleSwitch") return _dazzle_switch(ins);
            if (ct == "ComfySwitchNode") return _comfy_switch(ins, graph);
            if (ct == "Any Switch (rgthree)") return _rgthree_any_switch(ins);
            return null;
        }

        // --- the walk -----------------------------------------------------------------

        static Side _walk(JObject graph, JToken link, string side, int depth = 0, bool heuristic = false)
        {
            if (depth > MAX_HOPS)
                return new Side("unresolved", "conditioning chain too deep", heuristic);
            string nid = LinkNode(link);
            var node = Node(graph, nid);
            if (node == null)
                return new Side("unresolved", $"link to missing node {nid}", heuristic);
            string ct = ClassType(node); var ins = Inputs(node);

            if (ZEROING_CLASSES.Contains(ct))
                return new Side("empty", $"zeroed by {ct} node {nid}", heuristic);
            List<TextPart> parts;
            try
            {
                parts = _encoder_parts(graph, nid, node, LinkSlot(link));
            }
            catch (Unresolved e)
            {
                return new Side("unresolved", e.Message, heuristic);
            }
            if (parts != null)
                return new Side("resolved", null, heuristic) { Parts = parts };

            var branch = _switch_branch(node, graph);
            if (branch != null)
            {
                var (key, why, guessed) = branch.Value;
                if (key == null)
                    return new Side("unresolved", $"{ct} node {nid}: {why}", heuristic);
                return _walk(graph, ins[key], side, depth + 1, heuristic || guessed);
            }

            // A pass-through carrying both sides: follow only the side being resolved.
            if (IsLink(ins[side]))
                return _walk(graph, ins[side], side, depth + 1, heuristic);
            // An rgthree context that doesn't set this side inherits it from its base context.
            if (RGTHREE_CONTEXT_FIELDS.ContainsKey(ct) && IsLink(ins["base_ctx"]))
                return _walk(graph, ins["base_ctx"], side, depth + 1, heuristic);

            var conds = ins.Properties().Select(p => p.Name).Where(k => k.StartsWith("conditioning") && IsLink(ins[k])).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (conds.Count == 1)
                return _walk(graph, ins[conds[0]], side, depth + 1, heuristic);
            if (conds.Count > 1)
            {
                // A combiner: every branch reaches the model, so every branch is shown.
                var subs = conds.Select(k => _walk(graph, ins[k], side, depth + 1, heuristic)).ToList();
                var failed = subs.Where(s => s.Status == "unresolved").ToList();
                if (failed.Count > 0)
                    return new Side("unresolved", string.Join("; ", failed.Select(s => s.Note)), heuristic);
                var all = subs.SelectMany(s => s.Parts).ToList();
                return new Side(all.Count > 0 ? "resolved" : "empty", all.Count > 0 ? null : $"every branch of {ct} node {nid} is zeroed",
                                subs.Any(s => s.Heuristic)) { Parts = all };
            }

            return new Side("unresolved", $"stopped at {ct} node {nid}", heuristic);
        }

        /// <summary>Every node reachable backwards from node_id along the links the walk can follow.</summary>
        static HashSet<string> _conditioning_ancestry(JObject graph, string nodeId)
        {
            var seen = new HashSet<string>(); var stack = new Stack<string>(); stack.Push(nodeId);
            while (stack.Count > 0)
            {
                var node = Node(graph, stack.Pop());
                if (node == null) continue;
                foreach (var p in Inputs(node).Properties())
                {
                    string key = p.Name;
                    bool follow = key == "positive" || key == "negative" || key == "on_true" || key == "on_false" || key == "base_ctx"
                                  || key.StartsWith("conditioning") || _DAZZLE_INPUT.IsMatch(key) || _RGTHREE_ANY.IsMatch(key);
                    if (follow && IsLink(p.Value) && seen.Add(LinkNode(p.Value)))
                        stack.Push(LinkNode(p.Value));
                }
            }
            return seen;
        }

        static HashSet<string> _full_ancestry(JObject graph, string nodeId)
        {
            var seen = new HashSet<string>(); var stack = new Stack<string>(); stack.Push(nodeId);
            while (stack.Count > 0)
            {
                var node = Node(graph, stack.Pop());
                if (node == null) continue;
                foreach (var p in Inputs(node).Properties())
                    if (IsLink(p.Value) && seen.Add(LinkNode(p.Value)))
                        stack.Push(LinkNode(p.Value));
            }
            return seen;
        }

        /// <summary>Order ids so a stage whose inputs come from another stage follows it.</summary>
        static List<string> _execution_order(JObject graph, List<string> ids)
        {
            var ancestry = ids.ToDictionary(i => i, i => _full_ancestry(graph, i));
            var remaining = SortedIds(ids).ToList(); var ordered = new List<string>();
            while (remaining.Count > 0)
            {
                var ready = remaining.Where(i => !remaining.Any(o => o != i && ancestry[i].Contains(o))).ToList();
                string nxt = ready.Count > 0 ? ready[0] : remaining[0]; // a cycle can't occur in a valid graph; don't hang if it does
                ordered.Add(nxt);
                remaining.Remove(nxt);
            }
            return ordered;
        }

        static Side _pair_side(JObject graph, string nodeId, JObject node, string key)
        {
            var value = Inputs(node)[key];
            try
            {
                var part = _text_part(graph, "text", value, ClassType(node), nodeId);
                return new Side("resolved") { Parts = new List<TextPart> { part } };
            }
            catch (Unresolved e)
            {
                return new Side("unresolved", e.Message);
            }
        }

        /// <summary>Return one Step per sampling stage in the graph, in execution order.</summary>
        public static List<Step> resolve_prompts(JObject graph)
        {
            var steps = new List<Step>();
            if (graph == null)
                return steps;
            var consumers = graph.Properties().Where(p => p.Value is JObject n && IsLink(Inputs(n)["positive"]) && IsLink(Inputs(n)["negative"])).Select(p => p.Name).ToList();
            // A consumer that sits on another consumer's conditioning path is a pass-through, not a stage of its own.
            var inner = new HashSet<string>(consumers.SelectMany(c => _conditioning_ancestry(graph, c)));
            var stages = consumers.Where(c => !inner.Contains(c)).ToList();
            var pairs = graph.Properties().Where(p => p.Value is JObject n && PAIR_KEYS.All(k => IsStr(Inputs(n)[k]) || IsLink(Inputs(n)[k]))).Select(p => p.Name).ToList();

            foreach (var nid in _execution_order(graph, stages.Concat(pairs).ToList()))
            {
                var node = Node(graph, nid);
                Side pos, neg;
                if (pairs.Contains(nid))
                {
                    pos = _pair_side(graph, nid, node, "positive_prompt"); neg = _pair_side(graph, nid, node, "negative_prompt");
                }
                else
                {
                    var ins = Inputs(node);
                    pos = _walk(graph, ins["positive"], "positive"); neg = _walk(graph, ins["negative"], "negative");
                }
                steps.Add(new Step { Index = steps.Count + 1, SamplerClass = ClassType(node), SamplerNode = nid, Positive = pos, Negative = neg });
            }
            return steps;
        }

        /// <summary>Every node holding prompt text, unlabelled -- the fallback when no stage is found.</summary>
        public static List<TextPart> text_nodes(JObject graph)
        {
            var found = new List<TextPart>();
            foreach (var nid in SortedIds(graph.Properties().Where(p => p.Value is JObject).Select(p => p.Name)))
            {
                try
                {
                    var parts = _encoder_parts(graph, nid, Node(graph, nid));
                    if (parts != null) found.AddRange(parts);
                }
                catch (Unresolved) { }
            }
            return found;
        }

        /// <summary>True for an API-format graph: {node_id: {"class_type": ..., "inputs": ...}}.</summary>
        public static bool is_prompt_graph(JToken data)
        {
            return data is JObject o && o.Count > 0 && o.Properties().All(p => p.Value is JObject n && n["class_type"] != null);
        }

        // --- repeats ------------------------------------------------------------------

        static string side_signature(Side side)
        {
            // A [heuristic] side is a guess at what ran, so it never counts as the same as a traced one.
            // Edge whitespace is ignored: a trailing newline is invisible and dropped by the tokenizer.
            if (side.Status == "resolved")
                return "resolved|" + string.Join("\u0001", side.Parts.Select(p => p.Field + "\u0002" + p.Text.Trim())) + "|" + side.Heuristic;
            if (side.Status == "unresolved")
                return "unresolved|" + side.Note + "|" + side.Heuristic;
            return "empty|" + side.Heuristic;
        }

        /// <summary>Per step, the index of the first earlier step equal to it as (whole, positive, negative), else null.</summary>
        public static List<(int? Whole, int? Pos, int? Neg)> duplicate_map(List<Step> steps)
        {
            var firstWhole = new Dictionary<string, int>(); var firstPos = new Dictionary<string, int>(); var firstNeg = new Dictionary<string, int>();
            var refs = new List<(int?, int?, int?)>();
            foreach (var step in steps)
            {
                string pos = side_signature(step.Positive), neg = side_signature(step.Negative), whole = pos + "\u0003" + neg;
                refs.Add((firstWhole.TryGetValue(whole, out int w) ? w : (int?)null,
                          firstPos.TryGetValue(pos, out int p) ? p : (int?)null,
                          firstNeg.TryGetValue(neg, out int n) ? n : (int?)null));
                if (!firstWhole.ContainsKey(whole)) firstWhole[whole] = step.Index;
                if (!firstPos.ContainsKey(pos)) firstPos[pos] = step.Index;
                if (!firstNeg.ContainsKey(neg)) firstNeg[neg] = step.Index;
            }
            return refs;
        }

        /// <summary>The same document `comfydbg prompt --json --no-prune` prints, for the port check and the pane.</summary>
        public static JObject to_json(string filePath, List<Step> steps, List<TextPart> fallback)
        {
            JObject PartJ(TextPart p)
            {
                var d = new JObject { ["field"] = p.Field, ["text"] = p.Text, ["source"] = new JObject { ["class"] = p.SourceClass, ["node"] = p.SourceNode } };
                if (p.Pieces != null) d["pieces"] = new JArray(p.Pieces);
                return d;
            }
            JObject SideJ(Side s, int? sameAs) => new JObject
            {
                ["status"] = s.Status, ["heuristic"] = s.Heuristic, ["note"] = s.Note == null ? JValue.CreateNull() : (JToken)s.Note,
                ["parts"] = new JArray(s.Parts.Select(PartJ)), ["same_as"] = sameAs == null ? JValue.CreateNull() : (JToken)sameAs
            };
            var refs = duplicate_map(steps);
            var docSteps = new JArray();
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i]; var (whole, posRef, negRef) = refs[i];
                docSteps.Add(new JObject
                {
                    ["step"] = s.Index, ["sampler"] = new JObject { ["class"] = s.SamplerClass, ["node"] = s.SamplerNode },
                    ["positive"] = SideJ(s.Positive, posRef), ["negative"] = SideJ(s.Negative, negRef),
                    ["same_as"] = whole == null ? JValue.CreateNull() : (JToken)whole
                });
            }
            var doc = new JObject { ["file"] = filePath, ["steps"] = docSteps };
            if (fallback != null) doc["unlabelled"] = new JArray(fallback.Select(PartJ));
            return doc;
        }

        // --- workflow-side fingerprint (comfydbg detect, the half that needs no ComfyUI install) ---

        public class Fingerprint
        {
            public string FrontendVersion = "(not set)", RendererVersion = "(not set)", BackendVersion = "(not set)";
            public int TotalNodes; public List<string> NodeTypes = new List<string>();
            public SortedDictionary<string, string> Packages = new SortedDictionary<string, string>(StringComparer.Ordinal); // cnr_id -> ver
        }

        /// <summary>Port of extract_workflow_versions(): reads the UI-format `workflow` chunk.</summary>
        public static Fingerprint extract_workflow_versions(JObject workflow)
        {
            var fp = new Fingerprint();
            var extra = workflow?["extra"] as JObject;
            if (extra?["frontendVersion"] != null && extra["frontendVersion"].Type != JTokenType.Null) fp.FrontendVersion = extra["frontendVersion"].ToString();
            if (extra?["workflowRendererVersion"] != null && extra["workflowRendererVersion"].Type != JTokenType.Null) fp.RendererVersion = extra["workflowRendererVersion"].ToString();
            var nodes = workflow?["nodes"] as JArray ?? new JArray();
            var types = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var n in nodes.OfType<JObject>())
            {
                var props = n["properties"] as JObject;
                string cnr = props?["cnr_id"]?.ToString() ?? "", ver = props?["ver"]?.ToString() ?? "";
                if (cnr.Length > 0 && ver.Length > 0 && !fp.Packages.ContainsKey(cnr)) fp.Packages[cnr] = ver;
                string t = n["type"]?.ToString() ?? "";
                if (t.Length > 0) types.Add(t);
            }
            fp.TotalNodes = nodes.Count;
            fp.NodeTypes = types.ToList();
            if (fp.Packages.TryGetValue("comfy-core", out var core)) fp.BackendVersion = core;
            return fp;
        }
    }
}
