# Fingerprint Service

Standalone fingerprint enrollment and identification service built on
**ASP.NET Core (.NET 8)** with **SourceAFIS** as the recognition engine and
**MySQL (`limen_db`)** as the biometric template store.

It is an **independent service**. It owns its own API contract, configuration,
and database. It does **not** store or access application data (names,
employee codes, classes, attendance). Application data lives in the
application server's own database (`limen_db`) and is linked to the
fingerprint service only through the string `externalUserId`.

---

## 1. Database ownership

```
MySQL Server
└── limen_db    owned by server (unified)
    ├── users, identifications (server)
    ├── fingerprint_subjects    (external_user_id -> application user id)
    └── fingerprint_templates   (serialized SourceAFIS template)
```

- The service only queries `fingerprint_subjects` + `fingerprint_templates` in `limen_db`.
- No cross-table FK between application tables and fingerprint tables.
- The service never stores 
ame`, `employee_code`, class, schedule, or
  attendance.

Schema: `../server/database/schema.sql` (single source of truth, owned by server)
Tables: `users`, `identifications` (server) + `fingerprint_subjects`, `fingerprint_templates` (this service)

```
fingerprint_subjects
  id BIGINT PK
  external_user_id VARCHAR(100) UNIQUE
  created_at

fingerprint_templates
  id BIGINT PK
  subject_id BIGINT FK -> fingerprint_subjects(id) ON DELETE CASCADE
  template LONGBLOB
  created_at
```

---

## 2. Architecture

```
Client (R503 / app / gateway)
        │ HTTP/HTTPS
        ▼
Fingerprint Service
  Controllers -> Application -> Recognition/Imaging -> Repository
        │ SQL
        ▼
limen_db
```

Layers: `Controllers` (API) → `Application` (use cases) → `Recognition`
(SourceAFIS) / `Imaging` (R503) → `Data` (repository) → `limen_db`.
Plus `Diagnostics`, `Configuration`, `Errors`, `Health`, `Middleware`.

---

## 3. Fingerprint pipeline (unchanged)

```
R503 packed image 18,432 bytes
  -> nibble unpack -> 192x192 grayscale 36,864 bytes (nibble * 17)
  -> FingerprintImage(192,192,Dpi)
  -> FingerprintTemplate.ToByteArray()
  -> LONGBLOB
```

Protocol constants: width 192, height 192, 36,864 pixels, 18,432 packed bytes,

ibble * 17`. The serialized template format is unchanged, so existing
templates in `limen_db` remain readable. Templates are never regenerated
by this service.

---

## 4. API

All user references are `externalUserId` (a string). The service never accepts
or returns name/employee code.

| Method | Route | Body | Description |
| --- | --- | --- | --- |
| `POST` | `/api/v1/fingerprints/enroll` | JSON `{ externalUserId, template }` | Enroll from Base64 template |
| `POST` | `/api/v1/fingerprints/enroll/raw?externalUserId=...` | `octet-stream` | Enroll raw R503 image |
| `POST` | `/api/v1/fingerprints/identify` | `octet-stream` | Identify raw R503 image |
| `POST` | `/api/v1/fingerprints/diagnostics/capture` | `octet-stream` | Diagnostic decode (PGM only if enabled) |
| `GET` | `/api/v1/health` | — | Versioned health summary |
| `GET` | `/health`, `/health/live`, `/health/ready` | — | Liveness / readiness |

### Legacy (backward compatible)

| Method | Route | Notes |
| --- | --- | --- |
| `POST` | `/identification` | Same as v1 identify; 404 when no subjects enrolled |
| `POST` | `/enrollment` | Base64 template; resolves `externalUserId`, else falls back to legacy `employeeCode` |
| `POST` | `/fingerprint/template` | Returns Base64 template (legacy/testing) |
| `POST` | `/fingerprint/raw` | Diagnostic decode |
| `POST` | `/fingerprint/match-test` | Compare two Base64 templates |

### Identification response

```json
{
  "success": true,
  "matched": true,
  "score": 57.97,
  "candidateCount": 10,
  "externalUserId": "123",
  "fingerprintId": 456
}
```

Not matched (normal business result, HTTP 200):

```json
{ "success": true, "matched": false, "score": 12.34, "candidateCount": 10 }
```

The service deliberately does **not** return name/employeeCode. The application
server resolves `externalUserId` against `limen_db.users`.

Errors use `{ "success": false, "error": { "code": "...", "message": "..." } }`.

---

## 5. Enrollment

