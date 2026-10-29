# Express API

API Node.js/Express yang menjadi pintu masuk aplikasi. Service ini mengelola
data user pada PostgreSQL dan menjadi gateway ke fingerprint service .NET.
Client aplikasi biasanya berbicara ke Express, sedangkan Express meneruskan
operasi biometrik ke `FINGERPRINT_SERVICE_URL`.

## Tanggung jawab service

- Menyediakan CRUD minimum untuk user.
- Memvalidasi dan meneruskan capture/template fingerprint ke service .NET.
- Menambahkan `fullName` dan `email` ke hasil identification yang matched.
- Menyimpan hasil identification terakhir ke memory, tabel `identifications`,
  dan menyiarkannya melalui Server-Sent Events (SSE).
- Menyediakan health check yang tidak memerlukan database.

## Prasyarat

- Node.js 20 atau lebih baru (Docker image memakai Node 20 Alpine).
- PostgreSQL dengan database `limen_db`.
- Jalankan [schema.sql](./database/schema.sql) dan, bila diperlukan,
  [grants.sql](./database/grants.sql).
- Service fingerprint .NET aktif dan dapat dijangkau dari Express.

## Struktur direktori

| Direktori/file | Fungsi |
|---|---|
| `server.js` | Memuat environment dan menjalankan HTTP server. |
| `src/app.js` | Middleware CORS/body parser, API key gate, health, dan routing. |
| `src/routes/` | Route user dan fingerprint. |
| `src/controllers/` | Validasi request, proxy, dan bentuk response. |
| `src/services/userService.js` | Query data user. |
| `src/services/identificationRepository.js` | Audit identification ke PostgreSQL. |
| `src/services/identificationStore.js` | Latest result in-memory dan orkestrasi persistence/SSE. |
| `src/services/identificationEvents.js` | Registry client SSE dan broadcast. |
| `src/db/postgres.js` | Pool PostgreSQL dan validasi environment database. |
| `database/` | Schema dan grants PostgreSQL. |

## Instalasi dan konfigurasi

```powershell
npm ci
Copy-Item .env.example .env
```

Isi `.env` dengan nilai nyata. Jangan commit `.env`.

| Variable | Default | Keterangan |
|---|---|---|
| `PORT` | `3000` | Port HTTP Express. |
| `DB_HOST` | - | Host PostgreSQL. |
| `DB_PORT` | - | Port PostgreSQL, umumnya `5432`. |
| `DB_USER` | - | User database. |
| `DB_PASSWORD` | - | Password database. Wajib kecuali `ALLOW_MISSING_DB=1`. |
| `DB_NAME` | - | Nama database, umumnya `limen_db`. |
| `FINGERPRINT_SERVICE_URL` | `http://localhost:5001` | Base URL service .NET. In Docker Compose use `http://fingerprint-service:8080`. |
| `FINGERPRINT_API_KEY` | kosong | API key yang diteruskan ke service .NET sebagai `X-Api-Key`. |
| `ApiKey__Value` | kosong | Alias fallback untuk API key fingerprint. |
| `API_KEY` / `EXPRESS_API_KEY` | kosong | Jika diisi, client wajib mengirim `X-Api-Key` pada `/api/*`. |
| `ALLOWED_ORIGINS` | kosong | Daftar origin CORS dipisahkan koma. |
| `NODE_ENV` | - | Pada `production` tanpa origin, CORS ditolak. |
| `ALLOW_MISSING_DB` | `0` | Hanya untuk skenario khusus; jangan gunakan untuk operasi API database. |

Contoh `.env` lokal:

```dotenv
PORT=3000
DB_HOST=localhost
DB_PORT=5432
DB_USER=limen_user
DB_PASSWORD=<secret>
DB_NAME=limen_db
FINGERPRINT_SERVICE_URL=http://localhost:5001
ALLOWED_ORIGINS=http://localhost:5173
```

Service gagal start jika konfigurasi database wajib tidak lengkap. Pool
menggunakan maksimum 20 koneksi, connection timeout 5 detik, dan idle timeout
30 detik.

## Menyiapkan database

```powershell
psql -h localhost -U postgres -d limen_db -f database/schema.sql
psql -h localhost -U postgres -d limen_db -f database/grants.sql
```

Schema yang sama dipakai service Express dan .NET. Tabel utama:

- `users`: identitas aplikasi.
- `fingerprint_subjects` dan `fingerprint_templates`: data fingerprint.
- `identifications`: audit payload hasil identification.

## Menjalankan

```powershell
npm start
```

Dengan Docker:

```powershell
docker build -t express-api .
docker run --rm -p 3000:3000 --env-file .env express-api
```

Jika Express dan fingerprint service berjalan dalam Docker Compose, jalankan
dari direktori `server`:

```powershell
Copy-Item .env.example .env
Copy-Item ..\.env.example ..\.env
# Isi DB_* di server/express/.env, POSTGRES_CONNECTION_STRING dan DB_HOST
# di server/.env. DB_HOST harus menunjuk ke PostgreSQL yang sama dengan
# fingerprint service; jangan gunakan localhost dari dalam container.
docker compose up --build
```

Compose otomatis memakai `http://fingerprint-service:8080` untuk komunikasi
antar-container. Dari host, API Express tersedia di
`http://localhost:3000`, sedangkan fingerprint service di
`http://localhost:5001`.

