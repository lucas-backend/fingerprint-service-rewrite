void handleSerialCommand();
void printSerialMenu();
bool isValidExternalUserId(const String &value);
bool setEnrollmentUserId(String value);
void sendCommand(uint8_t instruction);

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
        Serial.println(
            "Contoh: 550e8400-e29b-41d4-a716-446655440000");

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
    if (value.length() != 36)
    {
        return false;
    }

    for (size_t i = 0; i < value.length(); i++)
    {
        char character = value.charAt(i);

        bool isHyphen = i == 8 || i == 13 || i == 18 || i == 23;
        bool isHex =
            (character >= '0' && character <= '9') ||
            (character >= 'a' && character <= 'f') ||
            (character >= 'A' && character <= 'F');

        if ((isHyphen && character != '-') || (!isHyphen && !isHex))
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