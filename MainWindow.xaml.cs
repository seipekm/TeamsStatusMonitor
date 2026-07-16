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

namespace TeamsStatus
{
    public partial class MainWindow : Window
    {
        private SerialPort? _serialPort;
        private CancellationTokenSource? _cts;
        private string _lastStatus = "";
        private System.Windows.Threading.DispatcherTimer _sendTimer;
        private bool _isLoaded = false;
        private bool _isConnected = false;

        public MainWindow()
        {
            InitializeComponent();
            
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
            BtnMode_Click(BtnModeAuto, new RoutedEventArgs()); // Standard: Auto-Modus
        }

        private void LoadPorts()
        {
            CmbPorts.ItemsSource = SerialPort.GetPortNames();
            if (CmbPorts.Items.Count > 0)
                CmbPorts.SelectedIndex = 0;
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
                    Brightness = SldBrightness.Value
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
                }
            }
            catch { }
        }

        private void CmbBaudRate_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Bei Baudraten-Änderung einfach den COM-Port neu initialisieren (falls einer gewählt ist)
            if (CmbPorts != null && CmbPorts.SelectedItem != null)
            {
                CmbPorts_SelectionChanged(null!, null!);
            }
            SaveSettings();
        }

        private void CmbPorts_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbPorts.SelectedItem == null) return;
            string port = CmbPorts.SelectedItem.ToString() ?? string.Empty;
            
            // Baudrate auslesen
            int baudRate = 9600;
            if (CmbBaudRate != null && CmbBaudRate.SelectedItem is ComboBoxItem selectedItem)
            {
                int.TryParse(selectedItem.Content.ToString(), out baudRate);
            }

            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                _serialPort.Dispose();
            }

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
            SaveSettings();
        }

        private void SetConnectionStatus(bool connected)
        {
            _isConnected = connected;
            Dispatcher.Invoke(() => {
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

            // Reset all buttons
            BtnModeAuto.Background = SystemColors.ControlBrush;
            BtnModeAvailable.Background = SystemColors.ControlBrush;
            BtnModeBusy.Background = SystemColors.ControlBrush;
            BtnModeAway.Background = SystemColors.ControlBrush;

            btn.Background = Brushes.LightBlue;

            string tag = btn.Tag?.ToString() ?? "Auto";

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

        private void UpdateStatus(string statusText, char command)
        {
            _lastStatus = command.ToString();
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
                
                IntPtr hIcon = bmp.GetHicon();
                MyNotifyIcon.Icon = System.Drawing.Icon.FromHandle(hIcon);
            }
        }

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
                        // Automatische Erkennung des neuesten New Teams Logs
                        string newTeamsLogPath = "";
                        DateTime newWriteTime = DateTime.MinValue;

                        if (Directory.Exists(newTeamsLogDir))
                        {
                            var logFiles = Directory.GetFiles(newTeamsLogDir, "MSTeams_*.log");
                            if (logFiles.Length > 0)
                            {
                                var latestLog = logFiles.Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).First();
                                newTeamsLogPath = latestLog.FullName;
                                newWriteTime = latestLog.LastWriteTime;
                            }
                        }

                        // Automatische Erkennung für Classic Teams
                        DateTime classicWriteTime = System.IO.File.Exists(classicLogPath) ? System.IO.File.GetLastWriteTime(classicLogPath) : DateTime.MinValue;

                        if (classicWriteTime == DateTime.MinValue && newWriteTime == DateTime.MinValue)
                        {
                            Dispatcher.Invoke(() => TxtStatus.Text = "Status: Weder Classic noch New Teams Log gefunden");
                            continue;
                        }

                        // Wähle die Datei, die zuletzt beschrieben wurde
                        string logPathToUse = newWriteTime > classicWriteTime ? newTeamsLogPath : classicLogPath;
                        string teamsVersion = newWriteTime > classicWriteTime ? "New Teams" : "Classic Teams";

                        // FileShare.ReadWrite ist wichtig, da Teams die Datei offen hält
                        using var stream = new FileStream(logPathToUse, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        
                        // Stream vor dem Erstellen des StreamReaders verschieben, sonst liefert der Puffer Müll!
                        if (stream.Length > 131072) // 128 KB
                        {
                            stream.Seek(-131072, SeekOrigin.End);
                        }
                        
                        using var reader = new StreamReader(stream);
                        string content = await reader.ReadToEndAsync(token);

                        // Suche nach dem allerletzten Vorkommen eines Status in den gelesenen Daten (sowohl Classic als auch New Teams)
                        int lastAvailable = Math.Max(
                            content.LastIndexOf("StatusIndicatorStateService: Added Available"), 
                            Math.Max(content.LastIndexOf("GlyphBadge{\"available\"}"), 
                                     content.LastIndexOf("availability: Available")));
                        
                        int lastBusy = Math.Max(
                            content.LastIndexOf("StatusIndicatorStateService: Added Busy"), 
                            Math.Max(content.LastIndexOf("GlyphBadge{\"busy\"}"), 
                                     content.LastIndexOf("availability: Busy")));

                        int lastDnd = Math.Max(
                            content.LastIndexOf("StatusIndicatorStateService: Added DoNotDisturb"), 
                            Math.Max(content.LastIndexOf("GlyphBadge{\"dnd\"}"), 
                                     content.LastIndexOf("availability: DoNotDisturb")));

                        int lastMeeting = Math.Max(
                            content.LastIndexOf("StatusIndicatorStateService: Added InAMeeting"), 
                            Math.Max(content.LastIndexOf("GlyphBadge{\"in-a-meeting\"}"), 
                                     content.LastIndexOf("availability: InAMeeting")));

                        int lastAway = Math.Max(
                            content.LastIndexOf("StatusIndicatorStateService: Added Away"), 
                            Math.Max(content.LastIndexOf("GlyphBadge{\"away\"}"), 
                                     content.LastIndexOf("availability: Away")));

                        int lastBrb = Math.Max(
                            content.LastIndexOf("StatusIndicatorStateService: Added BeRightBack"), 
                            Math.Max(content.LastIndexOf("GlyphBadge{\"brb\"}"), 
                                     content.LastIndexOf("availability: BeRightBack")));

                        int maxIndex = Math.Max(lastAvailable, 
                                       Math.Max(lastBusy, 
                                       Math.Max(lastDnd, 
                                       Math.Max(lastMeeting, 
                                       Math.Max(lastAway, lastBrb)))));
                        
                        if (maxIndex > -1)
                        {
                            if (maxIndex == lastAvailable)
                                UpdateStatus("Auto: Verfügbar", 'A');
                            else if (maxIndex == lastDnd)
                                UpdateStatus("Auto: Nicht stören", 'D');
                            else if (maxIndex == lastMeeting)
                                UpdateStatus("Auto: Im Termin", 'B'); 
                            else if (maxIndex == lastBusy)
                                UpdateStatus("Auto: Beschäftigt", 'B');
                            else if (maxIndex == lastBrb)
                                UpdateStatus("Auto: Gleich zurück", 'W');
                            else if (maxIndex == lastAway)
                                UpdateStatus("Auto: Abwesend", 'W');
                        }
                    }
                    catch (IOException)
                    {
                        // Wird geworfen, wenn die Datei kurzzeitig nicht lesbar ist -> ignorieren
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
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
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
            
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                _serialPort.Dispose();
            }

            MyNotifyIcon.Dispose();
            Application.Current.Shutdown();
        }

        private void MyNotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            MenuItem_Open_Click(sender, e);
        }
    }
}
