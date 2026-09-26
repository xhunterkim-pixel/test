/* LevelGate Editor — the page. Same look as the Custom Trader Creator.
   Shows every game item (and modded items) by category, and edits the level
   each one unlocks at: BepInEx\plugins\LevelGate\config\level_requirements.json.
   The C# window (HostForm) reads / writes the file; this page does the rest. */
'use strict';

const $ = s => document.querySelector(s);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const fmt = n => Math.round(n).toLocaleString('en-US');
const MAX_LEVEL = 79;

// =====================================================================
// Host bridge
// =====================================================================

const host = (() => {
  const wv = (window.chrome && window.chrome.webview) || window.devHost;
  const pending = new Map();
  let seq = 0;
  wv.addEventListener('message', e => {
    const m = e.data;
    if (m && m.event) { onHostEvent(m); return; }
    const p = pending.get(m.id);
    if (!p) return;
    pending.delete(m.id);
    m.ok ? p.resolve(m.result) : p.reject(new Error(m.error || 'failed'));
  });
  return {
    call(method, args = {}) {
      const id = ++seq;
      return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        wv.postMessage({ id, method, args });
      });
    },
  };
})();

// =====================================================================
// State
// =====================================================================

/** Categories = the item groups of the Custom Trader Creator (key, name, color). */
const GROUPS = [
  ['Weapons', 'Weapons', '#f15e6c'], ['Melee', 'Melee', '#ff8a65'], ['Grenades', 'Grenades', '#ffa42b'], ['Ammo', 'Ammo', '#f5cd46'],
  ['WeaponParts', 'Weapon Parts', '#c7a36b'], ['Armor', 'Armor', '#509bf5'], ['Headwear', 'Headwear', '#6fb3ff'], ['Rigs', 'Rigs', '#7d9cf0'],
  ['Backpacks', 'Backpacks', '#a082ff'], ['Gear', 'Other Gear', '#b39ddb'], ['Medical', 'Medical', '#1ed760'], ['Food', 'Food & Drink', '#8bd66b'],
  ['Electronics', 'Electronics', '#4dd0e1'], ['Barter', 'Barter Items', '#bdbdbd'], ['Keys', 'Keys', '#e0c068'], ['Containers', 'Containers', '#90a4ae'],
  ['Special', 'Special', '#ff7ab6'], ['Other', 'Other', '#8a8a8a'],
];
const GROUP = Object.fromEntries(GROUPS.map(([k, n, c]) => [k, { key: k, name: n, color: c }]));
const SORTS = [['name', 'Name'], ['level', 'Level'], ['cat', 'Category'], ['price', 'Price'], ['changed', 'Changed First']];

const S = {
  configFile: null, sptRoot: null, version: '',
  items: new Map(),          // id -> { i, n, s, c, g, k, h, f, x, m }
  saved: new Map(),          // id -> level, as on disk
  levels: new Map(),         // id -> level, as edited
  mods: [], modItemsOff: [],
  page: 'items', cat: 'all', filter: 'all', sort: { key: 'name', dir: 1 }, search: '', showHidden: false,
  sel: null, picked: new Set(), anchor: null,
  limit: 300, shown: [],
  undo: [], redo: [],
  ui: {},
};

// =====================================================================
// Start
// =====================================================================

async function start() {
  try { apply(await host.call('init')); }
  catch (e) { status('Could not start: ' + e.message); }
}

function apply(snap) {
  S.configFile = snap.configFile; S.sptRoot = snap.sptRoot; S.version = snap.version || S.version;
  if (snap.ui && !S.uiLoaded) { S.ui = snap.ui; S.uiLoaded = true; restoreUi(); }
  S.saved = new Map(Object.entries(snap.levels || {}));
  S.levels = new Map(S.saved);
  S.undo = []; S.redo = [];
  applyItems(snap);
  $('#folder').textContent = S.configFile || 'Pick BepInEx\\plugins\\LevelGate\\config\\level_requirements.json (Browse…)';
  $('#appVersion').textContent = 'v' + (S.version || '1.0.0');
  status(!S.configFile ? 'LevelGate\'s config wasn\'t found — click Browse… and pick SPT\\BepInEx\\plugins\\LevelGate\\config\\level_requirements.json.'
    : snap.notInGame ? '⚠ This file isn\'t inside an SPT folder, so the game never reads it — Browse… to SPT\\BepInEx\\plugins\\LevelGate\\config\\level_requirements.json.'
    : `Loaded ${S.saved.size} level limit(s).`);
  $('#top .search-pill').classList.toggle('warn', !!snap.notInGame);
  renderAll();
}

function applyItems(snap) {
  if (snap.items) S.items = new Map(snap.items.map(it => [it.i, it]));
  if (snap.mods) S.mods = snap.mods;
  if (snap.modItemsOff) S.modItemsOff = snap.modItemsOff;
  if (snap.itemsStatus !== undefined) $('#itemsStatus').textContent = snap.itemsStatus;
}

function restoreUi() {
  const u = S.ui;
  if (u.cat) S.cat = u.cat;
  if (u.filter) S.filter = u.filter;
  if (u.sort) S.sort = u.sort;
  S.showHidden = !!u.showHidden;
  if (u.left) document.documentElement.style.setProperty('--left', clamp(u.left, 200, 420) + 'px');
  if (u.right) document.documentElement.style.setProperty('--right', clamp(u.right, 340, 900) + 'px');
}
let uiTimer = 0;
function saveUi() {
  Object.assign(S.ui, { cat: S.cat, filter: S.filter, sort: S.sort, showHidden: S.showHidden });
  clearTimeout(uiTimer);
  uiTimer = setTimeout(() => host.call('saveUi', { ui: S.ui }).catch(() => { }), 400);
}

// =====================================================================
// Items, levels, changes
// =====================================================================

const levelOf = id => S.levels.get(id);
const isLimited = id => S.levels.has(id);
const groupOf = it => GROUP[it.g] ? it.g : 'Other';
const priceOf = it => it.f || it.h || 0;

/** An item id from the config that isn't in the game's list or an imported mod. */
function unknownItem(id) {
  const off = S.modItemsOff.find(x => x.i === id);
  return off ? { ...off, off: true } : { i: id, n: `Unknown item ${id}`, s: '?', g: 'Other', unknown: true };
}
const itemOf = id => S.items.get(id) || unknownItem(id);
const unknownIds = () => [...S.levels.keys()].filter(id => !S.items.has(id));

