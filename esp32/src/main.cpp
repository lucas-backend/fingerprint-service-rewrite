#include <Arduino.h>
#include <WiFi.h>
#include <HTTPClient.h>
#include <WebSocketsServer.h>

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
// Placeholder values below intentionally do NOT connect — replace locally.

#ifndef WIFI_SSID
#define WIFI_SSID "Jasa Website Dipa"
#endif

#ifndef WIFI_PASSWORD
#define WIFI_PASSWORD "ayambakar123"
#endif

#ifndef SERVER_URL
// Express gateway — MUST be reachable from ESP32's WiFi LAN.
// localhost does NOT work from ESP32 (it would mean the ESP32 itself).
// Use your PC's LAN IP (e.g. http://192.168.1.7:3000/...).
#define SERVER_URL "http://192.168.1.7:3000/api/fingerprint/identify"
#endif

#ifndef SERVER_ENROLL_URL
#define SERVER_ENROLL_URL "http://192.168.1.7:3000/api/fingerprint/enroll/raw"
#endif

// =====================================================
// Websockets
// =====================================================
WebSocketsServer webSocket(81);

// =====================================================
// R503 UART
// =====================================================

HardwareSerial R503(2);

#define R503_RX 16 // ESP32 RX <- TX R503
#define R503_TX 17 // ESP32 TX -> RX R503
#define R503_BAUD 57600

// =====================================================
// R503 Packet Type
// =====================================================

#define R503_PACKET_COMMAND 0x01
#define R503_PACKET_DATA 0x02
#define R503_PACKET_ACK 0x07
#define R503_PACKET_END 0x08

// =====================================================
// R503 Commands
// =====================================================

#define R503_CMD_GET_IMAGE 0x01
#define R503_CMD_UP_IMAGE 0x0A

// =====================================================
// Image Buffer
// =====================================================

// 32 KB cukup untuk pengujian awal.
// Kita hanya menyimpan PAYLOAD image,
// bukan header/checksum packet.
#define IMAGE_BUFFER_SIZE 32768

uint8_t imageBuffer[IMAGE_BUFFER_SIZE];
size_t imageSize = 0;

// =====================================================
// Function declarations
// =====================================================

void sendCommand(uint8_t instruction);

bool getImage();

bool uploadImage();

bool readByteTimeout(
    uint8_t &value,
    unsigned long timeoutMs);

bool readR503Packet(
    uint8_t &packetType,
    uint8_t *payload,
    size_t payloadBufferSize,
    size_t &payloadLength);

bool uploadToServer(int mode);
void handleSerialCommand();
void printSerialMenu();
bool isValidExternalUserId(const String &value);
bool setEnrollmentUserId(String value);

// Mode
// 0 = Identify
// 1 = Enroll
int mode = 0;
String enrollmentExternalUserId = "1";

void sendDeviceStatus(uint8_t clientId);
void webSocketEvent(
    uint8_t clientId,
    WStype_t type,
    uint8_t *payload,
    size_t length);

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

    while (
        WiFi.status() != WL_CONNECTED)
    {
        delay(500);

        Serial.print(".");
    }

    Serial.println();

    Serial.println(
        "WiFi terhubung.");

    Serial.print(
        "IP ESP32: ");

    Serial.println(
        WiFi.localIP());

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

// =====================================================
// SERIAL COMMANDS
// =====================================================

