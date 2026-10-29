# Fingerprint Service Rewrite

Proyek ini menyediakan sistem identifikasi sidik jari berbasis sensor R503.
Perangkat ESP32 mengambil capture dari sensor, lalu mengirimkannya melalui
HTTP ke backend. Backend memisahkan tanggung jawab menjadi:

- **ESP32**: menjembatani sensor R503 dan jaringan. ESP32 tidak melakukan
  matching dan tidak menyimpan template secara permanen.
- **Fingerprint service (.NET)**: membuat template, menyimpan template, dan
  melakukan identifikasi terhadap template yang sudah terdaftar.
- **Express API (opsional)**: gateway aplikasi yang mengelola data user,
  meneruskan request fingerprint ke service .NET, serta memperkaya hasil
  identifikasi dengan data aplikasi.
- **PostgreSQL**: menyimpan data user, subject/template fingerprint, dan audit
  identification sesuai skema yang digunakan deployment.

README ini berfokus pada cara menggunakan API dan mengintegrasikannya dengan
sistem lain, khususnya sistem absensi. Detail implementasi decoding dan
scanning citra tidak dibahas di sini. Lihat README pada masing-masing
komponen untuk detail operasional:

- [ESP32 firmware](./esp32/README.md)
- [Fingerprint service .NET](./server/dotnet/README.md)
- [Express API](./server/express/README.md)
- [Schema aplikasi](./server/express/database/schema.sql)
- [Schema domain yang direncanakan](./docs/NEW_SCHEMA.md)

## Alur sistem

### Identifikasi

```text
Sensor R503
    │ capture binary
    ▼
ESP32 atau client lain
    │ POST application/octet-stream
    ▼
Express API (opsional)
    │ proxy
    ▼
Fingerprint service .NET
    │ hasil: externalUserId, score, fingerprintId
    ▼
Sistem aplikasi/absensi
    │ resolve user dan simpan event
    ▼
PostgreSQL aplikasi
```

Client dapat melewati Express dan memanggil service .NET secara langsung.
Pilihan ini cocok untuk service absensi yang sudah memiliki backend sendiri.
Jika memakai Express, gunakan endpoint gateway `/api/fingerprint/*`; jika
langsung ke .NET, gunakan endpoint `/api/v1/fingerprints/*`.

Hasil identifikasi tidak mengembalikan nama, email, NIS/NIP, atau data profil
lain. Nilai `externalUserId` adalah UUID `users.id` milik aplikasi. Aplikasi
tetap bertanggung jawab untuk mengambil profil user, memeriksa status user,
dan menentukan apakah hasil tersebut boleh dicatat sebagai absensi.

### Enrollment

Enrollment menghubungkan satu atau lebih template fingerprint dengan satu
`externalUserId`.

1. Aplikasi memastikan user sudah dibuat dan aktif.
2. Aplikasi mengirim capture R503 mentah ke endpoint `enroll/raw`, atau
   mengirim template Base64 ke endpoint `enroll`.
3. Service .NET memvalidasi ID dan input, membuat/menyimpan template, lalu
   mengembalikan ID subject dan fingerprint.
4. Aplikasi menyimpan metadata user dan perangkatnya sendiri; service
   fingerprint hanya menyimpan relasi biometrik dengan `externalUserId`.

`enroll/raw` biasanya lebih aman dan sederhana karena template tidak perlu
keluar dari service .NET. Beberapa template dapat didaftarkan untuk user yang
sama, misalnya beberapa jari atau beberapa capture berkualitas baik.

## Menjalankan komponen

### Prasyarat

- .NET SDK 8 untuk service fingerprint.
- Node.js 20 atau lebih baru untuk Express API.
- PostgreSQL yang sudah diisi dengan schema yang sesuai.
- Sensor R503 dan ESP32 hanya diperlukan bila capture dilakukan melalui
  perangkat fisik; client lain dapat mengirim binary capture yang sama.