function changes() {
  const out = [];
  for (const [id, lvl] of S.levels) if (S.saved.get(id) !== lvl) out.push({ id, from: S.saved.get(id), to: lvl });
  for (const [id, lvl] of S.saved) if (!S.levels.has(id)) out.push({ id, from: lvl, to: undefined });
  return out;
}
const isChanged = id => S.saved.get(id) !== S.levels.get(id);

/** Sets levels (undefined = no limit) as one undo step. */
function setLevels(ids, level, label) {
  const step = [];
  for (const id of ids) {
    const from = S.levels.get(id);
    const to = level === undefined || level === null || level === '' ? undefined : clamp(Math.round(Number(level)), 1, MAX_LEVEL);
    if (from === to) continue;
    step.push({ id, from, to });
    if (to === undefined) S.levels.delete(id); else S.levels.set(id, to);
  }
  if (!step.length) return 0;
  S.undo.push(step); S.redo = [];
  if (S.undo.length > 200) S.undo.shift();
  if (label) status(label);
  afterChange(step.map(x => x.id));
  return step.length;
}

function undo(redo = false) {
  const from = redo ? S.redo : S.undo, to = redo ? S.undo : S.redo;
  const step = from.pop();
  if (!step) return;
  for (const c of step) {
    const v = redo ? c.to : c.from;
    if (v === undefined) S.levels.delete(c.id); else S.levels.set(c.id, v);
  }
  to.push(step);
  afterChange(step.map(x => x.id));
  toast(`${redo ? 'Redone' : 'Undone'}: ${step.length} change${step.length === 1 ? '' : 's'}`);
}

/** Redraws what a level change touches without rebuilding the list (keeps focus while typing). */
function afterChange(ids) {
  for (const id of ids) updateRow(id);
  renderNav(); renderHeader(); renderBottom();
  if (ids.includes(S.sel) || S.picked.size > 1) renderDetails();
}

// =====================================================================
// Filtering / sorting
// =====================================================================

function inCat(it, cat = S.cat) {
  if (cat === 'all') return true;
  if (cat === 'modded') return !!it.m;
  if (cat === 'unknown') return !S.items.has(it.i);
  return groupOf(it) === cat;
}

function catItems(cat) {
  if (cat === 'unknown') return unknownIds().map(itemOf);
  const list = [];
  for (const it of S.items.values()) if ((S.showHidden || !it.x || isLimited(it.i)) && inCat(it, cat)) list.push(it);
  if (cat === 'all') for (const id of unknownIds()) list.push(itemOf(id));
  return list;
}

function matches(it, q) {
  if (!q) return true;
  return it.i.includes(q) || (it.n || '').toLowerCase().includes(q) || (it.s || '').toLowerCase().includes(q) || (it.m || '').toLowerCase().includes(q);
}

function shownItems() {
  const q = S.search.trim().toLowerCase();
  let list = catItems(S.cat).filter(it => matches(it, q));
  if (S.filter === 'limited') list = list.filter(it => isLimited(it.i));
  else if (S.filter === 'free') list = list.filter(it => !isLimited(it.i));
  const d = S.sort.dir;
  const byName = (a, b) => (a.n || '').localeCompare(b.n || '');
  const cmp = {
    name: (a, b) => d * byName(a, b),
    level: (a, b) => {
      const la = levelOf(a.i), lb = levelOf(b.i);
      if (la === undefined && lb === undefined) return byName(a, b);
      if (la === undefined) return 1; if (lb === undefined) return -1; // no limit always last
      return d * (la - lb) || byName(a, b);
    },
    cat: (a, b) => d * (GROUPS.findIndex(g => g[0] === groupOf(a)) - GROUPS.findIndex(g => g[0] === groupOf(b))) || byName(a, b),
    price: (a, b) => d * (priceOf(a) - priceOf(b)) || byName(a, b),
    changed: (a, b) => Number(isChanged(b.i)) - Number(isChanged(a.i)) || byName(a, b),
  }[S.sort.key] || ((a, b) => byName(a, b));
  return list.sort(cmp);
}

// =====================================================================
// Rendering
// =====================================================================

function renderAll() {
  renderNav(); renderHeader(); renderPage(); renderDetails(); renderBottom();
}

function catCounts(cat) {
  const list = catItems(cat);
  return { total: list.length, limited: list.filter(it => isLimited(it.i)).length };
}

function renderNav() {
  const entry = (key, name, color) => {
    const c = catCounts(key);
    if (key !== 'all' && key !== 'modded' && !c.total) return '';
    if (key === 'modded' && !c.total && !S.mods.length) return '';
    return `<button class="nav cat ${S.page === 'items' && S.cat === key ? 'on' : ''}" data-act="cat" data-arg="${key}" title="${esc(name)}">
      <i class="dot" style="--c:${color}"></i><span>${esc(name)}</span><em>${c.limited ? `<b>${fmt(c.limited)}</b> / ` : ''}${fmt(c.total)}</em></button>`;
  };
  let html = entry('all', 'All Items', '#ffffff') + GROUPS.map(([k, n, c]) => entry(k, n, c)).join('') + entry('modded', 'Modded Items', '#ff7ab6');
  if (unknownIds().length) html += `<button class="nav cat ${S.cat === 'unknown' && S.page === 'items' ? 'on' : ''}" data-act="cat" data-arg="unknown" title="Ids in the config that no loaded item has">
      <i class="dot" style="--c:var(--red)"></i><span>Unknown IDs</span><em class="bad">${unknownIds().length}</em></button>`;
  $('#nav').innerHTML = html;
  $('#navModsBtn').classList.toggle('on', S.page === 'mods');
  $('#navMods').textContent = S.mods.length ? `${S.mods.filter(m => m.enabled).length}/${S.mods.length}` : '';
}

function catName(cat) {
  return cat === 'all' ? 'All Items' : cat === 'modded' ? 'Modded Items' : cat === 'unknown' ? 'Unknown IDs' : GROUP[cat]?.name || cat;
}
function catColor(cat) {
  return cat === 'all' ? '#5b5b5b' : cat === 'modded' ? '#ff7ab6' : cat === 'unknown' ? '#f15e6c' : GROUP[cat]?.color || '#5b5b5b';
}

