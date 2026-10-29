const express = require("express");
const cors = require("cors");

const fingerprintRoutes =
    require("./routes/fingerprintRoutes");

const userRoutes =
    require("./routes/userRoutes");

const app = express();

const allowedOrigins = (process.env.ALLOWED_ORIGINS || "")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);

if (allowedOrigins.length > 0) {
    app.use(cors({ origin: allowedOrigins, credentials: true }));
} else if (process.env.NODE_ENV !== "production") {
    // Dev: no ALLOWED_ORIGINS set -> allow any localhost/Vite origin.
    // Production must set ALLOWED_ORIGINS explicitly.
    app.use(cors({ origin: true, credentials: true }));
} else {
    app.use(cors({ origin: false }));
}

// JSON bodies (application-owned APIs, e.g. users)
app.use(
    express.json({
        limit: "100kb"
    })
);

// Raw binary fingerprint bodies
app.use(
    express.raw({
        type: "application/octet-stream",
        limit: "64kb"
    })
);

// Health check (unauthenticated — for load balancer probes)
app.get("/health", (req, res) => {
    res.json({
        success: true,
        service: "express-api"
    });
});

// Optional API-key gate for /api/* (disabled when not configured).
// Health is exempt. Keys are compared in constant time.
const requiredApiKey = process.env.API_KEY || process.env.EXPRESS_API_KEY || "";
if (requiredApiKey) {
    app.use("/api", (req, res, next) => {
        const presented = req.headers["x-api-key"];
        if (!presented || presented !== requiredApiKey) {
            return res.status(401).json({
                success: false,
                error: { code: "UNAUTHORIZED", message: "Missing or invalid API key." }
            });
        }
        next();
    });
}

// PayloadTooLarge handler — keep contract consistent (JSON, not HTML)
app.use((err, req, res, next) => {
    if (err && (err.status === 413 || err.type === "entity.too.large")) {
        return res.status(413).json({
            success: false,
            error: { code: "REQUEST_TOO_LARGE", message: "Request body is too large." }
        });
    }
    return next(err);
});

// Application data API (limen_db)
app.use(
    "/api/users",
    userRoutes
);

// Fingerprint gateway API (proxies to the fingerprint service)
app.use(
    "/api/fingerprint",
    fingerprintRoutes
);

module.exports = app;
