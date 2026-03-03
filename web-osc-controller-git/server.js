const express = require('express');
const session = require('express-session');
const { Client } = require('node-osc');
const http = require('http');
const { Server } = require('socket.io');

const app = express();
const server = http.createServer(app); // Wrap Express with HTTP
const io = new Server(server); // Attach Socket.IO to the server
const PORT = 3000;

const oscClient = new Client('127.0.0.1', 3333);

app.use(express.json());
app.use(express.static('public'));
app.use(session({
    secret: 'my_super_secret_key',
    resave: false,
    saveUninitialized: true
}));

let globalClickCount = 0;

app.post('/api/login', (req, res) => {
    const { username, password } = req.body;
    // Expanded simple login to allow multiple users to test
    if (password === 'password123') {
        req.session.isAuthenticated = true;
        req.session.username = username; // Store who logged in
        res.json({ success: true, message: 'Logged in successfully', username });
    } else {
        res.status(401).json({ success: false, message: 'Invalid credentials' });
    }
});

const requireAuth = (req, res, next) => {
    if (req.session.isAuthenticated) next();
    else res.status(401).json({ success: false, message: 'Unauthorized.' });
};

app.post('/api/interact', requireAuth, (req, res) => {
    globalClickCount++;
    const user = req.session.username;
    
    // BROADCAST to all connected WebSocket clients
    io.emit('aggregator_update', { 
        count: globalClickCount, 
        message: `${user} triggered an interaction!` 
    });

    res.json({ success: true });
});

app.post('/api/send-osc', requireAuth, (req, res) => {
    const user = req.session.username;
    oscClient.send('/web/button', 1, (err) => {
        if (err) return res.status(500).json({ success: false });
        
        // Broadcast the OSC event to the aggregator too
        io.emit('aggregator_update', { 
            count: globalClickCount, 
            message: `${user} fired an OSC message!` 
        });
        
        res.json({ success: true });
    });
});

// Use server.listen instead of app.listen!
server.listen(PORT, () => {
    console.log(`Server running at http://localhost:${PORT}`);
});