Secara default Compose memakai `host.docker.internal` untuk `DB_HOST`, karena
PostgreSQL pada contoh ini berjalan di host Docker. Jika PostgreSQL berjalan di
container atau host lain, ubah `DB_HOST` di `server/.env` ke hostname jaringan
yang sama dan pastikan `POSTGRES_CONNECTION_STRING` menggunakan database
tersebut juga.

Health check container memanggil `GET /health`.

## Endpoint

### Health

`GET /health` tidak memerlukan API key dan tidak memerlukan database:

```powershell
curl.exe http://localhost:3000/health
```

Response:

```json
{ "success": true, "service": "express-api" }
```

### User API

#### `POST /api/users`

Membuat user. `passwordHash` harus sudah berupa hash dari layer autentikasi;
Express tidak melakukan hashing di endpoint ini.

```powershell
curl.exe -X POST http://localhost:3000/api/users `
  -H "Content-Type: application/json" `
  -d '{\"email\":\"user@example.com\",\"passwordHash\":\"<password-hash>\",\"fullName\":\"User Example\"}'
```

Field wajib: `email`, `passwordHash`, `fullName`. Email duplikat menghasilkan
HTTP 409.

#### `GET /api/users`

Mengembalikan daftar user tanpa `password_hash`.

#### `GET /api/users/:id`

Mengambil satu user berdasarkan UUID. User tidak ditemukan menghasilkan HTTP
404.

### Fingerprint gateway

Express membatasi body fingerprint ke 18.432 byte dan meneruskan request ke
service .NET. Header correlation (`X-Request-Id`, `X-Correlation-Id`, atau
`X-Trace-Id`) diteruskan jika tersedia.

#### `POST /api/fingerprint/enroll`

Body JSON:

```json
{
  "externalUserId": "<users-uuid>",
  "template": "<base64-template>"
}
```

#### `POST /api/fingerprint/enroll/raw?externalUserId=<uuid>`

Kirim capture R503 sebagai `application/octet-stream` dan body tepat 18.432
byte:

```powershell
curl.exe -X POST "http://localhost:3000/api/fingerprint/enroll/raw?externalUserId=<users-uuid>" `
  -H "Content-Type: application/octet-stream" `
  --data-binary "@capture.r503"
```

#### `POST /api/fingerprint/identify`

Kirim capture R503 sebagai `application/octet-stream`:

```powershell
curl.exe -X POST http://localhost:3000/api/fingerprint/identify `
  -H "Content-Type: application/octet-stream" `
  --data-binary "@capture.r503"
```

Jika hasil matched memiliki `externalUserId`, Express mencoba mengambil data
user dan menambahkan `fullName` serta `email`. Hasil kemudian disimpan ke tabel
`identifications` dan cache latest.

#### `GET /api/fingerprint/latest`

Mengembalikan hasil identification terakhir. Setelah restart, Express
fallback ke baris terakhir tabel `identifications`; jika belum ada hasil,
`data` bernilai `null`.

#### `GET /api/fingerprint/stream`

Membuka SSE stream. Client menerima event terakhir segera bila tersedia, lalu
event baru setiap kali identification selesai.

Contoh browser:

```javascript
const events = new EventSource("http://localhost:3000/api/fingerprint/stream");
events.onmessage = (event) => console.log(JSON.parse(event.data));
```

Komentar `: connected` dan `: keepalive` bukan event data. Keepalive dikirim
setiap 25 detik.

## API key dan CORS

Jika `API_KEY` atau `EXPRESS_API_KEY` diisi, semua route `/api/*` harus
mengirim:

```text
X-Api-Key: <api-key>
```

`/health` tetap terbuka untuk load balancer. Untuk browser, set
`ALLOWED_ORIGINS` secara eksplisit, misalnya
`http://localhost:5173,https://app.example.com`. Pada `NODE_ENV=production`
dan origin kosong, request CORS ditolak.

`FINGERPRINT_API_KEY` adalah key berbeda secara konsep: key tersebut dipakai
Express saat memanggil .NET. Nilainya harus sama dengan `ApiKey:Value` pada
service .NET bila gate .NET diaktifkan.

## Error dan troubleshooting

- HTTP 400 dari gateway biasanya berarti body bukan `Buffer`,
  `Content-Type` salah, field JSON kosong, atau ukuran R503 bukan 18.432 byte.
- HTTP 401 berarti API key Express atau downstream tidak ada/tidak cocok.
- HTTP 413 berarti body melewati limit 64 KB untuk binary atau 100 KB untuk JSON.
- HTTP 500 dengan pesan tidak dapat terhubung berarti `FINGERPRINT_SERVICE_URL`
  tidak dapat dijangkau; periksa DNS/port/container network.
- Jika user API gagal start, periksa seluruh `DB_*` dan konektivitas PostgreSQL.
- Kegagalan menyimpan audit identification dicatat sebagai warning dan tidak
  menggagalkan response fingerprint; periksa log dan koneksi database.

## Pengembangan dan deployment

```powershell
npm start
```

Script `test` saat ini masih placeholder dan selalu gagal dengan pesan
“no test specified”; tambahkan test runner sebelum menjadikan test sebagai gate
CI. Dalam deployment production, gunakan secret manager, batasi CORS, aktifkan
API key, tempatkan service .NET di private network, dan jangan mengekspos
PostgreSQL ke publik.