### Service .NET

```powershell
cd server/dotnet
dotnet restore
dotnet run --urls http://localhost:5001
```

Konfigurasi penting:

| Environment variable | Fungsi |
|---|---|
| `ConnectionStrings__Postgres` | Connection string PostgreSQL. |
| `ApiKey__Value` | API key opsional untuk request `/api/*`. |
| `Fingerprint__MatchThreshold` | Ambang score matching; kalibrasikan sebelum production. |
| `Fingerprint__MaxTemplatesPerSubject` | Batas template per user, default `3`. |
| `Server__MaxRequestBodySizeBytes` | Batas ukuran body HTTP. |
| `Cors__AllowedOrigins__0` | Origin CORS bila client browser memanggil service langsung. |

Swagger hanya tersedia pada environment `Development` di
`http://localhost:5001/swagger`. Contoh Docker dan konfigurasi lengkap ada di
[server/dotnet/README.md](./server/dotnet/README.md).

### Express API sebagai gateway

```powershell
cd server/express
npm ci
Copy-Item .env.example .env
npm start
```

Setidaknya isi `FINGERPRINT_SERVICE_URL`, konfigurasi PostgreSQL (`DB_*`), dan
`FINGERPRINT_API_KEY` bila service .NET memakai API key. Express tersedia pada
`http://localhost:3000`. Endpoint health:

```http
GET /health
```

Docker Compose dari direktori `server` menjalankan kedua service sekaligus:

```powershell
cd server
docker compose up --build
```

Di dalam Compose, Express menggunakan
`http://fingerprint-service:8080` untuk komunikasi internal; dari host,
fingerprint service tersedia di port `5001` dan Express di port `3000`.

## Kontrak API fingerprint (.NET)

Semua capture mentah harus dikirim sebagai:

```http
Content-Type: application/octet-stream
```

Body capture harus tepat **18.432 byte** sesuai format output R503. Service
menolak ukuran atau format yang tidak valid. Bila `ApiKey__Value` diaktifkan,
tambahkan:

```http
X-Api-Key: <shared-api-key>
```

Request ID dari client dapat diteruskan melalui `X-Request-Id` (atau header
korelasi yang didukung gateway) untuk penelusuran log.

### Endpoint production/versioned

#### `POST /api/v1/fingerprints/enroll`

Mendaftarkan template yang sudah dibuat sebelumnya.

Request:

```json
{
  "externalUserId": "11111111-1111-1111-1111-111111111111",
  "template": "<base64-template>"
}
```

Response `200`:

```json
{
  "success": true,
  "subjectId": 1,
  "fingerprintId": 1,
  "externalUserId": "11111111-1111-1111-1111-111111111111",
  "templateSize": 1234
}
```

`externalUserId` wajib berupa UUID user yang menjadi pemilik fingerprint.
Field profil seperti `name` dan `employeeCode` tidak diproses oleh endpoint
versioned ini.

#### `POST /api/v1/fingerprints/enroll/raw?externalUserId=<uuid>`

Pilihan enrollment yang disarankan. Kirim capture R503 langsung sebagai body
binary. Response memiliki bentuk yang sama dengan endpoint `enroll`.

```powershell
curl.exe -X POST "http://localhost:5001/api/v1/fingerprints/enroll/raw?externalUserId=11111111-1111-1111-1111-111111111111" `
  -H "Content-Type: application/octet-stream" `
  --data-binary "@capture.r503"
```

#### `POST /api/v1/fingerprints/identify`

Mencari template paling cocok dari seluruh template aktif.

```powershell
curl.exe -X POST http://localhost:5001/api/v1/fingerprints/identify `
  -H "Content-Type: application/octet-stream" `
  --data-binary "@capture.r503"
```

Response matched:

```json
{
  "success": true,
  "matched": true,
  "score": 62.41,
  "candidateCount": 17,
  "externalUserId": "11111111-1111-1111-1111-111111111111",
  "fingerprintId": 1
}
```

