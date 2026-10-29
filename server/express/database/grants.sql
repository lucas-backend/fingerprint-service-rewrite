-- =========================================================
-- Least-privilege PostgreSQL roles for limen_db (single DB)
-- =========================================================
-- Unified DB: all tables live in limen_db.
-- Prefer a single application user (limen_user) for ops
-- simplicity. Fingerprint identity is enforced at the
-- application/HTTP layer (server is the DB gateway), not
-- at the PostgreSQL GRANT layer.
--
-- If you need per-service least privilege within limen_db,
-- create per-table grants instead of per-database.
--
-- IMPORTANT: replace placeholders with strong passwords.
-- Never commit real passwords. Generate from a secret manager.
-- =========================================================

-- ---------------------------------------------------------
-- limen_user — server + fingerprint-service (shared DB)
-- ---------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'limen_user') THEN
        CREATE ROLE limen_user LOGIN PASSWORD 'change_me_limen';
    END IF;
END
$$;

GRANT CONNECT ON DATABASE limen_db TO limen_user;
GRANT USAGE ON SCHEMA public TO limen_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO limen_user;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO limen_user;

-- ---------------------------------------------------------
-- Backwards compatibility aliases (optional)
-- ---------------------------------------------------------
-- If you still have legacy fingerprint/application role
-- variables, you may alias them to
-- the same privilege. Uncomment if needed:
-- ---------------------------------------------------------
-- CREATE ROLE fingerprint_user LOGIN PASSWORD 'change_me_fingerprint';
-- GRANT USAGE ON SCHEMA public TO fingerprint_user;
-- GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO fingerprint_user;
