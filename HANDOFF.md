# Teams Status Monitor - Handoff / Gedächtnisdatei

Diese Datei dient als Gedächtnis und Dokumentation für zukünftige Arbeiten an diesem Projekt.

## Projektübersicht
Ein C# / WPF Windows-Tool, das den Microsoft Teams Status über die lokale WebSocket-API ausliest und per USB (Seriell) an einen Mikrocontroller (mit angeschlossenem WS2812 LED-Strip/Matrix) sendet.

## Unterstützte Hardware (Firmware)
Der Code (PlatformIO in `PlatformIO`) unterstützt mittlerweile drei Architekturen, die jeweils über GitHub Actions parallel gebaut werden:
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

## Historie (Erledigte Arbeiten)
Damit der Kontext nicht verloren geht, hier ein Protokoll der wichtigsten und zuletzt durchgeführten Implementierungen:
1. **RP2350 Support**: Vollständige Integration des *Waveshare RP2350-Zero* (Firmware Build Environment, Laufwerk-Erkennung `RP2350` beim Flashen).
2. **ESP32 Support**: Integration des *Seeed Studio XIAO ESP32S3* inkl. Auto-Update Mechanismus über `.bin` Dateien.
3. **ESP32 Native USB Fix**: Fehlerbehebung für einfrierende ESP32-Boards bei Verbindungsabbruch durch `Serial.setTxTimeoutMs(0)`.
4. **Erweiterte COM-Port Erkennung**: Anzeige der tatsächlichen Hardware-Seriennummern im UI-Dropdown anstelle generischer Namen für ESP32 und RP2xxx.
5. **Debug Log Viewer**: Hinzufügen eines Buttons im Info-Fenster ("Log-Datei öffnen (Fehlersuche)"), um schwer greifbare Teams-WebSocket Fehler (z.B. UI friert auf Grau ein) analysieren zu können.
6. **Automatisches Handoff & Versionierung**: Implementierung von KI-Regeln in `.agents/AGENTS.md` für automatische Release-Tagging und Projekt-Status-Aktualisierungen.

## Zukünftige To-Dos / Letzte Änderungen
- **App-Version:** Zuletzt auf `v1.2.34` angehoben (C# App setzt kein DTR/RTS Flag mehr beim Öffnen des Ports, wodurch verhindert wird, dass der ESP32-S3 beim Trennen in den ROM Bootloader resetet wird).
- **Firmware-Version:** Zuletzt auf `v1.2.21` angehoben (Korrektur der internen Versionsnummer, da in 1.2.20 der String in main.cpp versehentlich nicht aktualisiert wurde).

## KI / Agenten Regeln
- Es existiert ein `.agents/AGENTS.md` File, welches festlegt, dass diese `HANDOFF.md` immer automatisch von der KI bei Codeänderungen gepflegt werden muss. Dieses Regel-Verzeichnis liegt in Git und wird vom System automatisch eingelesen.
