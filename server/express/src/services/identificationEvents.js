const clients = new Set();

function addClient(res) {
    clients.add(res);
}

function removeClient(res) {
    clients.delete(res);
}

function broadcast(data) {
    const payload = `data: ${JSON.stringify(data)}\n\n`;
    for (const res of clients) {
        try {
            res.write(payload);
        } catch {
            // client gone — will be removed on close
        }
    }
}

function clientCount() {
    return clients.size;
}

module.exports = { addClient, removeClient, broadcast, clientCount };
