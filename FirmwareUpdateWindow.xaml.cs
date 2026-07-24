using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.IO.Ports;
using Wpf.Ui.Controls;
using System.IO.Compression;
using System.Diagnostics;

namespace TeamsStatus
{
    public partial class FirmwareUpdateWindow : Wpf.Ui.Controls.FluentWindow
    {
        private string _downloadUrl;
        private string _comPort;
        private string _architecture;
        public bool UpdateFailed { get; private set; } = false;

        public FirmwareUpdateWindow(string downloadUrl, string comPort, string architecture = "RP2040")
        {
            InitializeComponent();
            _downloadUrl = downloadUrl;
            _comPort = comPort;
            _architecture = architecture;
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
                // 1. Download Firmware
                TxtStatus.Text = "Lade Firmware herunter...";
                string fileExt = _architecture == "ESP32" ? "bin" : "uf2";
                string tempFile = Path.Combine(Path.GetTempPath(), $"firmware.{fileExt}");
                
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

                if (_architecture == "ESP32")
                {
                    await FlashESP32(tempFile);
                }
                else
                {
                    await FlashRP2040(tempFile);
                }
                
                TxtStatus.Text = "Erfolgreich abgeschlossen!";
                TxtDetail.Text = "Gerät startet neu...";
                
                // Wir warten 3 Sekunden, damit Windows genug Zeit hat, den USB Port nach dem Reboot (soft_reset) neu zu mounten
                await Task.Delay(3000);
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
        
        private async Task FlashESP32(string tempFile)
        {
            TxtStatus.Text = "Bereite Flashen vor...";
            ProgressDownload.IsIndeterminate = true;
            
            // esptool Pfad
            string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TeamsStatusMonitor", "esptool");
            Directory.CreateDirectory(localAppData);
            string esptoolExe = Path.Combine(localAppData, "esptool-win64", "esptool.exe");
            
            if (!File.Exists(esptoolExe))
            {
                TxtDetail.Text = "Lade esptool herunter...";
                string esptoolUrl = "https://github.com/espressif/esptool/releases/download/v4.8.1/esptool-win64.zip";
                string zipPath = Path.Combine(localAppData, "esptool.zip");
                
                using (var client = new HttpClient())
                {
                    var response = await client.GetByteArrayAsync(esptoolUrl);
                    await File.WriteAllBytesAsync(zipPath, response);
                }
                
                TxtDetail.Text = "Entpacke esptool...";
                ZipFile.ExtractToDirectory(zipPath, localAppData, true);
                File.Delete(zipPath);
            }
            
            TxtStatus.Text = "Flashe ESP32S3...";
            TxtDetail.Text = "Bitte warten...";
            
            // Execute esptool
            var startInfo = new ProcessStartInfo
            {
                FileName = esptoolExe,
                Arguments = $"--chip esp32s3 --port {_comPort} --baud 460800 --after soft_reset write_flash -z 0x10000 \"{tempFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using (var process = Process.Start(startInfo))
            {
                if (process == null) throw new Exception("Konnte esptool.exe nicht starten.");
                
                // Wir k�nnten hier den Output lesen und im UI anzeigen
                string output = await process.StandardOutput.ReadToEndAsync();
                string err = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();
                
                if (process.ExitCode != 0)
                {
                    throw new Exception($"esptool Fehler:\n{output}\n{err}");
                }
            }
        }

        private async Task FlashRP2040(string tempFile)
        {
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
                catch { } // Kann fehlschlagen, wenn das Ger�t sofort verschwindet
            }

            // 3. Warten auf RPI-RP2 Laufwerk
            TxtStatus.Text = "Suche nach Controller...";
            TxtDetail.Text = "Warte auf RPI-RP2 / RP2350 Laufwerk...";
            string targetDrive = "";
            for (int i = 0; i < 30; i++) // 15 Sekunden warten
            {
                await Task.Delay(500);
                var drives = DriveInfo.GetDrives();
                foreach (var d in drives)
                {
                    if (d.IsReady && d.VolumeLabel == "RPI-RP2" || d.VolumeLabel == "RP2350")
                    {
                        targetDrive = d.Name;
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(targetDrive)) break;
            }

            if (string.IsNullOrEmpty(targetDrive))
            {
                UpdateFailed = true;
                throw new Exception("RP2040/RP2350 Bootloader-Laufwerk (RPI-RP2 oder RP2350) wurde nicht gefunden. Bitte manuell abstecken und mit gedr�ckter BOOT-Taste anstecken.");
            }

            // 4. Kopieren
            TxtStatus.Text = "Kopiere Firmware...";
            TxtDetail.Text = "Schreibe Daten auf den Controller...";
            string destFile = Path.Combine(targetDrive, "firmware.uf2");
            
            await Task.Run(() => File.Copy(tempFile, destFile, true));
        }
    }
}
