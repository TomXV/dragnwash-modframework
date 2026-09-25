'use strict';
/* Shared page parts (webui/): how boards, sheets and rows come and go. Needs kit.js; the keyframes and the
   rules are in motion.css.

   Boards are .layer sections under the header, one on screen at a time, each with data-b naming what it is
   (list, upd, fail, setup, done), so Motion knows its parts: the left column, the right column and the footer.
   A change is one of five kinds:
     fwd   onwards (list → updating, Try again, setup → installing): leaves to the left, comes in from the right
     back  back (Cancel, failed → Back): leaves to the right, comes in from the left
     up    success (installing → done): leaves upwards, comes in from below and settles with a little overshoot
     fail  something failed: leaves downwards, calm, and the next board only starts once it is gone
     bye   the window closes: a leading part (the countdown or ready card) first, then the rest
   With motion reduced, every leaving is a .12 s fade and every coming in a .15 s fade, one after the other.
   Commands to the app go out on the click, never after the animation: a page sends first, then calls Motion.

     Motion.timers                     the timeouts Motion sets; a page clears them when it stops (or closes)
     Motion.KIND                       each kind's numbers (seconds and pixels)
     Motion.kind(name)                 those numbers, or the reduced ones while motion is reduced
     Motion.show(board)                shows a board as it is (no entrance of its own)
     Motion.reset(board)               takes a past change's marks off a board (before it is built again)
     Motion.setKind(board, name)       writes a kind's numbers on a board, with the class k-<name>
     Motion.partsOf(board)             {l, r, f}: the board's visible parts by column, in reading order
     Motion.exitBoard(board, kind, parts, first)  the parts leave (first = parts that lead); the board goes inert;
                                       returns the seconds until the last part is gone
     Motion.enterBoard(board, kind, pick)  shows the board and its parts come in (pick(parts) may change which);
                                       returns the seconds until the last part is in
     Motion.change(from, kind, startNext, {parts, first, timers})  the whole change: the old board leaves,
                                       startNext() runs `lap` before it is gone (after it for fail), then the old
                                       board is hidden. Returns the seconds until startNext, at once
     Motion.fly(fly, hlogo, target)    aims the header logo's copy (.fly) from the header logo to a working logo
     Motion.flyBack(fly, hlogo, mlogo) the working logo flies back up into the header (Cancel)
     Motion.sheetOpen(board, veil, host)   a sheet comes up over the window (veil = <div class="veil"><div class="sheet">);
                                       the board behind steps back and goes inert
     Motion.sheetSwap(veil, next)      one sheet makes way for the next; the veil stays
     Motion.sheetClose(veil, board)    the sheet goes; the board behind is back and takes clicks at once
     Motion.collapse(row)              a row (a plan line) leaves to the left, then the rows below close up; removed
     Motion.expand(row)                a row just put in: the gap opens, then it comes in from the left
     Motion.details(det)               opens or closes a <details> (words first, then the box; the other way
                                       round when opening)
     Motion.skip(row, on, text)        a skipped version: the row steps back with a small tag, or comes back */

