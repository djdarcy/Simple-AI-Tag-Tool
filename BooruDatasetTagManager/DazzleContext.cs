using System;
using System.Collections.Generic;
using System.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// What may go to the model besides the skill, the caption and the image: the folder's rules and the Review mode's
    /// Check for list. Nothing is sent automatically unless the person turns it on (Settings > AI, or the strip's Context
    /// dropdown for this session), and each piece goes with a framing sentence the person can edit. The defaults are the
    /// wording that tested clean on 2026-10-02 (tools\refine-replay.py, variant D). Plan addenda 19:32 and 19:34.
    /// Only what applies to this image is sent (djdarcy, 2026-10-03; tools\framing-check.py): the rules whose left side the
    /// caption has, and from the Check for list only the unwanted (-tag) entries. A listed tag that does not apply is not an
    /// instruction, only a word that leaks: the full Check for list put a false "1girl" into 6 of 10 replies on a male
    /// demon, the unwanted-only list into none.
    /// </summary>
    public static class DazzleContext
    {
        public const string DefaultRulesFraming =
            "The folder's rules, for checking only. Each says: if the caption contains the tag on the left, it should also contain the tags on the right " +
            "(a leading dash before a tag (i.e. \"-BadTag\") means the tag must be absent). A rule says nothing about an image whose caption does not " +
            "contain its left side; never add a left-side tag because a rule mentions it.";

        public const string DefaultUnwantedFraming =
            "Unwanted tags for this dataset: never include them, and remove any the current caption has.";

        /// <summary>The framed block for the rules, or "" when there are none.</summary>
        public static string RulesBlock(IEnumerable<string> ruleLines, string framing)
        {
            var lines = ruleLines?.Where(l => !string.IsNullOrWhiteSpace(l)).ToList() ?? new List<string>();
            if (lines.Count == 0) return "";
            return (string.IsNullOrWhiteSpace(framing) ? DefaultRulesFraming : framing.Trim()) + "\n" + string.Join("\n", lines);
        }

        /// <summary>The framed block of the Check for list's unwanted tags (its "-tag" entries, sent without the dash), or "" when it has none.</summary>
        public static string UnwantedBlock(string checkText, string framing)
        {
            string list = string.Join(", ", Unwanted(checkText));
            if (list.Length == 0) return "";
            return (string.IsNullOrWhiteSpace(framing) ? DefaultUnwantedFraming : framing.Trim()) + "\n" + list;
        }

        /// <summary>The Check for list's unwanted entries, "-watermark" read as "watermark".</summary>
        public static List<string> Unwanted(string checkText) =>
            Checks(checkText).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())
                .Where(s => s.StartsWith("-") && s.Length > 1).Select(s => s.Substring(1).Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>The Check for box's text as one comma-separated line.</summary>
        public static string Checks(string checkText) =>
            string.Join(", ", (checkText ?? "").Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0));

        public static int CheckCount(string checkText) => Checks(checkText).Length == 0 ? 0 : Checks(checkText).Split(',').Length;

        /// <summary>A placeholder in the skill wins over the automatic copy, so the model never sees a piece twice under two framings.</summary>
        public static bool SkillPlaces(string skillText, string placeholder) =>
            skillText != null && skillText.IndexOf("{" + placeholder, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>"also sending: rules (1 of 3 apply to this image), unwanted tags (2)" -- or the sentence that nothing else goes.
        /// checkCount is the number of unwanted tags; when the skill places {checks} it inserts the whole list itself.</summary>
        public static string Describe(bool rules, int ruleCount, bool checks, int checkCount, bool rulesPlaced, bool checksPlaced, int rulesApplying = -1)
        {
            var parts = new List<string>();
            if (rules) parts.Add(rulesPlaced ? "rules: placed by the skill"
                : ruleCount == 0 ? "rules (none in this folder)"
                : rulesApplying < 0 ? "rules (" + ruleCount + ")"
                : "rules (" + rulesApplying + " of " + ruleCount + " apply to this image)");
            if (checks) parts.Add(checksPlaced ? "Check for: placed by the skill" : checkCount == 0 ? "unwanted tags (none in Check for)" : "unwanted tags (" + checkCount + ")");
            return parts.Count == 0 ? "sending: the image, the skill and the caption, nothing else" : "also sending: " + string.Join(", ", parts);
        }
    }
}
