/* LevelGate Editor — tags, custom order and the mouse / keyboard moves of the Custom Trader Creator:
   Ctrl-click / Shift-click to pick, drag across rows to pick a box of them, Ctrl+drag a row to move it,
   Alt+↑ / Alt+↓ to move the picked rows. Tags and orders live in the editor's own settings (S.ui),
   never in the game's or other mods' files. */
'use strict';

// =====================================================================
// Tags (per item id, shared by both tabs)
// =====================================================================

const TAG_COLORS = ['#ff7ab6', '#5cc8ff', '#f5cd46', '#a082ff', '#1ed760', '#ffa42b', '#ff6b6b', '#7ee0c3'];
const TAG_PALETTE = [...TAG_COLORS, '#b3b3b3', '#ffffff'];
function tagColor(tag) {
  const custom = S.ui.tagColors?.[tag];
  if (custom) return custom;
  let h = 0;
  for (const ch of tag.toLowerCase()) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return TAG_COLORS[h % TAG_COLORS.length];
}
const tagsOf = id => S.ui.tags?.[id] || [];
const allTags = () => [...new Set(Object.values(S.ui.tags || {}).flat())].sort((a, b) => a.localeCompare(b));
S.tagFilter = { levels: null, stats: null };
const tabKey = () => (S.tab === 'stats' ? 'stats' : 'levels');
const tagPass = id => { const t = S.tagFilter[tabKey()]; return !t || tagsOf(id).includes(t); };

function setTags(ids, fn) {
  const all = { ...(S.ui.tags || {}) };
  for (const id of ids) {
    const t = [...new Set(fn(tagsOf(id)).map(x => x.trim()).filter(Boolean))];
    if (t.length) all[id] = t; else delete all[id];
  }
  S.ui.tags = all;
  saveUi();
  renderPage(); renderDetails();
}

const tagBadges = id => tagsOf(id).map(t => `<span class="badge tagb" style="--c:${tagColor(t)}" data-tag="${esc(t)}">${esc(t)}</span>`).join('');

/** Filter chips for the tags used by the items of this list. */
function tagBar(ids) {
  const counts = new Map();
  for (const id of ids) for (const t of tagsOf(id)) counts.set(t, (counts.get(t) || 0) + 1);
  const cur = S.tagFilter[tabKey()];
  if (cur && !counts.has(cur)) counts.set(cur, 0);
  if (!counts.size) return '';
  return `<div class="toolbar tagbar"><span class="muted small">Tags:</span>
    <button class="chip ${!cur ? 'on' : ''}" data-act="tagFilter" data-arg="">All</button>
    ${[...counts].sort((a, b) => a[0].localeCompare(b[0])).map(([t, n]) => `<button class="chip tagchip ${cur === t ? 'on' : ''}" style="--c:${tagColor(t)}" data-act="tagFilter" data-arg="${esc(t)}" data-tag="${esc(t)}" title="Right-click: Rename, Color, Remove">${esc(t)} <small>${n}</small></button>`).join('')}</div>`;
}

/** Tags card for one or several items. */
function tagCard(ids) {
  const many = ids.length > 1;
  const on = [...new Set(ids.flatMap(tagsOf))].sort((a, b) => a.localeCompare(b));
  const known = allTags().filter(t => !ids.every(id => tagsOf(id).includes(t)));
  return `<div class="card"><h3>Tags</h3>
    <div class="chips">${on.map(t => `<span class="tag" style="--c:${tagColor(t)}" data-tag="${esc(t)}">${esc(t)}${many ? ` <small>${ids.filter(id => tagsOf(id).includes(t)).length}/${ids.length}</small>` : ''}<button data-act="tagRemove" data-arg="${esc(t)}" title="${many ? 'Remove From All Picked' : 'Remove'}">✕</button></span>`).join('')}
      <input type="text" id="tagInput" placeholder="${many ? '+ Tag All (Enter)' : '+ Add Tag (Enter)'}" style="width:160px"></div>
    ${known.length ? `<div class="chips" style="margin-top:6px">${known.map(t => `<button class="chip tagchip" style="--c:${tagColor(t)}" data-act="tagAdd" data-arg="${esc(t)}" data-tag="${esc(t)}">+ ${esc(t)}</button>`).join('')}</div>` : ''}
    <div class="hint">Your own labels (e.g. "Early Meds", "Boss Loot") — shown on the lists and usable as a filter. Only in this editor.</div></div>`;
}
const tagTargets = () => (S.picked.size > 1 ? [...S.picked] : S.sel ? [S.sel] : []);

