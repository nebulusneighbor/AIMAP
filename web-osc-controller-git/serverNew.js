const express = require('express');
const session = require('express-session');
const http = require('http');
const { Server } = require('socket.io');
const { Client } = require('node-osc');

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
let lastCalculatedResults = null;
let appState = {
    phase: "lobby", // lobby, active, results
    mode: "both",   // audio, visual, both
    round: 1        // Incrementing this effectively resets all users
};

let oscTargets = {
    ableton: { ip: "127.0.0.1", port: 11002 },
    unity: { ip: "127.0.0.1", port: 11003 },
    generator: { ip: "127.0.0.1", port: 11007 } 
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

        ["d1", "d2"].forEach(d => {
            const char = s[`${d}_character`];
            const move = s[`${d}_move`];
            if (char) summary.dancers[`${d}_char`][char] = (summary.dancers[`${d}_char`][char] || 0) + 1;
            if (move) summary.dancers[`${d}_move`][move] = (summary.dancers[`${d}_move`][move] || 0) + 1;
        });

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
    const alreadySubmitted = req.session.lastSubmittedRound === appState.round;
    if (alreadySubmitted && appState.phase !== "results") {
        return res.sendFile(__dirname + "/public/waiting.html");
    }
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
        users[socket.id] = { username: session.username, mode: appState.mode };
        sendUserList();
    }

    socket.on("admin_command", (data) => {
        if (data.command === "start_vote") {
            appState.phase = "active";
            appState.mode = data.mode;
            submissions = [];
            globalClickCount = 0;
            io.emit("phase_change", appState);
            io.emit("redirect_clients", { page: "/" });
        }

        if (data.command === "lock_and_average") {
            appState.phase = "results";
            const results = calculateAverages();
            if (results) {
                results.mode = appState.mode;
                lastCalculatedResults = results;
            }
            io.emit("results_ready", results);
        }

        if (data.command === "initiate_performance") {
            if (!lastCalculatedResults) return;
            
            if (data.abletonIP) {
                oscTargets.ableton.ip = data.abletonIP;
                oscTargets.generator.ip = data.abletonIP; 
            }
            if (data.unityIP) oscTargets.unity.ip = data.unityIP;

            const abletonClient = new Client(oscTargets.ableton.ip, oscTargets.ableton.port);
            const generatorClient = new Client(oscTargets.generator.ip, oscTargets.generator.port);
            const unityClient = new Client(oscTargets.unity.ip, oscTargets.unity.port);

            // 1. Send Setup to Ableton Controller (Port 11002)
            abletonClient.send('/performance/setup', [
                lastCalculatedResults.genre,
                parseFloat(lastCalculatedResults.tempo),
                lastCalculatedResults.mode_val,
                parseInt(lastCalculatedResults.humanGuitarTone)
            ], () => abletonClient.close());

            // 2. Trigger Z-Groove AI Generation (Port 11007) - DRUM SEEDED
            const feelMapping = {
                "drive/rock": "Content_Drive",
                "pulse/hiphop": "Content_Pulse",
                "groove/funky": "Content_Groove",
                "elegant/atmospheric": "Content_Elegance"
            };
            const progMapping = { "guitar": 24, "bass": 33, "piano": 0, "strings": 48, "winds": 73 };
            
            const targetProgs = []; 
            for (let [inst, count] of Object.entries(lastCalculatedResults.instruments)) {
                if (Math.round((count / lastCalculatedResults.count) * 100) >= 50) {
                    if (progMapping[inst] !== undefined) targetProgs.push(progMapping[inst]);
                }
            }

            generatorClient.send('/web/drum_seeded_request', [
                feelMapping[lastCalculatedResults.genre] || "Content_Groove",
                ...targetProgs
            ], () => generatorClient.close());

            // 3. Send Visuals to Unity (Port 11003)
            const envMap = { 'haunted forest': 0, 'mars scape': 1, 'laser grid': 2 };
            const getSkinIdx = (charName) => {
                if (!charName) return 0;
                if (charName.match(/vampire|werewolf|moose|goblin/)) return 2;
                if (charName.match(/flesh|dark|lizard|webbed/)) return 1;
                return 0; // robots
            };

            unityClient.send('/environment/skybox', envMap[lastCalculatedResults.environment] || 0);
            unityClient.send('/avatar/dancer1/skin', getSkinIdx(lastCalculatedResults.dancers.d1.char));
            unityClient.send('/avatar/dancer2/skin', getSkinIdx(lastCalculatedResults.dancers.d2.char));
            
            const m = lastCalculatedResults.band.members;
            unityClient.send('/avatar/drum1/skin', getSkinIdx(m.drum1));
            unityClient.send('/avatar/drum2/skin', getSkinIdx(m.drum2));
            unityClient.send('/avatar/guitar/skin', getSkinIdx(m.guitar));
            unityClient.send('/avatar/bass/skin', getSkinIdx(m.bass));
            unityClient.send('/avatar/violin/skin', getSkinIdx(m.strings));

            unityClient.close();
            console.log("Reverted to Separate Initiation Packets.");
            io.emit("performance_started");
        }

        if (data.command === "reset") {
            appState.phase = "lobby";
            appState.round++;
            submissions = [];
            globalClickCount = 0;
            lastCalculatedResults = null;
            io.emit("phase_change", appState);
            io.emit("aggregator_reset");
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
        req.session.lastSubmittedRound = 0;
    }
    res.redirect("/");
});

function sendUserList() {
    const grouped = { audio: [], visual: [], both: [] };
    for (let id in users) {
        const user = users[id];
        if (grouped[user.mode]) grouped[user.mode].push({ socketId: id, username: user.username });
    }
    io.emit("user_list_update", grouped);
}

app.use(express.static('public'));

server.listen(PORT, () => {
    console.log(`http://localhost:${PORT}`);
});