function renderHeader() {
  const mods = S.page === 'mods';
  $('#headerKind').textContent = mods ? 'MODS' : 'LEVEL LIMITS';
  $('#headerTitle').textContent = mods ? 'Modded Items' : catName(S.cat);
  const color = mods ? '#ff7ab6' : catColor(S.cat);
  $('#header').style.setProperty('--hc', color);
  $('#headerArt').style.setProperty('--c', color);
  $('#headerArt').textContent = mods ? '⚙' : S.cat === 'all' ? 'LV' : catName(S.cat).slice(0, 2).toUpperCase();
  if (mods) {
    $('#headerSub').textContent = `${S.mods.length} mod${S.mods.length === 1 ? '' : 's'} imported · their items show in the categories (switch a mod off to hide its items)`;
  } else {
    const list = catItems(S.cat);
    const lv = list.map(it => levelOf(it.i)).filter(x => x !== undefined);
    $('#headerSub').textContent = `${fmt(lv.length)} limited of ${fmt(list.length)} item${list.length === 1 ? '' : 's'}` +
      (lv.length ? ` · Levels ${Math.min(...lv)}–${Math.max(...lv)}` : '');
  }
  $('#chips').hidden = mods;
  $('#viewLabel').textContent = (SORTS.find(s => s[0] === S.sort.key) || SORTS[0])[1];
  const box = $('#searchBox'), input = $('#search');
  if (document.activeElement !== input) input.value = S.search;
  box.classList.toggle('has', !!input.value);
  box.classList.toggle('open', !!input.value || document.activeElement === input);
  document.body.classList.toggle('search-open', box.classList.contains('open'));
}

function icon(it, big = false) {
  const short = esc((it.s || it.n || '?').slice(0, 8));
  if (it.unknown) return `<div class="thumb" style="--tc:#5a2a2e">?</div>`;
  const kind = big ? 'base-image' : 'icon';
  return `<div class="thumb pic${big ? ' big' : ''}" data-short="${short}"><img src="https://files.local/icon/${it.i}-${kind}.webp" alt="" loading="lazy" onerror="this.remove()"><span>${short}</span></div>`;
}

function levelCell(id) {
  const lvl = levelOf(id);
  return `<div class="lvl ${lvl === undefined ? 'free' : ''}"><input type="number" class="lvl-in" data-lvl="${id}" min="1" max="${MAX_LEVEL}" step="1" placeholder="—" value="${lvl ?? ''}" title="Level it unlocks at (empty = no limit)"></div>`;
}

function rowHtml(it, n) {
  const id = it.i;
  const g = GROUP[groupOf(it)];
  const badges = [
    it.m ? `<span class="badge" style="--c:var(--pink)" title="Added by the mod ${esc(it.m)}">${esc(it.m)}</span>` : '',
    it.off ? `<span class="badge" style="--c:var(--orange)" title="Its mod is switched off in Mods">MOD OFF</span>` : '',
    it.unknown ? `<span class="badge" style="--c:var(--red)" title="No loaded item has this id (a mod that isn't imported, or was removed)">UNKNOWN</span>` : '',
    it.x ? `<span class="badge" style="--c:#9a9a9a" title="A dev / template item players don't normally get">HIDDEN</span>` : '',
  ].join('');
  const price = priceOf(it);
  return `<div class="row ${id === S.sel ? 'sel' : ''} ${S.picked.has(id) ? 'picked' : ''} ${isChanged(id) ? 'changed' : ''}" data-row="${id}">
    <div class="idx">${n}</div>
    <div class="cell">${icon(it)}<div class="text"><div class="line1"><span class="title">${esc(it.n)}</span><span class="dirty" title="Changed — not saved yet">•</span><div class="badges">${badges}</div></div>
      <div class="line2">${esc(it.s || '')}${it.s ? ' · ' : ''}<span class="mono">${id}</span></div></div></div>
    <div class="col"><i class="dot" style="--c:${g?.color || '#888'}"></i>${esc(g?.name || 'Other')}</div>
    <div class="col num">${price ? fmt(price) + ' ₽' : '—'}</div>
    ${levelCell(id)}
  </div>`;
}

function updateRow(id) {
  const row = document.querySelector(`#page .row[data-row="${id}"]`);
  if (!row) return;
  row.classList.toggle('changed', isChanged(id));
  const input = row.querySelector('.lvl-in');
  const lvl = levelOf(id);
  if (document.activeElement !== input) input.value = lvl ?? '';
  row.querySelector('.lvl').classList.toggle('free', lvl === undefined);
}

function listHead() {
  const cols = [['', '#'], ['name', 'Item'], ['cat', 'Category'], ['price', 'Price'], ['level', 'Level']];
  return `<div class="list-head">${cols.map(([k, t]) => k
    ? `<div><span class="head-text sortable ${S.sort.key === k ? 'on' : ''}" data-act="sortBy" data-arg="${k}">${t}${S.sort.key === k ? `<span class="sort-arrow">${S.sort.dir > 0 ? '▲' : '▼'}</span>` : ''}</span></div>`
    : `<div>${t}</div>`).join('')}</div>`;
}

function renderPage(keepScroll = true) {
  const page = $('#page');
  const top = page.scrollTop;
  if (S.page === 'mods') { page.innerHTML = modsPage(); if (!keepScroll) page.scrollTop = 0; return; }
  const list = S.shown = shownItems();
  const rows = list.slice(0, S.limit).map((it, i) => rowHtml(it, i + 1)).join('');
  const n = S.picked.size;
  const chip = (v, t) => `<button class="chip ${S.filter === v ? 'on' : ''}" data-act="filter" data-arg="${v}">${t}</button>`;
  page.innerHTML = `<div class="toolbar sticky">
      ${chip('all', 'All')}${chip('limited', 'Limited')}${chip('free', 'Not Limited')}
      <span class="sep"></span>
      <button class="outline" data-act="bulkSet" ${n ? '' : 'disabled'} title="Set one level for every picked item">${n > 1 ? `Set Level (${n})…` : 'Set Level…'}</button>
      <button class="danger" data-act="bulkRemove" ${n ? '' : 'disabled'} title="Del">${n > 1 ? `Remove Limit (${n})` : 'Remove Limit'}</button>
      <button class="outline" data-act="addId" title="Add a limit by item id (for items not in the list)">+ Add by ID</button>
      <span class="muted small">${fmt(list.length)} shown${n ? ` · ${n} picked` : ''}</span>
    </div>
    <div class="list lg" data-list="items">${listHead()}${rows || `<div class="empty">${S.items.size || S.levels.size ? 'Nothing matches.' : 'No items loaded — pick the config file with Browse… (the item list comes from the SPT folder above it).'}</div>`}
    ${list.length > S.limit ? `<button class="more" data-act="more">Show ${fmt(Math.min(300, list.length - S.limit))} more (${fmt(list.length - S.limit)} left)</button>` : ''}</div>`;
  page.scrollTop = keepScroll ? top : 0;
}

