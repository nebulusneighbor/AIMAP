const express = require('express');
const session = require('express-session');
const http = require('http');
const { Server } = require('socket.io');
const { Client } = require('node-osc');

const fs = require('fs');
const path = require('path');

const app = express();
const server = http.createServer(app);
const io = new Server(server);

const PORT = 3000;

/* ---------- DATA LOGGING SYSTEM ---------- */
class CSVLogger {
    constructor() {
        const now = new Date();
        const timestamp = now.toISOString().replace(/T/, '_').replace(/\..+/, '').replace(/:/g, '-');
        this.sessionDir = path.join(__dirname, 'logs', timestamp);
        this.participantDir = path.join(this.sessionDir, 'individual_participant_logs');
        
        // Ensure directories exist
        if (!fs.existsSync(path.join(__dirname, 'logs'))) fs.mkdirSync(path.join(__dirname, 'logs'));
        fs.mkdirSync(this.sessionDir);
        fs.mkdirSync(this.participantDir);
        
        console.log(`[LOGGER] Session logs initialized at: ${this.sessionDir}`);
    }

    formatCSVRow(data, headers) {
        return headers.map(key => {
            const val = data[key];
            const str = (val === null || val === undefined) ? "" : (typeof val === 'object' ? JSON.stringify(val) : String(val));
            return `"${str.replace(/"/g, '""')}"`; // Escape quotes
        }).join(',');
    }

    logEvent(filename, data, useParticipantDir = false, schema = null) {
        const timestamp = new Date().toISOString();
        const dir = useParticipantDir ? this.participantDir : this.sessionDir;
        const filePath = path.join(dir, filename.endsWith('.csv') ? filename : `${filename}.csv`);
        
        let headers = [];
        let rowData = { timestamp, ...data };

        if (schema) {
            // STRICT SCHEMA: Header order is defined by SUBMISSION_SCHEMA
            headers = ['timestamp', ...schema];
        } else {
            // DYNAMIC: Headers are whatever keys are present
            headers = Object.keys(rowData);
        }
        
        const isNewFile = !fs.existsSync(filePath);
        if (isNewFile) {
            fs.writeFileSync(filePath, headers.join(',') + '\n');
        }

        const row = this.formatCSVRow(rowData, headers);
        fs.appendFileSync(filePath, row + '\n');
    }
}

const logger = new CSVLogger();

// MASTER SCHEMA for Submissions (ensures CSV alignment)
const SUBMISSION_SCHEMA = [
    'username', 'submitted_at', 'seconds_to_submit', 'total_choices_made',
    'genre', 'tempo', 'mode', 'human_guitar_tone',
    'instruments', 'guitar_fx_choice', 'bass_fx_choice', 'piano_fx_choice', 'strings_fx_choice', 'winds_fx_choice',
    'environment', 'd1_character', 'd1_move', 'd2_character', 'd2_move',
    'drum1_char', 'drum2_char', 'guitar_char', 'bass_char', 'piano_char', 'strings_char', 'winds_char'
];

/* ----------------------------------------- */

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
    round: 1,        // Incrementing this effectively resets all users
    round_start_time: null
};

let oscTargets = {
    ableton: { ip: "127.0.0.1", port: 11002 },
    unity: { ip: "127.0.0.1", port: 11003 },
    generator: { ip: "127.0.0.1", port: 11007 } 
};

function sendOscAsync(client, address, args = []) {
    return new Promise((resolve, reject) => {
        client.send(address, ...args, (err) => {
            if (err) {
                reject(err);
                return;
            }
            resolve();
        });
    });
}

/* ---------- HELPERS ---------- */

const getRandom = (arr) => arr[Math.floor(Math.random() * arr.length)];

const getWeightedRandom = (arr, weights) => {
    let sum = weights.reduce((a, b) => a + b, 0);
    let r = Math.random() * sum;
    for (let i = 0; i < arr.length; i++) {
        if (r < weights[i]) return arr[i];
        r -= weights[i];
    }
    return arr[0];
};

