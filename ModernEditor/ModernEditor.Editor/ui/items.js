/* Modern Editor — the item pages: Level Limits, Item Stats and Progression (the former Level & Item Editor).
   Everything here lives in its own scope, drawn into the shell's panels (app.js: #page, #details, the
   left panel's #lgNav) while one of those pages is open. Its listeners only run then; the shell's own
   ones stand aside meanwhile (see lgActive() in app.js). Files:
     BepInEx\plugins\LevelGate\config\level_requirements.json — Level Gate's file (its layout is kept)
     user\mods\ModernEditor\item_stats.json · user\mods\ModernEditor\disabled_levels.json */
'use strict';

const shellHost = host;              // app.js's bridge
const shellRenderBottom = () => renderBottom();   // app.js's bottom bar (total of every page)
const shellOpenModal = openModal, shellCloseModal = closeModal, shellConfirm = confirmBox, shellError = errorBox, shellPrompt = promptBox, shellToast = toast, shellStatus = status;

const LG = (() => {
  const active = () => lgActive();
  // document listeners of this scope run only while an item page is open
  const realDoc = window.document, wrapped = new WeakMap();
  const document = new Proxy(realDoc, {
    get(t, k) {
      if (k === 'addEventListener') return (type, fn, opt) => { const w = e => { if (active()) fn(e); }; wrapped.set(fn, w); t.addEventListener(type, w, opt); };
      if (k === 'removeEventListener') return (type, fn, opt) => t.removeEventListener(type, wrapped.get(fn) || fn, opt);
      const v = t[k];
      return typeof v === 'function' ? v.bind(t) : v;
    },
  });
  const openModal = shellOpenModal, closeModal = shellCloseModal, confirmBox = shellConfirm, errorBox = shellError, promptBox = shellPrompt, toast = shellToast, status = shellStatus;
  /** Switches pages through the shell (keep = keep the pick and filters set just before). */
  const goTab = (t, keep) => { S.keepSel = !!keep; shellShowPage(t); };
  /** Levels changed (typed, undone, loaded, changed in game): quests that follow an item's level follow it (app.js). */
  const shellLevelsChanged = () => { try { if (typeof lgLevelsChanged === 'function') lgLevelsChanged(); } catch (e) { console.error(e); } };


const $ = s => document.querySelector(s);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const fmt = n => Math.round(n).toLocaleString('en-US');
const MAX_LEVEL = 79;

// =====================================================================
// Host bridge: the shell's (app.js); the item pages' own methods start with "lg."
// =====================================================================

const LG_METHODS = new Set(['init', 'reload', 'browse', 'save', 'saveStats', 'saveUi', 'openConfigFolder', 'openStatsFolder', 'saveDisabled']);
const host = { call: (method, args = {}) => shellHost.call(LG_METHODS.has(method) ? 'lg.' + method : method, args) };


// =====================================================================
// State
// =====================================================================

/** Categories = the item groups of the Custom Trader Creator (key, name, color). */
const GROUPS = [
  ['Weapons', 'Weapons', '#f15e6c'], ['Melee', 'Melee', '#ff8a65'], ['Grenades', 'Grenades', '#ffa42b'], ['Ammo', 'Ammo', '#f5cd46'], ['AmmoPacks', 'Ammo Packs', '#d9b44a'],
  ['WeaponParts', 'Weapon Parts', '#c7a36b'], ['Armor', 'Armor', '#509bf5'], ['Headwear', 'Headwear', '#6fb3ff'], ['Rigs', 'Rigs', '#7d9cf0'],
  ['Backpacks', 'Backpacks', '#a082ff'], ['Gear', 'Other Gear', '#b39ddb'], ['Medical', 'Medical', '#1ed760'], ['Food', 'Food & Drink', '#8bd66b'],
  ['Electronics', 'Electronics', '#4dd0e1'], ['Barter', 'Barter Items', '#bdbdbd'], ['Keys', 'Keys', '#e0c068'], ['Containers', 'Containers', '#90a4ae'],
  ['Special', 'Special', '#ff7ab6'], ['Other', 'Other', '#8a8a8a'],
];
const GROUP = Object.fromEntries(GROUPS.map(([k, n, c]) => [k, { key: k, name: n, color: c }]));
const SORTS = [['custom', 'Custom Order'], ['name', 'Name'], ['level', 'Level'], ['cat', 'Category'], ['price', 'Price'], ['changed', 'Changed First']];

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
  // Item Stats tab (meds, stims, food): base = game + other mods, edits = LevelGate's item_stats.json
  tab: 'levels', statCat: 'all', band: null, statSort: { key: 'name', dir: 1 },
  meds: {}, medsDefault: {}, sortStale: false, statEdits: {}, statSaved: {}, statsFile: null, serverMod: false, statSources: [],
};

// =====================================================================
// Start
// =====================================================================

/** Called by the shell once its snapshot (the item list) is in. */
async function start(itemsSnap) {
  try {
    const snap = await host.call('init');
    if (itemsSnap) applyItems(itemsSnap);
    apply(snap);
  } catch (e) { status('Level Gate: ' + e.message); }
}

function apply(snap) {
  S.configFile = snap.configFile; S.sptRoot = snap.sptRoot; S.version = snap.version || S.version;
  if (snap.ui && !S.uiLoaded) { S.ui = snap.ui; S.uiLoaded = true; restoreUi(); }
  S.saved = new Map(Object.entries(snap.levels || {}));
  S.levels = new Map(S.saved);
  S.undo = []; S.redo = [];
  applyItems(snap);
  S.levelGateVersion = snap.levelGateVersion || null;
  if (snap.disabled) { S.disabled = new Map(Object.entries(snap.disabled)); S.disabledSaved = new Map(S.disabled); }
  if (snap.disabledFile !== undefined) S.disabledFile = snap.disabledFile;
  S.notInGame = !!snap.notInGame;
  status(!S.configFile ? 'LevelGate\'s config wasn\'t found — click Browse… and pick SPT\\BepInEx\\plugins\\LevelGate\\config\\level_requirements.json.'
    : snap.notInGame ? '⚠ This file isn\'t inside an SPT folder, so the game never reads it — Browse… to SPT\\BepInEx\\plugins\\LevelGate\\config\\level_requirements.json.'
    : `Loaded ${S.saved.size} level limit(s).`);
  renderAll();
  shellLevelsChanged();
}

/** Items come with the shell's snapshot: money isn't listed here, and ammo packs have their own category (lg). */
const lgItem = it => (it.lg ? { ...it, g: it.lg } : it);
function applyItems(snap) {
  if (snap.items) S.items = new Map(snap.items.filter(it => it.c !== 'Money').map(it => [it.i, lgItem(it)]));
  if (snap.modItemsOff) snap = { ...snap, modItemsOff: snap.modItemsOff.map(lgItem) };
  if (snap.mods) S.mods = snap.mods;
  if (snap.modItemsOff) S.modItemsOff = snap.modItemsOff;
  if (snap.itemsStatus !== undefined) $('#itemsStatus').textContent = snap.itemsStatus;
  if (snap.meds) S.meds = snap.meds;
  if (snap.medsDefault) S.medsDefault = snap.medsDefault;
  if (snap.statSources) S.statSources = snap.statSources;
  if (snap.statsFile !== undefined) S.statsFile = snap.statsFile;
  if (snap.statsReadFrom !== undefined) S.statsReadFrom = snap.statsReadFrom;
  if (snap.serverMod !== undefined) S.serverMod = !!snap.serverMod;
  if (snap.statEdits) { S.statEdits = JSON.parse(JSON.stringify(snap.statEdits)); S.statSaved = JSON.parse(JSON.stringify(snap.statEdits)); }
}

function restoreUi() {
  const u = S.ui;
  if (typeof progRestore === "function") progRestore();
  if (u.cat) S.cat = u.cat;
  if (u.filter) S.filter = u.filter;
  if (u.sort) S.sort = u.sort;
  if (u.statCat) S.statCat = u.statCat;
  if (u.statSort) S.statSort = u.statSort;
  S.showHidden = !!u.showHidden;
}
let uiTimer = 0;
function saveUi() {
  Object.assign(S.ui, { cat: S.cat, filter: S.filter, sort: S.sort, showHidden: S.showHidden, tab: S.tab, statCat: S.statCat, statSort: S.statSort });
  clearTimeout(uiTimer);
  uiTimer = setTimeout(() => host.call('saveUi', { ui: S.ui }).catch(() => { }), 400);
}

// =====================================================================
// Items, levels, changes
// =====================================================================

const levelOf = id => S.levels.get(id);
const isLimited = id => S.levels.has(id);
/** Category, or the one you moved the item to (kept in the editor's own settings only). */
const groupOf = it => { const moved = S.ui.catMove?.[it.i]; return moved && GROUP[moved] ? moved : GROUP[it.g] ? it.g : 'Other'; };
/** Flea or handbook price, picked with the toggle in the Price header (kept in the editor settings). */
const fleaMode = () => S.ui.priceMode !== 'hb';
const priceOf = it => (fleaMode() ? it.f : it.h) || 0;
const priceCell = it => { const p = priceOf(it), other = fleaMode() ? it.h : it.f;
  return `<div class="col num" title="${fleaMode() ? 'Flea' : 'Handbook'}: ${p ? fmt(p) + ' ₽' : 'none'} · ${fleaMode() ? 'Handbook' : 'Flea'}: ${other ? fmt(other) + ' ₽' : 'none'}">${p ? fmt(p) + ' ₽' : '—'}</div>`; };
const priceToggle = () => `<button class="pmode" data-act="priceMode" title="Show flea or handbook (trader) prices — click to switch">${fleaMode() ? 'Flea' : 'Handbook'}</button>`;

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
const isChanged = id => S.saved.get(id) !== S.levels.get(id) || S.disabledSaved.get(id) !== S.disabled.get(id);

// ---- switched-off limits: out of Level Gate's file (the item is free), the level kept in user\mods\ModernEditor\disabled_levels.json
const isDisabled = id => S.disabled.has(id);
/** Switches limits off (off = true) or back on, as one undo step. */
function setDisabled(ids, off) {
  const dis = [];
  for (const id of ids) {
    const l = off ? S.levels.get(id) : S.disabled.get(id);
    if (l === undefined) continue;
    dis.push({ id, level: l, off });
    if (off) { S.levels.delete(id); S.disabled.set(id, l); } else { S.disabled.delete(id); S.levels.set(id, l); }
  }
  if (!dis.length) return 0;
  S.undo.push({ dis }); S.redo = [];
  afterChange(dis.map(x => x.id));
  renderPage();
  return dis.length;
}

/** Sets levels (undefined = no limit) as one undo step. */
function setLevels(ids, level, label) {
  const step = [];
  for (const id of ids) {
    const from = S.levels.get(id);
    const to = level === undefined || level === null || level === '' ? undefined : clamp(Math.round(Number(level)), 1, MAX_LEVEL);
    if (from === to) continue;
    const wasDis = to !== undefined && S.disabled.has(id) ? S.disabled.get(id) : undefined; // a level set on a switched-off item switches it on
    step.push({ id, from, to, wasDis });
    if (wasDis !== undefined) S.disabled.delete(id);
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
  if (step.stat) { undoStat(step, redo); to.push(step); return; }
  if (step.dis) {
    for (const c of step.dis) {
      const off = redo ? c.off : !c.off;
      if (off) { S.levels.delete(c.id); S.disabled.set(c.id, c.level); } else { S.disabled.delete(c.id); S.levels.set(c.id, c.level); }
    }
    to.push(step);
    afterChange(step.dis.map(x => x.id)); renderPage();
    toast(`${redo ? 'Redone' : 'Undone'}: ${step.dis.length} limit${step.dis.length === 1 ? '' : 's'} switched ${(redo ? step.dis[0].off : !step.dis[0].off) ? 'off' : 'on'}`);
    return;
  }
  for (const c of step) {
    const v = redo ? c.to : c.from;
    if (v === undefined) S.levels.delete(c.id); else S.levels.set(c.id, v);
    if (c.wasDis !== undefined) { if (redo) S.disabled.delete(c.id); else S.disabled.set(c.id, c.wasDis); }
  }
  to.push(step);
  afterChange(step.map(x => x.id));
  toast(`${redo ? 'Redone' : 'Undone'}: ${step.length} change${step.length === 1 ? '' : 's'}`);
}

/** Redraws what a level change touches without rebuilding the list (keeps focus while typing). */
function afterChange(ids) {
  for (const id of ids) updateRow(id);
  if (['level', 'changed'].includes(S.sort.key) || S.filter !== 'all' || S.band) markStale();
  renderNav(); renderHeader(); renderBottom();
  shellLevelsChanged();
  // (not while typing a level in the details — that box would be redrawn under the cursor)
  if ((ids.includes(S.sel) || S.picked.size > 1) && !document.activeElement?.closest?.('#details .lvl')) renderDetails();
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
  for (const it of S.items.values()) if ((S.showHidden || !it.x || isLimited(it.i) || isDisabled(it.i)) && inCat(it, cat)) list.push(it);
  if (cat === 'all') for (const id of unknownIds()) list.push(itemOf(id));
  return list;
}

function matches(it, q) {
  if (!q) return true;
  return it.i.includes(q) || (it.n || '').toLowerCase().includes(q) || (it.s || '').toLowerCase().includes(q) || (it.m || '').toLowerCase().includes(q);
}

function shownItems() {
  const q = S.search.trim().toLowerCase();
  let list = catItems(S.cat).filter(it => matches(it, q) && tagPass(it.i));
  if (S.filter === 'limited') list = list.filter(it => isLimited(it.i));
  else if (S.filter === 'disabled') list = list.filter(it => isDisabled(it.i));
  else if (S.filter === 'free') list = list.filter(it => !isLimited(it.i));
  if (S.band) list = list.filter(it => { const l = levelOf(it.i); return l !== undefined && l >= S.band[0] && l <= S.band[1]; });
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
    custom: customCmp('levels'),
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
  if (!active()) return;
  document.body.classList.toggle('prog-mode', S.tab === 'prog');
  $('#lgNavBox').hidden = S.tab === 'prog';
  if (S.tab === 'prog') return;
  if (S.tab === 'stats') { statsNav(); return; }
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
  $('#lgNav').innerHTML = html;
  $('#lgNavTitle').textContent = 'Categories';
}

function catName(cat) {
  return cat === 'all' ? 'All Items' : cat === 'modded' ? 'Modded Items' : cat === 'unknown' ? 'Unknown IDs' : GROUP[cat]?.name || cat;
}
function catColor(cat) {
  return cat === 'all' ? '#5b5b5b' : cat === 'modded' ? '#ff7ab6' : cat === 'unknown' ? '#f15e6c' : GROUP[cat]?.color || '#5b5b5b';
}

function renderHeader() {
  if (!active()) return;
  const mods = S.page === 'mods';
  if (S.tab === 'prog') {
    $('#headerSub').textContent = `${fmt(S.levels.size)} limited items · Levels 1–${MAX_LEVEL} · click a level, or ← → / Q E`;
  } else if (S.tab === 'stats') {
    const ids = statIds('all');
    $('#headerSub').textContent = `${fmt(ids.length)} meds, stims & food · ${Object.keys(S.statEdits).length} edited by you` +
      (S.statSources.length ? ` · also changed by: ${S.statSources.join(', ')}` : '');
  } else if (mods) {
    $('#headerSub').textContent = `${S.mods.length} mod${S.mods.length === 1 ? '' : 's'} imported · their items show in the categories (switch a mod off to hide its items)`;
  } else {
    const list = catItems(S.cat);
    const lv = list.map(it => levelOf(it.i)).filter(x => x !== undefined);
    $('#headerSub').textContent = `${catName(S.cat)} · ${fmt(lv.length)} limited of ${fmt(list.length)} item${list.length === 1 ? '' : 's'}` +
      (lv.length ? ` · Levels ${Math.min(...lv)}–${Math.max(...lv)}` : '');
  }
  $('#chips').hidden = mods;

  $('#viewLabel').textContent = S.tab === 'stats' ? (STAT_SORTS.find(s => s[0] === S.statSort.key) || STAT_SORTS[1])[1] : (SORTS.find(s => s[0] === S.sort.key) || SORTS[1])[1];
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
  if (lvl === undefined && isDisabled(id))
    return `<div class="lvl free disabled"><input type="number" class="lvl-in" data-lvl="${id}" min="1" max="${MAX_LEVEL}" step="1" placeholder="(${S.disabled.get(id)})" value="" title="Switched off — was level ${S.disabled.get(id)} (right-click › Switch Limit On puts it back; typing a level sets a new one)"></div>`;
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
    isDisabled(id) ? `<span class="badge keep" style="--c:#8a8a8a" title="Its limit is switched off (not in Level Gate's file) — switch it back on to restore level ${S.disabled.get(id)}">LIMIT OFF · LVL ${S.disabled.get(id)}</span>` : '',
    usedBadge(id),
    tagBadges(id),
  ].join('');
  return `<div class="row ${id === S.sel ? 'sel' : ''} ${S.picked.has(id) ? 'picked' : ''} ${isChanged(id) ? 'changed' : ''}" data-row="${id}">
    <div class="idx">${n}</div>
    <div class="cell">${icon(it)}<div class="text"><div class="line1"><span class="title">${esc(it.n)}</span><span class="dirty" title="Changed — not saved yet">•</span><div class="badges">${badges}</div></div>
      <div class="line2">${esc(it.s || '')}${it.s ? ' · ' : ''}<span class="mono">${id}</span></div></div></div>
    <div class="col"><i class="dot" style="--c:${g?.color || '#888'}"></i>${esc(g?.name || 'Other')}</div>
    <div class="col stats chips-col" title="${esc(statsShort(it))}">${statChips(it)}</div>
    ${noteCell(id)}
    ${priceCell(it)}
    ${levelCell(id)}
  </div>`;
}

/** The list is in an old order (a level changed while sorted by level): the Re-sort button lights up. */
function markStale() {
  S.sortStale = true;
  const b = $('#resortBtn');
  if (b) { b.disabled = false; b.classList.add('stale'); }
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
  // [column key, title, sort key]
  const cols = [['', '#', 'custom'], ['item', 'Item', 'name'], ['cat', 'Category', 'cat'], ['stats', 'Stats', ''], ['notes', 'Notes', ''], ['price', 'Price', 'price'], ['level', 'Level', 'level']];
  return `<div class="list-head">${cols.map(([c, t, k]) => `<div>${c ? grip('lg', c) : ''}${k
    ? `<span class="head-text sortable ${S.sort.key === k ? 'on' : ''}" data-act="sortBy" data-arg="${k}">${t}${S.sort.key === k && k !== 'custom' ? `<span class="sort-arrow">${S.sort.dir > 0 ? '▲' : '▼'}</span>` : ''}</span>${k === 'price' ? priceToggle() : ''}`
    : `<span class="head-text">${t}</span>`}</div>`).join('')}</div>`;
}

function renderPage(keepScroll = true) {
  if (!active()) return;
  const page = $('#page');
  const top = page.scrollTop;
  if (S.page === 'mods') { page.innerHTML = modsPage(); if (!keepScroll) page.scrollTop = 0; return; }
  if (S.tab === 'prog') { page.innerHTML = progPage(); page.scrollTop = 0; return; }
  if (S.tab === 'stats') { page.innerHTML = statsPage(); page.scrollTop = keepScroll ? top : 0; return; }
  const list = S.shown = shownItems();
  S.sortStale = false;
  const rows = list.slice(0, S.limit).map((it, i) => rowHtml(it, i + 1)).join('');
  const n = S.picked.size;
  const chip = (v, t) => `<button class="chip ${S.filter === v ? 'on' : ''}" data-act="filter" data-arg="${v}">${t}</button>`;
  page.innerHTML = `<div class="toolbar sticky">
      ${chip('all', 'All')}${chip('limited', 'Limited')}${chip('free', 'Not Limited')}${S.disabled.size || S.filter === 'disabled' ? chip('disabled', `Switched Off (${S.disabled.size})`) : ''}
      ${S.band ? `<button class="chip on" style="--c:var(--accent)" data-act="band" title="Showing only these levels — click to show all">Levels ${S.band[0]}–${S.band[1]} ✕</button>` : ''}
      <span class="sep"></span>
      <button class="outline" data-act="bulkSet" ${n ? '' : 'disabled'} title="Set one level for every picked item">${n > 1 ? `Set Level (${n})…` : 'Set Level…'}</button>
      <button class="danger" data-act="bulkRemove" ${n ? '' : 'disabled'} title="Del">${n > 1 ? `Remove Limit (${n})` : 'Remove Limit'}</button>
      <button class="outline" id="offBtn" data-act="disableLimits" title="Take the picked limits out of Level Gate's file for now; Switch On puts the same levels back">Switch Off</button>
      <button class="outline" id="onBtn" data-act="enableLimits" title="Put the switched-off limits back into Level Gate's file">Switch On</button>
      <button class="primary" id="batchBtn" data-act="batch" title="Make offers / barters and unlock quests for the picked items, by their level">⚡ Barters / Quests…</button>
      <button class="outline" data-act="addId" title="Add a limit by item id (for items not in the list)">+ Add by ID</button>
      <button class="outline resort ${S.sortStale ? 'stale' : ''}" id="resortBtn" data-act="resort" ${S.sortStale ? '' : 'disabled'} title="Sort the list again (levels you changed stay where they are until then)">⟳ Re-sort</button>
      <span class="muted small">${fmt(list.length)} shown${n ? ` · ${n} picked` : ''}</span>
    </div>${tagBar(catItems(S.cat).filter(it => matches(it, S.search.trim().toLowerCase())).map(it => it.i))}
    <div class="list lg" data-list="items" data-cols="lg" style="--cols:${colsCss('lg')}">${listHead()}${rows || `<div class="empty">${S.items.size || S.levels.size ? 'Nothing matches.' : 'No items loaded — pick the config file with Browse… (the item list comes from the SPT folder above it).'}</div>`}
    ${list.length > S.limit ? `<button class="more" data-act="more">Show ${fmt(Math.min(300, list.length - S.limit))} more (${fmt(list.length - S.limit)} left)</button>` : ''}</div>`;
  toolbarPicks();
  page.scrollTop = keepScroll ? top : 0;
}

// ---- right panel
function renderDetails() {
  if (!active()) return;
  const d = $('#details');
  const picked = [...S.picked];
  if (S.page === 'mods') { $('#detailsTitle').textContent = 'Mods'; d.innerHTML = modsHelp(); return; }
  if (S.tab === 'prog') return;
  if (S.tab === 'stats') { statsDetails(); return; }
  if (picked.length > 1) { $('#detailsTitle').textContent = `${picked.length} Items Picked`; d.innerHTML = overall() + multiDetails(picked) + tagCard(picked); return; }
  if (!S.sel) {
    $('#detailsTitle').textContent = 'Level Limits';
    d.innerHTML = overall(true) + welcome();
    return;
  }
  const it = itemOf(S.sel);
  const lvl = levelOf(it.i), was = S.saved.get(it.i);
  const g = GROUP[groupOf(it)];
  $('#detailsTitle').textContent = it.n;
  d.innerHTML = overall() + `
    <div class="card hero">${icon(it, true)}
      <div class="hero-text"><div class="kind" style="color:${g?.color}">${esc((g?.name || 'Other').toUpperCase())}</div>
        <div class="hero-name">${esc(it.s || it.n)}</div>
        <div class="muted small mono copy" data-act="copyId" data-arg="${it.i}" title="Click to copy">${it.i} ⧉</div></div></div>
    <div class="card level-card">
      <h3>Unlocks At</h3>
      <div class="big-level ${lvl === undefined ? 'free' : ''}">${lvl === undefined ? (isDisabled(it.i) ? `Switched Off <span class="muted small">(was level ${S.disabled.get(it.i)})</span>` : 'No Limit') : `Level <b>${lvl}</b>`}</div>
      <input type="range" class="slider" id="lvlSlider" min="1" max="${MAX_LEVEL}" value="${lvl ?? 1}" ${lvl === undefined ? 'disabled' : ''}>
      <div class="quick">${[1, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50].map(n => `<button class="chip ${lvl === n ? 'on' : ''}" data-act="quick" data-arg="${n}">${n}</button>`).join('')}</div>
      <div class="toolbar" style="padding:8px 0 0">
        ${isDisabled(it.i) ? `<button class="primary" data-act="enableLimits">Switch On (Level ${S.disabled.get(it.i)})</button>` : lvl === undefined ? '<button class="primary" data-act="quick" data-arg="1">Add a Limit</button>' : '<button class="danger" data-act="bulkRemove">Remove Limit</button><button class="outline" data-act="disableLimits" title="Take it out of Level Gate\'s file for now; Switch On puts this level back">Switch Off</button>'}
        ${isChanged(it.i) ? `<button class="outline" data-act="revert" title="Back to what's saved">Revert (${was === undefined ? 'no limit' : 'Lvl ' + was})</button>` : ''}
      </div>
      <div class="hint">${lvl === undefined ? 'Anyone can use it.' : lvl === 1 ? 'Level 1 = usable from the start, but tracked (green stripes).' : `Players below level ${lvl} can't use, equip or load it (red stripes); from level ${lvl} on it's unlocked (green).`}</div>
    </div>
    ${usedByCard(it.i)}
    ${statsCard(it)}
    ${noteCard(it.i)}
    ${tagCard([it.i])}
    <div class="card">
      <h3>Item</h3>
      <div class="field"><label>Name</label><div>${esc(it.n)}</div></div>
      <div class="field"><label>Short Name</label><div>${esc(it.s || '—')}</div></div>
      <div class="field"><label>Category</label><div class="cat-pick"><select data-catmove="${it.i}" title="Move it to another category (only in this editor — nothing in the game or other mods changes)">${GROUPS.map(([k, n]) => `<option value="${k}" ${groupOf(it) === k ? 'selected' : ''}>${esc(n)}</option>`).join('')}</select>${S.ui.catMove?.[it.i] ? `<span class="muted small">moved · <a href="#" data-act="catMoveBack" data-arg="${it.i}">back to ${esc(GROUP[GROUP[it.g] ? it.g : 'Other'].name)}</a></span>` : ''}${it.k ? `<span class="muted small">${esc(it.k.replace(/^Caliber/, ''))}</span>` : ''}</div></div>
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
      <div class="toolbar" style="padding:8px 0 0">
        <button class="outline" data-act="disableLimits" ${ids.some(isLimited) ? '' : 'disabled'} title="Out of Level Gate's file for now (the levels are kept)">Switch Off ${ids.filter(isLimited).length}</button>
        <button class="outline" data-act="enableLimits" ${ids.some(isDisabled) ? '' : 'disabled'}>Switch On ${ids.filter(isDisabled).length}</button></div>
    </div>
    <div class="card"><h3>Make Barters / Quests</h3><div class="hint">Offers (money or barter) and unlock quests for all ${ids.length} items — each at its own level, optionally following it when it changes.</div>
      <div class="toolbar" style="padding:6px 0 0"><button class="primary big-btn" data-act="batch">⚡ Make Barters / Quests…</button></div></div>
    <div class="card"><h3>Picked</h3><div class="mini">${ids.slice(0, 200).map(id => { const it = itemOf(id); const l = levelOf(id); return `<div class="row" data-act="selOnly" data-arg="${id}"><div class="cell">${icon(it)}<div class="text"><div class="title">${esc(it.n)}</div></div><span class="side">${l === undefined ? 'no limit' : 'Lvl ' + l}</span></div></div>`; }).join('')}</div></div>`;
}

function welcome() {
  return `<div class="card"><h3>How It Works</h3>
      <div class="hint">Click an item to set the level it unlocks at, or type it straight into the Level column (empty = no limit). Ctrl / Shift-click to pick several.</div></div>
    <div class="card"><h3>While You Play</h3>
      <div class="hint">Save here and LevelGate picks the file up in game within a couple of seconds (or press F9 → Reload from disk). Reopen the inventory to refresh item names. Changes made in the game's F9 window show up here too.</div></div>`;
}

/** Overall: how many items unlock at which levels. Always on top of the right panel; click a band to list only those. */
function overall(open) {
  // only the category picked on the left (All Items = everything)
  const lv = S.cat === 'all' ? [...S.levels.values()] : catItems(S.cat).map(it => levelOf(it.i)).filter(l => l !== undefined);
  const bands = [[1, 1], [2, 10], [11, 20], [21, 30], [31, 40], [41, MAX_LEVEL]];
  const max = Math.max(1, ...bands.map(([a, b]) => lv.filter(x => x >= a && x <= b).length));
  const folded = !open && S.ui.overallFolded;
  return `<div class="card overall ${folded ? 'folded' : ''}"><h3 class="card-title" data-act="foldOverall"><span class="fold">▾</span>Overall${S.cat === 'all' ? '' : ` · ${esc(catName(S.cat))}`} <span class="muted small">${fmt(lv.length)} limited</span></h3>
      ${folded ? '' : `<div class="bars">${bands.map(([a, b]) => { const n = lv.filter(x => x >= a && x <= b).length; const on = S.band && S.band[0] === a && S.band[1] === b;
        return `<div class="bar-row ${on ? 'on' : ''}" data-act="band" data-arg="${a}-${b}" title="Show only items unlocking at these levels"><span>${a === b ? 'Lvl ' + a : `Lvl ${a}–${b}`}</span><div class="bar"><i style="width:${(n / max) * 100}%"></i></div><b>${n}</b></div>`; }).join('')}</div>`}
    </div>`;
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

/** Unsaved changes of the item pages (the shell adds the traders' and shows the total). */
const unsavedCount = () => { const ch = changes(); const ids = new Set(ch.map(c => c.id)); return ch.length + statChanges().length + disabledChanges().filter(id => !ids.has(id)).length; };
function renderBottom() { shellRenderBottom(); }

// =====================================================================
// Actions
// =====================================================================

const ACT = {
  tab(t) { goTab(t); },
  viewMenu(arg, el) { sortMenu(el); },
  band(arg) {
    const b = arg ? arg.split('-').map(Number) : null;
    S.band = b && !(S.band && S.band[0] === b[0] && S.band[1] === b[1]) ? b : null;
    if (S.band) S.filter = 'all';
    S.limit = 300; renderPage(false); renderDetails();
  },
  resort() { renderPage(); },
  priceMode() { S.ui.priceMode = fleaMode() ? 'hb' : 'flea'; saveUi(); renderPage(); },
  catMoveBack(id) { moveCategory([id], null); },
  foldOverall() { S.ui.overallFolded = !S.ui.overallFolded; saveUi(); renderDetails(); },
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
  disableLimits() { const k = setDisabled(pickedIds(), true); if (k) toast(`${k} limit${k === 1 ? '' : 's'} switched off — Save to take them out of Level Gate's file`); },
  enableLimits() { const k = setDisabled(pickedIds(), false); if (k) toast(`${k} limit${k === 1 ? '' : 's'} switched back on`); },
  batch() { const ids = pickedIds(); if (ids.length) batchDialog(ids.map(id => ({ id, level: levelOf(id) ?? S.disabled.get(id) ?? 1 }))); },
  goUsed(arg) { goToUse(arg); },
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
  copyId(id) { copyText(id, 'id'); },
  clearSearch() { S.search = ''; $('#search').value = ''; S.limit = 300; renderHeader(); renderPage(false); },
  undo() { undo(false); },
  redo() { undo(true); },
  async save() {
    const list = changes(), stats = statChanges(), dis = disabledChanges();
    if (!list.length && !stats.length && !dis.length) return toast('Nothing to save');
    if (list.length && !S.configFile) return toast('Pick Level Gate\'s file first (Browse… above the list)');
    if (!await reviewChanges(list, stats)) return;
    const done = [];
    try {
      if (list.length) {
        const set = {}, remove = [];
        for (const c of list) c.to === undefined ? remove.push(c.id) : (set[c.id] = c.to);
        const r = await host.call('save', { set, remove });
        S.saved = new Map(Object.entries(r.levels || {}));
        S.levels = new Map(S.saved);
        done.push(`${list.length} level change(s) — in game within ~2 s (or F9 → Reload from disk)`);
      }
      if (dis.length) {
        const r = await host.call('saveDisabled', { disabled: Object.fromEntries(S.disabled) });
        S.disabled = new Map(Object.entries(r || {})); S.disabledSaved = new Map(S.disabled);
        done.push(`${dis.length} switched-off limit(s) remembered`);
      }
      if (stats.length) {
        const r = await host.call('saveStats', { edits: S.statEdits });
        S.statEdits = JSON.parse(JSON.stringify(r.statEdits || {}));
        S.statSaved = JSON.parse(JSON.stringify(r.statEdits || {}));
        if (r.serverMod !== undefined) S.serverMod = !!r.serverMod;
        done.push(`${stats.length} item stat change(s) — restart the SPT server to apply`);
      }
      renderAll();
      status(`Saved at ${new Date().toLocaleTimeString()}: ${done.join(' · ')}.`);
      toast(stats.length ? 'Saved — restart the SPT server for the item stats' : `Saved ${list.length} change${list.length === 1 ? '' : 's'}`);
    } catch (e) { renderAll(); errorBox(e); }
  },
  async reload() {
    const n = unsavedCount();
    if (n && !await confirmBox('Unsaved changes', `${n} unsaved change(s) will be lost. Reload anyway?`, 'Reload')) return;
    try { apply(await host.call('reload')); toast('Reloaded'); } catch (e) { errorBox(e); }
  },
  async browse() {
    if (changes().length + statChanges().length && !await confirmBox('Unsaved changes', 'Pick another file and lose the unsaved changes?', 'Continue')) return;
    try { const r = await host.call('browse'); if (r) apply(r); } catch (e) { errorBox(e); }
  },
  openFolder() { host.call(S.tab === 'stats' ? 'openStatsFolder' : 'openConfigFolder').catch(errorBox); },
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

/** Moves items to another category — only in this editor's lists (saved in its own settings). */
function moveCategory(ids, key) {
  const moves = { ...(S.ui.catMove || {}) };
  for (const id of ids) {
    const own = GROUP[itemOf(id).g] ? itemOf(id).g : 'Other';
    if (!key || key === own) delete moves[id]; else moves[id] = key;
  }
  S.ui.catMove = moves;
  saveUi();
  renderAll();
  toast(key ? `${ids.length} item(s) moved to ${GROUP[key].name}` : 'Back to its own category');
}

/** Right-click menu at the pointer. items: [{ text, act, arg }] or { title } headers. */
function contextMenu(x, y, items) {
  closePopover();
  const pop = document.createElement('div');
  pop.className = 'popover menu2 ctx';
  pop.innerHTML = items.map(it => it.title ? `<div class="menu-title">${esc(it.title)}</div>` : it.sep ? '<hr>'
    : `<button data-ctx="${esc(it.act)}" data-arg="${esc(it.arg ?? '')}" class="${it.on ? 'on' : ''}">${esc(it.text)}${it.on ? '<span class="check">✓</span>' : ''}</button>`).join('');
  document.body.appendChild(pop);
  const w = pop.offsetWidth, h = pop.offsetHeight;
  pop.style.left = Math.min(x, innerWidth - w - 8) + 'px';
  pop.style.top = Math.max(8, Math.min(y, innerHeight - h - 8)) + 'px';
  pop.addEventListener('click', e => {
    const b = e.target.closest('[data-ctx]');
    if (!b) return;
    closePopover();
    CTX[b.dataset.ctx]?.(b.dataset.arg);
  });
  closePopover.fn = e => { if (!pop.contains(e.target)) closePopover(); };
  setTimeout(() => document.addEventListener('mousedown', closePopover.fn), 0);
}
const CTX = {
  copyName() { copyText([...new Set(ctxIds().map(id => itemOf(id)?.n).filter(Boolean))].join('\n'), 'item name'); },
  copyShort() { copyText([...new Set(ctxIds().map(id => itemOf(id)?.s || itemOf(id)?.n).filter(Boolean))].join('\n'), 'short name'); },
  copyIds() { copyText(ctxIds().join('\n'), 'item id'); },
  move(key) { moveCategory(ctxIds(), key); },
  moveBack() { moveCategory(ctxIds(), null); },
  setLevel() { ACT.bulkSet(); },
  off() { ACT.disableLimits(); },
  on() { ACT.enableLimits(); },
  remove() { ACT.bulkRemove(); },
  batch() { ACT.batch(); },
};
const pickedIds = () => (S.picked.size ? [...S.picked] : S.sel ? [S.sel] : []);
/** Switch Off / Switch On / ⚡ follow the picks (without redrawing the list). */
function toolbarPicks() {
  const ids = pickedIds(), on = ids.filter(isLimited).length, off = ids.filter(isDisabled).length;
  const set = (sel, n, text) => { const b = document.querySelector(sel); if (!b) return; b.disabled = !n; b.textContent = n > 1 ? `${text} (${n})` : text; };
  set('#offBtn', on, 'Switch Off'); set('#onBtn', off, 'Switch On'); set('#batchBtn', ids.length, '⚡ Barters / Quests…');
  const bb = document.querySelector('#batchBtn'); if (bb && ids.length > 1) bb.textContent = `⚡ Barters / Quests (${ids.length})…`;
}
let ctxTargets = [];
const ctxIds = () => ctxTargets;

document.addEventListener('contextmenu', e => {
  const row = e.target.closest('#page .row[data-row]');
  if (!row || e.target.closest('input, select')) return;
  e.preventDefault();
  const id = row.dataset.row;
  if (S.tab === 'stats') { statsContextMenu(id, e.clientX, e.clientY); return; }
  ctxTargets = S.picked.has(id) ? [...S.picked] : [id];
  if (!S.picked.has(id)) ACT.select(id);
  const cur = ctxTargets.length === 1 ? groupOf(itemOf(id)) : null;
  const nOn = ctxTargets.filter(isLimited).length, nOff = ctxTargets.filter(isDisabled).length;
  contextMenu(e.clientX, e.clientY, [
    { title: ctxTargets.length > 1 ? `${ctxTargets.length} Items` : itemOf(id).n },
    { text: 'Set Level…', act: 'setLevel' },
    ...(nOn ? [{ text: `Switch Limit Off${nOn > 1 ? ` (${nOn})` : ''}`, act: 'off' }] : []),
    ...(nOff ? [{ text: `Switch Limit On${nOff > 1 ? ` (${nOff})` : ''}`, act: 'on' }] : []),
    ...(nOn ? [{ text: `Remove Limit${nOn > 1 ? ` (${nOn})` : ''}`, act: 'remove' }] : []),
    { text: `⚡ Make Barters / Quests${ctxTargets.length > 1 ? ` (${ctxTargets.length})` : ''}…`, act: 'batch' },
    { sep: true },
    { text: ctxTargets.length > 1 ? 'Copy Item Names' : 'Copy Item Name', act: 'copyName' },
    { text: 'Copy Short Name', act: 'copyShort' },
    { text: 'Copy Item ID', act: 'copyIds' },
    { sep: true },
    { title: `Move ${ctxTargets.length > 1 ? ctxTargets.length + ' items' : 'to Category'} (this editor only)` },
    ...GROUPS.map(([k, n]) => ({ text: n, act: 'move', arg: k, on: cur === k })),
    ...(ctxTargets.some(x => S.ui.catMove?.[x]) ? [{ sep: true }, { text: 'Back to Its Own Category', act: 'moveBack' }] : []),
  ]);
});

function markSelection() {
  document.querySelectorAll('#page .row[data-row]').forEach(r => {
    r.classList.toggle('sel', r.dataset.row === S.sel);
    r.classList.toggle('picked', S.picked.has(r.dataset.row));
  });
  const n = S.picked.size;
  toolbarPicks();
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
  shellLevelsChanged();
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
      S.tab === 'stats'
        ? { title: 'Sort By', items: STAT_SORTS.map(([k, n]) => ({ key: 'ssort:' + k, text: n + (S.statSort.key === k && k !== 'custom' ? (S.statSort.dir > 0 ? '  ▲' : '  ▼') : ''), on: S.statSort.key === k })) }
        : { title: 'Sort By', items: SORTS.map(([k, n]) => ({ key: 'sort:' + k, text: n + (S.sort.key === k && k !== 'custom' ? (S.sort.dir > 0 ? '  ▲' : '  ▼') : ''), on: S.sort.key === k })) },
      { title: 'Show', items: [{ key: 'hidden', text: 'Dev / Hidden Items', on: S.showHidden }] },
    ]);
  };
  draw();
  pop.addEventListener('click', e => {
    const b = e.target.closest('[data-v]');
    if (!b) return;
    const v = b.dataset.v;
    if (v.startsWith('sort:')) ACT.sortBy(v.slice(5));
    else if (v.startsWith('ssort:')) ACT.statSortBy(v.slice(6));
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

function reviewChanges(list, stats = []) {
  const kinds = { add: [], edit: [], del: [] };
  kinds.off = []; kinds.on = [];
  for (const c of list) (c.from === undefined ? (S.disabledSaved.get(c.id) === c.to ? kinds.on : kinds.add) : c.to === undefined ? (S.disabled.get(c.id) === c.from ? kinds.off : kinds.del) : kinds.edit).push(c);
  const line = (c, sign, color, text) => `<div class="chg"><span class="chg-i" style="color:${color}">${sign}</span><div>${esc(itemOf(c.id).n)} <span class="muted">${text}</span></div></div>`;
  const section = (title, arr, fn) => arr.length ? `<div class="chg-trader"><h3>${title} <span class="muted small">${arr.length}</span></h3>${arr.slice(0, 300).map(fn).join('')}${arr.length > 300 ? `<div class="muted small">… and ${arr.length - 300} more</div>` : ''}</div>` : '';
  return openModal(`<div class="dialog"><h2>Save These Changes?</h2>
      <div class="muted small" style="margin-bottom:10px">${list.length ? `${list.length} level change${list.length === 1 ? '' : 's'} to ${esc(S.configFile)}` : ''}${list.length && stats.length ? '<br>' : ''}${stats.length ? `${stats.length} item stat change${stats.length === 1 ? '' : 's'} to ${esc(S.statsFile || 'user\\mods\\ModernEditor\\item_stats.json')}` : ''}</div>
      <div class="chg-list">
        ${section('New Limits', kinds.add, c => line(c, '+', 'var(--green)', `→ Level ${c.to}`))}
        ${section('Changed', kinds.edit, c => line(c, '•', 'var(--orange)', `Level ${c.from} → ${c.to}`))}
        ${section('Limits Removed', kinds.del, c => line(c, '−', 'var(--red)', `was Level ${c.from}`))}
        ${section('Switched Off (out of Level Gate\'s file, level kept in disabled_levels.json)', kinds.off, c => line(c, '○', '#8a8a8a', `Level ${c.from} kept`))}
        ${section('Switched Back On', kinds.on, c => line(c, '●', 'var(--green)', `Level ${c.to}`))}
        ${section('Item Stats (after a server restart)', stats, id => `<div class="chg"><span class="chg-i" style="color:var(--violet)">✎</span><div>${esc(itemOf(id).n)} <span class="muted">${esc(statDiffText(id))}</span></div></div>`)}
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


// =====================================================================
// Events
// =====================================================================

document.addEventListener('click', e => {
  if (inShell(e.target)) return; // the top bar, page links and bottom bar are the shell's (app.js)
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
  else { const n = Number(el.value); if (!(n >= 1)) return; S.levels.set(id, clamp(Math.round(n), 1, MAX_LEVEL)); S.disabled.delete(id); }
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
  if (el.dataset?.catmove) { moveCategory([el.dataset.catmove], el.value); return; }
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
  if (!active()) return;
  if (e.target.closest('button') || e.target === $('#search')) return;
  e.preventDefault();
  searchOpen(true);
  setTimeout(() => $('#search').focus(), 0);
});
const searchOpen = on => { $('#searchBox').classList.toggle('open', on); document.body.classList.toggle('search-open', on); };
$('#search').addEventListener('focus', () => { if (active()) searchOpen(true); });
$('#search').addEventListener('blur', () => { if (active() && !$('#search').value) searchOpen(false); });
$('#search').addEventListener('keydown', e => {
  if (!active()) return;
  if (e.key === 'Escape') { e.stopPropagation(); ACT.clearSearch(); e.target.blur(); }
  if (e.key === 'Enter') e.target.blur();
});

// infinite list: more rows as you scroll down
$('#page').addEventListener('scroll', () => {
  if (!active()) return;
  const p = $('#page');
  if (S.page === 'items' && S.shown.length > S.limit && p.scrollTop + p.clientHeight > p.scrollHeight - 600) { S.limit += 300; renderPage(); }
});

document.addEventListener('keydown', e => {
  const k = e.key, ctrl = e.ctrlKey || e.metaKey;
  const typing = e.target.matches?.('input[type=text], textarea') || (e.target.matches?.('input') && !e.target.dataset?.lvl && e.target.type !== 'range');
  if (!$('#modal').hidden) { if (k === 'Escape') closeModal(null); return; }
  if (ctrl && k.toLowerCase() === 'f') { e.preventDefault(); searchOpen(true); $('#search').focus(); $('#search').select(); return; }
  if (typing) return;
  if (S.tab === 'prog') return; // prog.js has its own keys
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
  if (S.tab === 'stats') return; // the keys below edit level limits
  if (inLevel) { if (k === 'Enter' || k === 'Escape') e.target.blur(); return; }
  if (k === 'Escape') { if (S.picked.size) { S.picked = new Set(); markSelection(); renderDetails(); } else if (S.search) ACT.clearSearch(); return; }
  if ((k === 'Delete' || k === 'Backspace') && (S.sel || S.picked.size)) { e.preventDefault(); ACT.bulkRemove(); return; }
  if (k === 'Enter' && S.sel) { e.preventDefault(); document.querySelector(`#page .lvl-in[data-lvl="${S.sel}"]`)?.focus(); return; }
  if (/^[0-9]$/.test(k) && S.sel && !ctrl) {
    const input = document.querySelector(`#page .lvl-in[data-lvl="${S.sel}"]`);
    if (input) { e.preventDefault(); input.focus(); input.value = k; input.dispatchEvent(new Event('input', { bubbles: true })); }
  }
});



/* LevelGate Editor — item stats: what the lists show (ammo pen, armor class, slots, med / food
   effects) and the Item Stats tab that edits meds, stims and food.
   S.meds[id]      = the item as the game has it (SPT database + other mods like BalancedMeds)
   S.statEdits[id] = your edit (the whole item, same shape) → user\mods\ModernEditor\item_stats.json,
                     applied by Modern Editor's server mod (ModernEditor.dll) when the server starts.
   S.medsDefault[id] = Escape From Tarkov's own values (the SPT database before any mod). */

const MED_KINDS = [['medkit', 'Medkits', '#1ed760'], ['medical', 'Medical', '#4dd0e1'], ['drug', 'Painkillers', '#ffa42b'], ['stim', 'Stimulators', '#a082ff'], ['food', 'Food & Drink', '#8bd66b']];
const MED_KIND = Object.fromEntries(MED_KINDS.map(([k, n, c]) => [k, { name: n, color: c }]));
const DAMAGE = [['LightBleeding', 'Light Bleeding'], ['HeavyBleeding', 'Heavy Bleeding'], ['Fracture', 'Fracture'], ['Pain', 'Pain'], ['Contusion', 'Contusion'],
  ['Intoxication', 'Intoxication'], ['LethalIntoxication', 'Lethal Toxin'], ['RadExposure', 'Radiation'], ['DestroyedPart', 'Destroyed Limb']];
const DAMAGE_NAME = Object.fromEntries(DAMAGE);
const HEALTH = [['Energy', 'Energy'], ['Hydration', 'Hydration']];
const BUFFS = [
  ['HealthRate', 'Health Regen (HP/s)'], ['EnergyRate', 'Energy (per s)'], ['HydrationRate', 'Hydration (per s)'], ['StaminaRate', 'Stamina Recovery'],
  ['MaxStamina', 'Max Stamina'], ['SkillRate', 'Skill'], ['WeightLimit', 'Carry Weight'], ['DamageModifier', 'Damage Taken'],
  ['BodyTemperature', 'Body Temperature'], ['RemoveAllBloodLosses', 'Stops All Bleeding'], ['Antidote', 'Antidote'],
  ['HandsTremor', 'Hand Tremor'], ['QuantumTunnelling', 'Tunnel Vision'], ['Pain', 'Pain'], ['StomachBloodloss', 'Stomach Bleeding'],
  ['Contusion', 'Contusion'], ['UnknownToxin', 'Toxin'], ['LightBleeding', 'Light Bleeding'], ['HeavyBleeding', 'Heavy Bleeding'], ['Fracture', 'Fracture'],
];
const BUFF_NAME = Object.fromEntries(BUFFS);
const SKILLS = ['Endurance', 'Strength', 'Vitality', 'Health', 'StressResistance', 'Metabolism', 'Immunity', 'Perception', 'Intellect', 'Attention',
  'Charisma', 'Memory', 'RecoilControl', 'Surgery', 'AimDrills', 'Throwing', 'CovertMovement', 'Search', 'Sniping', 'ProneMovement', 'LightVests', 'HeavyVests',
  'WeaponTreatment', 'TroubleShooting', 'MagDrills', 'FieldMedicine', 'FirstAid', 'Crafting', 'HideoutManagement'];
const STAT_SORTS = [['custom', 'Custom Order'], ['name', 'Name'], ['type', 'Type'], ['price', 'Price'], ['level', 'Level'], ['edited', 'Edited First']];
const KEEP = ['medUseTime', 'MaxHpResource', 'hpResourceRate', 'foodUseTime', 'MaxResource', 'effects_health', 'effects_damage', 'buffName', 'effects_buffs'];

const r2 = n => Math.round(n * 100) / 100;
const sgn = n => (n > 0 ? '+' : n < 0 ? '−' : '') + r2(Math.abs(n));
const skillName = s => (s || '').replace(/([a-z])([A-Z])/g, '$1 $2');

/** "(1.5 Default)" next to a value that isn't Escape From Tarkov's. */
function defNote(cur, def, short = false) {
  const norm = v => v === undefined || v === null || v === '' ? undefined : Number(v);
  if (norm(cur) === norm(def)) return '';
  const d = norm(def) === undefined ? 'none' : r2(Number(def));
  return `<span class="def" title="Escape From Tarkov's value">(${d}${short ? '' : ' Default'})</span>`;
}
/** True when the item (with edits) has Escape From Tarkov's own values. */
function sameAsDefault(id) {
  const m = effMed(id), d = S.medsDefault[id];
  if (!d) return true;
  return KEEP.every(k => JSON.stringify(m[k] ?? null) === JSON.stringify(d[k] ?? null));
}
function statsContextMenu(id, x, y) {
  if (S.sel !== id) { S.sel = id; S.picked = new Set(); markSelection(); renderDetails(); }
  contextMenu(x, y, [
    { title: S.items.get(id)?.n || id },
    { text: 'Reset to Escape From Tarkov Default', act: 'statDefault', arg: id },
    ...(S.statEdits[id] ? [{ text: 'Undo My Edit (Other Mods\' Values)', act: 'statUndoEdit', arg: id }] : []),
  ]);
}
Object.assign(CTX, {
  statDefault(id) { ACT.statResetDefault(id); },
  statUndoEdit(id) { S.sel = id; ACT.statReset(); },
});

/** The item as the game will have it: base, or LevelGate's edit. */
function effMed(id) {
  const base = S.meds[id];
  if (!base) return null;
  return S.statEdits[id] ? { ...base, ...S.statEdits[id], kind: base.kind } : base;
}
const medKind = it => it.mk || S.meds[it.i]?.kind || null;

// =====================================================================
// Readable text
// =====================================================================

function buffText(b) {
  const v = Number(b.Value) || 0;
  const what = b.BuffType === 'SkillRate' ? `${skillName(b.SkillName) || 'Skill'} ${sgn(v)}`
    : b.BuffType === 'WeightLimit' ? `Carry weight ${sgn(v * 100)}%`
      : b.BuffType === 'DamageModifier' ? `Damage taken ${sgn(v * 100)}%`
        : ['HealthRate', 'EnergyRate', 'HydrationRate'].includes(b.BuffType) ? `${BUFF_NAME[b.BuffType].split(' (')[0]} ${sgn(v)}/s`
          : ['StaminaRate', 'MaxStamina', 'BodyTemperature'].includes(b.BuffType) ? `${BUFF_NAME[b.BuffType]} ${sgn(v)}`
            : BUFF_NAME[b.BuffType] || b.BuffType;
  const when = `${Number(b.Duration) || 0} s${Number(b.Delay) > 1 ? ` after ${b.Delay} s` : ''}`;
  const chance = Number(b.Chance) < 1 ? ` · ${Math.round(Number(b.Chance) * 100)}% chance` : '';
  return `${what} · ${when}${chance}`;
}

function curesText(m, kind) {
  return Object.entries(m.effects_damage || {}).map(([k, e]) => {
    const n = DAMAGE_NAME[k] || k;
    if (kind === 'medkit' || !e?.duration) return n + (e?.cost ? ` (${e.cost} HP)` : '');
    return `${n} ${e.duration} s`;
  });
}

function healthText(m) {
  return Object.entries(m.effects_health || {}).map(([k, e]) => `${sgn(Number(e?.value) || 0)} ${k.toLowerCase()}`);
}

/** One line for the lists. */
// ---- small icons for the lists
const ICON = {
  energy: '<path d="M13 2 4 14h7l-1 8 9-12h-7l1-8z"/>',
  water: '<path d="M12 2s7 8 7 13a7 7 0 0 1-14 0c0-5 7-13 7-13z"/>',
  time: '<path d="M12 2a10 10 0 1 1 0 20 10 10 0 0 1 0-20Zm1 5h-2v6l5 3 1-1.7-4-2.3V7Z"/>',
  uses: '<path d="M4 5h16v3H4zM4 10.5h16v3H4zM4 16h16v3H4z"/>',
  hp: '<path d="M10 3h4v7h7v4h-7v7h-4v-7H3v-4h7z"/>',
  cure: '<path d="M12 2 4 5v6c0 5 3.4 9.6 8 11 4.6-1.4 8-6 8-11V5l-8-3Zm-1 5h2v3h3v2h-3v3h-2v-3H8v-2h3V7Z"/>',
  fx: '<path d="m12 2 2.4 7.2L22 12l-7.6 2.8L12 22l-2.4-7.2L2 12l7.6-2.8z"/>',
};
/** One icon per treatment (drawn for this editor, in the spirit of the game's health icons). */
const CURE_ICON = {
  LightBleeding: ['<path d="M12 5s5 6 5 9.5a5 5 0 0 1-10 0C7 11 12 5 12 5z"/>', '#ff6b6b'],
  HeavyBleeding: ['<path d="M8 3s4 5 4 8a4 4 0 0 1-8 0c0-3 4-8 4-8zm8 6s4 5 4 8a4 4 0 0 1-8 0c0-3 4-8 4-8z"/>', '#ff3b3b'],
  Fracture: ['<path d="M7 3a3 3 0 0 0-2.8 4.1A3 3 0 1 0 7.9 11L13 16.1a3 3 0 1 0 3.9 3.7A3 3 0 1 0 20.9 16 3 3 0 0 0 17 12.1L11.9 7A3 3 0 0 0 7 3z"/>', '#e8e8e8'],
  Pain: ['<path d="M4.9 13.4 13.4 4.9a4.5 4.5 0 0 1 6.4 6.4l-8.5 8.5a4.5 4.5 0 0 1-6.4-6.4zm1.4 1.4a2.5 2.5 0 0 0 3.5 3.5l3.5-3.5-3.5-3.5z"/>', '#ffa42b'],
  Contusion: ['<path d="m12 2 2 5 5-2-2 5 5 2-5 2 2 5-5-2-2 5-2-5-5 2 2-5-5-2 5-2-2-5 5 2z"/>', '#f5cd46'],
  Intoxication: ['<path d="M9 2h6v2h-1v5l5 9a2 2 0 0 1-1.8 3H6.8A2 2 0 0 1 5 18l5-9V4H9V2z"/>', '#8bd66b'],
  LethalIntoxication: ['<path d="M12 2a8 8 0 0 0-5 14.2V20h3v-2h1v2h2v-2h1v2h3v-3.8A8 8 0 0 0 12 2zm-3 8a2 2 0 1 1 0 4 2 2 0 0 1 0-4zm6 0a2 2 0 1 1 0 4 2 2 0 0 1 0-4z"/>', '#5fd35f'],
  RadExposure: ['<path d="M12 10a2 2 0 1 1 0 4 2 2 0 0 1 0-4zM10.3 9.1L7.0 3.3A10 10 0 0 1 17.0 3.3L13.7 9.1A3.4 3.4 0 0 0 10.3 9.1zM15.4 12.0L22.0 12.0A10 10 0 0 1 17.0 20.7L13.7 14.9A3.4 3.4 0 0 0 15.4 12.0zM10.3 14.9L7.0 20.7A10 10 0 0 1 2.0 12.0L8.6 12.0A3.4 3.4 0 0 0 10.3 14.9z"/>', '#ffd54f'],
  DestroyedPart: ['<path d="M5 3 21 19l-2 2L3 5zm14 0 2 2L5 21l-2-2z"/>', '#ff8a80'],
};
const cureIcon = t => (CURE_ICON[t] ? `<svg class="cure-ico" viewBox="0 0 24 24" style="fill:${CURE_ICON[t][1]}">${CURE_ICON[t][0]}</svg>` : '');
const chip = (icon, text, color, title) => `<span class="schip ${text === '' ? 'ico' : ''}" style="--c:${color}" title="${esc(title)}"><svg viewBox="0 0 24 24">${ICON[icon] || CURE_ICON[icon]?.[0] || ''}</svg>${esc(text)}</span>`;
/** Uses of a med: 0 means single use (splints, antibiotics...). */
const usesOf = m => Math.max(1, Number(m.MaxHpResource) || 0);

/** Meds / food as icon chips (lists); other items as text. */
function statChips(it) {
  const kind = medKind(it), m = kind && effMed(it.i);
  if (!m) return esc(statsShort(it)) || '<span class="muted">—</span>';
  const out = [];
  const use = m.medUseTime ?? m.foodUseTime;
  const eh = m.effects_health || {};
  if (eh.Energy?.value) out.push(chip('energy', sgn(Number(eh.Energy.value)), '#f5cd46', 'Energy (whole item)'));
  if (eh.Hydration?.value) out.push(chip('water', sgn(Number(eh.Hydration.value)), '#5cc8ff', 'Hydration (whole item)'));
  if (kind === 'medkit') out.push(chip('hp', `${m.MaxHpResource ?? 0}`, '#f15e6c', 'HP resource'));
  else if (kind === 'food') { if (m.MaxResource > 1) out.push(chip('uses', `${m.MaxResource}`, '#b3b3b3', 'Units')); }
  else out.push(chip('uses', `${usesOf(m)}`, '#b3b3b3', usesOf(m) === 1 ? 'Single use' : 'Uses'));
  if (use !== undefined) out.push(chip('time', `${r2(use)}s`, '#9a9a9a', 'Use time'));
  // one icon per treatment; painkillers / stims show for how long
  for (const [k, e] of Object.entries(m.effects_damage || {})) {
    const timed = kind !== 'medkit' && Number(e?.duration) > 0;
    const cost = Number(e?.cost) > 0 ? ` (${e.cost} ${kind === 'medkit' ? 'HP' : 'uses'})` : '';
    out.push(chip(k, timed ? `${e.duration}s` : '', CURE_ICON[k]?.[1] || '#7ee0c3', `${kind === 'medkit' || kind === 'medical' ? 'Treats' : 'Removes'} ${DAMAGE_NAME[k] || k}${timed ? ` for ${e.duration} s` : ''}${cost}`));
  }
  const fx = m.effects_buffs || [];
  if (fx.length) out.push(chip('fx', `${fx.length} · ${Math.max(0, ...fx.map(b => Number(b.Duration) || 0))}s`, '#a082ff', fx.map(buffText).join('\n')));
  if (S.statEdits[it.i]) out.push('<span class="schip ed" title="Edited by you">✎</span>');
  return out.join('');
}

function statsShort(it) {
  const st = it.st || {};
  const parts = [];
  if (st.pen !== undefined) parts.push(`Pen ${st.pen} · Dmg ${st.dmg}${st.pc > 1 ? `×${st.pc}` : ''}`);
  if (st.of) { const a = S.items.get(st.of); parts.push(`${st.pack ? st.pack + ' × ' : ''}${a?.s || a?.n || 'rounds'}${a?.st?.pen !== undefined ? ` · Pen ${a.st.pen} · Dmg ${a.st.dmg}` : ''}`); }
  if (st.ac) parts.push(`Class ${st.ac}`);
  if (st.slots && !st.pen) parts.push(`${st.slots} slots`);
  const kind = medKind(it), m = kind && effMed(it.i);
  if (m) {
    if (kind === 'medkit') parts.push(`${m.MaxHpResource ?? 0} HP`, ...curesText(m, kind).slice(0, 2));
    else if (kind === 'food') parts.push(...healthText(m), ...((m.effects_buffs || []).length ? [`${m.effects_buffs.length} effect(s)`] : []));
    else if (kind === 'stim') parts.push(`${(m.effects_buffs || []).length} effects`, `${Math.max(0, ...(m.effects_buffs || []).map(b => Number(b.Duration) || 0))} s`);
    else parts.push(...curesText(m, kind).slice(0, 3), usesOf(m) === 1 ? 'single use' : `${usesOf(m)} uses`);
    if (S.statEdits[it.i]) parts.push('✎');
  }
  return parts.join(' · ');
}

/** The Stats card in the item details (Level Limits tab). */
function statsCard(it) {
  const st = it.st || {};
  const rows = [];
  const row = (l, v) => rows.push(`<div class="field"><label>${esc(l)}</label><div>${v}</div></div>`);
  if (st.pen !== undefined) {
    row('Penetration', `<b>${st.pen}</b>`); row('Damage', `<b>${st.dmg}</b>${st.pc > 1 ? ` × ${st.pc} pellets` : ''}`);
    if (st.ad !== undefined) row('Armor Damage', `${st.ad}%`);
    if (st.v) row('Speed', `${fmt(st.v)} m/s`);
    if (st.frag) row('Fragmentation', `${Math.round(st.frag * 100)}%`);
  }
  if (st.of) { const a = S.items.get(st.of); row('Contains', `${st.pack ? `<b>${st.pack}</b> × ` : ''}${esc(a?.n || st.of)}`); if (a?.st?.pen !== undefined) row('Rounds', `Pen <b>${a.st.pen}</b> · Dmg <b>${a.st.dmg}</b>${a.st.ad !== undefined ? ` · Armor dmg ${a.st.ad}%` : ''}`); }
  if (st.ac) row('Armor Class', `<b>${st.ac}</b>${st.dur ? ` · ${fmt(st.dur)} durability` : ''}`);
  if (st.slots) row('Storage', `${st.slots} slots`);
  const kind = medKind(it), m = kind && effMed(it.i);
  if (m) {
    const use = m.medUseTime ?? m.foodUseTime;
    if (use !== undefined) row('Use Time', `${use} s`);
    if (kind === 'medkit') row('HP Resource', `${m.MaxHpResource ?? 0}${m.hpResourceRate ? ` · heals up to ${m.hpResourceRate} HP per use` : ''}`);
    else if (kind === 'food') { if (m.MaxResource > 1) row('Uses', m.MaxResource); }
    else row('Uses', usesOf(m) === 1 ? '1 (single use)' : usesOf(m));
    const health = healthText(m);
    if (health.length) row(kind === 'food' ? 'Gives' : 'Also', esc(health.join(', ')) + (kind === 'food' && m.MaxResource > 1 ? ` <span class="muted small">(whole item, ${m.MaxResource} units — drinking / eating part gives that share)</span>` : ''));
    const cures = curesText(m, kind);
    if (cures.length) row(kind === 'medkit' ? 'Treats' : 'Removes', esc(cures.join(', ')));
    const buffs = m.effects_buffs || [];
    if (buffs.length) rows.push(`<div class="buff-list">${buffs.map(b => `<div class="buff ${Number(b.Value) < 0 || ['HandsTremor', 'QuantumTunnelling', 'Pain', 'StomachBloodloss', 'Contusion', 'UnknownToxin'].includes(b.BuffType) ? 'bad' : ''}">${esc(buffText(b))}</div>`).join('')}</div>`);
  }
  if (!rows.length) return '';
  return `<div class="card"><h3>Stats${m ? `<span class="grow"></span><button class="outline small-btn" data-act="editStats" data-arg="${it.i}">✎ Edit Stats</button>` : ''}</h3>
    ${m && S.statEdits[it.i] ? '<div class="hint" style="color:var(--violet)">Edited in Item Stats (in game after a server restart).</div>' : ''}${rows.join('')}</div>`;
}

// =====================================================================
// Item Stats tab
// =====================================================================

function statIds(cat) {
  const q = (S.search || '').trim().toLowerCase();
  return Object.keys(S.meds).filter(id => {
    const it = S.items.get(id);
    if (!it || (it.x && !S.statEdits[id] && !S.showHidden)) return false;
    if (cat === 'edited') return !!S.statEdits[id];
    if (cat !== 'all' && S.meds[id].kind !== cat) return false;
    return (!q || matches(it, q)) && tagPass(id);
  });
}

/** Food: the value is for the whole item; drinking part of it gives that share ("≈ 1 per unit"). */
function wholeItemHint(m, f) {
  const v = Number(m.effects_health?.[f]?.value), n = Number(m.MaxResource) || 1;
  return n > 1 && v ? `whole item · ≈ ${r2(v / n)} per unit (${n} units)` : 'whole item';
}

function statChanges() {
  const ids = new Set([...Object.keys(S.statEdits), ...Object.keys(S.statSaved)]);
  return [...ids].filter(id => JSON.stringify(S.statEdits[id] ?? null) !== JSON.stringify(S.statSaved[id] ?? null));
}

function statDiffText(id) {
  const base = S.meds[id] || {};
  const was = S.statSaved[id] ? { ...base, ...S.statSaved[id] } : base, now = effMed(id) || base;
  if (!S.statEdits[id]) return 'back to the game\'s values';
  const label = { medUseTime: 'use time', foodUseTime: 'use time', MaxHpResource: now.kind === 'medkit' ? 'HP' : 'uses', MaxResource: 'uses', hpResourceRate: 'HP per use',
    effects_health: 'energy / hydration', effects_damage: 'treats', effects_buffs: 'effects' };
  const diff = Object.keys(label).filter(k => JSON.stringify(was[k] ?? null) !== JSON.stringify(now[k] ?? null))
    .map(k => typeof now[k] === 'number' || typeof was[k] === 'number' ? `${label[k]} ${was[k] ?? '—'} → ${now[k] ?? '—'}` : `${label[k]} changed`);
  return diff.join(', ') || 'edited';
}

function statsNav() {
  const entry = (key, name, color) => {
    const n = statIds(key).length;
    if (!n && key !== 'all' && key !== 'edited') return '';
    return `<button class="nav cat ${S.statCat === key ? 'on' : ''}" data-act="statCat" data-arg="${key}"><i class="dot" style="--c:${color}"></i><span>${esc(name)}</span><em>${fmt(n)}</em></button>`;
  };
  $('#lgNav').innerHTML = entry('all', 'All Meds & Food', '#ffffff') + MED_KINDS.map(([k, n, c]) => entry(k, n, c)).join('') + entry('edited', 'Edited by You', 'var(--violet)');
  $('#lgNavTitle').textContent = 'Meds & Food';
}

function statsPage() {
  const d = S.statSort.dir, byName = (a, b) => (a.n || '').localeCompare(b.n || '');
  const kindIx = it => MED_KINDS.findIndex(k => k[0] === S.meds[it.i].kind);
  const cmp = {
    custom: customCmp('stats'),
    name: (a, b) => d * byName(a, b),
    type: (a, b) => d * (kindIx(a) - kindIx(b)) || byName(a, b),
    price: (a, b) => d * (priceOf(a) - priceOf(b)) || byName(a, b),
    level: (a, b) => { const la = levelOf(a.i), lb = levelOf(b.i); if (la === undefined && lb === undefined) return byName(a, b); if (la === undefined) return 1; if (lb === undefined) return -1; return d * (la - lb) || byName(a, b); },
    edited: (a, b) => Number(!!S.statEdits[b.i]) - Number(!!S.statEdits[a.i]) || byName(a, b),
  }[S.statSort.key] || ((a, b) => byName(a, b));
  S.shown = statIds(S.statCat).map(id => S.items.get(id)).sort(cmp);
  const ids = S.shown.map(it => it.i);
  const head = ([k, t], c) => `<div>${c ? grip('st', c) : ''}<span class="head-text sortable ${S.statSort.key === k ? 'on' : ''}" data-act="statSortBy" data-arg="${k}">${t}${S.statSort.key === k && k !== 'custom' ? `<span class="sort-arrow">${d > 0 ? '▲' : '▼'}</span>` : ''}</span></div>`;
  const warn = !S.serverMod
    ? `<div class="warn-line">⚠ The Modern Editor server mod (ModernEditor.dll) wasn't found in ${esc(S.statsFile ? S.statsFile.replace(/[\\/]item_stats\.json$/, '') : 'SPT\\user\\mods\\ModernEditor')} — item stat edits are saved but only take effect with it installed (Install\\SPT_Runtime\\user\\mods\\ModernEditor in the download).</div>` : '';
  const oldFile = S.statsReadFrom && S.statsFile && S.statsReadFrom.toLowerCase() !== S.statsFile.toLowerCase()
    ? `<div class="warn-line">⚠ Your edits were read from the old file ${esc(S.statsReadFrom)} — they're saved to ${esc(S.statsFile)} from now on (or click Move My Files on the start page).</div>` : '';
  const rows = ids.map((id, i) => {
    const it = S.items.get(id), m = effMed(id), k = MED_KIND[m.kind];
    const changed = JSON.stringify(S.statEdits[id] ?? null) !== JSON.stringify(S.statSaved[id] ?? null);
    return `<div class="row ${id === S.sel ? 'sel' : ''} ${S.picked.has(id) ? 'picked' : ''} ${changed ? 'changed' : ''}" data-row="${id}">
      <div class="idx">${i + 1}</div>
      <div class="cell">${icon(it)}<div class="text"><div class="line1"><span class="title">${esc(it.n)}</span><span class="dirty" title="Changed — not saved yet">•</span>
        <div class="badges">${S.statEdits[id] ? '<span class="badge" style="--c:var(--violet)">EDITED</span>' : ''}${it.m ? `<span class="badge" style="--c:var(--pink)">${esc(it.m)}</span>` : ''}${tagBadges(id)}</div></div>
        <div class="line2">${esc(it.s || '')}</div></div></div>
      <div class="col"><i class="dot" style="--c:${k.color}"></i>${esc(k.name)}</div>
      <div class="col stats chips-col">${statChips(it)}</div>
      ${noteCell(id)}
      ${priceCell(it)}
      ${levelCell(id)}
    </div>`;
  }).join('');
  return `<div class="toolbar sticky">${warn}${oldFile}
      <span class="muted small">${fmt(ids.length)} shown${S.picked.size > 1 ? ` · ${S.picked.size} picked` : ''} · Ctrl+drag to reorder · edits apply after restarting the SPT server${S.statSources.length ? ` · your edits win over ${esc(S.statSources.map(x => x.replace(/ \(.*/, '')).join(' and '))}` : ''}</span></div>
    ${tagBar(statIds(S.statCat))}
    <div class="list st" data-cols="st" style="--cols:${colsCss('st')}"><div class="list-head">${head(['custom', '#'])}${head(['name', 'Item'], 'item')}${head(['type', 'Type'], 'type')}<div>${grip('st', 'effects')}<span class="head-text">Effects</span></div><div>${grip('st', 'notes')}<span class="head-text">Notes</span></div>${head(['price', 'Price'], 'price').replace(/<\/div>$/, priceToggle() + '</div>')}${head(['level', 'Level'], 'level')}</div>${rows || `<div class="empty">${Object.keys(S.meds).length ? 'Nothing matches.' : 'No meds loaded — the stats come from the SPT database (pick the config inside your SPT folder).'}</div>`}</div>`;
}

// ---- the editor (right panel)
function statsDetails() {
  const d = $('#details');
  const picked = [...S.picked].filter(x => S.meds[x]);
  if (picked.length > 1) {
    $('#detailsTitle').textContent = `${picked.length} Items Picked`;
    const edited = picked.filter(x => S.statEdits[x]).length, notDefault = picked.filter(x => !sameAsDefault(x)).length;
    d.innerHTML = `<div class="card"><h3>All Picked</h3><div class="hint">${edited} edited by you · ${notDefault} differ from Escape From Tarkov's values.</div>
        <div class="toolbar" style="padding:4px 0 0"><button class="outline" data-act="statManyDefault" ${notDefault ? '' : 'disabled'}>Reset All to EFT Default</button><button class="outline" data-act="statManyUndo" ${edited ? '' : 'disabled'}>Undo My Edits</button></div></div>
      <div class="card"><h3>Picked</h3><div class="mini">${picked.map(x => { const it = S.items.get(x); return `<div class="row" data-act="selOnly" data-arg="${x}"><div class="cell">${icon(it)}<div class="text"><div class="title">${esc(it.n)}</div></div><span class="side">${esc(statsShort(it).replace(/ · ✎$/, ''))}</span></div></div>`; }).join('')}</div></div>
      ${tagCard(picked)}`;
    return;
  }
  const id = S.sel && S.meds[S.sel] ? S.sel : null;
  if (!id) {
    $('#detailsTitle').textContent = 'Item Stats';
    d.innerHTML = `<div class="card"><h3>Meds, Stims & Food</h3><div class="hint">Pick an item to change how long it takes to use, how many uses / HP it has, what it treats, energy and hydration, and a stim's effects.
      <br><br>Edits are saved to <span class="mono">${esc(S.statsFile || 'SPT\\user\\mods\\ModernEditor\\item_stats.json')}</span> and applied by the Modern Editor server mod (ModernEditor.dll) when the server starts (after other mods, so they win). The Level Limits tab shows the edited stats too.
      <br><br>Values that differ from Escape From Tarkov's show the original next to them, like <span class="def">(1.5 Default)</span>. Right-click an item to reset it.</div></div>`;
    return;
  }
  const keep = document.activeElement?.dataset?.key;
  const it = S.items.get(id), m = effMed(id), kind = m.kind, k = MED_KIND[kind];
  const def = S.medsDefault[id] || {};
  $('#detailsTitle').textContent = it.n;
  const num = (label, key, field, step = 1, hint = '') => `<div class="field"><label>${esc(label)} ${defNote(m[field], def[field])}</label><div class="numrow"><input type="number" step="${step}" data-key="${key}" data-sf="${field}" value="${m[field] ?? ''}">${hint ? `<span class="muted small">${hint}</span>` : ''}</div></div>`;
  const resource = kind === 'medkit' ? num('HP Resource', 'hp', 'MaxHpResource', 1, 'total HP it can heal') + num('HP per Use', 'rate', 'hpResourceRate', 1, 'most it heals in one use')
    : kind === 'food' ? num('Units', 'res', 'MaxResource', 1, 'how much there is to eat / drink') : num('Uses', 'hp', 'MaxHpResource', 1, '0 or 1 = single use');
  const useField = kind === 'food' ? 'foodUseTime' : 'medUseTime';
  const health = HEALTH.map(([f, n]) => `<div class="field"><label>${n} ${defNote(m.effects_health?.[f]?.value, def.effects_health?.[f]?.value)}</label><div class="numrow"><input type="number" step="1" data-key="eh-${f}" data-eh="${f}" value="${m.effects_health?.[f]?.value ?? ''}" placeholder="none"><span class="muted small">${kind === 'food' ? wholeItemHint(m, f) : 'when used'}</span></div></div>`).join('');
  const dmg = DAMAGE.map(([t, n]) => {
    const e = m.effects_damage?.[t], de = def.effects_damage?.[t];
    const on = !!e;
    const f = (field, label) => `<label class="mini-f">${label} ${on && de ? defNote(e?.[field], de?.[field], true) : ''}<input type="number" step="1" data-key="ed-${t}-${field}" data-ed="${t}" data-edf="${field}" value="${e?.[field] ?? ''}" ${on ? '' : 'disabled'}></label>`;
    return `<div class="dmg-row ${on ? 'on' : ''}"><label class="switch"><input type="checkbox" data-key="ed-${t}" data-edon="${t}" ${on ? 'checked' : ''}><span class="track"></span><span>${cureIcon(t)}${n} ${!!de !== on ? `<span class="def">(${de ? 'on' : 'off'} Default)</span>` : ''}</span></label>
      <div class="dmg-fields" ${on ? '' : 'hidden'}>${kind === 'medkit' || kind === 'medical' ? f('cost', 'Cost') : ''}${f('duration', 'For (s)')}${f('delay', 'Delay')}${f('fadeOut', 'Fade')}${t === 'DestroyedPart' ? f('healthPenaltyMin', 'Min %') + f('healthPenaltyMax', 'Max %') : ''}</div></div>`;
  }).join('');
  const buffs = (m.effects_buffs || []).map((b, i) => `<div class="buff-row">
      <select data-key="b${i}-t" data-bi="${i}" data-bf="BuffType">${BUFFS.map(([v, n]) => `<option value="${v}" ${b.BuffType === v ? 'selected' : ''}>${n}</option>`).join('')}${BUFF_NAME[b.BuffType] ? '' : `<option selected>${esc(b.BuffType)}</option>`}</select>
      <select data-key="b${i}-s" data-bi="${i}" data-bf="SkillName" ${b.BuffType === 'SkillRate' ? '' : 'disabled'}><option value=""></option>${SKILLS.map(v => `<option value="${v}" ${b.SkillName === v ? 'selected' : ''}>${skillName(v)}</option>`).join('')}${b.SkillName && !SKILLS.includes(b.SkillName) ? `<option selected>${esc(b.SkillName)}</option>` : ''}</select>
      <button class="icon-btn" data-act="buffRemove" data-arg="${i}" title="Remove this effect">✕</button>
      <div class="buff-nums"><label class="mini-f">Value<input type="number" step="any" data-key="b${i}-v" data-bi="${i}" data-bf="Value" value="${b.Value ?? 0}"></label>
      <label class="mini-f">For (s)<input type="number" step="1" data-key="b${i}-d" data-bi="${i}" data-bf="Duration" value="${b.Duration ?? 0}"></label>
      <label class="mini-f">Delay<input type="number" step="1" data-key="b${i}-y" data-bi="${i}" data-bf="Delay" value="${b.Delay ?? 0}"></label>
      <label class="mini-f">Chance<input type="number" step="0.05" min="0" max="1" data-key="b${i}-c" data-bi="${i}" data-bf="Chance" value="${b.Chance ?? 1}"></label></div>
      <div class="buff-say muted small">${esc(buffText(b))}</div></div>`).join('');
  const sharing = m.buffName ? Object.keys(S.meds).filter(x => x !== id && effMed(x).buffName === m.buffName).map(x => S.items.get(x)?.n).filter(Boolean) : [];
  d.innerHTML = `<div class="card hero">${icon(it, true)}<div class="hero-text"><div class="kind" style="color:${k.color}">${esc(k.name.toUpperCase())}</div>
      <div class="hero-name">${esc(it.s || it.n)}</div><div class="muted small">${esc(statsShort(it).replace(/ · ✎$/, ''))}</div></div></div>
    ${S.statEdits[id] || !sameAsDefault(id) ? `<div class="edited-bar"><span>${S.statEdits[id] ? '✎ Edited here' : 'Changed by another mod'}</span><span>${S.statEdits[id] ? '<button class="outline small-btn" data-act="statReset" title="Remove your edit (other mods\' values come back)">Undo My Edit</button>' : ''}${!sameAsDefault(id) ? `<button class="outline small-btn" data-act="statResetDefault" data-arg="${id}" title="Escape From Tarkov's own values">Reset to EFT Default</button>` : ''}</span></div>` : ''}
    <div class="card lvl-card"><h3>Unlocks At (LevelGate)</h3><div class="numrow">${levelCell(id)}<div class="quick">${[1, 5, 10, 15, 20, 25, 30, 40, 50].map(n => `<button class="chip ${levelOf(id) === n ? 'on' : ''}" data-act="quick" data-arg="${n}">${n}</button>`).join('')}</div></div>
      <div class="hint">${levelOf(id) === undefined ? 'No level limit — anyone can use it.' : `Players below level ${levelOf(id)} can't use it.`} Saved to LevelGate's level_requirements.json.</div></div>
    <div class="card"><h3>Use</h3>${num('Use Time', 'use', useField, 0.5, 'seconds')}${resource}</div>
    <div class="card"><h3>${kind === 'food' ? 'Energy & Hydration' : 'Energy & Hydration'}</h3>${health}</div>
    ${kind === 'food' ? '' : `<div class="card"><h3>${kind === 'medkit' || kind === 'medical' ? 'Treats' : 'Removes'}</h3><div class="hint">${kind === 'medkit' || kind === 'medical' ? '"Cost" = HP / uses spent to treat it.' : 'Switched on = the effect is suppressed for that long (painkillers, stims).'}</div>${dmg}</div>`}
    <div class="card"><h3>Effects Over Time<span class="grow"></span><button class="outline small-btn" data-act="buffAdd">+ Add Effect</button></h3>
      ${sharing.length && (m.effects_buffs || []).length ? `<div class="hint">These effects are shared with ${esc(sharing.join(', '))} — changing them changes those too.</div>` : (m.effects_buffs || []).length ? '' : '<div class="hint">No effects over time (stims and some food have them). + Add Effect to give it some.</div>'}
      ${buffs || ''}
      ${JSON.stringify(m.effects_buffs || []) !== JSON.stringify(def.effects_buffs || []) ? `<div class="def-list"><div class="def">Escape From Tarkov default:</div>${(def.effects_buffs || []).map(b => `<div>${esc(buffText(b))}</div>`).join('') || '<div>no effects</div>'}</div>` : ''}</div>
    ${noteCard(id)}
    ${tagCard([id])}`;
  if (keep) d.querySelector(`[data-key="${keep}"]`)?.focus();
}

/** Changes the edited item (as one undo step). */
function editStat(id, mutate) {
  const before = S.statEdits[id] ? JSON.stringify(S.statEdits[id]) : null;
  const m = JSON.parse(JSON.stringify(effMed(id)));
  mutate(m);
  if (!(m.effects_buffs || []).length && String(m.buffName || '').startsWith('ItemStatEditor_')) m.buffName = S.meds[id].buffName ?? '';
  const edit = {};
  for (const k of KEEP) if (m[k] !== undefined) edit[k] = m[k];
  const base = {};
  for (const k of KEEP) if (S.meds[id][k] !== undefined) base[k] = S.meds[id][k];
  if (JSON.stringify(edit) === JSON.stringify(base)) delete S.statEdits[id]; else S.statEdits[id] = edit;
  const after = S.statEdits[id] ? JSON.stringify(S.statEdits[id]) : null;
  if (before === after) return;
  S.undo.push({ stat: id, from: before, to: after }); S.redo = [];
  afterStat(id);
}
function undoStat(step, redo) {
  const v = redo ? step.to : step.from;
  if (v === null) delete S.statEdits[step.stat]; else S.statEdits[step.stat] = JSON.parse(v);
  afterStat(step.stat);
  toast(redo ? 'Redone' : 'Undone');
}
function afterStat(id) {
  if (S.tab === 'stats') { renderPage(); renderNav(); } else updateRow(id);
  renderDetails(); renderHeader(); renderBottom();
}

Object.assign(ACT, {
  statSortBy(key) {
    S.statSort = S.statSort.key === key && key !== 'custom' ? { key, dir: -S.statSort.dir } : { key, dir: key === 'edited' || key === 'price' ? -1 : 1 };
    saveUi(); renderPage(); renderHeader();
  },
  statManyDefault() { const ids = [...S.picked].filter(x => S.meds[x]); ids.forEach(x => ACT.statResetDefault(x)); toast(`${ids.length} reset to Escape From Tarkov's values`); },
  statManyUndo() { const ids = [...S.picked].filter(x => S.statEdits[x]); ids.forEach(x => { S.sel = x; ACT.statReset(); }); toast(`${ids.length} edit(s) undone`); },
  statCat(key) { S.statCat = key; S.page = 'items'; saveUi(); renderAll(); $('#page').scrollTop = 0; },
  editStats(id) { S.statCat = 'all'; S.sel = id; S.picked = new Set(); saveUi(); goTab('stats', true); document.querySelector(`#page .row[data-row="${id}"]`)?.scrollIntoView({ block: 'center' }); },
  statResetDefault(id) {
    id = id || S.sel;
    const d = S.medsDefault[id];
    if (!d) return;
    editStat(id, m => { for (const k of KEEP) { if (d[k] === undefined) delete m[k]; else m[k] = JSON.parse(JSON.stringify(d[k])); } });
    toast('Reset to Escape From Tarkov\'s values');
  },
  statReset() { const id = S.sel; editStat(id, m => { for (const k of KEEP) { if (S.meds[id][k] === undefined) delete m[k]; else m[k] = JSON.parse(JSON.stringify(S.meds[id][k])); } }); },
  buffAdd() {
    const id = S.sel;
    editStat(id, m => {
      if (!m.buffName) m.buffName = `ItemStatEditor_${id}`;
      m.effects_buffs = [...(m.effects_buffs || []), { BuffType: 'HealthRate', Chance: 1, Delay: 1, Duration: 60, Value: 1, AbsoluteValue: true, SkillName: '' }];
    });
  },
  buffRemove(i) { editStat(S.sel, m => { m.effects_buffs.splice(Number(i), 1); }); },
});

// form changes in the Item Stats editor
document.addEventListener('change', e => {
  const el = e.target;
  if (S.tab !== 'stats' || !el.closest?.('#details') || !S.sel || !S.meds[S.sel]) return;
  const id = S.sel, val = el.value === '' ? undefined : Number(el.value);
  if (el.dataset.sf) editStat(id, m => { if (val === undefined || isNaN(val)) delete m[el.dataset.sf]; else m[el.dataset.sf] = Math.max(0, val); });
  else if (el.dataset.eh) editStat(id, m => {
    m.effects_health = { ...(m.effects_health || {}) };
    if (val === undefined || isNaN(val)) delete m.effects_health[el.dataset.eh]; else m.effects_health[el.dataset.eh] = { ...(m.effects_health[el.dataset.eh] || {}), value: val };
  });
  else if (el.dataset.edon) editStat(id, m => {
    m.effects_damage = { ...(m.effects_damage || {}) };
    if (el.checked) m.effects_damage[el.dataset.edon] = { delay: 0, duration: m.kind === 'medkit' ? 0 : 60, fadeOut: 0, ...(m.kind === 'medkit' || m.kind === 'medical' ? { cost: 0 } : {}) };
    else delete m.effects_damage[el.dataset.edon];
  });
  else if (el.dataset.ed) editStat(id, m => { const e2 = m.effects_damage?.[el.dataset.ed]; if (e2) e2[el.dataset.edf] = val === undefined || isNaN(val) ? 0 : val; });
  else if (el.dataset.bi !== undefined) editStat(id, m => {
    const b = m.effects_buffs[Number(el.dataset.bi)];
    if (!b) return;
    const f = el.dataset.bf;
    if (f === 'BuffType') { b.BuffType = el.value; if (el.value !== 'SkillRate') b.SkillName = ''; }
    else if (f === 'SkillName') b.SkillName = el.value;
    else b[f] = val === undefined || isNaN(val) ? 0 : (f === 'Chance' ? clamp(val, 0, 1) : val);
  });
});

/* LevelGate Editor — tags, custom order and the mouse / keyboard moves of the Custom Trader Creator:
   Ctrl-click / Shift-click to pick, drag across rows to pick a box of them, Ctrl+drag a row to move it,
   Alt+↑ / Alt+↓ to move the picked rows. Tags and orders live in the editor's own settings (S.ui),
   never in the game's or other mods' files. */

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

// =====================================================================
// Resizable columns (like the Custom Trader Creator): each divider only trades width
// between its two neighbours; double-click a divider to reset. Kept in S.ui.colf.
// =====================================================================

const COLS = {
  lg: [['item', 380, 160], ['cat', 140, 70], ['stats', 220, 70], ['notes', 170, 60], ['price', 150, 120], ['level', 110, 100]],
  st: [['item', 360, 160], ['type', 130, 70], ['effects', 260, 80], ['notes', 200, 60], ['price', 150, 120], ['level', 110, 100]],
};
function colWeights(list) {
  const saved = S.ui.colf?.[list] || {};
  return COLS[list].map(([k, w]) => (saved[k] > 0 ? saved[k] : w));
}
const colsCss = list => colWeights(list).map((w, i) => `minmax(${COLS[list][i][2]}px, ${Math.round(w * 100) / 100}fr)`).join(' ');
/** The divider at the left edge of a header cell (not before the first column). */
const grip = (list, key) => (COLS[list].findIndex(c => c[0] === key) > 0 ? `<span class="grip" data-grip="${list}|${key}" title="Drag to resize · double-click to reset"></span>` : '');
function applyCols(list) { document.querySelectorAll(`.list[data-cols="${list}"]`).forEach(el => el.style.setProperty('--cols', colsCss(list))); }

let colDrag = null;
document.addEventListener('mousedown', e => {
  const g = e.target.closest('.list-head .grip[data-grip]');
  if (!g || e.button !== 0) return;
  e.preventDefault(); e.stopPropagation();
  const [list, key] = g.dataset.grip.split('|');
  const cols = COLS[list], i = cols.findIndex(c => c[0] === key);
  const cells = [...g.closest('.list-head').children].slice(1); // after "#"
  const px = cols.map((c, n) => cells[n]?.getBoundingClientRect().width || c[1]);
  S.ui.colf = { ...(S.ui.colf || {}), [list]: Object.fromEntries(cols.map((c, n) => [c[0], px[n]])) };
  const listEl = g.closest('.list').getBoundingClientRect();
  const guide = document.createElement('div');
  guide.className = 'col-guide';
  Object.assign(guide.style, { left: e.clientX + 'px', top: listEl.top + 'px', height: Math.min(listEl.height, innerHeight - listEl.top) + 'px' });
  document.body.appendChild(guide);
  g.classList.add('drag'); document.body.classList.add('col-drag');
  colDrag = { list, left: cols[i - 1], right: cols[i], lw: px[i - 1], w: px[i], x: e.clientX, guide, g };
}, true);
document.addEventListener('mousemove', e => {
  if (!colDrag) return;
  const d = colDrag;
  const dx = clamp(e.clientX - d.x, d.left[2] - d.lw, d.w - d.right[2]);
  const w = S.ui.colf[d.list];
  w[d.left[0]] = d.lw + dx; w[d.right[0]] = d.w - dx;
  d.guide.style.left = (d.x + dx) + 'px';
  applyCols(d.list);
});
document.addEventListener('mouseup', () => {
  if (!colDrag) return;
  colDrag.guide.remove(); colDrag.g.classList.remove('drag'); document.body.classList.remove('col-drag');
  colDrag = null;
  saveUi();
});
document.addEventListener('dblclick', e => {
  const g = e.target.closest('.list-head .grip[data-grip]');
  if (!g) return;
  const list = g.dataset.grip.split('|')[0];
  if (S.ui.colf) delete S.ui.colf[list];
  applyCols(list); saveUi(); toast('Column widths reset');
});

// =====================================================================
// Notes (per item, only in this editor)
// =====================================================================

const noteOf = id => S.ui.notes?.[id] || '';
const noteCell = id => `<div class="col note" data-note-cell="${id}" title="${esc(noteOf(id))}">${esc(noteOf(id))}</div>`;
function noteCard(id) {
  return `<div class="card"><h3>Notes</h3><textarea id="noteInput" data-note="${id}" rows="3" spellcheck="true" placeholder="Only you see these (shown in the Notes column).">${esc(noteOf(id))}</textarea></div>`;
}
document.addEventListener('input', e => {
  const el = e.target;
  if (!el.dataset?.note) return;
  const id = el.dataset.note, notes = { ...(S.ui.notes || {}) };
  if (el.value.trim()) notes[id] = el.value; else delete notes[id];
  S.ui.notes = notes;
  document.querySelectorAll(`[data-note-cell="${id}"]`).forEach(c => { c.textContent = el.value; c.title = el.value; });
  saveUi();
});

// =====================================================================
// Progression tab — a Call of Duty style unlock track: five level cards per
// page along the bottom, everything that unlocks at the picked level on top,
// grouped by category, with your quests of that level. Click an item to open
// it in Level Limits; right-click it to change its level right here.
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
  P.page = Math.floor((P.level - 1) / PER_PAGE);
  // every level at once: number + how many unlock there (click to open it)
  const levels = Array.from({ length: MAX_LEVEL }, (_, i) => i + 1).map(l => {
    const n = (by.get(l) || []).length;
    return `<button class="plv ${l === P.level ? 'on' : ''} ${n ? '' : 'empty'}" data-act="progLevel" data-arg="${l}" title="Level ${l}: ${fmt(n)} unlock${n === 1 ? '' : 's'}"><b>${l}</b><span>${n ? fmt(n) : '–'}</span></button>`;
  }).join('');
  return `<div class="prog plain">
    <div class="plv-bar">
      <div class="plv-head"><b>Levels</b><span class="muted small">click a level · ← → next / previous · Q / E five levels</span><span class="grow"></span>
        <label class="switch small" title="Items without a limit can be used from level 1"><input type="checkbox" data-prog-free ${S.ui.progFree !== false ? 'checked' : ''}><span class="track"></span><span>Count Items Without a Limit as Level 1</span></label>
        <label class="pob-src small" title="Where to look for a way to get each item. Vanilla: the game's own traders (Prapor, Therapist…). Modded: your traders made in Modern Editor (switched on), their offers and quest rewards. Both: either one.">Unobtainable Check
          <select data-prog-obtain>${[['vanilla', 'Read Vanilla'], ['modded', 'Read Modded'], ['both', 'Read Both']].map(([v, t]) => `<option value="${v}" ${(S.ui.progObtain || 'both') === v ? 'selected' : ''}>${t}</option>`).join('')}</select></label></div>
      <div class="plv-grid">${levels}</div>
    </div>
    <div class="prog-top" id="progTop">${progTop(by)}</div>
  </div>`;
}

function progTop(by) {
  const list = by.get(P.level) || [];
  const groups = new Map();
  for (const it of list) { const g = groupOf(it); (groups.get(g) || groups.set(g, []).get(g)).push(it); }
  const order = GROUPS.map(g => g[0]).filter(k => groups.has(k));
  const free = S.ui.progFree !== false;
  const limitedHere = list.filter(it => levelOf(it.i) === P.level).length;
  const unobtainable = list.filter(it => !obtainInfo(it.i, S.ui.progObtain || 'both').kind).length; // 2.0.8
  const sections = order.map(k => {
    const g = GROUP[k], items = groups.get(k).sort((a, b) => (a.n || '').localeCompare(b.n || ''));
    const open = P.open.has(k) || items.length <= 60;
    const shown = open ? items : items.slice(0, 60);
    return `<section class="pcat" id="pcat-${k}" style="--c:${g.color}">
      <h3><i></i>${esc(g.name)}<em>${fmt(items.length)}</em></h3>
      <div class="ptiles">${shown.map(it => {
        const own = levelOf(it.i) === P.level;
        return `<button class="ptile ${own ? '' : 'free'}" data-act="progItem" data-arg="${it.i}" title="${esc(it.n)}${own ? '' : ' · no limit'} — click: open in Level Limits · right-click: change its level">
          <div class="pt-pic">${tileIcon(it)}</div>${obtainBadge(it.i, S.ui.progObtain || 'both')}
          <div class="pt-name">${esc(it.n || it.s)}</div>${own ? '' : '<div class="pt-free">no limit</div>'}</button>`;
      }).join('')}</div>
      ${items.length > shown.length ? `<button class="pmore" data-act="progOpen" data-arg="${k}">Show All ${fmt(items.length)}</button>` : ''}
    </section>`;
  }).join('');
  return `<div class="ptop-head">
      <h2 class="ph-level">Level ${P.level}</h2>
      <div class="ph-tier">${fmt(list.length)} unlock${list.length === 1 ? '' : 's'}${P.level === 1 && free ? ` <span class="muted">(${fmt(limitedHere)} set to 1 · ${fmt(list.length - limitedHere)} without a limit)</span>` : ''}${unobtainable ? ` · <span class="ph-unob" title="No trader sells or barters them and no quest gives them">${fmt(unobtainable)} unobtainable</span>` : ''} · right-click an item to change its level</div>
      <div class="ph-chips">${order.map(k => `<button class="pchip" style="--c:${GROUP[k].color}" data-act="progJump" data-arg="${k}">${esc(GROUP[k].name)} <b>${fmt(groups.get(k).length)}</b></button>`).join('')}</div>
    </div>
    <div class="pcats">${questsAtLevelHtml(P.level)}${sections || `<div class="pempty"><div>Nothing unlocks at level ${P.level}.</div><div class="muted small">Set items to this level in Level Limits, or right-click an item on another level.</div></div>`}</div>`;
}

/** Another level picked: only the top half is redrawn, the level grid just moves its highlight. */
function progShowLevel(l) {
  P.level = clamp(l, 1, MAX_LEVEL);
  P.page = Math.floor((P.level - 1) / PER_PAGE);
  P.open = new Set();
  const top = $('#progTop');
  if (!top) { renderPage(); return; }
  document.querySelectorAll('#page .plv').forEach(c => c.classList.toggle('on', Number(c.dataset.arg) === P.level));
  top.innerHTML = progTop(progByLevel());
  top.scrollTop = 0;
  saveProg();
}

function progGoPage(p) {
  p = clamp(p, 0, PROG_PAGES - 1);
  if (p === P.page) return;
  progShowLevel(p * PER_PAGE + 1);
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
    S.page = 'items'; S.cat = groupOf(it); S.filter = 'all'; S.band = null; S.search = ''; $('#search').value = '';
    S.sel = id; S.picked = new Set(); S.limit = 100000;
    saveUi(); goTab('levels', true);
    requestAnimationFrame(() => document.querySelector(`#page .row[data-row="${id}"]`)?.scrollIntoView({ block: 'center' }));
  },
});

