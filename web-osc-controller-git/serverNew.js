const express = require('express');
const session = require('express-session');
const http = require('http');
const { Server } = require('socket.io');

const app = express();
const server = http.createServer(app);
const io = new Server(server);

const PORT = 3000;

app.use(express.json());
app.use(express.urlencoded({ extended: true }));

const sessionMiddleware = session({
    secret: 'music_secret',
    resave: false,
    saveUninitialized: true
});

app.use(sessionMiddleware);

io.use((socket, next) => {
    sessionMiddleware(socket.request, {}, next);
});

let globalClickCount = 0;
let users = {};

/* ---------- ROUTES ---------- */

app.get('/', (req, res) => {

    if (!req.session.username) {
        res.sendFile(__dirname + "/public/username.html");
    }
    else if (!req.session.mode) {
        res.sendFile(__dirname + "/public/mode.html");
    }
    else {
        res.sendFile(__dirname + `/public/${req.session.mode}.html`);
    }

});

app.post('/set-username', (req, res) => {
    const { username } = req.body;

    if (username && username.trim() !== "") {
        req.session.username = username.trim();
    }

    res.redirect("/");
});

app.post('/set-mode', (req, res) => {
    const { mode } = req.body;

    if (["audio","visual","both"].includes(mode)) {
        req.session.mode = mode;
    }

    res.redirect("/");
});

app.post('/submit', (req, res) => {

    const { 
        genre, instruments, tempo, color,

        // 👇 NEW FIELDS
        dancer1_dance, dancer1_model,
        dancer2_dance, dancer2_model,

        drummer1, drummer2,
        guitar_skin, bass_skin, piano_skin, strings_skin

    } = req.body;

    const username = req.session.username;

    if (username && genre) {

        globalClickCount++;

        io.emit("aggregator_update", {
            count: globalClickCount,
            message: `${username} submitted a track`
        });

        io.emit("new_submission", {
            username,
            genre,
            tempo,
            instruments,
            color,

            // 👇 ADD THESE (this is all you needed)
            dancer1_dance,
            dancer1_model,
            dancer2_dance,
            dancer2_model,

            drummer1,
            drummer2,
            guitar_skin,
            bass_skin,
            piano_skin,
            strings_skin
        });
    }

    res.redirect("/");
});

/* ---------- SOCKET ---------- */

io.on("connection", (socket) => {

    const session = socket.request.session;

    if (session?.username) {

        const mode = session.mode || "none";

        users[socket.id] = {
            username: session.username,
            mode
        };

        if (mode !== "none") {
            socket.join(mode);
        }

        sendUserList();
    }

    socket.on("force_redirect", ({ group }) => {

        const page = `${group}.html`;

        if (group === "all") {
            io.emit("redirect_clients", { page: "both.html" });
        } else {
            io.to(group).emit("redirect_clients", { page });
        }

    });

    socket.on("move_user", ({ socketId, newGroup }) => {

        const user = users[socketId];
        if (!user) return;

        const targetSocket = io.sockets.sockets.get(socketId);
        if (!targetSocket) return;

        // Leave old room
        if (user.mode) {
            targetSocket.leave(user.mode);
        }

        // Join new room
        targetSocket.join(newGroup);

        // Update server state
        user.mode = newGroup;

        // 🔥 IMPORTANT: also update session
        targetSocket.request.session.mode = newGroup;
        targetSocket.request.session.save();

        // 🔥 FORCE REDIRECT THAT USER
        targetSocket.emit("redirect_clients", {
            page: `${newGroup}.html`
        });

        sendUserList();
    });

    socket.on("disconnect", () => {
        delete users[socket.id];
        sendUserList();
    });

});

function sendUserList() {

    const grouped = { audio: [], visual: [], both: [] };

    for (let id in users) {
        const user = users[id];

        if (grouped[user.mode]) {
            grouped[user.mode].push({
                socketId: id,
                username: user.username
            });
        }
    }

    io.emit("user_list_update", grouped);
}

app.use(express.static('public'));

server.listen(PORT, () => {
    console.log(`http://localhost:${PORT}`);
});