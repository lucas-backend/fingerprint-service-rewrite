const { saveIdentification } = require("./identificationRepository");
const { broadcast } = require("./identificationEvents");

let latestIdentification = null;
let scanCounter = 0;

async function setLatestIdentification(data) {
    scanCounter++;

    latestIdentification = {
        ...data,
        scanId: scanCounter,
        timestamp: new Date().toISOString()
    };

    // Durable + push — best-effort, never throw.
    try {
        await saveIdentification(latestIdentification);
    } catch {
        // already logged in repository
    }

    try {
        broadcast(latestIdentification);
    } catch {
        // ignore SSE broadcast failure
    }
}

function getLatestIdentification() {
    return latestIdentification;
}

module.exports = {
    setLatestIdentification,
    getLatestIdentification
};