Response tidak matched:

```json
{
  "success": true,
  "matched": false,
  "score": 18.2,
  "candidateCount": 17,
  "reason": "No candidate exceeded the match threshold."
}
```

Interpretasi penting:

- `matched` adalah keputusan berdasarkan `MatchThreshold`; jangan
  menganggap score sebagai persentase.
- `candidateCount` adalah jumlah template yang dibandingkan.
- Bila belum ada template, `matched` bernilai `false` dan
  `candidateCount` bernilai `0`.
- Aplikasi harus memeriksa `externalUserId` dan mengambil data user dari
  database aplikasi sebelum membuat event absensi.

#### `POST /api/v1/fingerprints/diagnostics/capture`

Endpoint debugging terkontrol yang mengembalikan statistik capture dan dapat
menyimpan PGM bila diagnostics diaktifkan. Endpoint ini bukan bagian dari
alur absensi dan sebaiknya tidak diekspos ke jaringan publik. Di production,
route menjadi `404` bila `Fingerprint:Diagnostics:SavePgm` tidak diaktifkan.

### Endpoint compatibility lama

Endpoint berikut dipertahankan untuk client lama. Integrasi baru sebaiknya
memakai endpoint versioned.

| Method dan path | Keterangan |
|---|---|
| `POST /enrollment` | Enrollment JSON lama; tetap membutuhkan `externalUserId`. |
| `POST /identification` | Identification binary dengan bentuk response lama. |
| `POST /fingerprint/raw` | Validasi/unpack capture dan diagnostics lama. |
| `POST /fingerprint/template` | Membuat template Base64 dari capture R503. |
| `POST /fingerprint/match-test` | Pengujian pencocokan dua template Base64. |

### Endpoint gateway Express

Jika aplikasi memilih Express sebagai gateway, path-nya berbeda dari service
.NET tetapi payload fingerprint diteruskan hampir apa adanya:

| Method dan path | Kegunaan |
|---|---|
| `POST /api/fingerprint/enroll` | Proxy enrollment JSON ke .NET. |
| `POST /api/fingerprint/enroll/raw?externalUserId=<uuid>` | Proxy enrollment capture mentah. |
| `POST /api/fingerprint/identify` | Proxy identification dan memperkaya hasil dengan `fullName`/`email` bila user ditemukan. |
| `GET /api/fingerprint/latest` | Hasil identification terakhir; fallback ke database setelah restart. |
| `GET /api/fingerprint/stream` | SSE untuk menerima hasil identification baru tanpa polling. |
| `POST /api/users` | Membuat user aplikasi. |
| `GET /api/users` | Daftar user tanpa password hash. |
| `GET /api/users/:id` | Mengambil satu user berdasarkan UUID. |

Semua route `/api/*` pada Express dapat dilindungi `API_KEY` atau
`EXPRESS_API_KEY`. Key tersebut berbeda dari `FINGERPRINT_API_KEY`, yang
digunakan Express saat memanggil service .NET. `GET /health` tidak memerlukan
API key. Detail konfigurasi gateway ada di
[server/express/README.md](./server/express/README.md).

### Health dan readiness

| Endpoint | Kegunaan | Ketergantungan database |
|---|---|---|
| `GET /health` | Liveness compatibility | Tidak |
| `GET /health/live` | Probe proses/container | Tidak |
| `GET /health/ready` | Memastikan dependency siap | Ya, PostgreSQL |
| `GET /api/v1/health` | Ringkasan status service | Tidak |

Gunakan `/health/live` untuk liveness probe dan `/health/ready` untuk
readiness probe load balancer/orchestrator. Response readiness `503` berarti
service belum siap menerima operasi yang membutuhkan database.

### Error dan keamanan

