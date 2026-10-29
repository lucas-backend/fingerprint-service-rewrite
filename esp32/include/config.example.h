#pragma once
// =====================================================
// ESP32 local configuration — TEMPLATE
// =====================================================
// Copy to `config.h` (gitignored) and fill in real values.
// Never commit `config.h` with real credentials.
//
//   copy  esp32/include/config.example.h  ->  esp32/include/config.h
//
// Alternatively, pass values via PlatformIO build_flags:
//   build_flags = -D WIFI_SSID="\"MySSID\"" -D WIFI_PASSWORD="\"secret\"" -D SERVER_URL="\"http://192.168.1.7:3000/api/fingerprint/identify\""
//
// `config.h` (if present) overrides the placeholder defaults in main.cpp
// via the #ifndef guards, so no source edit is needed.

#define WIFI_SSID "MyWiFiSSID"
#define WIFI_PASSWORD "MyWiFiPassword"
// Express gateway endpoint (must be reachable from ESP32's WiFi)
#define SERVER_URL "http://192.168.1.7:3000/api/fingerprint/identify"
