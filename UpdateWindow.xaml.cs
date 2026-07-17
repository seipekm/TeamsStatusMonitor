using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;

namespace TeamsStatus
{
    public partial class UpdateWindow : FluentWindow
    {
        private readonly string _downloadUrl;

        public UpdateWindow(string downloadUrl)
        {
            InitializeComponent();
            _downloadUrl = downloadUrl;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await DownloadAndInstallUpdateAsync();
        }

        private async Task DownloadAndInstallUpdateAsync()
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "TeamsStatusMonitorUpdate");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                string zipPath = Path.Combine(tempDir, "update.zip");

                TxtStatus.Text = "Update wird heruntergeladen...";
                
                using (var client = new HttpClient())
                {
                    using var response = await client.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength;
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var totalRead = 0L;
                    var buffer = new byte[8192];
                    var isMoreToRead = true;

                    do
                    {
                        var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                        if (read == 0)
                        {
                            isMoreToRead = false;
                        }
                        else
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;

                            if (totalBytes.HasValue)
                            {
                                var percentage = Math.Round((double)totalRead / totalBytes.Value * 100, 1);
                                ProgressDownload.Value = percentage;
                                TxtDetail.Text = $"{percentage:F1} %";
                            }
                            else
                            {
                                // Indeterminate wenn keine Länge bekannt ist
                                ProgressDownload.IsIndeterminate = true;
                                TxtDetail.Text = $"{totalRead / 1024} KB geladen";
                            }
                        }
                    } while (isMoreToRead);
                }

                TxtStatus.Text = "Update wird entpackt...";
                ProgressDownload.IsIndeterminate = true;
                TxtDetail.Text = "Bitte warten...";
                
                // Kurze Pause fürs Auge
                await Task.Delay(500);

                string extractDir = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractDir);

                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractDir, true));

                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(currentDir, "TeamsStatus.exe");

                TxtStatus.Text = "Neustart wird vorbereitet...";
                await Task.Delay(500);

                // Batch-Script erstellen, das alle Dateien (inkl. DLLs) rüberkopiert und neu startet
                string batPath = Path.Combine(tempDir, "update.bat");
                string batContent = $@"@echo off
:waitloop
tasklist | find /i ""TeamsStatus.exe"" >nul 2>&1
if not errorlevel 1 (
    timeout /t 1 /nobreak > NUL
    goto waitloop
)
xcopy /s /y ""{extractDir}\*"" ""{currentDir}\""
start """" ""{currentExe}"" -updated
del ""%~f0""
";
                File.WriteAllText(batPath, batContent);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = batPath,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                System.Diagnostics.Process.Start(psi);

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Fehler bei der Update-Installation: {ex.Message}", "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                this.Close();
            }
        }
    }
}
