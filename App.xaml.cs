using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ZnDownloader
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "ZnDownloader_log.txt");
                string errorMessage = $"!!! UNHANDLED EXCEPTION !!!\n\n{DateTime.Now}\n\n{e.Exception}";
                File.WriteAllText(logPath, errorMessage);

                MessageBox.Show("A critical error occurred and the application will close. A log file has been created at:\n" + logPath, "Zn Tools - Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"A critical error occurred, but failed to write the log file.\n\nOriginal Error:\n{e.Exception.Message}\n\nLogging Error:\n{ex.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Prevent default unhandled exception processing
            e.Handled = true;
            Current.Shutdown();
        }
    }
}
