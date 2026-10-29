# Fingerprint Service (.NET)

Service ASP.NET Core 8 untuk mengubah citra fingerprint R503 menjadi template
SourceAFIS, menyimpan template ke PostgreSQL, melakukan enrollment, dan
mengidentifikasi fingerprint. Service ini hanya menyimpan relasi
`externalUserId` (UUID dari tabel `users`) dan data biometrik; data aplikasi
seperti nama, email, atau employee code tidak diproses atau dikembalikan.

## Tanggung jawab service

- Menerima citra R503 packed 4-bit berukuran tepat **18.432 byte**.
- Mengubah citra menjadi grayscale 192 x 192, lalu membuat template SourceAFIS.
- Menyimpan maksimal tiga template per subject secara default.
- Mencocokkan probe terhadap seluruh template yang terdaftar.
- Menyediakan API JSON, endpoint compatibility lama, health check, dan
  diagnostics yang dinonaktifkan secara default.

## Prasyarat

- .NET SDK 8.
- PostgreSQL yang memiliki database `limen_db`.
- Schema database pada [../express/database/schema.sql](../express/database/schema.sql)
  sudah diterapkan. Role/grant dapat memakai
  [../express/database/grants.sql](../express/database/grants.sql).
- `externalUserId` harus berupa UUID yang sudah ada pada tabel `users`.

## Struktur direktori

| Direktori/file | Fungsi |
|---|---|
| `Program.cs` | Dependency injection, middleware, routing, Swagger, health check, dan limit request. |
| `Controllers/` | Endpoint versioned, compatibility lama, dan health. |
| `Application/` | Use case enrollment dan identification. |
| `Recognition/` | Integrasi SourceAFIS. |
| `Imaging/` | Validasi dan unpack format R503. |
| `Data/` | Repository PostgreSQL untuk subject/template fingerprint. |
| `Configuration/` | Opsi `Fingerprint`, `Server`, dan `Cors`. |
| `Middleware/` | API key opsional dan correlation/request ID. |
| `Diagnostics/` | Penyimpanan capture `.pgm` saat diaktifkan eksplisit. |
| `Contracts/` | Bentuk request dan response API. |

## Konfigurasi

Konfigurasi lokal tersedia di [appsettings.json](./appsettings.json).
Untuk production, gunakan environment variable atau secret manager, bukan
menyimpan password di file.

Contoh environment variable PowerShell:

```powershell
$env:ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=limen_db;Username=limen_user;Password=<secret>;Timeout=5;Command Timeout=5;Maximum Pool Size=20"
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:ApiKey__Value="<shared-api-key>" # opsional
```

| Key | Default | Keterangan |
|---|---:|---|
| `ConnectionStrings__Postgres` | - | Connection string PostgreSQL, wajib saat endpoint database digunakan. |
| `Fingerprint:Dpi` | `508` | DPI sensor untuk SourceAFIS. Harus sesuai karakteristik sensor. |
| `Fingerprint:MatchThreshold` | `40` | Batas score agar dianggap cocok; kalibrasikan dengan data nyata sebelum production. |
| `Fingerprint:MaxTemplatesPerSubject` | `3` | Template maksimum per user; template tertua dihapus saat melebihi limit. |
| `Fingerprint:Diagnostics:SavePgm` | `false` | Menulis capture `.pgm`; jangan aktifkan di production karena berisi biometrik. |
| `Fingerprint:Diagnostics:Directory` | `./diagnostic-captures` | Direktori output diagnostics, relatif terhadap content root. |
| `Server:MaxRequestBodySizeBytes` | `65536` | Limit body HTTP global. |
| `Cors:AllowedOrigins` | `[]` | Origin CORS eksplisit; kosong berarti middleware CORS tidak didaftarkan. |
| `ApiKey:Value` | kosong | Jika diisi, semua `/api/*` harus mengirim `X-Api-Key`. Endpoint health dikecualikan. |
| `Urls` | `http://0.0.0.0:8080` | URL binding default; saat development gunakan `dotnet run --urls http://localhost:5001`. |

## Menjalankan lokal

```powershell
dotnet restore
dotnet run --urls http://localhost:5001
```

Swagger hanya tersedia pada environment `Development` di
`http://localhost:5001/swagger`.

## Menjalankan dengan Docker

Build dan jalankan dari direktori ini:

```powershell
docker build -t fingerprint-service .
docker run --rm -p 5001:8080 `
  -e ConnectionStrings__Postgres="Host=host.docker.internal;Port=5432;Database=limen_db;Username=limen_user;Password=<secret>" `
  fingerprint-service
```

