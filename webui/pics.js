'use strict';
/* Shared page parts (webui/): the step pictures beside the working logo, and the failure marks. Needs kit.js and
   modicon.js; the look and the motion are in pics.css. Pictures are hand-made SVG (viewBox 84×72), kept here by
   name so any page can show any of them.

   Steps: wait, dl, chk, bak, ins (the first five), steam, opt, optx, find, scan, loader, set, unz, out, cfgx, fwx,
   lnx, bakx, rb, and the three drawn around a mod's own icon: modin (goes in), modup (is updated), modout (comes
   out). A mod is {name, guid, icon} as modicon.js takes it; without an icon it gets the Mods screen's letter tile.
   Failure marks: github (GitHub's mark with the × badge), bang (!), busy, limited, notfound, mismatch, rolled,
   partly, running, otherloader, lock, broken, steamstuck. They play under .fail.play.

     Pics.names               every step picture's name
     Pics.marks               every failure mark's name
     Pics.make(name, mod)     a new <svg class="sc NAME"> (null for an unknown name); mod for the mod pictures
     Pics.setMod(pic, mod)    puts another mod's icon into a mod picture, and starts its motion over
     Pics.fill(box, names)    puts the named pictures into a .scbox, all hidden; show one with .on
     Pics.stage(host, names)  builds the working logo (sparkles, logo, gear, sponge) and a .scbox with the named
                              pictures into an empty .mstage.wsc host; returns {logo, box}
     Pics.mark(name)          a failure mark (42 px) for the failed board's heading; an unknown name gives bang */

