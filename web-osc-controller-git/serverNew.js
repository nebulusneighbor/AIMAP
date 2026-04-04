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
let submissions = [];
let appState = {
    phase: "lobby", // lobby, active, results
    mode: "both",   // audio, visual, both
    round: 1        // Incrementing this effectively resets all users
};

/* ---------- HELPERS ---------- */

function calculateAverages() {
    if (submissions.length === 0) return null;

    const fxList = ["guitar", "bass", "piano", "strings", "winds"];
    const members = ["drum1", "drum2", "guitar", "bass", "piano", "strings", "winds"];

    const summary = {
        count: submissions.length,
        genre: {},
        mode: {},
        environment: {},
        tempo: 0,
        human_guitar_tone: {},
        instruments: { guitar: 0, bass: 0, piano: 0, strings: 0, winds: 0 },
        fx: { guitar: {}, bass: {}, piano: {}, strings: {}, winds: {} },
        dancers: {
            d1_char: {}, d1_move: {},
            d2_char: {}, d2_move: {}
        },
        band: {
            drum1: {}, drum2: {}, guitar: {}, bass: {}, piano: {}, strings: {}, winds: {}
        }
    };

    submissions.forEach(s => {
        summary.tempo += parseInt(s.tempo || 115);
        summary.genre[s.genre] = (summary.genre[s.genre] || 0) + 1;
        summary.mode[s.mode] = (summary.mode[s.mode] || 0) + 1;
        summary.environment[s.environment] = (summary.environment[s.environment] || 0) + 1;
        if (s.human_guitar_tone) summary.human_guitar_tone[s.human_guitar_tone] = (summary.human_guitar_tone[s.human_guitar_tone] || 0) + 1;

        if (s.instruments) {
            try {
                const inst = typeof s.instruments === 'string' ? JSON.parse(s.instruments) : s.instruments;
                for (let key in summary.instruments) { if (inst[key]) summary.instruments[key]++; }
            } catch(e) {}
        }

        fxList.forEach(name => {
            const val = s[`${name}_fx`];
            if (val) summary.fx[name][val] = (summary.fx[name][val] || 0) + 1;
        });

        // Dancers
        ["d1", "d2"].forEach(d => {
            const char = s[`${d}_character`];
            const move = s[`${d}_move`];
            if (char) summary.dancers[`${d}_char`][char] = (summary.dancers[`${d}_char`][char] || 0) + 1;
            if (move) summary.dancers[`${d}_move`][move] = (summary.dancers[`${d}_move`][move] || 0) + 1;
        });

        // Band
        members.forEach(m => {
            const char = s[`${m}_char`];
            if (char) summary.band[m][char] = (summary.band[m][char] || 0) + 1;
        });
    });

    const getTop = (obj) => {
        const keys = Object.keys(obj);
        if (keys.length === 0) return "N/A";
        const max = Math.max(...Object.values(obj));
        const winners = keys.filter(k => obj[k] === max);
        return winners[Math.floor(Math.random() * winners.length)];
    };

    const final = {
        count: summary.count,
        tempo: Math.round(summary.tempo / summary.count),
        genre: getTop(summary.genre),
        mode_val: getTop(summary.mode),
        environment: getTop(summary.environment),
        humanGuitarTone: getTop(summary.human_guitar_tone),
        instruments: summary.instruments,
        topFX: {},
        dancers: {
            d1: { char: getTop(summary.dancers.d1_char), move: getTop(summary.dancers.d1_move) },
            d2: { char: getTop(summary.dancers.d2_char), move: getTop(summary.dancers.d2_move) }
        },
        band: { members: {} }
    };

    fxList.forEach(f => final.topFX[f] = getTop(summary.fx[f]));
    members.forEach(m => final.band.members[m] = getTop(summary.band[m]));

    return final;
}

/* ---------- ROUTES ---------- */

app.get('/', (req, res) => {
    if (!req.session.username) {
        return res.sendFile(__dirname + "/public/username.html");
    }
    
    // Check if they submitted for the CURRENT round
    const alreadySubmitted = req.session.lastSubmittedRound === appState.round;

    if (alreadySubmitted && appState.phase !== "results") {
        return res.sendFile(__dirname + "/public/waiting.html");
    }

    // Admin-controlled redirection
    if (appState.phase === "lobby") {
        return res.sendFile(__dirname + "/public/lobby.html");
    }

    res.sendFile(__dirname + `/public/${appState.mode}.html`);
});

app.get('/aggregator', (req, res) => {
    res.sendFile(__dirname + "/public/aggregator.html");
});

app.post('/submit', (req, res) => {
    if (req.session.lastSubmittedRound === appState.round) return res.redirect("/");

    submissions.push(req.body);
    req.session.lastSubmittedRound = appState.round;
    
    globalClickCount++;
    io.emit("aggregator_update", { 
        count: globalClickCount,
        username: req.session.username 
    });

    res.redirect("/");
});

/* ---------- SOCKET ---------- */

io.on("connection", (socket) => {
    const session = socket.request.session;

    if (session?.username) {
        users[socket.id] = {
            username: session.username,
            mode: appState.mode
        };
        sendUserList();
    }

    socket.on("admin_command", (data) => {
        if (data.command === "start_vote") {
            appState.phase = "active";
            appState.mode = data.mode;
            submissions = [];
            globalClickCount = 0;
            
            // Broadcast the phase change and also a redirect command
            io.emit("phase_change", appState);
            io.emit("redirect_clients", { page: "/" });
        }

        if (data.command === "lock_and_average") {
            appState.phase = "results";
            const results = calculateAverages();
            if (results) results.mode = appState.mode; // Pass mode to aggregator
            io.emit("results_ready", results);
        }

        if (data.command === "reset") {
            appState.phase = "lobby";
            appState.round++; // CRITICAL: Incrementing the round ID resets all user sessions
            submissions = [];
            globalClickCount = 0;
            
            io.emit("phase_change", appState);
            io.emit("aggregator_reset"); // Custom event to clear the projection screen
            io.emit("redirect_clients", { page: "/" });
        }
    });

    socket.on("disconnect", () => {
        delete users[socket.id];
        sendUserList();
    });
});

app.post('/set-username', (req, res) => {
    const { username } = req.body;
    if (username && username.trim() !== "") {
        req.session.username = username.trim();
        req.session.hasSubmitted = false; // Reset on new login
    }
    res.redirect("/");
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
