using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
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
    }
}
