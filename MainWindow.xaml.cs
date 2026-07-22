using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using System.Net.Http;
using System.Collections.Generic;

namespace TeamsStatus
{
    public class PortItem
    {
        public string PortName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public override string ToString() => DisplayName;
    }

    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private SerialPort? _serialPort;
        private CancellationTokenSource? _cts;
        private string _lastStatus = "";
        private System.Windows.Threading.DispatcherTimer _sendTimer;
        private bool _isLoaded = false;
        private bool _isConnected = false;
        private string _currentMode = "Auto";
        private string _teamsWebSocketToken = "";
        private TeamsWebSocketService? _webSocketService;
        private bool _isWebSocketInMeeting = false;
        private string _lastParsedLogStatus = "";

        /// <summary>
        /// Initialisiert das Hauptfenster, startet Timer, lädt Einstellungen und initialisiert 
        /// den WebSocket-Service sowie die zyklische Überprüfung.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            
            try
            {
                // Lösche das Log-File beim Start, damit es nicht endlos wächst
                System.IO.File.WriteAllText(GetLogFilePath(), string.Empty);
            }
            catch { }
            
            // Check for update success
            Loaded += MainWindow_Loaded;
            
            // Verhindert, dass die App geschlossen wird, wenn das Hauptfenster unsichtbar ist
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Timer für zyklisches Senden (z.B. alle 2 Sekunden)
            _sendTimer = new System.Windows.Threading.DispatcherTimer();
            _sendTimer.Interval = TimeSpan.FromSeconds(2);
            _sendTimer.Tick += (s, e) => {
                if (!string.IsNullOrEmpty(_lastStatus))
                {
                    SendStatus(_lastStatus);
                }
            };
            _sendTimer.Start();

            LoadPorts();
            
            // Load saved settings
            LoadSettings();

            // Setup WebSocket
            _webSocketService = new TeamsWebSocketService(_teamsWebSocketToken);
            _webSocketService.OnLog += Log;
            _webSocketService.OnTokenReceived += (token) => {
                _teamsWebSocketToken = token;
                Dispatcher.Invoke(() => SaveSettings());
            };
            _webSocketService.OnMeetingStateChanged += (isInMeeting, isMuted) => {
                _isWebSocketInMeeting = isInMeeting;
                // WebSocket ändert nicht mehr aktiv das UI/die Farbe!
                // Wir nutzen _isWebSocketInMeeting nur noch intern im Log-Scanner, um das Ringing nach dem Abheben zu stoppen.
                // Künftige Erweiterung: Hier könnte später Mute/Cam-Status an den Mikrocontroller gesendet werden.
            };
            _webSocketService.OnCallStateChanged += (isRinging) => {
                Dispatcher.Invoke(() => {
                    if (_currentMode != "Auto") return;
                    
                    if (isRinging)
                    {
                        UpdateStatus("Auto: Eingehender Anruf (Klingelt)", 'R'); // 'R' = Ringing mode
                        if (_isConnected && _serialPort != null)
                        {
                            try {
                                _serialPort.WriteLine($"Ringing,{(int)SldBrightness.Value}");
                            } catch {}
                        }
                    }
                    else
                    {
                        // Wenn es aufhört zu klingeln, zwingen wir den Log-Scanner dazu,
                        // den aktuellsten Status aus dem Log neu anzuwenden.
                        _lastParsedLogStatus = "";
                    }
                });
            };
            _webSocketService.Start();
            
            _isLoaded = true;
            
            if (ChkAutoConnect.IsChecked == true && CmbPorts.SelectedItem != null)
            {
                _ = ConnectSerial(); // Automatisch beim Start mit den geladenen Settings verbinden
            }
            
