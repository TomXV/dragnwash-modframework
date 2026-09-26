'use strict';
/* Shared page parts (webui/): the logo intro. Needs kit.js; the look and the motion are in intro.css.
   One image (logo-notagames.png, untouched), shown piece by piece through clip-paths and masks, with the gear
   (gear.png) turning behind the sponge, water drifting below, a status line and a progress bar.

     LogoIntro.T                      the timeline in seconds, shared by every page (see below)
     LogoIntro.build(host)            fills an empty <section class="intro"> with the markup; returns {big, ist}
     LogoIntro.moments(host, found)   sets the bar's moments; found = the variant that hands the logo over
     LogoIntro.typed(text, t0, len, cls)  a status line that types itself out from t0 (each character its own --t)
     LogoIntro.withDots(line, t0)     adds three dots that loop from t0
     LogoIntro.leaves(line, at)       the line fades out at `at`
     LogoIntro.aim(big, target)       measures the hand-off: the big logo moves to the header logo's box

   A page runs it by adding .play to the host (replay(host, 'play')), .skipped on a click, and .handoff to send
   the logo up into the header (after aim). <html>.mf-write picks handwriting, <html>.pb-edge the edge bar. */

const LogoIntro = (() => {
  /* Seconds from the start. The logo is done by about 2 s (MOD FRAMEWORK from 1.3 s to 2.0 s) and its light runs
     over it until 2.8 s; meanwhile the "checking" line types itself out and its dots go round once, and the result
     replaces it at 3.2 s. With nothing to do, the last line follows at 3.7 s while the bar fills to the end, and
     the page is done at 4.25 s. With something to show, the result stays up for a second and the logo hands
     over to the header at 4.2 s. */
  const T = { chk: 2, chkLen: .36, dots: 2.4, round: .8, res: 3.2, resLen: .3, go: 3.7, goLen: .35, fill: .45, end: .55, ho: 4.2 };

  const MARKUP = `<div class="waves" aria-hidden="true"><i class="w1"></i><i class="w2"></i><i class="w3"></i></div>
<div class="big" id="big" role="img" aria-label="Drag'n Wash ModFramework">
<img class="full" src="logo-notagames.png" alt="">
<div class="pc gD l0"></div><div class="pc gd l1"></div><div class="pc gd l2"></div><div class="pc gd l3"></div><div class="pc gd l4"></div><div class="pc gd l5"></div><div class="gearspin"><div class="gshine"><div class="gband"></div></div></div><div class="pc gs l6"></div><div class="pc gw l8"></div><div class="pc gw l9"></div><div class="pc gw l10"></div><div class="pc gw l11"></div><div class="pc gm l12"></div><div class="pc gm l13"></div><div class="pc gm l14"></div><div class="pc gm l15"></div><div class="pc gm l16"></div><div class="pc gm l17"></div><div class="pc gm l18"></div><div class="pc gm l19"></div><div class="pc gm l20"></div><div class="pc gm l21"></div><div class="pc gm l22"></div><div class="pc gm l23"></div>
<div class="scan"></div><svg class="mfw" viewBox="0 0 1200 624" preserveAspectRatio="none" aria-hidden="true"><defs><mask id="mfw0" maskUnits="userSpaceOnUse" x="0" y="0" width="1200" height="624"><g fill="none" stroke="#fff" stroke-linecap="round" stroke-linejoin="round"><path d="M221 575 L219 578 L219 597 L221 615" stroke-width="21" pathLength="1" style="--dl:1.300s;--du:0.014s"/><path d="M220 576 L223 574 L226 574 L232 578 L239 598 L244 601 L250 596 L257 576 L265 572 L267 573 L269 577 L269 595 L268 597 L269 617" stroke-width="21" pathLength="1" style="--dl:1.318s;--du:0.042s"/><path d="M310 568 L301 568 L295 571 L291 576 L288 586 L288 597 L290 606 L294 612 L301 616 L310 616 L315 614 L320 609 L323 603 L325 592 L323 580 L320 575 L315 570 L308 568" stroke-width="19.4" pathLength="1" style="--dl:1.367s;--du:0.046s"/><path d="M346 573 L344 578 L344 603 L346 610 L348 612" stroke-width="20.6" pathLength="1" style="--dl:1.422s;--du:0.013s"/><path d="M348 571 L354 568 L360 568 L368 570 L374 576 L378 588 L377 603 L370 613 L363 616 L354 616 L349 613" stroke-width="20.6" pathLength="1" style="--dl:1.439s;--du:0.031s"/><path d="M418 572 L417 573 L417 590 L418 592 L418 612 L417 615" stroke-width="23.9" pathLength="1" style="--dl:1.478s;--du:0.015s"/><path d="M419 571 L423 568 L444 568 L446 569" stroke-width="23.9" pathLength="1" style="--dl:1.496s;--du:0.009s"/><path d="M418 592 L420 593 L437 593" stroke-width="23.9" pathLength="1" style="--dl:1.510s;--du:0.006s"/><path d="M464 570 L461 579 L461 588 L463 595 L462 597 L463 614" stroke-width="22" pathLength="1" style="--dl:1.524s;--du:0.015s"/><path d="M465 569 L471 566 L479 566 L487 570 L490 577 L490 584 L488 589 L480 596 L466 596 L465 595 L462 596 L465 595 L466 596 L480 596 L482 605 L492 615" stroke-width="22" pathLength="1" style="--dl:1.543s;--du:0.038s"/><path d="M529 571 L523 574 L520 580 L517 590 L516 598 L517 603 L514 606 L510 616" stroke-width="23.9" pathLength="1" style="--dl:1.589s;--du:0.017s"/><path d="M529 571 L535 572 L537 574 L542 590 L543 601 L550 615" stroke-width="23.9" pathLength="1" style="--dl:1.611s;--du:0.017s"/><path d="M517 603 L520 604 L542 604 L544 603 L545 604" stroke-width="23.9" pathLength="1" style="--dl:1.632s;--du:0.010s"/><path d="M568 574 L567 576 L568 615" stroke-width="21.5" pathLength="1" style="--dl:1.650s;--du:0.014s"/><path d="M567 576 L571 572 L574 572 L579 575 L587 599 L593 601 L598 596 L604 576 L610 572 L615 573 L616 575 L616 602 L615 604 L616 615" stroke-width="21.5" pathLength="1" style="--dl:1.667s;--du:0.043s"/><path d="M639 570 L637 575 L637 587 L638 589 L637 608 L639 612" stroke-width="23" pathLength="1" style="--dl:1.718s;--du:0.014s"/><path d="M639 570 L648 567 L657 567 L664 569" stroke-width="23" pathLength="1" style="--dl:1.737s;--du:0.009s"/><path d="M638 590 L640 591 L653 591 L655 590 L656 591" stroke-width="23" pathLength="1" style="--dl:1.749s;--du:0.006s"/><path d="M640 613 L643 615 L648 616 L654 616 L661 614 L666 615" stroke-width="23" pathLength="1" style="--dl:1.759s;--du:0.009s"/><path d="M681 569 L684 591 L687 603 L690 609 L694 610 L697 608 L700 602 L701 595 L705 591 L707 587 L712 594 L715 605 L722 611 L726 606 L729 596 L729 589 L733 571 L732 568" stroke-width="22.9" pathLength="1" style="--dl:1.776s;--du:0.049s"/><path d="M771 569 L769 568 L763 569 L757 572 L753 578 L750 587 L750 598 L753 608 L758 613 L764 616 L776 615 L782 609 L786 596 L785 584 L780 573 L773 569 L767 569" stroke-width="19.6" pathLength="1" style="--dl:1.834s;--du:0.046s"/><path d="M808 570 L806 572 L805 577 L805 589 L807 595 L806 597 L806 615 L807 616" stroke-width="22" pathLength="1" style="--dl:1.888s;--du:0.016s"/><path d="M809 569 L814 566 L823 566 L829 569 L833 574 L834 582 L832 589 L828 593 L824 596 L812 596 L810 595 L806 596 L809 595 L824 596 L825 603 L827 607 L836 615" stroke-width="22" pathLength="1" style="--dl:1.908s;--du:0.038s"/><path d="M857 570 L856 576 L857 615 L856 617" stroke-width="22.2" pathLength="1" style="--dl:1.954s;--du:0.016s"/><path d="M887 569 L868 592 L859 592 L868 592 L871 595 L871 597 L874 601 L889 615" stroke-width="22.2" pathLength="1" style="--dl:1.974s;--du:0.026s"/></g></mask></defs><image href="logo-notagames.png" width="1200" height="624" preserveAspectRatio="none" mask="url(#mfw0)"/></svg><div class="shine"></div>
</div>
<div class="ipb" aria-hidden="true"><i></i></div>
<div class="ist" id="ist" role="status"></div>`;

  // a line that types itself out: every character gets its own start time (--t); CSS does the motion
  function typed(text, t0, len, cls) {
    const line = el('span', 'il' + (cls ? ' ' + cls : ''));
    line.setAttribute('aria-hidden', 'true');
    const chars = Array.from(text);
    const per = Math.min(.04, len / chars.length);
    chars.forEach((c, i) => {
      const ch = el('span', 'ch', c);
      ch.style.setProperty('--t', sec(t0 + i * per));
      line.append(ch);
    });
    return line;
  }

  // three dots that loop from t0: (none) → . → .. → ... and round again
  function withDots(line, t0) {
    for (let d = 1; d <= 3; d++) {
      const dot = el('span', 'dt d' + d, '.');
      dot.style.setProperty('--t', sec(t0));
      dot.style.setProperty('--p', sec(T.round));
      line.append(dot);
    }
    return line;
  }

  function leaves(line, at) {
    line.classList.add('out');
    line.style.setProperty('--t', sec(at));
    return line;
  }

  return {
    T,
    typed,
    withDots,
    leaves,
    build(host) {
      host.innerHTML = MARKUP;
      return { big: host.querySelector('.big'), ist: host.querySelector('.ist') };
    },
    moments(host, found) {
      host.classList.toggle('found', !!found);
      const at = { pchk: T.chk, dchk: T.res - T.chk, pres: T.res, pgo: T.go, pho: T.ho };
      for (const [name, s] of Object.entries(at)) host.style.setProperty('--' + name, sec(s));
    },
    // where the big logo has to go to land on the header logo
    aim(big, target) {
      const from = boxOf(big);
      const to = boxOf(target);
      big.style.setProperty('--hox', to.x - from.x + 'px');
      big.style.setProperty('--hoy', to.y - from.y + 'px');
      big.style.setProperty('--hok', to.w / from.w);
    },
  };
})();
