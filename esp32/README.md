# ESP32 + R503 Firmware

Firmware ESP32 untuk membaca image sidik jari dari sensor **R503** melalui UART, lalu mengirimkan image mentah (raw) tersebut ke **server** melalui HTTP.

---

## 1. Overview

### Tujuan firmware

Mengambil image sidik jari dari sensor R503 dan mengirimkannya ke backend sebagai **binary payload mentah**. Firmware ini **tidak** melakukan pencocokan (matching) sidik jari.

### Hardware yang digunakan

- **ESP32 DevKit** (`esp32dev`)
- **R503 Fingerprint Sensor** (komunikasi UART)

### Peran masing-masing komponen

| Komponen | Peran |
| --- | --- |
| **R503** | Menangkap (capture) image sidik jari dan menyimpannya di buffer internal sensor. Image kemudian di-upload sensor ke ESP32. |
| **ESP32** | Berkomunikasi dengan sensor melalui UART (kirim command, terima packet), menyimpan payload image di RAM, lalu mengirimkannya via HTTP. |
| **ASP.NET Fingerprint Service + SourceAFIS** | Melakukan decoding image, pembuatan template, dan matching sidik jari. |

> **Penting:** ESP32 **tidak** melakukan fingerprint recognition/matching. ESP32 hanya menjembatani sensor (UART) dan jaringan (HTTP). Seluruh proses pengenalan ada di `fingerprint-service` (SourceAFIS).

### Apa yang TIDAK dilakukan ESP32

