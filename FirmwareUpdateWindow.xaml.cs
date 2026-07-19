using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.IO.Ports;
using Wpf.Ui.Controls;

namespace TeamsStatus
{
    public partial class FirmwareUpdateWindow : FluentWindow
    {
        private readonly string _downloadUrl;
        private readonly string _comPort;

        public FirmwareUpdateWindow(string downloadUrl, string comPort)
        {
            InitializeComponent();
            _downloadUrl = downloadUrl;
            _comPort = comPort;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.MinWidth = this.ActualWidth;
            this.MaxWidth = this.ActualWidth;
            this.MinHeight = this.ActualHeight;
            this.MaxHeight = this.ActualHeight;

            await DownloadAndInstallFirmwareAsync();
        }

        private void TitleBar_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private async Task DownloadAndInstallFirmwareAsync()
        {
            try
            {
                // 1. Download UF2
                TxtStatus.Text = "Lade Firmware herunter...";
                string tempFile = Path.Combine(Path.GetTempPath(), "firmware.uf2");
                
                using (var client = new HttpClient())
                {
                    using var response = await client.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength;
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

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
                                ProgressDownload.IsIndeterminate = true;
                                TxtDetail.Text = $"{totalRead / 1024} KB geladen";
                            }
                        }
                    } while (isMoreToRead);
                }

                // 2. Trigger Bootloader (1200 Baud)
                TxtStatus.Text = "Neustart in den Bootloader...";
                ProgressDownload.IsIndeterminate = true;
                TxtDetail.Text = "Bitte warten...";
                await Task.Delay(500);

                if (!string.IsNullOrEmpty(_comPort))
                {
                    try
                    {
                        using (var resetPort = new SerialPort(_comPort, 1200))
                        {
                            resetPort.Open();
                            await Task.Delay(100);
                            resetPort.Close();
                        }
                    }
                    catch { } // Kann fehlschlagen, wenn das Gerät sofort verschwindet
                }

                // 3. Warten auf RPI-RP2 Laufwerk
                TxtStatus.Text = "Suche nach Controller...";
                TxtDetail.Text = "Warte auf RPI-RP2 Laufwerk...";
                string targetDrive = "";
                for (int i = 0; i < 30; i++) // 15 Sekunden warten
                {
                    await Task.Delay(500);
                    var drives = DriveInfo.GetDrives();
                    foreach (var d in drives)
                    {
                        if (d.IsReady && d.VolumeLabel == "RPI-RP2")
                        {
                            targetDrive = d.Name;
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(targetDrive)) break;
                }

                if (string.IsNullOrEmpty(targetDrive))
                {
                    var uiMessageBox = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = "Fehler",
                        Content = "RP2040 Bootloader-Laufwerk (RPI-RP2) wurde nicht gefunden. Bitte manuell abstecken und mit gedrückter BOOT-Taste anstecken.",
                        CloseButtonText = "OK",
                        ShowTitle = true
                    };
                    await uiMessageBox.ShowDialogAsync();
                    this.Close();
                    return;
                }

                // 4. Kopieren
                TxtStatus.Text = "Kopiere Firmware...";
                TxtDetail.Text = "Schreibe Daten auf den Controller...";
                string destFile = Path.Combine(targetDrive, "firmware.uf2");
                
                await Task.Run(() => File.Copy(tempFile, destFile, true));
                
                TxtStatus.Text = "Erfolgreich abgeschlossen!";
                TxtDetail.Text = "Gerät startet neu...";
                
                await Task.Delay(1500);
                this.Close();
            }
            catch (Exception ex)
            {
                var uiMessageBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Fehler",
                    Content = $"Fehler beim Firmware-Flash: {ex.Message}",
                    CloseButtonText = "OK",
                    ShowTitle = true
                };
                await uiMessageBox.ShowDialogAsync();
                this.Close();
            }
        }
    }
}
