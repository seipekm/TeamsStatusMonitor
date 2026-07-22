#include <Arduino.h>
#include <Adafruit_NeoPixel.h>
#include <EEPROM.h>

#define FIRMWARE_VERSION "1.2.18"

// Konfiguration der LED-Matrix
#if defined(ARDUINO_ARCH_ESP32)
#define LED_PIN    1   // Data Pin für die WS2812 LEDs am XIAO ESP32S3
#else
#define LED_PIN    15   // Data Pin für die WS2812 LEDs am RP2040 / RP2350
#endif
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
    MODE_TIMEOUT,
    MODE_RINGING
};
Mode currentMode = MODE_SOLID;

uint8_t solidR = 0, solidG = 0, solidB = 0, globalBrightness = 128;

// Variablen für Animationen
unsigned long lastAnimTime = 0;
uint16_t rainbowHue = 0;

/**
 * Setzt die globale Helligkeit der LED-Matrix und speichert den Wert
 * dauerhaft im internen EEPROM (Flash), falls er sich geändert hat.
 * 
 * @param newBrightness Der neue Helligkeitswert (0-255)
 */
void setGlobalBrightness(uint8_t newBrightness) {
    if (globalBrightness != newBrightness) {
        globalBrightness = newBrightness;
        EEPROM.write(1, globalBrightness);
        EEPROM.commit();
    }
}

/**
 * Setzt alle LEDs der Matrix auf eine einheitliche Farbe (R,G,B) 
 * unter Berücksichtigung der globalen Helligkeit.
 * 
 * @param r Rot-Wert (0-255)
 * @param g Grün-Wert (0-255)
 * @param b Blau-Wert (0-255)
 * @param brightness Helligkeits-Skalierung (0-255)
 */
void setAllLeds(uint8_t r, uint8_t g, uint8_t b, uint8_t brightness) {
    strip.setBrightness(brightness);
    for (int i = 0; i < NUM_LEDS; i++) {
        strip.setPixelColor(i, strip.Color(r, g, b));
    }
    strip.show();
}

/**
 * Initialisierungs-Routine (wird einmalig beim Starten des Mikrocontrollers aufgerufen).
 * Konfiguriert die serielle Schnittstelle, lädt Einstellungen aus dem EEPROM,
 * initialisiert den LED-Strip und spielt eine kurze Startanimation ab.
 */
void setup() {
    // Serielle Verbindung starten (Baudrate muss mit C# App übereinstimmen)
    Serial.begin(9600);
#if defined(ARDUINO_ARCH_ESP32)
    Serial.setTxTimeoutMs(0); // Verhindert Aufhaengen bei USB Disconnect
#endif 

    // EEPROM initialisieren (512 Bytes reservieren im Flash)
    EEPROM.begin(512);
    // Lade gespeicherte Helligkeit
    // Byte 0: Magic Byte (0xAA) um zu prüfen ob jemals gespeichert wurde
    // Byte 1: Helligkeitswert
    if (EEPROM.read(0) == 0xAA) {
        globalBrightness = EEPROM.read(1);
    } else {
        // Initiale Speicherung falls noch nie gesetzt
        EEPROM.write(0, 0xAA);
        EEPROM.write(1, 128); // Standardhelligkeit
        EEPROM.commit();
        globalBrightness = 128;
    }

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

/**
 * Haupt-Schleife (wird fortlaufend in hoher Frequenz ausgeführt).
 * Liest serielle Befehle von der C#-Anwendung aus, aktualisiert den Betriebsmodus,
 * prüft auf Timeouts (Verbindungsabbruch) und rendert die aktiven LED-Animationen.
 */
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
                if (firstComma > 0) setGlobalBrightness(constrain(input.substring(firstComma + 1).toInt(), 0, 255));
            } 
            else if (input.startsWith("Rainbow")) {
                currentMode = MODE_RAINBOW;
                if (firstComma > 0) setGlobalBrightness(constrain(input.substring(firstComma + 1).toInt(), 0, 255));
            } 
            else if (input.startsWith("Fire")) {
                currentMode = MODE_FIRE;
                if (firstComma > 0) setGlobalBrightness(constrain(input.substring(firstComma + 1).toInt(), 0, 255));
            } 
            else if (input.startsWith("Ringing")) {
                currentMode = MODE_RINGING;
                if (firstComma > 0) setGlobalBrightness(constrain(input.substring(firstComma + 1).toInt(), 0, 255));
            } 
            else if (input.startsWith("VERSION")) {
#if defined(ARDUINO_ARCH_ESP32)
      Serial.println("VERSION:1.1,ARCH:ESP32");
#elif defined(ARDUINO_ARCH_RP2350)
      Serial.println("VERSION:1.1,ARCH:RP2350");
#else
      Serial.println("VERSION:1.1,ARCH:RP2040");
#endif
                Serial.println(FIRMWARE_VERSION);
            }
            else if (input.startsWith("UPDATE")) {
#if defined(ARDUINO_ARCH_RP2040)
                rp2040.rebootToBootloader();
#elif defined(ARDUINO_ARCH_RP2350)
                rp2040.rebootToBootloader(); // The earlephilhower core uses the same namespace or provides this for compatibility
#elif defined(ARDUINO_ARCH_ESP32)
                ESP.restart(); // ESP32 hat keinen direkten uf2-Bootloader per Software, aber restart hilft oft
#endif
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
                    uint8_t newBrightness = input.substring(comma3 + 1).toInt();

                    // Zur Sicherheit die Werte auf das Maximum 255 begrenzen
                    solidR = constrain(solidR, 0, 255);
                    solidG = constrain(solidG, 0, 255);
                    solidB = constrain(solidB, 0, 255);
                    setGlobalBrightness(constrain(newBrightness, 0, 255));

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
    else if (currentMode == MODE_TIMEOUT) {
        unsigned long now = millis();
        // Langsames Pulsieren (Sinus-Welle) für den Disconnected-Status
        // sin() gibt -1.0 bis 1.0 zurück.
        float sinVal = (sin(now / 600.0) + 1.0) / 2.0; // 0.0 bis 1.0, sehr langsam
        
        // Helligkeit von 10 (nicht ganz aus) bis 200 (nicht ganz hell)
        uint8_t breathBrightness = 10 + (uint8_t)(sinVal * globalBrightness);
        
        strip.setBrightness(breathBrightness);
        for(int i = 0; i < NUM_LEDS; i++) {
            // Zeige weiterhin das "Disconnected" Violett an
            strip.setPixelColor(i, strip.Color(138, 43, 226)); 
        }
        strip.show();
    }
    else if (currentMode == MODE_RINGING) {
        unsigned long now = millis();
        int cycle = now % 600; // 600ms cycle for fast blinking
        strip.setBrightness(globalBrightness);
        
        if (cycle < 300) {
            for (int i = 0; i < NUM_LEDS; i++) strip.setPixelColor(i, strip.Color(255, 0, 0)); // Red ON
        } else {
            strip.clear(); // Red OFF
        }
        strip.show();
    }
}
