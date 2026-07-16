#include <Arduino.h>
#include <Adafruit_NeoPixel.h>

// Konfiguration der LED-Matrix
#define LED_PIN    15   // Data Pin für die WS2812 LEDs
#define NUM_LEDS   16   // Anzahl der LEDs (16 bit Matrix = 16 LEDs)

// Initialisierung der Adafruit NeoPixel Bibliothek
Adafruit_NeoPixel strip(NUM_LEDS, LED_PIN, NEO_GRB + NEO_KHZ800);

// Variablen für den Timeout (Violett-Status)
unsigned long lastReceiveTime = 0;
const unsigned long TIMEOUT_MS = 6000; // Nach 6 Sekunden ohne Signal wird auf Violett geschaltet

void setAllLeds(uint8_t r, uint8_t g, uint8_t b, uint8_t brightness) {
    strip.setBrightness(brightness);
    for (int i = 0; i < NUM_LEDS; i++) {
        strip.setPixelColor(i, strip.Color(r, g, b));
    }
    strip.show();
}

void setup() {
    // Serielle Verbindung starten (Baudrate muss mit C# App übereinstimmen)
    Serial.begin(9600); 

    // LEDs initialisieren
    strip.begin();
    strip.show(); // Alle LEDs initial ausschalten

    // Kurze Start-Animation (Weißes Blinken), damit man sieht, dass der Controller läuft
    setAllLeds(100, 100, 100, 50);
    delay(500);
    setAllLeds(0, 0, 0, 0);

    // Initialen Timer setzen
    lastReceiveTime = millis();
}

void loop() {
    // 1. Serielle Daten einlesen
    if (Serial.available()) {
        String input = Serial.readStringUntil('\n'); // Zeile bis zum Umbruch einlesen
        input.trim(); // Etwaige unsichtbare Leerzeichen oder "\r" entfernen

        if (input.length() > 0) {
            // Signal empfangen -> Timeout zurücksetzen
            lastReceiveTime = millis();

            // 2. Daten im Format "R,G,B,Brightness" auswerten
            int comma1 = input.indexOf(',');
            int comma2 = input.indexOf(',', comma1 + 1);
            int comma3 = input.indexOf(',', comma2 + 1);

            // Prüfen, ob wirklich 3 Kommas (also 4 Werte) vorhanden sind
            if (comma1 > 0 && comma2 > 0 && comma3 > 0) {
                int r = input.substring(0, comma1).toInt();
                int g = input.substring(comma1 + 1, comma2).toInt();
                int b = input.substring(comma2 + 1, comma3).toInt();
                int brightness = input.substring(comma3 + 1).toInt();

                // Zur Sicherheit die Werte auf das Maximum 255 begrenzen
                r = constrain(r, 0, 255);
                g = constrain(g, 0, 255);
                b = constrain(b, 0, 255);
                brightness = constrain(brightness, 0, 255);

                // LEDs setzen
                setAllLeds(r, g, b, brightness);
            }
        }
    }

    // 3. Timeout Check: Wenn z.B. der PC aus oder gesperrt ist (kein Signal mehr)
    if (millis() - lastReceiveTime > TIMEOUT_MS) {
        // Violett (Not at desk / Offline) bei ca. 50% Helligkeit
        // RGB für Violett z.B. 138, 43, 226
        setAllLeds(138, 43, 226, 128); 
        
        // Timeout-Wert sehr weit in die Zukunft setzen, 
        // damit er nicht jeden Frame unnötig neu zeichnet, aber bei Überlauf trotzdem funktioniert
        // (Wird beim nächsten validen Serial-Input sowieso wieder neu überschrieben)
        delay(500); // Entlastet den Controller
    }
}
