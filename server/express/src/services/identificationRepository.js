const db = require("../db/postgres");

async function saveIdentification(result) {
    try {
        const externalUserId = result?.externalUserId || null;
        const matched = Boolean(result?.matched);
        const score = typeof result?.score === "number" ? result.score : null;
        const candidateCount = Number.isFinite(result?.candidateCount) ? result.candidateCount : 0;
        const fingerprintId = result?.fingerprintId || null;

        await db.query(
            `INSERT INTO identifications
                (external_user_id, matched, score, candidate_count, fingerprint_id, payload_json)
              VALUES ($1, $2, $3, $4, $5, $6::jsonb)`,
            [
                externalUserId,
                matched ? 1 : 0,
                score,
                candidateCount,
                fingerprintId,
                JSON.stringify(result),
            ]
        );
    } catch (err) {
        // Never fail the request if audit write fails — log and continue.
        console.warn("[identifications] save failed:", err.message);
    }
}

async function getLatestFromDb() {
    try {
        const result = await db.query(
            `SELECT payload_json, created_at
              FROM identifications
              ORDER BY id DESC
              LIMIT 1`
        );
        if (result.rows.length === 0) return null;
        const raw = result.rows[0].payload_json;
        const data = typeof raw === "string" ? JSON.parse(raw) : raw;
        return { ...data, timestamp: result.rows[0].created_at };
    } catch (err) {
        console.warn("[identifications] getLatest failed:", err.message);
        return null;
    }
}

module.exports = { saveIdentification, getLatestFromDb };
