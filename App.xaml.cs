using System;
using System.Linq;
using System.Windows;

namespace TeamsStatus
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
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
