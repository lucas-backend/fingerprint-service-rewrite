CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TYPE "user_status" AS ENUM ('active', 'inactive');
CREATE TYPE "subject_type" AS ENUM ('student', 'teacher');
CREATE TYPE "attendance_status" AS ENUM ('hadir', 'terlambat', 'izin', 'sakit', 'alpa');
CREATE TYPE "attendance_source" AS ENUM ('device', 'manual', 'correction');
CREATE TYPE "attendance_event_type" AS ENUM ('check_in', 'check_out');
CREATE TYPE "correction_status" AS ENUM ('pending', 'approved', 'rejected');
CREATE TYPE "schedule_kind" AS ENUM ('pelajaran', 'ujian', 'tryout');
CREATE TYPE "question_type" AS ENUM ('pilihan_ganda', 'pilihan_ganda_kompleks', 'benar_salah', 'rentang_nilai', 'essay');
CREATE TYPE "question_bank_scope" AS ENUM ('latihan', 'ujian');
CREATE TYPE "quiz_attempt_status" AS ENUM ('in_progress', 'submitted', 'graded');

CREATE TABLE "users" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "email" citext UNIQUE NOT NULL,
  "password_hash" text NOT NULL,
  "full_name" text NOT NULL,
  "status" user_status NOT NULL DEFAULT 'active',
  "last_login_at" timestamptz,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "roles" (
  "code" text PRIMARY KEY,
  "name" text NOT NULL
);

CREATE TABLE "user_roles" (
  "user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "role_code" text NOT NULL REFERENCES "roles" ("code") DEFERRABLE INITIALLY IMMEDIATE,
  "granted_at" timestamptz NOT NULL DEFAULT (now()),
  "granted_by" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  PRIMARY KEY ("user_id", "role_code")
);

CREATE TABLE "sessions" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "token_hash" text UNIQUE NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "last_seen_at" timestamptz,
  "expires_at" timestamptz NOT NULL,
  "revoked_at" timestamptz,
  "ip" inet,
  "user_agent" text
);