Error validasi biasanya berbentuk JSON dengan `success: false`, `error.code`,
`error.message`, dan request ID. Client sebaiknya memperlakukan:

- `400` sebagai input atau content type tidak valid;
- `401` sebagai API key hilang/salah;
- `404` sebagai route/resource tidak tersedia;
- `413` sebagai body terlalu besar;
- `503` pada readiness sebagai dependency belum siap;
- `5xx` sebagai kegagalan service yang dapat dicoba ulang dengan backoff.

Gunakan HTTPS atau jaringan privat, simpan API key di secret manager, dan
jangan mengaktifkan diagnostics secara permanen. Capture dan template adalah
data biometrik sensitif. Hindari menulis payload biometrik ke log atau
menyimpannya di sistem absensi kecuali ada kebutuhan dan kebijakan retensi
yang jelas.

## Integrasi tanpa Express: contoh Go

Express bukan persyaratan. Sistem Go dapat memanggil service .NET langsung
dengan HTTP. Tanggung jawab client Go adalah:

1. mengambil atau menerima capture binary dari perangkat;
2. mengirim request ke service .NET;
3. memvalidasi status dan response JSON;
4. me-resolve `externalUserId` ke user lokal;
5. menerapkan aturan bisnis absensi dan menyimpan event secara idempotent.

Contoh minimal client Go menggunakan standard library:

```go
package fingerprint

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"time"
)

type Client struct {
	BaseURL string
	APIKey  string
	HTTP    *http.Client
}

type EnrollResponse struct {
	Success        bool   `json:"success"`
	SubjectID      int64  `json:"subjectId"`
	FingerprintID  int64  `json:"fingerprintId"`
	ExternalUserID string `json:"externalUserId"`
	TemplateSize   int    `json:"templateSize"`
}

type IdentifyResponse struct {
	Success        bool    `json:"success"`
	Matched        bool    `json:"matched"`
	Score          *float64 `json:"score"`
	CandidateCount int     `json:"candidateCount"`
	ExternalUserID *string `json:"externalUserId"`
	FingerprintID  *int64  `json:"fingerprintId"`
	Reason         *string `json:"reason"`
}

func (c *Client) do(ctx context.Context, method, path string, body []byte) ([]byte, error) {
	req, err := http.NewRequestWithContext(
		ctx, method, c.BaseURL+path, bytes.NewReader(body),
	)
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/octet-stream")
	if c.APIKey != "" {
		req.Header.Set("X-Api-Key", c.APIKey)
	}

	client := c.HTTP
	if client == nil {
		client = &http.Client{Timeout: 10 * time.Second}
	}
	resp, err := client.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	responseBody, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return nil, fmt.Errorf("fingerprint service returned HTTP %d: %s",
			resp.StatusCode, string(responseBody))
	}
	return responseBody, nil
}

func (c *Client) EnrollRaw(ctx context.Context, userID string, capture []byte) (*EnrollResponse, error) {
	if len(capture) != 18432 {
		return nil, fmt.Errorf("capture size must be 18432 bytes, got %d", len(capture))
	}
	body, err := c.do(ctx, http.MethodPost,
		"/api/v1/fingerprints/enroll/raw?externalUserId="+url.QueryEscape(userID), capture)
	if err != nil {
		return nil, err
	}
	var result EnrollResponse
	if err := json.Unmarshal(body, &result); err != nil {
		return nil, err
	}
	return &result, nil
}

func (c *Client) Identify(ctx context.Context, capture []byte) (*IdentifyResponse, error) {
	if len(capture) != 18432 {
		return nil, fmt.Errorf("capture size must be 18432 bytes, got %d", len(capture))
	}
	body, err := c.do(ctx, http.MethodPost,
		"/api/v1/fingerprints/identify", capture)
	if err != nil {
		return nil, err
	}
	var result IdentifyResponse
	if err := json.Unmarshal(body, &result); err != nil {
		return nil, err
	}
	return &result, nil
}
```

