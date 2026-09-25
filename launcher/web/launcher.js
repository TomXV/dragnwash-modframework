'use strict';
/* Drag'n Wash launcher page: the boards of the launcher's window, driven by events from the launcher (C#).

   Bridge: the page posts {cmd, ...} as JSON with chrome.webview.postMessage; the launcher calls
   window.dnw(event) with a plain object. The launcher sends nothing before the page has posted 'ready'.

   The launcher's work can run far ahead of the pictures (a fast update is over in a blink), so the updating
   board keeps its own queue: every event waits its turn, and each step's picture stays up for at least the
   time it has in the approved mock before the next step shows. */

// ---------- bridge ----------

function send(cmd, extra) {
  const text = JSON.stringify({ cmd, ...extra });
  if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(text);
  else console.info('launcher ->', text);  // in a plain browser (testing) just show what would be sent
}

// commands that must go out once at most (the window is about to close or start the game)
const sentOnce = new Set();
function sendOnce(cmd) {
  if (sentOnce.has(cmd)) return;
  sentOnce.add(cmd);
  send(cmd);
}

// ---------- small helpers (the shared ones are in webui/kit.js) ----------

let lang = 'en';
function t(key, ...args) {
  const v = STRINGS[lang][key];
  return typeof v === 'function' ? v(...args) : v;
}

// ---------- formatting ----------

// sizes as in the mock: 829 KB, 3.4 MB
function fmtSize(bytes) {
  if (!(bytes > 0)) return '';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
}

const pad = (n) => String(n).padStart(2, '0');

// when the game last checked, in local time: "Sep 24, 21:40" / "9 月 24 日 21:40" (with the year if not this year)
function fmtChecked(iso) {
  const d = new Date(iso || '');
  if (!iso || isNaN(d)) return '';
  const thisYear = d.getFullYear() === new Date().getFullYear();
  const time = `${pad(d.getHours())}:${pad(d.getMinutes())}`;
  if (lang === 'ja') return (thisYear ? '' : `${d.getFullYear()} 年 `) + `${d.getMonth() + 1} 月 ${d.getDate()} 日 ${time}`;
  const month = d.toLocaleString('en', { month: 'short' });
  return thisYear ? `${month} ${d.getDate()}, ${time}` : `${month} ${d.getDate()}, ${d.getFullYear()}`;
}

