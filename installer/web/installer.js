'use strict';
/* Drag'n Wash Mod Installer page: the boards of Install.exe's window, driven by events from Install.exe (C#,
   InstallerSession). The shared parts (kit.js, intro.js, pics.js, motion.js, modicon.js) come first.

   Bridge: the page posts {cmd, ...} as JSON with chrome.webview.postMessage; Install.exe calls window.dnw(event)
   with a plain object. Install.exe sends nothing before the page has posted 'ready'. Every word on the page comes
   from Install.exe (Strings.cs, in the installer's language): the texts table in init, and ready-made lines in
   the events. The page keeps no state of its own that Install.exe doesn't know: a click is sent, and the board
   changes when the answer comes back.

   Boards: intro → setup (with the Steam and download questions as sheets over it) → working → done, or failed
   (Try again goes back to working, Back to setup). A failure before anything could start (no mod files next to
   Install.exe) opens straight on the failed board. */

// ---------- bridge ----------

function send(cmd, extra) {
  const text = JSON.stringify({ cmd, ...extra });
  if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(text);
  else console.info('installer ->', text);  // in a plain browser (testing) just show what would be sent
}

const S = {
  init: null,
  texts: {},
  board: '',      // intro | setup | work | done | fail | bye
  sheet: '',      // steam | consent | ''
  setup: null,    // the last setup state from Install.exe
  mod: null,
  log: [],        // this run's log lines
  busy: false,    // a run is going (the language can't change)
};

const t = (key) => (S.texts && S.texts[key] != null ? S.texts[key] : key);

function button(cls, text, onClick) {
  const b = el('button', cls, text);
  b.type = 'button';
  if (onClick) b.addEventListener('click', onClick);
  return b;
}

// ---------- header ----------

const Head = {
  reveal(animate) {
    const head = $('head');
    if (!head.classList.contains('veiled')) return;
    head.classList.remove('veiled');
    if (animate && !reduced()) replay(head, 'reveal');
    head.classList.add('shimmer');
  },
  // the header's state: a text from Install.exe, or a key of the texts (said again when the language changes)
  state(text) {
    this.key = '';
    swapText($('state'), text || '');
  },
  stateKey(key) {
    this.key = key;
    swapText($('state'), t(key));
  },
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
  // the language select: off while something runs or a question is open
  langs() {
    const sel = $('hsel');
    sel.textContent = '';
    for (const l of S.init.langs || []) {
      const o = el('option', null, l.name);
      o.value = l.code;
      o.selected = l.code === S.init.lang;
      sel.append(o);
    }
    sel.setAttribute('aria-label', t('Language'));
    sel.title = t('Language');
    this.lock();
  },
  lock() { $('hsel').disabled = S.busy || !!S.sheet || S.board === 'intro' || S.board === 'bye'; },
  words() {
    $('btn-min').title = t('WebMinimize');
    $('btn-min').setAttribute('aria-label', t('WebMinimize'));
    $('btn-close').title = t('Close');
    $('btn-close').setAttribute('aria-label', t('Close'));
    $('hsel').setAttribute('aria-label', t('Language'));
    $('hsel').title = t('Language');
  },
};

$('hsel').addEventListener('change', (e) => send('lang', { value: e.target.value }));

// ---------- boards coming and going ----------

// One board at a time, each with data-b naming it for Motion (setup, upd, done, fail). The old one leaves by one
// of Motion's kinds (fwd, back, up, fail; none: at once) and is removed once gone; it gives up its ids at once, so
// the new board's can be looked up straight away. enter(moving): the new board's own entrance (else Motion's, by
// kind). opts go to Motion.change (parts: which of the old board's parts leave).
const Board = {
  node: null,
  timers: new Timers(),

  change(kind, node, enter, opts = {}) {
    const old = this.node;
    this.node = node;
    this.timers.clear();
    node.hidden = true;
    $('win').append(node);
    const moving = !!old && old !== node && old.isConnected && !old.hidden && kind !== 'none';
    const start = () => {
      if (enter) enter(moving);
      else if (moving) Motion.enterBoard(node, kind);
      else Motion.show(node);
    };
    if (old && old !== node) {
      old.inert = true;
      for (const n of old.querySelectorAll('[id]')) n.removeAttribute('id');
      old.removeAttribute('id');
    }
    if (!moving) {
      if (old && old !== node) old.remove();
      start();
      return 0;
    }
    const at = Motion.change(old, kind, start, { ...opts, timers: this.timers });
    setTimeout(() => old.remove(), (at + 1) * 1000);
    return at;
  },
};

// the board's default button, focused for the keyboard once it's there (a board still waiting for the old one
// to leave is hidden, so it waits for that too)
function focusLater(node, ms = 450, tries = 30) {
  setTimeout(() => {
    if (!node || !node.isConnected || node.disabled || document.activeElement?.closest('.sheet')) return;
    if (node.closest('[hidden], [inert]')) {
      if (tries > 0) focusLater(node, 50, tries - 1);
      return;
    }
    node.focus({ preventScroll: true });
  }, reduced() ? Math.min(ms, 50) : ms);
}

// ---------- the step pictures ----------

// a step picture by name; the mod pictures (modin, modup, modout) are drawn around the mod's icon or letter tile
function picture(name) {
  const pic = name ? Pics.make(name, S.mod) : null;
  if (pic) pic.dataset.pic = name;
  return pic;
}

function showPicture(box, name) {
  if (!box) return;
  for (const pic of box.children) {
    if (name && pic.dataset.pic === name) {
      pic.classList.remove('leaving');
      pic.classList.add('on');
      const lens = pic.querySelector('.lens');
      if (lens) replay(lens, 'sweep');
    } else if (pic.classList.contains('on')) {
      pic.classList.remove('on');
      pic.classList.add('leaving');
      setTimeout(() => pic.classList.remove('leaving'), 200);
    }
  }
}

