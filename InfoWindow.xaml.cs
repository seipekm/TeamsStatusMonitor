using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace TeamsStatus
{
    public partial class InfoWindow : Wpf.Ui.Controls.FluentWindow
    {
        public InfoWindow()
        {
            InitializeComponent();
            
            // Lade die aktuelle Version der Applikation
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                TxtVersion.Text = $"Version: {version.Major}.{version.Minor}.{version.Build}";
            }
        }

        private async void BtnUpdateFirmware_Click(object sender, RoutedEventArgs e)
        {
            if (this.Owner is MainWindow mw)
            {
                await mw.CheckAndPerformFirmwareUpdate();
                if (mw.FirmwareUpdateFailed)
                {
                    BtnBootloader.Visibility = Visibility.Visible;
                }
                
                // Kurz warten, damit die serielle Schnittstelle die Versionsantwort verarbeiten konnte
                await Task.Delay(1000);
                TxtFirmwareVersion.Text = $"Firmware: {mw.CurrentFirmwareVersion}";
            }
        }

        private void BtnBootloader_Click(object sender, RoutedEventArgs e)
        {
            if (this.Owner is MainWindow mw)
            {
                if (MessageBox.Show("Möchtest du den Pico wirklich in den Update-Modus (Bootloader) versetzen? Er wird als USB-Laufwerk neu gestartet.", "Bootloader starten", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    mw.SendStatus("UPDATE");
                }
            }
        }

        private async void BtnUpdateApp_Click(object sender, RoutedEventArgs e)
        {
            if (this.Owner is MainWindow mw)
            {
                await mw.CheckAndPerformAppUpdate(true);
            }
        }

        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logPath = MainWindow.GetLogFilePath();
                if (System.IO.File.Exists(logPath))
                {
                    Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("Die Log-Datei existiert noch nicht.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Öffnen der Log-Datei: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // Öffnet den Link im Standardbrowser
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.MinWidth = this.ActualWidth;
            this.MaxWidth = this.ActualWidth;
            this.MinHeight = this.ActualHeight;
            this.MaxHeight = this.ActualHeight;

            if (this.Owner is MainWindow mw)
            {
                TxtFirmwareVersion.Text = $"Firmware: {mw.CurrentFirmwareVersion}";
            }
        }

        private void TitleBar_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
