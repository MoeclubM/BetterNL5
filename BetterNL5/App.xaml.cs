using System;
using System.Globalization;
using System.IO;
using Microsoft.UI.Xaml;

namespace BetterNL5
{
    public partial class App : Application
    {
        private static readonly string StartupLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterNL5",
            "startup.log");

        private Window? window;

        public App()
        {
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            InitializeComponent();
            WriteStartupLog("App constructed.");
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            WriteStartupLog("OnLaunched entered.");
            try
            {
                window = new MainWindow();
                WriteStartupLog("MainWindow created.");
                window.Activate();
                WriteStartupLog("MainWindow activated.");
            }
            catch (Exception ex)
            {
                WriteStartupLog("Launch failed: " + ex);
                throw;
            }
        }

        private static void CurrentDomain_UnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
        {
            WriteStartupLog("AppDomain unhandled exception: " + e.ExceptionObject);
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            WriteStartupLog("UI unhandled exception: " + e.Exception);
        }

        private static void WriteStartupLog(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StartupLogPath)!);
                File.AppendAllText(
                    StartupLogPath,
                    DateTime.Now.ToString("O", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