void handleSerialCommand()
{
    if (!Serial.available())
    {
        return;
    }

    String command = Serial.readStringUntil('\n');
    command.trim();
    String normalizedCommand = command;
    normalizedCommand.toLowerCase();

    if (
        normalizedCommand == "i" ||
        normalizedCommand == "identify" ||
        normalizedCommand == "identification" ||
        normalizedCommand == "cancel")
    {
        mode = 0;
        Serial.println();
        Serial.println("Mode IDENTIFICATION aktif.");
        Serial.println("Tempelkan jari untuk mencari kecocokan.");
    }
    else if (normalizedCommand == "menu" || normalizedCommand == "help")
    {
        printSerialMenu();
    }
    else if (
        normalizedCommand == "e" ||
        normalizedCommand == "enroll" ||
        normalizedCommand == "enrollment")
    {
        Serial.println();
        Serial.println("=== PENDAFTARAN FINGERPRINT ===");
        Serial.println("Masukkan externalUserId lalu tekan Enter.");
        Serial.println("Contoh: 123 atau EMP-001");

        Serial.setTimeout(30000);
        String externalUserId = Serial.readStringUntil('\n');
        Serial.setTimeout(1000);
        externalUserId.trim();

        if (!setEnrollmentUserId(externalUserId))
        {
            Serial.println(
                "Enrollment dibatalkan: externalUserId tidak valid.");
            return;
        }

        mode = 1;
        Serial.println();
        Serial.print("ID pengguna: ");
        Serial.println(enrollmentExternalUserId);
        Serial.println("Mode ENROLLMENT aktif.");
        Serial.println("Tempelkan jari untuk mendaftarkan fingerprint.");
        Serial.println("Ketik cancel untuk kembali ke identification.");
    }
    else if (normalizedCommand.startsWith("enroll "))
    {
        String externalUserId = command.substring(7);
        externalUserId.trim();

        if (!setEnrollmentUserId(externalUserId))
        {
            Serial.println(
                "Enrollment dibatalkan: externalUserId tidak valid.");
            return;
        }

        mode = 1;
        Serial.println();
        Serial.print("Enrollment dimulai untuk ID: ");
        Serial.println(enrollmentExternalUserId);
        Serial.println("Tempelkan jari sekarang.");
    }
    else if (normalizedCommand == "status")
    {
        Serial.println();
        Serial.println("=== STATUS DEVICE ===");
        Serial.print("Mode: ");
        Serial.println(mode == 0 ? "IDENTIFICATION" : "ENROLLMENT");
        Serial.print("Enrollment ID: ");
        Serial.println(enrollmentExternalUserId);
        Serial.print("WiFi: ");
        Serial.println(
            WiFi.status() == WL_CONNECTED ? "TERHUBUNG" : "TERPUTUS");
        Serial.print("IP: ");
        Serial.println(WiFi.localIP());
    }
    else
    {
        Serial.println("Perintah tidak dikenal. Ketik menu untuk bantuan.");
    }
}

void printSerialMenu()
{
    Serial.println();
    Serial.println("======================================");
    Serial.println("        MENU ADMIN SERIAL MONITOR");
    Serial.println("======================================");
    Serial.println("i / identify       - Mode identification");
    Serial.println("e / enroll         - Enrollment interaktif");
    Serial.println("enroll <id>        - Enrollment langsung");
    Serial.println("status             - Lihat status device");
    Serial.println("cancel             - Batalkan enrollment");
    Serial.println("menu / help        - Tampilkan menu ini");
    Serial.println("--------------------------------------");
    Serial.println("Default: IDENTIFICATION");
    Serial.println("Baud rate: 115200, line ending: Newline");
    Serial.println("======================================");
}

bool isValidExternalUserId(const String &value)
{
    if (value.length() == 0 || value.length() > 64)
    {
        return false;
    }

    for (size_t i = 0; i < value.length(); i++)
    {
        char character = value.charAt(i);

        if (
            !(
                (character >= 'a' && character <= 'z') ||
                (character >= 'A' && character <= 'Z') ||
                (character >= '0' && character <= '9') ||
                character == '-' ||
                character == '_' ||
                character == '.'))
        {
            return false;
        }
    }

    return true;
}

bool setEnrollmentUserId(String value)
{
    value.trim();

    if (!isValidExternalUserId(value))
    {
        return false;
    }

    enrollmentExternalUserId = value;
    return true;
}

// =====================================================
// SEND COMMAND
// =====================================================

