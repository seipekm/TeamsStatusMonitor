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
using System.IO.Compression;

namespace TeamsStatus
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private SerialPort? _serialPort;
        private CancellationTokenSource? _cts;
        private string _lastStatus = "";
        private System.Windows.Threading.DispatcherTimer _sendTimer;
        private bool _isLoaded = false;
        private bool _isConnected = false;
        private string _currentMode = "Auto";

        public MainWindow()
        {
            InitializeComponent();
            
            // Check for update success
            string[] args = Environment.GetCommandLineArgs();
            if (args.Contains("-updated"))
            {
                MessageBox.Show("Das Update wurde erfolgreich installiert!", "Update erfolgreich", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            
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
            LoadSettings();
            _isLoaded = true;
            ConnectSerial(); // Automatisch beim Start mit den geladenen Settings verbinden
            
            // Start im zuletzt gespeicherten Modus
            if (_currentMode == "Auto" && !string.IsNullOrEmpty(_lastStatus) && _lastStatus != "U")
            {
                // Wenn Auto-Modus und wir einen alten Status haben, starte Monitoring, aber überschreibe nicht mit "Suche Log..."
                BtnModeAuto.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                MenuStatusAuto.IsChecked = true;
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

        private void LoadPorts()
        {
            CmbPorts.ItemsSource = SerialPort.GetPortNames();
            if (CmbPorts.Items.Count > 0)
                CmbPorts.SelectedIndex = 0;
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
                    PortName = CmbPorts.SelectedItem?.ToString() ?? "",
                    BaudRate = (CmbBaudRate.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "9600",
                    Brightness = SldBrightness.Value,
                    CurrentMode = _currentMode,
                    LastStatus = _lastStatus,
                    LastStatusText = _lastUiStatusText
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
                    if (root.TryGetProperty("BaudRate", out var br))
                    {
                        string savedBaud = br.GetString() ?? "9600";
                        foreach (ComboBoxItem item in CmbBaudRate.Items)
                        {
                            if (item.Content.ToString() == savedBaud)
                            {
                                CmbBaudRate.SelectedItem = item;
                                break;
                            }
                        }
                    }
                    if (root.TryGetProperty("PortName", out var pn))
                    {
                        string savedPort = pn.GetString() ?? "";
                        if (!string.IsNullOrEmpty(savedPort) && CmbPorts.Items.Contains(savedPort))
                        {
                            CmbPorts.SelectedItem = savedPort;
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
                }
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

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
            {
                DisconnectSerial();
            }
            else
            {
                ConnectSerial();
            }
        }

        private void ConnectSerial()
        {
            if (CmbPorts.SelectedItem == null) return;
            string port = CmbPorts.SelectedItem.ToString() ?? string.Empty;
            
            int baudRate = 9600;
            if (CmbBaudRate != null && CmbBaudRate.SelectedItem is ComboBoxItem selectedItem)
            {
                int.TryParse(selectedItem.Content.ToString(), out baudRate);
            }

            DisconnectSerial();

            try
            {
                _serialPort = new SerialPort(port, baudRate);
                _serialPort.Open();
                SetConnectionStatus(true);
                SendStatus(_lastStatus); // Zuletzt bekannten Status senden
            }
            catch (Exception ex)
            {
                SetConnectionStatus(false);
                MessageBox.Show($"Fehler beim Öffnen des COM-Ports: {ex.Message}");
            }
        }

        private void DisconnectSerial()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try { _serialPort.Close(); } catch { }
                _serialPort.Dispose();
                _serialPort = null;
            }
            SetConnectionStatus(false);
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
                if (StatusIcon != null)
                {
                    UpdateTrayIcon((SolidColorBrush)StatusIcon.Fill);
                }
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
            SaveSettings();

            // Reset all buttons and menus
            BtnModeAuto.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            BtnModeAvailable.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            BtnModeBusy.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            BtnModeAway.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;

            MenuStatusAuto.IsChecked = false;
            MenuStatusAvailable.IsChecked = false;
            MenuStatusBusy.IsChecked = false;
            MenuStatusAway.IsChecked = false;

            if (tag == "Auto") 
            {
                BtnModeAuto.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                MenuStatusAuto.IsChecked = true;
            }
            else if (tag == "A") 
            {
                BtnModeAvailable.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                MenuStatusAvailable.IsChecked = true;
            }
            else if (tag == "B") 
            {
                BtnModeBusy.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                MenuStatusBusy.IsChecked = true;
            }
            else if (tag == "W") 
            {
                BtnModeAway.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                MenuStatusAway.IsChecked = true;
            }

            if (tag == "Auto")
            {
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
                TxtStatus.Text = $"Status: {statusText}";
                
                // Icon Farbe aktualisieren
                Brush fillBrush = Brushes.Gray;
                if (command == 'A') fillBrush = Brushes.LimeGreen;
                else if (command == 'B') fillBrush = Brushes.Red;
                else if (command == 'D') fillBrush = Brushes.DarkRed;
                else if (command == 'W') fillBrush = Brushes.Orange;
                
                StatusIcon.Fill = fillBrush;
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

        private void SendStatus(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    // RGB-Werte anhand des Status ermitteln
                    int r = 128, g = 128, b = 128; // Standard: Grau
                    if (data == "A") { r = 50; g = 205; b = 50; } // LimeGreen (Verfügbar)
                    else if (data == "B") { r = 255; g = 0; b = 0; } // Red (Beschäftigt)
                    else if (data == "D") { r = 139; g = 0; b = 0; } // DarkRed (Nicht stören)
                    else if (data == "W") { r = 255; g = 165; b = 0; } // Orange (Abwesend/Gleich zurück)

                    // Lese aktuelle Helligkeit sicher aus der UI aus
                    int brightness = 128;
                    Dispatcher.Invoke(() => {
                        if (SldBrightness != null)
                            brightness = (int)SldBrightness.Value;
                    });

                    // Sende Format: R,G,B,Helligkeit (z.B. "50,205,50,255\n")
                    string command = $"{r},{g},{b},{brightness}\n";
                    _serialPort.Write(command);
                    
                    if (!_isConnected) SetConnectionStatus(true);
                }
                catch 
                {
                    if (_isConnected) SetConnectionStatus(false);
                }
            }
            else
            {
                if (_isConnected) SetConnectionStatus(false);
            }
        }

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

        private async Task MonitorTeamsLogAsync(CancellationToken token)
        {
            // Ordner-Pfade
            string classicLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Teams");
            string classicLogPath = Path.Combine(classicLogDir, "logs.txt");
            string newTeamsLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache", "Microsoft", "MSTeams", "Logs");

            // PeriodicTimer zum regelmäßigen Überprüfen (500ms = extrem schnell, aber zuverlässig!)
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    try
                    {
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

                        if (System.IO.File.Exists(classicLogPath))
                        {
                            filesToCheck.Add(new FileInfo(classicLogPath));
                        }

                        if (filesToCheck.Count == 0)
                        {
                            Log("Es konnte keine Teams Logdatei (weder Classic noch New) gefunden werden.");
                            Dispatcher.Invoke(() => TxtStatus.Text = "Status: Weder Classic noch New Teams Log gefunden");
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
                                
                                // Nur bei der alten classic "logs.txt" das Lesen auf 256 KB beschränken, da diese Datei 100+ MB werden kann.
                                // Die New Teams Logs ("MSTeams_*.log") rotieren bei exakt 2 MB. Diese lesen wir komplett,
                                // um zu verhindern, dass alte Statusmeldungen aus dem 256KB-Fenster rutschen.
                                if (logFile.Name.ToLower() == "logs.txt" && stream.Length > 262144) 
                                {
                                    stream.Seek(-262144, SeekOrigin.End);
                                }
                                
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

                                if (maxIndex > -1)
                                {
                                    if (parsedStatus.Equals("Available", StringComparison.OrdinalIgnoreCase))
                                        UpdateStatus("Auto: Verfügbar", 'A');
                                    else if (parsedStatus == "DoNotDisturb" || parsedStatus == "dnd")
                                        UpdateStatus("Auto: Nicht stören", 'D');
                                    else if (parsedStatus == "Busy" || parsedStatus == "InAMeeting")
                                        UpdateStatus("Auto: Beschäftigt", 'B');
                                    else if (parsedStatus == "Away" || parsedStatus == "BeRightBack" || parsedStatus == "brb")
                                        UpdateStatus("Auto: Abwesend", 'W');
                                    
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

        private void BtnInstallAutostart_Click(object sender, RoutedEventArgs e)
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

                MessageBox.Show("Autostart-Verknüpfung erfolgreich im Autostart-Ordner erstellt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Erstellen der Verknüpfung: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnUninstallAutostart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string shortcutPath = GetShortcutPath();
                if (System.IO.File.Exists(shortcutPath))
                {
                    System.IO.File.Delete(shortcutPath);
                    MessageBox.Show("Autostart-Verknüpfung wurde entfernt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Es wurde keine Autostart-Verknüpfung gefunden.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Entfernen der Verknüpfung: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
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
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            StopMonitoring();
            Application.Current.Shutdown();
        }

        private void MenuItem_OpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logPath = GetLogFilePath();
                if (System.IO.File.Exists(logPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = logPath,
                        UseShellExecute = true
                    });
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

        private void MenuItem_StatusAuto_Click(object sender, RoutedEventArgs e) => SetMode("Auto");
        private void MenuItem_StatusAvailable_Click(object sender, RoutedEventArgs e) => SetMode("A");
        private void MenuItem_StatusBusy_Click(object sender, RoutedEventArgs e) => SetMode("B");
        private void MenuItem_StatusAway_Click(object sender, RoutedEventArgs e) => SetMode("W");

        private void MenuItem_Update_Click(object sender, RoutedEventArgs e)
        {
            CheckForUpdates(true);
        }

        private void MenuItem_Info_Click(object sender, RoutedEventArgs e)
        {
            var infoWindow = new InfoWindow();
            infoWindow.Owner = this;
            infoWindow.ShowDialog();
        }

        private async void CheckForUpdates(bool showMessageIfUpToDate)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "TeamsStatusMonitor");
                
                string url = "https://api.github.com/repos/seipekm/TeamsStatusMonitor/releases/latest";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show("Keine Verbindung zu GitHub möglich.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                string json = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string tag = root.GetProperty("tag_name").GetString() ?? "";
                string version = tag.TrimStart('v');
                
                // Aktuelle Version abfragen
                string currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
                
                // In C# wird oft ein vierstelliger String zurückgegeben, wir vergleichen die Major.Minor.Build
                if (Version.TryParse(version, out Version? latestV) && Version.TryParse(currentVersion, out Version? currentV) && latestV != null && currentV != null)
                {
                    if (latestV > currentV)
                    {
                        var result = MessageBox.Show($"Ein Update auf Version {version} ist verfügbar!\nJetzt herunterladen und installieren?", "Update verfügbar", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (result == MessageBoxResult.Yes)
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
                                MessageBox.Show("Das Update-Paket (ZIP) konnte im Release nicht gefunden werden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show($"Die App ist bereits auf dem neuesten Stand (Version {currentVersion}).", "Kein Update", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler bei der Update-Prüfung: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void MyNotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            MenuItem_Open_Click(sender, e);
        }
    }
}
