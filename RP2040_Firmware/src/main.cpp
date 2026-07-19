#include <Arduino.h>
#include <Adafruit_NeoPixel.h>

#define FIRMWARE_VERSION "1.2.7"

// Konfiguration der LED-Matrix
#define LED_PIN    15   // Data Pin für die WS2812 LEDs
#define NUM_LEDS   16   // Anzahl der LEDs (16 bit Matrix = 16 LEDs)

// Initialisierung der Adafruit NeoPixel Bibliothek
Adafruit_NeoPixel strip(NUM_LEDS, LED_PIN, NEO_GRB + NEO_KHZ800);

// Variablen für den Timeout (Violett-Status)
unsigned long lastReceiveTime = 0;
const unsigned long TIMEOUT_MS = 6000; // Nach 6 Sekunden ohne Signal wird auf Violett geschaltet

enum Mode {
    MODE_SOLID,
    MODE_BLAULICHT,
    MODE_RAINBOW,
    MODE_FIRE,
    MODE_TIMEOUT
};
Mode currentMode = MODE_SOLID;

uint8_t solidR = 0, solidG = 0, solidB = 0, globalBrightness = 128;

// Variablen für Animationen
unsigned long lastAnimTime = 0;
uint16_t rainbowHue = 0;

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

            int firstComma = input.indexOf(',');

            if (input.startsWith("Blaulicht")) {
                currentMode = MODE_BLAULICHT;
                if (firstComma > 0) globalBrightness = constrain(input.substring(firstComma + 1).toInt(), 0, 255);
            } 
            else if (input.startsWith("Rainbow")) {
                currentMode = MODE_RAINBOW;
                if (firstComma > 0) globalBrightness = constrain(input.substring(firstComma + 1).toInt(), 0, 255);
            } 
            else if (input.startsWith("Fire")) {
                currentMode = MODE_FIRE;
                if (firstComma > 0) globalBrightness = constrain(input.substring(firstComma + 1).toInt(), 0, 255);
            } 
            else if (input.startsWith("VERSION")) {
                Serial.print("VERSION:");
                Serial.println(FIRMWARE_VERSION);
            }
            else {
                // 2. Daten im Format "R,G,B,Brightness" auswerten
                int comma1 = input.indexOf(',');
                int comma2 = input.indexOf(',', comma1 + 1);
                int comma3 = input.indexOf(',', comma2 + 1);

                // Prüfen, ob wirklich 3 Kommas (also 4 Werte) vorhanden sind
                if (comma1 > 0 && comma2 > 0 && comma3 > 0) {
                    solidR = input.substring(0, comma1).toInt();
                    solidG = input.substring(comma1 + 1, comma2).toInt();
                    solidB = input.substring(comma2 + 1, comma3).toInt();
                    globalBrightness = input.substring(comma3 + 1).toInt();

                    // Zur Sicherheit die Werte auf das Maximum 255 begrenzen
                    solidR = constrain(solidR, 0, 255);
                    solidG = constrain(solidG, 0, 255);
                    solidB = constrain(solidB, 0, 255);
                    globalBrightness = constrain(globalBrightness, 0, 255);

                    currentMode = MODE_SOLID;
                    setAllLeds(solidR, solidG, solidB, globalBrightness);
                }
            }
        }
    }

    // 3. Timeout Check: Wenn z.B. der PC aus oder gesperrt ist (kein Signal mehr)
    if (millis() - lastReceiveTime > TIMEOUT_MS) {
        if (currentMode != MODE_TIMEOUT) {
            currentMode = MODE_TIMEOUT;
            setAllLeds(138, 43, 226, 255); // Violett
        }
    }

    // 4. Animationen ausführen (ohne delay!)
    if (currentMode == MODE_BLAULICHT) {
        unsigned long now = millis();
        int cycle = now % 500; // 500ms Zyklus
        strip.setBrightness(globalBrightness);
        strip.clear();
        
        // Doppelblitz: AN (0-40), AUS (40-80), AN (80-120), AUS (120-200)
        // Dann andere Seite: AN (200-240), AUS (240-280), AN (280-320), AUS (320-500)
        bool leftOn = (cycle >= 0 && cycle < 40) || (cycle >= 80 && cycle < 120);
        bool rightOn = (cycle >= 200 && cycle < 240) || (cycle >= 280 && cycle < 320);

        if (leftOn) {
            for (int i = 0; i < NUM_LEDS / 2; i++) strip.setPixelColor(i, strip.Color(0, 0, 255));
        }
        if (rightOn) {
            for (int i = NUM_LEDS / 2; i < NUM_LEDS; i++) strip.setPixelColor(i, strip.Color(0, 0, 255));
        }
        strip.show();
    } 
    else if (currentMode == MODE_RAINBOW) {
        unsigned long now = millis();
        // Langsame Farbverschiebung
        rainbowHue = (now * 20) % 65536; 
        strip.setBrightness(globalBrightness);
        
        for(int i = 0; i < NUM_LEDS; i++) {
            // Farbkreis über alle LEDs verteilen
            int pixelHue = rainbowHue + (i * 65536L / NUM_LEDS);
            strip.setPixelColor(i, strip.gamma32(strip.ColorHSV(pixelHue)));
        }
        strip.show();
    }
    else if (currentMode == MODE_FIRE) {
        unsigned long now = millis();
        if (now - lastAnimTime > 50) { // Update alle 50ms
            lastAnimTime = now;
            strip.setBrightness(globalBrightness);
            for(int i = 0; i < NUM_LEDS; i++) {
                int r = 255;
                int g = random(30, 120); // Orange bis leichtes Gelb
                int b = 0;
                
                // Flackern
                int flicker = random(0, 100);
                r = constrain(r - flicker, 0, 255);
                g = constrain(g - flicker, 0, 255);
                
                // Manche LEDs auch mal ganz aus für echten Feuer-Effekt
                if (random(0, 10) > 8) {
                    r = r / 3;
                    g = g / 3;
                }
                
                strip.setPixelColor(i, strip.Color(r, g, b));
            }
            strip.show();
        }
    }
}
