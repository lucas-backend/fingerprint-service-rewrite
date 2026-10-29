const express =
    require("express");

const controller =
    require("../controllers/userController");

const router =
    express.Router();

// Application-owned user data (limen_db).
router.post(
    "/",
    controller.createUser
);

router.get(
    "/",
    controller.getUsers
);

router.get(
    "/:id",
    controller.getUserById
);

module.exports = router;
