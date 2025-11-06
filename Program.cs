using System;
using System.Windows.Forms;

namespace PBIPortWrapper
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Set up global exception handlers
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }

        private static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            HandleException(e.Exception);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                HandleException(ex);
            }
        }

        private static void HandleException(Exception ex)
        {
            string errorMessage = $"An unexpected error occurred:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}";

            MessageBox.Show(
                errorMessage,
                "PBI Port Wrapper - Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );

            // Optionally log to file
            try
            {
                string logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PBIPortWrapper",
                    "crash.log"
                );

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                System.IO.File.AppendAllText(
                    logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CRASH: {ex.Message}\n{ex.StackTrace}\n\n"
                );
            }
            catch
            {
                // If we can't log, just ignore
            }
        }
    }
}