            // Start im zuletzt gespeicherten Modus
            if (_currentMode == "Auto" && !string.IsNullOrEmpty(_lastStatus) && _lastStatus != "U")
            {
                // Wenn Auto-Modus und wir einen alten Status haben, starte Monitoring, aber überschreibe nicht mit "Suche Log..."
                HighlightActiveButton("Auto");
                ChkMonitorStatus.IsChecked = true;
                StartMonitoring();
                
                string savedText = _lastUiStatusText;
                char savedCmd = _lastStatus[0];
                _lastStatus = "";
                _lastUiStatusText = "";
                UpdateStatus(savedText, savedCmd);
            }
            else
            {
                string savedMode = _currentMode;
                _currentMode = ""; // Force update
                SetMode(savedMode); 
            }
        }

        /// <summary>
        /// Sucht in WMI nach allen verfügbaren seriellen Ports und versucht,
        /// spezifisch den Raspberry Pi Pico bzw. Teams Status Monitor zu identifizieren.
        /// </summary>
        private void LoadPorts()
        {
            List<PortItem> ports = new List<PortItem>();
            string[] rawPorts = System.IO.Ports.SerialPort.GetPortNames();

            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT Caption, PNPDeviceID FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'"))
                {
                    foreach (System.Management.ManagementObject queryObj in searcher.Get())
                    {
                        string caption = queryObj["Caption"]?.ToString() ?? "";
                        string pnpId = queryObj["PNPDeviceID"]?.ToString() ?? "";
                        int startIndex = caption.LastIndexOf("(COM");
                        if (startIndex >= 0)
                        {
                            int endIndex = caption.IndexOf(")", startIndex);
                            if (endIndex >= 0)
                            {
                                string portName = caption.Substring(startIndex + 1, endIndex - startIndex - 1);
                                string description = caption.Substring(0, startIndex).Trim();
                                
                                // Nur Raspberry Pi Pico (VID_2E8A) oder explizit Teams Status Monitor (PID_1234)
                                if (pnpId.Contains("PID_1234") || pnpId.Contains("VID_2E8A")) 
                                {
                                    description = pnpId.Contains("PID_1234") ? "Teams Status Monitor" : "Raspberry Pi Pico";
                                    
                                    // Versuche, die Seriennummer des USB-Root-Geräts zu ermitteln
                                    string serial = "";
                                    try 
                                    {
                                        int miIndex = pnpId.IndexOf("&MI_");
                                        if (miIndex > 0)
                                        {
                                            string vidPid = pnpId.Substring(0, miIndex); // z.B. "USB\VID_2E8A&PID_1234"
                                            string queryStr = $"SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '{vidPid.Replace("\\", "\\\\")}\\\\%'";
                                            using (var parentSearcher = new System.Management.ManagementObjectSearcher(queryStr))
                                            {
                                                foreach (System.Management.ManagementObject parentObj in parentSearcher.Get())
                                                {
                                                    string parentPnpId = parentObj["PNPDeviceID"]?.ToString() ?? "";
                                                    if (!parentPnpId.Contains("&MI_"))
                                                    {
                                                        serial = parentPnpId.Substring(parentPnpId.LastIndexOf('\\') + 1);
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    } catch {}

                                    if (!string.IsNullOrEmpty(serial))
                                    {
                                        description = serial;
                                    }

                                    ports.Add(new PortItem { PortName = portName, DisplayName = description });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback falls WMI fehlschlägt
            }

            // Fallback, wenn keine passenden RP2040 Ports gefunden wurden: 
            // Füge alle rawPorts hinzu, damit der User wenigstens etwas auswählen kann
            if (ports.Count == 0)
            {
                foreach (var rp in rawPorts)
                {
                    ports.Add(new PortItem { PortName = rp, DisplayName = rp });
                }
            }

            CmbPorts.ItemsSource = ports;
        }

        private string GetLogFilePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TeamsStatusMonitor");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "TeamsStatusMonitor.log");
        }

        private string _lastLoggedMessage = "";

        private void Log(string message)
        {
            if (message == _lastLoggedMessage) return; // Spam-Schutz
            _lastLoggedMessage = message;
            
            try
            {
                string logPath = GetLogFilePath();
                System.IO.File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}");
                
                Dispatcher.InvokeAsync(() => {
                    if (TxtLogOutput != null)
                    {
                        TxtLogOutput.AppendText($"{DateTime.Now:HH:mm:ss} - {message}\n");
                        TxtLogOutput.ScrollToEnd();
                    }
                });
            }
            catch { }
        }

        private string GetSettingsFilePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TeamsStatusMonitor");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        private void SaveSettings()
        {
            if (!_isLoaded) return;
            try
            {
                var settings = new
                {
                    PortName = (CmbPorts.SelectedItem as PortItem)?.PortName ?? "",
                    BaudRate = "9600",
                    Brightness = SldBrightness.Value,
                    CurrentMode = _currentMode,
                    LastStatus = _lastStatus,
                    LastStatusText = _lastUiStatusText,
                    AutoConnect = ChkAutoConnect.IsChecked ?? false,
                    StartMinimized = ChkStartMinimized.IsChecked ?? false,
                    SendFreeOnConnect = ChkSendFree.IsChecked ?? false,
                    TeamsWebSocketToken = _teamsWebSocketToken
                };
                string json = JsonSerializer.Serialize(settings);
                System.IO.File.WriteAllText(GetSettingsFilePath(), json);
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                string path = GetSettingsFilePath();
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("Brightness", out var b))
                    {
                        SldBrightness.Value = b.GetDouble();
                    }
                    // BaudRate dropdown was removed
                    if (root.TryGetProperty("PortName", out var pn))
                    {
                        string savedPort = pn.GetString() ?? "";
                        if (!string.IsNullOrEmpty(savedPort))
                        {
                            foreach (PortItem item in CmbPorts.Items)
                            {
                                if (item.PortName == savedPort)
                                {
                                    CmbPorts.SelectedItem = item;
                                    break;
                                }
                            }
                        }
                    }
                    if (root.TryGetProperty("CurrentMode", out var cm))
                    {
                        _currentMode = cm.GetString() ?? "Auto";
                    }
                    if (root.TryGetProperty("LastStatus", out var ls))
                    {
                        _lastStatus = ls.GetString() ?? "";
                    }
                    if (root.TryGetProperty("LastStatusText", out var lst))
                    {
                        _lastUiStatusText = lst.GetString() ?? "";
                    }
                    if (root.TryGetProperty("AutoConnect", out var ac))
                    {
                        ChkAutoConnect.IsChecked = ac.GetBoolean();
                    }
                    if (root.TryGetProperty("StartMinimized", out var sm))
                    {
                        ChkStartMinimized.IsChecked = sm.GetBoolean();
                    }
                    if (root.TryGetProperty("SendFreeOnConnect", out var sendFree))
                    {
                        ChkSendFree.IsChecked = sendFree.GetBoolean();
                    }
                    
                    if (root.TryGetProperty("TeamsWebSocketToken", out var tokenProp))
                    {
                        _teamsWebSocketToken = tokenProp.GetString() ?? "";
                    }
                }
                
                ChkStartWindows.IsChecked = System.IO.File.Exists(GetShortcutPath());
            }
            catch { }
        }

        private void CmbBaudRate_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            SaveSettings();
            if (_isConnected) DisconnectSerial();
        }

        private void CmbPorts_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SaveSettings();
            if (_isConnected) DisconnectSerial();
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
            {
                DisconnectSerial();
            }
            else
            {
                await ConnectSerial();
            }
        }

        /// <summary>
        /// Stellt asynchron die serielle Verbindung (COM-Port) zum Raspberry Pi Pico (RP2040) her
        /// und initialisiert den Lesethread für einkommende Bestätigungen.
        /// </summary>
        private async Task ConnectSerial()
        {
            if (CmbPorts.SelectedItem is not PortItem selectedItem) return;
            string port = selectedItem.PortName;
            
            int baudRate = 9600; // Fixed baud rate

            DisconnectSerial();

            try
            {
                _serialPort = new SerialPort(port, baudRate);
                _serialPort.DtrEnable = true;
                _serialPort.RtsEnable = true;
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();
                SetConnectionStatus(true);
                
                // Firmware Version abfragen (Default: Unbekannt)
                CurrentFirmwareVersion = "Unbekannt";
                _serialPort.WriteLine("VERSION");
                
                if (ChkSendFree.IsChecked == true)
                {
                    UpdateStatus("Manuell: Verfügbar", 'A');
                }
                else
                {
                    SendStatus(_lastStatus); // Zuletzt bekannten Status senden
                }
            }
            catch (Exception ex)
            {
                SetConnectionStatus(false);
                await ShowFluentMessageBoxAsync("Fehler", $"Fehler beim Öffnen des COM-Ports: {ex.Message}");
            }
        }

        private void DisconnectSerial()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try { _serialPort.DataReceived -= SerialPort_DataReceived; _serialPort.Close(); } catch { }
                _serialPort.Dispose();
                _serialPort = null;
            }
            SetConnectionStatus(false);
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                while (_serialPort != null && _serialPort.IsOpen && _serialPort.BytesToRead > 0)
                {
                    string data = _serialPort.ReadLine().Trim();
                    Log($"Empfangen: {data}");
                    if (data.StartsWith("VERSION:"))
                    {
                        string[] parts = data.Split(':');
                        if (parts.Length >= 3)
                        {
                            CurrentFirmwareArchitecture = parts[1]; // RP2040 oder ESP32
                            CurrentFirmwareVersion = parts[2];
                        }
                        else
                        {
                            // Fallback fr alte Firmware
                            CurrentFirmwareVersion = data.Substring(8);
                        }
                    }
                }
            }
            catch { }
        }

        public string CurrentFirmwareVersion { get; set; } = "Unbekannt";
        public string CurrentFirmwareArchitecture { get; set; } = "RP2040";
        public bool FirmwareUpdateFailed { get; set; } = false;

        /// <summary>
        /// Prüft auf GitHub, ob eine neuere Firmware (.uf2) für den Mikrocontroller verfügbar ist,
        /// und führt den Download sowie ein Update-Fenster aus, falls ja.
        /// </summary>
        public async Task CheckAndPerformFirmwareUpdate()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "TeamsStatusMonitor");
                
                string url = "https://api.github.com/repos/seipekm/TeamsStatusMonitor/releases";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    await ShowFluentMessageBoxAsync("Fehler", "Keine Verbindung zu GitHub möglich.");
                    return;
                }
                
                string json = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                
                JsonElement? latestFwRelease = null;
                Version? maxFwVersion = null;
                
                foreach (var release in doc.RootElement.EnumerateArray())
                {
                    string tagName = release.GetProperty("tag_name").GetString() ?? "";
                    if (tagName.StartsWith("fw-v") || (tagName.StartsWith("v") && !tagName.StartsWith("app-v")))
                    {
                        string versionStr = tagName.StartsWith("fw-v") ? tagName.Substring(4) : tagName.TrimStart('v');
                        if (Version.TryParse(versionStr, out Version? v))
                        {
                            if (maxFwVersion == null || v > maxFwVersion)
                            {
                                maxFwVersion = v;
                                latestFwRelease = release;
                            }
                        }
                    }
                }

                if (latestFwRelease == null)
                {
                    await ShowFluentMessageBoxAsync("Info", "Keine Firmware-Releases gefunden.");
                    return;
                }

                var root = latestFwRelease.Value;
                string tag = root.GetProperty("tag_name").GetString() ?? "";
                string latestVersion = tag.StartsWith("fw-v") ? tag.Substring(4) : tag.TrimStart('v');

                if (Version.TryParse(latestVersion, out Version? latestV) && Version.TryParse(CurrentFirmwareVersion, out Version? currentV) && latestV != null && currentV != null)
                {
                    if (latestV > currentV)
                    {
                        var result = await ShowFluentMessageBoxAsync("Firmware Update", $"Neue Firmware {latestVersion} verfügbar (Aktuell: {CurrentFirmwareVersion}).\nJetzt flashen?", true);
                        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                        {
                            string downloadUrl = "";
                        string targetAsset = CurrentFirmwareArchitecture == "ESP32" ? "ESP32_firmware.bin" : (CurrentFirmwareArchitecture == "RP2350" ? "RP2350_firmware.uf2" : "RP2040_firmware.uf2");
                            if (root.TryGetProperty("assets", out JsonElement assets))
                            {
                                foreach (var asset in assets.EnumerateArray())
                                {
                                    if (asset.GetProperty("name").GetString() == targetAsset)
                                    {
                                        downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                        break;
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(downloadUrl))
                            {
                                await PerformFirmwareUpdate(downloadUrl);
                            }
                            else
                            {
                                await ShowFluentMessageBoxAsync("Fehler", $"Die Datei {targetAsset} wurde im neuesten Release nicht gefunden.");
                            }
                        }
                    }
                    else
                    {
                        await ShowFluentMessageBoxAsync("Info", $"Die Firmware ist bereits auf dem neuesten Stand ({CurrentFirmwareVersion}).");
                    }
                }
                else
                {
                    // Fallback wenn aktuelle Version "Unbekannt" ist, trotzdem Update zulassen
                    var result = await ShowFluentMessageBoxAsync("Firmware Update", $"Neue Firmware {latestVersion} verfügbar. (Aktuell: {CurrentFirmwareVersion})\nMöchtest du das Update erzwingen?", true);
                    if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                    {
                        string downloadUrl = "";
                        string targetAsset = CurrentFirmwareArchitecture == "ESP32" ? "ESP32_firmware.bin" : (CurrentFirmwareArchitecture == "RP2350" ? "RP2350_firmware.uf2" : "RP2040_firmware.uf2");
                        if (root.TryGetProperty("assets", out JsonElement assets))
                        {
                            foreach (var asset in assets.EnumerateArray())
                            {
                                if (asset.GetProperty("name").GetString() == targetAsset)
                                {
                                    downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                    break;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(downloadUrl))
                        {
                            await PerformFirmwareUpdate(downloadUrl);
                        }
                        else
                        {
                            await ShowFluentMessageBoxAsync("Fehler", $"Die Datei {targetAsset} wurde im neuesten Release nicht gefunden.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowFluentMessageBoxAsync("Fehler", $"Update-Fehler: {ex.Message}");
            }
        }

        private async Task PerformFirmwareUpdate(string downloadUrl)
        {
            try
            {
                string port = "";
                if (CmbPorts.SelectedItem is PortItem pi)
                {
                    port = pi.PortName;
                }

                DisconnectSerial();
                await Task.Delay(500);

                var fwWindow = new FirmwareUpdateWindow(downloadUrl, port, CurrentFirmwareArchitecture)
                {
                    Owner = this
                };
                fwWindow.ShowDialog();

                if (fwWindow.UpdateFailed)
                {
                    FirmwareUpdateFailed = true;
                }

                // Wieder verbinden (Pico braucht etwas Zeit zum Neustarten)
                bool reconnected = false;
                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(1000);
                    LoadPorts();
                    
                    foreach (PortItem item in CmbPorts.Items)
                    {
                        if (item.PortName == port)
                        {
                            CmbPorts.SelectedItem = item;
                            await ConnectSerial();
                            reconnected = true;
                            break;
                        }
                    }
                    if (reconnected) break;
                }
            }
            catch (Exception ex)
            {
                await ShowFluentMessageBoxAsync("Fehler", $"Firmware-Flash fehlgeschlagen: {ex.Message}");
            }
        }
        
        private void SetConnectionStatus(bool connected)
        {
            _isConnected = connected;
            Dispatcher.Invoke(() => {
                if (BtnConnect != null)
                {
                    BtnConnect.Content = connected ? "Trennen" : "Verbinden";
                }
                
                if (TxtConnStatus != null && ConnIcon != null)
                {
                    if (connected)
                    {
                        TxtConnStatus.Text = "Verbunden";
                        ConnIcon.Fill = Brushes.LimeGreen;
                        MyNotifyIcon.ToolTipText = "Teams Status Monitor (Verbunden)";
                    }
                    else
                    {
                        TxtConnStatus.Text = "Getrennt";
                        ConnIcon.Fill = Brushes.Red;
                        MyNotifyIcon.ToolTipText = "Teams Status Monitor (Getrennt)";
                    }
                }
                
                // Force tray icon update
                // Tray Icon update
                Brush brush = Brushes.Gray;
                if (_lastStatus == "A") brush = Brushes.LimeGreen;
                else if (_lastStatus == "W") brush = Brushes.Orange;
                else if (_lastStatus == "B") brush = Brushes.Red;
                else if (_lastStatus == "D") brush = Brushes.DarkRed;
                else if (_lastStatus == "R") brush = Brushes.DeepPink;
                UpdateTrayIcon(brush);
            });
        }

        private void BtnMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string tag = btn.Tag?.ToString() ?? "Auto";
            SetMode(tag);
        }

        private void SetMode(string tag)
        {
            _currentMode = tag;
            _lastParsedLogStatus = ""; // Erzwinge UI-Update beim nächsten Log-Scan
            SaveSettings();

            if (tag == "Auto") 
            {
                HighlightActiveButton("Auto");
                ChkMonitorStatus.IsChecked = true;
                StartMonitoring();
                UpdateStatus("Suche Log...", 'U'); // U für Unknown/Suche
            }
            else
            {
                StopMonitoring();
                if (tag == "A") UpdateStatus("Manuell: Verfügbar", 'A');
                else if (tag == "B") UpdateStatus("Manuell: Beschäftigt", 'B');
                else if (tag == "W") UpdateStatus("Manuell: Abwesend", 'W');
            }
        }

        private string _lastUiStatusText = "";

        private void UpdateStatus(string statusText, char command)
        {
            if (_lastStatus == command.ToString() && _lastUiStatusText == statusText)
            {
                return; // Status hat sich nicht geändert, kein UI-Update notwendig
            }
            
            _lastStatus = command.ToString();
            _lastUiStatusText = statusText;
            SaveSettings();
            
            // Dispatcher wird benötigt, falls das Event aus einem Hintergrund-Task (Auto) kommt
            Dispatcher.Invoke(() => 
            {
                // Kein InfoBar mehr, aber Tray-Icon kann separat aktualisiert werden falls gewünscht
                
                // Icon Farbe aktualisieren
                Brush fillBrush = Brushes.Gray;
                Wpf.Ui.Controls.SymbolRegular symbol = Wpf.Ui.Controls.SymbolRegular.QuestionCircle24;
                
                if (command == 'A')
                {
                    fillBrush = Brushes.LimeGreen;
                    symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24;
                }
                else if (command == 'B')
                {
                    fillBrush = Brushes.Red;
                    symbol = Wpf.Ui.Controls.SymbolRegular.Prohibited24;
                }
                else if (command == 'W')
                {
                    fillBrush = Brushes.Orange;
                    symbol = Wpf.Ui.Controls.SymbolRegular.Clock24;
                }
                else if (command == 'D') // Do Not Disturb
                {
                    fillBrush = Brushes.DarkRed;
                    symbol = Wpf.Ui.Controls.SymbolRegular.DismissCircle24;
                }
                else if (command == 'O') // Offline / Teams closed
                {
                    fillBrush = Brushes.White;
                    symbol = Wpf.Ui.Controls.SymbolRegular.PlugDisconnected24;
                }
                else if (command == 'R') // Ringing
                {
                    fillBrush = Brushes.DeepPink;
                    symbol = Wpf.Ui.Controls.SymbolRegular.Call24;
                }
                else
                {
                    symbol = Wpf.Ui.Controls.SymbolRegular.Search24;
                }

                if (MainTitleBar != null)
                {
                    MainTitleBar.Icon = new Wpf.Ui.Controls.SymbolIcon
                    {
                        Symbol = symbol,
                        Foreground = fillBrush
                    };
                }
                
                UpdateTrayIcon(fillBrush);
            });
            SendStatus(_lastStatus);
        }

        private void UpdateTrayIcon(Brush brush)
        {
            var wpfColor = ((SolidColorBrush)brush).Color;
            var winColor = System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);

            using (var bmp = new System.Drawing.Bitmap(16, 16))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var b = new System.Drawing.SolidBrush(winColor))
                    {
                        g.FillEllipse(b, 1, 1, 14, 14);
                    }
                    using (var p = new System.Drawing.Pen(System.Drawing.Color.DarkGray, 1))
                    {
                        g.DrawEllipse(p, 1, 1, 14, 14);
                    }

                    if (!_isConnected)
                    {
                        using (var p = new System.Drawing.Pen(System.Drawing.Color.Red, 2))
                        {
                            g.DrawLine(p, 3, 3, 13, 13);
                            g.DrawLine(p, 3, 13, 13, 3);
                        }
                    }
                }
                
                IntPtr newHandle = bmp.GetHicon();
                var newIcon = System.Drawing.Icon.FromHandle(newHandle);
                
                MyNotifyIcon.Icon = newIcon;
                
                if (_currentTrayIcon != null)
                {
                    _currentTrayIcon.Dispose();
                    DestroyIcon(_currentTrayIconHandle);
                }
                
                _currentTrayIcon = newIcon;
                _currentTrayIconHandle = newHandle;
            }
        }

        private System.Drawing.Icon? _currentTrayIcon;
        private IntPtr _currentTrayIconHandle = IntPtr.Zero;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        private void SldBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtBrightness != null)
            {
                int brightness = (int)e.NewValue;
                TxtBrightness.Text = brightness.ToString();
                
                // Sofort senden, wenn ein Status aktiv ist
                if (!string.IsNullOrEmpty(_lastStatus))
                {
                    SendStatus(_lastStatus);
                }
            }
            SaveSettings();
        }

        /// <summary>
        /// Sendet einen formatierten Status-String über die serielle Schnittstelle an den Mikrocontroller.
        /// Berücksichtigt dabei die ausgewählte Helligkeit der UI.
        /// </summary>
        /// <param name="data">Der Status, z.B. "A", "B", "Ringing" usw.</param>
        public void SendStatus(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    int brightness = 128;
                    Dispatcher.Invoke(() => {
                        if (SldBrightness != null)
                            brightness = (int)SldBrightness.Value;
                    });

                    string command = "";
                    string dataUpper = data.ToUpper();

                    if (dataUpper == "VERSION") command = "VERSION\n";
                    else if (dataUpper == "A") command = $"0,255,0,{brightness}\n"; // Reines Grün
                    else if (dataUpper == "B") command = $"255,0,0,{brightness}\n"; // Reines Rot
                    else if (dataUpper == "D") command = $"128,0,0,{brightness}\n"; // Dunkelrot für "Nicht stören"
                    else if (dataUpper == "W") command = $"255,255,0,{brightness}\n"; // Reines Gelb
                    else if (dataUpper == "O") command = $"255,255,255,{brightness}\n"; // Weiß für "Offline" / Teams aus
                    else if (dataUpper == "R") command = $"Ringing,{brightness}\n"; // Klingeln
                    else if (System.Linq.Enumerable.Count(data, c => c == ',') == 2) command = $"{data},{brightness}\n";
                    else command = $"{data},{brightness}\n";

                    _serialPort.Write(command);
                    Log($"Gesendet: {command.TrimEnd('\n')}");
                    if (!_isConnected) SetConnectionStatus(true);
                }
                catch (Exception ex)
                {
                    Log($"Fehler: {ex.Message}");
                    if (_isConnected) SetConnectionStatus(false);
                }
            }
            else
            {
                if (_isConnected) SetConnectionStatus(false);
            }
        }

        /// <summary>
        /// Startet die asynchrone Überwachung der Teams-Logdateien in einem Hintergrund-Task.
        /// Wird aufgerufen, wenn der "Auto"-Modus aktiviert wird.
        /// </summary>
        private void StartMonitoring()
        {
            StopMonitoring();
            _cts = new CancellationTokenSource();
            _ = MonitorTeamsLogAsync(_cts.Token);
        }

        private void StopMonitoring()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Das Herzstück der Automatik: Analysiert zyklisch die neuesten Microsoft Teams Log-Dateien,
        /// um den Anwesenheits- und Anrufstatus (Available, Busy, DND, Ringing) zu ermitteln.
        /// Diese Methode läuft durchgehend im Hintergrund.
        /// </summary>
        /// <param name="token">Cancellation Token zum sauberen Beenden des Threads.</param>
        private async Task MonitorTeamsLogAsync(CancellationToken token)
        {
            // Ordner-Pfade
            string newTeamsLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache", "Microsoft", "MSTeams", "Logs");

            // PeriodicTimer zum regelmäßigen Überprüfen (500ms = extrem schnell, aber zuverlässig!)
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    try
                    {
                        bool isTeamsRunning = System.Diagnostics.Process.GetProcessesByName("ms-teams").Length > 0 || System.Diagnostics.Process.GetProcessesByName("Teams").Length > 0;
                        if (!isTeamsRunning)
                        {
                            UpdateStatus("Auto: Teams geschlossen", 'O');
                            continue;
                        }

                        // Sammle die relevanten Log-Dateien
                        var filesToCheck = new System.Collections.Generic.List<FileInfo>();
                        
                        if (Directory.Exists(newTeamsLogDir))
                        {
                            var logFiles = Directory.GetFiles(newTeamsLogDir, "MSTeams_*.log");
                            if (logFiles.Length > 0)
                            {
                                // Wir nehmen die 10 neuesten Dateien, falls Teams extrem viel loggt (z.B. in Meetings)
                                filesToCheck.AddRange(logFiles.Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).Take(10));
                            }
                        }

                        if (filesToCheck.Count == 0)
                        {
                            Log("Es konnte keine New Teams Logdatei gefunden werden.");
                                Log("New Teams Log nicht gefunden");
                            continue;
                        }

                        // Sortiere alle gesammelten Dateien nach Dateinamen absteigend (neueste zuerst).
                        // WICHTIG: Nutze f.Name statt LastWriteTime, da Windows bei offnenen Dateien die LastWriteTime oft nicht aktualisiert!
                        filesToCheck = filesToCheck.OrderByDescending(f => f.Name).ToList();

                        bool foundStatus = false;

                        foreach (var logFile in filesToCheck)
                        {
                            try
                            {
                                // FileShare.ReadWrite ist wichtig, da Teams die Datei offen hält
                                using var stream = new FileStream(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                
                                using var reader = new StreamReader(stream);
                                // Nutze synchrones ReadToEnd() um Abbrüche bei asynchronen Datei-Leseoperationen (Overlapped I/O) durch Windows zu verhindern, wenn Teams gleichzeitig schreibt.
                                string content = reader.ReadToEnd();
                                
                                // Lese relevante Status (ACHTUNG: UserPresenceAction und UserDataGlobalState wurden entfernt, da diese bei mehreren Accounts den Status des falschen Accounts ausgeben können!)
                                int maxIndex = -1;
                                string parsedStatus = "";

                                // Check GlyphBadge (Lokales Taskleisten-Icon, absolut verlässlich und entspricht immer dem UI!)
                                // Teams schreibt "doNotDistrb" (ohne 'u'), daher matchen wir generisch und filtern danach.
                                var glyphBadgeMatches = System.Text.RegularExpressions.Regex.Matches(content, @"GlyphBadge\{\""([a-zA-Z]+)\""\}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                foreach (System.Text.RegularExpressions.Match m in glyphBadgeMatches)
                                {
                                    if (m.Index > maxIndex) 
                                    { 
                                        string gbStatus = m.Groups[1].Value.ToLower();
                                        if (gbStatus.Contains("avail")) { maxIndex = m.Index; parsedStatus = "Available"; }
                                        else if (gbStatus.Contains("busy") || gbStatus.Contains("meeting")) { maxIndex = m.Index; parsedStatus = "Busy"; }
                                        else if (gbStatus.Contains("distrb") || gbStatus.Contains("disturb") || gbStatus.Contains("dnd")) { maxIndex = m.Index; parsedStatus = "DoNotDisturb"; }
                                        else if (gbStatus.Contains("away")) { maxIndex = m.Index; parsedStatus = "Away"; }
                                        else if (gbStatus.Contains("rightback") || gbStatus.Contains("brb")) { maxIndex = m.Index; parsedStatus = "BeRightBack"; }
                                    }
                                }

                                // Fallback: Suche nach "availability" (UserPresenceAction oder UserDataGlobalState)
                                // Auch wenn bei mehreren Accounts Ungenauigkeiten auftreten können, ist dies besser als gar kein Status.
                                var availabilityMatches = System.Text.RegularExpressions.Regex.Matches(content, @"\""availability\""\s*:\s*\""([a-zA-Z]+)\""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                foreach (System.Text.RegularExpressions.Match m in availabilityMatches)
                                {
                                    if (m.Index > maxIndex)
                                    {
                                        string availStatus = m.Groups[1].Value.ToLower();
                                        if (availStatus.Contains("avail") || availStatus == "free") { maxIndex = m.Index; parsedStatus = "Available"; }
                                        else if (availStatus.Contains("busy") || availStatus.Contains("meeting")) { maxIndex = m.Index; parsedStatus = "Busy"; }
                                        else if (availStatus.Contains("distrb") || availStatus.Contains("disturb") || availStatus.Contains("dnd")) { maxIndex = m.Index; parsedStatus = "DoNotDisturb"; }
                                        else if (availStatus.Contains("away")) { maxIndex = m.Index; parsedStatus = "Away"; }
                                        else if (availStatus.Contains("rightback") || availStatus.Contains("brb")) { maxIndex = m.Index; parsedStatus = "BeRightBack"; }
                                    }
                                }

                                // Check Calls
                                int lastIncomingCallIdx = -1;
                                var incomingMatches = System.Text.RegularExpressions.Regex.Matches(content, @"reportIncomingCall");
                                foreach (System.Text.RegularExpressions.Match m in incomingMatches) if (m.Index > lastIncomingCallIdx) lastIncomingCallIdx = m.Index;

                                int lastCallEndedIdx = -1;
                                var endedMatches = System.Text.RegularExpressions.Regex.Matches(content, @"reportCall(Ended|Answered|Accepted|Connected)");
                                foreach (System.Text.RegularExpressions.Match m in endedMatches) if (m.Index > lastCallEndedIdx) lastCallEndedIdx = m.Index;

                                if (lastIncomingCallIdx > lastCallEndedIdx && lastIncomingCallIdx > maxIndex && !_isWebSocketInMeeting)
                                {
                                    parsedStatus = "Ringing";
                                    maxIndex = lastIncomingCallIdx;
                                }

                                if (maxIndex > -1)
                                {
                                    if (parsedStatus != _lastParsedLogStatus)
                                    {
                                        _lastParsedLogStatus = parsedStatus;
                                        
                                        // Der Log-Scanner hat jetzt wieder volle Priorität über die Farben!
                                        if (parsedStatus.Equals("Available", StringComparison.OrdinalIgnoreCase))
                                            UpdateStatus("Auto: Verfügbar", 'A');
                                        else if (parsedStatus == "DoNotDisturb" || parsedStatus == "dnd")
                                            UpdateStatus("Auto: Nicht stören", 'D');
                                        else if (parsedStatus == "Busy" || parsedStatus == "InAMeeting")
                                            UpdateStatus("Auto: Beschäftigt", 'B');
                                        else if (parsedStatus == "Away" || parsedStatus == "BeRightBack" || parsedStatus == "brb")
                                            UpdateStatus("Auto: Abwesend", 'W');
                                        else if (parsedStatus == "Ringing")
                                            UpdateStatus("Auto: Eingehender Anruf (Klingelt)", 'R');
                                    }
                                    
                                    foundStatus = true;
                                    break;
                                }
                            }
                            catch (IOException ex)
                            {
                                Log($"Fehler beim Zugriff auf Datei {logFile.FullName}: {ex.Message}");
                                // Falls diese Datei exklusiv gesperrt ist, probiere die nächste
                                continue;
                            }
                            catch (Exception ex)
                            {
                                Log($"Unerwarteter Fehler beim Lesen von {logFile.FullName}: {ex.Message}");
                                continue;
                            }
                        }

                        if (!foundStatus)
                        {
                            Log("In den überprüften Logdateien wurde kein bekannter Status gefunden.");
                        }
                    }
                    catch (IOException ex)
                    {
                        Log($"Allgemeiner IO-Fehler im Überwachungs-Loop: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Unerwarteter Fehler im Überwachungs-Loop: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Task wurde abgebrochen (z.B. Wechsel auf manuellen Modus)
            }
        }

        private string GetShortcutPath()
        {
            string startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(startupDir, "TeamsStatusMonitor.lnk");
        }

        private async void BtnInstallAutostart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string shortcutPath = GetShortcutPath();
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";

                if (string.IsNullOrEmpty(exePath))
                {
                    throw new Exception("Konnte den Pfad der Executable nicht ermitteln.");
                }

                // COM-Objekt WScript.Shell per Reflection erstellen (späte Bindung),
                // so sparen wir uns die COM-Referenz im Projekt, die in .NET Core oft Probleme macht.
                Type? t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) throw new Exception("WScript.Shell Type nicht gefunden.");
                dynamic? shell = Activator.CreateInstance(t);
                if (shell == null) throw new Exception("WScript.Shell Instanz konnte nicht erstellt werden.");
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                
                shortcut.TargetPath = exePath;
                shortcut.Arguments = "-autostart";
                shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                shortcut.Description = "Teams Status Monitor Autostart";
                shortcut.Save();

                await ShowFluentMessageBoxAsync("Erfolg", "Autostart-Verknüpfung erfolgreich im Autostart-Ordner erstellt.");
            }
            catch (Exception ex)
            {
                await ShowFluentMessageBoxAsync("Fehler", $"Fehler beim Erstellen der Verknüpfung: {ex.Message}");
            }
        }

        private async void BtnUninstallAutostart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string shortcutPath = GetShortcutPath();
                if (System.IO.File.Exists(shortcutPath))
                {
                    System.IO.File.Delete(shortcutPath);
                    await ShowFluentMessageBoxAsync("Erfolg", "Autostart-Verknüpfung wurde entfernt.");
                }
                else
                {
                    await ShowFluentMessageBoxAsync("Info", "Es wurde keine Autostart-Verknüpfung gefunden.");
                }
            }
            catch (Exception ex)
            {
                await ShowFluentMessageBoxAsync("Fehler", $"Fehler beim Entfernen der Verknüpfung: {ex.Message}");
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Beim Schließen auf das X das Fenster nur verstecken
            e.Cancel = true;
            this.Hide();
        }

        private void MenuItem_Open_Click(object sender, RoutedEventArgs e)
        {
            this.ShowInTaskbar = true;
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.Topmost = true;  // kurz in den Vordergrund zwingen
            this.Topmost = false;
            _isLoaded = true;
        }

        private System.Windows.Threading.DispatcherTimer? _updateCheckTimer;

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Ein kurzer Delay ist ZWINGEND erforderlich, da WPF-UI (FluentWindow) 
            // das WindowChrome (Titelleiste) initialisiert. Wenn sofort im Loaded-Event 
            // ein modaler Dialog (MessageBox) geöffnet wird, unterbricht dies den Message Loop,
            // und die Buttons "X" (Schließen) sowie "Minimieren" werden nicht anklickbar (als wäre eine unsichtbare Ebene darüber).
            await Task.Delay(500);

            string[] args = Environment.GetCommandLineArgs();
            if (args.Contains("-updated"))
            {
                await ShowFluentMessageBoxAsync("Update erfolgreich", "Das Update wurde erfolgreich installiert!");
            }
            else
            {
                _ = CheckAndPerformAppUpdate(false);
            }
            
            // Start cyclical update check every hour
            _updateCheckTimer = new System.Windows.Threading.DispatcherTimer();
            _updateCheckTimer.Interval = TimeSpan.FromHours(1);
            _updateCheckTimer.Tick += (s, args) =>
            {
                _ = CheckAndPerformAppUpdate(false);
            };
            _updateCheckTimer.Start();
        }


        private async Task<Wpf.Ui.Controls.MessageBoxResult> ShowFluentMessageBoxAsync(string title, string content, bool isYesNo = false)
        {
            var uiMessageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = title,
                Content = content,
                ShowTitle = true
            };

            if (isYesNo)
            {
                uiMessageBox.PrimaryButtonText = "Ja";
                uiMessageBox.CloseButtonText = "Nein";
            }
            else
            {
                uiMessageBox.CloseButtonText = "OK";
            }

            return await uiMessageBox.ShowDialogAsync();
        }

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            StopMonitoring();
            if (MyNotifyIcon != null)
            {
                MyNotifyIcon.Dispose();
            }
            if (_webSocketService != null)
            {
                _webSocketService.Stop();
            }
            Application.Current.Shutdown();
        }

        private async void MenuItem_OpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logPath = GetLogFilePath();
                if (System.IO.File.Exists(logPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logPath) { UseShellExecute = true });
                }
                else
                {
                    await ShowFluentMessageBoxAsync("Hinweis", "Die Log-Datei existiert noch nicht.");
                }
            }
            catch (Exception ex)
            {
                await ShowFluentMessageBoxAsync("Fehler", $"Fehler beim Öffnen der Log-Datei: {ex.Message}");
            }
        }

        private void MenuItem_StatusAuto_Click(object sender, RoutedEventArgs e) => SetMode("Auto");
        private void MenuItem_StatusAvailable_Click(object sender, RoutedEventArgs e) => SetMode("A");
        private void MenuItem_StatusBusy_Click(object sender, RoutedEventArgs e) => SetMode("B");
        private void MenuItem_StatusAway_Click(object sender, RoutedEventArgs e) => SetMode("W");

        private void MenuItem_Update_Click(object sender, RoutedEventArgs e)
        {
            _ = CheckAndPerformAppUpdate(true);
        }

        private void MenuItem_FirmwareUpdate_Click(object sender, RoutedEventArgs e)
        {
            _ = CheckAndPerformFirmwareUpdate();
        }

        private void MenuItem_Info_Click(object sender, RoutedEventArgs e)
        {
            var infoWindow = new InfoWindow();
            infoWindow.Owner = this;
            infoWindow.ShowDialog();
        }

        private bool _isUpdateCheckRunning = false;

        /// <summary>
        /// Prüft asynchron die GitHub Releases-API auf neue App-Versionen.
        /// Falls eine neuere Version gefunden wird, kann ein Dialog zur direkten Installation angezeigt werden.
        /// </summary>
        /// <param name="showMessageIfUpToDate">Gibt an, ob eine "Kein Update verfügbar"-Meldung erscheinen soll.</param>
        public async Task CheckAndPerformAppUpdate(bool showMessageIfUpToDate = false)
        {
            if (_isUpdateCheckRunning) return;
            _isUpdateCheckRunning = true;
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "TeamsStatusMonitor");
                
                string url = "https://api.github.com/repos/seipekm/TeamsStatusMonitor/releases";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    if (showMessageIfUpToDate)
                        await ShowFluentMessageBoxAsync("Fehler", "Keine Verbindung zu GitHub möglich.");
                    return;
                }
                
                string json = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                
                JsonElement? latestAppRelease = null;
                Version? maxAppVersion = null;
                
                foreach (var release in doc.RootElement.EnumerateArray())
                {
                    string tagName = release.GetProperty("tag_name").GetString() ?? "";
                    if (tagName.StartsWith("app-v") || (tagName.StartsWith("v") && !tagName.StartsWith("fw-v")))
                    {
                        string versionStr = tagName.StartsWith("app-v") ? tagName.Substring(5) : tagName.TrimStart('v');
                        if (Version.TryParse(versionStr, out Version? v))
                        {
                            if (maxAppVersion == null || v > maxAppVersion)
                            {
                                maxAppVersion = v;
                                latestAppRelease = release;
                            }
                        }
                    }
                }

                if (latestAppRelease == null)
                {
                    if (showMessageIfUpToDate)
                        await ShowFluentMessageBoxAsync("Info", "Keine App-Releases gefunden.");
                    return;
                }

                var root = latestAppRelease.Value;
                string tag = root.GetProperty("tag_name").GetString() ?? "";
                string version = tag.StartsWith("app-v") ? tag.Substring(5) : tag.TrimStart('v');
                
                // Aktuelle Version abfragen
                string currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
                
                // In C# wird oft ein vierstelliger String zurückgegeben, wir vergleichen die Major.Minor.Build
                if (Version.TryParse(version, out Version? latestV) && Version.TryParse(currentVersion, out Version? currentV) && latestV != null && currentV != null)
                {
                    if (latestV > currentV)
                    {
                        var result = await ShowFluentMessageBoxAsync("Update verfügbar", $"Ein Update auf Version {version} ist verfügbar!\nJetzt herunterladen und installieren?", true);
                        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                        {
                            string downloadUrl = "";
                            if (root.TryGetProperty("assets", out JsonElement assets))
                            {
                                foreach (var asset in assets.EnumerateArray())
                                {
                                    if (asset.GetProperty("name").GetString() == "TeamsStatusMonitor_Windows_x64.zip")
                                    {
                                        downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                        break;
                                    }
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(downloadUrl))
                            {
                                var updateWindow = new UpdateWindow(downloadUrl);
                                updateWindow.Owner = this;
                                updateWindow.ShowDialog();
                            }
                            else
                            {
                                await ShowFluentMessageBoxAsync("Fehler", "Das Update-Paket (ZIP) konnte im Release nicht gefunden werden.");
                            }
                        }
                    }
                    else
                    {
                        if (showMessageIfUpToDate)
                            await ShowFluentMessageBoxAsync("Kein Update", $"Die App ist bereits auf dem neuesten Stand (Version {currentVersion}).");
                    }
                }
            }
            catch (Exception ex)
            {
                if (showMessageIfUpToDate)
                    await ShowFluentMessageBoxAsync("Fehler", $"Fehler bei der Update-Prüfung: {ex.Message}");
            }
            finally
            {
                _isUpdateCheckRunning = false;
            }
        }


        private void MyNotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            MenuItem_Open_Click(sender, e);
        }
        
        private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            LoadPorts();
        }

        private void HighlightActiveButton(string tag)
        {
            if (GridEffects != null)
            {
                foreach (var child in GridEffects.Children)
                {
                    if (child is Wpf.Ui.Controls.Button b)
                    {
                        if (b.Tag?.ToString() == tag)
                        {
                            b.BorderThickness = new Thickness(3);
                            b.BorderBrush = Brushes.White;
                        }
                        else
                        {
                            b.BorderThickness = new Thickness(0);
                        }
                    }
                }
            }
        }

        private void BtnEffect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag != null)
            {
                string tag = btn.Tag.ToString() ?? "";
                HighlightActiveButton(tag);
                if (tag == "Auto")
                {
                    ChkMonitorStatus.IsChecked = true;
                }
                else
                {
                    ChkMonitorStatus.IsChecked = false;
                    _lastStatus = tag; // Set last status so cyclical timer keeps sending it
                    _lastUiStatusText = "Manuell";
                    SendStatus(tag);
                }
            }
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void ChkStartWindows_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SaveSettings();
            if (ChkStartWindows.IsChecked == true)
            {
                BtnInstallAutostart_Click(sender, e);
            }
            else
            {
                BtnUninstallAutostart_Click(sender, e);
            }
        }

        private void ChkMonitorStatus_Changed(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            if (ChkMonitorStatus.IsChecked == true)
            {
                StartMonitoring();
                UpdateStatus("Suche Log...", 'U');
                HighlightActiveButton("Auto");
            }
            else
            {
                StopMonitoring();
            }
        }


        private void TxtManualCommand_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                BtnManualCommand_Click(sender, e);
            }
        }

        private void BtnManualCommand_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtManualCommand.Text))
            {
                SendStatus(TxtManualCommand.Text);
                TxtManualCommand.Clear();
            }
        }
    }
}
