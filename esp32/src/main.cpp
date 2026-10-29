#include <Arduino.h>
#include <WiFi.h>
#include <HTTPClient.h>

// Local credentials (gitignored). See include/config.example.h
#if __has_include("config.h")
#include "config.h"
#endif
#if __has_include("include/config.h")
#include "include/config.h"
#endif

// =====================================================
// WiFi / Server — injected via build config
// =====================================================
//
// IMPORTANT: never commit real credentials.
// Provide values via ONE of:
//   (a) esp32/include/config.h  (gitignored, see config.example.h), or
//   (b) PlatformIO build_flags  -D WIFI_SSID="..." etc (preferred for CI).
//
#ifndef WIFI_SSID
#error "WIFI_SSID must be defined in include/config.h or build_flags"
#endif

#ifndef WIFI_PASSWORD
#error "WIFI_PASSWORD must be defined in include/config.h or build_flags"
#endif

#ifndef SERVER_URL
#error "SERVER_URL must be defined in include/config.h or build_flags"
#endif

#ifndef SERVER_ENROLL_URL
#error "SERVER_ENROLL_URL must be defined in include/config.h or build_flags"
#endif

#ifndef WEBSOCKET_TOKEN
#error "WEBSOCKET_TOKEN must be defined in include/config.h or build_flags"
#endif

// Mode
// 0 = Identify
// 1 = Enroll
int mode = 0;
String enrollmentExternalUserId;

void sendCommand(uint8_t instruction);

#include "fingerprint.h"
#include "serial_command.h"
#include "server_connection.h"

// =====================================================
// SETUP
// =====================================================

void setup()
{

    Serial.begin(115200);
    Serial.setTimeout(1000);

    R503.begin(
        R503_BAUD,
        SERIAL_8N1,
        R503_RX,
        R503_TX);

    WiFi.begin(
        WIFI_SSID,
        WIFI_PASSWORD);

    Serial.print(
        "Menghubungkan WiFi");

    unsigned long wifiStart = millis();
    WiFi.setAutoReconnect(true);
    while (WiFi.status() != WL_CONNECTED &&
           millis() - wifiStart < 30000)
    {
        delay(500);

        Serial.print(".");
    }

    Serial.println();
    if (WiFi.status() == WL_CONNECTED)
    {
        Serial.println("WiFi terhubung.");
        Serial.print("IP ESP32: ");
        Serial.println(WiFi.localIP());
    }
    else
    {
        Serial.println("WiFi belum terhubung; akan mencoba kembali.");
    }

    webSocket.begin();
    webSocket.onEvent(webSocketEvent);

    Serial.println("WebSocket server aktif.");
    Serial.print("Alamat WebSocket: ws://");
    Serial.print(WiFi.localIP());
    Serial.println(":81");

    Serial.println();
    Serial.println("======================================");
    Serial.println(" ESP32 + R503 Fingerprint Test");
    Serial.println(" GetImage + UpImage");
    Serial.println("======================================");

    printSerialMenu();

    delay(1000);
}

// =====================================================
// LOOP
// =====================================================

void loop()
{
    static unsigned long lastWifiRetry = 0;
    if (WiFi.status() != WL_CONNECTED &&
        millis() - lastWifiRetry >= 10000)
    {
        lastWifiRetry = millis();
        WiFi.reconnect();
    }

    webSocket.loop();

    handleSerialCommand();

    Serial.print("Mode aktif: ");
    Serial.println(mode == 0 ? "IDENTIFICATION" : "ENROLLMENT");

    Serial.println();
    Serial.println("--------------------------------------");
    Serial.println("Tempelkan jari...");
    Serial.println("--------------------------------------");

    // -------------------------------------------------
    // 1. Ambil fingerprint ke buffer R503
    // -------------------------------------------------

    if (!getImage())
    {

        Serial.println(
            "Gagal mengambil fingerprint.");

        delay(2000);
        return;
    }

    Serial.println(
        ">>> Fingerprint image berhasil diambil!");

    delay(100);

    // -------------------------------------------------
    // 2. Upload image dari R503 ke ESP32
    // -------------------------------------------------

    if (uploadImage())
    {

        Serial.println();
        Serial.println("======================================");
        Serial.println(">>> UpImage BERHASIL <<<");
        Serial.println("======================================");

        Serial.print(
            "Ukuran PAYLOAD image: ");

        Serial.print(imageSize);

        Serial.println(" bytes");

        // Tampilkan 32 byte pertama
        Serial.println();
        Serial.println(
            "32 byte pertama image:");

        for (size_t i = 0; i < 32 && i < imageSize; i++)
        {

            if (imageBuffer[i] < 0x10)
            {
                Serial.print("0");
            }

            Serial.print(
                imageBuffer[i],
                HEX);

            Serial.print(" ");
        }

        Serial.println();
    }
    else
    {

        Serial.println();
        Serial.println(
            ">>> UpImage GAGAL <<<");
    }

    // -----------------------------------------
    // Upload ke Express gateway — hanya bila payload valid 18432 B
    // -----------------------------------------

    bool uploadOk = (imageSize == 18432);

    if (!uploadOk)
    {
        Serial.print(
            "Discard: imageSize != 18432 (actual=");
        Serial.print(imageSize);
        Serial.println(") — tidak upload.");
    }
    else if (uploadToServer(mode))
    {
        Serial.println(
            ">>> Upload ke server BERHASIL <<<");
    }
    else
    {
        Serial.println(
            ">>> Upload ke server GAGAL <<<");
    }

    delay(3000);
}