void sendCommand(uint8_t instruction)
{

    uint8_t packet[12];

    // PID = command
    uint16_t packetLength = 3;

    // Header
    packet[0] = 0xEF;
    packet[1] = 0x01;

    // Address = 0xFFFFFFFF
    packet[2] = 0xFF;
    packet[3] = 0xFF;
    packet[4] = 0xFF;
    packet[5] = 0xFF;

    // Packet identifier
    packet[6] = R503_PACKET_COMMAND;

    // Length
    packet[7] =
        (packetLength >> 8) & 0xFF;

    packet[8] =
        packetLength & 0xFF;

    // Instruction
    packet[9] = instruction;

    // Checksum
    uint16_t checksum =
        packet[6] +
        packet[7] +
        packet[8] +
        packet[9];

    packet[10] =
        (checksum >> 8) & 0xFF;

    packet[11] =
        checksum & 0xFF;

    // Kirim packet
    R503.write(
        packet,
        sizeof(packet));

    R503.flush();
}

// =====================================================
// GET IMAGE
// =====================================================

bool getImage()
{

    // Bersihkan buffer UART
    while (R503.available())
    {
        R503.read();
    }

    // Kirim GetImage
    sendCommand(
        R503_CMD_GET_IMAGE);

    uint8_t packet[32];
    size_t length = 0;

    unsigned long timeout =
        millis() + 1000;

    // -------------------------------------------------
    // Cari header EF 01
    // -------------------------------------------------

    while (millis() < timeout)
    {

        if (R503.available())
        {

            uint8_t b = R503.read();

            if (length == 0)
            {

                if (b == 0xEF)
                {
                    packet[length++] = b;
                }
            }
            else if (length == 1)
            {

                if (b == 0x01)
                {

                    packet[length++] = b;
                    break;
                }
                else
                {

                    length = 0;
                }
            }
        }
    }

    if (length != 2)
    {

        Serial.println(
            "GET_IMAGE: header timeout");

        return false;
    }

    // -------------------------------------------------
    // Baca sampai PID + length
    // -------------------------------------------------

    while (
        length < 9 &&
        millis() < timeout)
    {

        if (R503.available())
        {

            packet[length++] =
                R503.read();
        }
    }

    if (length < 9)
    {

        Serial.println(
            "GET_IMAGE: packet tidak lengkap");

        return false;
    }

    // -------------------------------------------------
    // Hitung total packet
    // -------------------------------------------------

    uint16_t dataLength =
        ((uint16_t)packet[7] << 8) |
        packet[8];

    size_t totalLength =
        9 + dataLength;

    if (totalLength > sizeof(packet))
    {

        Serial.println(
            "GET_IMAGE: packet terlalu besar");

        return false;
    }

    // -------------------------------------------------
    // Baca sisa packet
    // -------------------------------------------------

    while (
        length < totalLength &&
        millis() < timeout)
    {

        if (R503.available())
        {

            packet[length++] =
                R503.read();
        }
    }

    if (length != totalLength)
    {

        Serial.println(
            "GET_IMAGE: packet timeout");

        return false;
    }

    // -------------------------------------------------
    // Confirmation code
    // -------------------------------------------------

    uint8_t confirmationCode =
        packet[9];

    if (confirmationCode == 0x00)
    {

        return true;
    }

    Serial.print(
        "R503 response error: 0x");

    if (confirmationCode < 0x10)
    {
        Serial.print("0");
    }

    Serial.println(
        confirmationCode,
        HEX);

    if (confirmationCode == 0x02)
    {

        Serial.println(
            "-> Jari tidak terdeteksi.");
    }

    return false;
}

// =====================================================
// READ BYTE WITH TIMEOUT
// =====================================================

bool readByteTimeout(
    uint8_t &value,
    unsigned long timeoutMs)
{

    unsigned long start =
        millis();

    while (
        millis() - start <
        timeoutMs)
    {

        if (R503.available())
        {

            value = R503.read();

            return true;
        }

        delay(1);
    }

    return false;
}

// =====================================================
// READ R503 PACKET
// =====================================================
//
// Struktur:
//
// EF 01
// ADDRESS 4 byte
// PID 1 byte
// LENGTH 2 byte
// PAYLOAD
// CHECKSUM 2 byte
//
// LENGTH = payload + checksum
//
// Jadi:
//
// payloadLength = length - 2
//
// =====================================================