function tagMenu(x, y, tag) {
  const n = Object.values(S.ui.tags || {}).filter(t => t.includes(tag)).length;
  closePopover();
  const pop = document.createElement('div');
  pop.className = 'popover ctx menu2';
  pop.innerHTML = `<div class="menu-title">Tag “${esc(tag)}” · ${n} item${n === 1 ? '' : 's'}</div>
    <button data-t="only">Show Only This</button><button data-t="rename">Rename…</button>
    <div class="menu-title">Color</div>
    <div class="swatches">${TAG_PALETTE.map(c => `<button class="swatch ${tagColor(tag) === c ? 'on' : ''}" style="--c:${c}" data-t="color" data-c="${c}"></button>`).join('')}<button class="swatch auto" data-t="color" data-c="" title="Automatic">A</button></div>
    <hr><button data-t="remove">Remove From All Items</button>`;
  pop.addEventListener('click', async e => {
    const b = e.target.closest('[data-t]');
    if (!b) return;
    closePopover();
    const what = b.dataset.t;
    if (what === 'only') ACT.tagFilter(tag);
    if (what === 'color') { const c = (S.ui.tagColors ||= {}); if (b.dataset.c) c[tag] = b.dataset.c; else delete c[tag]; saveUi(); renderPage(); renderDetails(); }
    if (what === 'rename') {
      const name = ((await promptBox('Rename Tag', `New name for “${tag}” (on all ${n} items)`, tag)) || '').trim();
      if (!name || name === tag) return;
      const ids = Object.keys(S.ui.tags || {}).filter(id => tagsOf(id).includes(tag));
      const c = S.ui.tagColors || {};
      if (c[tag]) { c[name] = c[tag]; delete c[tag]; }
      for (const k of ['levels', 'stats']) if (S.tagFilter[k] === tag) S.tagFilter[k] = name;
      setTags(ids, t => t.map(x => (x === tag ? name : x)));
      toast(`Renamed to “${name}”`);
    }
    if (what === 'remove') {
      const ids = Object.keys(S.ui.tags || {}).filter(id => tagsOf(id).includes(tag));
      for (const k of ['levels', 'stats']) if (S.tagFilter[k] === tag) S.tagFilter[k] = null;
      setTags(ids, t => t.filter(x => x !== tag));
      toast(`Removed “${tag}”`);
    }
  });
  document.body.appendChild(pop);
  pop.style.left = Math.min(x, innerWidth - pop.offsetWidth - 8) + 'px';
  pop.style.top = Math.max(8, Math.min(y, innerHeight - pop.offsetHeight - 8)) + 'px';
  closePopover.fn = ev => { if (!pop.contains(ev.target)) closePopover(); };
  setTimeout(() => document.addEventListener('mousedown', closePopover.fn), 0);
}

Object.assign(ACT, {
  tagFilter(t) { S.tagFilter[tabKey()] = t || null; S.limit = 300; renderPage(false); },
  tagAdd(t) { setTags(tagTargets(), tags => [...tags, t]); },
  tagRemove(t) { setTags(tagTargets(), tags => tags.filter(x => x !== t)); },
});
document.addEventListener('keydown', e => {
  if (e.target.id !== 'tagInput' || e.key !== 'Enter') return;
  const v = e.target.value.trim();
  if (!v) return;
  e.preventDefault();
  setTags(tagTargets(), tags => [...tags, v]);
  setTimeout(() => $('#tagInput')?.focus(), 0);
}, true);
document.addEventListener('contextmenu', e => {
  const t = e.target.closest('[data-tag]');
  if (!t) return;
  e.preventDefault(); e.stopImmediatePropagation();
  tagMenu(e.clientX, e.clientY, t.dataset.tag);
}, true);

