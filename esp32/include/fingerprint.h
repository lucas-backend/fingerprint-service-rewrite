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



bool getImage();

bool uploadImage();

bool readByteTimeout(
    uint8_t &value,
    unsigned long timeoutMs);

bool readR503Packet(
    uint8_t &packetType,
    uint8_t *payload,
    size_t payloadBufferSize,
    size_t &payloadLength,
    unsigned long deadline = 0);

bool readByteUntil(
    uint8_t &value,
    unsigned long deadline);


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

bool readByteUntil(
    uint8_t &value,
    unsigned long deadline)
{
    unsigned long now = millis();
    if (now >= deadline)
    {
        return false;
    }

    return readByteTimeout(value, deadline - now);
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
    size_t &payloadLength,
    unsigned long deadline)
{

    payloadLength = 0;

    uint8_t b;

    // -------------------------------------------------
    // Cari header EF 01
    // -------------------------------------------------

    bool foundHeader = false;

    while (true)
    {

        if (deadline != 0 ? !readByteUntil(b, deadline) : !readByteTimeout(b, 2000))
        {

            return false;
        }

        if (b != 0xEF)
        {

            continue;
        }

        if (deadline != 0 ? !readByteUntil(b, deadline) : !readByteTimeout(b, 100))
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

        if (deadline != 0
                ? !readByteUntil(address[i], deadline)
                : !readByteTimeout(address[i], 1000))
        {

            return false;
        }
    }

    if (address[0] != 0xFF ||
        address[1] != 0xFF ||
        address[2] != 0xFF ||
        address[3] != 0xFF)
    {
        Serial.println("Alamat packet R503 tidak sesuai.");
        return false;
    }

    // -------------------------------------------------
    // Packet identifier
    // -------------------------------------------------

    if (deadline != 0
            ? !readByteUntil(packetType, deadline)
            : !readByteTimeout(packetType, 1000))
    {

        return false;
    }

    // -------------------------------------------------
    // Length
    // -------------------------------------------------

    uint8_t lengthHigh;
    uint8_t lengthLow;

    if (deadline != 0
            ? !readByteUntil(lengthHigh, deadline)
            : !readByteTimeout(lengthHigh, 1000))
    {

        return false;
    }

    if (deadline != 0
            ? !readByteUntil(lengthLow, deadline)
            : !readByteTimeout(lengthLow, 1000))
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

        if (deadline != 0
                ? !readByteUntil(payload[i], deadline)
                : !readByteTimeout(payload[i], 3000))
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

    if (deadline != 0
            ? !readByteUntil(checksumHigh, deadline)
            : !readByteTimeout(checksumHigh, 1000))
    {

        return false;
    }

    if (deadline != 0
            ? !readByteUntil(checksumLow, deadline)
            : !readByteTimeout(checksumLow, 1000))
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
                packetPayloadLength,
                startTime + MAX_TRANSFER_TIME))
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
