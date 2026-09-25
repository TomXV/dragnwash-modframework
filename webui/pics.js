'use strict';
/* Shared page parts (webui/): the step pictures beside the working logo, and the failure marks. Needs kit.js;
   the look and the motion are in pics.css. Pictures are hand-made SVG (viewBox 84×72), kept here by name so any
   page can show any of them.

     Pics.names               every picture's name
     Pics.make(name)          a new <svg class="sc NAME"> (null for an unknown name)
     Pics.fill(box, names)    puts the named pictures into a .scbox, all hidden; show one with .on
     Pics.stage(host, names)  builds the working logo (sparkles, logo, gear, sponge) and a .scbox with the named
                              pictures into an empty .mstage.wsc host; returns {logo, box}
     Pics.mark(name)          a failure mark for the failed board: 'github' (GitHub's mark with the × badge) or 'bang' (!) */

const Pics = (() => {
  const SVG = {
    wait: '<svg class="sc wait" viewBox="0 0 84 72"><g class="gw fb"><rect class="ink" x="14" y="12" width="56" height="44" rx="4" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M14 22H70"/><path class="acc" d="M60 15L65 20M65 15L60 20" style="stroke-width:2.2"/><path d="M36 30V48L50 39Z" style="fill:var(--ink);opacity:.6"/></g></svg>',
    dl: '<svg class="sc dl" viewBox="0 0 84 72"><path class="ink" d="M28 24H56A8 8 0 0 0 57 8.1A11 11 0 0 0 36 7.5A8.5 8.5 0 0 0 28 24Z"/><g class="ar"><path class="acc" d="M42 20V40M35 33L42 40L49 33"/></g><g class="tr"><path class="ink" d="M18 48V60A3 3 0 0 0 21 63H63A3 3 0 0 0 66 60V48"/><path class="ink" d="M18 53H32L35 57H49L52 53H66"/></g></svg>',
    chk: '<svg class="sc chk" viewBox="0 0 84 72"><g class="doc"><path class="ink" d="M25 8H46L56 18V60A3 3 0 0 1 53 63H25A3 3 0 0 1 22 60V11A3 3 0 0 1 25 8Z" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M46 8V18H56"/><g style="fill:var(--ink);opacity:.75"><rect x="35" y="11" width="4" height="3"/><rect x="39" y="15" width="4" height="3"/><rect x="35" y="19" width="4" height="3"/><rect x="39" y="23" width="4" height="3"/><rect x="35" y="27" width="4" height="3"/></g><rect class="ink" x="35" y="32" width="8" height="9" rx="1.5" style="stroke-width:1.8"/></g><g class="lens"><circle class="acc" r="9" style="fill:rgba(82,199,184,.14)"/><path class="acc" d="M6.5 6.5L14 14" style="stroke-width:4"/><path d="M-5 -2.5A6 6 0 0 1 -1.5 -6" style="stroke:#fff;stroke-width:1.5;opacity:.7"/></g><g class="ok fb"><circle cx="56" cy="58" r="8" style="fill:var(--accent)"/><path d="M52.5 58L55 60.5L59.5 55.5" style="stroke:var(--on-accent);stroke-width:2.2"/></g></svg>',
    bak: '<svg class="sc bak" viewBox="0 0 84 72"><path class="ink" d="M10 18H20L25 23V42A2 2 0 0 1 23 44H10A2 2 0 0 1 8 42V20A2 2 0 0 1 10 18Z"/><path class="acc fb cp" d="M10 18H20L25 23V42A2 2 0 0 1 23 44H10A2 2 0 0 1 8 42V20A2 2 0 0 1 10 18Z" style="stroke-width:2.2;fill:rgba(82,199,184,.18)"/><path class="ink" d="M47 37V58A2 2 0 0 0 49 60H73A2 2 0 0 0 75 58V37" style="fill:#1b252b"/><path class="ink" d="M56 44H66"/><rect class="ink lid" x="44" y="29" width="34" height="8" rx="2" style="fill:#1b252b;transform-box:fill-box;transform-origin:100% 100%"/></svg>',
    ins: '<svg class="sc ins" viewBox="0 0 84 72"><path class="ink" d="M14 30H32L37 35H70V61A3 3 0 0 1 67 64H17A3 3 0 0 1 14 61Z" style="fill:rgba(255,255,255,.04)"/><path class="acc cd" d="M30 24H39L43 28V39H30Z" style="stroke-width:2;fill:#1d3a38"/><path class="acc cd c2" d="M45 24H54L58 28V39H45Z" style="stroke-width:2;fill:#1d3a38"/><path class="ink ff fb" d="M11 41H73L69 61A3 3 0 0 1 66 64H18A3 3 0 0 1 15 61Z" style="fill:#1b252b"/></svg>',
    // the launch option's steps borrow two of the pictures: Steam's window shrinks away like the game's, and the
    // file's copy goes into the box like the backup's
    steam: '<svg class="sc steam" viewBox="0 0 84 72"><g class="gw fb"><rect class="ink" x="14" y="12" width="56" height="44" rx="4" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M14 22H70"/><path class="acc" d="M60 15L65 20M65 15L60 20" style="stroke-width:2.2"/><path d="M36 30V48L50 39Z" style="fill:var(--ink);opacity:.6"/></g></svg>',
    opt: '<svg class="sc opt" viewBox="0 0 84 72"><path class="ink" d="M10 18H20L25 23V42A2 2 0 0 1 23 44H10A2 2 0 0 1 8 42V20A2 2 0 0 1 10 18Z"/><path class="acc fb cp" d="M10 18H20L25 23V42A2 2 0 0 1 23 44H10A2 2 0 0 1 8 42V20A2 2 0 0 1 10 18Z" style="stroke-width:2.2;fill:rgba(82,199,184,.18)"/><path class="ink" d="M47 37V58A2 2 0 0 0 49 60H73A2 2 0 0 0 75 58V37" style="fill:#1b252b"/><path class="ink" d="M56 44H66"/><rect class="ink lid" x="44" y="29" width="34" height="8" rx="2" style="fill:#1b252b;transform-box:fill-box;transform-origin:100% 100%"/></svg>',
  };

  const MARKS = {
    github: '<div class="ghmark" aria-hidden="true"><svg viewBox="0 0 24 24"><path fill="currentColor" d="M10.226 17.284c-2.965-.36-5.054-2.493-5.054-5.256 0-1.123.404-2.336 1.078-3.144-.292-.741-.247-2.314.09-2.965.898-.112 2.111.36 2.83 1.01.853-.269 1.752-.404 2.853-.404 1.1 0 1.999.135 2.807.382.696-.629 1.932-1.1 2.83-.988.315.606.36 2.179.067 2.942.72.854 1.101 2 1.101 3.167 0 2.763-2.089 4.852-5.098 5.234.763.494 1.28 1.572 1.28 2.807v2.336c0 .674.561 1.056 1.235.786 4.066-1.55 7.255-5.615 7.255-10.646C23.5 6.188 18.334 1 11.978 1 5.62 1 .5 6.188.5 12.545c0 4.986 3.167 9.12 7.435 10.669.606.225 1.19-.18 1.19-.786V20.63a2.9 2.9 0 0 1-1.078.224c-1.483 0-2.359-.808-2.987-2.313-.247-.607-.517-.966-1.034-1.033-.27-.023-.359-.135-.359-.27 0-.27.45-.471.898-.471.652 0 1.213.404 1.797 1.235.45.651.921.943 1.483.943.561 0 .92-.202 1.437-.719.382-.381.674-.718.944-.943"/></svg><span class="ghx">&#10005;</span></div>',
    bang: '<div class="errmark" aria-hidden="true">!</div>',
  };
  const STAGE = '<i class="spk" style="left:14%;top:18%;--st:.15s"></i><i class="spk a" style="left:82%;top:12%;--st:.25s"></i><i class="spk" style="left:90%;top:62%;--st:.4s"></i><i class="spk a" style="left:10%;top:70%;--st:.35s"></i><i class="spk" style="left:50%;top:2%;--st:.5s"></i>'
    + '<div class="mlogo" id="mlogo"><img src="logo-notagames.png" alt=""><div class="mgear"></div><div class="msp"></div><div class="mshine"></div></div>';

  const build = (markup) => {
    const t = document.createElement('template');
    t.innerHTML = markup;
    return t.content.firstElementChild;
  };

  return {
    names: Object.keys(SVG),
    make(name) { return SVG[name] ? build(SVG[name]) : null; },
    fill(box, names) {
      box.textContent = '';
      for (const name of names) {
        const pic = this.make(name);
        if (pic) box.append(pic);
      }
      return box;
    },
    stage(host, names) {
      host.innerHTML = STAGE;
      const box = el('div', 'scbox');
      host.append(this.fill(box, names));
      return { logo: host.querySelector('.mlogo'), box };
    },
    mark(name) { return build(MARKS[name] || MARKS.bang); },
  };
})();
