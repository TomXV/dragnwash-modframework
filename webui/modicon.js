'use strict';
/* Shared page parts (webui/): a mod's icon, or the letter tile the in-game Mods screen draws for a mod without
   one (ModsMenu.List.cs: ModIcon, ShortName, Initials, StableHash, InitialsColors; the two must agree).
   A mod here is {name, guid, icon}; icon is a data:image/png or data:image/jpeg URL, or "" / missing.

     ModIcon.shortName(name)       what the tile is made from: what follows the last ": ", without a leading
                                   "Drag'n Wash" ("Drag'n Wash Localization" → "Localization")
     ModIcon.initials(name)        the tile's letters: first letters of the first two words ("Bubble Timer" → BT)
     ModIcon.color(mod)            the tile's colour, the same as the Mods screen's for that mod
     ModIcon.url(mod)              the icon's data URL, or '' when there is none (or it isn't a PNG/JPEG data URL)
     ModIcon.el(mod, px)           an HTML element px × px: <img class="micon"> or <span class="micon tile">
     ModIcon.svg(mod, x, y, size)  an SVG <g> for inside a picture: <image> or the tile (rect + letters) */

const ModIcon = (() => {
  const COLORS = ['#80b3e6', '#e8b873', '#52c7b8', '#e6949e', '#ad99eb', '#94cc80'];
  const INSET = '#0e121a';
  const STATE_WORDS = ['experimental', 'beta', 'alpha', 'preview', 'wip'];
  const NS = 'http://www.w3.org/2000/svg';

  // the same number for a text every time: C#'s int arithmetic on UTF-16 units
  function hash(text) {
    let h = 17;
    const s = String(text || '');
    for (let i = 0; i < s.length; i++) h = (Math.imul(h, 31) + s.charCodeAt(i)) | 0;
    return h & 0x7fffffff;
  }

  // "Drag'n Wash ModFramework: Inspector" → "Inspector"; "Drag'n Wash Localization" → "Localization"; a name
  // that is only "Drag'n Wash" stays
  function shortName(name) {
    let n = String(name || '');
    const colon = n.lastIndexOf(': ');
    if (colon >= 0 && colon + 2 < n.length) n = n.slice(colon + 2);
    const m = /^drag['’]n wash[ :\-]+/i.exec(n);
    if (m && n.length > m[0].length) n = n.slice(m[0].length);
    return n;
  }

  // the first letter of each of the first two words, or the first letter alone for one word; what is in brackets
  // and words like "experimental" are passed over
  function initials(name) {
    let kept = '';
    let depth = 0;
    for (const c of String(name || '')) {
      if (c === '(' || c === '[') depth++;
      else if ((c === ')' || c === ']') && depth > 0) depth--;
      else if (depth === 0) kept += c;
    }
    let letters = '';
    for (const word of kept.split(/[ \-_.]+/).filter(Boolean)) {
      if (STATE_WORDS.includes(word.toLowerCase())) continue;
      const first = [...word].find((c) => c.length === 1 && /[\p{L}\p{Nd}]/u.test(c));
      if (first) {
        const up = first.toUpperCase();
        letters += up.length === 1 ? up : first;
      }
      if (letters.length === 2) break;
    }
    return letters || '?';
  }

  const color = (mod) => {
    const m = mod || {};
    return COLORS[hash(m.guid ?? shortName(m.name)) % COLORS.length];
  };

  // only a picture we can show as it is: a PNG or JPEG in base64
  const url = (mod) => {
    const u = mod && typeof mod.icon === 'string' ? mod.icon : '';
    return /^data:image\/(png|jpeg);base64,[A-Za-z0-9+/=]+$/.test(u) ? u : '';
  };

  function el(mod, px) {
    const src = url(mod);
    let node;
    if (src) {
      node = document.createElement('img');
      node.className = 'micon';
      node.alt = '';
      node.src = src;
      node.style.borderRadius = px * .25 + 'px';
      node.style.objectFit = 'cover';
    } else {
      node = document.createElement('span');
      node.className = 'micon tile';
      node.textContent = initials(shortName(mod && mod.name));
      Object.assign(node.style, {
        display: 'inline-flex', alignItems: 'center', justifyContent: 'center', boxSizing: 'border-box',
        background: color(mod), color: INSET, borderRadius: px * .25 + 'px', fontWeight: '700',
        fontSize: px * .4 + 'px', lineHeight: '1', fontFamily: '"Segoe UI","Yu Gothic UI",sans-serif',
      });
    }
    node.setAttribute('aria-hidden', 'true');
    node.style.width = node.style.height = px + 'px';
    node.style.flex = '0 0 auto';
    return node;
  }

  function svg(mod, x, y, size) {
    const g = document.createElementNS(NS, 'g');
    const put = (tag, attrs) => {
      const n = document.createElementNS(NS, tag);
      for (const [k, v] of Object.entries(attrs)) n.setAttribute(k, v);
      g.append(n);
      return n;
    };
    const src = url(mod);
    if (src) {
      const clip = 'mic' + Math.random().toString(36).slice(2, 9);
      const defs = put('defs', {});
      const cp = document.createElementNS(NS, 'clipPath');
      cp.id = clip;
      const r = document.createElementNS(NS, 'rect');
      for (const [k, v] of Object.entries({ x, y, width: size, height: size, rx: size * .25 })) r.setAttribute(k, v);
      cp.append(r);
      defs.append(cp);
      put('image', { href: src, x, y, width: size, height: size, preserveAspectRatio: 'xMidYMid slice', 'clip-path': `url(#${clip})` });
    } else {
      put('rect', { x, y, width: size, height: size, rx: size * .25, fill: color(mod) });
      const text = put('text', {
        x: x + size / 2, y: y + size / 2, dy: '.36em', 'text-anchor': 'middle', 'font-size': size * .4,
        'font-weight': 700, fill: INSET, style: 'font-family:"Segoe UI","Yu Gothic UI",sans-serif;stroke:none',
      });
      text.textContent = initials(shortName(mod && mod.name));
    }
    return g;
  }

  return { shortName, initials, hash, color, url, el, svg };
})();
