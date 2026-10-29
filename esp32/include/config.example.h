#pragma once

// Copy this file to config.h and replace every placeholder.
// Never commit config.h or real credentials.

#define WIFI_SSID "your-wifi-ssid"
#define WIFI_PASSWORD "your-wifi-password"

// Use an address reachable from the ESP32; localhost refers to the ESP32.
#define SERVER_URL "http://192.168.1.100:3000/api/fingerprint/identify"
#define SERVER_ENROLL_URL "http://192.168.1.100:3000/api/fingerprint/enroll/raw"

// Clients must send auth:<token> before issuing WebSocket commands.
#define WEBSOCKET_TOKEN "replace-with-a-long-random-token"