// ---------- 0: logo intro ----------

const Intro = {
  timers: new Timers(),
  live: false,

  start(found) {
    S.board = 'intro';
    Head.lock();
    const host = $('intro');
    host.hidden = false;
    const { ist } = LogoIntro.build(host);
    LogoIntro.moments(host, true);
    const I = LogoIntro.T;
    const last = t(found ? 'WebFoundIt' : 'WebNotFoundIt');
    ist.append(el('span', 'sr', last));
    if (reduced()) {
      ist.append(LogoIntro.typed(last, 0, 0, found ? 'acc' : ''));
    } else {
      ist.append(LogoIntro.leaves(LogoIntro.withDots(LogoIntro.typed(t('WebLooking'), I.chk, I.chkLen), I.dots), I.res - .16));
      ist.append(LogoIntro.typed(last, I.res, I.resLen, found ? 'acc' : ''));
    }
    // the "look for the folder" picture beside the line
    const find = picture('find');
    if (find) {
      const box = el('div', 'scbox ifind');
      box.append(find);
      host.append(box);
      this.timers.after(reduced() ? 0 : I.chk, () => find.classList.add('on'));
      if (found) this.timers.after(reduced() ? 0 : I.res, () => find.classList.add('found'));
    }
    replay(host, 'play');
    if (reduced()) {
      // the logo and the line just show, for a moment to read them
      this.timers.after(1, () => this.handoff());
      return;
    }
    this.live = true;
    this.timers.after(I.ho, () => this.handoff());
  },

  // a click or a key: straight to the hand-off
  skip() {
    if (!this.live) return;
    $('intro').classList.add('skipped');
    this.handoff();
  },

  // the logo moves up into the header while the header and the setup board fade in under it
  handoff() {
    this.live = false;
    this.timers.clear();
    const node = $('intro');
    const logo = $('hlogo');
    Head.stateKey('WebStateSetup');
    if (reduced()) {
      node.hidden = true;
      Head.reveal(false);
      Setup.show(true);
      return;
    }
    LogoIntro.aim($('big'), logo);
    logo.classList.add('landing');
    Head.reveal(true);
    Setup.show(true);
    node.classList.add('handoff');
    this.timers.after(.6, () => logo.classList.remove('landing'));
    this.timers.after(.9, () => { node.hidden = true; node.textContent = ''; });
  },
};

window.addEventListener('pointerdown', () => Intro.skip(), true);

// ---------- 1: setup ----------