bool readR503Packet(
    uint8_t &packetType,
    uint8_t *payload,
    size_t payloadBufferSize,
    size_t &payloadLength)
{

    payloadLength = 0;

    uint8_t b;

    // -------------------------------------------------
    // Cari header EF 01
    // -------------------------------------------------

    bool foundHeader = false;

    while (true)
    {

        if (!readByteTimeout(b, 2000))
        {

            return false;
        }

        if (b != 0xEF)
        {

            continue;
        }

        if (!readByteTimeout(b, 100))
        {

            return false;
        }

        if (b == 0x01)
        {

            foundHeader = true;
            break;
        }
    }

    if (!foundHeader)
    {
        return false;
    }

    // -------------------------------------------------
    // Address 4 byte
    // -------------------------------------------------

    uint8_t address[4];

    for (int i = 0; i < 4; i++)
    {

        if (!readByteTimeout(
                address[i],
                1000))
        {

            return false;
        }
    }

    // -------------------------------------------------
    // Packet identifier
    // -------------------------------------------------

    if (!readByteTimeout(
            packetType,
            1000))
    {

        return false;
    }

    // -------------------------------------------------
    // Length
    // -------------------------------------------------

    uint8_t lengthHigh;
    uint8_t lengthLow;

    if (!readByteTimeout(
            lengthHigh,
            1000))
    {

        return false;
    }

    if (!readByteTimeout(
            lengthLow,
            1000))
    {

        return false;
    }

    uint16_t packetLength =
        ((uint16_t)lengthHigh << 8) |
        lengthLow;

    // -------------------------------------------------
    // Minimal packet length
    //
    // Harus mempunyai checksum 2 byte.
    // -------------------------------------------------

    if (packetLength < 2)
    {

        Serial.println(
            "Packet length tidak valid.");

        return false;
    }

    payloadLength =
        packetLength - 2;

    // -------------------------------------------------
    // Pastikan buffer cukup
    // -------------------------------------------------

    if (
        payloadLength >
        payloadBufferSize)
    {

        Serial.println(
            "Payload packet terlalu besar.");

        return false;
    }

    // -------------------------------------------------
    // Baca PAYLOAD
    // -------------------------------------------------

    for (
        size_t i = 0;
        i < payloadLength;
        i++)
    {

        if (!readByteTimeout(
                payload[i],
                3000))
        {

            Serial.println(
                "Timeout membaca payload.");

            return false;
        }
    }

    // -------------------------------------------------
    // Baca checksum 2 byte
    // -------------------------------------------------

    uint8_t checksumHigh;
    uint8_t checksumLow;

    if (!readByteTimeout(
            checksumHigh,
            1000))
    {

        return false;
    }

    if (!readByteTimeout(
            checksumLow,
            1000))
    {

        return false;
    }

    // Validasi checksum: sum(PID + LENGTH bytes + PAYLOAD) & 0xFFFF
    uint16_t computed = 0;
    computed += packetType;
    computed += lengthHigh;
    computed += lengthLow;
    for (size_t i = 0; i < payloadLength; i++)
    {
        computed += payload[i];
    }
    uint16_t received =
        ((uint16_t)checksumHigh << 8) | checksumLow;

    if (computed != received)
    {
        Serial.print("Checksum mismatch: computed 0x");
        Serial.print(computed, HEX);
        Serial.print(" received 0x");
        Serial.println(received, HEX);
        return false;
    }

    return true;
}

// =====================================================
// UP IMAGE
// =====================================================

