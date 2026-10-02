using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool: conditional rules over an image's tags.
    ///
    /// A rule is one line:  condition => tag, tag, -tag
    /// The condition is a small boolean language over the tags the image has:
    ///   tag            the image has this tag (matched by the current TagMatch mode)
    ///   !x  not x      negation
    ///   a & b  a and b both
    ///   a | b  a or b  either
    ///   ( ... )        grouping
    ///   ~"regex"       some tag matches the .NET regex (case-insensitive, on the normalised tag);
    ///                  the quote must follow the tilde directly, otherwise ~ is part of a tag
    ///   "a tag"        quoting, for tags holding spaces or operator characters
    /// Precedence: ! then & then |. A line without "=>" has no condition: it is always active
    /// (the Check for box's entries are exactly that). A "#" line is a comment.
    ///
    /// Evaluation: a rule whose condition is false is dormant and says nothing. An active
    /// rule's wanted tags must be present and its -tags absent; a tag wanted by one active
    /// rule and forbidden by another is a conflict.
    /// </summary>
    public static class DazzleRules
    {
        public enum TagMatch { Strict, Lazy }

        // ------------------------------------------------------------------ matching

        /// <summary>The form two tags are compared in: lower-case, '_' as space, weights and escapes removed.</summary>
        public static string Normalize(string tag)
        {
            string t = (tag ?? "").Trim();
            var m = WeightedTag.Match(t);
            if (m.Success) t = m.Groups[1].Value;
            t = t.Replace("\\(", "(").Replace("\\)", ")").Replace('_', ' ');
            return Regex.Replace(t, @"\s+", " ").Trim().ToLowerInvariant();
        }

        private static readonly Regex WeightedTag = new Regex(@"^\(+(.*?)(?::\s*[+-]?[\d.]+)?\)+$", RegexOptions.Compiled);

        /// <summary>The image's tags, normalised, as the evaluator sees them. Built once per image.</summary>
        public sealed class TagSet
        {
            public readonly List<string> Tags;            // normalised, in caption order
            private readonly HashSet<string> set;
            public readonly TagMatch Mode;

            public TagSet(IEnumerable<string> rawTags, TagMatch mode)
            {
                Tags = rawTags.Select(Normalize).Where(t => t.Length > 0).ToList();
                set = new HashSet<string>(Tags);
                Mode = mode;
            }

            /// <summary>Does the image satisfy this term? Strict: a whole tag equals it. Lazy: it appears as a whole word or phrase inside any tag.</summary>
            public bool Has(string normalizedTerm)
            {
                if (normalizedTerm.Length == 0) return false;
                if (set.Contains(normalizedTerm)) return true;
                if (Mode != TagMatch.Lazy) return false;
                return Tags.Any(t => PhraseIn(normalizedTerm, t) >= 0);
            }

            /// <summary>Lazy mode: the start of the term inside the tag at a word boundary, or -1.</summary>
            public static int PhraseIn(string term, string tag)
            {
                int from = 0;
                while (from <= tag.Length - term.Length)
                {
                    int i = tag.IndexOf(term, from, StringComparison.Ordinal);
                    if (i < 0) return -1;
                    bool startOk = i == 0 || !IsWordChar(tag[i - 1]);
                    bool endOk = i + term.Length == tag.Length || !IsWordChar(tag[i + term.Length]);
                    if (startOk && endOk) return i;
                    from = i + 1;
                }
                return -1;
            }

            private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

            public bool AnyMatches(Regex re) => Tags.Any(t => re.IsMatch(t));
        }

        // ------------------------------------------------------------------ the expression tree

        public abstract class Node
        {
            public abstract bool Eval(TagSet tags);
            public abstract string Show();
        }

        sealed class TagNode : Node
        {
            public readonly string Term; // normalised
            public TagNode(string term) { Term = term; }
            public override bool Eval(TagSet tags) => tags.Has(Term);
            public override string Show() => Term.Any(c => " &|!()#\"".IndexOf(c) >= 0) ? "\"" + Term + "\"" : Term;
        }

        sealed class RegexNode : Node
        {
            public readonly Regex Re; public readonly string Pattern;
            public RegexNode(string pattern) { Pattern = pattern; Re = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)); }
            public override bool Eval(TagSet tags) { try { return tags.AnyMatches(Re); } catch (RegexMatchTimeoutException) { return false; } }
            public override string Show() => "~\"" + Pattern + "\"";
        }

        sealed class NotNode : Node
        {
            public readonly Node Inner;
            public NotNode(Node inner) { Inner = inner; }
            public override bool Eval(TagSet tags) => !Inner.Eval(tags);
            public override string Show() => "!" + (Inner is TagNode || Inner is RegexNode || Inner is NotNode ? Inner.Show() : "(" + Inner.Show() + ")");
        }

        sealed class AndNode : Node
        {
            public readonly Node L, R;
            public AndNode(Node l, Node r) { L = l; R = r; }
            public override bool Eval(TagSet tags) => L.Eval(tags) && R.Eval(tags);
            public override string Show() => Wrap(L) + " & " + Wrap(R);
            static string Wrap(Node n) => n is OrNode ? "(" + n.Show() + ")" : n.Show();
        }

        sealed class OrNode : Node
        {
            public readonly Node L, R;
            public OrNode(Node l, Node r) { L = l; R = r; }
            public override bool Eval(TagSet tags) => L.Eval(tags) || R.Eval(tags);
            public override string Show() => L.Show() + " | " + R.Show();
        }

        sealed class TrueNode : Node
        {
            public override bool Eval(TagSet tags) => true;
            public override string Show() => "";
        }

        // ------------------------------------------------------------------ parsing

        public class ParseException : Exception
        {
            public readonly int Column;
            public ParseException(string message, int column) : base(message) { Column = column; }
        }

        /// <summary>One consequence: a tag that must be present (Wanted) or absent (!Wanted).</summary>
        public sealed class Consequence
        {
            public string Term;     // normalised
            public string Raw;      // as typed, for display
            public bool Wanted;
            public override string ToString() => (Wanted ? "" : "-") + Raw;
        }

        public sealed class Rule
        {
            public string Line;                 // the line as typed (what the file stores)
            public string ConditionText = "";   // left of =>, as typed ("" = unconditional)
            public string ThenText = "";        // right of =>, as typed
            public Node Condition;              // null when Error != null
            public List<Consequence> Consequences = new List<Consequence>();
            public string Error;                // parse error message, or null
            public int ErrorColumn;
            public bool IsComment;
            public bool IsEmpty => Error == null && !IsComment && Condition == null && Consequences.Count == 0;
        }

        /// <summary>Parse one line. Never throws: a bad line comes back with Error set and its text kept.</summary>
        public static Rule ParseLine(string line)
        {
            var rule = new Rule { Line = line ?? "" };
            string text = rule.Line.Trim();
            if (text.Length == 0) return rule;
            if (text.StartsWith("#")) { rule.IsComment = true; return rule; }
            int arrow = IndexOfArrow(text);
            string cond, then;
            if (arrow < 0) { cond = ""; then = text; }
            else { cond = text.Substring(0, arrow).Trim(); then = text.Substring(arrow + 2).Trim(); }
            rule.ConditionText = cond;
            rule.ThenText = then;
            try
            {
                rule.Condition = cond.Length == 0 ? new TrueNode() : new Parser(cond).ParseAll();
                rule.Consequences = ParseConsequences(then, arrow < 0 ? 0 : arrow + 2);
                if (rule.Consequences.Count == 0 && cond.Length > 0)
                    throw new ParseException("nothing after '=>': list the tags that should (or, with '-', should not) be present", arrow + 2);
            }
            catch (ParseException e)
            {
                rule.Condition = null;
                rule.Consequences.Clear();
                rule.Error = e.Message;
                rule.ErrorColumn = e.Column;
            }
            return rule;
        }

        /// <summary>Build a rule line from the grid's two cells.</summary>
        public static string Compose(string condition, string then)
        {
            condition = (condition ?? "").Trim(); then = (then ?? "").Trim();
            return condition.Length == 0 ? then : condition + " => " + then;
        }

        /// <summary>"=>" outside quotes, or -1.</summary>
        private static int IndexOfArrow(string text)
        {
            bool inQuote = false;
            for (int i = 0; i < text.Length - 1; i++)
            {
                if (text[i] == '"') inQuote = !inQuote;
                else if (!inQuote && text[i] == '=' && text[i + 1] == '>') return i;
            }
            return -1;
        }

        private static List<Consequence> ParseConsequences(string then, int baseColumn)
        {
            var list = new List<Consequence>();
            foreach (var piece in SplitTopLevel(then, ','))
            {
                string raw = piece.Trim();
                if (raw.Length == 0) continue;
                bool wanted = true;
                if (raw.StartsWith("-")) { wanted = false; raw = raw.Substring(1).Trim(); }
                if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"') raw = raw.Substring(1, raw.Length - 2);
                string term = Normalize(raw);
                if (term.Length == 0) throw new ParseException("a '-' with no tag after it", baseColumn);
                list.Add(new Consequence { Raw = raw, Term = term, Wanted = wanted });
            }
            return list;
        }

        private static IEnumerable<string> SplitTopLevel(string s, char sep)
        {
            var sb = new StringBuilder(); bool inQuote = false;
            foreach (char c in s)
            {
                if (c == '"') { inQuote = !inQuote; sb.Append(c); }
                else if (c == sep && !inQuote) { yield return sb.ToString(); sb.Clear(); }
                else sb.Append(c);
            }
            yield return sb.ToString();
        }

        /// <summary>Recursive descent over:  or := and ('|' and)* ;  and := not ('&' not)* ;  not := '!' not | atom ;  atom := '(' or ')' | ~"re" | "tag" | tag</summary>
        private sealed class Parser
        {
            private readonly string s; private int i;
            public Parser(string text) { s = text; }

            public Node ParseAll()
            {
                var n = ParseOr();
                SkipWs();
                if (i < s.Length) throw new ParseException($"unexpected '{s[i]}'", i + 1);
                return n;
            }

            private Node ParseOr()
            {
                var left = ParseAnd();
                while (true)
                {
                    SkipWs();
                    if (TryOp("|") || TryWord("or")) { var right = ParseAnd(); left = new OrNode(left, right); }
                    else return left;
                }
            }

            private Node ParseAnd()
            {
                var left = ParseNot();
                while (true)
                {
                    SkipWs();
                    if (TryOp("&") || TryWord("and")) { var right = ParseNot(); left = new AndNode(left, right); }
                    else return left;
                }
            }

            private Node ParseNot()
            {
                SkipWs();
                if (TryOp("!") || TryWord("not")) return new NotNode(ParseNot());
                return ParseAtom();
            }

            private Node ParseAtom()
            {
                SkipWs();
                if (i >= s.Length) throw new ParseException("expected a tag", i + 1);
                char c = s[i];
                if (c == '(')
                {
                    i++;
                    var inner = ParseOr();
                    SkipWs();
                    if (i >= s.Length || s[i] != ')') throw new ParseException("missing ')'", i + 1);
                    i++;
                    return inner;
                }
                if (c == ')') throw new ParseException("unexpected ')'", i + 1);
                if (c == '~' && i + 1 < s.Length && s[i + 1] == '"')
                {
                    int start = i; i++;
                    string pattern = ReadQuoted();
                    try { return new RegexNode(pattern); }
                    catch (ArgumentException e) { throw new ParseException("bad regex: " + e.Message, start + 1); }
                }
                if (c == '"')
                {
                    string q = ReadQuoted();
                    string term = Normalize(q);
                    if (term.Length == 0) throw new ParseException("empty quoted tag", i);
                    return new TagNode(term);
                }
                // a bare tag runs until an operator, a parenthesis, or a double space-separated keyword
                int from = i;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char ch = s[i];
                    if (ch == '&' || ch == '|' || ch == '(' || ch == ')' || ch == '"') break;
                    if (ch == '!' && sb.Length > 0 && !char.IsWhiteSpace(s[i - 1])) { sb.Append(ch); i++; continue; } // "cool!" mid-tag is a tag
                    if (ch == '!') break;
                    if (char.IsWhiteSpace(ch) && IsKeywordAhead()) break;
                    sb.Append(ch); i++;
                }
                string tag = Normalize(sb.ToString());
                if (tag.Length == 0) throw new ParseException("expected a tag", from + 1);
                return new TagNode(tag);
            }

            /// <summary>At whitespace: is the next word 'and' / 'or' (so the bare tag ends here)?</summary>
            private bool IsKeywordAhead()
            {
                int j = i;
                while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                foreach (var kw in new[] { "and", "or" })
                    if (j + kw.Length <= s.Length && string.Compare(s, j, kw, 0, kw.Length, StringComparison.OrdinalIgnoreCase) == 0
                        && (j + kw.Length == s.Length || char.IsWhiteSpace(s[j + kw.Length]) || "(!\"~".IndexOf(s[j + kw.Length]) >= 0))
                        return true;
                return false;
            }

            private string ReadQuoted()
            {
                int start = i; i++; // opening quote
                var sb = new StringBuilder();
                while (i < s.Length && s[i] != '"') { sb.Append(s[i]); i++; }
                if (i >= s.Length) throw new ParseException("missing closing quote", start + 1);
                i++;
                return sb.ToString();
            }

            private void SkipWs() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            private bool TryOp(string op)
            {
                if (string.CompareOrdinal(s, i, op, 0, op.Length) == 0) { i += op.Length; return true; }
                return false;
            }

            private bool TryWord(string word)
            {
                if (i + word.Length > s.Length) return false;
                if (string.Compare(s, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
                int after = i + word.Length;
                if (after < s.Length && !char.IsWhiteSpace(s[after]) && "(!\"~".IndexOf(s[after]) < 0) return false;
                if (i > 0 && !char.IsWhiteSpace(s[i - 1]) && ")".IndexOf(s[i - 1]) < 0) return false;
                i = after;
                return true;
            }
        }

        // ------------------------------------------------------------------ evaluation

        public enum TermState { Present, Missing, ForbiddenPresent, Conflict }

        public sealed class ConsequenceResult
        {
            public Consequence Consequence;
            public TermState State;
            public bool Ok => State == TermState.Present && Consequence.Wanted || State == TermState.Missing && !Consequence.Wanted;
        }

        public sealed class RuleResult
        {
            public Rule Rule;
            public bool Active;                 // condition true (always true for an unconditional rule)
            public List<ConsequenceResult> Consequences = new List<ConsequenceResult>();
            public bool HasProblem => Active && Consequences.Any(c => !c.Ok || c.State == TermState.Conflict);
            public bool HasConflict => Consequences.Any(c => c.State == TermState.Conflict);
        }

        public sealed class Evaluation
        {
            public List<RuleResult> Rules = new List<RuleResult>();
            public HashSet<string> Required = new HashSet<string>();   // terms wanted by active rules
            public HashSet<string> Forbidden = new HashSet<string>();  // terms forbidden by active rules
            public HashSet<string> Conflicts = new HashSet<string>();  // in both
            public int ProblemCount => Rules.Count(r => r.HasProblem);
            public int ActiveCount => Rules.Count(r => r.Active);
        }

        /// <summary>Evaluate every rule against one image's tags.</summary>
        public static Evaluation Evaluate(IEnumerable<Rule> rules, TagSet tags)
        {
            var ev = new Evaluation();
            var usable = rules.Where(r => r.Error == null && !r.IsComment && !r.IsEmpty).ToList();
            foreach (var rule in usable)
            {
                var rr = new RuleResult { Rule = rule, Active = rule.Condition.Eval(tags) };
                if (rr.Active)
                    foreach (var c in rule.Consequences)
                        (c.Wanted ? ev.Required : ev.Forbidden).Add(c.Term);
                ev.Rules.Add(rr);
            }
            ev.Conflicts.UnionWith(ev.Required.Intersect(ev.Forbidden));
            foreach (var rr in ev.Rules)
            {
                if (!rr.Active) continue;
                foreach (var c in rr.Rule.Consequences)
                {
                    bool present = tags.Has(c.Term);
                    TermState state = ev.Conflicts.Contains(c.Term) ? TermState.Conflict
                                    : c.Wanted ? (present ? TermState.Present : TermState.Missing)
                                    : (present ? TermState.ForbiddenPresent : TermState.Missing);
                    rr.Consequences.Add(new ConsequenceResult { Consequence = c, State = state });
                }
            }
            return ev;
        }

        /// <summary>One line of plain words for the Result column.</summary>
        public static string Describe(RuleResult rr)
        {
            if (!rr.Active) return "dormant";
            var missing = rr.Consequences.Where(c => c.State == TermState.Missing && c.Consequence.Wanted).Select(c => c.Consequence.Raw).ToList();
            var present = rr.Consequences.Where(c => c.State == TermState.ForbiddenPresent).Select(c => c.Consequence.Raw).ToList();
            var conflict = rr.Consequences.Where(c => c.State == TermState.Conflict).Select(c => c.Consequence.Raw).ToList();
            var parts = new List<string>();
            if (missing.Count > 0) parts.Add(missing.Count + " missing: " + string.Join(", ", missing));
            if (present.Count > 0) parts.Add(present.Count + " should not be here: " + string.Join(", ", present));
            if (conflict.Count > 0) parts.Add("conflict: " + string.Join(", ", conflict));
            return parts.Count == 0 ? "ok" : string.Join(";  ", parts);
        }
    }
}