const Setup = {
  node: null,
  planText: '',

  build() {
    const n = el('section', 'layer setup');
    n.id = 'setup';
    n.dataset.b = 'setup';
    n.innerHTML = `<div class="lbody">
  <div class="col-l">
    <div class="seg" role="radiogroup"><button type="button" role="radio" id="su-install"></button><button type="button" role="radio" id="su-uninstall"></button></div>
    <p class="segnote" id="su-note"></p>
    <h1 class="lh" id="su-head"></h1>
    <p class="ls" id="su-lead"></p>
    <div class="fcard" id="su-folder">
      <div class="flab"><span id="su-flab"></span><span class="ok" id="su-found"><span class="mk"></span><span id="su-found-text"></span></span></div>
      <div class="frow"><span class="fpath" id="su-path"></span><button type="button" class="lb gho sm" id="su-browse"></button></div>
      <div class="chips" id="su-chips"></div>
    </div>
    <div id="su-inst"><div id="su-choices"></div>
      <label class="opt" id="su-lopt"><input type="checkbox" class="ck" id="su-launch"><span><span id="su-ltext"></span><small id="su-lsmall"></small></span></label>
    </div>
    <div id="su-uninst">
      <label class="opt"><input type="checkbox" class="ck" id="su-keep"><span id="su-keep-text"></span></label>
      <label class="opt"><input type="checkbox" class="ck" id="su-bep"><span id="su-bep-text"></span></label>
    </div>
  </div>
  <div class="col-r">
    <div class="banner" id="su-banner" hidden><span class="mk"></span><span id="su-banner-text"></span></div>
    <div class="warnbox" id="su-warn" hidden><span class="mk" style="display:flex;align-items:center;justify-content:center">!</span><div><b id="su-warn-title"></b><span id="su-warn-text"></span></div></div>
    <h2 class="nt" id="su-will"></h2>
    <ol class="plan" id="su-plan"></ol>
    <p class="hint" id="su-hint"></p>
  </div>
</div>
<div class="lfoot">
  <p class="net"><span id="su-net"></span><span id="su-web"> &nbsp;&middot;&nbsp; <button type="button" class="llink" id="su-website"></button></span></p>
  <button type="button" class="lb gho" id="su-close"></button>
  <button type="button" class="lb pri" id="su-run"></button>
</div>`;
    const q = (id) => n.querySelector('#' + id);
    q('su-install').addEventListener('click', () => this.mode('install'));
    q('su-uninstall').addEventListener('click', () => this.mode('uninstall'));
    q('su-browse').addEventListener('click', () => send('browse'));
    q('su-launch').addEventListener('change', (e) => send('launch', { on: e.target.checked }));
    q('su-keep').addEventListener('change', (e) => send('keepData', { on: e.target.checked }));
    q('su-bep').addEventListener('change', (e) => send('alsoBep', { on: e.target.checked }));
    q('su-website').addEventListener('click', () => send('open', { value: 'website' }));
    q('su-close').addEventListener('click', () => send('close'));
    q('su-run').addEventListener('click', () => this.run());
    this.node = n;
    this.planText = '';
    return n;
  },

  mode(value) {
    if (!S.setup || S.setup.mode === value) return;
    send('mode', { value });
  },

  run() {
    const b = $('su-run');
    if (!b || b.disabled || S.board !== 'setup' || S.sheet) return;
    send('run');
    b.classList.add('pressed');
  },

  // the board comes in: after the intro (reveal), or from another board (kind; opts for Motion.change)
  show(reveal, kind, opts) {
    S.board = 'setup';
    Head.lock();
    const n = this.build();
    this.fill(S.setup, true);
    if (reveal) {
      $('win').append(n);
      Board.node = n;
      replay(n, 'play');
      if (reduced()) n.classList.remove('play');
    } else {
      Board.change(kind || 'none', n, null, opts);
    }
    focusLater($('su-run'), reveal ? 900 : 450);
  },

  // puts the state into the board as it is; first: while it's being built (the plan's lines come in with it)
  fill(s, first) {
    if (!s || !this.node) return;
    S.setup = s;
    const n = this.node;
    const q = (id) => n.querySelector('#' + id);
    const install = s.mode !== 'uninstall';

    const mi = q('su-install');
    mi.textContent = t(s.installed ? 'Update' : 'Install');
    mi.classList.toggle('on', install);
    mi.setAttribute('aria-checked', install);
    const mu = q('su-uninstall');
    mu.textContent = t('Uninstall');
    mu.classList.toggle('on', !install);
    mu.setAttribute('aria-checked', !install);
    mu.disabled = !s.canUninstall;
    q('su-note').textContent = t('NothingToUninstall');
    q('su-note').hidden = s.canUninstall || !s.found;
    q('su-head').textContent = s.head;
    q('su-lead').textContent = s.lead;

    // the game folder
    q('su-folder').classList.toggle('bad', !s.found);
    q('su-flab').textContent = t('GameFolder');
    q('su-found').hidden = !s.found;
    q('su-found-text').textContent = t('WebFound');
    const path = q('su-path');
    path.classList.toggle('empty', !s.found);
    path.textContent = '';
    if (s.found) {
      const bdi = el('bdi', null, s.game);
      path.append(bdi);
      path.title = s.game;
    } else {
      path.textContent = t('NotFound');
      path.removeAttribute('title');
    }
    q('su-browse').textContent = t('Browse');
    const chips = q('su-chips');
    chips.textContent = '';
    chips.hidden = !s.found;
    const chip = (name, value, yes) => {
      const c = el('span', 'chip' + (yes ? ' yes' : ''), name);
      c.append(el('b', null, value));
      chips.append(c);
    };
    chip(t('StatusBepInEx'), t(s.bep ? 'Yes' : 'No'), s.bep);
    chip(t('StatusFramework'), s.fw || t('No'), !!s.fw);
    chip(t('StatusMod'), s.installed || t('No'), !!s.installed);

    // install: the mod's choices and the launch option; uninstall: what to keep
    q('su-inst').hidden = !install;
    q('su-uninst').hidden = install;
    const box = q('su-choices');
    for (const c of s.choices || []) {
      let field = box.querySelector(`[data-id="${CSS.escape(c.id)}"]`);
      if (!field) {
        field = el('label', 'field');
        field.dataset.id = c.id;
        field.append(el('span'));
        const sel = el('select', 'dsel');
        sel.addEventListener('change', () => send('choice', { id: c.id, value: sel.value }));
        field.append(sel);
        box.append(field);
      }
      field.firstChild.textContent = c.label;
      const sel = field.lastChild;
      if (sel.options.length !== c.options.length || [...sel.options].some((o, i) => o.value !== c.options[i].value || o.textContent !== c.options[i].name)) {
        sel.textContent = '';
        for (const o of c.options) {
          const opt = el('option', null, o.name);
          opt.value = o.value;
          sel.append(opt);
        }
      }
      sel.value = c.value || '';
      if (!c.value) sel.selectedIndex = -1;
      sel.disabled = !s.found;
    }
    const launch = s.launch || {};
    const usable = !!launch.usable;
    q('su-lopt').classList.toggle('off', !usable);
    const ck = q('su-launch');
    ck.checked = usable ? !!launch.on : false;
    ck.disabled = !usable;
    q('su-ltext').textContent = t('WebLaunchCheck');
    const small = q('su-lsmall');
    small.textContent = launch.why || t('WebLaunchSmall');
    small.classList.toggle('why', !!launch.why);
    q('su-keep').checked = !!s.keepData;
    q('su-keep-text').textContent = t('KeepData');
    q('su-bep').checked = !!s.alsoBep;
    q('su-bep-text').textContent = t('AlsoBepInEx');

    // the plan, and what's above and below it
    const banner = q('su-banner');
    banner.hidden = !s.banner;
    q('su-banner-text').textContent = s.banner || '';
    q('su-warn').hidden = !s.other;
    q('su-warn-title').textContent = s.other ? s.other.title : '';
    q('su-warn-text').textContent = s.other ? s.other.text : '';
    q('su-will').textContent = s.will;
    const plan = q('su-plan');
    const text = s.found ? (s.plan || []).map((l) => l.text + (l.net ? '*' : '')).join('\n') : '-';
    if (text !== this.planText) {
      plan.textContent = '';
      plan.classList.toggle('wait', !s.found);
      if (!s.found) {
        plan.textContent = t('WebLeadNotFound');
      } else {
        (s.plan || []).forEach((l, i) => {
          const li = el('li', l.cls || '');
          li.style.setProperty('--i', i);
          const span = el('span', null, l.text);
          if (l.net) span.append(el('span', 'tag', t('WebOnline')));
          li.append(span);
          plan.append(li);
        });
      }
      if (!first && this.planText && !reduced()) replay(plan, 'swap');
      this.planText = text;
    }
    q('su-hint').textContent = s.hint || '';
    q('su-hint').hidden = !s.found;

    const net = q('su-net');
    net.textContent = s.net || '';
    net.classList.toggle('blocked', !!s.blocked);
    q('su-web').hidden = !S.init.mod || !S.init.mod.website;
    q('su-website').textContent = t('Website');
    q('su-close').textContent = t('Close');
    const run = q('su-run');
    run.textContent = s.button;
    run.disabled = !s.found || !!s.blocked;
  },
};