CREATE TABLE "classes" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "name" text NOT NULL,
  "level" text,
  "homeroom_teacher_user_id" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "students" (
  "user_id" uuid PRIMARY KEY REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "nis" text UNIQUE NOT NULL,
  "class_id" uuid REFERENCES "classes" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "teachers" (
  "user_id" uuid PRIMARY KEY REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "nip" text UNIQUE,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "subjects" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "code" text UNIQUE NOT NULL,
  "name" text NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "teaching_assignments" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "teacher_user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "subject_id" uuid NOT NULL REFERENCES "subjects" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "class_id" uuid NOT NULL REFERENCES "classes" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "schedules" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "class_id" uuid NOT NULL REFERENCES "classes" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "subject_id" uuid NOT NULL REFERENCES "subjects" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "teacher_user_id" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "day_of_week" smallint NOT NULL,
  "start_time" time NOT NULL,
  "end_time" time NOT NULL,
  "kind" schedule_kind NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "school_settings" (
  "id" smallint PRIMARY KEY,
  "first_entry_time" time NOT NULL,
  "timezone" text NOT NULL DEFAULT 'Asia/Jakarta',
  "late_tolerance_minutes" integer NOT NULL DEFAULT 0,
  "updated_by" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "devices" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "name" text NOT NULL,
  "location" text,
  "serial" text UNIQUE,
  "is_active" boolean NOT NULL DEFAULT true,
  "credential_ref" text,
  "last_seen_at" timestamptz,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "biometric_identities" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "identifier" text NOT NULL,
  "subject_user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "subject_type" subject_type NOT NULL,
  "is_active" boolean NOT NULL DEFAULT true,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "attendance_events" (
  "id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  "device_id" uuid NOT NULL REFERENCES "devices" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "identifier" text NOT NULL,
  "subject_user_id" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "event_type" attendance_event_type NOT NULL,
  "occurred_at" timestamptz NOT NULL,
  "received_at" timestamptz NOT NULL DEFAULT (now()),
  "raw_payload" jsonb
);

CREATE TABLE "attendance_records" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "subject_user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "subject_type" subject_type NOT NULL,
  "attendance_date" date NOT NULL,
  "check_in_at" timestamptz,
  "check_out_at" timestamptz,
  "status" attendance_status NOT NULL,
  "source" attendance_source NOT NULL DEFAULT 'device',
  "check_in_device_id" uuid REFERENCES "devices" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "check_out_device_id" uuid REFERENCES "devices" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "late_minutes" integer,
  "note" text,
  "created_by" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "updated_by" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "attendance_corrections" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "attendance_record_id" uuid NOT NULL REFERENCES "attendance_records" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "requested_by" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "reason" text NOT NULL,
  "old_status" attendance_status,
  "new_status" attendance_status NOT NULL,
  "status" correction_status NOT NULL DEFAULT 'pending',
  "review_note" text,
  "reviewed_by" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "reviewed_at" timestamptz,
  "created_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "audit_logs" (
  "id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  "actor_user_id" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "entity_type" text NOT NULL,
  "entity_id" text NOT NULL,
  "action" text NOT NULL,
  "before_data" jsonb,
  "after_data" jsonb,
  "created_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "notifications" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "type" text NOT NULL,
  "payload" jsonb,
  "read_at" timestamptz,
  "created_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "assessments" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "class_id" uuid NOT NULL REFERENCES "classes" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "subject_id" uuid NOT NULL REFERENCES "subjects" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "teacher_user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "kind" text NOT NULL,
  "kkm" numeric,
  "is_remedial" boolean NOT NULL DEFAULT false,
  "period" text NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "assessment_scores" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "assessment_id" uuid NOT NULL REFERENCES "assessments" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "student_user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "score" numeric NOT NULL,
  "updated_by" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "assignments" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "class_id" uuid NOT NULL REFERENCES "classes" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "subject_id" uuid NOT NULL REFERENCES "subjects" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "teacher_user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "title" text NOT NULL,
  "description" text,
  "due_at" timestamptz NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "assignment_submissions" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "assignment_id" uuid NOT NULL REFERENCES "assignments" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "student_user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "submitted_at" timestamptz,
  "score" numeric,
  "note" text,
  "graded_by" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "graded_at" timestamptz,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "question_banks" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "subject_id" uuid NOT NULL REFERENCES "subjects" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "owner_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "scope" question_bank_scope NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "questions" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "bank_id" uuid NOT NULL REFERENCES "question_banks" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "type" question_type NOT NULL,
  "body" text NOT NULL,
  "options" jsonb,
  "answer_key" jsonb,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "quizzes" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "subject_id" uuid NOT NULL REFERENCES "subjects" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "owner_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "title" text NOT NULL,
  "description" text,
  "published_at" timestamptz,
  "start_time" timestamptz,
  "end_time" timestamptz,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "quiz_questions" (
  "quiz_id" uuid NOT NULL REFERENCES "quizzes" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "question_id" uuid NOT NULL REFERENCES "questions" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "question_number" integer NOT NULL,
  PRIMARY KEY ("quiz_id", "question_id")
);

CREATE TABLE "quiz_attempts" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "quiz_id" uuid NOT NULL REFERENCES "quizzes" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "student_user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "started_at" timestamptz NOT NULL DEFAULT (now()),
  "submitted_at" timestamptz,
  "status" quiz_attempt_status NOT NULL DEFAULT 'in_progress',
  "score" numeric,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "quiz_answers" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "attempt_id" uuid NOT NULL REFERENCES "quiz_attempts" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "question_id" uuid NOT NULL REFERENCES "questions" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "answer" jsonb,
  "is_correct" boolean,
  "score" numeric,
  "feedback" text,
  "graded_by" uuid REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "graded_at" timestamptz,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "counseling_notes" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "student_user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "author_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "note" text NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "counseling_followups" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "note_id" uuid NOT NULL REFERENCES "counseling_notes" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "note" text NOT NULL,
  "created_by" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "created_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "aptitude_results" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "student_user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "result" text NOT NULL,
  "assessed_at" timestamptz NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "target_majors" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "student_user_id" uuid NOT NULL REFERENCES "users" ("id") DEFERRABLE INITIALLY IMMEDIATE,
  "major" text NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "passing_grades" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "university" text NOT NULL,
  "major" text NOT NULL,
  "score" numeric NOT NULL,
  "year" integer NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE INDEX ON "user_roles" ("user_id");
CREATE INDEX ON "sessions" ("user_id");
CREATE INDEX ON "sessions" ("expires_at");
CREATE INDEX ON "classes" ("homeroom_teacher_user_id");
CREATE UNIQUE INDEX ON "teaching_assignments" ("teacher_user_id", "subject_id", "class_id");
CREATE INDEX ON "schedules" ("class_id", "day_of_week");
CREATE UNIQUE INDEX ON "biometric_identities" ("identifier");
CREATE INDEX ON "biometric_identities" ("subject_user_id");
CREATE UNIQUE INDEX ON "attendance_events" ("device_id", "identifier", "occurred_at", "event_type");
CREATE INDEX ON "attendance_events" ("occurred_at");
CREATE INDEX ON "attendance_events" ("subject_user_id");
CREATE UNIQUE INDEX ON "attendance_records" ("subject_user_id", "attendance_date");
CREATE INDEX ON "attendance_records" ("attendance_date", "status");
CREATE INDEX ON "attendance_records" ("status");
CREATE INDEX ON "attendance_corrections" ("attendance_record_id");
CREATE INDEX ON "attendance_corrections" ("status");
CREATE INDEX ON "audit_logs" ("entity_type", "entity_id");
CREATE INDEX ON "audit_logs" ("created_at");
CREATE INDEX ON "notifications" ("user_id", "read_at");
CREATE INDEX ON "assessments" ("class_id", "subject_id", "period");
CREATE UNIQUE INDEX ON "assessment_scores" ("assessment_id", "student_user_id");
CREATE INDEX ON "assignments" ("class_id", "subject_id");
CREATE UNIQUE INDEX ON "assignment_submissions" ("assignment_id", "student_user_id");
CREATE INDEX ON "questions" ("bank_id");
CREATE UNIQUE INDEX ON "quiz_questions" ("quiz_id", "question_number");
CREATE INDEX ON "quiz_attempts" ("quiz_id", "student_user_id");
CREATE UNIQUE INDEX ON "quiz_answers" ("attempt_id", "question_id");
CREATE INDEX ON "counseling_notes" ("student_user_id");
CREATE INDEX ON "counseling_followups" ("note_id");
CREATE INDEX ON "aptitude_results" ("student_user_id");
CREATE INDEX ON "target_majors" ("student_user_id");
CREATE INDEX ON "passing_grades" ("university", "major", "year");

## Integrasi fingerprint

Layanan .NET dan Express menggunakan database PostgreSQL yang sama. Tabel
fingerprint menjadi ekstensi dari skema ini dan menghubungkan setiap template
ke akun pada tabel `users`, bukan menyimpan identitas pengguna terpisah:

```sql
CREATE TABLE "fingerprint_subjects" (
  "id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  "user_id" uuid NOT NULL UNIQUE REFERENCES "users" ("id") ON DELETE CASCADE,
  "created_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "fingerprint_templates" (
  "id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  "subject_id" bigint NOT NULL REFERENCES "fingerprint_subjects" ("id") ON DELETE CASCADE,
  "template" bytea NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now())
);
```

`fingerprint_subjects.user_id` adalah relasi 1:1 ke `users.id`. Satu subject
dapat memiliki beberapa template pada `fingerprint_templates`; batas jumlah
template diterapkan oleh layanan fingerprint.

COMMENT ON TABLE "users" IS 'BR-01, FR-AUTH-001, DR-001. Confirmed (kebutuhan).';
COMMENT ON COLUMN "users"."email" IS 'unik case-insensitive (citext atau lower() index)';
COMMENT ON COLUMN "users"."password_hash" IS 'Argon2id (DEC-05)';
COMMENT ON TABLE "roles" IS '5 peran (DEC-07). Seed idempoten. Lookup table (DB-04).';
COMMENT ON COLUMN "roles"."code" IS 'siswa, guru_mapel, guru_wali, guru_bk, admin';
COMMENT ON TABLE "user_roles" IS 'ADR-013. Peran ganda (Guru Mapel + Wali). Proposed (SDD-OI-07/DB-01).';
COMMENT ON TABLE "sessions" IS 'ADR-007. Idle 30 mnt / absolut 12 jam (DEC-05). Reset password mencabut semua sesi.';
COMMENT ON COLUMN "sessions"."token_hash" IS 'hash token sesi; nilai mentah tidak disimpan';
COMMENT ON TABLE "students" IS 'DR-002. Confirmed (kebutuhan). class_id nullable sampai penempatan.';
COMMENT ON COLUMN "students"."user_id" IS '1:1 dengan users';
COMMENT ON TABLE "teachers" IS 'DR-002; guru ikut absen (BR-13). Proposed.';
COMMENT ON COLUMN "teachers"."user_id" IS '1:1 dengan users';
COMMENT ON TABLE "classes" IS 'DR-002; wali kelas (FEAT-WALI-01). Confirmed (kebutuhan).';
COMMENT ON TABLE "subjects" IS 'DR-002. Confirmed (kebutuhan).';
COMMENT ON TABLE "teaching_assignments" IS 'Cakupan Guru Mapel (BR-14/DEC-07). Proposed.';
COMMENT ON TABLE "schedules" IS 'DR-004, FR-JADWAL-001. CHECK start_time < end_time. Bentrok jadwal = TBD (K-05). Confirmed (kebutuhan).';
COMMENT ON COLUMN "schedules"."day_of_week" IS 'CHECK 1..7';
COMMENT ON TABLE "school_settings" IS 'FR-ABS-010, DEC-02. Confirmed. Perubahan tidak menghitung ulang historis (K-04).';
COMMENT ON COLUMN "school_settings"."id" IS 'singleton; CHECK id = 1';
COMMENT ON COLUMN "school_settings"."first_entry_time" IS 'jam masuk pertama acuan keterlambatan';
COMMENT ON COLUMN "school_settings"."timezone" IS 'tidak diubah klien (BR-12)';
COMMENT ON COLUMN "school_settings"."late_tolerance_minutes" IS 'CHECK >= 0; dipakai? (K-11)';
COMMENT ON TABLE "devices" IS 'BR-11, DEC-04. Confirmed (kebutuhan); skema Proposed. Format kredensial TBD (SDD-OI-02).';
COMMENT ON COLUMN "devices"."credential_ref" IS 'referensi rahasia, bukan nilai; tidak pernah direspons';
COMMENT ON TABLE "biometric_identities" IS 'FR-ABS-006. Proposed. Global vs per-device = TBD (SDD-OI-08).';
COMMENT ON COLUMN "biometric_identities"."identifier" IS 'sidik jari; UNIQUE global (DB-02/SDD-OI-08)';
COMMENT ON TABLE "attendance_events" IS 'FR-ABS-006, ADR-014. Proposed. Retensi mentah = TBD (DR-003/DEC-11).';
COMMENT ON COLUMN "attendance_events"."id" IS 'append-only / imutabel';
COMMENT ON COLUMN "attendance_events"."subject_user_id" IS 'null bila pemetaan tidak ditemukan';
COMMENT ON TABLE "attendance_records" IS 'FR-ABS-001..013. Confirmed (kebutuhan). check_in_at null = tanda merah (BR-10).';
COMMENT ON COLUMN "attendance_records"."attendance_date" IS 'dihitung dalam WIB (Asia/Jakarta)';
COMMENT ON COLUMN "attendance_records"."late_minutes" IS 'CHECK >= 0; turunan (Proposed)';
COMMENT ON COLUMN "attendance_records"."note" IS 'Proposed (dari kontrak API-ABS-004)';
COMMENT ON TABLE "attendance_corrections" IS 'FR-ABS-004, BR-07, DEC-03. Proposed. pending tidak mengubah rekap resmi.';
COMMENT ON COLUMN "attendance_corrections"."review_note" IS 'Proposed (dari kontrak API-ABS-007/008)';
COMMENT ON TABLE "audit_logs" IS 'BR-09, ADR-016, NFR-OBS-001. Proposed. Retensi >= 1 tahun (DEC-11).';
COMMENT ON COLUMN "audit_logs"."id" IS 'append-only; tidak di-update/delete';
COMMENT ON TABLE "notifications" IS 'FR-NOTIF-001, DEC-12. Proposed. Kanal eksternal di luar lingkup awal.';
COMMENT ON COLUMN "notifications"."type" IS 'katalog type = TBD (K-13)';
COMMENT ON TABLE "assessments" IS 'DR-005, FR-NILAI-001. Proposed.';
COMMENT ON COLUMN "assessments"."kind" IS 'jenis penilaian; katalog TBD';
COMMENT ON COLUMN "assessments"."kkm" IS 'CHECK >= 0; sumber KKM per mapel/sekolah (DEC-10)';
COMMENT ON COLUMN "assessments"."is_remedial" IS 'FR-KORESI-002; detail TBD (DEC-09)';
COMMENT ON COLUMN "assessments"."period" IS 'mis. 2026-Ganjil';
COMMENT ON TABLE "assessment_scores" IS 'DR-005. Proposed. ON DELETE CASCADE dari assessments.';
COMMENT ON TABLE "assignments" IS 'DR-007, FR-TUGAS-001. Proposed.';
COMMENT ON TABLE "assignment_submissions" IS 'DR-007. Proposed. ON DELETE CASCADE dari assignments.';
COMMENT ON TABLE "question_banks" IS 'DR-006, FR-BANK-001/002. Proposed.';
COMMENT ON TABLE "questions" IS 'DR-006, FR-BANK-002 (5 tipe paralel src/features/quiz/types.ts). Proposed.';
COMMENT ON COLUMN "questions"."id" IS 'tipe id vs integer: lihat DB-06';
COMMENT ON COLUMN "questions"."options" IS 'bentuk per tipe = TBD (K-06)';
COMMENT ON COLUMN "questions"."answer_key" IS 'tidak pernah dikirim ke siswa';
COMMENT ON TABLE "quizzes" IS 'DR-006, FR-QUIZ-001. start_time/end_time dari kontrak API (Proposed).';
COMMENT ON TABLE "quiz_questions" IS 'Proposed (DB-05). Diperlukan agar GET /quizzes/{quizId} mengembalikan soal; tidak tercantum di SDD §15.4. Versi soal = TBD (K-07).';
COMMENT ON COLUMN "quiz_questions"."question_number" IS 'urutan tampil dalam quiz';
COMMENT ON TABLE "quiz_attempts" IS 'FR-QUIZ-002. Proposed. Satu attempt aktif per siswa/quiz = usulan (belum final).';
COMMENT ON TABLE "quiz_answers" IS 'FR-QUIZ-001, FR-KORESI-001. Proposed. Autosave (API-QZ-004) dan koreksi (API-KOR-001).';
COMMENT ON COLUMN "quiz_answers"."answer" IS 'number | number[] | boolean | boolean[] | string | null';
COMMENT ON TABLE "counseling_notes" IS 'DR-008, FR-KONSELING-001. Proposed.';
COMMENT ON TABLE "counseling_followups" IS 'FR-KONSELING-002. Proposed. ON DELETE CASCADE dari counseling_notes.';
COMMENT ON TABLE "aptitude_results" IS 'DR-009, FR-MB-001. Proposed.';
COMMENT ON TABLE "target_majors" IS 'DR-009, FR-MB-002. Proposed.';
COMMENT ON TABLE "passing_grades" IS 'FR-PG-001. Data referensi dikelola Admin (DEC-10). Proposed.';
