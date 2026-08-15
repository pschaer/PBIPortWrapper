using PBIRelay.Services;
using Xunit;

namespace PBIRelay.Core.Tests
{
    /// <summary>
    /// Covers the language-independent Undo-button match (#82 solution A): the probe
    /// no longer depends on the English label "Undo", so the Clean fast-path works on
    /// localized Power BI Desktop installs instead of falling back to a prompt.
    /// </summary>
    public sealed class UndoButtonMatcherTests
    {
        [Theory]
        [InlineData("Undo (Ctrl+Z)")]   // English, with the shortcut suffix
        [InlineData("undo")]            // case-insensitive
        [InlineData("Rückgängig")]      // German
        [InlineData("Annuler")]         // French
        [InlineData("Deshacer")]        // Spanish
        [InlineData("Annulla")]         // Italian
        [InlineData("Desfazer")]        // Portuguese
        [InlineData("元に戻す")]         // Japanese
        [InlineData("撤消")]             // Chinese (Simplified)
        public void IsUndo_matches_localized_undo_labels(string name)
        {
            Assert.True(UndoButtonMatcher.IsUndo(name));
        }

        [Theory]
        [InlineData("Redo")]
        [InlineData("Save")]
        [InlineData("RückgängZZ")]      // near-miss that must not match
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void IsUndo_rejects_non_undo_labels(string name)
        {
            Assert.False(UndoButtonMatcher.IsUndo(name));
        }

        [Fact]
        public void IsUndo_tolerates_leading_whitespace()
        {
            Assert.True(UndoButtonMatcher.IsUndo("   Undo"));
        }

        [Theory]
        [InlineData("undo")]
        [InlineData("UNDO")]   // AutomationId compared case-insensitively
        [InlineData(" undo ")] // trimmed
        public void IsUndoAutomationId_matches_the_stable_id(string id)
        {
            Assert.True(UndoButtonMatcher.IsUndoAutomationId(id));
        }

        [Theory]
        [InlineData("redo")]
        [InlineData("undoButton")] // must be exact, not a prefix
        [InlineData("")]
        [InlineData(null)]
        public void IsUndoAutomationId_rejects_other_ids(string id)
        {
            Assert.False(UndoButtonMatcher.IsUndoAutomationId(id));
        }

        [Fact]
        public void Matches_uses_the_automationid_even_when_the_label_is_unrecognized()
        {
            // A Desktop language none of the curated labels cover: the id still wins.
            Assert.True(UndoButtonMatcher.Matches("undo", "visszavonás")); // Hungarian, not in the label set
        }

        [Fact]
        public void Matches_falls_back_to_the_localized_label_when_id_is_absent()
        {
            Assert.True(UndoButtonMatcher.Matches(null, "Rückgängig"));
            Assert.True(UndoButtonMatcher.Matches("", "Undo (Ctrl+Z)"));
        }

        [Fact]
        public void Matches_rejects_a_non_undo_control()
        {
            Assert.False(UndoButtonMatcher.Matches("redo", "Redo"));
        }
    }
}
