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
