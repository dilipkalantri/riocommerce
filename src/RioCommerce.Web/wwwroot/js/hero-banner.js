// hero-banner.js — drives the homepage centre-mode carousel: centring, autoplay,
// arrows, dots, keyboard, touch swipe, pause-on-hover / tab-hidden, and the
// infinite-loop wrap. Imported by HeroBanner.razor only when there's more than
// one slide. Idempotent: re-running init() rebinds cleanly, dispose() tears down.
//
// Track layout (rendered by Blazor, never mutated here):
//     [clone of last][s0][s1]…[sn-1][clone of first]
//      cell 0         1   2      n    n+1
// `pos` is the CELL index. Moving onto a clone looks identical to the real slide
// it copies, so once the transition ends we jump silently to the real one.

let state = null;

function clearTimer() {
    if (state && state.timer) { clearInterval(state.timer); state.timer = null; }
}

function startTimer() {
    if (!state || !state.autoplay) return;
    clearTimer();
    state.timer = setInterval(() => go(1), state.interval);
}

// Slide the track so cell `pos` is centred in the viewport. Measuring offsetLeft
// keeps this correct regardless of slide width, gap, or the track's negative
// start margin — and transforms (the scale on each card) don't affect layout
// metrics, so the scaling never skews the maths.
function centerOn(pos, animate) {
    const { cells, viewport, track } = state;
    const cell = cells[pos];
    if (!cell) return;

    const offset = cell.offsetLeft - (viewport.clientWidth - cell.offsetWidth) / 2;

    if (!animate) {
        track.style.transition = 'none';
        track.style.transform = `translate3d(${-offset}px, 0, 0)`;
        void track.offsetWidth;          // commit before restoring the transition
        track.style.transition = '';
    } else {
        track.style.transform = `translate3d(${-offset}px, 0, 0)`;
    }

    cells.forEach((c, i) => c.classList.toggle('is-active', i === pos));
}

// Cell index -> real slide index (clones map to the slide they copy).
function logicalIndex(pos) {
    const n = state.count;
    if (pos === 0) return n - 1;
    if (pos === n + 1) return 0;
    return pos - 1;
}

function syncDots() {
    const active = logicalIndex(state.pos);
    state.dots.forEach((d, i) => {
        const on = i === active;
        d.classList.toggle('is-active', on);
        d.setAttribute('aria-selected', on ? 'true' : 'false');
    });
}

function go(delta) {
    if (!state || state.animating || !delta) return;
    state.animating = true;
    state.pos += delta;
    centerOn(state.pos, true);
    syncDots();
}

function goTo(logical) {
    if (!state || state.animating) return;
    go((logical + 1) - state.pos);
}

// Once a move finishes, if we've landed on a bookend clone, hop to the identical
// real slide with transitions off. Visually nothing changes; the carousel is now
// back inside the real range and can keep going in either direction forever.
function onTransitionEnd(e) {
    if (!state || e.target !== state.track || e.propertyName !== 'transform') return;
    state.animating = false;

    const n = state.count;
    if (state.pos === 0) { state.pos = n; centerOn(state.pos, false); }
    else if (state.pos === n + 1) { state.pos = 1; centerOn(state.pos, false); }
}

export function init() {
    dispose();   // clean slate if re-initialised

    const root = document.querySelector('.hjc-banner');
    if (!root) return;

    const viewport = root.querySelector('.hjc-banner__viewport');
    const track = root.querySelector('.hjc-banner__track');
    if (!viewport || !track) return;

    const cells = Array.from(track.querySelectorAll('.hjc-banner__slide'));
    // cells = real slides + the two bookend clones; fewer than 3 means nothing to drive.
    if (cells.length < 3) return;

    const dots = Array.from(root.querySelectorAll('.hjc-banner__dot'));
    const interval = Math.max(1500, parseInt(root.dataset.interval || '5000', 10));
    const autoplay = root.dataset.autoplay === 'true';
    const pauseHover = root.dataset.pauseHover === 'true';

    state = {
        root, viewport, track, cells, dots,
        count: cells.length - 2,      // real slides
        pos: 1,                       // first real slide
        interval, autoplay, pauseHover,
        timer: null, animating: false, handlers: []
    };

    const on = (el, ev, fn, opts) => { el.addEventListener(ev, fn, opts); state.handlers.push([el, ev, fn, opts]); };

    // CSS already parks the first real slide in the centre; this makes it exact
    // (and corrects for any sub-pixel rounding) without animating.
    centerOn(state.pos, false);
    syncDots();

    on(track, 'transitionend', onTransitionEnd);

    // Arrows
    root.querySelectorAll('.hjc-banner__arrow').forEach(btn => {
        const d = btn.dataset.dir === 'next' ? 1 : -1;
        on(btn, 'click', () => { go(d); startTimer(); });
    });

    // Dots
    dots.forEach(dot => on(dot, 'click', () => { goTo(parseInt(dot.dataset.go, 10) || 0); startTimer(); }));

    // Keyboard (when the carousel has focus within)
    on(root, 'keydown', (e) => {
        if (e.key === 'ArrowLeft') { go(-1); startTimer(); }
        else if (e.key === 'ArrowRight') { go(1); startTimer(); }
    });

    // Pause on hover
    if (autoplay && pauseHover) {
        on(root, 'mouseenter', clearTimer);
        on(root, 'mouseleave', startTimer);
    }

    // Pause when the tab is hidden (saves work, avoids "jump" on return)
    on(document, 'visibilitychange', () => { if (document.hidden) clearTimer(); else startTimer(); });

    // Touch swipe
    let startX = null;
    on(root, 'touchstart', (e) => { startX = e.touches[0].clientX; clearTimer(); }, { passive: true });
    on(root, 'touchend', (e) => {
        if (startX === null) return;
        const dx = e.changedTouches[0].clientX - startX;
        if (Math.abs(dx) > 40) go(dx < 0 ? 1 : -1);
        startX = null;
        startTimer();
    }, { passive: true });

    // Widths are percentage-based, so re-centre (without animating) on resize.
    let rz = null;
    on(window, 'resize', () => {
        clearTimeout(rz);
        rz = setTimeout(() => { if (state) centerOn(state.pos, false); }, 120);
    });

    startTimer();
}

export function dispose() {
    clearTimer();
    if (state && state.handlers) {
        for (const [el, ev, fn, opts] of state.handlers) {
            try { el.removeEventListener(ev, fn, opts); } catch { }
        }
    }
    state = null;
}
