# Teams Status Monitor & RP2040 LED Matrix Controller

Ein Zwei-Komponenten-Projekt, bestehend aus einer **C# WPF Desktop-App** und einer **C++ PlatformIO Firmware** für einen RP2040-Zero Mikrocontroller. Die Desktop-App liest automatisch deinen Microsoft Teams Status aus (verfügbar, beschäftigt, nicht stören, abwesend) und sendet ihn in Echtzeit mitsamt Helligkeits-Vorgaben über die serielle Schnittstelle an den Mikrocontroller. Der Controller visualisiert diesen Status auf einer WS2812 LED-Matrix (z. B. 4x4 oder Ring).

> **Inspiration & Credits:** Dieses Projekt entstand aus der Idee von [Simple Teams Status Light auf MakerWorld](https://makerworld.com/de/models/719141-simple-teams-status-light#profileId-649962).

## Features

- **Automatischer Teams-Sync:** Liest den Status direkt aus den lokalen Teams-Logdateien – keine Admin-Rechte oder Graph API erforderlich!
- **Manuelle Steuerung:** Den Status bei Bedarf per Klick übersteuern (Verfügbar, Beschäftigt, Abwesend).
- **Helligkeits-Slider:** Stufenlose Regelung der LED-Leuchtkraft direkt aus der Windows-App heraus.
- **Fail-Safe Timeout:** Der RP2040 Controller schaltet die LED-Matrix automatisch auf "Violett" (Offline / Not at desk), wenn die App geschlossen wird, der PC in den Standby geht oder keine Daten mehr empfangen werden.
- **System-Tray Integration:** Die App läuft unsichtbar im Hintergrund und blendet sich in der Taskleiste neben der Uhr ein (mit farbigem Status-Indikator).

## Projektstruktur

1. **`./` (Root)** - C# .NET 10 WPF Desktop-Applikation.  
2. **`RP2040_Firmware/`** - C++ Firmware-Projekt für PlatformIO.

## Installation & Setup

### 1. Hardware
- Ein **Waveshare RP2040-Zero** (oder ein kompatibler RP2040 / ESP Controller).
- Eine **WS2812 LED-Matrix** (z.B. 16 LEDs).
- Die Datenleitung (DIN) der LEDs muss mit **Pin 15** des RP2040 verbunden werden.

### 2. Mikrocontroller flashen
1. Installiere [Visual Studio Code](https://code.visualstudio.com/) und die Erweiterung [PlatformIO](https://platformio.org/).
2. Öffne den Ordner `RP2040_Firmware` in VS Code.
3. Schließe deinen RP2040 an und klicke in PlatformIO auf **Upload** (Pfeil nach rechts).

### 3. Desktop-App starten
1. Öffne die `TeamsStatus.csproj` in **Visual Studio** oder baue die App mit dem Befehl `dotnet build`.
2. Starte die Anwendung.
3. Wähle den entsprechenden **COM-Port** deines angesteckten RP2040-Zero in der App.
4. Klicke bei Bedarf auf **"Autostart installieren"**, damit die App beim nächsten PC-Start direkt im Hintergrund hochfährt.

## Datenprotokoll (Serial)

Die Desktop-App sendet alle 2 Sekunden folgenden String über die gewählte COM-Schnittstelle an den Controller:

`R,G,B,Helligkeit\n`

Beispiel: `50,205,50,255\n` (Grün bei maximaler Helligkeit).
Wird 6 Sekunden lang nichts empfangen, wechselt der RP2040 als Timeout-Schutz auf Violett.

## Lizenz
MIT License (oder deine eigene Lizenz eintragen).