// ---- right panel
function renderDetails() {
  const d = $('#details');
  const picked = [...S.picked];
  if (S.page === 'mods') { $('#detailsTitle').textContent = 'Mods'; d.innerHTML = modsHelp(); return; }
  if (picked.length > 1) { $('#detailsTitle').textContent = `${picked.length} Items Picked`; d.innerHTML = multiDetails(picked); return; }
  if (!S.sel) {
    $('#detailsTitle').textContent = 'LevelGate Editor';
    d.innerHTML = welcome();
    return;
  }
  const it = itemOf(S.sel);
  const lvl = levelOf(it.i), was = S.saved.get(it.i);
  const g = GROUP[groupOf(it)];
  $('#detailsTitle').textContent = it.n;
  d.innerHTML = `
    <div class="card hero">${icon(it, true)}
      <div class="hero-text"><div class="kind" style="color:${g?.color}">${esc((g?.name || 'Other').toUpperCase())}</div>
        <div class="hero-name">${esc(it.s || it.n)}</div>
        <div class="muted small mono copy" data-act="copyId" data-arg="${it.i}" title="Click to copy">${it.i} ⧉</div></div></div>
    <div class="card level-card">
      <h3>Unlocks At</h3>
      <div class="big-level ${lvl === undefined ? 'free' : ''}">${lvl === undefined ? 'No Limit' : `Level <b>${lvl}</b>`}</div>
      <input type="range" class="slider" id="lvlSlider" min="1" max="${MAX_LEVEL}" value="${lvl ?? 1}" ${lvl === undefined ? 'disabled' : ''}>
      <div class="quick">${[1, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50].map(n => `<button class="chip ${lvl === n ? 'on' : ''}" data-act="quick" data-arg="${n}">${n}</button>`).join('')}</div>
      <div class="toolbar" style="padding:8px 0 0">
        ${lvl === undefined ? '<button class="primary" data-act="quick" data-arg="1">Add a Limit</button>' : '<button class="danger" data-act="bulkRemove">Remove Limit</button>'}
        ${isChanged(it.i) ? `<button class="outline" data-act="revert" title="Back to what's saved">Revert (${was === undefined ? 'no limit' : 'Lvl ' + was})</button>` : ''}
      </div>
      <div class="hint">${lvl === undefined ? 'Anyone can use it.' : lvl === 1 ? 'Level 1 = usable from the start, but tracked (green stripes).' : `Players below level ${lvl} can't use, equip or load it (red stripes); from level ${lvl} on it's unlocked (green).`}</div>
    </div>
    <div class="card">
      <h3>Item</h3>
      <div class="field"><label>Name</label><div>${esc(it.n)}</div></div>
      <div class="field"><label>Short Name</label><div>${esc(it.s || '—')}</div></div>
      <div class="field"><label>Category</label><div>${esc(g?.name || 'Other')}${it.k ? ` · ${esc(it.k.replace(/^Caliber/, ''))}` : ''}</div></div>
      ${it.h ? `<div class="field"><label>Handbook Price</label><div>${fmt(it.h)} ₽</div></div>` : ''}
      ${it.f ? `<div class="field"><label>Flea Price</label><div>${fmt(it.f)} ₽</div></div>` : ''}
      ${it.m ? `<div class="field"><label>Mod</label><div style="color:var(--pink)">${esc(it.m)}${it.off ? ' (switched off)' : ''}</div></div>` : ''}
      ${it.unknown ? '<div class="hint">No loaded item has this id — it may come from a mod that isn\'t imported (Mods page) or was uninstalled. The limit still works in game if the item exists.</div>' : ''}
    </div>`;
  const slider = $('#lvlSlider');
  if (slider) slider.oninput = () => {
    S.levels.set(it.i, Number(slider.value));
    $('.big-level').innerHTML = `Level <b>${slider.value}</b>`;
    updateRow(it.i); renderNav(); renderHeader(); renderBottom();
  };
  if (slider) slider.onchange = () => {
    // one undo step for the whole slide
    const to = Number(slider.value);
    S.levels.set(it.i, lvl); if (lvl === undefined) S.levels.delete(it.i);
    setLevels([it.i], to);
  };
}

function multiDetails(ids) {
  const levels = ids.map(levelOf);
  const same = levels.every(l => l === levels[0]) ? levels[0] : null;
  const limited = levels.filter(l => l !== undefined).length;
  return `<div class="card">
      <h3>Set One Level for All</h3>
      <div class="hint">${limited} of ${ids.length} limited${same !== null && same !== undefined ? ` · all at level ${same}` : ''}.</div>
      <div class="quick">${[1, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50].map(n => `<button class="chip ${same === n ? 'on' : ''}" data-act="quickMany" data-arg="${n}">${n}</button>`).join('')}</div>
      <div class="toolbar" style="padding:10px 0 0"><button class="outline" data-act="bulkSet">Other Level…</button><button class="danger" data-act="bulkRemove">Remove All Limits</button>
        <button class="outline" data-act="bulkShift" data-arg="1">+1</button><button class="outline" data-act="bulkShift" data-arg="-1">−1</button></div>
    </div>
    <div class="card"><h3>Picked</h3><div class="mini">${ids.slice(0, 200).map(id => { const it = itemOf(id); const l = levelOf(id); return `<div class="row" data-act="selOnly" data-arg="${id}"><div class="cell">${icon(it)}<div class="text"><div class="title">${esc(it.n)}</div></div><span class="side">${l === undefined ? 'no limit' : 'Lvl ' + l}</span></div></div>`; }).join('')}</div></div>`;
}

