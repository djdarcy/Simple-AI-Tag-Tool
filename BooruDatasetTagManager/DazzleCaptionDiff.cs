using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool: the diff between a caption and a proposed caption, at the grain a reviewer
    /// validates -- tags added, removed, changed, and words changed inside a sentence item. Pure: no UI.
    /// Tags are compared by DazzleRules.Normalize (case, '_' vs space, weights), so a reorder is never a
    /// change and "long_hair" vs "long hair" is the same tag -- the matching rule the rest of the tool uses.
    /// </summary>
    public static class DazzleCaptionDiff
    {
        public enum Kind { Same, Added, Removed, Changed }

        public sealed class Item
        {
            public string Text;                 // the raw item on this side
            public string Key;                  // Normalize(Text)
            public Kind Kind;
            public bool IsSentence;
            public int Pair = -1;               // index of the matching item on the other side, or -1
            public List<(string Word, Kind Kind)> Words;   // word diff, for a Changed sentence (this side's view)
            public override string ToString() => Kind + ":" + Text;
        }

        public sealed class Result
        {
            public List<Item> Left = new List<Item>();
            public List<Item> Right = new List<Item>();
            public int Added => Right.Count(i => i.Kind == Kind.Added);
            public int Removed => Left.Count(i => i.Kind == Kind.Removed);
            public int Changed => Right.Count(i => i.Kind == Kind.Changed);
            public bool NoChange => Added == 0 && Removed == 0 && Changed == 0;
            public string Summary => NoChange ? "no change proposed" : $"+{Added} added, -{Removed} removed, {Changed} changed";
        }

        /// <summary>Split a caption into items on commas (and newlines), trimmed, empties dropped.</summary>
        public static List<string> SplitItems(string caption)
        {
            return (caption ?? "").Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }

        /// <summary>A sentence item: six or more words, or a sentence-ending mark inside it.</summary>
        public static bool LooksLikeSentence(string item)
        {
            var words = Regex.Split(item.Trim(), @"\s+");
            return words.Length >= 6 || Regex.IsMatch(item.Trim(), @"[.!?;]\s+\S");
        }

        public static Result Compare(string left, string right, DazzleRules.TagMatch mode)
        {
            var res = new Result();
            res.Left = SplitItems(left).Select(t => new Item { Text = t, Key = DazzleRules.Normalize(t), Kind = Kind.Removed, IsSentence = LooksLikeSentence(t) }).ToList();
            res.Right = SplitItems(right).Select(t => new Item { Text = t, Key = DazzleRules.Normalize(t), Kind = Kind.Added, IsSentence = LooksLikeSentence(t) }).ToList();

            // 1. exact key matches (tags and identical sentences): Same, or Changed when only the raw text differs (weight, case, underscore)
            for (int r = 0; r < res.Right.Count; r++)
            {
                var ri = res.Right[r];
                int l = IndexOfUnpaired(res.Left, li => li.Key == ri.Key);
                if (l < 0) continue;
                Pair(res, l, r, string.Equals(res.Left[l].Text, ri.Text, StringComparison.Ordinal) ? Kind.Same : Kind.Changed);
            }
            // 2. lazy mode: a tag that is a word or phrase of a tag on the other side counts as the same tag, reworded
            if (mode == DazzleRules.TagMatch.Lazy)
            {
                for (int r = 0; r < res.Right.Count; r++)
                {
                    var ri = res.Right[r];
                    if (ri.Pair >= 0 || ri.IsSentence) continue;
                    int l = IndexOfUnpaired(res.Left, li => !li.IsSentence && (DazzleRules.TagSet.PhraseIn(ri.Key, li.Key) >= 0 || DazzleRules.TagSet.PhraseIn(li.Key, ri.Key) >= 0));
                    if (l >= 0) Pair(res, l, r, Kind.Changed);
                }
            }
            // 3. sentences: pair the closest unpaired sentence on the other side by word overlap, then word-diff it
            for (int r = 0; r < res.Right.Count; r++)
            {
                var ri = res.Right[r];
                if (ri.Pair >= 0 || !ri.IsSentence) continue;
                int best = -1; double bestScore = 0;
                for (int l = 0; l < res.Left.Count; l++)
                {
                    var li = res.Left[l];
                    if (li.Pair >= 0 || !li.IsSentence) continue;
                    double s = Jaccard(Words(li.Key), Words(ri.Key));
                    if (s > bestScore) { bestScore = s; best = l; }
                }
                if (best >= 0 && bestScore >= 0.3)
                {
                    Pair(res, best, r, Kind.Changed);
                    WordDiff(res.Left[best], ri);
                }
            }
            return res;
        }

        private static int IndexOfUnpaired(List<Item> items, Func<Item, bool> pred)
        {
            for (int i = 0; i < items.Count; i++) if (items[i].Pair < 0 && pred(items[i])) return i;
            return -1;
        }

        private static void Pair(Result res, int l, int r, Kind kind)
        {
            res.Left[l].Pair = r; res.Right[r].Pair = l;
            res.Left[l].Kind = kind; res.Right[r].Kind = kind;
        }

        private static List<string> Words(string s) => Regex.Split(s.Trim(), @"\s+").Where(w => w.Length > 0).ToList();

        private static double Jaccard(List<string> a, List<string> b)
        {
            var sa = new HashSet<string>(a); var sb = new HashSet<string>(b);
            int inter = sa.Intersect(sb).Count(); int union = sa.Union(sb).Count();
            return union == 0 ? 0 : (double)inter / union;
        }

        /// <summary>LCS over words of the raw texts; each side gets its own view (Same / Removed on the left, Same / Added on the right).</summary>
        private static void WordDiff(Item left, Item right)
        {
            var a = Words(left.Text); var b = Words(right.Text);
            int n = a.Count, m = b.Count;
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = string.Equals(a[i], b[j], StringComparison.OrdinalIgnoreCase) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            left.Words = new List<(string, Kind)>(); right.Words = new List<(string, Kind)>();
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (string.Equals(a[x], b[y], StringComparison.OrdinalIgnoreCase)) { left.Words.Add((a[x], Kind.Same)); right.Words.Add((b[y], Kind.Same)); x++; y++; }
                else if (lcs[x + 1, y] >= lcs[x, y + 1]) { left.Words.Add((a[x], Kind.Removed)); x++; }
                else { right.Words.Add((b[y], Kind.Added)); y++; }
            }
            while (x < n) { left.Words.Add((a[x], Kind.Removed)); x++; }
            while (y < m) { right.Words.Add((b[y], Kind.Added)); y++; }
        }

        /// <summary>Compose a caption from the right side with the given items dropped and the given left items kept (the per-chip accept).</summary>
        public static string Compose(Result res, ISet<int> dropRight, ISet<int> keepLeft)
        {
            var parts = new List<string>();
            for (int r = 0; r < res.Right.Count; r++)
                if (!dropRight.Contains(r)) parts.Add(res.Right[r].Text);
            for (int l = 0; l < res.Left.Count; l++)
                if (res.Left[l].Pair < 0 && keepLeft.Contains(l)) parts.Add(res.Left[l].Text);
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Keep current + add new (the person, 2026-10-02): every item of the current caption as it stands, in its
        /// order, then the proposal's added items, minus any chip dropped. Items the two share are not repeated, and an
        /// item the proposal rewrote keeps the current wording.
        /// </summary>
        public static string ComposeAppend(Result res, ISet<int> dropRight)
        {
            var parts = res.Left.Select(i => i.Text).ToList();
            for (int r = 0; r < res.Right.Count; r++)
                if (res.Right[r].Kind == Kind.Added && !dropRight.Contains(r)) parts.Add(res.Right[r].Text);
            return string.Join(", ", parts);
        }
    }
}
