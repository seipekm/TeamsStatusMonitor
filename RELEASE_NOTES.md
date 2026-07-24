# Release Notes

## Version 1.2.34 (App)
*Veröffentlicht: 24. Juli 2026*

### Bugfixes 🐛
* **ESP32-S3 Freeze bei "Trennen":** Das Klick auf "Trennen" in der App führte bei ESP32-S3 Boards (mit nativem USB) dazu, dass sich das Gerät komplett aufgehängt hat. Ursache war, dass die C# App beim Schließen des Ports die DTR/RTS-Signale auf Low gezogen hat. Der ESP32-S3 interpretiert diesen Spannungsabfall hardwareseitig als Reset-Befehl in den ROM-Bootloader. Das DTR/RTS Flag wird nun beim normalen Verbindungsaufbau nicht mehr gesetzt, weswegen es beim Trennen auch keinen Reset mehr auslöst.

---

## Version 1.2.33 (App)
*Veröffentlicht: 24. Juli 2026*

### Bugfixes 🐛
* **ESP32-S3 esptool Fatal Error:** Ein Versuch, den ESP32-S3 über das `--after soft_reset` Argument nach dem Flashen per Software neu zu starten, führte in Version 1.2.32 zu einem Fatal Error (da der ESP32-S3 Stub diese Methode nicht unterstützt). Dies wurde rückgängig gemacht.
Stattdessen verwendet `esptool` wieder den Standard-Neustart, welcher intern den Chip zurücksetzt. Das Problem der Verbindungsabbrüche wurde behoben, indem die App nun **4 Sekunden** wartet, bevor sie versucht sich neu zu verbinden. Dadurch hat Windows genug Zeit, den Native-USB COM-Port nach dem ESP-Reboot neu zu enumerieren. Das Flashen läuft nun fehlerfrei und ohne "Strom-Ziehen" ab!

---

## Version 1.2.32 (App)
*Veröffentlicht: 24. Juli 2026*

### Features ✨
* **ESP32-S3 Auto-Reboot Fix:** Das Firmware-Update für ESP32-S3 Boards (wie XIAO ESP32-S3) wurde verbessert. `esptool` verwendet nun den `--after soft_reset` Parameter. Dadurch sendet der ROM-Bootloader nach erfolgreichem Schreiben einen Software-Neustart an den Mikrocontroller. Der nervige manuelle Schritt "USB-Kabel trennen" entfällt dadurch komplett!

---
## Version 1.2.31 (App)
*Veröffentlicht: 24. Juli 2026*

### Features ✨
* **ESP32-S3 Firmware Update:** Da ESP32-S3 Boards mit nativ angebundenem USB (wie z.B. der XIAO ESP32-S3) nach dem Firmware-Flash via `esptool` keinen automatischen Hardware-Reset durchführen können (aufgrund fehlender CP210x Logikschaltung), bleibt das Board im ROM-Bootloader hängen. Die App zeigt nun nach erfolgreichem ESP32-Update ein Dialogfenster an, das den Benutzer auffordert, das USB-Kabel kurz zu ziehen und wieder einzustecken (Stromversorgung trennen), bevor sich die App wiederverbindet. (Dieser Schritt wurde in Version 1.2.32 durch einen `soft_reset` automatisiert!)

---
## Version 1.2.30 (App)
*Veröffentlicht: 24. Juli 2026*

### Bugfixes 🐛
* **Firmware Update Erkennung:** Es wurde ein Fehler behoben, bei dem die App die Firmware-Version alter Versionen (`< 1.2.20`) falsch einlas und dadurch bei jedem Start fälschlicherweise vorschlug, ein Firmware-Update durchzuführen.

---
## Version 1.2.20 (Firmware)
*Veröffentlicht: 24. Juli 2026*

### Bugfixes 🐛
* **Versions-Ausgabe (Format):** Die Ausgabe der Firmware-Version über die serielle Schnittstelle wurde überarbeitet (`VERSION:<ARCH>:<VERSION>`), damit die Windows-App die Informationen in einer einzigen Textzeile robuster einlesen kann.

---
## Version 1.2.19 (Firmware)
*Veröffentlicht: 24. Juli 2026*

### Features ✨
* **ESP32-S3 USB Identifier:** Es wurde die Möglichkeit in der `platformio.ini` hinzugefügt, bei der ESP32-S3 Firmware eigene `USB_PID` und `USB_PRODUCT` Strings für die native USB CDC Verbindung zu definieren, um den Mikrocontroller im Windows Gerätemanager eindeutig als "Teams Status Monitor" (und mit passender PID 0x1234) zu identifizieren.

---
## Version 1.2.29 (App)
*Veröffentlicht: 24. Juli 2026*

### Bugfixes 🛠️
* **Auto-Modus Fix:** Es wurde ein Problem behoben, bei dem die App nicht mehr selbstständig den Status aktualisierte, wenn man von einem manuellen Effekt (wie "Rainbow" oder "Blaulicht") zurück in den Auto-Modus gewechselt hat. Der lokale Status-Speicher wird nun korrekt zurückgesetzt.

---

## Version 1.2.28 (App)
*Veröffentlicht: 24. Juli 2026*

### Änderungen 📝
* **WebSocket Entfernung:** Die Microsoft Teams WebSocket-Anbindung (`TeamsWebSocketService`) wurde vollständig aus der App entfernt. Die App liest den Teams-Status und eingehende Anrufe nun ressourcenschonend und absturzsicher ausschließlich über den lokalen Teams-Log-Scanner aus. Dies behebt potentielle Probleme mit blockierenden WebSockets (eingefrorener "Grau"-Status im UI).

---

## Version 1.2.27 (App)
*Veröffentlicht: 24. Juli 2026*

### Features ✨
* **Debug-Log Button:** Im Info-Fenster wurde ein Button "Log-Datei öffnen (Fehlersuche)" hinzugefügt, um Endanwendern den direkten Zugriff auf die interne Logdatei zu erleichtern, wodurch Fehler im Log-Scanner schneller analysiert werden können.

---
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