function calculateAverages(mode) {
    const fxList = ["guitar", "bass", "piano", "strings", "winds"];
    const members = ["drum1", "drum2", "guitar", "bass", "piano", "strings", "winds"];
    const fxChoiceNames = ["piano_fx_choice", "guitar_fx_choice", "bass_fx_choice", "strings_fx_choice", "winds_fx_choice"];
    const chars = ['vampire', 'werewolf', 'moose_orc', 'goblin_elf', 'flesh_eater', 'dark_noire', 'lizard_species', 'webbed_and_armored', 'x-bot', 'y-bot'];
    const feelMap = ['drive/rock', 'pulse/hiphop', 'groove/funky', 'elegant/atmospheric'];
    const danceMoves = ['hiphop', 'breakdance', 'latin', 'drink'];
    const envOptions = ['haunted forest', 'mars scape', 'laser grid'];
    const fxChoiceMap = {
        piano: ['default', 'dist', 'echo'], guitar: ['default', 'dist', 'airy'],
        bass: ['default', 'dist', 'reverb'], strings: ['default', 'dist', 'chorus'],
        winds: ['default', 'dist', 'phaser']
    };

    const summary = {
        count: submissions.length,
        genre: {},
        mode: {},
        environment: {},
        tempo: 0,
        human_guitar_tone: {},
        instruments: { guitar: 0, bass: 0, piano: 0, strings: 0, winds: 0 },
        fx_choices: {},
        dancers: {
            d1_char: {}, d1_move: {},
            d2_char: {}, d2_move: {}
        },
        band: {
            drum1: {}, drum2: {}, guitar: {}, bass: {}, piano: {}, strings: {}, winds: {}
        }
    };

    fxChoiceNames.forEach(fc => summary.fx_choices[fc] = {});

    submissions.forEach(s => {
        summary.tempo += parseInt(s.tempo || 115);
        if (s.genre) summary.genre[s.genre] = (summary.genre[s.genre] || 0) + 1;
        if (s.mode) summary.mode[s.mode] = (summary.mode[s.mode] || 0) + 1;
        if (s.environment) summary.environment[s.environment] = (summary.environment[s.environment] || 0) + 1;
        if (s.human_guitar_tone) summary.human_guitar_tone[s.human_guitar_tone] = (summary.human_guitar_tone[s.human_guitar_tone] || 0) + 1;

        if (s.instruments) {
            try {
                const inst = typeof s.instruments === 'string' ? JSON.parse(s.instruments) : s.instruments;
                for (let key in summary.instruments) { if (inst[key]) summary.instruments[key]++; }
            } catch(e) {}
        }

        fxChoiceNames.forEach(fc => {
            const val = s[fc] || "none";
            summary.fx_choices[fc][val] = (summary.fx_choices[fc][val] || 0) + 1;
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
        if (keys.length === 0) return null;
        const max = Math.max(...Object.values(obj));
        const winners = keys.filter(k => obj[k] === max);
        return winners[Math.floor(Math.random() * winners.length)];
    };

    const final = {
        count: summary.count,
        mode: mode,
        tempo: summary.count > 0 ? Math.round(summary.tempo / summary.count) : 115,
        genre: getTop(summary.genre),
        mode_val: getTop(summary.mode),
        environment: getTop(summary.environment),
        humanGuitarTone: getTop(summary.human_guitar_tone),
        instruments: summary.instruments,
        fx_choices: {},
        dancers: {
            d1: { char: getTop(summary.dancers.d1_char), move: getTop(summary.dancers.d1_move) },
            d2: { char: getTop(summary.dancers.d2_char), move: getTop(summary.dancers.d2_move) }
        },
        band: { members: {} }
    };

    fxChoiceNames.forEach(fc => {
        final.fx_choices[fc.replace("_fx_choice", "")] = getTop(summary.fx_choices[fc]);
    });
    members.forEach(m => final.band.members[m] = getTop(summary.band[m]));

    // AI Generation for any missing core variables
    const effectiveCount = Math.max(summary.count, 1);

    if (!final.genre) {
        final.ai_audio = true;
        final.genre = getRandom(feelMap);
        summary.genre[final.genre] = effectiveCount;
    }
    if (!final.mode_val) {
        final.ai_audio = true;
        final.mode_val = getRandom(['major', 'minor']);
        summary.mode[final.mode_val] = effectiveCount;
    }
    if (!final.humanGuitarTone) {
        final.ai_audio = true;
        final.humanGuitarTone = getRandom(['1', '2', '3']);
        summary.human_guitar_tone[final.humanGuitarTone] = effectiveCount;
    }
    if (summary.tempo === 0 || !final.tempo) {
        final.ai_audio = true;
        final.tempo = Math.floor(Math.random() * (140 - 90 + 1)) + 90;
    }

    // Random instrument mix if none voted
    if (Object.values(final.instruments).every(v => v === 0)) {
        final.ai_instruments = true;
        fxList.forEach(inst => { 
            if (Math.random() > 0.4) {
                final.instruments[inst] = effectiveCount;
                summary.instruments[inst] = effectiveCount;
            }
        });
    }

    // FX Choices AI Weighted (3x for Default)
    const weights = [3, 1, 1];
    Object.keys(fxChoiceMap).forEach(inst => {
        if (!final.fx_choices[inst] || final.fx_choices[inst] === "none" || final.fx_choices[inst] === null) {
            final.fx_choices[inst] = getWeightedRandom(fxChoiceMap[inst], weights);
            if (!summary.fx_choices[`${inst}_fx_choice`]) summary.fx_choices[`${inst}_fx_choice`] = {};
            summary.fx_choices[`${inst}_fx_choice`][final.fx_choices[inst]] = effectiveCount;
        }
    });

    if (!final.environment) {
        final.ai_visual = true;
        final.environment = getRandom(envOptions);
        summary.environment[final.environment] = effectiveCount;
    }
    if (!final.dancers.d1.char) final.dancers.d1.char = getRandom(chars);
    if (!final.dancers.d1.move) {
        final.dancers.d1.move = getRandom(danceMoves);
        if (!summary.dancers.d1_move) summary.dancers.d1_move = {};
        summary.dancers.d1_move[final.dancers.d1.move] = effectiveCount;
    }
    if (!final.dancers.d2.char) final.dancers.d2.char = getRandom(chars);
    if (!final.dancers.d2.move) {
        final.dancers.d2.move = getRandom(danceMoves);
        if (!summary.dancers.d2_move) summary.dancers.d2_move = {};
        summary.dancers.d2_move[final.dancers.d2.move] = effectiveCount;
    }
    
    members.forEach(m => {
        if (!final.band.members[m]) {
            final.band.members[m] = getRandom(chars);
            summary.band[m][final.band.members[m]] = effectiveCount;
        }
    });

    // Final distributions for charts
    final.distributions = {
        genre: summary.genre,
        mode: summary.mode,
        environment: summary.environment,
        tone: summary.human_guitar_tone,
        fx: summary.fx_choices,
        d1_move: summary.dancers.d1_move,
        d2_move: summary.dancers.d2_move,
        band: summary.band
    };

    return final;
}

/* ---------- ROUTES ---------- */

app.get('/', (req, res) => {
    if (!req.session.username) {
        return res.sendFile(__dirname + "/public/username.html");
    }
    const alreadySubmitted = req.session.lastSubmittedRound === appState.round;
    if (alreadySubmitted) {
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
    
    const now = Date.now();
    const durationSeconds = appState.round_start_time ? (now - appState.round_start_time) / 1000 : 0;

    // Calculate choices made (Interaction Depth)
    let choicesMade = 0;
    for (let key in req.body) {
        if (key === 'instruments') {
            try {
                const inst = JSON.parse(req.body[key]);
                choicesMade += Object.keys(inst).length;
            } catch(e) {}
        } else {
            const val = req.body[key];
            // Only count if it's a valid, non-default, non-empty selection
            if (val && val !== "" && val !== "default" && val !== "none") {
                // If it's tempo, only count if it's not the default value (115)
                if (key === 'tempo' && val === "115") continue;
                choicesMade++;
            }
        }
    }

    const submission = { 
        ...req.body, 
        username: req.session.username,
        submitted_at: new Date().toISOString(),
        seconds_to_submit: durationSeconds.toFixed(2),
        total_choices_made: choicesMade
    };
    
    submissions.push(submission);
    req.session.lastSubmittedRound = appState.round;
    globalClickCount++;

    // LOG SUBMISSION (STRICT SCHEMA)
    logger.logEvent('submissions', submission, false, SUBMISSION_SCHEMA);
    logger.logEvent(`user_${req.session.username}`, submission, true, SUBMISSION_SCHEMA);

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
        // LOG ADMIN ACTION
        logger.logEvent('admin_actions', data);

        if (data.command === "start_vote") {
            appState.phase = "active";
            appState.mode = data.mode;
            appState.round_start_time = Date.now(); // TRACK START OF ROUND
            submissions = [];
            globalClickCount = 0;
            io.emit("phase_change", appState);
            io.emit("redirect_clients", { page: "/" });
        }

        if (data.command === "lock_and_average") {
            appState.phase = "results";
            const results = calculateAverages(appState.mode);
            if (results) {
                lastCalculatedResults = results;
                
                // LOG FINAL AGGREGATED RESULTS
                logger.logEvent('final_results', { round: appState.round, ...results });
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

            // Unified Initiation Block (Fixes Array Spreading for Ableton and AI Generator)
            (async () => {
                try {
                    // 1. Send Setup to Ableton Controller (Port 11002)
                    const fc = lastCalculatedResults.fx_choices;
                    const safeFx = (val) => (val === "default" || !val) ? "none" : val;
                    
                    await sendOscAsync(abletonClient, '/performance/setup', [
                        lastCalculatedResults.genre,
                        parseFloat(lastCalculatedResults.tempo),
                        lastCalculatedResults.mode_val,
                        parseInt(lastCalculatedResults.humanGuitarTone),
                        "none", // Placeholder for unused guitar_fx arg
                        safeFx(fc.piano), safeFx(fc.guitar), safeFx(fc.bass), safeFx(fc.strings), safeFx(fc.winds)
                    ]);
                    console.log(`Ableton Performance Setup sent to ${oscTargets.ableton.ip}:${oscTargets.ableton.port}`);

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

                    await sendOscAsync(generatorClient, '/web/drum_seeded_request', [
                        feelMapping[lastCalculatedResults.genre] || "Content_Groove",
                        ...targetProgs
                    ]);
                    console.log(`AI Generation request sent to ${oscTargets.generator.ip}:${oscTargets.generator.port}`);

                    // 3. Send Visuals to Unity (Port 11003)
                    const envMap = { 'haunted forest': 0, 'mars scape': 1, 'laser grid': 2 };
                    const getSkinIdx = (charName) => {
                        if (!charName) return 0;
                        if (charName.match(/vampire|werewolf|moose|goblin/)) return 2;
                        if (charName.match(/flesh|dark|lizard|webbed/)) return 1;
                        return 0; // robots
                    };

                    const m = lastCalculatedResults.band.members;
                    const unityMessages = [
                        { address: '/environment/skybox', args: [envMap[lastCalculatedResults.environment] || 0] },
                        { address: '/avatar/dancer1/skin', args: [getSkinIdx(lastCalculatedResults.dancers.d1.char)] },
                        { address: '/avatar/dancer2/skin', args: [getSkinIdx(lastCalculatedResults.dancers.d2.char)] },
                        { address: '/avatar/dancer1/move', args: [lastCalculatedResults.dancers.d1.move || "default"] },
                        { address: '/avatar/dancer2/move', args: [lastCalculatedResults.dancers.d2.move || "default"] },
                        { address: '/avatar/drum1/skin', args: [getSkinIdx(m.drum1)] },
                        { address: '/avatar/drum2/skin', args: [getSkinIdx(m.drum2)] },
                        { address: '/avatar/guitar/skin', args: [getSkinIdx(m.guitar)] },
                        { address: '/avatar/bass/skin', args: [getSkinIdx(m.bass)] },
                        { address: '/avatar/strings/skin', args: [getSkinIdx(m.strings)] },
                        { address: '/avatar/piano/skin', args: [getSkinIdx(m.piano)] },
                        { address: '/avatar/winds/skin', args: [getSkinIdx(m.winds)] }
                    ];

                    for (const msg of unityMessages) {
                        await sendOscAsync(unityClient, msg.address, msg.args);
                    }
                    console.log(`Unity Visuals sent to ${oscTargets.unity.ip}:${oscTargets.unity.port}`);
                    
                    io.emit("performance_started");
                } catch (error) {
                    console.error("Failed to send OSC initiation batch:", error);
                    io.emit("performance_start_failed", { reason: "osc_batch_send_failed" });
                } finally {
                    abletonClient.close();
                    generatorClient.close();
                    unityClient.close();
                }
            })();
        }

        if (data.command === "reset") {
            appState.phase = "lobby";
            appState.round++;
            submissions = [];
            globalClickCount = 0;
            lastCalculatedResults = null;

            // // Reset Ableton Effects State
            // const abletonClient = new Client(oscTargets.ableton.ip, oscTargets.ableton.port);
            // abletonClient.send('/system/resetall', 1, () => abletonClient.close());
            // Reset OSC state on both audio and visuals pipelines.
            const abletonClient = new Client(oscTargets.ableton.ip, oscTargets.ableton.port);
            const unityClient = new Client(oscTargets.unity.ip, oscTargets.unity.port);
            (async () => {
                try {
                    await sendOscAsync(abletonClient, '/system/resetall', [1]);
                    await sendOscAsync(unityClient, '/system/resetall', []);
                    console.log(`Reset sent to Ableton ${oscTargets.ableton.ip}:${oscTargets.ableton.port} and Unity ${oscTargets.unity.ip}:${oscTargets.unity.port}`);
                } catch (error) {
                    console.error("Failed to send OSC resetall:", error);
                } finally {
                    abletonClient.close();
                    unityClient.close();
                }
            })();
            

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
        
        // LOG LOGIN
        logger.logEvent('logins', { username: req.session.username, action: 'login' });
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
