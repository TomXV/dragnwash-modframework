'use strict';
/* Shared page parts (webui/): how boards come and go. Needs kit.js; the keyframes are in motion.css.

   Boards are .layer sections under the header; one shows at a time.
     showLayer(node)          shows a board (takes back a leave that was under way)
     leaveLayer(node, after)  fades a board out, `after` seconds from now; gone at once when motion is reduced */

function showLayer(node) {
  clearTimeout(node.hideTimer);
  node.classList.remove('leave');
  node.hidden = false;
}

function leaveLayer(node, after = 0) {
  if (node.hidden) return;
  clearTimeout(node.hideTimer);
  if (reduced()) {
    node.hidden = true;
    return;
  }
  if (!after) replay(node, 'leave');
  node.hideTimer = setTimeout(() => {
    node.hidden = true;
    node.classList.remove('leave');
  }, (after + .3) * 1000);
}