function welcome() {
  const lv = [...S.levels.values()];
  const bands = [[1, 1], [2, 10], [11, 20], [21, 30], [31, 40], [41, MAX_LEVEL]];
  const max = Math.max(1, ...bands.map(([a, b]) => lv.filter(x => x >= a && x <= b).length));
  return `<div class="card"><h3>Level Limits</h3>
      <div class="hint">${fmt(S.levels.size)} item(s) limited. Click an item to set the level it unlocks at, or type it straight into the Level column. Empty = no limit.</div>
      <div class="bars">${bands.map(([a, b]) => { const n = lv.filter(x => x >= a && x <= b).length; return `<div class="bar-row"><span>${a === b ? 'Lvl ' + a : `Lvl ${a}–${b === MAX_LEVEL ? b + '' : b}`}</span><div class="bar"><i style="width:${(n / max) * 100}%"></i></div><b>${n}</b></div>`; }).join('')}</div></div>
    <div class="card"><h3>While You Play</h3>
      <div class="hint">Save here and LevelGate picks the file up in game within a couple of seconds (or press F9 → Reload from disk). Reopen the inventory to refresh item names. Changes made in the game's F9 window show up here too.</div></div>`;
}

// ---- Mods page
function modsPage() {
  const rows = S.mods.map(m => `<div class="row mod-row">
      <div class="idx"><label class="switch" title="${m.enabled ? 'On — its items are listed' : 'Off — its items are hidden'}"><input type="checkbox" data-mod="${esc(m.name)}" ${m.enabled ? 'checked' : ''}><span class="track"></span></label></div>
      <div class="cell"><div class="thumb" style="--tc:#5a2d48">${esc(m.name.slice(0, 2).toUpperCase())}</div><div class="text"><div class="title">${esc(m.name)}</div>
        <div class="line2">${esc(m.folder)}</div>${m.error ? `<div class="line2" style="color:var(--red)">${esc(m.error)}</div>` : ''}</div></div>
      <div class="col">${fmt(m.count)} item${m.count === 1 ? '' : 's'}</div>
      <div class="col">${fmt([...S.levels.keys()].filter(id => (S.items.get(id) || S.modItemsOff.find(x => x.i === id))?.m === m.name).length)} limited</div>
      <div><button class="icon-btn" data-act="modRemove" data-arg="${esc(m.name)}" title="Remove this import (its limits stay in the file)">✕</button></div>
    </div>`).join('');
  return `<div class="toolbar sticky">
      <button class="primary" data-act="modsScanAll" title="Looks through every folder in SPT\\user\\mods for item files and en.json">Scan My Mods Folder</button>
      <button class="outline" data-act="modAddFolder">Import a Mod Folder…</button>
      <button class="outline" data-act="modRescan" ${S.mods.length ? '' : 'disabled'}>Rescan All</button>
    </div>
    <div class="list mods">${rows || '<div class="empty">No mods imported yet. <b>Scan My Mods Folder</b> finds every server mod that adds items (weapons, gear, meds…).</div>'}</div>`;
}
function modsHelp() {
  return `<div class="card"><h3>Modded Items</h3><div class="hint">Items added by server mods (WTT, custom items…) are found by reading the mod's json files — nothing in the mod is changed.
    Switch a mod off to hide its items from the lists; limits you set on them stay in the file either way.</div></div>`;
}

function renderBottom() {
  const n = changes().length;
  $('#unsaved').textContent = n ? `${n} Unsaved` : '';
  $('#undoBtn').disabled = !S.undo.length;
  $('#redoBtn').disabled = !S.redo.length;
  host.call('setUnsaved', { count: n }).catch(() => { });
}

// =====================================================================
// Actions
// =====================================================================

const ACT = {
  cat(key) { S.page = 'items'; S.cat = key; S.limit = 300; S.picked = new Set(); saveUi(); renderAll(); $('#page').scrollTop = 0; },
  page(p) { S.page = p; renderAll(); },
  filter(v) { S.filter = v; S.limit = 300; saveUi(); renderPage(false); renderHeader(); },
  sortBy(key) {
    S.sort = S.sort.key === key ? { key, dir: -S.sort.dir } : { key, dir: key === 'price' || key === 'changed' ? -1 : 1 };
    saveUi(); renderPage(); renderHeader();
  },
  more() { S.limit += 300; renderPage(); },
  select(id, e) {
    if (e?.shiftKey && S.anchor) {
      const ids = S.shown.map(x => x.i);
      const a = ids.indexOf(S.anchor), b = ids.indexOf(id);
      if (a >= 0 && b >= 0) { S.picked = new Set(ids.slice(Math.min(a, b), Math.max(a, b) + 1)); S.sel = id; }
    } else if (e?.ctrlKey || e?.metaKey) {
      if (!S.picked.size && S.sel) S.picked.add(S.sel);
      S.picked.has(id) ? S.picked.delete(id) : S.picked.add(id);
      S.sel = id; S.anchor = id;
    } else { S.picked = new Set(); S.sel = id; S.anchor = id; }
    if (S.picked.size === 1) S.picked = new Set();
    markSelection(); renderDetails();
  },
  selOnly(id) { S.picked = new Set(); S.sel = id; S.anchor = id; markSelection(); renderDetails(); scrollToRow(id); },
  quick(n) { const ids = S.sel ? [S.sel] : []; setLevels(ids, Number(n), null); renderDetails(); },
  quickMany(n) { const k = setLevels([...S.picked], Number(n)); toast(`${k} item(s) set to level ${n}`); },
  async bulkSet() {
    const ids = S.picked.size ? [...S.picked] : S.sel ? [S.sel] : [];
    if (!ids.length) return;
    const v = await promptBox('Set Level', `Level for ${ids.length} item${ids.length === 1 ? '' : 's'} (1–${MAX_LEVEL})`, String(levelOf(ids[0]) ?? 1));
    if (v === null) return;
    const n = Number(v);
    if (!(n >= 1)) return toast('Type a level from 1 to ' + MAX_LEVEL);
    const k = setLevels(ids, n);
    toast(`${k} item(s) set to level ${clamp(Math.round(n), 1, MAX_LEVEL)}`);
  },
  bulkRemove() {
    const ids = S.picked.size ? [...S.picked] : S.sel ? [S.sel] : [];
    const k = setLevels(ids, undefined);
    if (k) toast(`Limit removed from ${k} item(s)`);
  },
  bulkShift(d) {
    const step = [];
    for (const id of S.picked) { const l = levelOf(id); if (l !== undefined) step.push([id, clamp(l + Number(d), 1, MAX_LEVEL)]); }
    // one undo step
    const changes = step.filter(([id, to]) => levelOf(id) !== to).map(([id, to]) => ({ id, from: levelOf(id), to }));
    if (!changes.length) return;
    changes.forEach(c => S.levels.set(c.id, c.to));
    S.undo.push(changes); S.redo = [];
    afterChange(changes.map(c => c.id));
  },
  revert() { const was = S.saved.get(S.sel); setLevels([S.sel], was); renderDetails(); },
  async addId() {
    const v = await promptBox('Add by ID', 'Item id (24 characters, e.g. from a mod\'s files)', '');
    if (!v) return;
    const id = v.trim().toLowerCase();
    if (!/^[0-9a-f]{24}$/.test(id)) return toast('That isn\'t a 24-character item id');
    const lvl = await promptBox('Add by ID', `Level for ${S.items.get(id)?.n || id}`, '1');
    if (lvl === null) return;
    setLevels([id], Number(lvl) || 1);
    S.sel = id; S.picked = new Set();
    renderAll(); scrollToRow(id);
  },
  copyId(id) { navigator.clipboard?.writeText(id).then(() => toast('Id copied'), () => toast(id)); },
  clearSearch() { S.search = ''; $('#search').value = ''; S.limit = 300; renderHeader(); renderPage(false); },
  undo() { undo(false); },
  redo() { undo(true); },
  async save() {
    const list = changes();
    if (!list.length) return toast('Nothing to save');
    if (!S.configFile) return toast('Pick the config file first (Browse…)');
    if (!await reviewChanges(list)) return;
    const set = {}, remove = [];
    for (const c of list) c.to === undefined ? remove.push(c.id) : (set[c.id] = c.to);
    try {
      const r = await host.call('save', { set, remove });
      S.saved = new Map(Object.entries(r.levels || {}));
      S.levels = new Map(S.saved);
      renderAll();
      status(`Saved ${list.length} change(s) at ${new Date().toLocaleTimeString()}. In game: picked up within ~2 s (or F9 → Reload from disk).`);
      toast(`Saved ${list.length} change${list.length === 1 ? '' : 's'}`);
    } catch (e) { errorBox(e); }
  },
  async reload() {
    if (changes().length && !await confirmBox('Unsaved changes', `${changes().length} unsaved change(s) will be lost. Reload anyway?`, 'Reload')) return;
    try { apply(await host.call('reload')); toast('Reloaded'); } catch (e) { errorBox(e); }
  },
  async browse() {
    if (changes().length && !await confirmBox('Unsaved changes', 'Pick another file and lose the unsaved changes?', 'Continue')) return;
    try { const r = await host.call('browse'); if (r) apply(r); } catch (e) { errorBox(e); }
  },
  openFolder() { host.call('openConfigFolder').catch(errorBox); },
  sortMenu(arg, el) { sortMenu(el); },
  shortcuts() { shortcutsBox(); },
  async modsScanAll() { await modCall('modsScanAll'); },
  async modAddFolder() { await modCall('modAddFolder'); },
  async modRescan() { await modCall('modRescan'); toast('Rescanned'); },
  async modRemove(name) {
    if (!await confirmBox('Remove Import', `Stop listing the items of ${name}? Limits on them stay in the file (they'll show under Unknown IDs).`, 'Remove')) return;
    await modCall('modRemove', { name });
  },
};

