'use strict';
// =====================================================================
// Progression tab — a Call of Duty style unlock track: five level cards per
// page along the bottom, everything that unlocks at the picked level on top,
// grouped by category. Read only (click an item to open it in Level Limits).
// =====================================================================

const PER_PAGE = 5;
const PROG_PAGES = Math.ceil(MAX_LEVEL / PER_PAGE);
/** Rank look per level band (badge metal + name). */
const TIERS = [
  [1, 'Recruit', '#9aa3ad', '#dfe6ee'], [10, 'Private', '#8c6b3f', '#e8c27a'], [20, 'Corporal', '#9aa3ad', '#f4f7fa'],
  [30, 'Sergeant', '#b8901c', '#ffe27a'], [40, 'Lieutenant', '#3f7fb8', '#9fd4ff'], [50, 'Captain', '#7b4fc9', '#d3b8ff'],
  [60, 'Major', '#b8323f', '#ff9aa5'], [70, 'Colonel', '#c9a227', '#fff1a8'],
];
const tierOf = lvl => [...TIERS].reverse().find(t => lvl >= t[0]);

const P = { level: 1, page: 0, open: new Set(), anim: '' };

/** level -> items unlocking there (level 1 also gets every item without a limit, unless switched off). */
function progByLevel() {
  const by = new Map();
  const free = S.ui.progFree !== false;
  for (const it of S.items.values()) {
    if (it.x && !S.showHidden) continue;
    const l = levelOf(it.i) ?? (free ? 1 : undefined);
    if (l === undefined) continue;
    (by.get(l) || by.set(l, []).get(l)).push(it);
  }
  return by;
}

function badge(lvl, size = 'md', dim = false) {
  const [, , a, b] = tierOf(lvl);
  return `<div class="pbadge ${size} ${dim ? 'dim' : ''}" style="--ta:${a};--tb:${b}"><div class="pb-in"><b>${lvl}</b></div></div>`;
}

const tileIcon = it => `<img src="https://files.local/icon/${it.i}-icon.webp" alt="" loading="lazy" onerror="this.remove()">`;

function progPage() {
  S.shown = [];
  const by = progByLevel();
  P.page = clamp(P.page, 0, PROG_PAGES - 1);
  const first = P.page * PER_PAGE + 1;
  const levels = Array.from({ length: PER_PAGE }, (_, i) => first + i).filter(l => l <= MAX_LEVEL);
  const anim = P.anim; P.anim = '';

  const cards = levels.map((l, i) => {
    const list = by.get(l) || [];
    const preview = list.slice().sort((a, b) => (b.f || b.h || 0) - (a.f || a.h || 0)).slice(0, 3);
    return `<button class="pcard ${l === P.level ? 'on' : ''} ${list.length ? '' : 'empty'} ${anim}" style="--d:${i * 45}ms" data-act="progLevel" data-arg="${l}">
      <div class="pc-title">LEVEL ${l}</div>
      ${badge(l, 'md', !list.length)}
      <div class="pc-tier">${esc(tierOf(l)[1].toUpperCase())}</div>
      <div class="pc-items">${preview.map(it => `<div class="pc-ico" title="${esc(it.n)}">${tileIcon(it)}</div>`).join('')}${list.length > 3 ? `<div class="pc-more">+${fmt(list.length - 3)}</div>` : ''}${list.length ? '' : '<div class="pc-none">No Unlocks</div>'}</div>
      <div class="pc-count">${list.length ? `${fmt(list.length)} UNLOCK${list.length === 1 ? '' : 'S'}` : '&nbsp;'}</div>
    </button>`;
  }).join('');

  const pct = trackPct(levels.length);
  const dots = Array.from({ length: PROG_PAGES }, (_, i) => `<button class="pdot ${i === P.page ? 'on' : ''}" data-act="progPage" data-arg="${i}" title="Levels ${i * PER_PAGE + 1}–${Math.min(MAX_LEVEL, i * PER_PAGE + PER_PAGE)}"></button>`).join('');

  return `<div class="prog">
    <div class="prog-top" id="progTop">${progTop(by)}</div>
    <div class="prog-bottom">
      <div class="pb-head">
        <span class="pb-range">LEVELS ${first}–${levels[levels.length - 1]}</span>
        <label class="switch small" title="Items without a limit can be used from level 1"><input type="checkbox" data-prog-free ${S.ui.progFree !== false ? 'checked' : ''}><span class="track"></span><span>Count Items Without a Limit as Level 1</span></label>
      </div>
      <div class="pb-row">
        <button class="parrow" data-act="progPage" data-arg="${P.page - 1}" ${P.page ? '' : 'disabled'} title="Previous levels (Page Up / Q)">‹</button>
        <div class="pcards">${cards}</div>
        <button class="parrow" data-act="progPage" data-arg="${P.page + 1}" ${P.page < PROG_PAGES - 1 ? '' : 'disabled'} title="Next levels (Page Down / E)">›</button>
      </div>
      <div class="ptrack"><i style="width:${pct}%"></i>${levels.map((l, i) => `<span class="ptick ${l <= P.level ? 'past' : ''}" style="left:${((i + .5) / levels.length) * 100}%"></span>`).join('')}</div>
      <div class="pdots">${dots}<span class="pg-label">PAGE ${P.page + 1} / ${PROG_PAGES}</span></div>
    </div>
  </div>`;
}

