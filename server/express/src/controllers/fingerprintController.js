const axios = require("axios");

const {
    setLatestIdentification
} = require("../services/identificationStore");

const {
    getUserById
} = require("../services/userService");

const fingerprintServiceUrl =
    process.env.FINGERPRINT_SERVICE_URL || "http://localhost:5001";

// The fingerprint service returns only its own identifiers
// (externalUserId, fingerprintId). Server enriches the result with
// application data (name, employeeCode) from limen_db before
// returning it to clients. Application data never enters the
// fingerprint service.
async function enrichWithApplicationUser(result) {
    if (
        !result ||
        !result.matched ||
        !result.externalUserId
    ) {
        return result;
    }

    try {
        const user = await getUserById(
            result.externalUserId
        );

        if (user) {
            return {
                ...result,
                fullName: user.full_name,
                email: user.email
            };
        }
    }
    catch (error) {
        console.error(
            "Gagal resolve externalUserId ke limen_db:",
            error.message
        );
    }

    return result;
}

async function identifyFingerprint(req, res) {
    try {
        const r503Data = req.body;

        if (!Buffer.isBuffer(r503Data)) {
            return res.status(400).json({
                success: false,
                message: "Body harus berupa raw fingerprint binary."
            });
        }

        if (r503Data.length !== 18432) {
            return res.status(400).json({
                success: false,
                message:
                    `Ukuran fingerprint tidak sesuai. ` +
                    `Expected: 18432, Actual: ${r503Data.length}`
            });
        }

        const inboundRequestId =
            req.headers["x-request-id"] || req.headers["x-correlation-id"] || req.headers["x-trace-id"] || null;

        const fingerprintApiKey = process.env.FINGERPRINT_API_KEY || process.env.ApiKey__Value || "";

        const response = await axios.post(
            `${fingerprintServiceUrl}/api/v1/fingerprints/identify`,
            r503Data,
            {
                headers: {
                    "Content-Type": "application/octet-stream",
                    ...(inboundRequestId ? { "X-Request-Id": inboundRequestId } : {}),
                    ...(fingerprintApiKey ? { "X-Api-Key": fingerprintApiKey } : {})
                },
                timeout: 10000,
                maxContentLength: Infinity,
                maxBodyLength: Infinity
            }
        );

        const enriched = await enrichWithApplicationUser(
            response.data
        );

        await setLatestIdentification(enriched);

        return res.status(response.status).json(enriched);
    }
    catch (error) {
        console.error("Fingerprint identification error:");

        if (error.response) {
            console.error(
                "Fingerprint service response:",
                error.response.data
            );

            return res
                .status(error.response.status)
                .json(error.response.data);
        }

        console.error(error.message);

        return res.status(500).json({
            success: false,
            message:
                "Tidak dapat terhubung ke fingerprint service.",
            error: error.message
        });
    }
}

async function enrollFingerprint(req, res) {
    try {
        const { externalUserId, template } = req.body || {};

        if (!externalUserId || typeof externalUserId !== "string" || !externalUserId.trim()) {
            return res.status(400).json({
                success: false,
                message: "externalUserId wajib diisi."
            });
        }

        if (!template || typeof template !== "string" || !template.trim()) {
            return res.status(400).json({
                success: false,
                message: "template (base64) wajib diisi."
            });
        }

        const inboundRequestId =
            req.headers["x-request-id"] || req.headers["x-correlation-id"] || req.headers["x-trace-id"] || null;

        const fingerprintApiKey = process.env.FINGERPRINT_API_KEY || process.env.ApiKey__Value || "";

        const response = await axios.post(
            `${fingerprintServiceUrl}/api/v1/fingerprints/enroll`,
            {
                externalUserId: String(externalUserId).trim(),
                template: String(template).trim()
            },
            {
                headers: {
                    "Content-Type": "application/json",
                    ...(inboundRequestId ? { "X-Request-Id": inboundRequestId } : {}),
                    ...(fingerprintApiKey ? { "X-Api-Key": fingerprintApiKey } : {})
                },
                timeout: 10000
            }
        );

        return res.status(response.status).json(response.data);
    }
    catch (error) {
        console.error("Fingerprint enroll error:");

        if (error.response) {
            console.error(
                "Fingerprint service response:",
                error.response.data
            );

            return res
                .status(error.response.status)
                .json(error.response.data);
        }

        console.error(error.message);

        return res.status(500).json({
            success: false,
            message:
                "Tidak dapat terhubung ke fingerprint service.",
            error: error.message
        });
    }
}

async function enrollFingerprintRaw(req, res) {
    try {
        const externalUserId = req.query.externalUserId;

        if (!externalUserId || typeof externalUserId !== "string" || !externalUserId.trim()) {
            return res.status(400).json({
                success: false,
                message: "Query ?externalUserId wajib diisi."
            });
        }

        const r503Data = req.body;

        if (!Buffer.isBuffer(r503Data)) {
            return res.status(400).json({
                success: false,
                message: "Body harus berupa raw fingerprint binary (application/octet-stream)."
            });
        }

        if (r503Data.length !== 18432) {
            return res.status(400).json({
                success: false,
                message:
                    `Ukuran fingerprint tidak sesuai. ` +
                    `Expected: 18432, Actual: ${r503Data.length}`
            });
        }

        const inboundRequestId =
            req.headers["x-request-id"] || req.headers["x-correlation-id"] || req.headers["x-trace-id"] || null;

        const fingerprintApiKey = process.env.FINGERPRINT_API_KEY || process.env.ApiKey__Value || "";

        const response = await axios.post(
            `${fingerprintServiceUrl}/api/v1/fingerprints/enroll/raw?externalUserId=${encodeURIComponent(String(externalUserId).trim())}`,
            r503Data,
            {
                headers: {
                    "Content-Type": "application/octet-stream",
                    ...(inboundRequestId ? { "X-Request-Id": inboundRequestId } : {}),
                    ...(fingerprintApiKey ? { "X-Api-Key": fingerprintApiKey } : {})
                },
                timeout: 10000,
                maxContentLength: Infinity,
                maxBodyLength: Infinity
            }
        );

        return res.status(response.status).json(response.data);
    }
    catch (error) {
        console.error("Fingerprint enroll raw error:");

        if (error.response) {
            console.error(
                "Fingerprint service response:",
                error.response.data
            );

            return res
                .status(error.response.status)
                .json(error.response.data);
        }

        console.error(error.message);

        return res.status(500).json({
            success: false,
            message:
                "Tidak dapat terhubung ke fingerprint service.",
            error: error.message
        });
    }
}

module.exports = {
    identifyFingerprint,
    enrollFingerprint,
    enrollFingerprintRaw
};
