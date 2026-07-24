# Teams Status Monitor - Projektstatus

## Aktuelle Versionen
- **Windows App**: v1.2.33
- **Firmware**: v1.2.20

## Status der Applikation
🟢 **Stabil**
- Die App verbindet sich lokal über die WebSocket API mit Microsoft Teams.
- Die COM-Port-Auswahl filtert intelligent nach passenden Geräten (RP2040, RP2350, ESP32) und zeigt die USB-Seriennummern an.
- Ein automatischer Firmware-Flasher für Raspberry Pi Picos (`.uf2`) ist integriert.
- Ein Debug-Log kann über die Info-Seite ("Log-Datei öffnen") eingesehen werden.

## Status der Firmware
🟢 **Stabil**
- **RP2040 (Raspberry Pi Pico):** Vollständig unterstützt (NeoPixel Pin 15/16).
- **RP2350 (Waveshare Zero):** Vollständig unterstützt (NeoPixel Pin 15/16).
- **ESP32 (XIAO ESP32S3):** Vollständig unterstützt, inkl. Bugfix für Native USB CDC Freeze bei PC-Disconnect.

## Bekannte Probleme / Beobachtungen (Backlog)
- **Status Freeze nach langen Calls:** Es wurde beobachtet, dass die App nach längeren Calls manchmal fälschlicherweise auf "Grün" (Verfügbar) springt, während der Teams-Status in der App grau bleibt und sich nicht mehr aktualisiert. 
  - *Vermutung:* Microsoft Teams beendet intern temporär die lokale WebSocket-API (evtl. aus Performance-Gründen), oder die Netzwerkverbindung reißt ab.
  - *Nächster Schritt:* Das in v1.2.27 eingeführte Logfile muss beim nächsten Auftreten geprüft werden, um den exakten Fehlertext für das Debugging zu haben. Da der WebSocket nun in v1.2.28 komplett entfernt wurde, könnte sich das Problem möglicherweise bereits erledigt haben.

## Nächste geplante Schritte
1. Auswertung der Log-Dateien zur Lösung des Verbindungsabbruchs bei langen Teams-Calls.
2. (Platzhalter für zukünftige Features)