// ---------- sheets: the Steam and download questions, over the board ----------

const STEAM_ICON = '<div class="sicon" aria-hidden="true"><svg viewBox="0 0 42 42"><rect x="5" y="8" width="30" height="24" rx="3" stroke="#e8eff7" stroke-width="2" fill="rgba(255,255,255,.04)"/><path d="M5 14H35" stroke="#e8eff7" stroke-width="2"/><circle cx="33" cy="31" r="7.5" fill="#52c7b8"/><path d="M29.8 31H36.2" stroke="#0d2522" stroke-width="2.4"/></svg></div>';

const Sheet = {
  veil: null,
  node: null,
  keys: null,   // {enter, esc}: what the keys press on this sheet

  // a new sheet: over the board, or in place of the one that's up (the veil stays, so nothing flickers)
  put(node, keys) {
    this.keys = keys;
    if (this.veil && !this.veil.classList.contains('x-out')) {
      if (this.node) for (const n of this.node.querySelectorAll('[id]')) n.removeAttribute('id');
      Motion.sheetSwap(this.veil, node);
    } else {
      const veil = el('div', 'veil');
      veil.append(node);
      Motion.sheetOpen(Board.node, veil);
      this.veil = veil;
    }
    this.node = node;
    Head.lock();
    focusLater(node.querySelector('.lb.pri:not(:disabled)'), 420);
  },

  // the sheet goes; the board behind is back at once, without a second entrance
  close() {
    S.sheet = '';
    const veil = this.veil;
    this.veil = null;
    this.node = null;
    this.keys = null;
    Head.lock();
    if (!veil) return;
    for (const n of veil.querySelectorAll('[id]')) n.removeAttribute('id');
    Motion.sheetClose(veil, Board.node);
    // back on the setup board (the question was turned down): the keyboard is back on its button
    if (S.board === 'setup') focusLater($('su-run'), 250);
  },

  steam(e) {
    S.sheet = 'steam';
    this.slow = e.slow || '';
    const n = el('div', 'sheet');
    n.setAttribute('role', 'dialog');
    n.setAttribute('aria-modal', 'true');
    n.innerHTML = `<button type="button" class="sx" id="sh-x">&#10005;</button>
<div class="sbody">${STEAM_ICON}<div class="stext"><h2 id="sh-head"></h2><p id="sh-body"></p><p class="dim" id="sh-keep"></p>
<div class="waitrow" id="sh-wait" hidden><div class="scbox" id="sh-pic"></div><span id="sh-wait-text"></span></div></div></div>
<div class="sfoot"><button type="button" class="lb gho" id="sh-skip"></button><span class="sp"></span><button type="button" class="lb gho" id="sh-self"></button><button type="button" class="lb pri" id="sh-close"></button></div>`;
    const q = (id) => n.querySelector('#' + id);
    q('sh-x').title = t('WebSheetStop');
    q('sh-x').setAttribute('aria-label', t('WebSheetStop'));
    q('sh-head').textContent = t('SteamHead');
    n.setAttribute('aria-label', t('SteamHead'));
    q('sh-body').textContent = t(e.remove ? 'SteamBodyRemove' : 'SteamBody');
    q('sh-keep').textContent = t('SteamKeep');
    q('sh-skip').textContent = t(e.remove ? 'SteamSkipRemove' : 'SteamSkip');
    q('sh-self').textContent = t('SteamSelf');
    q('sh-close').textContent = t('SteamClose');
    // the app-closing picture while it waits; the logo's turning gear when there's none
    const pic = picture('steam');
    if (pic) {
      q('sh-pic').append(pic);
    } else {
      q('sh-pic').replaceWith(el('span', 'gearwait'));
    }
    const answer = (value) => () => send('steam', { value });
    q('sh-x').addEventListener('click', answer('stop'));
    q('sh-skip').addEventListener('click', answer('skip'));
    q('sh-self').addEventListener('click', answer('self'));
    q('sh-close').addEventListener('click', answer('close'));
    this.put(n, { enter: () => n.querySelector('.lb.pri'), esc: () => n.querySelector('.sx') });
  },

  // Steam: closing, waiting for the player, or still there after the wait
  steamWait(phase) {
    const n = this.node;
    if (S.sheet !== 'steam' || !n) return;
    const row = n.querySelector('.waitrow');
    row.hidden = false;
    row.classList.toggle('slow', phase === 'slow');
    swapTextPlain(n.querySelector('#sh-wait-text'), phase === 'slow' ? this.slow : t(phase === 'closing' ? 'SteamClosing' : 'SteamWaiting'));
    const pic = row.querySelector('.sc');
    if (pic) pic.classList.toggle('on', phase !== 'slow');
    n.querySelector('#sh-self').disabled = phase !== 'slow';
    n.querySelector('#sh-close').disabled = phase === 'closing';
  },

  consent(e) {
    S.sheet = 'consent';
    const c = e.consent || {};
    const n = el('div', 'sheet wide');
    n.setAttribute('role', 'dialog');
    n.setAttribute('aria-modal', 'true');
    n.setAttribute('aria-label', t('ConsentHead'));
    const body = el('div', 'sbody');
    body.append(Pics.mark('github'));
    const text = el('div', 'stext');
    text.append(el('h2', null, t('ConsentHead')), el('p', null, c.need || ''));
    const facts = el('dl', 'facts');
    for (const [name, value] of c.facts || []) facts.append(el('dt', null, name), el('dd', null, value));
    text.append(facts);
    const note = el('p', 'dim', (c.note || '') + ' ');
    note.append(button('llink', t('OpenReleasePage'), () => send('open', { value: 'release' })));
    text.append(note);
    body.append(text);
    const foot = el('div', 'sfoot');
    foot.append(
      button('lb gho', t('ChooseZip'), () => send('consent', { value: 'zip' })),
      el('span', 'sp'),
      button('lb gho', t('CancelPlain'), () => send('consent', { value: 'cancel' })),
      button('lb pri', t('DownloadAndInstall'), () => send('consent', { value: 'download' })));
    n.append(body, foot);
    this.put(n, { enter: () => foot.querySelector('.lb.pri'), esc: () => foot.querySelectorAll('.lb.gho')[1] });
  },
};

