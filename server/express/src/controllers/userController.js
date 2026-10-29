const userService =
    require("../services/userService");

async function createUser(req, res) {
    try {
        const {
            email,
            passwordHash,
            fullName
        } = req.body || {};

        if (!email || !passwordHash || !fullName) {
            return res.status(400).json({
                success: false,
                message: "email, passwordHash, dan fullName wajib."
            });
        }

        const user = await userService.createUser(
            email,
            passwordHash,
            fullName
        );

        return res.status(201).json({
            success: true,
            id: user.id,
            email: user.email,
            fullName: user.full_name
        });
    }
    catch (error) {
        if (error && error.code === "23505") {
            return res.status(409).json({
                success: false,
                message: "email sudah terdaftar."
            });
        }

        console.error(
            "Gagal membuat user:",
            error.message
        );

        return res.status(500).json({
            success: false,
            message: "Gagal membuat user."
        });
    }
}

async function getUsers(req, res) {
    try {
        const users = await userService.getUsers();

        return res.json({
            success: true,
            data: users
        });
    }
    catch (error) {
        console.error(
            "Gagal mengambil users:",
            error.message
        );

        return res.status(500).json({
            success: false,
            message: "Gagal mengambil users."
        });
    }
}

async function getUserById(req, res) {
    try {
        const user = await userService.getUserById(
            req.params.id
        );

        if (!user) {
            return res.status(404).json({
                success: false,
                message: "User tidak ditemukan."
            });
        }

        return res.json({
            success: true,
            data: user
        });
    }
    catch (error) {
        console.error(
            "Gagal mengambil user:",
            error.message
        );

        return res.status(500).json({
            success: false,
            message: "Gagal mengambil user."
        });
    }
}

module.exports = {
    createUser,
    getUsers,
    getUserById
};
