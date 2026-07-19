using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PBIPortWrapper.Services;

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
            // #64: concurrent wrappers share config.json/log.txt without
            // coordination and compete for the same fixed ports - allow only one
            // instance and front the existing one instead. The mutex handle is
            // held for the process lifetime; the kernel object disappears with
            // the process, so a crashed wrapper never blocks the next start.
            using var singleInstance = AcquireSingleInstanceMutex(out bool isFirstInstance);
            if (!isFirstInstance)
            {
                FrontExistingInstance();
                return;
            }

            // Set up global exception handlers
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }

        private static Mutex AcquireSingleInstanceMutex(out bool isFirstInstance)
        {
            try
            {
                return new Mutex(initiallyOwned: true, @"Global\PBIPortWrapper_SingleInstance", out isFirstInstance);
            }
            catch (UnauthorizedAccessException)
            {
                // The mutex exists but belongs to another session/user - still a
                // running wrapper (ports are machine-wide, so this counts too).
                isFirstInstance = false;
                return null;
            }
        }

        private static void FrontExistingInstance()
        {
            var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(current.ProcessName))
            {
                if (process.Id == current.Id) continue;

                var hwnd = process.MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                {
                    if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                    return;
                }
            }

            // No window to front (minimized to tray, or the other instance runs
            // in another session) - at least say why nothing opened.
            MessageBox.Show(
                "PBI Port Wrapper is already running - check the system tray.",
                "PBI Port Wrapper", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

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

                        // Log using LoggerService
            try
            {
                var logger = new LoggerService();
                logger.LogError("Global", "Unhandled exception occurred", ex);
            }
            catch
            {
                // If we can't log, just ignore
            }
        }
    }
}