// a plain text swap (no cross-fade) for a line that isn't in a grid cell
function swapTextPlain(node, text) {
  if (node) node.textContent = text;
}

// ---------- 2: working ----------

const Work = {
  timers: new Timers(),
  queue: [],
  pumpTimer: 0,
  nowAt: 0,       // when the running line last changed
  now: -2,        // which line runs
  startAt: 0,     // when the board has come in (the logo has landed): the run's lines wait for it

  // the minimum time a running line stays up, so a fast run can still be followed
  get dwell() { return reduced() ? .15 : .6; },

  start(e) {
    this.stop();
    this.stopped = false;
    S.board = 'work';
    S.busy = true;
    S.log = [];
    Head.lock();
    const kind = e.kind || 'install';
    const n = el('section', 'layer upd inst');
    n.id = 'work';
    n.dataset.b = 'upd';
    n.dataset.kind = kind;
    n.innerHTML = `<div class="lbody">
  <div class="col-l"><h1 class="lh" id="w-title"></h1><p class="ls" id="w-lead"></p><ol class="steps" id="steps"></ol></div>
  <div class="col-r">
    <div class="mstage wsc" id="w-stage" aria-hidden="true"></div>
    <div class="pcard">
      <div class="prow"><div class="pl" id="plabel"></div><div class="pp"><span class="pn" id="pct">0%</span></div></div>
      <div class="pbar" id="pbar" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0" aria-labelledby="plabel"><div class="f" id="pfill"></div></div>
      <div class="psub" id="psub"></div>
    </div>
    <div class="tail" id="w-tail"><div class="logbox" id="log" tabindex="0"><div class="lns" id="log-lines"></div></div></div>
  </div>
</div>
<div class="lfoot"><p class="net" id="w-net"></p><button type="button" class="lb gho cx" id="w-cancel"></button></div>`;
    const q = (id) => n.querySelector('#' + id);
    q('w-title').textContent = e.title || '';
    q('w-lead').textContent = e.lead || '';
    q('log').setAttribute('aria-label', t('WebLog'));
    S.mod = S.init.mod ? { name: S.init.mod.name, icon: e.icon || '' } : null;
    const steps = q('steps');
    (e.steps || []).forEach((s, i) => {
      const li = el('li', 'st todo');
      li.style.setProperty('--t', sec(.17 + .06 * i));  // it always comes in forwards (from setup or Try again)
      const text = el('div');
      text.append(el('span', null, s.text));
      if (s.small) text.append(el('small', null, s.small));
      li.append(el('span', 'mk'), text);
      steps.append(li);
    });
    // the working logo with the pictures of this run's lines (and the one for putting things back)
    const { box } = Pics.stage(q('w-stage'), []);
    box.id = 'pictures';
    const names = [...new Set((e.steps || []).map((s) => s.pic).filter(Boolean).concat(['rb']))];
    for (const name of names) {
      const pic = picture(name);
      if (pic) box.append(pic);
    }
    const cancel = q('w-cancel');
    cancel.textContent = e.cancel || t('CancelPlain');
    cancel.hidden = kind === 'uninstall';
    cancel.disabled = true;
    cancel.addEventListener('click', () => {
      if (cancel.disabled) return;
      cancel.disabled = true;
      send('cancel');
      cancel.classList.add('pressed');
    });

    Head.state(e.state);
    const fromSheet = !!S.sheet;
    if (fromSheet) Sheet.close();
    this.now = -2;
    this.nowAt = 0;
    this.startAt = Infinity;
    // its own build-up (the logo flight, the card, the bar) stays; the words come in from the right
    Board.change(fromSheet || Board.node ? 'fwd' : 'none', n, (moving) => {
      Motion.show(n);
      if (moving) {
        n.classList.add('enter');
        Motion.setKind(n, 'fwd');
      }
      replay(n, 'play');
      // with motion reduced the board's own build-up doesn't run: its parts just fade in
      if (moving && reduced()) Motion.enterBoard(n, 'fwd');
      Motion.fly($('fly'), $('hlogo'), n.querySelector('.mlogo'));
      Head.logoAway();
      replay($('fly'), 'go');
      // nothing ticks while the logo is still on its way down (1.3 s into the board's build-up), however fast
      // the run is
      this.startAt = performance.now() + (reduced() ? 150 : 1300);
      this.pump();
    });
  },

  stop() {
    this.timers.clear();
    clearTimeout(this.pumpTimer);
    this.queue = [];
  },

  // every event of the run waits its turn: a line that starts running stays up a moment before the next one
  push(fn, line) {
    this.queue.push({ fn, line });
    this.pump();
  },

  pump() {
    clearTimeout(this.pumpTimer);
    while (this.queue.length) {
      const item = this.queue[0];
      const at = performance.now();
      let wait = 0;
      if (item.line === 'end' || (item.line != null && item.line !== this.now)) wait = Math.max(this.startAt - at, this.nowAt + this.dwell * 1000 - at);
      if (wait > 0) {
        // (until the board has come in, its entrance calls pump itself)
        if (wait !== Infinity) this.pumpTimer = setTimeout(() => this.pump(), wait);
        return;
      }
      this.queue.shift();
      if (item.line != null && item.line !== 'end' && item.line !== this.now) {
        this.now = item.line;
        this.nowAt = performance.now();
      }
      item.fn();
    }
  },

  progress(e) {
    const marks = Array.isArray(e.marks) ? e.marks : [];
    this.push(() => this.apply(e), marks.indexOf('now'));
  },

  apply(e) {
    const steps = $('steps');
    if (!steps) return;
    [...steps.children].forEach((li, i) => {
      const m = e.marks[i] || 'todo';
      if (!li.classList.contains(m)) {
        li.classList.remove('todo', 'now', 'done');
        li.classList.add(m);
        if (m === 'now') li.scrollIntoView({ block: 'nearest' });
      }
    });
    if (e.pic) showPicture($('pictures'), e.pic);
    swapText($('plabel'), e.label || '');
    swapText($('psub'), e.sub || '');
    this.pct(e.pct || 0);
    const cancel = $('w-cancel');
    if (cancel && !cancel.hidden) cancel.disabled = !e.canStop;
    // (after Cancel, the lines still waiting their turn don't take back the "stopping" note)
    if (e.net && !this.stopped) swapText($('w-net'), e.net);
  },

  pct(p) {
    $('pfill').style.width = p + '%';
    $('pct').textContent = Math.round(p) + '%';
    $('pbar').setAttribute('aria-valuenow', Math.round(p));
  },

  log(text) {
    S.log.push(String(text));
    const box = $('log-lines');
    if (box && S.board === 'work') box.append(el('div', null, String(text)));
  },

  // Install is putting everything back after a failure while copying
  rollback(e) {
    this.push(() => {
      showPicture($('pictures'), 'rb');
      swapText($('plabel'), e.label || '');
    }, null);
  },

  // Cancel pressed: nothing more can be pressed until Install.exe says how it ended
  stopping() {
    const cancel = $('w-cancel');
    if (cancel) cancel.disabled = true;
    this.stopped = true;
    if ($('w-net')) swapText($('w-net'), t('WebNetStopping'));
  },

  // all in: the steps are ticked off, the logo celebrates, then the board becomes the done board
  done(e) {
    this.push(() => {
      S.busy = false;
      const n = $('work');
      if (!n) return Done.show(e);
      for (const li of $('steps').children) {
        li.classList.remove('todo', 'now');
        li.classList.add('done');
      }
      showPicture($('pictures'), null);
      swapText($('plabel'), e.label || '');
      swapText($('psub'), e.sub || '');
      this.pct(100);
      $('pfill').classList.add('full');
      $('w-cancel').disabled = true;
      n.classList.add('fin');
      this.timers.after(reduced() ? 0 : 1.5, () => Done.show(e));
    }, 'end');
  },

  // how it ended when it didn't finish: back to setup (cancelled), or the failed board
  end(fn) {
    this.push(() => {
      S.busy = false;
      fn();
    }, 'end');
  },
};