function progTop(by) {
  const list = by.get(P.level) || [];
  const groups = new Map();
  for (const it of list) { const g = groupOf(it); (groups.get(g) || groups.set(g, []).get(g)).push(it); }
  const order = GROUPS.map(g => g[0]).filter(k => groups.has(k));
  const [, tier] = tierOf(P.level);
  const free = S.ui.progFree !== false;
  const limitedHere = list.filter(it => levelOf(it.i) === P.level).length;
  let n = 0;
  const sections = order.map(k => {
    const g = GROUP[k], items = groups.get(k).sort((a, b) => (a.n || '').localeCompare(b.n || ''));
    const open = P.open.has(k) || items.length <= 36;
    const shown = open ? items : items.slice(0, 36);
    return `<section class="pcat" id="pcat-${k}" style="--c:${g.color}">
      <h3><i></i>${esc(g.name.toUpperCase())}<em>${fmt(items.length)}</em></h3>
      <div class="ptiles">${shown.map(it => {
        const d = Math.min(n++, 40) * 12;
        const own = levelOf(it.i) === P.level;
        return `<button class="ptile ${own ? '' : 'free'}" style="--d:${d}ms" data-act="progItem" data-arg="${it.i}" title="${esc(it.n)}${own ? '' : ' · no limit'}">
          <div class="pt-pic">${tileIcon(it)}<span>${esc((it.s || it.n || '?').slice(0, 10))}</span></div>
          <div class="pt-name">${esc(it.s || it.n)}</div></button>`;
      }).join('')}</div>
      ${items.length > shown.length ? `<button class="pmore" data-act="progOpen" data-arg="${k}">Show All ${fmt(items.length)}</button>` : ''}
    </section>`;
  }).join('');
  return `<div class="ptop-head">
      <div class="phero">
        ${badge(P.level, 'xl')}
        <div class="ph-text">
          <div class="ph-kicker">PROGRESSION</div>
          <div class="ph-level">LEVEL ${P.level}</div>
          <div class="ph-tier">${esc(tier.toUpperCase())} · ${fmt(list.length)} UNLOCK${list.length === 1 ? '' : 'S'}${P.level === 1 && free ? ` <span class="muted">(${fmt(limitedHere)} set to 1 · ${fmt(list.length - limitedHere)} without a limit)</span>` : ''}</div>
          <div class="ph-chips">${order.map(k => `<button class="pchip" style="--c:${GROUP[k].color}" data-act="progJump" data-arg="${k}">${esc(GROUP[k].name)} <b>${fmt(groups.get(k).length)}</b></button>`).join('')}</div>
        </div>
      </div>
      <img class="eft-logo" src="eft-logo.png" alt="Escape From Tarkov">
    </div>
    <div class="pcats">${sections || `<div class="pempty">${badge(P.level, 'lg', true)}<div>Nothing unlocks at level ${P.level}.</div><div class="muted small">Set items to this level in the Level Limits tab.</div></div>`}</div>`;
}

