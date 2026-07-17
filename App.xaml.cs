using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace TeamsStatus
{
    public partial class App : Application
    {
        private static Mutex? _mutex = null;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            const string appName = "TeamsStatusMonitor_SingleInstance_Mutex";
            
            _mutex = new Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                // App is already running!
                MessageBox.Show("Teams Status Monitor läuft bereits im Hintergrund (siehe System-Tray).", "Bereits gestartet", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
                return;
            }

            // Prüfen, ob die App mit dem -autostart Argument gestartet wurde
            bool autostart = e.Args.Any(arg => arg.Equals("-autostart", StringComparison.OrdinalIgnoreCase));
            
            MainWindow mainWindow = new MainWindow();
            
            if (!autostart)
            {
                // Nur anzeigen, wenn NICHT im Autostart
                mainWindow.Show();
            }
            // Wenn autostart = true, bleibt das Fenster unsichtbar, 
            // aber die App läuft dank ShutdownMode im Hintergrund weiter.
        }
    }
}