// ---------- 3: done ----------

// Built on the working board: the logo and the progress card stay where they are, the steps give way to what
// was done and the log to the ready card; the log is still there under Log.
const Done = {
  show(e, relang) {
    S.board = 'done';
    S.busy = false;
    Head.lock();
    let n = $('work') || $('done');
    const fresh = !n;
    if (fresh) {
      // (a new language) or no working board to build on
      n = el('section', 'layer upd inst');
      n.dataset.b = 'done';
      n.innerHTML = `<div class="lbody"><div class="col-l"></div><div class="col-r"><div class="mstage wsc" aria-hidden="true"></div>
<div class="pcard"><div class="prow"><div class="pl" id="plabel"></div><div class="pp"><span class="pn" id="pct">100%</span></div></div>
<div class="pbar" id="pbar"><div class="f full" id="pfill"></div></div><div class="psub" id="psub"></div></div></div></div><div class="lfoot"></div>`;
      Pics.stage(n.querySelector('.mstage'), []);
      swapText(n.querySelector('#plabel'), e.label || '');
      swapText(n.querySelector('#psub'), e.sub || '');
    }
    n.id = 'done';
    const install = e.kind !== 'uninstall';
    const colL = n.querySelector('.col-l');
    const colR = n.querySelector('.col-r');
    const foot = n.querySelector('.lfoot');
    const build = () => {
      colL.textContent = '';
      colL.append(el('h1', 'lh', e.title), el('p', 'ls', e.lead), el('div', 'sumh', t('WebDoneList')));
      const sum = el('ol', 'sum');
      for (const l of e.summary || []) {
        const li = el('li', 'st ' + (l.done ? 'done' : 'kept'));
        li.append(el('span', 'mk'), el('div', null, l.text));
        sum.append(li);
      }
      colL.append(sum);
      colR.querySelector('.tail')?.remove();
      colR.querySelector('.tail2')?.remove();
      const tail = el('div', 'tail2');
      const card = el('div', 'okcard');
      const words = el('div');
      words.append(el('b', null, e.card), el('span', null, e.cardSmall));
      card.append(el('span', 'mk'), words);
      const det = el('details', 'det');
      det.append(el('summary', null, t('WebLog')));
      const box = el('div', 'logbox');
      box.tabIndex = 0;
      for (const line of e.log || S.log) box.append(el('div', null, line));
      det.append(box);
      tail.append(card, det);
      colR.append(tail);
      foot.textContent = '';
      const net = el('p', 'net');
      if (S.init.mod && S.init.mod.website) net.append(button('llink', t('Website'), () => send('open', { value: 'website' })));
      foot.append(net);
      if (install) {
        foot.append(button('lb gho', t('Close'), () => send('close')), button('lb pri', t('WebStart'), () => send('start')));
      } else {
        foot.append(button('lb pri', t('Close'), () => send('close')));
      }
      n.classList.remove('play', 'fin', 'enter');
      n.classList.add('done-b');
      Head.state(e.state);
      // the progress card's words too, when the language has changed
      if (relang) {
        n.querySelector('.pl').textContent = e.label || '';
        n.querySelector('.psub').textContent = e.sub || '';
      }
    };
    if (fresh) {
      build();
      Board.change('none', n);
      return;
    }
    if (relang || reduced()) {
      build();
      if (!relang) focusLater(foot.querySelector('.lb.pri'), 0);
      return;
    }
    // what changes lifts away (the logo and the progress card stay where they are), then the done parts come up
    // in its place, settling with a hint of overshoot
    const gone = Motion.exitBoard(n, 'up', {
      l: [...colL.children], r: [...colR.querySelectorAll('.tail')], f: [...foot.children],
    });
    Work.timers.after(gone, () => {
      build();
      n.dataset.b = 'done';
      Motion.enterBoard(n, 'up', (p) => { p.r = p.r.filter((x) => !x.matches('.mstage, .pcard')); });
      focusLater(foot.querySelector('.lb.pri'), 500);
    });
  },
};

