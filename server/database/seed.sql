-- Development seed for the unified PostgreSQL database.
-- Credentials: admin@example.com / Admin123!
-- Do not use this account or password in production.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

INSERT INTO users (
    email,
    password_hash,
    full_name,
    status
)
VALUES (
    'admin@example.com',
    crypt('Admin123!', gen_salt('bf', 12)),
    'Administrator',
    'active'
)
ON CONFLICT (email) DO NOTHING;

INSERT INTO users (
    email,
    password_hash,
    full_name,
    status
)
VALUES (
    'imadedipa@gmail.com',
    crypt('1234', gen_salt('bf', 12)),
    'Made Dipa',
    'active'
)
ON CONFLICT (email) DO NOTHING;