bool uploadImage()
{

    imageSize = 0;

    // Bersihkan UART
    while (R503.available())
    {
        R503.read();
    }

    Serial.println();
    Serial.println(
        "Meminta image dari R503...");

    // -------------------------------------------------
    // Kirim UpImage
    // -------------------------------------------------

    sendCommand(
        R503_CMD_UP_IMAGE);

    bool gotAck = false;
    bool gotEnd = false;

    unsigned long startTime =
        millis();

    // Maksimal 15 detik
    const unsigned long
        MAX_TRANSFER_TIME = 15000;

    // -------------------------------------------------
    // Terima semua packet
    // -------------------------------------------------

    while (
        millis() - startTime <
        MAX_TRANSFER_TIME)
    {

        uint8_t packetType;

        // Buffer sementara satu packet.
        //
        // Packet data R503 biasanya relatif kecil,
        // tetapi kita beri ruang 1024 byte.
        //
        uint8_t packetPayload[1024];

        size_t packetPayloadLength = 0;

        if (!readR503Packet(
                packetType,
                packetPayload,
                sizeof(packetPayload),
                packetPayloadLength))
        {

            Serial.println(
                "Gagal membaca packet R503.");

            return false;
        }

        // =================================================
        // ACK PACKET
        // =================================================

        if (packetType == R503_PACKET_ACK)
        {

            Serial.print(
                "[ACK] Payload: ");

            Serial.print(
                packetPayloadLength);

            Serial.println(
                " bytes");

            if (
                packetPayloadLength >= 1 &&
                packetPayload[0] == 0x00)
            {

                Serial.println(
                    "[ACK] UpImage diterima R503.");

                gotAck = true;
            }
            else
            {

                Serial.println(
                    "[ACK] Confirmation code ERROR.");

                if (packetPayloadLength >= 1)
                {

                    Serial.print(
                        "Code: 0x");

                    Serial.println(
                        packetPayload[0],
                        HEX);
                }

                return false;
            }
        }

        // =================================================
        // DATA PACKET
        // =================================================

        else if (
            packetType ==
            R503_PACKET_DATA)
        {

            Serial.print(
                "[DATA] ");

            Serial.print(
                packetPayloadLength);

            Serial.print(
                " bytes");

            Serial.print(
                " | Total: ");

            // Pastikan tidak overflow
            if (
                imageSize +
                    packetPayloadLength >
                IMAGE_BUFFER_SIZE)
            {

                Serial.println();

                Serial.println(
                    "ERROR: Image buffer penuh!");

                Serial.print(
                    "Ukuran maksimum: ");

                Serial.println(
                    IMAGE_BUFFER_SIZE);

                return false;
            }

            // Copy payload ke image buffer
            memcpy(
                imageBuffer + imageSize,
                packetPayload,
                packetPayloadLength);

            imageSize +=
                packetPayloadLength;

            Serial.println(
                imageSize);
        }

        // =================================================
        // END PACKET
        // =================================================

        else if (
            packetType ==
            R503_PACKET_END)
        {

            Serial.print(
                "[END] Payload: ");

            Serial.print(
                packetPayloadLength);

            Serial.println(
                " bytes");

            // Ada kemungkinan END packet membawa
            // bagian terakhir image.
            //
            // Karena itu payload tetap kita simpan.

            if (
                imageSize +
                    packetPayloadLength >
                IMAGE_BUFFER_SIZE)
            {

                Serial.println(
                    "ERROR: Image buffer penuh!");

                return false;
            }

            if (packetPayloadLength > 0)
            {

                memcpy(
                    imageBuffer + imageSize,
                    packetPayload,
                    packetPayloadLength);

                imageSize +=
                    packetPayloadLength;
            }

            gotEnd = true;

            break;
        }

        // =================================================
        // UNKNOWN PACKET
        // =================================================

        else
        {

            Serial.print(
                "[UNKNOWN] PID = 0x");

            Serial.println(
                packetType,
                HEX);
        }
    }

    // -------------------------------------------------
    // Validasi hasil
    // -------------------------------------------------

    if (!gotAck)
    {

        Serial.println(
            "Tidak menerima ACK UpImage.");

        return false;
    }

    if (!gotEnd)
    {

        Serial.println(
            "Tidak menerima END packet.");

        return false;
    }

    if (imageSize == 0)
    {

        Serial.println(
            "Image payload kosong.");

        return false;
    }

    Serial.println();
    Serial.println(
        "Image berhasil dikumpulkan.");

    Serial.print(
        "PAYLOAD IMAGE = ");

    Serial.print(
        imageSize);

    Serial.println(
        " bytes");

    return true;
}

