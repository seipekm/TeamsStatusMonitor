## Version 1.2.14 (App)
*Ver�ffentlicht: 19. Juli 2026*

### Bugfixes ??
* **Log-Spam behoben:** Die WebSocket API-Abfrage pr�ft nun vorher, ob Microsoft Teams �berhaupt gestartet ist. Falls nicht, wird im Hintergrund stillschweigend gewartet, anstatt min�tlich eine Fehlermeldung ins App-Log zu schreiben.

---

# Release Notes

## Version 1.2.13 (App) & 1.2.11 (Firmware)
*Veröffentlicht: 19. Juli 2026*

### Neue Features ✨
* **"Klingel"-Erkennung (Incoming Call):** Wenn du in Teams angerufen wirst, wechseln die LEDs an der Matrix nun sofort in einen auffälligen Alarmzustand (schnelles rotes Blinken: `MODE_RINGING`), sodass du keinen Anruf mehr verpasst.
* **Hybrid-Ansatz (WebSocket + Log-Scanning):** 
  * Das Programm integriert ab sofort die offizielle, lokale Microsoft Teams Third-Party WebSocket API (über Port 8124).
  * Sobald du dich in einem Call befindest (und das Meeting betreten hast), wird der `InCall` / `Muted` Status über die blitzschnelle WebSocket API verarbeitet.
  * Für den Zeitraum davor (während es klingelt) wertet die App weiterhin blitzschnell im Hintergrund das Teams Logfile aus und erkennt das `reportIncomingCall` Event in Echtzeit.
* **Neues System Tray Verhalten:**
  * Das Taskleisten-Symbol der Windows-App färbt sich während eines eingehenden Anrufs **Pink** 🔴 und wechselt das Symbol zu einem kleinen Hörer.

### Verbesserungen & Bugfixes 🐛
* Das serielle Kommunikationsprotokoll zwischen der C#-Anwendung und dem Mikrocontroller wurde robuster gemacht, um den neuen Status `Ringing, Helligkeit` fehlerfrei an die WS2812 LEDs weiterzugeben.
* Die interne Log-Ausgabe innerhalb der App wurde dahingehend verbessert, dass Entwickler und Nutzer exakt den Klartext des verschickten seriellen Strings (z.B. `Ringing,128`) sehen anstatt eines abgekürzten Status-Buchstabens.

### Wichtiger Hinweis für dieses Update ⚠️
Da diese Version eine komplett neue LED-Animation (das rote Anruf-Blinken) einführt, **muss die Hardware-Firmware zwingend auf Version 1.2.11 aktualisiert werden**, damit das Feature funktioniert. Nutze dafür einfach die Firmware-Update Funktion innerhalb der App! Ebenso wirst du beim ersten Start der App nach dem Update von Teams einmalig um die Erlaubnis gebeten, dass die Drittanbieter-Schnittstelle zugreifen darf ("Zulassen").