document.addEventListener('change', e => {
  if (e.target.matches?.('[data-prog-obtain]')) { S.ui.progObtain = e.target.value; saveUi(); renderPage(); return; } // 2.0.8
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
  else if (k === 'PageDown' || k.toLowerCase() === 'e') go(() => progShowLevel(P.level + 5));
  else if (k === 'PageUp' || k.toLowerCase() === 'q') go(() => progShowLevel(P.level - 5));
  else if (k === 'Home') go(() => progShowLevel(1));
  else if (k === 'End') go(() => progShowLevel(MAX_LEVEL));
});
// right-click an item: change its level right here (no trip to Level Limits)
document.addEventListener('contextmenu', e => {
  const tile = e.target.closest('#page .ptile[data-arg]');
  if (S.tab !== 'prog' || !tile) return;
  e.preventDefault();
  progLevelMenu(tile.dataset.arg, e.clientX, e.clientY);
});
function progLevelMenu(id, x, y) {
  closePopover();
  const it = itemOf(id), lvl = levelOf(id);
  const pop = document.createElement('div');
  pop.className = 'popover menu2 ctx prog-lvl';
  pop.innerHTML = `<div class="menu-title">${esc(it.n)}</div>
    <div class="pl-now">${lvl === undefined ? (isDisabled(id) ? `Switched off (was level ${S.disabled.get(id)})` : 'No limit — usable from level 1') : `Unlocks at level <b>${lvl}</b>`}</div>
    <div class="pl-row"><input type="number" id="plLevel" min="1" max="${MAX_LEVEL}" value="${lvl ?? P.level}"><button class="primary" data-pl="set">Set Level</button></div>
    <div class="quick">${[P.level - 1, P.level + 1, 1, 5, 10, 15, 20, 30, 40].filter((n, i, a) => n >= 1 && n <= MAX_LEVEL && a.indexOf(n) === i).map(n => `<button class="chip ${lvl === n ? 'on' : ''}" data-pl="lvl" data-v="${n}">${n}</button>`).join('')}</div>
    <hr>
    ${lvl !== undefined ? '<button data-pl="off">Switch Limit Off (Keep the Level)</button><button data-pl="remove">Remove Limit</button>' : isDisabled(id) ? '<button data-pl="on">Switch Limit Back On</button>' : ''}
    <button data-pl="open">Open in Level Limits ›</button>`;
  document.body.appendChild(pop);
  pop.style.left = Math.min(x, innerWidth - pop.offsetWidth - 8) + 'px';
  pop.style.top = Math.max(8, Math.min(y, innerHeight - pop.offsetHeight - 8)) + 'px';
  const input = pop.querySelector('#plLevel');
  input.focus(); input.select();
  const done = () => { closePopover(); const top = $('#progTop'), y0 = top?.scrollTop || 0; renderPage(); if ($('#progTop')) $('#progTop').scrollTop = y0; };
  const set = v => { const n = clamp(Math.round(Number(v)), 1, MAX_LEVEL); if (!(n >= 1)) return; setLevels([id], n); toast(`${it.s || it.n} → level ${n}`); done(); };
  input.onkeydown = e => { if (e.key === 'Enter') set(input.value); if (e.key === 'Escape') closePopover(); };
  pop.addEventListener('click', e => {
    const b = e.target.closest('[data-pl]');
    if (!b) return;
    const what = b.dataset.pl;
    if (what === 'set') set(input.value);
    else if (what === 'lvl') set(b.dataset.v);
    else if (what === 'off') { setDisabled([id], true); toast('Limit switched off'); done(); }
    else if (what === 'on') { setDisabled([id], false); toast('Limit switched back on'); done(); }
    else if (what === 'remove') { setLevels([id], undefined); toast('Limit removed'); done(); }
    else if (what === 'open') { closePopover(); ACT.progItem(id); }
  });
  closePopover.fn = ev => { if (!pop.contains(ev.target)) closePopover(); };
  setTimeout(() => document.addEventListener('mousedown', closePopover.fn), 0);
}

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


  S.disabled = S.disabled || new Map(); S.disabledSaved = S.disabledSaved || new Map();
  function disabledChanges() {
    const out = [];
    for (const [id, l] of S.disabled) if (S.disabledSaved.get(id) !== l) out.push(id);
    for (const id of S.disabledSaved.keys()) if (!S.disabled.has(id)) out.push(id);
    return out;
  }

  /** The shell opened one of the item pages. */
  function showTab(t) {
    const keep = S.keepSel; S.keepSel = false;
    S.tab = t; S.page = 'items';
    if (!keep) { S.sel = null; S.picked = new Set(); S.limit = 300; }
    saveUi(); renderAll();
    if (!keep) $('#page').scrollTop = 0;
  }

  return {
    start, applyItems, onHostEvent, showTab, renderAll, renderPage, renderDetails, renderHeader, renderNav,
    save: () => ACT.save(), reload: () => ACT.reload(), undo: () => undo(false), redo: () => undo(true),
    canUndo: () => S.undo.length > 0, canRedo: () => S.redo.length > 0, unsavedCount,
    loaded: () => !!S.configFile, configFile: () => S.configFile, statsFile: () => S.statsFile, statsReadFrom: () => S.statsReadFrom, levelGateVersion: () => S.levelGateVersion,
    levelOf: id => S.levels.get(id), levels: () => S.levels, savedLevels: () => S.saved, items: () => S.items,
    tab: () => S.tab, SHORTCUTS, state: S, api: { setLevels, itemOf, groupOf, GROUP, GROUPS, tierOf, icon },
    addActs: acts => Object.assign(ACT, acts),
  };
})();
