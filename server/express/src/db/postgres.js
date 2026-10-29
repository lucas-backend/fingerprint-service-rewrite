const { Pool } = require("pg");

const requiredDbEnv = ["DB_HOST", "DB_PORT", "DB_USER", "DB_PASSWORD", "DB_NAME"];
const missingDbEnv = requiredDbEnv.filter((key) => !process.env[key]);
const allowMissingDb = process.env.ALLOW_MISSING_DB === "1";

if (!allowMissingDb && missingDbEnv.length > 0) {
    console.error(
        `[postgres] SERVICE_MISCONFIGURED: missing required env vars: ${missingDbEnv.join(", ")}`
    );
    console.error("[postgres] Provide DB_HOST, DB_PORT, DB_USER, DB_PASSWORD, DB_NAME via environment.");
    process.exit(1);
}

if (!allowMissingDb) {
    console.log(`[postgres] host=${process.env.DB_HOST} db=${process.env.DB_NAME} port=${process.env.DB_PORT}`);
}

const pool = new Pool({
    host: process.env.DB_HOST,
    port: process.env.DB_PORT,
    user: process.env.DB_USER,
    password: process.env.DB_PASSWORD,
    database: process.env.DB_NAME,
    max: 20,
    connectionTimeoutMillis: 5000,
    idleTimeoutMillis: 30000
});

module.exports = pool;
