using System;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Language-independent identification of Power BI Desktop's Quick Access
    /// Toolbar "Undo" button for the unsaved-changes probe (#82 solution A).
    ///
    /// The old probe matched the English label "Undo" only, so non-English installs
    /// (DE/FR/ES/…) never matched and fell back to Unknown (always prompting). This
    /// matches a curated set of localized Undo labels instead. A future refinement is
    /// a stable AutomationId match once one is confirmed — UiaDirtyStateProbe logs the
    /// AutomationId it sees so a real Desktop reveals it.
    /// </summary>
    public static class UndoButtonMatcher
    {
        // Localized "Undo" ribbon labels, prefix-matched case-insensitively
        // (button names are often "Undo (Ctrl+Z)" and similar).
        private static readonly string[] UndoLabels =
        {
            "undo",            // English
            "rückgängig",      // German
            "annuler",         // French
            "deshacer",        // Spanish
            "annulla",         // Italian
            "desfazer",        // Portuguese
            "ongedaan maken",  // Dutch
            "ångra",           // Swedish
            "fortryd",         // Danish
            "angre",           // Norwegian
            "cofnij",          // Polish
            "zpět",            // Czech
            "отменить",        // Russian
            "geri al",         // Turkish
            "撤消",             // Chinese (Simplified)
            "復原",             // Chinese (Traditional)
            "元に戻す",         // Japanese
            "실행 취소"          // Korean
        };

        /// <summary>True if the UIA control name matches an Undo button in any known language.</summary>
        public static bool IsUndo(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            var n = name.TrimStart();
            foreach (var label in UndoLabels)
                if (n.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }
    }
}
