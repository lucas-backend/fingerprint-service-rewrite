#include <WebSocketsServer.h>

WebSocketsServer webSocket(81);
bool authenticatedClients[WEBSOCKETS_SERVER_CLIENT_MAX] = {};

bool uploadToServer(int mode);
void sendDeviceStatus(uint8_t clientId);
void webSocketEvent(
    uint8_t clientId,
    WStype_t type,
    uint8_t *payload,
    size_t length);

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
    if (!http.begin(endpoint))
    {
        Serial.println("Gagal membuka koneksi HTTP.");
        return false;
    }

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

        if (clientId < WEBSOCKETS_SERVER_CLIENT_MAX)
        {
            authenticatedClients[clientId] = false;
        }
        webSocket.sendTXT(clientId, "{\"type\":\"auth_required\"}");
        return;
    }

    if (type == WStype_DISCONNECTED)
    {
        if (clientId < WEBSOCKETS_SERVER_CLIENT_MAX)
        {
            authenticatedClients[clientId] = false;
        }
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

    if (message.startsWith("auth:"))
    {
        String token = message.substring(5);
        token.trim();
        if (token == WEBSOCKET_TOKEN)
        {
            authenticatedClients[clientId] = true;
            sendDeviceStatus(clientId);
        }
        else
        {
            webSocket.sendTXT(
                clientId,
                "{\"type\":\"error\",\"message\":\"Autentikasi gagal\"}");
        }
        return;
    }

    if (clientId >= WEBSOCKETS_SERVER_CLIENT_MAX ||
        !authenticatedClients[clientId])
    {
        webSocket.sendTXT(
            clientId,
            "{\"type\":\"error\",\"message\":\"Autentikasi diperlukan\"}");
        return;
    }

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