Untuk production, encode `userID` dengan `url.QueryEscape` atau gunakan
`url.Values` saat membangun query string, tambahkan request ID, dan bedakan
error transport dari error HTTP. Jangan melakukan retry otomatis pada
enrollment tanpa idempotency strategy karena retry dapat membuat template
tambahan. Untuk identification, retry terbatas dengan exponential backoff
dapat digunakan setelah memastikan error bukan `400` atau `401`.

Contoh penggunaan:

```go
ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
defer cancel()

client := &fingerprint.Client{
	BaseURL: "http://localhost:5001",
		APIKey:  os.Getenv("FINGERPRINT_API_KEY"),
}

result, err := client.Identify(ctx, captureBytes)
if err != nil {
	// Jangan catat captureBytes ke log; tandai perangkat/error untuk retry.
	return err
}
if !result.Matched || result.ExternalUserID == nil {
	// Tampilkan "sidik jari tidak dikenali"; jangan membuat absensi.
	return nil
}

// Gunakan *result.ExternalUserID untuk lookup user dan aturan absensi.
```

## Pola integrasi sistem absensi

### Enrollment pegawai/siswa

1. Admin membuat user pada database aplikasi dan memperoleh UUID.
2. Pastikan user aktif dan memiliki tipe/role yang boleh memakai absensi.
3. Minta user melakukan capture satu atau beberapa kali.
4. Panggil `POST /api/v1/fingerprints/enroll/raw` untuk setiap capture yang
   disetujui.
5. Simpan `fingerprintId` sebagai referensi audit opsional, bukan sebagai
   identitas utama aplikasi.
6. Jangan menganggap response enrollment sebagai bukti bahwa user hadir.

### Clock-in dan clock-out

1. Perangkat mengirim capture ke backend absensi.
2. Backend memanggil `POST /api/v1/fingerprints/identify`.
3. Jika `matched=false`, catat kegagalan teknis/UX tanpa membuat
   `attendance_record`.
4. Jika matched, lookup `externalUserId` pada database aplikasi dan pastikan
   user aktif serta perangkat diizinkan.
5. Buat event `check_in` atau `check_out` dengan waktu server, ID perangkat,
   dan ID fingerprint service bila diperlukan.
6. Terapkan aturan jadwal, toleransi keterlambatan, timezone, dan duplikasi
   di database aplikasi—bukan di fingerprint service.

Untuk desain domain yang lebih lengkap, [docs/NEW_SCHEMA.md](./docs/NEW_SCHEMA.md)
menyediakan konsep `devices`, `biometric_identities`, `attendance_events`,
`attendance_records`, dan `attendance_corrections`. Service fingerprint tidak
menentukan status `hadir`, `terlambat`, `izin`, `sakit`, atau `alpa`; semua
status tersebut adalah tanggung jawab sistem absensi.

### Idempotensi dan offline device

Gunakan `deviceId` dan `eventId` buatan perangkat/aplikasi untuk mencegah
event ganda ketika device mengirim ulang request. Simpan `occurredAt` dari
device hanya bila kebijakan sistem mengizinkannya, tetapi gunakan waktu
server (`receivedAt`) untuk audit dan validasi penyimpangan jam. Queue event
offline di sisi aplikasi/perangkat dan kirim ulang secara berurutan; jangan
menyimpan capture lebih lama dari yang diperlukan.

## Batas tanggung jawab

Fingerprint service bertanggung jawab atas validasi input biometrik,
enrollment, matching, dan relasi `externalUserId`. Sistem yang
mengintegrasikannya bertanggung jawab atas:

- autentikasi dan otorisasi operator/user;
- profil pegawai/siswa dan status aktif;
- registrasi perangkat dan lokasi;
- aturan jadwal dan status absensi;
- deduplikasi, koreksi, audit bisnis, dan retensi data;
- proteksi jaringan, secret, dan data biometrik.
