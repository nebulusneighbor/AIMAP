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
let vetoVotes = {}; // username -> { parameter: value }
let liveVotes = {}; // username -> { votes: {...}, first_vote_at, changes }
let lastCalculatedResults = null;
let freezeTimer = null; // setTimeout handle that freezes voting at the deadline
let appState = {
    phase: "lobby",
    current_question: null,
    round: 1,
    round_start_time: null,
    duration: 45,
    deadline: null,
    auto_mode: false,
    break_duration: 15,
    vote_duration: 45,
    auto_sequence: [["genre", "environment"], ["humanGuitarTone", "character_skin"], ["tempo", "dance_moves"]],
    auto_index: 0,
    global_state: {},
    get serverNow() { return Date.now(); }
};

let globalSessionState = {
    genre: null,
    mode: null,
    tempo: null,
    environment: null,
    humanGuitarTone: null,
    fx_piano: null,
    fx_guitar: null,
    fx_bass: null,
    fx_strings: null,
    fx_winds: null
};
appState.global_state = globalSessionState;

// Headline fields surfaced live on the aggregator + activity ticker
const HEADLINE_FIELDS = {
    genre: "Feel",
    mode: "Mode",
    environment: "Environment",
    d1_move: "Dancer 1",
    d2_move: "Dancer 2",
    tempo: "Tempo"
};

// Emoji the audience can send from their phones (must match vote.html)
const REACTION_EMOJIS = ["❤️", "🔥", "👏", "🤘"];
const REACTIONS_PER_SECOND = 5;
const REACTION_FLUSH_MS = 250;

// Votes can arrive many times per second with a full audience; coalesce the
// tally broadcast so every phone gets at most ~4 updates per second.
const LIVE_TALLY_MIN_MS = 250;
let liveTallyTimer = null, lastLiveTallyAt = 0;
function scheduleLiveTally() {
    if (liveTallyTimer) return;
    const wait = Math.max(0, LIVE_TALLY_MIN_MS - (Date.now() - lastLiveTallyAt));
    liveTallyTimer = setTimeout(() => {
        liveTallyTimer = null;
        lastLiveTallyAt = Date.now();
        io.emit("live_tally", computeLiveTally());
    }, wait);
}

// Reactions from the whole audience are summed and sent to the aggregator(s)
// as one small batch per flush, so 100 phones tapping costs ~4 messages/sec.
let pendingReactions = {};
setInterval(() => {
    if (Object.keys(pendingReactions).length === 0) return;
    io.to("displays").emit("reactions", pendingReactions);
    pendingReactions = {};
}, REACTION_FLUSH_MS);

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

/* ---------- LIVE VOTING ---------- */

// Lightweight running tally for the live aggregator. Unlike calculateAverages()
// it does NOT resolve winners or randomize, so live bars don't flicker on ties.
function computeLiveTally() {
    const dist = { genre: {}, mode: {}, environment: {}, d1_move: {}, d2_move: {} };
    let tempoSum = 0, tempoCount = 0;

    const currentArr = Array.isArray(appState.current_question) ? appState.current_question : (appState.current_question ? [appState.current_question] : []);
    
    currentArr.forEach(c => {
        if(c !== 'tempo') dist[c] = {};
    });

    Object.values(liveVotes).forEach(entry => {
        const v = entry.votes;
        currentArr.forEach(current => {
            if (current === 'tempo' && v.tempo) {
                const t = parseInt(v.tempo);
                if (!isNaN(t)) { tempoSum += t; tempoCount++; }
            } else if (current && v[current] && v[current] !== "") {
                dist[current] = dist[current] || {};
                dist[current][v[current]] = (dist[current][v[current]] || 0) + 1;
            }
        });
    });

    const voted = Object.keys(liveVotes).filter(u => {
        const v = liveVotes[u].votes;
        return currentArr.some(current => {
            if (current === 'tempo') return v.tempo !== undefined;
            return v[current] !== undefined && v[current] !== "";
        });
    });
    
    const connected = [...new Set(Object.values(users).map(u => u.username))];
    const deciding = connected.filter(u => !voted.includes(u));

    return {
        count: voted.length,
        distributions: dist,
        tempo: tempoCount ? Math.round(tempoSum / tempoCount) : 115,
        voted,
        deciding,
        deadline: appState.deadline,
        phase: appState.phase,
        current_question: appState.current_question
    };
}