// =====================================================================
// Custom order (per tab)
// =====================================================================

function orderMap(tab) {
  const m = new Map();
  (S.ui.order?.[tab] || []).forEach((id, i) => m.set(id, i));
  return m;
}
/** Sort compare for "Custom Order": your order first, the rest by name. */
function customCmp(tab) {
  const m = orderMap(tab);
  return (a, b) => {
    const ia = m.has(a.i) ? m.get(a.i) : Infinity, ib = m.has(b.i) ? m.get(b.i) : Infinity;
    return ia - ib || (a.n || '').localeCompare(b.n || '');
  };
}
const isCustom = () => (S.tab === 'stats' ? S.statSort.key === 'custom' : S.sort.key === 'custom');

/** Moves ids to sit before `beforeId` (null = after the last shown row), in the current tab's custom order. */
function moveRows(ids, beforeId) {
  const tab = tabKey();
  const shown = S.shown.map(x => x.i);
  // the order starts from what's on screen when you first rearrange
  let order = (S.ui.order?.[tab] || []).slice();
  if (!isCustom() || !order.length) order = shown.concat(order.filter(id => !shown.includes(id)));
  const moving = order.filter(id => ids.includes(id)).concat(ids.filter(id => !order.includes(id)));
  order = order.filter(id => !ids.includes(id));
  let at = beforeId ? order.indexOf(beforeId) : -1;
  if (at < 0) { const lastShown = [...shown].reverse().find(id => !ids.includes(id) && order.includes(id)); at = lastShown ? order.indexOf(lastShown) + 1 : order.length; }
  order.splice(at, 0, ...moving);
  S.ui.order = { ...(S.ui.order || {}), [tab]: order };
  if (S.tab === 'stats') S.statSort = { key: 'custom', dir: 1 }; else S.sort = { key: 'custom', dir: 1 };
  saveUi();
  renderPage(); renderHeader();
}

