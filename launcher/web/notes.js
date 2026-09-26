'use strict';
/* Release notes. The body is a GitHub release body: Markdown from the internet, so it is untrusted text.
   Everything is built as DOM nodes with textContent, never innerHTML. Only headings, list items and
   **bold** / __bold__ get any formatting; everything else (links included) stays plain text.

   Language sections: a heading named 日本語 (or Japanese) starts the Japanese part, which runs to the next
   heading of the same or a higher level named English (or 英語), or to the end. Japanese shows only that part;
   English shows the body without it. With no Japanese part, both show the whole body. */

const ReleaseNotes = (() => {
  const HEADING = /^ {0,3}(#{1,6})[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$/;
  const ITEM = /^([ \t]*)([-*+]|\d{1,9}[.)])[ \t]+(.*)$/;
  const BOLD = /(\*\*|__)(?=\S)(.+?)(?<=\S)\1/g;

  // one entry per line: heading, list item, text, or blank
  function parse(body) {
    return String(body || '').replace(/\r\n?/g, '\n').split('\n').map((raw) => {
      let m = HEADING.exec(raw);
      if (m) return { kind: 'heading', level: m[1].length, text: m[2] };
      m = ITEM.exec(raw);
      if (m) {
        const indent = m[1].replace(/\t/g, '    ').length;
        return { kind: 'item', depth: Math.min(3, Math.floor(indent / 2)), ordered: /\d/.test(m[2]), text: m[3] };
      }
      return raw.trim() ? { kind: 'text', text: raw.trim() } : { kind: 'blank' };
    });
  }

  // "## 日本語", "## 🇯🇵 Japanese", "### 日本語 / Japanese" all count; letters only, case ignored
  function isLanguage(line, words) {
    if (line.kind !== 'heading') return false;
    let rest = line.text.replace(/[^\p{L}]/gu, '').toLowerCase();
    if (!rest) return false;
    for (const w of words) rest = rest.split(w).join('');
    return rest === '';
  }
  const JAPANESE = ['japanese', '日本語'];
  const ENGLISH = ['english', '英語'];

  function pickLanguage(lines, lang) {
    const start = lines.findIndex((l) => isLanguage(l, JAPANESE));
    if (start < 0) return lines;
    const level = lines[start].level;
    let end = lines.findIndex((l, i) => i > start && l.level <= level && isLanguage(l, ENGLISH));
    if (end < 0) end = lines.length;
    if (lang === 'ja') return lines.slice(start + 1, end);
    // English: without the Japanese part, and without a bare "English" marker heading
    return lines.slice(0, start).concat(lines.slice(end)).filter((l) => !isLanguage(l, ENGLISH));
  }

  // text with **bold** turned into <b>, everything else as plain text nodes
  function inline(parent, text) {
    let at = 0;
    for (const m of text.matchAll(BOLD)) {
      if (m.index > at) parent.append(text.slice(at, m.index));
      const b = document.createElement('b');
      b.textContent = m[2];
      parent.append(b);
      at = m.index + m[0].length;
    }
    if (at < text.length) parent.append(text.slice(at));
  }

  /* Builds the notes as a fragment. Headings become h5 (levels 1 and 2) or h6, so they sit under the
     version heading the page puts on top. A run of list items becomes one list; nested items are indented.
     Lines of plain text next to each other become one paragraph, keeping their line breaks. */
  function render(body, lang) {
    const out = document.createDocumentFragment();
    let list = null;
    let para = null;
    for (const line of pickLanguage(parse(body), lang)) {
      if (line.kind !== 'item') list = null;
      if (line.kind !== 'text') para = null;
      if (line.kind === 'heading') {
        const h = document.createElement(line.level <= 2 ? 'h5' : 'h6');
        inline(h, line.text);
        out.append(h);
      } else if (line.kind === 'item') {
        // a new list when the kind changes at the top level (bullets after numbers, or the other way round)
        if (list && !line.depth && list.tagName !== (line.ordered ? 'OL' : 'UL')) list = null;
        if (!list) {
          list = document.createElement(line.ordered ? 'ol' : 'ul');
          out.append(list);
        }
        const li = document.createElement('li');
        if (line.depth) li.className = 'd' + line.depth;
        inline(li, line.text);
        list.append(li);
      } else if (line.kind === 'text') {
        if (para) para.append('\n');
        else {
          para = document.createElement('p');
          out.append(para);
        }
        inline(para, line.text);
      }
    }
    return out;
  }

  return { render };
})();