const Motion = (() => {
  const timers = new Timers();

  const KIND = {
    //        leaving: time, gap, x, y, curve                         coming in: time, gap, x, y, curve,               overlap, order
    fwd:  { out: .26, gapOut: .035, ox: -14, oy: 0, eo: 'var(--e-out)',  in: .4,  gapIn: .06, ix: 14,  iy: 0,  ei: 'var(--e-in)',     lap: .08,  order: 'ltr' },
    back: { out: .26, gapOut: .035, ox: 14,  oy: 0, eo: 'var(--e-out)',  in: .4,  gapIn: .06, ix: -14, iy: 0,  ei: 'var(--e-in)',     lap: .08,  order: 'rtl' },
    up:   { out: .26, gapOut: .035, ox: 0,  oy: -8, eo: 'var(--e-out)',  in: .45, gapIn: .06, ix: 0,   iy: 10, ei: 'var(--e-settle)', lap: .06,  order: 'group' },
    fail: { out: .3,  gapOut: .04,  ox: 0,  oy: 5,  eo: 'var(--e-calm)', in: .45, gapIn: .08, ix: 0,   iy: -4, ei: 'var(--e-calm)',   lap: -.04, order: 'group' },
    bye:  { out: .18, gapOut: .06,  ox: 0,  oy: 4,  eo: 'var(--e-out)',  in: 0,   gapIn: 0,   ix: 0,   iy: 0,  ei: 'linear',          lap: 0,    order: 'group' },
  };
  const REDUCED = { out: .12, gapOut: 0, ox: 0, oy: 0, in: .15, gapIn: 0, ix: 0, iy: 0, lap: 0 };
  const kind = (name) => (reduced() ? { ...KIND[name], ...REDUCED } : KIND[name]);

  // the parts of each board, by column: l (left), r (right), f (footer)
  const PARTS = {
    list: { l: '.col-l .lh, .col-l .ls, .mod, .total', r: '.nhead, .ncant, .notes', f: '.lfoot > *' },
    upd: { l: '.col-l .lh, .col-l .ls, .st', r: '.mlogo, .scbox, .pcard, .tail', f: '.lfoot > *' },
    fail: { l: '.one > *', r: '', f: '.lfoot > *' },
    setup: { l: '.col-l > *', r: '.col-r > *', f: '.lfoot > *' },
    done: { l: '.col-l > *', r: '.mstage, .pcard, .okcard, .det', f: '.lfoot > *' },
  };

  const shown = (n) => n && !n.hidden && n.getClientRects().length > 0;

  function show(board) {
    board.hidden = false;
    board.inert = false;
  }

  function reset(board) {
    board.classList.remove('x-out', 'x-in');
    for (const c of [...board.classList]) if (c.startsWith('k-')) board.classList.remove(c);
    for (const n of board.querySelectorAll('[data-p]')) {
      n.removeAttribute('data-p');
      n.style.removeProperty('--po');
      n.style.removeProperty('--pi');
    }
    for (const n of board.querySelectorAll('.pressed')) n.classList.remove('pressed');
    board.inert = false;
  }

  function setKind(board, name) {
    const k = kind(name);
    const s = board.style;
    s.setProperty('--do', sec(k.out));
    s.setProperty('--go', sec(k.gapOut));
    s.setProperty('--di', sec(k.in));
    s.setProperty('--gi', sec(k.gapIn));
    s.setProperty('--ox', k.ox + 'px');
    s.setProperty('--oy', k.oy + 'px');
    s.setProperty('--ix', k.ix + 'px');
    s.setProperty('--iy', k.iy + 'px');
    s.setProperty('--eo', k.eo);
    s.setProperty('--ei', k.ei);
    for (const c of [...board.classList]) if (c.startsWith('k-')) board.classList.remove(c);
    board.classList.add('k-' + name);
    return k;
  }

  function partsOf(board) {
    const sel = PARTS[board.dataset.b] || { l: '', r: '', f: '' };
    const q = (s) => (s ? [...board.querySelectorAll(s)].filter(shown) : []);
    return { l: q(sel.l), r: q(sel.r), f: q(sel.f) };
  }

  // leaving: in the direction of travel, the leading side first (ltr: the left column; rtl: the right column), or
  // all at once (group); the footer last. Indices are capped so long lists don't drag
  function exitBoard(board, name, p = partsOf(board), first = []) {
    for (const n of board.querySelectorAll('[data-p]')) n.removeAttribute('data-p');
    const k = setKind(board, name);
    const cap = (i) => Math.min(i, 4);
    const lead = new Set(first);
    const l = p.l.filter((n) => !lead.has(n));
    const r = p.r.filter((n) => !lead.has(n));
    const list = first.map((n) => [n, 0]);
    const o = first.length ? 1 : 0;
    if (k.order === 'ltr') {
      l.forEach((n, i) => list.push([n, o + cap(i)]));
      r.forEach((n, i) => list.push([n, o + cap(i + 1)]));
    } else if (k.order === 'rtl') {
      r.forEach((n, i) => list.push([n, o + cap(i)]));
      l.forEach((n, i) => list.push([n, o + cap(i + 1)]));
    } else {
      [...l, ...r].forEach((n) => list.push([n, o]));
    }
    const max = Math.max(0, ...list.map((x) => x[1]));
    p.f.filter((n) => !lead.has(n)).forEach((n) => list.push([n, k.order === 'group' ? o + (first.length ? 0 : 1) : max + 1]));
    for (const [n, i] of list) {
      n.dataset.p = '';
      n.style.setProperty('--po', i);
    }
    board.classList.remove('x-in');
    board.classList.add('x-out');
    board.inert = true;
    const last = Math.max(0, ...list.map((x) => x[1]));
    return k.out + (reduced() ? 0 : k.gapOut * last);
  }

  // coming in: always in reading order (heading first), the right column one step behind the left, the footer last
  function enterBoard(board, name, pick) {
    board.hidden = false;
    board.classList.remove('x-out');
    for (const n of board.querySelectorAll('[data-p]')) n.removeAttribute('data-p');
    const p = partsOf(board);
    if (pick) pick(p);
    const k = setKind(board, name);
    const cap = (i) => Math.min(i, 3);
    const list = [];
    p.l.forEach((n, i) => list.push([n, cap(i)]));
    p.r.forEach((n, i) => list.push([n, cap(i + 1)]));
    const max = Math.max(0, ...list.map((x) => x[1]));
    p.f.forEach((n) => list.push([n, max + 1]));
    for (const [n, i] of list) {
      n.dataset.p = '';
      n.style.setProperty('--pi', i);
    }
    replay(board, 'x-in');
    board.inert = false;
    const last = Math.max(0, ...list.map((x) => x[1]));
    return k.in + (reduced() ? 0 : k.gapIn * last);
  }

  // the whole change: the old board leaves, the next starts `lap` before it is gone (after it, if lap < 0)
  function change(from, name, startNext, opts = {}) {
    const t = opts.timers || timers;
    const k = kind(name);
    const gone = exitBoard(from, name, opts.parts || partsOf(from), opts.first || []);
    const at = Math.max(0, gone - k.lap);
    const hide = () => {
      from.hidden = true;
      reset(from);
    };
    if (at > gone) t.after(gone, hide);
    t.after(at, () => {
      startNext();
      if (at <= gone) t.after(gone - at, hide);
    });
    return at;
  }

  // the header logo's copy flies from the header logo's box to the working logo's box (.fly.go runs it)
  function fly(copy, hlogo, target) {
    hlogo.classList.remove('away', 'back', 'land', 'landing');
    const from = boxOf(hlogo);
    const to = boxOf(target);
    const w = from.w || 50;
    const s = copy.style;
    s.setProperty('--fl', from.x + 'px');
    s.setProperty('--ft', from.y + 'px');
    s.setProperty('--fw', w + 'px');
    s.setProperty('--dx', to.x - from.x + 'px');
    s.setProperty('--dy', to.y - from.y + 'px');
    s.setProperty('--k', to.w / w);
    copy.classList.remove('back');
    target.classList.remove('flown');
  }

  // back up into the header (Cancel): the same copy, from the working logo to the header logo's place, while the
  // header logo makes room and shows as the copy lands
  function flyBack(copy, hlogo, mlogo) {
    const from = boxOf(mlogo);
    hlogo.classList.remove('away', 'back', 'land');
    hlogo.classList.add('landing');
    const to = boxOf(hlogo);
    const s = copy.style;
    s.setProperty('--fl', from.x + 'px');
    s.setProperty('--ft', from.y + 'px');
    s.setProperty('--fw', from.w + 'px');
    s.setProperty('--dx', to.x - from.x + 'px');
    s.setProperty('--dy', to.y - from.y + 'px');
    s.setProperty('--k', to.w / (from.w || 1));
    copy.classList.remove('go');
    replay(copy, 'back');
    mlogo.classList.add('flown');
    hlogo.classList.remove('landing');
    replay(hlogo, 'land');
  }

  // ----- sheets -----

  function sheetOpen(board, veil, host = $('win')) {
    if (board) {
      board.classList.add('under');
      board.inert = true;
    }
    host.append(veil);
    return veil;
  }

  function sheetSwap(veil, next) {
    const old = [...veil.querySelectorAll('.sheet')].find((n) => !n.classList.contains('swap-out'));
    if (old) {
      old.classList.add('swap-out');
      old.inert = true;
      timers.after(reduced() ? .12 : .22, () => old.remove());
    }
    next.classList.remove('swap-out');
    next.classList.add('swap-in');
    veil.append(next);
    return next;
  }

  function sheetClose(veil, board) {
    if (veil.classList.contains('x-out')) return;
    veil.classList.add('x-out');
    veil.inert = true;
    if (board) {
      board.classList.remove('under');
      board.inert = false;
    }
    timers.after(reduced() ? .12 : .26, () => veil.remove());
  }

  // ----- rows and boxes that change size -----

  const flat = { height: '0px', paddingTop: '0px', paddingBottom: '0px', marginTop: '0px', marginBottom: '0px', borderTopWidth: '0px', borderBottomWidth: '0px' };
  function sizeOf(node) {
    const cs = getComputedStyle(node);
    return {
      height: node.offsetHeight + 'px', paddingTop: cs.paddingTop, paddingBottom: cs.paddingBottom, marginTop: cs.marginTop,
      marginBottom: cs.marginBottom, borderTopWidth: cs.borderTopWidth, borderBottomWidth: cs.borderBottomWidth,
    };
  }

  async function collapse(row) {
    if (reduced()) {
      row.remove();
      return;
    }
    const full = sizeOf(row);
    row.style.boxSizing = 'border-box';
    row.style.overflow = 'hidden';
    await row.animate([{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateX(-14px)' }], { duration: 200, easing: 'cubic-bezier(.5,0,.75,.4)', fill: 'forwards' }).finished;
    await row.animate([full, flat], { duration: 240, easing: 'cubic-bezier(.4,0,.2,1)', fill: 'forwards' }).finished;
    row.remove();
  }

  async function expand(row) {
    if (reduced()) return;
    const full = sizeOf(row);
    row.style.boxSizing = 'border-box';
    row.style.overflow = 'hidden';
    row.style.opacity = '0';
    await row.animate([flat, full], { duration: 240, easing: 'cubic-bezier(.4,0,.2,1)' }).finished;
    row.style.opacity = '';
    await row.animate([{ opacity: 0, transform: 'translateX(-14px)' }, { opacity: 1, transform: 'none' }], { duration: 300, easing: 'cubic-bezier(.2,.7,.3,1)' }).finished;
    row.removeAttribute('style');
  }

  // a card collapsing: closing, the words fade first, then the box closes up; opening, the other way round.
  // <details> keeps its own meaning (open, the summary's state), only the in-between is drawn
  async function details(det) {
    const box = det.querySelector('.logbox');
    if (!box || reduced()) {
      det.open = !det.open;
      return;
    }
    if (det.dataset.moving) return;
    det.dataset.moving = '1';
    box.style.boxSizing = 'border-box';
    try {
      if (det.open) {
        const full = sizeOf(box);
        await box.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 120, easing: 'linear', fill: 'forwards' }).finished;
        await box.animate([full, flat], { duration: 220, easing: 'cubic-bezier(.4,0,.2,1)', fill: 'forwards' }).finished;
        det.open = false;
        for (const a of box.getAnimations()) a.cancel();
      } else {
        det.open = true;
        const full = sizeOf(box);
        box.animate([{ ...flat, opacity: 0 }, { ...full, opacity: 0 }], { duration: 240, easing: 'cubic-bezier(.4,0,.2,1)' });
        await new Promise((done) => setTimeout(done, 200));
        await box.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 200, easing: 'linear' }).finished;
      }
    } finally {
      box.style.boxSizing = '';
      delete det.dataset.moving;
    }
  }

  function skip(row, on, text) {
    row.classList.toggle('skipped', on);
    const line = row.querySelector('.mv');
    const old = line && line.querySelector('.skiptag:not(.bye)');
    if (on && line && !old) line.append(el('span', 'skiptag', text));
    if (!on && old) {
      if (reduced()) old.remove();
      else {
        old.classList.add('bye');
        timers.after(.16, () => old.remove());
      }
    }
  }

  return {
    timers, KIND, kind, show, reset, setKind, partsOf, exitBoard, enterBoard, change, fly, flyBack,
    sheetOpen, sheetSwap, sheetClose, collapse, expand, details, skip,
  };
})();
