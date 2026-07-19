using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace TeamsStatus
{
    public class TeamsWebSocketService
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private string _token;
        
        public event Action<string>? OnTokenReceived;
        public event Action<bool, bool>? OnMeetingStateChanged; // isInMeeting, isMuted
        public event Action<bool>? OnCallStateChanged; // isRinging
        public event Action<string>? OnLog;

        public TeamsWebSocketService(string initialToken)
        {
            _token = initialToken;
        }

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();
            _ = ConnectLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            if (_ws != null)
            {
                _ws.Dispose();
                _ws = null;
            }
        }

        private async Task ConnectLoopAsync(CancellationToken token)
        {
            bool wasTeamsRunning = true; // um Log-Spam beim Statuswechsel zu vermeiden

            while (!token.IsCancellationRequested)
            {
                try
                {
                    bool isTeamsRunning = Process.GetProcessesByName("ms-teams").Length > 0;
                    if (!isTeamsRunning)
                    {
                        if (wasTeamsRunning)
                        {
                            OnLog?.Invoke("WebSocket: Teams läuft nicht, warte auf Start...");
                            wasTeamsRunning = false;
                        }
                        await Task.Delay(5000, token);
                        continue;
                    }
                    wasTeamsRunning = true;

                    _ws = new ClientWebSocket();
                    string url = $"ws://127.0.0.1:8124?protocol-version=2.0.0&manufacturer=TeamsStatusMonitor&device=Monitor&app=TeamsStatusMonitor&app-version=1.0.0";
                    if (!string.IsNullOrEmpty(_token))
                    {
                        url += $"&token={_token}";
                    }

                    Uri uri = new Uri(url);
                    OnLog?.Invoke($"Versuche WebSocket Verbindung zu {url}");
                    await _ws.ConnectAsync(uri, token);
                    OnLog?.Invoke("WebSocket verbunden!");

                    await ReceiveLoopAsync(_ws, token);
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"WebSocket Fehler/Getrennt: {ex.Message}. Neuer Versuch in 5 Sekunden...");
                    await Task.Delay(5000, token);
                }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken token)
        {
            var buffer = new byte[8192];
            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, token);
                    break;
                }
                
                string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                OnLog?.Invoke($"WS Msg: {msg}");
                
                ParseMessage(msg);
            }
        }

        private void ParseMessage(string msg)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(msg);
                var root = doc.RootElement;

                // Handle pairing token
                if (root.TryGetProperty("tokenRefresh", out var tokenRefreshStr))
                {
                    _token = tokenRefreshStr.GetString() ?? "";
                    OnTokenReceived?.Invoke(_token);
                }
                
                // Handle meeting state
                if (root.TryGetProperty("meetingUpdate", out var meetingUpdate))
                {
                    if (meetingUpdate.TryGetProperty("meetingState", out var meetingState))
                    {
                        bool isInMeeting = false;
                        bool isMuted = false;

                        if (meetingState.TryGetProperty("isInMeeting", out var isInMeetingProp))
                        {
                            isInMeeting = isInMeetingProp.GetBoolean();
                        }
                        
                        if (meetingState.TryGetProperty("isMuted", out var isMutedProp))
                        {
                            isMuted = isMutedProp.GetBoolean();
                        }

                        if (meetingUpdate.TryGetProperty("callState", out var callStateProp) || meetingState.TryGetProperty("callState", out callStateProp))
                        {
                            string state = callStateProp.GetString() ?? "";
                            OnCallStateChanged?.Invoke(state.Equals("Ringing", StringComparison.OrdinalIgnoreCase));
                        }

                        OnMeetingStateChanged?.Invoke(isInMeeting, isMuted);
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("Fehler beim Parsen der WS-Nachricht: " + ex.Message);
            }
        }
    }
}
