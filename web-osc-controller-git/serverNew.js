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

app.use(session({
    secret: 'music_secret',
    resave: false,
    saveUninitialized: true
}));

let globalClickCount = 0;

/* ---------- ROOT PAGE LOGIC ---------- */

app.get('/', (req,res)=>{

    if(!req.session.username){
        res.sendFile(__dirname + "/public/username.html");
    } else {
        res.sendFile(__dirname + "/public/submit.html");
    }

});

/* ---------- SET USERNAME ---------- */

app.post('/set-username',(req,res)=>{

    const { username } = req.body;

    if(username && username.trim() !== ""){
        req.session.username = username.trim();
    }

    res.redirect("/");

});

/* ---------- SUBMIT TRACK ---------- */

app.post('/submit',(req,res)=>{

    const { genre, instruments, tempo, color } = req.body;

    const username = req.session.username;

    if(username && genre){

        globalClickCount++;

        io.emit("aggregator_update",{
            count: globalClickCount,
            message: `${username} submitted a track`
        });

        io.emit("new_submission",{
            username,
            genre,
            tempo,
            instruments,
            color
        });

    }

    res.redirect("/");

});

/* ---------- STATIC FILES ---------- */
/* IMPORTANT: placed AFTER routes */

app.use(express.static('public'));

/* ---------- SERVER ---------- */

server.listen(PORT,()=>{
    console.log(`Server running at http://localhost:${PORT}`);
});