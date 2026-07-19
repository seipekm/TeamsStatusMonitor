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
                var win = new Wpf.Ui.Controls.FluentWindow
                {
                    Title = "Bereits gestartet",
                    Width = 450,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    ExtendsContentIntoTitleBar = true,
                    WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica
                };

                var grid = new System.Windows.Controls.Grid();
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var titleBar = new Wpf.Ui.Controls.TitleBar { Title = "Bereits gestartet", Margin = new Thickness(0) };
                System.Windows.Controls.Grid.SetRow(titleBar, 0);
                titleBar.PreviewMouseLeftButtonDown += (s, ev) => { if (ev.LeftButton == System.Windows.Input.MouseButtonState.Pressed) win.DragMove(); };
                grid.Children.Add(titleBar);

                var sp = new System.Windows.Controls.StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20) };
                System.Windows.Controls.Grid.SetRow(sp, 1);
                
                sp.Children.Add(new Wpf.Ui.Controls.TextBlock { Text = "Teams Status Monitor läuft bereits im Hintergrund.", Margin = new Thickness(0, 0, 0, 15) });
                
                var btn = new Wpf.Ui.Controls.Button { Content = "OK", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, HorizontalAlignment = HorizontalAlignment.Center, Width = 100 };
                btn.Click += (s, ev) => win.Close();
                sp.Children.Add(btn);

                grid.Children.Add(sp);
                win.Content = grid;
                win.ShowDialog();
                
                Application.Current.Shutdown();
                return;
            }

            // Prüfen, ob die App mit dem -autostart Argument gestartet wurde
            bool autostart = e.Args.Any(arg => arg.Equals("-autostart", StringComparison.OrdinalIgnoreCase));
            
            MainWindow mainWindow = new MainWindow();
            
            bool startMinimized = mainWindow.ChkStartMinimized.IsChecked == true;
            
            if (autostart || startMinimized)
            {
                // Wenn minimiert gestartet wird, WindowState setzen BEVOR das Fenster angezeigt wird.
                // Dadurch wird der WPF/Mica Rendering Bug vermieden und das Tray-Icon trotzdem erstellt.
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.ShowInTaskbar = false;
                mainWindow.Hide(); // WPF versteckt es komplett aus Taskleiste
            }
            
            mainWindow.Show();
            
            if (autostart || startMinimized)
            {
                mainWindow.Hide(); // Nochmal aufrufen, um sicherzustellen, dass es versteckt ist
            }
            // Wenn autostart = true, bleibt das Fenster unsichtbar, 
            // aber die App läuft dank ShutdownMode im Hintergrund weiter.
        }
    }
}