// ---------- 4: failed ----------

// Install.exe's names for the marks that Pics calls otherwise
const MARKS = { gh: 'github', '!': 'bang' };

const Fail = {
  e: null,

  show(e, relang) {
    const f = e.failed || e;
    this.e = f;
    S.board = 'fail';
    S.busy = false;
    Work.stop();
    Head.lock();
    const n = el('section', 'layer fail');
    n.id = 'fail';
    n.dataset.b = 'fail';
    const body = el('div', 'lbody one');
    const row = el('div', 'errrow');
    const mark = Pics.mark(MARKS[f.mark] || f.mark);
    const text = el('div', 'errtext');
    text.append(el('h1', 'lh', f.headline), el('p', 'ls', f.help || ''));
    row.append(mark, text);
    body.append(row);
    if (f.pill) {
      const pill = el('span', 'okpill' + (f.pillKind === 'warn' ? ' warn' : ''));
      pill.append(el('span', 'mk'), el('span', null, f.pill));
      const wrap = el('div');
      wrap.append(pill);
      body.append(wrap);
    }
    if (f.fw) {
      const way = el('div', 'fwway');
      way.append(el('p', null, f.fw.manual));
      way.append(button('llink', f.fw.release, () => send('open', { value: 'fwRelease' })));
      if (f.fw.keep) way.append(button('llink', f.fw.keep, () => send('keep')));
      body.append(way);
    }
    const det = el('details', 'det');
    det.open = !f.fw;
    det.append(el('summary', null, t('WebDetails')));
    const box = el('div', 'logbox');
    box.tabIndex = 0;
    box.setAttribute('aria-label', t('WebDetails'));
    for (const line of String(f.details || '').split(/\r?\n/)) box.append(el('div', null, line));
    det.append(box);
    body.append(det);

    const foot = el('div', 'lfoot');
    const net = el('p', 'net');
    const copy = button('llink', t('CopyDetails'), () => send('copy'));
    copy.id = 'f-copy';
    net.append(copy);
    foot.append(net);
    let primary;
    if (f.start) {
      primary = button('lb pri', t('Close'), () => send('close'));
      foot.append(primary);
    } else {
      if (f.fw) foot.append(button('lb gho', t('ChooseZip'), () => send('zip')));
      foot.append(button('lb gho', t('WebBack'), () => send('back')));
      primary = button('lb pri', t('Retry'), () => {
        send('retry');
        primary.classList.add('pressed');
      });
      foot.append(primary);
    }
    primary.dataset.primary = '1';
    n.append(body, foot);

    if (S.sheet) Sheet.close();
    $('fly').classList.remove('go');
    Head.reveal(false);
    Head.logoBack();
    Head.state(f.state);
    // the board on screen leaves calmly downwards and this one comes in once it has gone; the mark keeps its bounce
    Board.change(relang ? 'none' : 'fail', n, (moving) => {
      Motion.show(n);
      if (relang) return;
      replay(n, 'play');
      if (moving) Motion.enterBoard(n, 'fail');
    });
    if (!relang) focusLater(primary, 700);
  },

  copied() {
    const link = $('f-copy');
    if (!link) return;
    link.textContent = t('WebCopied');
    setTimeout(() => { if (link.isConnected) link.textContent = t('CopyDetails'); }, 1500);
  },
};

// ---------- window, keys ----------

$('btn-min').addEventListener('click', () => send('minimize'));
$('btn-close').addEventListener('click', () => send('close'));

// when the window comes up, WebView2 puts the focus on the page's first control: it would show its ring and take
// Enter from the board's default button, so it only stays once a key or the mouse has been used
let touched = false;
for (const type of ['keydown', 'pointerdown']) document.addEventListener(type, () => { touched = true; }, true);
for (const id of ['hsel', 'btn-min']) $(id).addEventListener('focus', (e) => { if (!touched) e.currentTarget.blur(); });

