# Release Notes

## Version 1.2.19 (App) & 1.2.14 (Firmware)
*Veröffentlicht: 20. Juli 2026*

### Änderungen 📝
* **Dokumentation:** Umfangreiche Entwickler-Kommentare (XML-Docs) im C#-Code (MainWindow, WebSocketService) und C++-Code (Arduino main.cpp) hinzugefügt, um die Wartbarkeit und das Code-Verständnis zu verbessern.

---

## Version 1.2.18 (App)
*Veröffentlicht: 20. Juli 2026*

### Bugfixes 🛠️
* **TitleBar / Fenster-Buttons gefixt:** Es wurde ein WPF-Bug behoben, bei dem die Update-Benachrichtigung nach einem Neustart zu schnell geladen wurde. Dies blockierte die Initialisierung der Fensterleiste, wodurch das Fenster nicht mehr über das "X" oder "Minimieren" bedient werden konnte.

---

## Version 1.2.17 (App)
*Veröffentlicht: 20. Juli 2026*

### Neue Features ✨
* **Automatische Update-Prüfung:** Die App prüft nun im Hintergrund stündlich auf neue Updates. Falls ein neues Update verfügbar ist, erscheint ein Benachrichtigungs-Dialog, ohne dass die App dafür neu gestartet werden muss.

---

## Version 1.2.16 (App)
*Veröffentlicht: 20. Juli 2026*

### Bugfixes 🛠️
* **Fenster Schließen-Button gefixt:** Es wurde ein Fehler behoben, bei dem die App nicht in den Tray minimiert wurde (bzw. sich nicht verstecken ließ), wenn man nach dem letzten App-Update in der Fensterleiste auf das "X" geklickt hat.

---

## Version 1.2.13 (Firmware)
*Veröffentlicht: 20. Juli 2026*

### Neue Features ✨
* **Helligkeits-Speicherung (EEPROM):** Die eingestellte Helligkeit wird nun im internen Flash-Speicher des RP2040 gesichert. Wird der Controller stromlos gemacht und neu gestartet, leuchtet er sofort wieder mit der zuletzt eingestellten Helligkeit, ohne dass die C#-App die Helligkeit erneut senden muss.

---

## Version 1.2.12 (Firmware)
*Veröffentlicht: 20. Juli 2026*

### Verbesserungen & Bugfixes 🛠️
* **Timeout/Violett-Animation:** Die pulsierende Timeout-Animation skaliert nun korrekt mit der in der App eingestellten allgemeinen Helligkeit (Slider) anstatt immer auf voller Helligkeit zu pulsieren.

---

## Version 1.2.15 (App)
*Veröffentlicht: 20. Juli 2026*

### Verbesserungen & Bugfixes 🛠️
* **Präsenz-Erkennung verbessert:** Der globale Status ("Verfügbar", "Abwesend", "Beschäftigt") wird nun wieder ausschließlich und extrem zuverlässig aus dem lokalen Teams-Log (`availability`) ausgelesen, da der Teams WebSocket keine Präsenzinformationen bereitstellt.
* **Ringing-Verhalten gefixt:** Es konnte passieren, dass die Lampe beim Annehmen eines Anrufs rot geblinkt hat, anstatt dauerhaft Rot ("In a Call") zu leuchten. Dies ist behoben.
* **Verzögerung beim Auflegen gefixt:** Wenn du auflegst, schaltet die Lampe jetzt sofort um und hängt nicht mehr für mehrere Sekunden im "Beschäftigt"-Zustand fest.

---

## Version 1.2.14 (App)
*Veröffentlicht: 19. Juli 2026*

### Bugfixes ??
* **Log-Spam behoben:** Die WebSocket API-Abfrage prüft nun vorher, ob Microsoft Teams überhaupt gestartet ist. Falls nicht, wird im Hintergrund stillschweigend gewartet, anstatt minütlich eine Fehlermeldung ins App-Log zu schreiben.

---

# Release Notes

## Version 1.2.13 (App) & 1.2.11 (Firmware)
*VerÃ¶ffentlicht: 19. Juli 2026*

### Neue Features âœ¨
* **"Klingel"-Erkennung (Incoming Call):** Wenn du in Teams angerufen wirst, wechseln die LEDs an der Matrix nun sofort in einen auffÃ¤lligen Alarmzustand (schnelles rotes Blinken: `MODE_RINGING`), sodass du keinen Anruf mehr verpasst.
* **Hybrid-Ansatz (WebSocket + Log-Scanning):** 
  * Das Programm integriert ab sofort die offizielle, lokale Microsoft Teams Third-Party WebSocket API (Ã¼ber Port 8124).
  * Sobald du dich in einem Call befindest (und das Meeting betreten hast), wird der `InCall` / `Muted` Status Ã¼ber die blitzschnelle WebSocket API verarbeitet.
  * FÃ¼r den Zeitraum davor (wÃ¤hrend es klingelt) wertet die App weiterhin blitzschnell im Hintergrund das Teams Logfile aus und erkennt das `reportIncomingCall` Event in Echtzeit.
* **Neues System Tray Verhalten:**
  * Das Taskleisten-Symbol der Windows-App fÃ¤rbt sich wÃ¤hrend eines eingehenden Anrufs **Pink** ðŸ”´ und wechselt das Symbol zu einem kleinen HÃ¶rer.

### Verbesserungen & Bugfixes ðŸ�›
* Das serielle Kommunikationsprotokoll zwischen der C#-Anwendung und dem Mikrocontroller wurde robuster gemacht, um den neuen Status `Ringing, Helligkeit` fehlerfrei an die WS2812 LEDs weiterzugeben.
* Die interne Log-Ausgabe innerhalb der App wurde dahingehend verbessert, dass Entwickler und Nutzer exakt den Klartext des verschickten seriellen Strings (z.B. `Ringing,128`) sehen anstatt eines abgekÃ¼rzten Status-Buchstabens.

### Wichtiger Hinweis fÃ¼r dieses Update âš ï¸�
Da diese Version eine komplett neue LED-Animation (das rote Anruf-Blinken) einfÃ¼hrt, **muss die Hardware-Firmware zwingend auf Version 1.2.11 aktualisiert werden**, damit das Feature funktioniert. Nutze dafÃ¼r einfach die Firmware-Update Funktion innerhalb der App! Ebenso wirst du beim ersten Start der App nach dem Update von Teams einmalig um die Erlaubnis gebeten, dass die Drittanbieter-Schnittstelle zugreifen darf ("Zulassen").