async function modCall(method, args = {}) {
  try {
    status('Scanning…');
    const r = await host.call(method, args);
    if (!r) { status(''); return; }
    applyItems(r);
    const added = r.added || [];
    status(added.length ? `Imported: ${added.join(', ')}` : method === 'modsScanAll' ? 'No new mods with items found.' : 'Done.');
    renderAll();
  } catch (e) { status(''); errorBox(e); }
}

function markSelection() {
  document.querySelectorAll('#page .row[data-row]').forEach(r => {
    r.classList.toggle('sel', r.dataset.row === S.sel);
    r.classList.toggle('picked', S.picked.has(r.dataset.row));
  });
  const n = S.picked.size;
  const bs = document.querySelector('[data-act=bulkSet]'), br = document.querySelector('#page [data-act=bulkRemove]');
  if (bs) { bs.disabled = !n && !S.sel; bs.textContent = n > 1 ? `Set Level (${n})…` : 'Set Level…'; }
  if (br) { br.disabled = !n && !S.sel; br.textContent = n > 1 ? `Remove Limit (${n})` : 'Remove Limit'; }
}

function scrollToRow(id) {
  const i = S.shown.findIndex(x => x.i === id);
  if (i >= S.limit) { S.limit = i + 50; renderPage(); }
  document.querySelector(`#page .row[data-row="${id}"]`)?.scrollIntoView({ block: 'nearest' });
}

function onHostEvent(m) {
  if (m.event !== 'diskChanged') return;
  const disk = new Map(Object.entries(m.levels || {}));
  const mine = changes();
  S.saved = disk;
  S.levels = new Map(disk);
  for (const c of mine) c.to === undefined ? S.levels.delete(c.id) : S.levels.set(c.id, c.to); // keep your unsaved edits on top
  renderAll();
  $('#diskNote').textContent = `File changed outside the editor at ${new Date().toLocaleTimeString()}${mine.length ? ' — your unsaved edits were kept' : ''}`;
  toast('Updated from the file (changed in game / elsewhere)');
}

// ---- sort / view menu
function menuHtml(sections) {
  return sections.map(sec => `<div class="menu-title">${esc(sec.title)}</div>` +
    sec.items.map(it => `<button class="${it.on ? 'on' : ''}" data-v="${esc(it.key)}">${esc(it.text)}${it.on ? '<span class="check">✓</span>' : ''}</button>`).join('')).join('');
}
function sortMenu(anchor) {
  closePopover();
  const pop = document.createElement('div');
  pop.className = 'popover menu2';
  const draw = () => {
    pop.innerHTML = menuHtml([
      { title: 'Sort By', items: SORTS.map(([k, n]) => ({ key: 'sort:' + k, text: n + (S.sort.key === k ? (S.sort.dir > 0 ? '  ▲' : '  ▼') : ''), on: S.sort.key === k })) },
      { title: 'Show', items: [{ key: 'hidden', text: 'Dev / Hidden Items', on: S.showHidden }] },
    ]);
  };
  draw();
  pop.addEventListener('click', e => {
    const b = e.target.closest('[data-v]');
    if (!b) return;
    const v = b.dataset.v;
    if (v.startsWith('sort:')) ACT.sortBy(v.slice(5));
    else if (v === 'hidden') { S.showHidden = !S.showHidden; saveUi(); renderAll(); }
    draw();
  });
  document.body.appendChild(pop);
  const r = anchor.getBoundingClientRect();
  pop.style.top = (r.bottom + 6) + 'px';
  pop.style.left = Math.max(8, r.right - pop.offsetWidth) + 'px';
  closePopover.fn = e => { if (!pop.contains(e.target) && !anchor.contains(e.target)) closePopover(); };
  setTimeout(() => document.addEventListener('mousedown', closePopover.fn), 0);
}
function closePopover() {
  document.querySelector('.popover')?.remove();
  if (closePopover.fn) document.removeEventListener('mousedown', closePopover.fn);
}