/** Only the top half (a level picked on the same page): the cards stay, the unlocks animate in. */
function progShowLevel(l) {
  P.level = clamp(l, 1, MAX_LEVEL);
  const page = Math.floor((P.level - 1) / PER_PAGE);
  if (page !== P.page) { P.anim = page > P.page ? 'from-right' : 'from-left'; P.page = page; P.open = new Set(); renderPage(); return; }
  P.open = new Set();
  document.querySelectorAll('#page .pcard').forEach(c => c.classList.toggle('on', Number(c.dataset.arg) === P.level));
  const top = $('#progTop');
  if (!top) { renderPage(); return; }
  top.innerHTML = progTop(progByLevel());
  top.scrollTop = 0;
  top.classList.remove('swap'); void top.offsetWidth; top.classList.add('swap');
  const pct = trackPct(document.querySelectorAll('#page .pcard').length);
  const bar = document.querySelector('#page .ptrack i'); if (bar) bar.style.width = pct + '%';
  document.querySelectorAll('#page .ptick').forEach((t, i) => t.classList.toggle('past', P.page * PER_PAGE + 1 + i <= P.level));
  saveProg();
}

function progGoPage(p) {
  p = clamp(p, 0, PROG_PAGES - 1);
  if (p === P.page) return;
  P.anim = p > P.page ? 'from-right' : 'from-left';
  P.page = p;
  P.level = p * PER_PAGE + 1;
  P.open = new Set();
  renderPage();
  saveProg();
}

/** The track under the cards fills up to the picked card's middle. */
const trackPct = n => ((P.level - 1 - P.page * PER_PAGE + .5) / n) * 100;

function saveProg() { S.ui.progLevel = P.level; saveUi(); }

Object.assign(ACT, {
  progLevel: arg => progShowLevel(Number(arg)),
  progPage: arg => progGoPage(Number(arg)),
  progOpen(k) { P.open.add(k); const y = $('#progTop').scrollTop; $('#progTop').innerHTML = progTop(progByLevel()); $('#progTop').scrollTop = y; },
  progJump(k) { document.getElementById('pcat-' + k)?.scrollIntoView({ behavior: 'smooth', block: 'start' }); },
  progItem(id) {
    const it = S.items.get(id); if (!it) return;
    S.tab = 'levels'; S.page = 'items'; S.cat = groupOf(it); S.filter = 'all'; S.band = null; S.search = ''; $('#search').value = '';
    S.sel = id; S.picked = new Set(); S.limit = 100000;
    saveUi(); renderAll();
    requestAnimationFrame(() => document.querySelector(`#page .row[data-row="${id}"]`)?.scrollIntoView({ block: 'center' }));
  },
});

document.addEventListener('change', e => {
  if (!e.target.matches?.('[data-prog-free]')) return;
  S.ui.progFree = e.target.checked; saveUi(); renderPage();
});

// ←/→ level, Page Up/Down or Q/E page, Home/End first/last; the mouse wheel over the cards flips pages
document.addEventListener('keydown', e => {
  if (S.tab !== 'prog' || !$('#modal').hidden || e.target.matches?.('input[type=text], textarea') || e.ctrlKey || e.altKey || e.metaKey) return;
  const k = e.key;
  const go = f => { e.preventDefault(); f(); };
  if (k === 'ArrowRight') go(() => progShowLevel(P.level + 1));
  else if (k === 'ArrowLeft') go(() => progShowLevel(P.level - 1));
  else if (k === 'PageDown' || k.toLowerCase() === 'e') go(() => progGoPage(P.page + 1));
  else if (k === 'PageUp' || k.toLowerCase() === 'q') go(() => progGoPage(P.page - 1));
  else if (k === 'Home') go(() => progShowLevel(1));
  else if (k === 'End') go(() => progShowLevel(MAX_LEVEL));
});
let wheelAt = 0;
document.addEventListener('wheel', e => {
  if (S.tab !== 'prog' || !e.target.closest?.('.prog-bottom')) return;
  e.preventDefault();
  if (Date.now() - wheelAt < 350) return;
  wheelAt = Date.now();
  progGoPage(P.page + ((e.deltaY || e.deltaX) > 0 ? 1 : -1));
}, { passive: false });

/** Called once the settings are in: back to the level you last looked at. */
function progRestore() {
  if (S.ui.progLevel) { P.level = clamp(S.ui.progLevel, 1, MAX_LEVEL); P.page = Math.floor((P.level - 1) / PER_PAGE); }
}
