const db = require("../db/postgres");

async function createUser(
    email,
    passwordHash,
    fullName
) {
    const result = await db.query(
        `
        INSERT INTO users
        (email, password_hash, full_name)
        VALUES ($1, $2, $3)
        RETURNING id, email, full_name, status, created_at, updated_at
        `,
        [
            email,
            passwordHash,
            fullName
        ]
    );

    return result.rows[0];
}

async function getUsers() {
    const result = await db.query(
        `
        SELECT
            id,
            email,
            full_name,
            status,
            last_login_at,
            created_at,
            updated_at
        FROM users
        ORDER BY id DESC
        `
    );

    return result.rows;
}

async function getUserById(id) {
    const result = await db.query(
        `
        SELECT
            id,
            email,
            full_name,
            status,
            last_login_at,
            created_at,
            updated_at
        FROM users
        WHERE id = $1
        `,
        [id]
    );

    return result.rows[0] || null;
}

module.exports = {
    createUser,
    getUsers,
    getUserById
};