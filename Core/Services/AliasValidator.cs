using System.Text.RegularExpressions;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Grid-independent validation for stable database aliases. Single source of
    /// the name rules; DatabaseRenameService validates through these methods before
    /// touching the instance.
    /// </summary>
    public static class AliasValidator
    {
        public const int MaxLength = 100;

        private static readonly Regex ValidNameRegex = new Regex(@"^[a-zA-Z0-9_\-\. ]+$", RegexOptions.Compiled);

        public static (bool IsValid, string ErrorMessage) ValidateAlias(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return (false, "New name cannot be empty.");

            if (alias.Length > MaxLength)
                return (false, $"New name is too long (max {MaxLength} chars).");

            if (!ValidNameRegex.IsMatch(alias))
                return (false, "Name contains invalid characters. Use only letters, numbers, spaces, underscores, hyphens, and dots.");

            return (true, string.Empty);
        }
    }
}