- Tidak melakukan matching/identification sidik jari.
- Tidak menyimpan template sidik jari.
- Tidak menyimpan image secara permanen (hanya di RAM, hilang saat reboot).
- Enrollment dapat dimulai oleh admin melalui Serial Monitor.
- Tidak memvalidasi checksum packet R503 (lihat [bagian UART](#3-uart)).
- Tidak memiliki mekanisme retry otomatis ke server (hanya loop sederhana).

---

## 2. Hardware / Wiring

Wiring didokumentasikan berdasarkan definisi pada `esp32/src/main.cpp`.

```cpp
HardwareSerial R503(2);   // gunakan UART2

#define R503_RX 16  // ESP32 RX <- TX R503
#define R503_TX 17  // ESP32 TX -> RX R503
```

| Sinyal R503 | Arah | Pin ESP32 |
| --- | --- | --- |
| R503 TX | → | ESP32 GPIO16 (RX ESP32) |
| R503 RX | ← | ESP32 GPIO17 (TX ESP32) |
| VCC | — | 3.3V ESP32 |
| GND | — | GND ESP32 |

> Catatan: VCC/GND tidak didefinisikan di source code (merupakan wiring fisik). Yang didefinisikan secara eksplisit di kode hanyalah RX=16 dan TX=17.

---

## 3. UART

| Parameter | Nilai (dari source code) |
| --- | --- |
| UART | UART2 (`HardwareSerial R503(2)`) |
| Baud rate | `57600` (`R503_BAUD`) |
| Format | `SERIAL_8N1` (8 data bit, no parity, 1 stop bit) |
| ESP32 RX (dari R503 TX) | GPIO16 |
| ESP32 TX (ke R503 RX) | GPIO17 |
| Serial monitor | 115200 baud (`Serial.begin(115200)`) |

### Struktur packet R503

Dikirim (`sendCommand`) dan diterima (`readR503Packet`) dengan format:

```text
EF 01                         (header, 2 byte)
ADDRESS                       (4 byte, 0xFFFFFFFF)
PID (Packet Identifier)       (1 byte)
LENGTH                        (2 byte, big-endian)
PAYLOAD                       (LENGTH - 2 byte)
CHECKSUM                      (2 byte, big-endian)
```

- `LENGTH = payload + checksum (2 byte)`, sehingga:
  `payloadLength = packetLength - 2`
- Checksum pada packet hasil `UpImage` **tidak divalidasi** (lihat komentar di `readR503Packet`). Validasi checksum masih berupa *future work*.

### Tipe packet (PID)

| Konstanta | Nilai | Arti |
| --- | --- | --- |
| `R503_PACKET_COMMAND` | `0x01` | Command yang dikirim ke sensor |
| `R503_PACKET_DATA` | `0x02` | Data image |
| `R503_PACKET_ACK` | `0x07` | Acknowledgment |
| `R503_PACKET_END` | `0x08` | Akhir transfer data |

### Commands

| Konstanta | Nilai | Fungsi |
| --- | --- | --- |
| `R503_CMD_GET_IMAGE` | `0x01` | Memerintahkan sensor menangkap image ke buffer internal |
| `R503_CMD_UP_IMAGE` | `0x0A` | Memerintahkan sensor mengirim image ke host |

---

## 4. Fingerprint capture flow

Alur pada `loop()`:

```text
Tempelkan jari
      │
      ▼
getImage()                       ── kirim command GetImage (0x01)
      │                             baca packet balasan
      │                             confirmation code 0x00 = sukses
      ▼
delay(100)
      │
      ▼
uploadImage()                    ── kirim command UpImage (0x0A)
      │                             terima packet ACK + DATA + END
      ▼
uploadToServer(mode)             ── HTTP POST imageBuffer
      │
      ▼
delay(3000) → ulangi loop
```

Mode default firmware adalah **IDENTIFICATION**. Mode dan enrollment dapat
dikendalikan melalui CLI pada Serial Monitor. Pada mode enrollment, firmware
meminta `externalUserId` sebelum melakukan capture dan menambahkan ID tersebut
ke URL enrollment.

### Detail `getImage()`

1. Membersihkan buffer UART.
2. Mengirim command `GetImage` (`sendCommand(0x01)`).
3. Mencari header `EF 01`, lalu membaca PID, LENGTH, payload, dan checksum.
4. Membaca **confirmation code** pada `packet[9]`:
   - `0x00` → sukses.
   - `0x02` → jari tidak terdeteksi.
   - kode lain → error.
5. Timeout 1000 ms untuk keseluruhan packet.

### Detail `uploadImage()`

1. Mengosongkan `imageSize` dan buffer UART.
2. Mengirim command `UpImage` (`sendCommand(0x0A)`).
3. Menerima packet berulang sampai:
   - packet `ACK` (`0x07`) dengan `payload[0] == 0x00` → `gotAck = true`.
   - packet `DATA` (`0x02`) → payload di-`memcpy` ke `imageBuffer`.
   - packet `END` (`0x08`) → payload terakhir (jika ada) disimpan, lalu loop berhenti (`gotEnd = true`).
4. Batas waktu transfer maksimum **15.000 ms** (`MAX_TRANSFER_TIME`).
5. Transfer dianggap sukses jika `gotAck == true`, `gotEnd == true`, dan `imageSize > 0`.

### Bagaimana firmware tahu image sudah lengkap

Firmware **tidak** mengetahui ukuran image final secara eksplisit. Firmware hanya:
- mengakumulasi seluruh payload dari packet `DATA` dan `END` ke `imageBuffer`, dan
- berhenti saat packet `END` diterima.

Validasi panjang image yang benar (18.432 byte) **tidak dilakukan di ESP32**, melainkan di layer server dan fingerprint-service.

---

## 5. Image format

Berdasarkan komentar pada source code dan decoding di sisi ASP.NET:

| Properti | Nilai |
| --- | --- |
| Resolusi | 192 × 192 pixel |
| Jumlah pixel | 36.864 pixel |
| Kedalaman | 4-bit per pixel (16 level grayscale) |
| Packing | 2 pixel per byte (high nibble + low nibble) |
| Ukuran payload | **18.432 byte** |
| Ukuran buffer firmware | 32.768 byte (`IMAGE_BUFFER_SIZE`) |

Perhitungan:

```text
192 × 192           = 36.864 pixel
36.864 / 2          = 18.432 byte
```

### Contoh packing

Satu byte `0xAB` berisi dua pixel:

```text
0xAB
  ├── high nibble = A (0xA)  → pixel pertama
  └── low nibble  = B (0xB)  → pixel kedua
```

Konversi 4-bit → 8-bit grayscale dilakukan di ASP.NET dengan `nibble * 17`:

```text
0x0 → 0
0x1 → 17
0x8 → 136
0xF → 255
```

Image yang dikirim ESP32 adalah **byte packed 4-bit** (18.432 byte), **bukan** grayscale 8-bit. ESP32 tidak melakukan unpacking.

---

## 6. Network upload

Implementasi pada `uploadToServer()`:

| Item | Nilai |
| --- | --- |
| Method | `POST` (`http.POST`) |
| Identification | `SERVER_URL`, default `http://192.168.137.1:3000/api/fingerprint/identify` |
| Enrollment | `SERVER_ENROLL_URL?externalUserId=<id>`, default `http://192.168.137.1:3000/api/fingerprint/enroll/raw` |
| Content-Type | `application/octet-stream` |
| Body | `imageBuffer` sebanyak `imageSize` byte (raw packed image) |
| Timeout | `http.setTimeout(15000)` (15 detik) |
| Sukses | HTTP status `2xx` (`httpCode >= 200 && httpCode < 300`) |

> **Catatan penting:** Kedua endpoint adalah milik **server gateway** pada port
> `3000`, bukan endpoint yang dipanggil langsung dari ESP32 ke
> `fingerprint-service`. Gateway meneruskan request ke service internal.

### Kontrak enrollment raw

Enrollment menggunakan:

```text
POST /api/fingerprint/enroll/raw?externalUserId=<id>
Content-Type: application/octet-stream
Body: 18432 byte raw packed image
```

`externalUserId` wajib diisi dan hanya boleh berisi huruf, angka, `-`, `_`,
atau `.`, dengan panjang maksimum 64 karakter. Contoh:

```text
/api/fingerprint/enroll/raw?externalUserId=EMP-001
```

### Response yang diharapkan

Firmware membaca response body sebagai string dan mencetaknya ke serial monitor. Response dari server adalah JSON hasil identifikasi dari ASP.NET:

```json
{
  "success": true,
  "matched": true,
  "userId": 1,
  "fingerprintId": 1,
  "name": "Budi",
  "employeeCode": "EMP001",
  "score": 45.68,
  "candidateCount": 1
}
```

### Error handling

- `WiFi.status() != WL_CONNECTED` → log `"WiFi tidak terhubung."`, return `false`.
- `imageSize == 0` → log `"Image kosong."`, return `false`.
- `httpCode <= 0` → tidak ada response; return `false`.
- `httpCode` di luar 2xx → dianggap gagal (return `false`).
- Kegagalan di `getImage()` / `uploadImage()` → loop berhenti, `delay(2000)`, coba lagi.

Tidak ada retry HTTP atau backoff eksplisit.

---

## 7. Data lifecycle

```text
R503
 │  capture ke buffer internal sensor
 ▼
raw packed image bytes (18.432 byte ideal)
 │  packet DATA/END via UART
 ▼
ESP32 RAM (imageBuffer[32768])
 │  HTTP POST application/octet-stream
 ▼
server :3000 /api/fingerprint/identify
 │
 ▼
ASP.NET fingerprint-service
```

- Image **hanya berada di RAM** ESP32 (`uint8_t imageBuffer[IMAGE_BUFFER_SIZE]`) dan **ditimpa** pada setiap iterasi `loop()`.
- **Tidak ada penyimpanan permanen** di ESP32. Setelah reboot, buffer kosong.
- ESP32 juga tidak menyimpan template atau identitas pengguna.

---

## 8. Configuration

Seluruh konfigurasi **hard-coded** di `esp32/src/main.cpp`:

| Konfigurasi | Constanta / definisi | Keterangan |
| --- | --- | --- |
| Wi-Fi SSID | `WIFI_SSID` | Kredensial Wi-Fi (nilai tidak ditampilkan di dokumentasi ini) |
| Wi-Fi Password | `WIFI_PASSWORD` | Kredensial Wi-Fi (nilai tidak ditampilkan) |
| Server URL | `SERVER_URL` | Endpoint server |
| Enrollment URL | `SERVER_ENROLL_URL` | Base endpoint enrollment raw |
| Mode default | `mode = 0` | Identification |
| Enrollment ID | `enrollmentExternalUserId` | Default `1`, dapat diubah dari CLI |
| UART instance | `HardwareSerial R503(2)` | UART2 |
| RX pin | `R503_RX` = 16 | GPIO16 |
| TX pin | `R503_TX` = 17 | GPIO17 |
| Baud rate | `R503_BAUD` = 57600 | UART R503 |
| Serial monitor | `Serial.begin(115200)` | Debug |
| Image buffer | `IMAGE_BUFFER_SIZE` = 32768 | Ukuran buffer RAM |
| Transfer timeout | `MAX_TRANSFER_TIME` = 15000 | ms, `UpImage` |
| HTTP timeout | `http.setTimeout(15000)` | ms |

> **Security:** Credential Wi-Fi (`WIFI_SSID`, `WIFI_PASSWORD`) dan `SERVER_URL` saat ini **hard-coded** di source. Untuk production sebaiknya dipindahkan ke mekanisme konfigurasi terpisah (mis. NVS/Preferences atau build flags) dan tidak di-commit. Rekomendasi ini belum diimplementasikan.

Konfigurasi melalui `platformio.ini`:

```ini
[env:esp32dev]
platform = espressif32
board = esp32dev
framework = arduino
monitor_speed = 115200
```

---

## 9. Cara compile/upload

Project menggunakan **PlatformIO**.

### Prasyarat

- [PlatformIO](https://platformio.org/) (VS Code extension atau CLI).
- Board ESP32 DevKit.

### Langkah

```bash
cd esp32

# Build
pio run

# Upload ke board (sesuaikan COM port)
pio run --target upload

# Serial monitor
pio device monitor --baud 115200
```

### CLI Serial Monitor

Gunakan Serial Monitor pada **115200 baud** dengan line ending **Newline**.
Saat boot, firmware menampilkan menu admin. Perintah yang tersedia:

| Perintah | Fungsi |
| --- | --- |
| `menu` atau `help` | Menampilkan menu admin |
| `i`, `identify`, `identification` | Mengaktifkan mode identification |
| `e`, `enroll` | Memulai enrollment interaktif dan meminta ID pengguna |
| `enroll <id>` | Memulai enrollment langsung dengan ID yang diberikan |
| `status` | Menampilkan mode, enrollment ID, status Wi-Fi, dan IP ESP32 |
| `cancel` | Membatalkan enrollment dan kembali ke identification |

#### Contoh enrollment interaktif

```text
> enroll
=== PENDAFTARAN FINGERPRINT ===
Masukkan externalUserId lalu tekan Enter.
Contoh: 123 atau EMP-001
> EMP-001
ID pengguna: EMP-001
Mode ENROLLMENT aktif.
Tempelkan jari untuk mendaftarkan fingerprint.
```

Setelah image berhasil diambil, firmware mengirimkannya ke endpoint enrollment
raw. Setelah proses selesai, mode tetap enrollment sampai admin mengetik
`cancel` atau memilih `identify`.

### Library

Tidak ada dependensi library eksternal pada `platformio.ini`; firmware hanya menggunakan library bawaan framework Arduino untuk ESP32 (`Arduino.h`, `WiFi.h`, `HTTPClient.h`).

### Yang diharapkan pada serial monitor

```text
Menghubungkan WiFi...
WiFi terhubung.
IP ESP32: 192.168.x.x

--------------------------------------
Tempelkan jari...
--------------------------------------
>>> Fingerprint image berhasil diambil!
...
[DATA] ... | Total: ...
[END] Payload: ...
PAYLOAD IMAGE = 18432 bytes
HTTP Status: 200
Response server:
{"success":true,...}
```

Contoh tampilan CLI saat enrollment:

```text
Mode ENROLLMENT aktif.
Tempelkan jari untuk mendaftarkan fingerprint.
...
Endpoint: http://192.168.137.1:3000/api/fingerprint/enroll/raw?externalUserId=EMP-001
HTTP Status: 200
Response server:
{"success":true,"subjectId":1,"fingerprintId":2,"externalUserId":"EMP-001"}
```

---

## 10. Troubleshooting

### Sensor tidak merespons

```text
GET_IMAGE: header timeout
```
- Periksa wiring: ESP32 RX (GPIO16) ← R503 TX, ESP32 TX (GPIO17) → R503 RX.
- Periksa baud rate sensor (`57600`).
- Pastikan R503 mendapat power 3.3V dan GND tersambung.

### GetImage gagal

- `R503 response error: 0x02` → jari tidak terdeteksi; tempelkan jari dengan menutupi seluruh area sensor.
- Kode error lain → cek datasheet R503 untuk arti confirmation code.

### UpImage gagal

- `Tidak menerima ACK UpImage.` → sensor tidak mengirim ACK; ulangi proses.
- `Tidak menerima END packet.` → transfer terputus / timeout 15 detik.
- `ERROR: Image buffer penuh!` → payload melebihi `IMAGE_BUFFER_SIZE` (32.768 byte).

### Payload tidak 18.432 byte

- Firmware tidak memvalidasi ukuran. Jika server menolak dengan pesan `Expected: 18432, Actual: ...`, berarti transfer packet R503 tidak lengkap.
- Periksa kestabilan UART (kabel, panjang kabel, baud rate) dan coba ulangi capture.

### Wi-Fi gagal

- `WiFi tidak terhubung.` → cek `WIFI_SSID` / `WIFI_PASSWORD` dan jangkauan jaringan.
- ESP32 hanya mendukung Wi-Fi 2.4 GHz.

### HTTP gagal

- `HTTP Status: -1` (atau `<= 0`) → server tidak dapat dijangkau.
- Pastikan `SERVER_URL` mengarah ke IP LAN komputer yang menjalankan server (bukan `localhost`).
- Untuk enrollment, pastikan `SERVER_ENROLL_URL` memakai IP LAN dan port gateway
  `3000`, bukan port internal fingerprint-service `5001`.
- Pastikan ESP32 dan komputer berada di jaringan yang sama.
- Pastikan server berjalan di port `3000` dan firewall mengizinkan koneksi masuk.

---

## 11. Dependency antar-service

```text
ESP32
  │  HTTP POST application/octet-stream (18.432 byte)
  ▼
server :3000 /api/fingerprint/identify
  │  HTTP POST application/octet-stream
  ▼
fingerprint-service :5001 /api/v1/fingerprints/identify
  │
  ▼
SourceAFIS + limen_db
```

ESP32 bergantung sepenuhnya pada server sebagai gateway. ESP32 tidak berkomunikasi langsung langsung dengan fingerprint-service maupun MySQL.

Urutan menjalankan sistem lengkap dapat dilihat pada bagian **Runbook End-to-End** di `server/README.md`.
