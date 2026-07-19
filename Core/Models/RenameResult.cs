namespace PBIPortWrapper.Models
{
    public class RenameResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string WarningMessage { get; set; }

        public static RenameResult Fail(string message) => new RenameResult { Success = false, Message = message };
        public static RenameResult Ok(string message, string warning = null) => new RenameResult { Success = true, Message = message, WarningMessage = warning };
    }
}
