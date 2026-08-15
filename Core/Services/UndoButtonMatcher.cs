using System;

namespace PBIRelay.Services
{
    /// <summary>
    /// Language-independent identification of Power BI Desktop's Quick Access
    /// Toolbar "Undo" button for the unsaved-changes probe (#82 solution A).
    ///
    /// The old probe matched the English label "Undo" only, so non-English installs
    /// (DE/FR/ES/…) never matched and fell back to Unknown (always prompting).
    ///
    /// The button is now identified primarily by its stable UIA <c>AutomationId</c>
    /// (<c>"undo"</c>, confirmed on a real Desktop) — the id is not localized, so this
    /// works in any UI language. The curated localized-label set is kept as a fallback
    /// for any future Desktop that changes or omits the id (behavior then degrades to
    /// the label match, never worse). See <see cref="Matches"/>.
    /// </summary>
    public static class UndoButtonMatcher
    {
        /// <summary>
        /// Power BI Desktop's stable, non-localized AutomationId for the Quick Access
        /// Toolbar Undo button. Confirmed on a live Desktop (logged by the probe, #82).
        /// </summary>
        private const string UndoAutomationId = "undo";
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

        /// <summary>
        /// True if this control is the Undo button, matched by stable AutomationId
        /// first (preferred, language-independent) then by localized label (fallback).
        /// This is what the probe should call.
        /// </summary>
        public static bool Matches(string automationId, string name) =>
            IsUndoAutomationId(automationId) || IsUndo(name);

        /// <summary>True if the UIA AutomationId is Desktop's stable Undo command id.</summary>
        public static bool IsUndoAutomationId(string automationId) =>
            !string.IsNullOrWhiteSpace(automationId)
            && automationId.Trim().Equals(UndoAutomationId, StringComparison.OrdinalIgnoreCase);

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
