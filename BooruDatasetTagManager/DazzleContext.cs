using System;
using System.Collections.Generic;
using System.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// What may go to the model besides the skill, the caption and the image: the folder's rules and the Review mode's
    /// Check for list. Nothing is sent automatically unless the person turns it on (Settings > AI, or the strip's Context
    /// dropdown for this session), and each piece goes with a framing sentence the person can edit. The defaults are the
    /// wording that tested clean on 2026-10-02 (tools\refine-replay.py, variant D): told plainly that the Check for list
    /// is not a list of tags to add, the model stopped adding them. Plan addenda 19:32 and 19:34.
    /// </summary>
    public static class DazzleContext
    {
        public const string DefaultRulesFraming =
            "The folder's rules, for checking only. Each says: if the caption contains the tag on the left, it should also contain the tags on the right " +
            "(a leading - means the tag must be absent). A rule says nothing about an image whose caption does not contain its left side; never add a " +
            "left-side tag because a rule mentions it.";

        public const string DefaultChecksFraming =
            "Tags the person is checking this dataset for (a leading - means unwanted). These are not tags to add: include one only if it is true of " +
            "this image and the instruction calls for it; never include an unwanted one.";

        /// <summary>The framed block for the rules, or "" when there are none.</summary>
        public static string RulesBlock(IEnumerable<string> ruleLines, string framing)
        {
            var lines = ruleLines?.Where(l => !string.IsNullOrWhiteSpace(l)).ToList() ?? new List<string>();
            if (lines.Count == 0) return "";
            return (string.IsNullOrWhiteSpace(framing) ? DefaultRulesFraming : framing.Trim()) + "\n" + string.Join("\n", lines);
        }

        /// <summary>The framed block for the Check for list, or "" when it is empty.</summary>
        public static string ChecksBlock(string checkText, string framing)
        {
            string list = Checks(checkText);
            if (list.Length == 0) return "";
            return (string.IsNullOrWhiteSpace(framing) ? DefaultChecksFraming : framing.Trim()) + "\n" + list;
        }

        /// <summary>The Check for box's text as one comma-separated line.</summary>
        public static string Checks(string checkText) =>
            string.Join(", ", (checkText ?? "").Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0));

        public static int CheckCount(string checkText) => Checks(checkText).Length == 0 ? 0 : Checks(checkText).Split(',').Length;

        /// <summary>A placeholder in the skill wins over the automatic copy, so the model never sees a piece twice under two framings.</summary>
        public static bool SkillPlaces(string skillText, string placeholder) =>
            skillText != null && skillText.IndexOf("{" + placeholder, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>"also sending: rules (2), Check for (4 tags)" -- or the sentence that nothing else goes.</summary>
        public static string Describe(bool rules, int ruleCount, bool checks, int checkCount, bool rulesPlaced, bool checksPlaced)
        {
            var parts = new List<string>();
            if (rules) parts.Add(rulesPlaced ? "rules: placed by the skill" : "rules (" + ruleCount + (ruleCount == 0 ? ", none in this folder" : "") + ")");
            if (checks) parts.Add(checksPlaced ? "Check for: placed by the skill" : "Check for (" + checkCount + (checkCount == 1 ? " tag" : " tags") + ")");
            return parts.Count == 0 ? "sending: the image, the skill and the caption, nothing else" : "also sending: " + string.Join(", ", parts);
        }
    }
}
