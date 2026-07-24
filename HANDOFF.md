# Teams Status Monitor - Handoff / Gedächtnisdatei

Diese Datei dient als Gedächtnis und Dokumentation für zukünftige Arbeiten an diesem Projekt.

## Projektübersicht
Ein C# / WPF Windows-Tool, das den Microsoft Teams Status über die lokale WebSocket-API ausliest und per USB (Seriell) an einen Mikrocontroller (mit angeschlossenem WS2812 LED-Strip/Matrix) sendet.

## Unterstützte Hardware (Firmware)
Der Code (PlatformIO in `RP2040_Firmware`) unterstützt mittlerweile drei Architekturen, die jeweils über GitHub Actions parallel gebaut werden:
1. **RP2040** (Standard Raspberry Pi Pico) - `.uf2`
2. **RP2350** (Waveshare RP2350-Zero) - `.uf2`
3. **ESP32** (z.B. Seeed Studio XIAO ESP32S3) - `.bin`

**Wichtige Firmware-Details:**
- **Pin-Belegung:** WS2812 Data-Pin ist generell Pin 15 (passend für aufsteckbare Matrix) oder Pin 16 (on-board).
- **Architektur-Erkennung:** Die Firmware antwortet auf den `VERSION` Befehl dynamisch mit `VERSION:1.1,ARCH:RP2040` (bzw. ESP32 / RP2350), damit die C# App weiß, welches Update-Asset sie laden muss.
- **ESP32 Besonderheit (Native USB CDC):** Im `setup()` wurde explizit `Serial.setTxTimeoutMs(0);` hinzugefügt, da sich Native-USB ESP32-Boards andernfalls komplett aufhängen, wenn die PC-App geschlossen oder die Verbindung getrennt wird.

## Windows App (C#)
- **COM-Port Erkennung:** Die App scannt die USB PnP-IDs der angeschlossenen COM-Ports. Sie filtert **nur** nach spezifischen VIDs/PIDs (RP2040, ESP32, "Teams Status Monitor"). Bei diesen Geräten wird versucht, direkt die USB-Seriennummer auszulesen, damit diese übersichtlich in der Dropdown-Liste steht (statt "Serielles USB-Gerät").
- **Auto-Update (Flashen):** Wenn ein Firmware-Update für einen RP2xxx ansteht, sendet die App den `UPDATE`-Befehl (löst `rebootToBootloader` aus). Anschließend sucht sie nach Laufwerken mit dem Namen `RPI-RP2` (RP2040) **oder** `RP2350`, um das `.uf2`-File automatisch dorthin zu kopieren.
- **Teams Prozess-Erkennung:** Um zuverlässig mitzubekommen, ob Teams läuft, sucht der `TeamsWebSocketService` nach den Prozessen `ms-teams` (neues Teams) und `Teams` (klassisches Teams). Ebenfalls wird der Status sauber zurückgesetzt, wenn ein Call beendet wird.

## CI/CD Pipeline
- `.github/workflows/release-firmware.yml`: Bei Erstellung eines neuen GitHub-Releases (Tags, z.B. `fw-v1.2.18`) wird der PlatformIO Build-Prozess für alle drei Architekturen gestartet, die Binaries (uf2/bin) richtig umbenannt und als Release-Assets angehängt. Die App greift für automatische Updates auf diese Assets zu.

## Zukünftige To-Dos / Letzte Änderungen
- **App-Version:** Zuletzt auf `v1.2.26` angehoben (Fix für das Dropdown-Filtering).
- **Firmware-Version:** Zuletzt auf `v1.2.18` angehoben (Fix für ESP32 USB Freeze).