bool uploadToServer(int mode)
{
    if (WiFi.status() != WL_CONNECTED)
    {
        Serial.println("WiFi tidak terhubung.");
        return false;
    }

    if (imageSize != 18432)
    {
        Serial.print("Image size invalid, expected 18432 actual ");
        Serial.println(imageSize);
        return false;
    }

    HTTPClient http;
    String endpoint;

    Serial.println();
    Serial.println("Mengirim image ke Express...");

    if (mode == 1)
    {
        endpoint = String(SERVER_ENROLL_URL) +
                   "?externalUserId=" +
                   enrollmentExternalUserId;
    }
    else
    {
        endpoint = SERVER_URL;
    }

    Serial.print("Endpoint: ");
    Serial.println(endpoint);
    http.begin(endpoint);

    http.addHeader(
        "Content-Type",
        "application/octet-stream");

    http.setTimeout(15000);

    int httpCode = http.POST(
        imageBuffer,
        imageSize);

    Serial.print("HTTP Status: ");
    Serial.println(httpCode);

    if (httpCode > 0)
    {
        String response =
            http.getString();

        Serial.println(
            "Response server:");

        Serial.println(response);
    }

    http.end();

    return (
        httpCode >= 200 &&
        httpCode < 300);
}

void sendDeviceStatus(uint8_t clientId)
{
    String json = "{";
    json += "\"type\":\"status\",";
    json += "\"mode\":\"";
    json += (mode == 0 ? "identify" : "enroll");
    json += "\",";
    json += "\"externalUserId\":\"";
    json += enrollmentExternalUserId;
    json += "\"}";

    webSocket.sendTXT(clientId, json);
}

void webSocketEvent(
    uint8_t clientId,
    WStype_t type,
    uint8_t *payload,
    size_t length)
{
    if (type == WStype_CONNECTED)
    {
        Serial.printf(
            "WebSocket client %u terhubung\n",
            clientId);

        sendDeviceStatus(clientId);
        return;
    }

    if (type != WStype_TEXT)
    {
        return;
    }

    String message;
    message.reserve(length);

    for (size_t i = 0; i < length; i++)
    {
        message += (char)payload[i];
    }

    message.trim();

    Serial.printf(
        "WebSocket command: %s\n",
        message.c_str());

    if (message == "identify")
    {
        mode = 0;

        Serial.println("Mode IDENTIFICATION aktif.");
        webSocket.broadcastTXT(
            "{\"type\":\"mode\",\"mode\":\"identify\",\"ok\":true}");
    }
    else if (message == "cancel")
    {
        mode = 0;

        Serial.println("Enrollment dibatalkan; mode identify aktif.");
        webSocket.broadcastTXT(
            "{\"type\":\"mode\",\"mode\":\"identify\",\"ok\":true}");
    }
    else if (message == "status")
    {
        sendDeviceStatus(clientId);
    }
    else if (message.startsWith("enroll:"))
    {
        String externalUserId = message.substring(7);
        externalUserId.trim();

        if (setEnrollmentUserId(externalUserId))
        {
            mode = 1;

            Serial.printf(
                "Mode ENROLLMENT aktif. ID: %s\n",
                enrollmentExternalUserId.c_str());

            String json = "{\"type\":\"mode\",";
            json += "\"mode\":\"enroll\",";
            json += "\"externalUserId\":\"";
            json += enrollmentExternalUserId;
            json += "\",\"ok\":true}";

            webSocket.broadcastTXT(json);
        }
        else
        {
            webSocket.sendTXT(
                clientId,
                "{\"type\":\"error\",\"message\":\"externalUserId tidak valid\"}");
        }
    }
    else
    {
        webSocket.sendTXT(
            clientId,
            "{\"type\":\"error\",\"message\":\"Perintah tidak dikenal\"}");
    }
}