// Count "real" choices in a vote payload (mirrors the old POST /submit logic).
function countChoices(votes) {
    let choicesMade = 0;
    for (let key in votes) {
        if (key === 'instruments') {
            try { choicesMade += Object.keys(JSON.parse(votes[key])).length; } catch (e) {}
        } else {
            const val = votes[key];
            if (val && val !== "" && val !== "default" && val !== "none") {
                if (key === 'tempo' && val === "115") continue;
                choicesMade++;
            }
        }
    }
    return choicesMade;
}

function freezeVoting() {
    if (appState.phase !== "active") return;
    appState.phase = "frozen";
    if (freezeTimer) { clearTimeout(freezeTimer); freezeTimer = null; }
    io.emit("voting_frozen", {});
    io.emit("phase_change", appState);
}

let autoTimer = null;



function handleAutoCycle() {
    if (!appState.auto_mode) return;
    
    if (appState.phase === "active") {
        // Vote time is up! Auto-lock.
        lockCurrentParameter(true); // true means it was called automatically
    } else if (appState.phase === "break") {
        // Break time is up! Start next vote.
        appState.auto_index = (appState.auto_index + 1) % appState.auto_sequence.length;
        startSingleVote(appState.auto_sequence[appState.auto_index], appState.vote_duration, true);
    }
}

function armFreezeTimer() {
    if (freezeTimer) { clearTimeout(freezeTimer); freezeTimer = null; }
    const ms = appState.deadline - Date.now();
    if (ms <= 0) { 
        if (appState.auto_mode) handleAutoCycle();
        else freezeVoting(); 
        return; 
    }
    freezeTimer = setTimeout(() => {
        if (appState.auto_mode) handleAutoCycle();
        else freezeVoting();
    }, ms);
}


function broadcastVetoDistributions() {
    const distributions = {};
    const lockable = ['genre', 'environment', 'humanGuitarTone', 'character_skin', 'instruments', 'dance_moves', 'tempo'];
    
    lockable.forEach(p => distributions[p] = {});
    
    Object.values(vetoVotes).forEach(userVotes => {
        for (let p in userVotes) {
            const val = userVotes[p];
            if (val && distributions[p]) {
                distributions[p][val] = (distributions[p][val] || 0) + 1;
            }
        }
    });
    
    io.emit("veto_distribution", distributions);
}

function checkVetoThresholds(p) {
    const threshold = Math.max(1, Math.ceil((Object.keys(users).length || 1) / 2));
    const dist = {};
    Object.values(vetoVotes).forEach(userVotes => {
        if (userVotes[p]) {
            dist[userVotes[p]] = (dist[userVotes[p]] || 0) + 1;
        }
    });
    
    for (let val in dist) {
        if (dist[val] >= threshold) {
            console.log(`[VETO] Threshold met for ${p}: ${val} (Will be applied at end of round)`);
            break;
        }
    }
}