// a release's day, in local time: 2026-09-24
function fmtDay(iso) {
  const d = new Date(iso || '');
  if (!iso || isNaN(d)) return '';
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

// "Drag'n Wash Localization" → "Localization" where space is short, as the mock does
const shortName = (name) => String(name || '').replace(/^drag'?n\s*wash\s+/i, '');

// the launcher's long path in the launch options, cut down to "…\DragNWash.Installer\Launcher.exe"
const shortLauncher = (options) => String(options || '').replace(/"[^"]*[\\/](DragNWash\.Installer[\\/]Launcher\.exe)"/gi, '"…\\$1"');
const modLabel = (name, version) => `${shortName(name)} ${version || ''}`.trim();

// ---------- the shared parts' markup ----------

LogoIntro.build($('intro'));
Pics.stage($('stage'), ['wait', 'dl', 'chk', 'bak', 'modup', 'steam', 'opt', 'optx']).box.id = 'pictures';

// ---------- state ----------

const S = {
  init: null,
  mods: [],        // from init
  chosen: [],      // the mods being updated
  picked: null,    // the mod whose notes show on the list
  skipped: {},     // "Skip this version", per mod
  restart: false,  // update after quitting: the game starts again at the end
  wait: false,     // ... and the first step waits for the game to close
  option: null,    // 'on' or 'off': the launch option switched from the Mods screen, on the updating board
  board: '',       // intro | list | upd | fail
};

// ---------- header ----------

const Head = {
  reveal(animate) {
    const head = $('head');
    if (!head.classList.contains('veiled')) return;
    head.classList.remove('veiled');
    if (animate && !reduced()) replay(head, 'reveal');
    head.classList.add('shimmer');
  },
  state(key) { swapText($('state'), t(key)); },
  logoAway() {
    const logo = $('hlogo');
    logo.classList.remove('back', 'landing', 'land');
    replay(logo, 'away');
  },
  logoBack() {
    const logo = $('hlogo');
    if (!logo.classList.contains('away')) return;
    logo.classList.remove('away');
    replay(logo, 'back');
  },
};

// ---------- 0: logo intro ----------

// the intro's timeline is shared with the installer: webui/intro.js
const INTRO = LogoIntro.T;
const { typed, withDots, leaves } = LogoIntro;

const Intro = {
  timers: new Timers(),
  variant: 'none',
  live: false,  // still playing, so a click or a key skips it

  start(variant) {
    this.variant = variant;
    S.board = 'intro';
    const node = $('intro');
    node.hidden = false;
    LogoIntro.moments(node, variant === 'found');
    const I = INTRO;
    this.status(false);
    replay(node, 'play');
    if (reduced()) {
      // the logo and the last line just show, for a moment to read them
      this.timers.after(1, () => (variant === 'found' ? this.handoff() : this.done()));
      return;
    }
    this.live = true;
    if (variant === 'found') {
      this.timers.after(I.ho, () => this.handoff());
    } else {
      // once "Starting the game" is up, the last half second just plays out
      this.timers.after(I.go, () => { this.live = false; });
      this.timers.after(I.go + I.end, () => this.done());
    }
  },

  // the status line under the logo; fromGo: only "Starting the game", from now (after a skip)
  status(fromGo) {
    const ist = $('ist');
    const I = INTRO;
    const found = this.variant === 'found';
    const last = t(found ? 'introFound' : 'introStarting');
    ist.textContent = '';
    ist.append(el('span', 'sr', last));
    if (reduced()) {
      ist.append(typed(last, 0, 0, found ? 'acc' : ''));
      return;
    }
    if (fromGo) {
      ist.append(typed(last, 0, I.goLen));
      return;
    }
    ist.append(leaves(withDots(typed(t('introChecking'), I.chk, I.chkLen), I.dots), I.res - .16));
    if (found) {
      ist.append(leaves(typed(last, I.res, I.resLen, 'acc'), I.ho));
    } else {
      ist.append(leaves(typed(t('introNone'), I.res, I.resLen), I.go - .15));
      ist.append(typed(last, I.go, I.goLen));
    }
  },

  // a click or a key: the logo stands whole at once; with no updates, "Starting the game" and the bar fill up
  skip() {
    if (!this.live) return;
    this.live = false;
    this.timers.clear();
    $('intro').classList.add('skipped');
    if (this.variant === 'found') {
      this.handoff();
    } else {
      this.status(true);
      this.timers.after(INTRO.end, () => this.done());
    }
  },

  // the bar is full: the launcher closes the window, then starts the game
  done() {
    this.live = false;
    sendOnce('play');
  },

  // updates found: the logo moves up into the header while the header and the list fade in under it
  handoff() {
    this.live = false;
    this.timers.clear();
    const node = $('intro');
    const logo = $('hlogo');
    Head.state('stateList');
    if (reduced()) {
      node.hidden = true;
      Head.reveal(false);
      List.show();
      return;
    }
    LogoIntro.aim($('big'), logo);
    logo.classList.add('landing');
    Head.reveal(true);
    List.show(true);
    node.classList.add('handoff');
    this.timers.after(.6, () => logo.classList.remove('landing'));
    this.timers.after(.9, () => { node.hidden = true; });
  },
};

// any click or key skips the intro
window.addEventListener('pointerdown', () => Intro.skip(), true);

// ---------- 1: updates available ----------

const List = {
  build() {
    const mods = S.mods;
    $('list-title').textContent = t('listTitle', mods.length);
    $('list-lead').textContent = t('listLead', fmtChecked(S.init.checkedUtc));
    const box = $('mods');
    box.textContent = '';
    mods.forEach((m, i) => box.append(this.row(m, i)));
    if (mods.length) this.pick(mods[0].guid);
    this.total();
  },

  row(m, i) {
    const row = el('div', 'mod');
    row.dataset.guid = m.guid;
    row.style.setProperty('--t', sec(.15 + .1 * i));
    if (m.installable) {
      const box = el('input', 'ck');
      box.type = 'checkbox';
      box.checked = !!m.selected;
      box.setAttribute('aria-label', t('installMod', m.name));
      box.addEventListener('change', () => {
        m.selected = box.checked;
        this.total();
      });
      row.append(box);
    } else {
      const mark = el('span', 'ext', '↗');
      mark.setAttribute('aria-hidden', 'true');
      row.append(mark);
    }
    const text = el('div', 'mt');
    const pick = el('button', 'pick');
    pick.type = 'button';
    const version = el('span', 'mv');
    version.append(`${m.from} `, el('b', null, '→'), ` ${m.to}`);
    const extra = m.installable ? fmtSize(m.size) : t('cantHere');
    // as in the mock: a wide gap before the size, a plain one before "can't be installed from here"
    if (extra) version.append(m.installable ? `  ·  ${extra}` : ` · ${extra}`);
    pick.append(el('span', 'mn', m.name), version);
    text.append(pick);
    // a mod without an installer manifest: never installed from here, only its release page
    if (!m.installable) {
      const line = el('span', 'mv');
      const link = el('button', 'llink', t('openRelease'));
      link.type = 'button';
      link.addEventListener('click', () => send('open', { guid: m.guid }));
      line.append(link);
      text.append(line);
    }
    row.append(text);
    row.addEventListener('click', (e) => {
      if (!e.target.closest('input, .llink')) this.pick(m.guid);
    });
    return row;
  },

  // shows a mod's release notes on the right
  pick(guid) {
    const m = S.mods.find((x) => x.guid === guid);
    if (!m) return;
    S.picked = guid;
    for (const row of $('mods').children) {
      const on = row.dataset.guid === guid;
      row.classList.toggle('sel', on);
      row.querySelector('.pick').setAttribute('aria-pressed', on);
    }
    $('notes-name').textContent = `${m.name} ${m.to}`;
    $('skip').checked = !!S.skipped[guid];
    $('notes-cant').hidden = !!m.installable;
    const notes = m.notes || {};
    const box = $('notes');
    box.textContent = '';
    box.scrollTop = 0;
    const heading = el('h4', null, m.to);
    const day = fmtDay(notes.publishedAt);
    if (day) heading.append(el('span', null, day));
    box.append(heading);
    const body = ReleaseNotes.render(notes.body, lang);
    if (body.childNodes.length) box.append(body);
    else box.append(el('p', 'cut', t('notesNone')));
    if (notes.truncated) box.append(el('p', 'cut', t('notesCut')));
  },

  // "Selected: 2 (4.2 MB)"; Update and play needs at least one
  total() {
    const chosen = S.mods.filter((m) => m.installable && m.selected);
    const known = chosen.length && chosen.every((m) => m.size > 0);
    const size = known ? fmtSize(chosen.reduce((sum, m) => sum + m.size, 0)) : '';
    $('total').textContent = t('selected', chosen.length, size);
    $('list-update').disabled = chosen.length === 0;
  },

  // the list after the intro's hand-off: the rows come in one after another, then the notes
  show(reveal) {
    const node = $('list');
    Motion.reset(node);
    Motion.show(node);
    if (reveal) replay(node, 'reveal');
    else node.classList.remove('reveal');
    replay(node, 'play');
    S.board = 'list';
  },

  // back from the updating board (Cancel): the list comes in from the left, in reading order
  enter(kind) {
    const node = $('list');
    Motion.reset(node);
    node.classList.remove('reveal', 'play');
    Motion.enterBoard(node, kind);
    S.board = 'list';
  },

  // "Update and play": the command goes at once, the button gives its little press and the list leaves to the
  // left; the updating board starts coming in just before it is gone
  update() {
    if ($('list-update').disabled || S.board !== 'list') return;
    S.chosen = S.mods.filter((m) => m.installable && m.selected);
    S.board = 'upd';
    send('update', { mods: S.chosen.map((m) => m.guid) });
    $('list-update').classList.add('pressed');
    const node = $('list');
    node.classList.remove('reveal');
    const at = Motion.change(node, 'fwd', () => Head.state('stateUpdating'));
    Updating.begin(at, true, 'fwd');
  },
};

$('list-update').addEventListener('click', () => List.update());
$('list-play').addEventListener('click', () => sendOnce('play'));
$('notes-open').addEventListener('click', () => send('open', { guid: S.picked }));
$('skip').addEventListener('change', (e) => {
  const on = e.target.checked;
  S.skipped[S.picked] = on;
  send('skip', { guid: S.picked, on });
  // the row stays where it is, a little dimmer, with a small tag
  const row = [...$('mods').children].find((r) => r.dataset.guid === S.picked);
  if (row) Motion.skip(row, on, t('skippedTag'));
});

// ---------- the countdown card ----------

const Countdown = {
  seconds: 10,
  timers: new Timers(),

  // counts 10 down to 1 after `delay` seconds, then starts the game; Start now starts it at once
  start(card, delay) {
    this.stop();
    const clock = card.querySelector('.cd');
    const now = card.querySelector('.start-now');
    for (const b of card.querySelectorAll('button')) b.disabled = false;
    card.classList.remove('counting');
    card.style.setProperty('--cd', this.seconds + 's');
    this.show(clock, this.seconds, false);
    this.timers.after(delay, () => {
      replay(card, 'counting');
      now.focus({ preventScroll: true });
      for (let k = 1; k < this.seconds; k++) this.timers.after(k, () => this.show(clock, this.seconds - k, true));
      this.timers.after(this.seconds, () => this.go(card, 'play'));
    });
  },

  // the number, and the ring when nothing moves; a screen reader hears it at the start and at 3 only
  show(clock, n, tick) {
    const num = clock.querySelector('.num');
    num.textContent = n;
    if (tick) replay(num, 'tick');
    clock.querySelector('.run').style.strokeDashoffset = reduced() ? 126 * (this.seconds - n) / this.seconds : '';
    if (n === this.seconds || n === 3) clock.querySelector('.cd-text').textContent = t('cdLeft', n);
  },

  // Start now, the end of the count, or Don't start it now: the card is done, and the window goes
  go(card, cmd) {
    this.stop();
    for (const b of card.querySelectorAll('button')) b.disabled = true;
    sendOnce(cmd);
  },

  stop() { this.timers.clear(); },
};

for (const card of document.querySelectorAll('.again')) {
  card.querySelector('.start-now').addEventListener('click', () => Countdown.go(card, 'play'));
  card.querySelector('.not-now').addEventListener('click', () => Countdown.go(card, 'close'));
}

// ---------- 2 and B: updating ----------

/* Pacing, in seconds, from the mock's timeline (flow.py). The board's clock starts when it gets .play. */
const PACE = {
  land: 1.3,                           // the flying logo lands above the bar; the gear and the sponge start working
  firstWait: .9, firstStep: 1.9,       // the earliest the first step starts: "wait", or any other (once the bar is in)
  wait: 1, dl: 2.2, bak: 1.8, ins: 1.6, // the least time each step's picture stays up
  closing: .4,                         // the game window shrinking away at the end of "wait"
  fileMin: .4,                         // the least time one file's name stays on the sub line while downloading
  sweep: 1.4, tickGap: .8, chkTail: .6, // check: one lens sweep per zip, next zip after a tick, pause after the last
  beat: 2.2,                           // the gear (a tooth every 0.2 s) and the scrub (1.1 s) are both at rest every 2.2 s
  countdown: 1.5,                      // from the all-in moment to the countdown starting (the celebration comes first)
  stepIn: .5, stepGap: .15,            // the steps come in one after another (from the list or Try again: .17, .06)
  steam: 1.8, opt: 1.8,                // the launch option's steps: closing Steam (its window shrinks away), the change
  mod: 1.4, modLoop: 2.4,              // installing: the least time one mod's icon stays up (it turns over at .9 s),
                                       // and one round of its picture
  failAfter: 2.4,                      // the launch option: a failure shows once its step has been up this long
};

/* What the bar and the percent show, as in the mock: download 0–45 % (by bytes), check 45–60 % (by ticks),
   backup 60–72 %, install 72–100 %. Nothing runs faster than its step's minimum time. */
const BAR = { dl: 45, chk: 60, bak: 72 };

const STEP_NAME = { wait: 'stepWait', dl: 'stepDl', chk: 'stepChk', bak: 'stepBak', ins: 'stepIns', steam: 'stepSteam', opt: 'stepOpt' };
// the picture each step shows: installing shows the icon of the mod going in, and the launch option's change its
// chip going in or coming out
const PICTURE = { ins: 'modup', opt: () => (S.option === 'off' ? 'optx' : 'opt') };
const pictureOf = (key) => (typeof PICTURE[key] === 'function' ? PICTURE[key]() : PICTURE[key] || key);

const PHASE_NAME = { wait: 'phaseWait', dl: 'phaseDl', chk: 'phaseChk', bak: 'phaseBak', ins: 'phaseIns', steam: 'phaseSteam', opt: 'phaseOpt' };

const Updating = {
  timers: new Timers(),
  queue: [],
  pumpTimer: 0,
  raf: 0,

  // prepares the board; it starts `delay` seconds from now. kind 'fwd': it comes in from the list or Try again
  begin(delay, enter, kind) {
    this.stop();
    this.kind = kind || '';
    Object.assign(this, {
      z: performance.now() + delay * 1000,
      cur: null, curAt: 0,              // the step on screen, and since when
      closingAt: null,                  // "wait": when the game window started shrinking
      files: [], fileIdx: 0, fileAt: 0, fileMin: 0, dlShare: 0,
      zips: 0, ticks: 0, sweepIdx: 1, sweepEnd: null, chkDoneAt: null,
      proceeded: false, cancelling: false,
      mod: -1, modAt: null, modEvents: false, modSeen: new Set(), // installing: the mod whose icon is up, since when,
                                        // whether the launcher says which, and the ones shown so far
      T: null, insFrom: 0, insFromAt: 0, // the all-in moment, and where the bar left off when it became known
      shown: 0, lastFrame: 0, pct: -1,
    });
    this.build();
    this.timers.after(delay, () => this.start(enter, kind));
  },

  // stops everything (the board is being left)
  stop() {
    this.timers.clear();
    clearTimeout(this.pumpTimer);
    cancelAnimationFrame(this.raf);
    this.queue = [];
    Countdown.stop();
  },

  now() { return (performance.now() - this.z) / 1000; },

  build() {
    const names = S.chosen.map((m) => modLabel(m.name, m.to));
    $('upd-title').textContent = t(S.restart ? 'restartTitle' : 'updTitle');
    $('upd-lead').textContent = S.restart ? t('restartLead') : t('updLead', names);
    $('upd-net').textContent = S.restart ? t('netRestart', S.init.backupPath || '') : t('netUpdating');
    const cancel = $('upd-cancel');
    cancel.hidden = S.restart;
    cancel.disabled = false;

    this.keys = S.option ? ['wait', 'steam', 'opt', 'go'] : [...(S.wait ? ['wait'] : []), 'dl', 'chk', 'bak', 'ins', 'go'];
    const size = S.chosen.every((m) => m.size > 0) ? fmtSize(S.chosen.reduce((sum, m) => sum + m.size, 0)) : '';
    const small = S.option ? { steam: t('smallSteam'), opt: t('smallOpt') }
      : { dl: t('smallDl', S.chosen.length, size), chk: t('smallChk'), bak: t('smallBak'), ins: t('smallIns') };
    const steps = $('steps');
    steps.textContent = '';
    const [stepIn, stepGap] = this.kind === 'fwd' ? [.17, .06] : [PACE.stepIn, PACE.stepGap];
    this.keys.forEach((key, i) => {
      const li = el('li', 'st todo');
      li.dataset.step = key;
      li.style.setProperty('--t', sec(stepIn + stepGap * i));
      const text = el('div');
      text.append(el('span', null, key === 'go' ? t(S.restart ? 'stepGoAgain' : 'stepGo') : t(STEP_NAME[key])));
      if (small[key]) text.append(el('small', null, small[key]));
      li.append(el('span', 'mk'), text);
      steps.append(li);
    });

    for (const pic of $('pictures').children) pic.classList.remove('on', 'leaving', 'closing');
    for (const part of $('pictures').querySelectorAll('.lens, .ok, .doc')) part.classList.remove('sweep', 'pop', 'gone', 'swap');
    $('plabel').textContent = '';
    $('psub').textContent = '';
    $('pfill').style.width = '0';
    $('pfill').classList.remove('full');
    $('pct').textContent = '0%';
    $('log-lines').textContent = '';
    $('upd-again').classList.remove('counting');
    Motion.reset($('upd'));
    $('upd').classList.remove('play', 'fin', 'enter');
  },

  start(enter, kind) {
    this.z = performance.now();
    const node = $('upd');
    Motion.show(node);
    if (enter) node.classList.add('enter');
    if (kind) Motion.setKind(node, kind);
    replay(node, 'play');
    // with motion reduced the board's own build-up doesn't run: its parts just fade in
    if (enter && reduced()) Motion.enterBoard(node, 'fwd');
    Motion.fly($('fly'), $('hlogo'), $('mlogo'));
    Head.logoAway();
    replay($('fly'), 'go');
    this.lastFrame = performance.now();
    this.raf = requestAnimationFrame(() => this.frame());
    this.pump();
  },

  // ----- the queue -----

  push(e) {
    this.queue.push(e);
    this.pump();
  },

  // applies queued events in order, each once its moment has come
  pump() {
    clearTimeout(this.pumpTimer);
    while (this.queue.length) {
      const now = this.now();
      const wait = this.gate(this.queue[0], now);
      if (wait === Infinity) return;  // something else (a sweep, an event) will pump again
      if (wait > .001) {
        this.pumpTimer = setTimeout(() => this.pump(), wait * 1000);
        return;
      }
      this.apply(this.queue.shift(), now);
    }
  },

  // how long an event still has to wait (0 = now)
  gate(e, now) {
    switch (e.type) {
      case 'step':
        if (!this.cur) return (e.step === 'wait' ? PACE.firstWait : PACE.firstStep) - now;
        return this.stepGate(now);
      case 'done':
        return this.cur ? this.stepGate(now) : PACE.firstStep - now;
      case 'failed':
        // the launch option's failures can come the moment their step starts; they wait for it to be seen
        if (!S.option) return 0;
        return this.cur ? this.curAt + PACE.failAfter - now : PACE.firstStep - now;
      case 'download':
        return this.fileIdx && e.index > this.fileIdx ? this.fileAt + this.fileMin - now : 0;
      case 'installing': {
        // each mod's icon stays up long enough to turn over (the one already up needs no wait)
        const i = S.chosen.findIndex((m) => m.guid === e.guid);
        return this.modAt === null || i === this.mod ? 0 : this.modAt + PACE.mod - now;
      }
      case 'verified':
        // its tick pops when the lens has finished looking over that zip
        if (this.cur !== 'chk' || e.index !== this.sweepIdx || this.sweepEnd === null) return Infinity;
        return this.sweepEnd - now;
      case 'checked':
        if (this.chkDoneAt === null) {
          if (this.ticks > 0) return Infinity;
          this.chkDoneAt = (this.cur === 'chk' ? this.curAt : now) + PACE.sweep;  // no zips were reported at all
        }
        return this.chkDoneAt - now;
      default:
        return 0;
    }
  },

  // when the step on screen may give way to the next one
  stepGate(now) {
    if (this.cur === 'chk') return this.chkDoneAt === null ? Infinity : this.chkDoneAt - now;
    if (this.cur === 'wait' || this.cur === 'steam') {
      // the game (or Steam) has closed: its window shrinks away, then the next step starts
      const key = this.cur;
      if (this.closingAt === null) {
        this.closingAt = Math.max(now, this.curAt + PACE[key] - PACE.closing);
        this.timers.after(this.closingAt - now, () => this.picture(key).classList.add('closing'));
      }
      return this.closingAt + PACE.closing - now;
    }
    if (this.cur === 'ins' && this.modAt !== null) {
      // on its own, the page shows every chosen mod once before it moves on
      if (!this.modEvents && this.modSeen.size < S.chosen.length) return this.modAt + PACE.mod - now + .01;
      return Math.max(this.curAt + PACE.ins, this.modAt + PACE.mod) - now;
    }
    return this.curAt + PACE[this.cur] - now;
  },

  apply(e, now) {
    switch (e.type) {
      case 'step': return this.enterStep(e.step, now);
      case 'download': return this.onDownload(e, now);
      case 'verified': return this.onVerified(e, now);
      case 'checked': return this.onChecked();
      case 'log': return this.log(e.text);
      case 'installing': return this.onInstalling(e, now);
      case 'done': return this.onDone(e, now);
      case 'failed': return Fail.show(e);
    }
  },

  // ----- steps -----

  enterStep(key, now) {
    if (!this.keys.includes(key)) return;
    this.marks(key);
    this.showPicture(key);
    swapText($('plabel'), t(PHASE_NAME[key]));
    const sub = {
      wait: t('subWait'),
      dl: this.fileIdx ? this.fileLine(this.fileIdx) : '',
      chk: this.chkLine(1),
      bak: t('subBak'),
      ins: S.chosen.length > 1 ? modLabel(S.chosen[0].name, S.chosen[0].to) : this.allNames(),
      steam: t('subSteam'),
      opt: t('subOpt'),
    }[key];
    swapText($('psub'), sub);
    this.cur = key;
    this.curAt = now;
    this.closingAt = null;
    if (key === 'ins') this.showMod(0, now);
    if (key === 'chk') {
      this.sweepIdx = 1;
      this.sweep();
    }
  },

  // steps before `key` done, `key` now, the rest to do
  marks(key) {
    const at = this.keys.indexOf(key);
    for (const li of $('steps').children) {
      const i = this.keys.indexOf(li.dataset.step);
      const state = i < at ? 'done' : i === at ? 'now' : 'todo';
      if (!li.classList.contains(state)) {
        li.classList.remove('todo', 'now', 'done');
        li.classList.add(state);
      }
    }
  },

  picture(key) { return $('pictures').querySelector('.sc.' + pictureOf(key)); },

  // the picture for the running step fades in over the last one (null: none)
  showPicture(key) {
    const name = key && pictureOf(key);
    for (const pic of $('pictures').children) {
      if (name && pic.classList.contains(name)) {
        pic.classList.remove('leaving');
        pic.classList.add('on');
      } else if (pic.classList.contains('on')) {
        pic.classList.remove('on');
        pic.classList.add('leaving');
        this.timers.after(.2, () => pic.classList.remove('leaving'));
      }
    }
  },

  fileLine(index) {
    const f = this.files[index - 1];
    if (!f) return '';
    const size = fmtSize(f.total);
    return modLabel(f.name, f.version) + (size ? ` · ${size}` : '');
  },

  chkLine(index) {
    const f = this.files[index - 1] || S.chosen[index - 1];
    if (!f) return '';
    const count = this.zips || S.chosen.length;
    return t('subChk', index, count, modLabel(f.name, f.version || f.to));
  },

  allNames() { return S.chosen.map((m) => modLabel(m.name, m.to)).join(' · '); },

  // ----- install: the icon of the mod going in, one mod after another -----

  // the launcher says which mod it is putting in; without that the page goes through the chosen mods by itself
  onInstalling(e, now) {
    this.modEvents = true;
    const i = S.chosen.findIndex((m) => m.guid === e.guid);
    this.showMod(i >= 0 ? i : Math.max(0, (e.index || 1) - 1), now);
  },

  showMod(i, now) {
    const mod = S.chosen[i];
    if (!mod || this.cur !== 'ins') return;
    const changed = i !== this.mod;
    this.mod = i;
    this.modAt = now;
    this.modSeen.add(i);
    if (changed) {
      Pics.setMod(this.picture('ins'), mod);
      if (S.chosen.length > 1) swapText($('psub'), modLabel(mod.name, mod.to));
    }
    if (!this.modEvents && S.chosen.length > 1) {
      this.timers.after(this.modSeen.size < S.chosen.length ? PACE.mod : PACE.modLoop, () => {
        if (this.modEvents || this.cur !== 'ins') return;
        this.showMod((this.mod + 1) % S.chosen.length, this.now());
        this.pump();
      });
    }
  },

  onDownload(e, now) {
    this.files[e.index - 1] = { name: e.name, version: e.version, total: e.total };
    this.zips = e.count;
    this.dlShare = e.allTotal > 0
      ? e.allDone / e.allTotal
      : (e.index - 1 + (e.total > 0 ? e.done / e.total : 0)) / Math.max(1, e.count);
    if (e.index !== this.fileIdx) {
      // a new file: its name stays up for its share of the download time
      this.fileIdx = e.index;
      this.fileAt = now;
      const share = e.allTotal > 0 && e.total > 0 ? e.total / e.allTotal : 1 / Math.max(1, e.count);
      this.fileMin = Math.max(PACE.fileMin, PACE.dl * share);
      if (this.cur === 'dl') swapText($('psub'), this.fileLine(e.index));
      const small = $('steps').querySelector('[data-step="dl"] small');
      if (small) small.textContent = t('smallDl', e.count, fmtSize(e.allTotal));
    }
  },

  // ----- check: one lens sweep per zip; its tick pops only on its 'verified' -----

  sweep() {
    const index = this.sweepIdx;
    replay(this.picture('chk').querySelector('.lens'), 'sweep');
    this.sweepEnd = this.now() + PACE.sweep;
    this.timers.after(PACE.sweep, () => {
      this.pump();
      // the check isn't back yet: look it over again
      if (this.cur === 'chk' && this.sweepIdx === index && this.ticks < index) this.sweep();
    });
  },

  onVerified(e, now) {
    const pic = this.picture('chk');
    const ok = pic.querySelector('.ok');
    this.zips = e.count;
    this.ticks = e.index;
    ok.classList.remove('gone');
    replay(ok, 'pop');
    this.timers.after(.45, () => {
      ok.classList.remove('pop');
      ok.classList.add('gone');
    });
    if (e.index < e.count) {
      this.sweepIdx = e.index + 1;
      this.sweepEnd = null;
      this.timers.after(.5, () => {
        replay(pic.querySelector('.doc'), 'swap');
        swapText($('psub'), this.chkLine(e.index + 1));
      });
      this.timers.after(PACE.tickGap, () => this.sweep());
    } else {
      this.chkDoneAt = now + PACE.chkTail;
      this.timers.after(PACE.chkTail, () => this.pump());
    }
  },

  // all zips passed and the pictures are done: the launcher may start writing to the game folder
  onChecked() {
    if (this.cancelling) return;
    this.proceeded = true;
    $('upd-cancel').disabled = true;
    send('proceed');
  },

  cancel() {
    if (this.proceeded || this.cancelling) return;
    this.cancelling = true;
    $('upd-cancel').disabled = true;
    send('cancel');
  },

  log(text) {
    const box = $('log-lines');
    box.append(el('div', null, String(text)));
  },

  // ----- the end -----

  // everything is in: the finale waits for a beat when the gear and the sponge are at rest
  onDone(e, now) {
    this.backup = e.backup || '';
    this.options = e.options || '';
    let at = now;
    if (!reduced()) at = PACE.land + PACE.beat * Math.ceil((now - PACE.land) / PACE.beat - 1e-6);
    this.T = at;
    this.insFrom = this.shown;
    this.insFromAt = now;
    this.timers.after(at - now, () => this.finish());
  },

  finish() {
    const node = $('upd');
    node.style.setProperty('--late', sec(-Math.max(0, this.now() - this.T)));
    this.marks('go');
    this.showPicture(null);
    this.cur = 'fin';
    swapText($('plabel'), t('phaseDone'));
    swapText($('psub'), S.option ? t('subDone', shortLauncher(this.options)) : this.allNames());
    if (S.restart && this.backup) $('upd-net').textContent = t('netRestart', this.backup);
    node.classList.add('fin');
    Countdown.start($('upd-again'), reduced() ? 0 : PACE.countdown);
  },

  // ----- the bar and the percent -----

  barTarget(now) {
    const since = now - this.curAt;
    if (S.option) return this.optionBar(now, since);
    switch (this.cur) {
      case 'dl': return BAR.dl * Math.min(this.dlShare, since / PACE.dl);
      case 'chk': return BAR.dl + (BAR.chk - BAR.dl) * this.ticks / Math.max(1, this.zips || S.chosen.length);
      case 'bak': return BAR.chk + (BAR.bak - BAR.chk) * Math.min(1, since / PACE.bak);
      case 'ins':
        // creeps on while installing; once the all-in moment is known it heads for 100 % right then
        if (this.T !== null) return this.insFrom + (100 - this.insFrom) * Math.min(1, (now - this.insFromAt) / Math.max(.1, this.T - this.insFromAt));
        return BAR.bak + (96 - BAR.bak) * Math.min(1, since / 2.1);
      case 'fin': return 100;
      default: return 0;
    }
  },

  // the launch option: waiting 0–8 %, closing Steam creeps towards 50 %, the change to 90 %, then 100 % at the
  // all-set moment
  optionBar(now, since) {
    switch (this.cur) {
      case 'wait': return 8 * Math.min(1, since / PACE.wait);
      case 'steam': return 8 + 42 * (1 - Math.exp(-since / 1.2));
      case 'opt':
        if (this.T !== null) return this.insFrom + (100 - this.insFrom) * Math.min(1, (now - this.insFromAt) / Math.max(.1, this.T - this.insFromAt));
        return 50 + 40 * Math.min(1, since / 1.4);
      case 'fin': return 100;
      default: return 0;
    }
  },

  frame() {
    const time = performance.now();
    const dt = (time - this.lastFrame) / 1000;
    this.lastFrame = time;
    const target = this.barTarget(this.now());
    this.shown = reduced() || Math.abs(target - this.shown) < .05
      ? target
      : this.shown + (target - this.shown) * (1 - Math.exp(-dt / .15));
    $('pfill').style.width = this.shown + '%';
    $('pfill').classList.toggle('full', this.shown >= 99.95);
    const pct = Math.round(this.shown);
    if (pct !== this.pct) {
      this.pct = pct;
      $('pct').textContent = pct + '%';
      $('pbar').setAttribute('aria-valuenow', pct);
    }
    this.raf = requestAnimationFrame(() => this.frame());
  },
};

$('upd-cancel').addEventListener('click', () => Updating.cancel());

// the launcher stopped after Cancel, nothing changed: back to the list as it was
function onCancelled() {
  if (S.board !== 'upd') return;
  S.board = 'moving';
  Updating.stop();
  const node = $('upd');
  // the working logo flies back up into the header while the board leaves to the right
  if (reduced()) $('hlogo').classList.remove('away');
  else Motion.flyBack($('fly'), $('hlogo'), $('mlogo'));
  $('upd-cancel').classList.add('pressed');
  const parts = Motion.partsOf(node);
  parts.r = parts.r.filter((n) => n !== $('mlogo'));
  Motion.change(node, 'back', () => {
    Head.state('stateList');
    List.enter('back');
  }, { parts });
}

// ---------- 3: failed ----------

const FAIL_KINDS = ['offline', 'busy', 'limited', 'notfound', 'mismatch', 'install', 'running', 'otherloader', 'other', 'steamstuck', 'writefail'];

// the mark beside the heading, for each kind of failure: GitHub's mark when it couldn't be reached, a picture of
// what went wrong for the others, and "!" for anything else
const FAIL_MARK = {
  offline: 'github', busy: 'busy', limited: 'limited', notfound: 'notfound', mismatch: 'mismatch',
  running: 'running', otherloader: 'otherloader', writefail: 'lock', steamstuck: 'steamstuck', other: 'bang',
};
const markOf = (kind, changed) => (kind === 'install' ? (changed === 'partly' ? 'partly' : 'rolled') : FAIL_MARK[kind] || 'bang');

const Fail = {
  details: '',

  // the board on screen leaves (calmly, downwards) and the failed board comes in once it has gone; the marks
  // keep their own bounce
  show(e) {
    Updating.stop();
    const was = [$('upd'), $('list')].find((n) => !n.hidden);
    S.board = 'moving';
    $('fly').classList.remove('go');
    Head.reveal(false);
    Head.logoBack();
    Head.state('stateFailed');
    const enter = () => {
      this.fill(e);
      const node = $('fail');
      Motion.reset(node);
      node.hidden = false;
      replay(node, 'play');
      Motion.enterBoard(node, 'fail');
      S.board = 'fail';
      if (e.restart) Countdown.start($('fail-again'), reduced() ? 0 : 1);
    };
    if (was) Motion.change(was, 'fail', enter);
    else enter();
  },

  fill(e) {
    const restart = !!e.restart;
    const kind = FAIL_KINDS.includes(e.kind) ? e.kind : 'other';
    const words = STRINGS[lang];
    const mod = e.mod ? `${e.mod.name || ''} ${e.mod.version || ''}`.trim() : '';

    const mark = Pics.mark(markOf(kind, e.changed));
    mark.id = 'fail-mark';
    $('fail-mark').replaceWith(mark);
    $('fail-title').textContent = words.failTitle[kind](mod);
    const advice = words.failAdvice[kind](e.minutes || 0);
    $('fail-lead').textContent = advice + (lang === 'ja' ? '' : ' ') + t(restart ? 'failRestart' : 'failPlayLater');

    // what happened to the game folder, always said
    const pill = $('fail-folder');
    pill.classList.toggle('warn', e.changed === 'partly');
    $('fail-folder-text').textContent = e.changed === 'partly' ? t('folderPartly')
      : e.changed === 'restored' ? t('folderRestored', e.files || 0)
        : t('folderSame');
    // under it: the updates that were in before this one failed, or the launch option's backup that stays
    const updated = Array.isArray(e.updated) ? e.updated : [];
    const under = updated.length ? t('alreadyUpdated', updated) : e.backupKept ? t('backupKept', S.init.backupPath || '') : '';
    $('fail-updated').hidden = !under;
    $('fail-updated').textContent = under;

    const lines = (Array.isArray(e.detail) ? e.detail : []).map(String);
    const logPath = e.logPath || S.init.logPath;
    if (logPath) lines.push(`Log: ${logPath}`);
    this.details = lines.join('\n');
    const box = $('fail-details');
    box.textContent = '';
    for (const line of lines) box.append(el('div', null, line));

    // after an in-game update the game starts again as it was, with the same countdown as B
    $('fail-retry').hidden = restart;
    $('fail-play').hidden = restart;
    $('fail-again').hidden = !restart;
  },

  // Try again: the command goes at once, then the failed board leaves to the left and the updating board comes in
  retry() {
    if (S.board !== 'fail') return;
    S.board = 'upd';
    send('retry');
    $('fail-retry').classList.add('pressed');
    const at = Motion.change($('fail'), 'fwd', () => Head.state(S.restart ? 'stateRestart' : 'stateUpdating'));
    Updating.begin(at, true, 'fwd');
  },
};

$('fail-retry').addEventListener('click', () => Fail.retry());
$('fail-play').addEventListener('click', () => sendOnce('play'));
$('fail-openlog').addEventListener('click', () => send('openLog'));
// Details: the box closes up and opens again instead of jumping
$('fail').querySelector('details.det > summary').addEventListener('click', (e) => {
  e.preventDefault();
  Motion.details(e.currentTarget.parentElement);
});
$('fail-copy').addEventListener('click', (e) => {
  send('copy', { text: Fail.details });
  const link = e.currentTarget;
  link.textContent = t('copied');
  setTimeout(() => { link.textContent = t('copyDetails'); }, 1500);
});

// ---------- window, keys ----------

$('btn-min').addEventListener('click', () => send('minimize'));
$('btn-close').addEventListener('click', () => send('close'));

// when the window comes up, WebView2 puts the focus on the page's first button (minimise): it would show its
// ring and take Enter from the board's default button, so it only stays once a key or the mouse has been used
let touched = false;
for (const type of ['keydown', 'pointerdown']) document.addEventListener(type, () => { touched = true; }, true);
$('btn-min').addEventListener('focus', (e) => { if (!touched) e.currentTarget.blur(); });

// the header moves the window (outside its buttons)
$('head').addEventListener('mousedown', (e) => {
  if (e.button === 0 && !e.target.closest('button')) send('drag');
});

// any key skips the intro; Enter presses the board's default button (unless a button has the focus)
document.addEventListener('keydown', (e) => {
  if (Intro.live) {
    Intro.skip();
    return;
  }
  if (e.key !== 'Enter' || e.repeat) return;
  if (e.target instanceof Element && e.target.closest('button, summary, a, textarea')) return;
  if (S.board === 'list') List.update();
  else if (S.board === 'fail' && !$('fail-play').hidden) sendOnce('play');
});

// ---------- closing ----------

// the launcher is closing the window: everything stops, the board's parts leave (the countdown card first), the
// header and the page fade, then the launcher fades the window away and closes it. With motion reduced the
// window just closes
function bye() {
  if (S.board === 'bye') return;
  S.board = 'bye';
  Intro.timers.clear();
  Intro.live = false;
  Updating.stop();
  Motion.timers.clear();
  const win = $('win');
  win.inert = true;
  if (reduced()) {
    send('gone', { on: false });
    return;
  }
  const board = [$('upd'), $('fail'), $('list')].find((n) => !n.hidden);
  if (board) {
    // the countdown card leads, once it is up (while it is still coming in, it just goes with the rest)
    const card = [...board.querySelectorAll('.again')]
      .find((n) => !n.hidden && getComputedStyle(n).visibility === 'visible' && getComputedStyle(n).opacity === '1');
    if (card) for (const b of card.querySelectorAll('button')) b.disabled = true;
    Motion.exitBoard(board, 'bye', Motion.partsOf(board), card ? [card] : []);
  }
  win.classList.add('x-bye');
  setTimeout(() => send('gone', { on: true }), 300);
}

// ---------- events from the launcher ----------

function init(e) {
  if (S.init) return;
  S.init = e;
  lang = e.lang === 'ja' ? 'ja' : 'en';
  root.lang = lang;
  if (e.mode === 'option') {
    // a window shows the switch and nothing else, so its words simply take the update's place
    S.option = e.opt === 'off' ? 'off' : 'on';
    const words = STRINGS[lang];
    Object.assign(words, words.launchOption, S.option === 'off' ? words.launchOptionOff : words.launchOptionOn);
  }
  root.classList.toggle('mf-write', e.lettering !== 'type');
  root.classList.toggle('pb-edge', e.bar !== 'text');
  for (const node of document.querySelectorAll('[data-t]')) node.textContent = t(node.dataset.t);
  for (const node of document.querySelectorAll('[data-t-label]')) {
    node.setAttribute('aria-label', t(node.dataset.tLabel));
    if (node.tagName === 'BUTTON') node.title = t(node.dataset.tLabel);
  }
  S.mods = Array.isArray(e.mods) ? e.mods : [];
  S.restart = !!e.restart;
  S.wait = !!e.wait;

  if (e.mode === 'update' || e.mode === 'option') {
    // straight to updating (B): the mods chosen in the game
    const chosen = S.mods.filter((m) => m.selected);
    S.chosen = chosen.length ? chosen : S.mods;
    S.board = 'upd';
    Head.reveal(false);
    Head.state(S.restart ? 'stateRestart' : 'stateUpdating');
    Updating.begin(0, false);
  } else if (e.mode === 'list') {
    List.build();
    Intro.start('found');
  } else {
    Intro.start('none');
  }
}

window.dnw = (e) => {
  if (!e || typeof e !== 'object') return;
  switch (e.type) {
    case 'init': return init(e);
    case 'cancelled': return onCancelled();
    case 'bye': return bye();
    case 'step': case 'download': case 'verified': case 'checked': case 'installing': case 'log': case 'done': case 'failed':
      return Updating.push(e);
  }
};

send('ready');