1. Receive `externalUserId` (+ template or raw R503 image).
2. Validate `externalUserId` non-empty, ≤ 100 chars.
3. Obtain a SourceAFIS template (generate from raw image, or decode Base64).
4. Atomically ensure `fingerprint_subjects` exists and insert the template.
5. Return `subjectId`, `fingerprintId`, `externalUserId`, `templateSize`.

Re-enrolling the same `externalUserId` adds another template to the same
subject (a subject may have multiple templates).

---

## 6. Identification

1. Validate R503 size (exactly 18,432 bytes).
2. Build the probe template.
3. Load all candidates from `fingerprint_subjects` + `fingerprint_templates`.
4. Score each, keep the highest, apply `Fingerprint:MatchThreshold`.
5. Return `externalUserId` + `fingerprintId` when matched.

No enrolled subjects → `matched:false`, `candidateCount:0`,
`reason:"NO_ENROLLED_TEMPLATES"`. No global in-memory state; requests are
independent.

---

## 7. Configuration

`appsettings.json` (no secrets):

```json
{
  "ConnectionStrings": { "MySql": "" },
  "Fingerprint": {
    "Dpi": 508,
    "MatchThreshold": 40,
    "Diagnostics": { "SavePgm": false, "Directory": "./diagnostic-captures" }
  },
  "Server": { "MaxRequestBodySizeBytes": 65536 },
  "Cors": { "AllowedOrigins": [] },
  "Urls": "http://0.0.0.0:5001"
}
```

The connection string **must point to `limen_db`** and use a
`limen_user` with access only to that database:

```
ConnectionStrings__MySql=Server=...;Port=3306;Database=limen_db;User ID=limen_user;Password=...;
```

Use the same `limen_db` credentials as server; keep identification enrichment in server (HTTP gateway).

### Environment variable mapping

| Key | Environment variable |
| --- | --- |
| `ConnectionStrings:MySql` | `ConnectionStrings__MySql` |
| `Fingerprint:Dpi` | `Fingerprint__Dpi` |
| `Fingerprint:MatchThreshold` | `Fingerprint__MatchThreshold` |
| `Fingerprint:Diagnostics:SavePgm` | `Fingerprint__Diagnostics__SavePgm` |
| `Fingerprint:Diagnostics:Directory` | `Fingerprint__Diagnostics__Directory` |
| `Server:MaxRequestBodySizeBytes` | `Server__MaxRequestBodySizeBytes` |
| `Cors:AllowedOrigins:0` | `Cors__AllowedOrigins__0` |
| listen URLs | `ASPNETCORE_URLS` |
| environment | `ASPNETCORE_ENVIRONMENT` |

---

## 8. Security, logging, diagnostics, reliability

- Centralized error contract; no stack traces/SQL/paths exposed.
- Request body limits, content-type + exact-size validation.
- CORS disabled unless `Cors:AllowedOrigins` is set.
- Structured logs with `X-Request-Id`; no biometric data, templates, or secrets
  logged.
- Diagnostic `.pgm` writing is **off by default** and must remain off in
  production. Never commit `.pgm` files.
- Async DB access with `CancellationToken`; graceful shutdown (30s).
- Auth/TLS are **not** implemented here and must be added at the reverse proxy
  or as a configurable scheme before public exposure.
- Threshold/DPI are development defaults and must be calibrated.

---

## 9. Testing

```bash
dotnet test ../FingerprintService.sln
```

Unit + integration tests cover R503 decoding/validation, threshold evaluation,
enrollment/identification logic, health, error contracts, and
**architecture guard tests** that fail if cross-database coupling is
reintroduced (server must not query fingerprint tables; the fingerprint
service must not query application data).

---

## 10. Breaking changes vs. the shared-database version

- Enrollment now uses `externalUserId`; 
ame`/`employeeCode` are not stored.
- Identification returns `externalUserId` + `fingerprintId` instead of
  `userId`/
ame`/`employeeCode`.
- The service no longer reads a `users` table; it uses `fingerprint_subjects` + `fingerprint_templates` in `limen_db`.
- Legacy `/enrollment` still accepts the old JSON shape but bridges
  `employeeCode` → `externalUserId` and ignores 
ame`.

---

## 11. Deployment

```
Reverse Proxy / Load Balancer (HTTPS, auth)
        │
        ▼
Fingerprint Service (:8080 in Docker)
        │
        ▼
limen_db on MySQL (same instance as server)
```

Docker:

```bash
docker build -t fingerprint-service ./fingerprint-service
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__MySql="Server=mysql;Port=3306;Database=limen_db;User ID=limen_user;Password=...;" \
  fingerprint-service
```

Because the service is decoupled, it can be moved to a separate server with only
its own connection string changed.