function lockCurrentParameter(is_auto) {
    if (freezeTimer) { clearTimeout(freezeTimer); freezeTimer = null; }
    
    const defaultOptions = {
        genre: ['drive/rock', 'pulse/hiphop', 'groove/funky', 'elegant/atmospheric'],
        environment: ['haunted forest', 'mars scape', 'laser grid'],
        humanGuitarTone: ['clean', 'crunch', 'ambient'],
        character_skin: ['monsters', 'robots', 'aliens'],
        instruments: ['full', 'rhythm', 'acoustic'],
        dance_moves: ['hiphop', 'breakdance', 'latin', 'drink'],
        tempo: [90, 110, 130, 150, 170]
    };
    
    const tally = computeLiveTally();
    const currentArr = Array.isArray(appState.current_question) ? appState.current_question : (appState.current_question ? [appState.current_question] : []);
    
    // Evaluate Veto Winners first
    const threshold = Math.max(1, Math.ceil((Object.keys(users).length || 1) / 2));
    const vetoWinners = [];
    const lockable = ['genre', 'environment', 'humanGuitarTone', 'character_skin', 'instruments', 'dance_moves', 'tempo'];
    
    lockable.forEach(vp => {
        if (currentArr.includes(vp)) return; // Only evaluate vetoes for non-active parameters
        const dist = {};
        Object.values(vetoVotes).forEach(userVotes => {
            if (userVotes[vp]) dist[userVotes[vp]] = (dist[userVotes[vp]] || 0) + 1;
        });
        
        let won = null;
        for (let val in dist) {
            if (dist[val] >= threshold) { won = val; break; }
        }
        
        if (won !== null) {
            vetoWinners.push({ p: vp, winner: won });
            // Clear veto votes for this parameter since it passed
            Object.values(vetoVotes).forEach(uv => { if(uv[vp]) delete uv[vp]; });
        }
    });

    // Combine active round parameters and veto winners
    // First, map the active parameters to objects { p, winner }
    const activeWinners = currentArr.map(p => {
        let winner = null;
        if (p === 'tempo') {
            winner = tally.tempo;
        } else {
            const dist = tally.distributions[p];
            if (dist && Object.keys(dist).length > 0) {
                const max = Math.max(...Object.values(dist));
                const winners = Object.keys(dist).filter(k => dist[k] === max);
                winner = winners[Math.floor(Math.random() * winners.length)];
            }
        }

        // Auto-Pilot fallback: Leave as is if no votes
        if (winner === null || winner === undefined || (isNaN(winner) && p === 'tempo')) {
            winner = globalSessionState[p];
            if (winner === undefined) {
                const opts = defaultOptions[p] || ['default'];
                winner = opts[Math.floor(Math.random() * opts.length)];
            }
        }
        return { p, winner };
    });

    const allToUpdate = [...activeWinners, ...vetoWinners];
    
    allToUpdate.forEach(({ p, winner }) => {
        if (winner !== null) {
            globalSessionState[p] = winner;
            
            const abletonClient = new Client(oscTargets.ableton.ip, oscTargets.ableton.port);
            const generatorClient = new Client(oscTargets.generator.ip, oscTargets.generator.port);
            const unityClient = new Client(oscTargets.unity.ip, oscTargets.unity.port);
            
            if (p === 'environment') {
                const envMap = { 'haunted forest': 0, 'mars scape': 1, 'laser grid': 2 };
                sendOscAsync(unityClient, '/environment/skybox', [envMap[winner] || 0]);
            } else if (p === 'genre') {
                sendOscAsync(generatorClient, '/performance/update/genre', [winner, 128, 0, 24, 33, 48, 73]);
                
                const feelMapping = {
                    "drive/rock": "Content_Drive",
                    "pulse/hiphop": "Content_Pulse",
                    "groove/funky": "Content_Groove",
                    "elegant/atmospheric": "Content_Elegance"
                };
                const g = feelMapping[winner] || "Content_Groove";
                const inst = globalSessionState['instruments'] || 'full';
                let progs = [];
                if(inst === 'full') progs = [128, 0, 24, 33, 48, 73];
                if(inst === 'rhythm') progs = [128, 33];
                if(inst === 'acoustic') progs = [0, 48];
                sendOscAsync(generatorClient, '/web/drum_seeded_request', [g, ...progs]);
            } else if (p === 'character_skin') {
                const getSkinIdx = (c) => {
                    if(c==='monsters') return 0;
                    if(c==='robots') return 1;
                    if(c==='aliens') return 2;
                    return 0;
                };
                const idx = getSkinIdx(winner);
                ['dancer1','dancer2','drum1','drum2','guitar','bass','violin','piano','flute'].forEach(av => {
                    sendOscAsync(unityClient, `/avatar/${av}/skin`, [idx]);
                });
            } else if (p === 'dance_moves') {
                sendOscAsync(unityClient, '/avatar/dancer1/move', [winner]);
                sendOscAsync(unityClient, '/avatar/dancer2/move', [winner]);
            } else if (p === 'instruments') {
                const feelMapping = {
                    "drive/rock": "Content_Drive",
                    "pulse/hiphop": "Content_Pulse",
                    "groove/funky": "Content_Groove",
                    "elegant/atmospheric": "Content_Elegance"
                };
                const g = feelMapping[globalSessionState['genre']] || "Content_Groove";
                let progs = [];
                if(winner === 'full') progs = [128, 0, 24, 33, 48, 73];
                if(winner === 'rhythm') progs = [128, 33];
                if(winner === 'acoustic') progs = [0, 48];
                sendOscAsync(generatorClient, '/web/drum_seeded_request', [g, ...progs]);
            } else if (p === 'humanGuitarTone') {
                sendOscAsync(abletonClient, `/performance/update/${p}`, [winner]);
            } else if (p === 'tempo') {
                sendOscAsync(abletonClient, `/performance/update/${p}`, [parseFloat(winner)]);
            } else {
                sendOscAsync(abletonClient, `/performance/update/${p}`, [winner]);
            }
            
            setTimeout(() => { abletonClient.close(); generatorClient.close(); unityClient.close(); }, 500);
        }
    });
    
    // Update UI
    io.emit("aggregator_update", globalSessionState);
    broadcastVetoDistributions(); // Update veto UI with cleared values
    
    // Send performance setup if at the end of the round
    if (appState.auto_mode && appState.auto_index === 2) {
        const abletonClient = new Client(oscTargets.ableton.ip, oscTargets.ableton.port);
        sendOscAsync(abletonClient, '/performance/setup', [1]);
        setTimeout(() => abletonClient.close(), 500);
    }

    if (is_auto) {
        appState.phase = "break";
        appState.duration = appState.break_duration;
        appState.deadline = Date.now() + appState.break_duration * 1000;
        armFreezeTimer();
        io.emit("phase_change", appState);
        io.emit("live_tally", computeLiveTally());
    }
}



