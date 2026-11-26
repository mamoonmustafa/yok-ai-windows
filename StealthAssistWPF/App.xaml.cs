using System;
using System.IO;
using System.Windows;
using System.Diagnostics;

namespace StealthAssistWPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Setup exception handling
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                HandleException(args.ExceptionObject as Exception);
            };

            Current.DispatcherUnhandledException += (s, args) =>
            {
                HandleException(args.Exception);
                args.Handled = true;
            };

            // Hide from Task Manager (similar to your WinForms implementation)
            HideFromTaskManager();
        }

        // Hide the process from Task Manager
        private void HideFromTaskManager()
        {
            try
            {
                // We can't directly rename the process, but we can adjust its priority
                // and implement other stealth techniques
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;

                // More advanced hiding techniques would be implemented here
                // but may require kernel-level code which is out of scope
            }
            catch
            {
                // Silently fail - no logs
            }
        }

        private void HandleException(Exception ex)
        {
            try
            {
                // In a stealth application, we might not want to show error messages
                // Instead, log them silently
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "YokAI");

                if (!Directory.Exists(appDataPath))
                {
                    Directory.CreateDirectory(appDataPath);
                }

                string logPath = Path.Combine(appDataPath, "error.log");
                File.AppendAllText(logPath, $"{DateTime.Now}: {ex.Message}\n{ex.StackTrace}\n\n");
            }
            catch
            {
                // Silently fail
            }
        }
    }
}