// Alt+↑ / Alt+↓ moves the picked rows one place
document.addEventListener('keydown', e => {
  if (!e.altKey || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') || S.page !== 'items' || !$('#modal').hidden) return;
  const ids = S.picked.size ? [...S.picked] : S.sel ? [S.sel] : [];
  if (!ids.length) return;
  e.preventDefault(); e.stopImmediatePropagation();
  const shown = S.shown.map(x => x.i);
  const idx = ids.map(id => shown.indexOf(id)).filter(i => i >= 0).sort((a, b) => a - b);
  if (!idx.length) return;
  if (e.key === 'ArrowUp') { if (idx[0] === 0) return; moveRows(ids, shown[idx[0] - 1]); }
  else {
    const after = idx[idx.length - 1] + 1;
    if (after >= shown.length) return;
    moveRows(ids, shown[after + 1] ?? null);
  }
  document.querySelector(`#page .row[data-row="${S.sel}"]`)?.scrollIntoView({ block: 'nearest' });
}, true);

// =====================================================================
// Mouse: drag across rows = pick a box of them; Ctrl+drag a row = move it
// =====================================================================

let pageDrag = null, suppressRowClick = false;
document.addEventListener('mousedown', e => {
  if (e.button !== 0 || S.page !== 'items' || !e.target.closest('#page')) return;
  if (e.target.closest('input, select, textarea, button, a, .tag, .badge.tagb, .toolbar, .list-head, .more')) return;
  const row = e.target.closest('.row[data-row]');
  if (!row && !e.target.closest('.list')) return;
  if (row && (e.ctrlKey || e.metaKey)) {
    pageDrag = { kind: 'row', id: row.dataset.row, y0: e.clientY, moving: false };
    return; // Ctrl+click without moving still picks
  }
  pageDrag = { kind: 'box', x0: e.clientX, y0: e.clientY + $('#page').scrollTop, base: e.shiftKey ? new Set(S.picked) : new Set(), moved: false };
});
document.addEventListener('mousemove', e => {
  if (!pageDrag) return;
  const page = $('#page'), r = page.getBoundingClientRect();
  if (e.clientY > r.bottom - 30) page.scrollTop += 14; else if (e.clientY < r.top + 90) page.scrollTop -= 14;
  if (pageDrag.kind === 'row') {
    if (!pageDrag.moving && Math.abs(e.clientY - pageDrag.y0) < 6) return;
    if (!pageDrag.moving) {
      pageDrag.moving = true;
      const ids = S.picked.has(pageDrag.id) ? [...S.picked] : [pageDrag.id];
      pageDrag.ids = ids;
      pageDrag.line = document.createElement('div'); pageDrag.line.className = 'drop-line'; document.body.appendChild(pageDrag.line);
      document.body.classList.add('dragging');
    }
    const rows = [...page.querySelectorAll('.row[data-row]')];
    let before = null, y = 0;
    for (const row of rows) { const b = row.getBoundingClientRect(); if (e.clientY < b.top + b.height / 2) { before = row.dataset.row; y = b.top; break; } y = b.bottom; }
    pageDrag.before = before;
    const lr = (rows[0] || page).getBoundingClientRect();
    Object.assign(pageDrag.line.style, { left: lr.left + 'px', width: lr.width + 'px', top: (y - 1) + 'px' });
    return;
  }
  // box select
  const y1 = e.clientY + page.scrollTop;
  if (!pageDrag.moved && Math.abs(y1 - pageDrag.y0) < 6 && Math.abs(e.clientX - pageDrag.x0) < 6) return;
  if (!pageDrag.el) { pageDrag.el = document.createElement('div'); pageDrag.el.className = 'marquee'; document.body.appendChild(pageDrag.el); document.body.classList.add('dragging'); }
  pageDrag.moved = true;
  const top = Math.min(pageDrag.y0, y1), bottom = Math.max(pageDrag.y0, y1);
  const left = Math.min(pageDrag.x0, e.clientX), right = Math.max(pageDrag.x0, e.clientX);
  const vt = Math.max(top - page.scrollTop, r.top), vb = Math.min(bottom - page.scrollTop, r.bottom);
  Object.assign(pageDrag.el.style, { left: left + 'px', width: (right - left) + 'px', top: vt + 'px', height: Math.max(0, vb - vt) + 'px' });
  const hits = new Set(pageDrag.base);
  page.querySelectorAll('.row[data-row]').forEach(row => {
    const b = row.getBoundingClientRect();
    if (b.top + page.scrollTop < bottom && b.bottom + page.scrollTop > top) hits.add(row.dataset.row);
  });
  S.picked = hits.size > 1 ? hits : new Set();
  if (hits.size) S.sel = [...hits].pop();
  markSelection();
});
document.addEventListener('mouseup', () => {
  if (!pageDrag) return;
  const d = pageDrag;
  pageDrag = null;
  document.body.classList.remove('dragging');
  d.line?.remove(); d.el?.remove();
  if (d.kind === 'row' && d.moving) {
    suppressRowClick = true; setTimeout(() => { suppressRowClick = false; }, 0);
    if (d.before && d.ids.includes(d.before)) return;
    moveRows(d.ids, d.before);
    toast(`Moved ${d.ids.length > 1 ? d.ids.length + ' items' : ''} — Custom Order`);
    return;
  }
  if (d.kind === 'box' && d.moved) {
    suppressRowClick = true; setTimeout(() => { suppressRowClick = false; }, 0);
    renderDetails();
  }
});
// a drag ends with a click on a row: don't let it change the picks
document.addEventListener('click', e => { if (suppressRowClick && e.target.closest('#page')) { e.stopImmediatePropagation(); e.preventDefault(); } }, true);