// =====================================================================
// Dialogs
// =====================================================================

let modalResolve = null;
function openModal(html, onOpen) {
  const m = $('#modal');
  m.innerHTML = html;
  m.hidden = false;
  onOpen?.(m);
  return new Promise(res => { modalResolve = res; });
}
function closeModal(value) {
  const m = $('#modal');
  m.hidden = true;
  m.innerHTML = '';
  const r = modalResolve; modalResolve = null;
  r?.(value);
}
function confirmBox(title, message, ok = 'OK', cancel = 'Cancel') {
  return openModal(`<div class="dialog small"><h2>${esc(title)}</h2><div class="req">${esc(message)}</div>
    <div class="buttons"><button class="outline" data-m="0">${esc(cancel)}</button><button class="primary" data-m="1">${esc(ok)}</button></div></div>`,
    m => { m.querySelectorAll('[data-m]').forEach(b => b.onclick = () => closeModal(b.dataset.m === '1')); m.querySelector('[data-m="1"]').focus(); });
}
function errorBox(err) {
  return openModal(`<div class="dialog small"><h2>Something went wrong</h2><div class="req">${esc(err?.message || err)}</div>
    <div class="buttons"><button class="primary" data-m>OK</button></div></div>`, m => m.querySelector('[data-m]').onclick = () => closeModal(null));
}
function promptBox(title, label, initial = '') {
  return openModal(`<div class="dialog small"><h2>${esc(title)}</h2><div class="stack"><label>${esc(label)}</label><input type="text" id="promptInput" value="${esc(initial)}" spellcheck="false"></div>
    <div class="buttons"><button class="outline" data-m="0">Cancel</button><button class="primary" data-m="1">OK</button></div></div>`, m => {
    const input = m.querySelector('#promptInput');
    input.focus(); input.select();
    const done = ok => closeModal(ok && input.value.trim() ? input.value.trim() : null);
    input.onkeydown = e => { if (e.key === 'Enter') done(true); };
    m.querySelectorAll('[data-m]').forEach(b => b.onclick = () => done(b.dataset.m === '1'));
  });
}
function reviewChanges(list) {
  const kinds = { add: [], edit: [], del: [] };
  for (const c of list) (c.from === undefined ? kinds.add : c.to === undefined ? kinds.del : kinds.edit).push(c);
  const line = (c, sign, color, text) => `<div class="chg"><span class="chg-i" style="color:${color}">${sign}</span><div>${esc(itemOf(c.id).n)} <span class="muted">${text}</span></div></div>`;
  const section = (title, arr, fn) => arr.length ? `<div class="chg-trader"><h3>${title} <span class="muted small">${arr.length}</span></h3>${arr.slice(0, 300).map(fn).join('')}${arr.length > 300 ? `<div class="muted small">… and ${arr.length - 300} more</div>` : ''}</div>` : '';
  return openModal(`<div class="dialog"><h2>Save These Changes?</h2>
      <div class="muted small" style="margin-bottom:10px">${list.length} change${list.length === 1 ? '' : 's'} to ${esc(S.configFile)}</div>
      <div class="chg-list">
        ${section('New Limits', kinds.add, c => line(c, '+', 'var(--green)', `→ Level ${c.to}`))}
        ${section('Changed', kinds.edit, c => line(c, '•', 'var(--orange)', `Level ${c.from} → ${c.to}`))}
        ${section('Limits Removed', kinds.del, c => line(c, '−', 'var(--red)', `was Level ${c.from}`))}
      </div>
      <div class="buttons"><button class="outline" data-m="0">Cancel</button><button class="primary" data-m="1">Save</button></div></div>`,
    m => { m.querySelectorAll('[data-m]').forEach(b => b.onclick = () => closeModal(b.dataset.m === '1')); m.querySelector('[data-m="1"]').focus(); });
}
const SHORTCUTS = [
  ['Ctrl+S', 'Save (shows the changes first)'], ['Ctrl+Z / Ctrl+Y', 'Undo / redo'], ['Ctrl+F', 'Search'],
  ['↑ / ↓', 'Previous / next item'], ['Enter', 'Type the selected item\'s level'], ['1 – 9', 'Start typing a level for the selected item'],
  ['Del', 'Remove the limit of the selected / picked items'], ['Ctrl+A', 'Pick every item shown'], ['Ctrl-click / Shift-click', 'Pick several'],
  ['Esc', 'Clear the search / picks'], ['F1', 'This list'],
];
function shortcutsBox() {
  openModal(`<div class="dialog small"><h2>Keyboard Shortcuts</h2><div class="keys-grid">${SHORTCUTS.map(([k, t]) => `<kbd>${esc(k)}</kbd><span>${esc(t)}</span>`).join('')}</div>
    <div class="buttons"><button class="primary" data-m>OK</button></div></div>`, m => m.querySelector('[data-m]').onclick = () => closeModal(null));
}

let toastTimer = 0;
function toast(text) {
  const t = $('#toast');
  t.textContent = text;
  t.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove('show'), 2200);
}
function status(text) { $('#status').textContent = text; }

// =====================================================================
// Events
// =====================================================================

document.addEventListener('click', e => {
  const el = e.target.closest('[data-act]');
  if (el && !el.disabled) {
    const fn = ACT[el.dataset.act];
    if (fn) { e.preventDefault(); fn(el.dataset.arg, el); }
    return;
  }
  const row = e.target.closest('#page .row[data-row]');
  if (row && !e.target.closest('input')) ACT.select(row.dataset.row, e);
});