Container listen pada port 8080 dan memakai user non-root. Port `5001` hanya
port host; antar-container gunakan `http://fingerprint-service:8080`.
Health check image memanggil `/health/live`.

## Endpoint utama

Semua endpoint menerima `Content-Type: application/octet-stream` untuk body
binary, kecuali enrollment berbasis template.

### `POST /api/v1/fingerprints/enroll`

Menyimpan template SourceAFIS yang sudah diencode Base64.

```powershell
curl.exe -X POST http://localhost:5001/api/v1/fingerprints/enroll `
  -H "Content-Type: application/json" `
  -d '{\"externalUserId\":\"<users-uuid>\",\"template\":\"<base64-template>\"}'
```

Response sukses:

```json
{
  "success": true,
  "subjectId": 1,
  "fingerprintId": 1,
  "externalUserId": "<users-uuid>",
  "templateSize": 1234
}
```

### `POST /api/v1/fingerprints/enroll/raw?externalUserId=<uuid>`

Pilihan enrollment yang disarankan. Kirim tepat 18.432 byte R503; template
dibuat di service sehingga template tidak perlu melewati caller.

```powershell
curl.exe -X POST "http://localhost:5001/api/v1/fingerprints/enroll/raw?externalUserId=<users-uuid>" `
  -H "Content-Type: application/octet-stream" `
  --data-binary "@capture.r503"
```

### `POST /api/v1/fingerprints/identify`

Mencocokkan capture R503 terhadap semua template.

```powershell
curl.exe -X POST http://localhost:5001/api/v1/fingerprints/identify `
  -H "Content-Type: application/octet-stream" `
  --data-binary "@capture.r503"
```

Response dapat berisi `matched: false`, atau saat cocok berisi
`externalUserId` dan `fingerprintId`. `score` dibandingkan dengan
`MatchThreshold`; `candidateCount` menunjukkan jumlah template yang diperiksa.

### `POST /api/v1/fingerprints/diagnostics/capture`

Mengembalikan ukuran/statistik grayscale dan secara opsional menyimpan `.pgm`.
Di luar Development, endpoint mengembalikan 404 jika `SavePgm` tidak aktif.
Endpoint ini hanya untuk debugging terkontrol.

## Endpoint compatibility dan utilitas

Endpoint berikut dipertahankan untuk integrasi lama:

| Method dan path | Kegunaan |
|---|---|
| `POST /enrollment` | Enrollment JSON lama; tetap membutuhkan `externalUserId`. `name` dan `employeeCode` tidak disimpan. |
| `POST /identification` | Identification binary lama dengan response berbeda; gunakan endpoint versioned untuk integrasi baru. |
| `POST /fingerprint/raw` | Validasi/unpack capture dan diagnostics lama. |
| `POST /fingerprint/template` | Membuat template Base64 dari capture R503. |
| `POST /fingerprint/match-test` | Membandingkan dua template Base64 untuk pengujian. |

## Health check

| Endpoint | Arti |
|---|---|
| `GET /health` | Liveness compatibility. |
| `GET /health/live` | Proses hidup; dipakai Docker. |
| `GET /health/ready` | Readiness, termasuk koneksi PostgreSQL; 503 jika belum siap. |
| `GET /api/v1/health` | Ringkasan health versioned. |

## Error, keamanan, dan operasional

- Request invalid memakai response error JSON dengan `code`, `message`, dan
  request/correlation ID sesuai handler.
- API key bersifat opsional, tetapi wajib digunakan bila service diakses melalui
  jaringan yang tidak sepenuhnya terpercaya.
- Jangan menaruh password PostgreSQL atau `ApiKey` di repository/log.
- Jangan mengaktifkan diagnostics secara permanen; file `.pgm` adalah data
  biometrik sensitif dan direktori tersebut tidak boleh diserve secara publik.
- `MatchThreshold` default adalah nilai development, bukan jaminan akurasi
  produksi. Kalibrasikan false accept/false reject menggunakan dataset sensor
  yang representatif.
- Identification membaca seluruh kandidat dari database. Untuk skala besar,
  evaluasi strategi indexing/caching sebelum menaikkan volume produksi.

## Pengujian dan build

```powershell
dotnet build
dotnet run --environment Development
```

Uji endpoint health terlebih dahulu, lalu enrollment dan identification dengan
capture R503 nyata. Pastikan database, `externalUserId`, ukuran payload, dan
`Content-Type` benar sebelum mendiagnosis masalah SourceAFIS.