// the header moves the window (outside its buttons and the language select)
$('head').addEventListener('mousedown', (e) => {
  if (e.button === 0 && !e.target.closest('button, select')) send('drag');
});

// (not on a board that is still coming in or already leaving)
function press(node) {
  if (node && !node.disabled && node.isConnected && !node.closest('[hidden], [inert]')) node.click();
}

// any key skips the intro; Enter presses the default button, Esc the way out (on a sheet: × or Cancel)
document.addEventListener('keydown', (e) => {
  if (Intro.live) {
    Intro.skip();
    return;
  }
  if (e.repeat || (e.key !== 'Enter' && e.key !== 'Escape')) return;
  const esc = e.key === 'Escape';
  if (!esc && e.target instanceof Element && e.target.closest('button, summary, a, select, input, textarea')) return;
  if (S.sheet && Sheet.keys) {
    e.preventDefault();
    press((esc ? Sheet.keys.esc : Sheet.keys.enter)());
    return;
  }
  const board = Board.node;
  if (!board) return;
  switch (S.board) {
    case 'setup':
      if (esc) send('close');
      else Setup.run();
      break;
    case 'work':
      if (esc) press($('w-cancel'));
      break;
    case 'done':
      if (esc) send('close');
      else press(board.querySelector('.lfoot .lb.pri'));
      break;
    case 'fail':
      if (esc) press([...board.querySelectorAll('.lfoot .lb')].find((b) => b.textContent === t('WebBack')) || board.querySelector('.lfoot .lb.pri'));
      else press(board.querySelector('[data-primary]'));
      break;
  }
});

// ---------- closing ----------

// Install.exe is closing the window: everything stops, the board's parts leave, the header and the page fade,
// then Install.exe fades the window itself away. With motion reduced the window just closes
function bye() {
  if (S.board === 'bye') return;
  S.board = 'bye';
  Intro.timers.clear();
  Intro.live = false;
  Work.stop();
  Board.timers.clear();
  Motion.timers.clear();
  const win = $('win');
  win.inert = true;
  if (reduced()) {
    send('gone', { on: false });
    return;
  }
  // the board's parts leave first (a question sheet with them), then the header and the page fade
  const board = Board.node;
  if (board && !board.hidden) Motion.exitBoard(board, 'bye');
  if (Sheet.veil) Motion.sheetClose(Sheet.veil, null);
  win.classList.add('x-bye');
  setTimeout(() => send('gone', { on: true }), 300);
}

// ---------- events from Install.exe ----------

function words(e) {
  S.texts = e.texts || {};
  root.lang = e.lang || 'en';
  S.init.lang = e.lang;
  Head.words();
}

function init(e) {
  if (S.init) return;
  S.init = e;
  words(e);
  root.classList.toggle('mf-write', e.lettering !== 'type');
  root.classList.toggle('pb-edge', e.bar !== 'text');
  $('app').textContent = e.mod ? `${e.mod.name} ${e.mod.version}` : "Drag'n Wash Mod Installer";
  $('app').title = $('app').textContent;
  Head.langs();
  S.mod = e.mod ? { name: e.mod.name, icon: e.mod.icon || '' } : null;
  if (e.failed) {
    // no mod files next to Install.exe: straight to the failed board, with Close only
    Head.reveal(false);
    Fail.show(e.failed);
    return;
  }
  S.setup = e.setup;
  Intro.start(!!e.found);
}

window.dnw = (e) => {
  if (!e || typeof e !== 'object') return;
  switch (e.type) {
    case 'init': return init(e);
    case 'texts':
      words(e);
      Head.langs();
      if (Head.key) Head.stateKey(Head.key);
      return;
    case 'setup':
      if (S.board === 'work') {
        // cancelled: back to setup with the banner, the logo flying back up
        Work.end(() => {
          S.setup = Object.assign({}, e.setup, { banner: e.banner || '' });
          const work = $('work');
          const mlogo = work && work.querySelector('.mlogo');
          // the working logo flies back up into the header while the board leaves to the right
          if (reduced() || !mlogo) {
            $('fly').classList.remove('go');
            $('hlogo').classList.remove('away');
          } else {
            Motion.flyBack($('fly'), $('hlogo'), mlogo);
          }
          const parts = work ? Motion.partsOf(work) : null;
          if (parts) parts.r = parts.r.filter((x) => x !== mlogo);
          Head.stateKey('WebStateSetup');
          Setup.show(false, 'back', parts ? { parts } : {});
        });
        return;
      }
      if (S.board === 'fail' || S.board === 'done') {
        S.setup = Object.assign({}, e.setup, { banner: e.banner || '' });
        Head.stateKey('WebStateSetup');
        Setup.show(false, 'back');
        return;
      }
      S.setup = Object.assign({}, e.setup, { banner: e.banner || '' });
      if (S.board === 'setup') Setup.fill(S.setup);
      return;
    case 'sheet':
      if (e.sheet === 'steam') Sheet.steam(e);
      else Sheet.consent(e);
      return;
    case 'steamWait': return Sheet.steamWait(e.phase);
    case 'sheetClose': return Sheet.close();
    case 'start': return Work.start(e);
    case 'progress': return Work.progress(e);
    case 'log': return Work.log(e.text);
    case 'rollback': return Work.rollback(e);
    case 'stopping': return Work.stopping();
    case 'done':
      if (e.relang) return Done.show(e, true);
      return Work.done(e);
    case 'failed':
      if (e.relang || S.board !== 'work') return Fail.show(e.failed, e.relang);
      return Work.end(() => Fail.show(e.failed));
    case 'copied': return Fail.copied();
    case 'bye': return bye();
  }
};

send('ready');