function startSingleVote(parameter, duration, is_auto) {
    let finalParams = Array.isArray(parameter) ? [...parameter] : (typeof parameter === 'string' && parameter.includes(',') ? parameter.split(',') : [parameter]);
    if (appState.queued_revisions && appState.queued_revisions.length > 0) {
        finalParams = finalParams.concat(appState.queued_revisions);
        appState.queued_revisions = [];
    }
    finalParams = [...new Set(finalParams)]; // deduplicate

    appState.phase = "active";
    appState.current_question = finalParams;
    appState.duration = parseInt(duration) || 45;
    appState.round_start_time = Date.now();
    appState.deadline = Date.now() + appState.duration * 1000;
    liveVotes = {};
    if (!is_auto) appState.auto_mode = false;
    armFreezeTimer();
    io.emit("phase_change", appState);
    io.emit("live_tally", computeLiveTally());
}

/* ---------- ROUTES ---------- */

app.get('/', (req, res) => {
    if (!req.session.username) {
        return res.sendFile(__dirname + "/public/username.html");
    }
    // Single unified voting page
    return res.sendFile(__dirname + "/public/vote.html");
});

app.get('/aggregator', (req, res) => {
    res.sendFile(__dirname + "/public/aggregator.html");
});

io.on("connection", (socket) => {
    const username = socket.request.session?.username;
    if (username) {
        users[socket.id] = { username: username, mode: socket.request.session?.mode || "both" };
        scheduleLiveTally(); // new participant shows up on the aggregator 
    }
    
    // Initial sync
    socket.emit("phase_change", appState);
    broadcastVetoDistributions();

    socket.on("veto_value_vote", data => {
        const username = socket.request.session?.username;
        if (!username || !data.parameter) return;

        if (!vetoVotes[username]) vetoVotes[username] = {};
        
        // If they select empty, it means they cleared their veto
        if (!data.value || data.value === "") {
            delete vetoVotes[username][data.parameter];
        } else {
            vetoVotes[username][data.parameter] = data.value;
        }
        
        broadcastVetoDistributions();
        io.to("displays").emit("vote_activity", { username: username, field: "VETO " + data.parameter, value: data.value || "cleared" });
        
        checkVetoThresholds(data.parameter);
    });

    // Livestream-style emoji reactions. Whitelisted and rate limited per socket
    // so a single phone can't flood the projector.
    let reactionWindowStart = 0, reactionCount = 0;
    socket.on("reaction", data => {
        const emoji = data && data.emoji;
        if (!REACTION_EMOJIS.includes(emoji)) return;
        const now = Date.now();
        if (now - reactionWindowStart > 1000) { reactionWindowStart = now; reactionCount = 0; }
        if (++reactionCount > REACTIONS_PER_SECOND) return;
        pendingReactions[emoji] = (pendingReactions[emoji] || 0) + 1;
    });

    // Aggregator + admin screens join this room for display-only traffic
    socket.on("join_displays", () => {
        socket.join("displays");
        socket.emit("live_tally", computeLiveTally()); // show who's here right away
    });

    socket.on("vote_update", (data) => {
        if (appState.phase !== "active") return; // ignore once frozen / not voting
        const username = socket.request.session?.username;
        if (!username) return;

        const votes = (data && data.votes) ? data.votes : {};
        const now = Date.now();
        const prev = liveVotes[username];
        const first_vote_at = prev ? prev.first_vote_at : now;
        const prevVotes = prev ? prev.votes : {};

        const seconds_to_submit = appState.round_start_time
            ? ((first_vote_at - appState.round_start_time) / 1000).toFixed(2)
            : "0.00";

        const snapshot = {
            ...votes,
            username,
            submitted_at: new Date(now).toISOString(),
            seconds_to_submit,
            total_choices_made: countChoices(votes)
        };

        // Emit activity for any headline field that actually changed value.
        Object.keys(HEADLINE_FIELDS).forEach(f => {
            const nv = votes[f];
            if (nv && nv !== "" && nv !== prevVotes[f]) {
                io.to("displays").emit("vote_activity", { username, field: HEADLINE_FIELDS[f], value: nv });
            }
        });

        liveVotes[username] = {
            votes: snapshot,
            first_vote_at,
            changes: (prev ? prev.changes : 0) + 1
        };

        // Log every change for post-hoc analysis of how people revise their votes.
        logger.logEvent('vote_changes', { username, change_no: liveVotes[username].changes, ...votes });

        scheduleLiveTally();
    });

    socket.on("admin_command", (data) => {
        // LOG ADMIN ACTION
        logger.logEvent('admin_actions', data);

        if (data.command === "start_vote") {
            if (data.abletonIP) { oscTargets.ableton.ip = data.abletonIP; oscTargets.generator.ip = data.abletonIP; }
            if (data.unityIP) oscTargets.unity.ip = data.unityIP;
            startSingleVote(data.parameter, data.duration, false);
        }
        if (data.command === "start_auto_cycle") {
            if (data.abletonIP) { oscTargets.ableton.ip = data.abletonIP; oscTargets.generator.ip = data.abletonIP; }
            if (data.unityIP) oscTargets.unity.ip = data.unityIP;
            appState.auto_mode = true;
            appState.vote_duration = parseInt(data.vote_duration) || 45;
            appState.break_duration = parseInt(data.break_duration) || 15;
            appState.auto_index = 0;
            startSingleVote(appState.auto_sequence[appState.auto_index], appState.vote_duration, true);
        }
        if (data.command === "stop_auto_cycle") {
            appState.auto_mode = false;
            appState.phase = "lobby";
            appState.deadline = null;
            appState.current_question = null;
            if (freezeTimer) { clearTimeout(freezeTimer); freezeTimer = null; }
            io.emit("phase_change", appState);
        }

        if (data.command === "extend_time") {
            if (appState.phase !== "active") return;
            appState.deadline += 15000;
            armFreezeTimer();
            io.emit("phase_change", appState);
        }

        if (data.command === "restart_time") {
            if (appState.phase !== "active" && appState.phase !== "frozen") return;
            appState.phase = "active"; // reopen if it had frozen
            appState.deadline = Date.now() + (appState.duration || 60) * 1000;
            armFreezeTimer();
            io.emit("phase_change", appState);
            io.emit("live_tally", computeLiveTally());
        }

        if (data.command === "lock_single_parameter") {
            if (data.abletonIP) { oscTargets.ableton.ip = data.abletonIP; oscTargets.generator.ip = data.abletonIP; }
            if (data.unityIP) oscTargets.unity.ip = data.unityIP;
            lockCurrentParameter(false);
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
                        { address: '/avatar/violin/skin', args: [getSkinIdx(m.strings)] },
                        { address: '/avatar/piano/skin', args: [getSkinIdx(m.piano)] },
                        { address: '/avatar/flute/skin', args: [getSkinIdx(m.winds)] }
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
            appState.deadline = null;
            liveVotes = {};
            submissions = [];
            globalClickCount = 0;
            lastCalculatedResults = null;
            vetoVotes = {};
            
            if (freezeTimer) { clearTimeout(freezeTimer); freezeTimer = null; }

            // Reset Ableton Effects State
            const abletonClient = new Client(oscTargets.ableton.ip, oscTargets.ableton.port);
            abletonClient.send('/system/resetall', 1, () => abletonClient.close());

            io.emit("phase_change", appState);
            io.emit("aggregator_reset");
            io.emit("redirect_clients", { page: "/" });
        }
    });

    socket.on("disconnect", () => {
        delete users[socket.id];
        sendUserList();
        scheduleLiveTally(); // keep the aggregator's audience panel current
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