const Pics = (() => {
  const SVG = {
    wait: '<svg class="sc wait" viewBox="0 0 84 72"><g class="gw fb"><rect class="ink" x="14" y="12" width="56" height="44" rx="4" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M14 22H70"/><path class="acc" d="M60 15L65 20M65 15L60 20" style="stroke-width:2.2"/><path d="M36 30V48L50 39Z" style="fill:var(--ink);opacity:.6"/></g></svg>',
    dl: '<svg class="sc dl" viewBox="0 0 84 72"><path class="ink" d="M28 24H56A8 8 0 0 0 57 8.1A11 11 0 0 0 36 7.5A8.5 8.5 0 0 0 28 24Z"/><g class="ar"><path class="acc" d="M42 20V40M35 33L42 40L49 33"/></g><g class="tr"><path class="ink" d="M18 48V60A3 3 0 0 0 21 63H63A3 3 0 0 0 66 60V48"/><path class="ink" d="M18 53H32L35 57H49L52 53H66"/></g></svg>',
    chk: '<svg class="sc chk" viewBox="0 0 84 72"><g class="doc"><path class="ink" d="M25 8H46L56 18V60A3 3 0 0 1 53 63H25A3 3 0 0 1 22 60V11A3 3 0 0 1 25 8Z" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M46 8V18H56"/><g style="fill:var(--ink);opacity:.75"><rect x="35" y="11" width="4" height="3"/><rect x="39" y="15" width="4" height="3"/><rect x="35" y="19" width="4" height="3"/><rect x="39" y="23" width="4" height="3"/><rect x="35" y="27" width="4" height="3"/></g><rect class="ink" x="35" y="32" width="8" height="9" rx="1.5" style="stroke-width:1.8"/></g><g class="lens"><circle class="acc" r="9" style="fill:rgba(82,199,184,.14)"/><path class="acc" d="M6.5 6.5L14 14" style="stroke-width:4"/><path d="M-5 -2.5A6 6 0 0 1 -1.5 -6" style="stroke:#fff;stroke-width:1.5;opacity:.7"/></g><g class="ok fb"><circle cx="56" cy="58" r="8" style="fill:var(--accent)"/><path d="M52.5 58L55 60.5L59.5 55.5" style="stroke:var(--on-accent);stroke-width:2.2"/></g></svg>',
    bak: '<svg class="sc bak" viewBox="0 0 84 72"><path class="ink" d="M10 18H20L25 23V42A2 2 0 0 1 23 44H10A2 2 0 0 1 8 42V20A2 2 0 0 1 10 18Z"/><path class="acc fb cp" d="M10 18H20L25 23V42A2 2 0 0 1 23 44H10A2 2 0 0 1 8 42V20A2 2 0 0 1 10 18Z" style="stroke-width:2.2;fill:rgba(82,199,184,.18)"/><path class="ink" d="M47 37V58A2 2 0 0 0 49 60H73A2 2 0 0 0 75 58V37" style="fill:#1b252b"/><path class="ink" d="M56 44H66"/><rect class="ink lid" x="44" y="29" width="34" height="8" rx="2" style="fill:#1b252b;transform-box:fill-box;transform-origin:100% 100%"/></svg>',
    ins: '<svg class="sc ins" viewBox="0 0 84 72"><path class="ink" d="M14 30H32L37 35H70V61A3 3 0 0 1 67 64H17A3 3 0 0 1 14 61Z" style="fill:rgba(255,255,255,.04)"/><path class="acc cd" d="M30 24H39L43 28V39H30Z" style="stroke-width:2;fill:#1d3a38"/><path class="acc cd c2" d="M45 24H54L58 28V39H45Z" style="stroke-width:2;fill:#1d3a38"/><path class="ink ff fb" d="M11 41H73L69 61A3 3 0 0 1 66 64H18A3 3 0 0 1 15 61Z" style="fill:#1b252b"/></svg>',
    // the launch option and the Close-Steam sheet: the app is asked to quit, then shrinks away (.closing)
    steam: '<svg class="sc steam" viewBox="0 0 84 72"><g class="gw fb"><rect class="ink" x="12" y="10" width="60" height="46" rx="4" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M12 20H72M29 20V56"/><path class="ink" d="M17 28H24M17 34H24M17 40H21" style="stroke-width:2;opacity:.5"/><circle class="acc rip fb" cx="50.5" cy="38" r="10" style="stroke-width:2"/><g class="pwr fb"><path class="acc" d="M45.4 31.9A8 8 0 1 0 55.6 31.9"/><path class="acc" d="M50.5 27.5V36.5"/></g></g></svg>',
    // the launch option goes in: the launcher's chip drops into the field
    opt: '<svg class="sc opt" viewBox="0 0 84 72"><rect x="10" y="11" width="26" height="4" rx="2" style="fill:var(--ink);opacity:.35"/><rect class="ink" x="8" y="22" width="68" height="26" rx="5" style="fill:rgba(255,255,255,.04)"/><rect class="acc fl" x="8" y="22" width="68" height="26" rx="5" style="stroke-width:2.2"/><rect class="slot" x="14" y="29" width="26" height="12" rx="3"/><rect x="45" y="32" width="24" height="6" rx="3" style="fill:var(--ink);opacity:.55"/><g class="chip"><rect class="acc" x="14" y="29" width="26" height="12" rx="3" style="stroke-width:2;fill:#1d3a38"/><path class="acc" d="M22.5 37.5L27 33L31.5 37.5" style="stroke-width:2"/></g><rect x="10" y="56" width="40" height="4" rx="2" style="fill:var(--ink);opacity:.18"/></svg>',
    // the launch option comes out: the chip lifts out
    optx: '<svg class="sc optx" viewBox="0 0 84 72"><rect x="10" y="11" width="26" height="4" rx="2" style="fill:var(--ink);opacity:.35"/><rect class="ink" x="8" y="22" width="68" height="26" rx="5" style="fill:rgba(255,255,255,.04)"/><rect class="slot" x="14" y="29" width="26" height="12" rx="3"/><rect x="45" y="32" width="24" height="6" rx="3" style="fill:var(--ink);opacity:.55"/><g class="chip"><rect class="acc" x="14" y="29" width="26" height="12" rx="3" style="stroke-width:2;fill:#1d3a38"/><path class="acc" d="M22.5 37.5L27 33L31.5 37.5" style="stroke-width:2"/></g><rect x="10" y="56" width="40" height="4" rx="2" style="fill:var(--ink);opacity:.18"/></svg>',
    // looking for the game folder
    find: '<svg class="sc find" viewBox="0 0 84 72"><path class="ink" transform="translate(7 31)" d="M0 3A2 2 0 0 1 2 1H7.5L10 3.5H18A2 2 0 0 1 20 5.5V15A2 2 0 0 1 18 17H2A2 2 0 0 1 0 15Z" style="fill:rgba(255,255,255,.04)"/><path class="ink" transform="translate(32 31)" d="M0 3A2 2 0 0 1 2 1H7.5L10 3.5H18A2 2 0 0 1 20 5.5V15A2 2 0 0 1 18 17H2A2 2 0 0 1 0 15Z" style="fill:rgba(255,255,255,.04)"/><path class="ink" transform="translate(57 31)" d="M0 3A2 2 0 0 1 2 1H7.5L10 3.5H18A2 2 0 0 1 20 5.5V15A2 2 0 0 1 18 17H2A2 2 0 0 1 0 15Z" style="fill:rgba(255,255,255,.04)"/><path class="acc hit" transform="translate(57 31)" d="M0 3A2 2 0 0 1 2 1H7.5L10 3.5H18A2 2 0 0 1 20 5.5V15A2 2 0 0 1 18 17H2A2 2 0 0 1 0 15Z" style="stroke-width:2.2;fill:rgba(82,199,184,.18)"/><g class="lz"><circle class="acc" r="8" style="fill:rgba(82,199,184,.14)"/><path class="acc" d="M5.8 5.8L12 12" style="stroke-width:4"/><path d="M-4.4 -2.2A5.3 5.3 0 0 1 -1.3 -5.3" style="stroke:#fff;stroke-width:1.5;opacity:.7"/></g><g class="ok fb"><circle cx="75" cy="50" r="8" style="fill:var(--accent)"/><path d="M71.5 50L74 52.5L78.5 47.5" style="stroke:var(--on-accent);stroke-width:2.2"/></g></svg>',
    // checking what is installed
    scan: '<svg class="sc scan" viewBox="0 0 84 72"><rect class="ink" x="20" y="9" width="44" height="56" rx="4" style="fill:rgba(255,255,255,.04)"/><rect class="ink" x="33" y="5" width="18" height="8" rx="2" style="fill:#1b252b"/><rect class="band" x="23" y="19" width="38" height="10" rx="3" style="fill:rgba(82,199,184,.16)"/><g style="fill:var(--ink);opacity:.45"><rect x="38" y="22" width="18" height="4" rx="2"/><rect x="38" y="35" width="14" height="4" rx="2"/><rect x="38" y="48" width="17" height="4" rx="2"/></g><g style="stroke:rgba(232,239,247,.4);stroke-width:1.6"><circle cx="30" cy="24" r="4.5"/><circle cx="30" cy="37" r="4.5"/><circle cx="30" cy="50" r="4.5"/></g><g class="t1 fb"><circle cx="30" cy="24" r="5" style="fill:var(--accent)"/><path d="M27.6 24.1L29.3 25.8L32.5 22.4" style="stroke:var(--on-accent);stroke-width:1.8"/></g><g class="t2 fb"><circle cx="30" cy="37" r="5" style="fill:var(--accent)"/><path d="M27.6 37.1L29.3 38.8L32.5 35.4" style="stroke:var(--on-accent);stroke-width:1.8"/></g><g class="t3 fb"><circle cx="30" cy="50" r="5" style="fill:var(--accent)"/><path d="M27.6 50.1L29.3 51.8L32.5 48.4" style="stroke:var(--on-accent);stroke-width:1.8"/></g></svg>',
    // BepInEx goes in: a plug into the game
    loader: '<svg class="sc loader" viewBox="0 0 84 72"><rect class="ink" x="14" y="6" width="56" height="40" rx="4" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M14 16H70"/><path d="M37 22V38L50 30Z" style="fill:var(--ink);opacity:.35"/><path class="lit" d="M37 22V38L50 30Z" style="fill:var(--accent)"/><g class="plug"><path class="acc" d="M38.5 45V52M45.5 45V52" style="stroke-width:2.4"/><rect class="acc" x="33" y="52" width="18" height="9" rx="2.5" style="stroke-width:2;fill:#1d3a38"/><path class="acc" d="M42 61V67" style="stroke-width:2.4"/></g><rect class="ink" x="34" y="42" width="16" height="7" rx="2" style="fill:#1b252b;stroke-width:1.8"/><path class="acc spk2" d="M29 44L25.5 41.5M55 44L58.5 41.5M28.5 49.5H24.5M55.5 49.5H59.5" style="stroke-width:2"/></svg>',
    // a setting is chosen (Language)
    set: '<svg class="sc set" viewBox="0 0 84 72"><rect class="ink" x="14" y="8" width="56" height="15" rx="4" style="fill:rgba(255,255,255,.04)"/><rect x="20" y="13.5" width="24" height="4" rx="2" style="fill:var(--ink);opacity:.55"/><rect class="val" x="20" y="13.5" width="24" height="4" rx="2" style="fill:var(--accent)"/><path class="ink chev fb" d="M59 14L62.5 17.5L66 14" style="stroke-width:2"/><g class="dd"><rect class="ink" x="14" y="27" width="56" height="37" rx="4" style="fill:#1b252b"/><rect class="hl" x="18" y="40" width="48" height="10" rx="3" style="fill:rgba(82,199,184,.2)"/><g style="fill:var(--ink);opacity:.5"><rect x="22" y="31" width="26" height="4" rx="2"/><rect x="22" y="43" width="20" height="4" rx="2"/><rect x="22" y="55" width="23" height="4" rx="2"/></g><path class="acc pk fb" d="M55 45L57.5 47.5L62 42.5" style="stroke-width:2.2"/></g></svg>',
    // a zip the player picked is opened
    unz: '<svg class="sc unz" viewBox="0 0 84 72"><path class="ink" d="M15 8H36L46 18V60A3 3 0 0 1 43 63H15A3 3 0 0 1 12 60V11A3 3 0 0 1 15 8Z" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M36 8V18H46"/><g style="fill:var(--ink);opacity:.75"><rect x="25" y="11" width="4" height="3"/><rect x="29" y="15" width="4" height="3"/><rect x="25" y="19" width="4" height="3"/><rect x="29" y="23" width="4" height="3"/><rect x="25" y="27" width="4" height="3"/></g><rect class="ink tab" x="25" y="32" width="8" height="9" rx="1.5" style="stroke-width:1.8"/><path class="acc ua fb" d="M55 12H63L67 16V27H55Z" style="stroke-width:2;fill:#1d3a38"/><path class="acc ub fb" d="M60 35H68L72 39V50H60Z" style="stroke-width:2;fill:#1d3a38"/></svg>',
    // a mod's files leave the folder (your data stays)
    out: '<svg class="sc out" viewBox="0 0 84 72"><path class="ink" d="M14 30H32L37 35H70V61A3 3 0 0 1 67 64H17A3 3 0 0 1 14 61Z" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M21 26H30L34 30V39H21Z" style="stroke-width:2;fill:#1b252b"/><path class="acc up" d="M38 24H47L51 28V39H38Z" style="stroke-width:2;fill:#1d3a38"/><path class="acc up u2" d="M53 24H62L66 28V39H53Z" style="stroke-width:2;fill:#1d3a38"/><path class="ink ff fb" d="M11 41H73L69 61A3 3 0 0 1 66 64H18A3 3 0 0 1 15 61Z" style="fill:#1b252b"/></svg>',
    // the settings file goes
    cfgx: '<svg class="sc cfgx" viewBox="0 0 84 72"><g class="obj"><path class="ink" d="M27 9H46L55 18V58A3 3 0 0 1 52 61H27A3 3 0 0 1 24 58V12A3 3 0 0 1 27 9Z" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M46 9V18H55"/><rect x="29" y="15" width="12" height="3.5" rx="1.75" style="fill:var(--ink);opacity:.45"/><rect x="29" y="26.5" width="13" height="7" rx="3.5" style="stroke:var(--ink);stroke-width:1.8"/><circle class="kn k1" cx="38.5" cy="30" r="2.2"/><rect x="45" y="28.2" width="6" height="3.5" rx="1.75" style="fill:var(--ink);opacity:.45"/><rect x="29" y="39.5" width="13" height="7" rx="3.5" style="stroke:var(--ink);stroke-width:1.8"/><circle class="kn k2" cx="38.5" cy="43" r="2.2"/><rect x="45" y="41.2" width="6" height="3.5" rx="1.75" style="fill:var(--ink);opacity:.45"/><rect x="29" y="51" width="18" height="3.5" rx="1.75" style="fill:var(--ink);opacity:.3"/><g class="rm fb"><circle cx="55" cy="57" r="8" style="fill:var(--accent)"/><path d="M51.5 57H58.5" style="stroke:var(--on-accent);stroke-width:2.2"/></g></g></svg>',
    // ModFramework goes
    fwx: '<svg class="sc fwx" viewBox="0 0 84 72"><g class="obj"><g transform="translate(40 34)"><g class="gr"><path class="ink" d="M-2.4 -12.3L-2.4 -16.8L2.4 -16.8L2.4 -12.3L6.9 -10.4L10.2 -13.6L13.6 -10.2L10.4 -6.9L12.3 -2.4L16.8 -2.4L16.8 2.4L12.3 2.4L10.4 6.9L13.6 10.2L10.2 13.6L6.9 10.4L2.4 12.3L2.4 16.8L-2.4 16.8L-2.4 12.3L-6.9 10.4L-10.2 13.6L-13.6 10.2L-10.4 6.9L-12.3 2.4L-16.8 2.4L-16.8 -2.4L-12.3 -2.4L-10.4 -6.9L-13.6 -10.2L-10.2 -13.6L-6.9 -10.4Z" style="fill:rgba(255,255,255,.04)"/><circle class="acc" r="5" style="stroke-width:2.6;fill:#1d3a38"/></g></g><g class="rm fb"><circle cx="57" cy="52" r="8" style="fill:var(--accent)"/><path d="M53.5 52H60.5" style="stroke:var(--on-accent);stroke-width:2.2"/></g></g></svg>',
    // the update launcher goes
    lnx: '<svg class="sc lnx" viewBox="0 0 84 72"><g class="obj"><rect class="ink" x="18" y="10" width="48" height="40" rx="4" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M18 19H66"/><g transform="translate(42 35)"><g class="rf"><path class="acc" d="M8 0A8 8 0 1 1 4 -6.93" style="stroke-width:2.6"/><path class="acc" d="M2.46 -11.16L4 -6.93L-0.43 -6.15" style="stroke-width:2.6"/></g></g><g class="rm fb"><circle cx="64" cy="49" r="8" style="fill:var(--accent)"/><path d="M60.5 49H67.5" style="stroke:var(--on-accent);stroke-width:2.2"/></g></g></svg>',
    // the installer's backup goes
    bakx: '<svg class="sc bakx" viewBox="0 0 84 72"><g class="obj"><path class="acc bc" d="M36 34H44L48 38V48H36Z" style="stroke-width:2;fill:#1d3a38"/><path class="ink" d="M26 37V58A2 2 0 0 0 28 60H56A2 2 0 0 0 58 58V37" style="fill:#1b252b"/><path class="ink" d="M37 44H47"/><rect class="ink lid" x="23" y="29" width="38" height="8" rx="2" style="fill:#1b252b;transform-box:fill-box;transform-origin:100% 100%"/><g class="rm fb"><circle cx="58" cy="58" r="8" style="fill:var(--accent)"/><path d="M54.5 58H61.5" style="stroke:var(--on-accent);stroke-width:2.2"/></g></g></svg>',
    // putting back from the backup (the backup played backwards)
    rb: '<svg class="sc rb" viewBox="0 0 84 72"><path class="ink" d="M10 18H20L25 23V42A2 2 0 0 1 23 44H10A2 2 0 0 1 8 42V20A2 2 0 0 1 10 18Z" style="stroke-dasharray:3.5 3.5;opacity:.5;stroke-width:1.8"/><path class="ink" d="M62 21C58 9 40 5 30 13M34.5 13.1L30 13L30.9 8.6" style="opacity:.4;stroke-width:2"/><path class="acc fb cb" d="M10 18H20L25 23V42A2 2 0 0 1 23 44H10A2 2 0 0 1 8 42V20A2 2 0 0 1 10 18Z" style="stroke-width:2.2;fill:rgba(82,199,184,.18)"/><path class="ink" d="M47 37V58A2 2 0 0 0 49 60H73A2 2 0 0 0 75 58V37" style="fill:#1b252b"/><path class="ink" d="M56 44H66"/><rect class="ink lid" x="44" y="29" width="34" height="8" rx="2" style="fill:#1b252b;transform-box:fill-box;transform-origin:100% 100%"/></svg>',
  };

  const MARKS = {
    github: '<div class="ghmark" aria-hidden="true"><svg viewBox="0 0 24 24"><path fill="currentColor" d="M10.226 17.284c-2.965-.36-5.054-2.493-5.054-5.256 0-1.123.404-2.336 1.078-3.144-.292-.741-.247-2.314.09-2.965.898-.112 2.111.36 2.83 1.01.853-.269 1.752-.404 2.853-.404 1.1 0 1.999.135 2.807.382.696-.629 1.932-1.1 2.83-.988.315.606.36 2.179.067 2.942.72.854 1.101 2 1.101 3.167 0 2.763-2.089 4.852-5.098 5.234.763.494 1.28 1.572 1.28 2.807v2.336c0 .674.561 1.056 1.235.786 4.066-1.55 7.255-5.615 7.255-10.646C23.5 6.188 18.334 1 11.978 1 5.62 1 .5 6.188.5 12.545c0 4.986 3.167 9.12 7.435 10.669.606.225 1.19-.18 1.19-.786V20.63a2.9 2.9 0 0 1-1.078.224c-1.483 0-2.359-.808-2.987-2.313-.247-.607-.517-.966-1.034-1.033-.27-.023-.359-.135-.359-.27 0-.27.45-.471.898-.471.652 0 1.213.404 1.797 1.235.45.651.921.943 1.483.943.561 0 .92-.202 1.437-.719.382-.381.674-.718.944-.943"/></svg><span class="ghx">&#10005;</span></div>',
    bang: '<div class="errmark" aria-hidden="true">!</div>',
  };
  // the other failure marks (viewBox 42×42), each with its badge: x = it failed, ! = something stands in the way,
  // w = only partly (yellow)
  const FM = {
    busy: ['x', '<path class="ink" d="M15 19H32.4A5 5 0 0 0 33 9.1A6.8 6.8 0 0 0 20 8.8A5.3 5.3 0 0 0 15 19Z" style="fill:rgba(255,255,255,.04)"/><g class="try"><path class="acc" d="M24 17V31" style="stroke-dasharray:3.2 3.6"/><path class="acc" d="M19.5 26.5L24 31L28.5 26.5"/></g>'],
    limited: ['!', '<path class="ink" d="M12 6H30M12 36H30"/><path class="ink" d="M14.5 6C14.5 14 20 17 20 21C20 25 14.5 28 14.5 36M27.5 6C27.5 14 22 17 22 21C22 25 27.5 28 27.5 36"/><path class="st1" d="M16.6 10H25.4C24.8 14.5 21.8 16.5 21 19C20.2 16.5 17.2 14.5 16.6 10Z" style="fill:var(--accent)"/><path class="stream" d="M21 20V32" style="stroke:var(--accent);stroke-width:1.4;stroke-dasharray:2 2"/><path class="sb1" d="M16 34H26C25.4 30.5 23 29 21 29C19 29 16.6 30.5 16 34Z" style="fill:var(--accent)"/>'],
    notfound: ['x', '<path class="ink" d="M8 21V35A2 2 0 0 0 10 37H32A2 2 0 0 0 34 35V21" style="fill:#1b252b"/><path class="ink" d="M8 21H34M8 21L4 15.5M34 21L38 15.5"/><g class="try fb"><path class="acc" d="M17.2 6A3.8 3.8 0 1 1 21 9.8V12.4"/><circle cx="21" cy="16.4" r="1.5" style="fill:var(--accent)"/></g>'],
    mismatch: ['x', '<path class="ink" d="M12 5H24L31 12V35A2 2 0 0 1 29 37H12A2 2 0 0 1 10 35V7A2 2 0 0 1 12 5Z" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M24 5V12H31"/><g style="fill:var(--ink);opacity:.7"><rect x="17" y="8" width="3" height="2.5"/><rect x="20" y="11" width="3" height="2.5"/><rect x="17" y="14" width="3" height="2.5"/><rect x="20" y="17" width="3" height="2.5"/></g><rect class="ink" x="17" y="21.5" width="6" height="6.5" rx="1.2" style="stroke-width:1.5"/><g class="lz"><circle class="acc" r="6" style="fill:rgba(82,199,184,.14);stroke-width:2.2"/><path class="acc" d="M4.3 4.3L8.5 8.5" style="stroke-width:3.2"/></g>'],
    rolled: ['x', '<path class="ink" d="M6 13A2 2 0 0 1 8 11H15L18 14H34A2 2 0 0 1 36 16V33A2 2 0 0 1 34 35H8A2 2 0 0 1 6 33Z" style="fill:rgba(255,255,255,.04)"/><g transform="translate(21 24.5) scale(-.72 .72)"><g class="rw"><path class="acc" vector-effect="non-scaling-stroke" d="M8 0A8 8 0 1 1 4 -6.93"/><path class="acc" vector-effect="non-scaling-stroke" d="M2.46 -11.16L4 -6.93L-0.43 -6.15"/></g></g>'],
    partly: ['w', '<path class="ink" d="M26 3H32L34.5 5.5V11H26Z" style="stroke-dasharray:2.5 2.5;stroke-width:1.6;opacity:.75"/><path class="ink" d="M6 13A2 2 0 0 1 8 11H15L18 14H34A2 2 0 0 1 36 16V33A2 2 0 0 1 34 35H8A2 2 0 0 1 6 33Z" style="fill:rgba(255,255,255,.04)"/><g transform="translate(21 24.5) scale(-.72 .72)"><g class="rw"><path class="acc" vector-effect="non-scaling-stroke" d="M8 0A8 8 0 1 1 4 -6.93"/><path class="acc" vector-effect="non-scaling-stroke" d="M2.46 -11.16L4 -6.93L-0.43 -6.15"/></g></g>'],
    running: ['!', '<rect class="ink" x="5" y="8" width="32" height="26" rx="3" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M5 15H37"/><path class="try fb" d="M17.5 19V30L27 24.5Z" style="fill:var(--accent)"/>'],
    otherloader: ['!', '<rect class="ink" x="2" y="3" width="28" height="19" rx="3" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M2 9H30M13.5 22V26M18.5 22V26"/><rect class="ink" x="9.5" y="26" width="13" height="7" rx="2" style="fill:#1b252b"/><path class="ink" d="M16 33V39"/><g class="try"><path class="acc" d="M28 27.5V24.5M32 27.5V24.5" style="stroke-width:2"/><rect class="acc" x="25.5" y="27.5" width="9" height="7" rx="2" style="stroke-width:2;fill:#1d3a38"/><path class="acc" d="M30 34.5V39.5" style="stroke-width:2"/></g>'],
    lock: ['x', '<path class="ink" d="M10 5H22L29 12V35A2 2 0 0 1 27 37H10A2 2 0 0 1 8 35V7A2 2 0 0 1 10 5Z" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M22 5V12H29"/><path class="ink" d="M12 13H18M12 18H21" style="opacity:.5"/><g class="try"><path class="acc" d="M20 22V18.5A4 4 0 0 1 28 18.5V22" style="stroke-width:2.2"/><rect class="acc" x="17" y="22" width="14" height="11" rx="2" style="stroke-width:2;fill:#1d3a38"/><circle cx="24" cy="27.3" r="1.4" style="fill:var(--accent)"/></g>'],
    broken: ['x', '<path class="ink" d="M12 5H24L31 12V22L27.5 25L24 22L20.5 25L17 22L13.5 25L10 22V7A2 2 0 0 1 12 5Z" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M24 5V12H31"/><g style="fill:var(--ink);opacity:.7"><rect x="17" y="8" width="3" height="2.5"/><rect x="20" y="11" width="3" height="2.5"/><rect x="17" y="14" width="3" height="2.5"/></g><g class="try pc fb"><path class="ink" d="M10 28.5L13.5 31.5L17 28.5L20.5 31.5L24 28.5L27.5 31.5L31 28.5V35A2 2 0 0 1 29 37H12A2 2 0 0 1 10 35Z" style="fill:rgba(255,255,255,.04)"/></g>'],
    steamstuck: ['!', '<rect class="ink" x="4" y="6" width="34" height="28" rx="3" style="fill:rgba(255,255,255,.04)"/><path class="ink" d="M4 13H38M13 13V34"/><path class="ink" d="M7 18H10M7 22.5H10" style="opacity:.5;stroke-width:1.6"/><g class="try fb"><path class="acc" d="M22.5 20.3A5.5 5.5 0 1 0 29.5 20.3" style="stroke-width:2.2"/><path class="acc" d="M26 17V24" style="stroke-width:2.2"/></g>'],
  };

  // the mod pictures: <g class="ico"> places are filled with the mod's icon (x, y, size in the viewBox)
  const FOLDER = '<path class="ink" d="M14 30H32L37 35H70V61A3 3 0 0 1 67 64H17A3 3 0 0 1 14 61Z" style="fill:rgba(255,255,255,.04)"/>';
  const FLAP = '<path class="ink ff fb" d="M11 41H73L69 61A3 3 0 0 1 66 64H18A3 3 0 0 1 15 61Z" style="fill:#1b252b"/>';
  const STAR = 'M0 -4.5L1.1 -1.1L4.5 0L1.1 1.1L0 4.5L-1.1 1.1L-4.5 0L-1.1 -1.1Z';
  const ICO = (x, y, size, extra = '') => `<g class="ico${extra}" data-x="${x}" data-y="${y}" data-s="${size}"></g>`;
  const MODPICS = {
    // the icon drops into the game folder
    modin: `<svg class="sc modin" viewBox="0 0 84 72">${FOLDER}<g class="mi">${ICO(28, 19, 28)}</g>${FLAP}<g transform="translate(22 19)"><path class="tw fb" d="${STAR}" style="fill:var(--accent)"/></g><g transform="translate(63 15)"><path class="tw t2 fb" d="${STAR}" style="fill:#fff"/></g></svg>`,
    // the icon gets the "−" badge and lifts out
    modout: `<svg class="sc modout" viewBox="0 0 84 72">${FOLDER}<g class="mi">${ICO(28, 19, 28)}<g class="rm fb"><circle cx="56" cy="20" r="7" style="fill:var(--accent);stroke:#182025;stroke-width:1.6"/><path d="M53 20H59" style="stroke:var(--on-accent);stroke-width:2.2"/></g></g>${FLAP}</svg>`,
    // the old icon turns over into the new one inside the update arrows
    modup: `<svg class="sc modup" viewBox="0 0 84 72"><g transform="translate(42 34)"><g class="ar2"><path class="acc" d="M-19.7 -7.2A21 21 0 0 1 13.5 -16.1M12.7 -20.5L13.5 -16.1L9 -16.1" style="stroke-width:2.6"/><path class="acc" d="M19.7 7.2A21 21 0 0 1 -13.5 16.1M-12.7 20.5L-13.5 16.1L-9 16.1" style="stroke-width:2.6"/></g></g><g class="flip">${ICO(27, 19, 30, ' old')}${ICO(27, 19, 30, ' nw')}</g><g class="upb fb"><circle cx="63" cy="53" r="8" style="fill:var(--accent)"/><path d="M63 57V49.5M59.5 53L63 49.5L66.5 53" style="stroke:var(--on-accent);stroke-width:2.2"/></g></svg>`,
  };

  const STAGE = '<i class="spk" style="left:14%;top:18%;--st:.15s"></i><i class="spk a" style="left:82%;top:12%;--st:.25s"></i><i class="spk" style="left:90%;top:62%;--st:.4s"></i><i class="spk a" style="left:10%;top:70%;--st:.35s"></i><i class="spk" style="left:50%;top:2%;--st:.5s"></i>'
    + '<div class="mlogo" id="mlogo"><img src="logo-notagames.png" alt=""><div class="mgear"></div><div class="msp"></div><div class="mshine"></div></div>';

  const build = (markup) => {
    const t = document.createElement('template');
    t.innerHTML = markup;
    return t.content.firstElementChild;
  };

  function setMod(pic, mod) {
    for (const place of pic.querySelectorAll('g.ico')) {
      place.textContent = '';
      place.append(ModIcon.svg(mod, +place.dataset.x, +place.dataset.y, +place.dataset.s));
    }
    if (pic.classList.contains('on')) {
      pic.classList.remove('on');
      pic.getBoundingClientRect();
      pic.classList.add('on');
    }
    return pic;
  }

  return {
    names: [...Object.keys(SVG), ...Object.keys(MODPICS)],
    marks: [...Object.keys(MARKS), ...Object.keys(FM)],
    make(name, mod) {
      if (MODPICS[name]) return setMod(build(MODPICS[name]), mod || {});
      return SVG[name] ? build(SVG[name]) : null;
    },
    setMod,
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
    mark(name) {
      if (FM[name]) {
        const [badge, svg] = FM[name];
        return build(`<div class="fmk ${name}" aria-hidden="true"><svg viewBox="0 0 42 42">${svg}</svg>`
          + `<span class="fx${badge === 'w' ? ' w' : ''}">${badge === 'x' ? '&#10005;' : '!'}</span></div>`);
      }
      return build(MARKS[name] || MARKS.bang);
    },
  };
})();
