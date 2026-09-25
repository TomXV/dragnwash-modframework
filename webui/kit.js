'use strict';
/* Shared page parts (webui/): small helpers every page of ours uses. Load it first, before the other webui
   scripts and the page's own.

   Gives the page these globals:
     $(id)                   document.getElementById
     root                    <html>
     sec(s)                  1.5 → "1.500s", for CSS times set from script
     el(tag, cls, text)      a new element with its class and text
     replay(node, cls)       takes a class off and puts it back, so the CSS animation tied to it starts over
     swapText(box, text)     fades a new text in over the old one in the same grid cell (a box of .ph spans)
     boxOf(node, top)        a box's place inside `top` (default #win) at rest, ignoring running transforms: {x, y, w}
     reduced()               true while Windows asks for less motion; <html> has .reduced then (the CSS keys off it)
     Timers                  timeouts that belong to one part of a page: t.after(seconds, fn), t.clear() */

const $ = (id) => document.getElementById(id);
const root = document.documentElement;
const sec = (s) => s.toFixed(3) + 's';

// .reduced mirrors "reduce animations"; the stylesheets key off it
const motionQuery = matchMedia('(prefers-reduced-motion: reduce)');
const syncMotion = () => root.classList.toggle('reduced', motionQuery.matches);
syncMotion();
motionQuery.addEventListener('change', syncMotion);
const reduced = () => root.classList.contains('reduced');

function el(tag, cls, text) {
  const node = document.createElement(tag);
  if (cls) node.className = cls;
  if (text != null) node.textContent = text;
  return node;
}

// restarts the CSS animation tied to a class
function replay(node, cls) {
  node.classList.remove(cls);
  node.getBoundingClientRect();
  node.classList.add(cls);
}

// fades a new text in over the old one in the same grid cell (progress label, sub line, header state)
function swapText(box, text) {
  const last = box.lastElementChild;
  if (last && last.classList.contains('on') && last.textContent === text) return;
  for (const old of [...box.children]) {
    old.classList.remove('on');
    setTimeout(() => old.remove(), 400);
  }
  const span = el('span', 'ph', text);
  box.append(span);
  span.getBoundingClientRect();
  span.classList.add('on');
}

// a box's place in the window at rest (offsets ignore running transforms)
function boxOf(node, top = $('win')) {
  let x = 0;
  let y = 0;
  for (let n = node; n && n !== top; n = n.offsetParent) {
    x += n.offsetLeft;
    y += n.offsetTop;
  }
  return { x, y, w: node.offsetWidth };
}

// timeouts that belong to one part of the page, cleared together when it is left
class Timers {
  constructor() { this.ids = new Set(); }
  after(seconds, fn) {
    const id = setTimeout(() => { this.ids.delete(id); fn(); }, Math.max(0, seconds * 1000));
    this.ids.add(id);
    return id;
  }
  clear() {
    for (const id of this.ids) clearTimeout(id);
    this.ids.clear();
  }
}
