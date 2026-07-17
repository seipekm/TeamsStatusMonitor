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
    }
}
