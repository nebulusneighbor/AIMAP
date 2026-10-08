// Top-of-screen bar that fills while the newly generated file loads into Ableton.
// Tempo, guitar tone and visual changes are applied by the server when it completes.
// Expects the page's `socket` to already exist.
(function () {
    const style = document.createElement('style');
    style.textContent = `
        #genBar { position: fixed; top: 0; left: 0; right: 0; height: 10px; z-index: 2000;
                  background: rgba(255,255,255,0.08); opacity: 0; transition: opacity 0.4s; pointer-events: none; }
        #genBar.on { opacity: 1; }
        #genBarFill { height: 100%; width: 0; background: linear-gradient(90deg, var(--secondary-color, #22d3ee), var(--primary-color, #a855f7));
                      box-shadow: 0 0 12px var(--primary-color, #a855f7); }
        #genBarLabel { position: absolute; top: 12px; left: 50%; transform: translateX(-50%); white-space: nowrap;
                       font-size: 0.9rem; font-weight: 800; letter-spacing: 0.08em; text-transform: uppercase;
                       color: var(--primary-color, #a855f7); text-shadow: 0 0 6px rgba(0,0,0,0.8); }
    `;
    document.head.appendChild(style);

    const bar = document.createElement('div');
    bar.id = 'genBar';
    bar.innerHTML = '<div id="genBarFill"></div><div id="genBarLabel">Generating next section…</div>';
    document.body.appendChild(bar);
    const fill = bar.querySelector('#genBarFill');
    let hideTimer = null;

    function start(duration, remaining) {
        clearTimeout(hideTimer);
        const startFrac = Math.min(1, Math.max(0, 1 - remaining / duration));
        fill.style.transition = 'none';
        fill.style.width = (startFrac * 100) + '%';
        bar.classList.add('on');
        void fill.offsetWidth; // commit the start width before animating
        fill.style.transition = `width ${Math.max(0, remaining)}ms linear`;
        fill.style.width = '100%';
        // Fallback hide in case the "applied" event is missed
        hideTimer = setTimeout(finish, remaining + 1500);
    }

    function finish() {
        clearTimeout(hideTimer);
        fill.style.transition = 'width 0.2s linear';
        fill.style.width = '100%';
        hideTimer = setTimeout(() => bar.classList.remove('on'), 400);
    }

    socket.on('generation_loading', d => start(d.duration, d.remaining));
    socket.on('generation_applied', finish);
    // Catch up if this page connects mid-load
    let synced = false;
    socket.on('phase_change', s => {
        if (synced) return;
        synced = true;
        if (s.apply_at && s.serverNow && s.apply_at > s.serverNow) {
            start(s.apply_duration, s.apply_at - s.serverNow);
        }
    });
})();