// level typed in the list
document.addEventListener('input', e => {
  const el = e.target;
  if (el.id === 'search') {
    $('#searchBox').classList.toggle('has', !!el.value);
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => { S.search = el.value; S.limit = 300; renderPage(false); renderHeader(); }, 120);
    return;
  }
  if (!el.dataset.lvl) return;
  // live while typing; the undo step is made when the box is left (change)
  const id = el.dataset.lvl;
  if (el.value === '') S.levels.delete(id);
  else { const n = Number(el.value); if (!(n >= 1)) return; S.levels.set(id, clamp(Math.round(n), 1, MAX_LEVEL)); }
  afterChange([id]);
});
document.addEventListener('focusin', e => {
  const el = e.target;
  if (!el.dataset?.lvl) return;
  const id = el.dataset.lvl;
  el.dataset.start = levelOf(id) ?? '';
  if (S.sel !== id && !S.picked.has(id)) { S.picked = new Set(); S.sel = id; S.anchor = id; markSelection(); renderDetails(); }
});
document.addEventListener('change', e => {
  const el = e.target;
  if (el.dataset?.mod) { host.call('modSet', { name: el.dataset.mod, enabled: el.checked }).then(r => { applyItems(r); renderAll(); }).catch(errorBox); return; }
  if (!el.dataset?.lvl) return;
  const id = el.dataset.lvl;
  const from = el.dataset.start === '' || el.dataset.start === undefined ? undefined : Number(el.dataset.start), to = levelOf(id);
  if (from !== to) { S.undo.push([{ id, from, to }]); S.redo = []; renderBottom(); }
  el.dataset.start = to ?? '';
  el.value = to ?? '';
});
let searchTimer = 0;
$('#searchBox').addEventListener('mousedown', e => {
  if (e.target.closest('button') || e.target === $('#search')) return;
  e.preventDefault();
  searchOpen(true);
  setTimeout(() => $('#search').focus(), 0);
});
const searchOpen = on => { $('#searchBox').classList.toggle('open', on); document.body.classList.toggle('search-open', on); };
$('#search').addEventListener('focus', () => searchOpen(true));
$('#search').addEventListener('blur', () => { if (!$('#search').value) searchOpen(false); });
$('#search').addEventListener('keydown', e => {
  if (e.key === 'Escape') { e.stopPropagation(); ACT.clearSearch(); e.target.blur(); }
  if (e.key === 'Enter') e.target.blur();
});

// infinite list: more rows as you scroll down
$('#page').addEventListener('scroll', () => {
  const p = $('#page');
  if (S.page === 'items' && S.shown.length > S.limit && p.scrollTop + p.clientHeight > p.scrollHeight - 600) { S.limit += 300; renderPage(); }
});

document.addEventListener('keydown', e => {
  const k = e.key, ctrl = e.ctrlKey || e.metaKey;
  const typing = e.target.matches?.('input[type=text], textarea') || (e.target.matches?.('input') && !e.target.dataset?.lvl && e.target.type !== 'range');
  if (!$('#modal').hidden) { if (k === 'Escape') closeModal(null); return; }
  if (k === 'F1') { e.preventDefault(); shortcutsBox(); return; }
  if (ctrl && k.toLowerCase() === 's') { e.preventDefault(); ACT.save(); return; }
  if (ctrl && k.toLowerCase() === 'f') { e.preventDefault(); searchOpen(true); $('#search').focus(); $('#search').select(); return; }
  if (typing) return;
  const inLevel = !!e.target.dataset?.lvl;
  if (ctrl && k.toLowerCase() === 'z' && !inLevel) { e.preventDefault(); undo(e.shiftKey); return; }
  if (ctrl && k.toLowerCase() === 'y' && !inLevel) { e.preventDefault(); undo(true); return; }
  if (S.page !== 'items') return;
  if (ctrl && k.toLowerCase() === 'a' && !inLevel) { e.preventDefault(); S.picked = new Set(S.shown.slice(0, S.limit).map(x => x.i)); markSelection(); renderDetails(); renderPage(); return; }
  if (k === 'ArrowDown' || k === 'ArrowUp') {
    e.preventDefault();
    const ids = S.shown.map(x => x.i);
    let i = ids.indexOf(S.sel);
    i = clamp(i + (k === 'ArrowDown' ? 1 : -1), 0, ids.length - 1);
    if (ids[i]) {
      ACT.selOnly(ids[i]);
      if (inLevel) document.querySelector(`#page .lvl-in[data-lvl="${ids[i]}"]`)?.focus();
    }
    return;
  }
  if (inLevel) { if (k === 'Enter' || k === 'Escape') e.target.blur(); return; }
  if (k === 'Escape') { if (S.picked.size) { S.picked = new Set(); markSelection(); renderDetails(); } else if (S.search) ACT.clearSearch(); return; }
  if ((k === 'Delete' || k === 'Backspace') && (S.sel || S.picked.size)) { e.preventDefault(); ACT.bulkRemove(); return; }
  if (k === 'Enter' && S.sel) { e.preventDefault(); document.querySelector(`#page .lvl-in[data-lvl="${S.sel}"]`)?.focus(); return; }
  if (/^[0-9]$/.test(k) && S.sel && !ctrl) {
    const input = document.querySelector(`#page .lvl-in[data-lvl="${S.sel}"]`);
    if (input) { e.preventDefault(); input.focus(); input.value = k; input.dispatchEvent(new Event('input', { bubbles: true })); }
  }
});

// panel widths
let drag = null;
document.addEventListener('mousedown', e => {
  if (!e.target.classList.contains('splitter')) return;
  const left = e.target.id === 'splitLeft';
  drag = { left, x: e.clientX, w: $(left ? '#left' : '#right').getBoundingClientRect().width, el: e.target };
  e.target.classList.add('drag');
  e.preventDefault();
});
document.addEventListener('mousemove', e => {
  if (!drag) return;
  if (drag.left) { const w = clamp(drag.w + e.clientX - drag.x, 200, 420); S.ui.left = w; document.documentElement.style.setProperty('--left', w + 'px'); }
  else { const w = clamp(drag.w + drag.x - e.clientX, 340, 900); S.ui.right = w; document.documentElement.style.setProperty('--right', w + 'px'); }
});
document.addEventListener('mouseup', () => { if (!drag) return; drag.el.classList.remove('drag'); drag = null; saveUi(); });
document.addEventListener('dblclick', e => {
  if (!e.target.classList.contains('splitter')) return;
  if (e.target.id === 'splitLeft') { delete S.ui.left; document.documentElement.style.setProperty('--left', '270px'); }
  else { delete S.ui.right; document.documentElement.style.setProperty('--right', '460px'); }
  saveUi();
});

start();
