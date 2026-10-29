const express = require("express");

const {
    identifyFingerprint,
    enrollFingerprint,
    enrollFingerprintRaw
} = require(
    "../controllers/fingerprintController"
);

const {
    getLatestIdentification
} = require(
    "../services/identificationStore"
);

const { getLatestFromDb } = require("../services/identificationRepository");
const { addClient, removeClient } = require("../services/identificationEvents");

const router = express.Router();

router.post(
    "/identify",
    identifyFingerprint
);

// Enrollment via gateway (proxies to fingerprint-service)
// - JSON:   POST /api/fingerprint/enroll           { externalUserId, template: base64 }
// - Raw:    POST /api/fingerprint/enroll/raw?externalUserId=...   body: 18432 bytes octet-stream
router.post(
    "/enroll",
    enrollFingerprint
);

router.post(
    "/enroll/raw",
    enrollFingerprintRaw
);

router.get("/latest", async (req, res) => {
    const mem = getLatestIdentification();
    if (mem) {
        return res.json({ success: true, data: mem });
    }
    // Fallback to DB after restart (in-memory is empty)
    const fromDb = await getLatestFromDb();
    return res.json({ success: true, data: fromDb });
});

// SSE stream — event-driven alternative to polling
// CORS is already handled by global cors() middleware; don't set header manually here.
router.get("/stream", (req, res) => {
    res.writeHead(200, {
        "Content-Type": "text/event-stream",
        "Cache-Control": "no-cache",
        "Connection": "keep-alive",
    });

    // Send initial comment to establish stream
    res.write(": connected\n\n");

    // Push current latest immediately if present
    const current = getLatestIdentification();
    if (current) {
        res.write(`data: ${JSON.stringify(current)}\n\n`);
    }

    addClient(res);

    const keepAlive = setInterval(() => {
        try { res.write(": keepalive\n\n"); } catch { /* ignore */ }
    }, 25000);

    req.on("close", () => {
        clearInterval(keepAlive);
        removeClient(res);
        try { res.end(); } catch { /* ignore */ }
    });
});

module.exports = router;