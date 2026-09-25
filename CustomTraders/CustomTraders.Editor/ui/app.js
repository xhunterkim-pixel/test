'use strict';
/* Trader Editor — the whole interface. Talks to the C# window (HostForm.cs)
   through `host.call(method, args)` for everything that touches files. The
   data is the same trader.json the server mod reads (see Shared/TraderModels.cs). */

// =====================================================================
// Game constants (mirror Shared/TraderModels.cs)
// =====================================================================

const CUR = {
  RUB: '5449016a4bdc2d6f028b456f', USD: '5696686a4bdc2da3298b456a', EUR: '569668774bdc2da2298b4568',
  GP: '5d235b4d86f7742e017bc88a', LEGA: '6656560053eaaa7a23349c86',
};
const MONEY = new Set(Object.values(CUR));
const MONEY_NAME = { [CUR.RUB]: '₽ roubles', [CUR.USD]: '$ dollars', [CUR.EUR]: '€ euros', [CUR.GP]: 'GP coins', [CUR.LEGA]: 'Lega medals' };
const MONEY_SYMBOL = { [CUR.RUB]: '₽', [CUR.USD]: '$', [CUR.EUR]: '€', [CUR.GP]: 'GP', [CUR.LEGA]: 'LM' };

const TYPES = ['HandoverItem', 'FindItem', 'Kill', 'Extract', 'UseItem', 'Skill'];
const TYPE_SHORT = { HandoverItem: 'Hand over', FindItem: 'Find', Kill: 'Kill', Extract: 'Extract', UseItem: 'Use item', Skill: 'Skill level' };
const TYPE_LONG = { HandoverItem: 'Hand over items / money', FindItem: 'Find in raid', Kill: 'Kill', Extract: 'Extract', UseItem: 'Use items in raid', Skill: 'Reach a skill level' };
const TYPE_COLOR = { HandoverItem: 'var(--blue)', FindItem: 'var(--green)', Kill: 'var(--red)', Extract: 'var(--orange)', UseItem: 'var(--pink)', Skill: 'var(--yellow)' };
const SKILLS = [
  ['Endurance', 'Endurance'], ['Strength', 'Strength'], ['Vitality', 'Vitality'], ['Health', 'Health'], ['StressResistance', 'Stress resistance'],
  ['Metabolism', 'Metabolism'], ['Immunity', 'Immunity'], ['Perception', 'Perception'], ['Intellect', 'Intellect'], ['Attention', 'Attention'],
  ['Charisma', 'Charisma'], ['Memory', 'Memory'], ['Surgery', 'Surgery'], ['AimDrills', 'Aim drills'], ['TroubleShooting', 'Troubleshooting'],
  ['CovertMovement', 'Covert movement'], ['Search', 'Search'], ['MagDrills', 'Mag drills'], ['LightVests', 'Light vests'], ['HeavyVests', 'Heavy vests'],
  ['WeaponTreatment', 'Weapon maintenance'], ['RecoilControl', 'Recoil control'], ['Crafting', 'Crafting'], ['HideoutManagement', 'Hideout management'],
  ['Pistol', 'Pistols'], ['Revolver', 'Revolvers'], ['SMG', 'SMGs'], ['Assault', 'Assault rifles'], ['Shotgun', 'Shotguns'], ['Sniper', 'Bolt-action rifles'],
  ['DMR', 'Marksman rifles'], ['LMG', 'LMGs'], ['HMG', 'HMGs'], ['Launcher', 'Launchers'], ['AttachedLauncher', 'UBGL'], ['Throwing', 'Throwables'], ['Melee', 'Melee'],
];
const skillName = id => (SKILLS.find(s => s[0] === id) || [id, id || '(pick a skill)'])[1];
const BODY_PARTS = [['Head', 'Head'], ['Chest', 'Chest'], ['Stomach', 'Stomach'], ['LeftArm', 'Left arm'], ['RightArm', 'Right arm'], ['LeftLeg', 'Left leg'], ['RightLeg', 'Right leg']];
const EXIT_STATUSES = [['Survived', 'Survived'], ['Runner', 'Run-through'], ['Killed', 'Killed'], ['MissingInAction', 'Missing in action'], ['Left', 'Left the raid']];
const WAY_COLOR = [null, '#a082ff', '#509bf5', '#f5cd46', '#ff7ab6'];

const MAPS = [
  ['bigmap', 'Customs'], ['factory4_day', 'Factory (day)'], ['factory4_night', 'Factory (night)'], ['Woods', 'Woods'],
  ['Shoreline', 'Shoreline'], ['Interchange', 'Interchange'], ['laboratory', 'The Lab'], ['RezervBase', 'Reserve'],
  ['Lighthouse', 'Lighthouse'], ['TarkovStreets', 'Streets of Tarkov'], ['Sandbox', 'Ground Zero'],
  ['Sandbox_high', 'Ground Zero (21+)'], ['Labyrinth', 'The Labyrinth'],
];
const KILL_TARGETS = [
  ['Any', 'Anyone (PMCs, Scavs, bosses…)'], ['AnyPmc', 'Any PMC'], ['Usec', 'USEC PMCs'], ['Bear', 'BEAR PMCs'],
  ['Savage', 'Scavs (incl. raiders, rogues, bosses)'], ['Boss', 'Bosses (pick which)'],
];
const BOSSES = [
  ['bossBully', 'Reshala'], ['bossKilla', 'Killa'], ['bossKojaniy', 'Shturman'], ['bossGluhar', 'Glukhar'],
  ['bossSanitar', 'Sanitar'], ['bossTagilla', 'Tagilla'], ['bossKnight', 'Knight'], ['followerBigPipe', 'Big Pipe'],
  ['followerBirdEye', 'Birdeye'], ['bossZryachiy', 'Zryachiy'], ['bossBoar', 'Kaban'], ['bossKolontay', 'Kollontay'],
  ['bossPartisan', 'Partisan'], ['sectantPriest', 'Cultist priest'], ['pmcBot', 'Raider'], ['exUsec', 'Rogue'],
];
const REWARD_TYPES = [
  ['Experience', 'XP', '#a082ff'], ['TraderStanding', 'Standing', '#509bf5'], ['Item', 'Item', '#ffa42b'], ['UnlockOffer', 'Unlock offer', '#1ed760'],
  ['Skill', 'Skill XP', '#f5cd46'], ['StashRows', 'Stash rows', '#ff7ab6'],
];
const CATEGORY_FILTERS = [
  ['all', 'All'], ['weapons+', 'Weapons + grenades'], ['Weapon', 'Weapons'], ['Grenade', 'Grenades'], ['Ammo', 'Ammo'],
  ['Gear', 'Gear'], ['Food', 'Food & drink'], ['Meds', 'Medical'], ['Money', 'Money'],
];

const mapName = id => (MAPS.find(m => m[0].toLowerCase() === String(id).toLowerCase()) || [id, id])[1];
const bossName = id => (BOSSES.find(b => b[0] === id) || [id, id])[1];
const wayLetter = n => 'ABCD'[Math.min(4, Math.max(1, n)) - 1];

// =====================================================================
// Host bridge
// =====================================================================

const host = (() => {
  const wv = (window.chrome && window.chrome.webview) || window.devHost;
  const pending = new Map();
  let seq = 0;
  wv.addEventListener('message', e => {
    const m = e.data;
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

const S = {
  modFolder: null,
  traders: [],          // { folder, file, images: {name: url}, avatarColor, dirty }
  items: new Map(),     // id -> { i, n, s, c, k }
  itemsStatus: '',
  ui: {},
  page: 'trader',
  t: null, offer: null, quest: null, cond: null, reward: null, check: null,
  checks: [], history: [], checkFilter: null,
  gameQuests: new Map(), // id -> { i, n, t } (the game's own quests)
  way: 1, tagFilter: null,
  open: new WeakMap(),  // objective -> Set of opened "must ..." sections
};

const $ = sel => document.querySelector(sel);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fmt = n => Number(n || 0).toLocaleString('en-US', { maximumFractionDigits: 2 });
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

function newId() {
  const b = new Uint8Array(12);
  crypto.getRandomValues(b);
  const t = Math.floor(Date.now() / 1000);
  b[0] = t >>> 24; b[1] = (t >>> 16) & 255; b[2] = (t >>> 8) & 255; b[3] = t & 255;
  return [...b].map(x => x.toString(16).padStart(2, '0')).join('');
}
const validId = id => typeof id === 'string' && /^[0-9a-f]{24}$/i.test(id);

// ---- items
const item = id => S.items.get(id);
const itemName = id => !id ? '(nothing picked yet)' : MONEY_NAME[id] && !item(id) ? MONEY_NAME[id] : (item(id)?.n ?? (S.items.size ? `(unknown ${id})` : id));
function shortName(id) {
  const it = item(id);
  if (!it) return itemName(id);
  return it.s && it.n.length > 24 ? it.s : it.n;
}
const initials = s => (s || '?').replace(/\s+/g, '').slice(0, 3);
const isMoney = id => MONEY.has(id);
const moneyShort = id => [CUR.RUB, CUR.USD, CUR.EUR].includes(id) ? MONEY_SYMBOL[id] : MONEY_NAME[id];
const isMoneyList = l => l.length > 0 && l.every(isMoney);
function caliberName(c) {
  let s = c.startsWith('Caliber') ? c.slice(7) : c;
  const x = s.indexOf('x');
  if (x === 2 && ['46', '57', '68', '86', '93'].includes(s.slice(0, 2))) s = s[0] + '.' + s.slice(1);
  else if (x === 3 && /\d/.test(s[0])) s = s[0] + '.' + s.slice(1);
  else if (x === 4 && /\d/.test(s[0])) s = s.slice(0, 2) + '.' + s.slice(2);
  return s.replace(/([0-9])([A-Z]{2,})$/, '$1 $2');
}

// ---- quests
function ways(q) {
  const used = [...new Set(q.conditions.map(c => clamp(c.option || 1, 1, 4)))].sort();
  return used.length ? used : [1];
}
function allQuests() {
  return S.traders.flatMap(t => t.file.quests.map(q => ({ t, q })));
}
function questsUnlocking(t, offer) {
  return t.file.quests.flatMap(q => q.rewards.filter(r => r.type === 'UnlockOffer' && r.offerId === offer.id).map(r => ({ q, r })));
}

// =====================================================================
// Loading & normalising trader files
// =====================================================================

const def = (o, k, v) => { if (o[k] === undefined || o[k] === null) o[k] = v; };

/** Every field an objective can have (new objectives and old files alike). */
function fillCond(c) {
  def(c, 'id', newId()); def(c, 'type', 'HandoverItem'); def(c, 'option', 1); def(c, 'text', ''); def(c, 'count', 1);
  def(c, 'foundInRaid', true); def(c, 'killTarget', 'Any');
  for (const k of ['itemTpls', 'bossRoles', 'weaponTpls', 'calibers', 'wearingTpls', 'locations', 'bodyParts', 'exitStatuses']) def(c, k, []);
  def(c, 'distance', 0); def(c, 'distanceCompare', '>='); def(c, 'daytimeFrom', 0); def(c, 'daytimeTo', 0); def(c, 'oneRaid', false); def(c, 'skill', '');
  return c;
}
function fillReward(r) {
  def(r, 'id', newId()); def(r, 'type', 'Experience'); def(r, 'value', 0); def(r, 'itemTpl', ''); def(r, 'count', 1);
  def(r, 'foundInRaid', true); def(r, 'offerId', ''); def(r, 'quantity', 0); def(r, 'onStart', false); def(r, 'skill', '');
  return r;
}
function fillQuest(q) {
  def(q, 'id', newId()); def(q, 'name', 'Quest'); def(q, 'description', ''); def(q, 'successMessage', '');
  def(q, 'minLevel', 1); def(q, 'prerequisiteQuestIds', []); def(q, 'conditions', []); def(q, 'rewards', []);
  def(q, 'failOnDeath', false); def(q, 'tags', []);
  q.conditions.forEach(fillCond);
  q.rewards.forEach(fillReward);
  return q;
}
function fillOffer(o) {
  def(o, 'id', newId()); def(o, 'itemTpl', ''); def(o, 'useDefaultPreset', true); def(o, 'loyaltyLevel', 1);
  def(o, 'unlimited', true); def(o, 'stock', 1); def(o, 'buyLimit', 0); def(o, 'cost', []);
  return o;
}

function normalize(entry) {
  const f = entry.file;
  def(f, 'name', entry.folder); def(f, 'nickname', f.name); def(f, 'surname', ''); def(f, 'location', ''); def(f, 'description', '');
  def(f, 'currency', 'RUB'); def(f, 'unlockedByDefault', true); def(f, 'enabled', true); def(f, 'listOnFlea', true); def(f, 'unlockQuestId', '');
  def(f, 'avatar', 'avatar.png'); def(f, 'refreshMinutesMin', 60); def(f, 'refreshMinutesMax', 120);
  def(f, 'loyaltyLevels', [{ minLevel: 1, minSalesSum: 0, minStanding: 0, buyPriceCoef: 50 }]);
  def(f, 'offers', []); def(f, 'quests', []);
  f.offers.forEach(fillOffer);
  for (const q of f.quests) {
    fillQuest(q);
    for (const r of q.rewards) {
      // The offer's own stock is the one setting now: move an old "stock after unlock" onto the offer.
      if (r.type === 'UnlockOffer' && r.quantity > 0) {
        const o = f.offers.find(x => x.id === r.offerId);
        if (o) {
          o.unlimited = false; o.stock = r.quantity; r.quantity = 0; entry.dirty = true;
          log('info', f.name, `Moved "stock after unlock" (${o.stock}) of ${q.name} onto the offer ${itemName(o.itemTpl)} — save to keep it.`, entry, o);
        }
      }
    }
  }
  return entry;
}

function applySnapshot(snap) {
  S.modFolder = snap.modFolder;
  S.ui = snap.ui || S.ui || {};
  S.items = new Map((snap.items || []).map(i => [i.i, i]));
  S.gameQuests = new Map((snap.gameQuests || []).map(q => [q.i, q]));
  S.itemsStatus = snap.itemsStatus || '';
  S.traders = (snap.traders || []).map(t => {
    const e = normalize({ ...t, dirty: false });
    e.migrated = e.dirty;                 // changed on load (old format) — stays unsaved until saved
    e.savedJson = JSON.stringify(e.file);
    return e;
  });
  resetHistory();
  for (const p of snap.problems || []) log('error', 'Load', p);
  applyBackground(snap.background);
  applyUi();
  const keep = S.t && S.traders.find(t => t.folder === S.t.folder);
  selectTrader(keep || S.traders[0] || null, false);
  $('#folder').textContent = S.modFolder || 'Click Browse… and pick your SPT_Runtime\\user\\mods\\CustomTraders folder';
  $('#itemsStatus').textContent = S.itemsStatus.length < 30 ? S.itemsStatus : '';
  if (S.itemsStatus && S.itemsStatus.length >= 30) log('warning', 'Items', S.itemsStatus);
  status(S.modFolder ? `Loaded ${S.traders.length} trader(s). Restart the SPT server after saving to apply changes.` : 'Click Browse… and pick the CustomTraders mod folder.');
  runChecks();
  renderAll(true);
}

// =====================================================================
// Rendering: bindings
// =====================================================================

let B = new Map();          // binding key -> { get, set, refresh }
let L = new Map();          // list key -> array (for inline remove buttons)
let bindSeq = 0;

function bind(get, set, refresh = 'light') {
  const key = 'b' + (++bindSeq);
  B.set(key, { get, set, refresh });
  return key;
}
function listRef(arr) {
  const key = 'l' + (++bindSeq);
  L.set(key, arr);
  return key;
}

const ui = {
  text(label, get, set, opts = {}) {
    const k = bind(get, set, opts.refresh);
    return `<div class="field"><label>${esc(label)}</label><input type="text" data-b="${k}" value="${esc(get())}" spellcheck="false"></div>`;
  },
  area(label, get, set, opts = {}) {
    const k = bind(get, set, opts.refresh);
    return `<div class="field top"><label>${esc(label)}</label><textarea data-b="${k}" rows="${opts.rows || 4}" spellcheck="false">${esc(get())}</textarea></div>`;
  },
  num(label, get, set, opts = {}) {
    const k = bind(get, v => set(clamp(v, opts.min ?? -1e12, opts.max ?? 1e12)), opts.refresh);
    return `<div class="field"><label>${esc(label)}</label><input type="number" data-b="${k}" value="${esc(get())}" min="${opts.min ?? ''}" max="${opts.max ?? ''}" step="${opts.step ?? 1}"></div>`;
  },
  toggle(text, get, set, opts = {}) {
    const k = bind(get, set, opts.refresh ?? 'details');
    const sw = `<label class="switch"><input type="checkbox" data-b="${k}" ${get() ? 'checked' : ''}><span class="track"></span><span>${esc(text)}</span></label>`;
    return opts.label !== undefined ? `<div class="field"><label>${esc(opts.label)}</label>${sw}</div>` : sw;
  },
  chips(label, values, get, set, opts = {}) {
    const k = bind(get, set, opts.refresh ?? 'details');
    const cur = get();
    const chips = values.map(([v, t, c]) => `<button class="chip ${String(cur) === String(v) ? 'on' : ''}" style="${c ? `--c:${c}` : ''}" data-chip="${k}" data-v="${esc(v)}">${esc(t)}</button>`).join('');
    return `<div class="stack">${label ? `<label>${esc(label)}</label>` : ''}<div class="chips">${chips}</div></div>`;
  },
  multichips(label, values, list, opts = {}) {
    const k = listRef(list);
    const chips = values.map(([v, t]) => `<button class="chip ${list.some(x => String(x).toLowerCase() === String(v).toLowerCase()) ? 'on' : ''}" style="--c:${opts.color || '#fff'}" data-act="toggleIn" data-arg="${k}|${esc(v)}">${esc(t)}</button>`).join('');
    return `<div class="stack">${label ? `<label>${esc(label)}</label>` : ''}<div class="chips">${chips}</div></div>`;
  },
  select(label, options, get, set, opts = {}) {
    const k = bind(get, set, opts.refresh ?? 'details');
    const cur = get();
    const o = options.map(([v, t]) => `<option value="${esc(v)}" ${String(cur) === String(v) ? 'selected' : ''}>${esc(t)}</option>`).join('');
    return `<div class="field"><label>${esc(label)}</label><select data-b="${k}">${!options.some(x => String(x[0]) === String(cur)) ? '<option value="" selected>(pick one)</option>' : ''}${o}</select></div>`;
  },
  item(label, get, set, filter = 'all') {
    const k = bind(get, set, 'details');
    const id = get();
    const missing = !id || (S.items.size && !item(id));
    return `<div class="field"><label>${esc(label)}</label><div class="itemline"><span class="name ${missing ? 'missing' : ''}">${esc(itemName(id))}</span>
      <button class="outline" data-act="pickInto" data-arg="${k}|${filter}">Pick…</button></div></div>`;
  },
  hint: text => `<div class="hint">${text}</div>`,
  badge: (text, color) => `<span class="badge" style="--c:${color}">${esc(text)}</span>`,
};

/** A section card with a ▾ to fold it away (remembered). */
function card(key, title, body, opts = {}) {
  const folded = !!S.ui.folded?.[key];
  return `<div class="card ${folded ? 'folded' : ''} ${opts.cls || ''}" ${opts.style ? `style="${opts.style}"` : ''}>
    <h3 class="card-head"><button class="fold" data-act="fold" data-arg="${key}" title="${folded ? 'Show' : 'Hide'}">▾</button>
      <span data-act="fold" data-arg="${key}" class="card-title">${title}</span><span class="grow"></span>${opts.actions || ''}</h3>
    <div class="card-body">${body}</div></div>`;
}

const TAG_COLORS = ['#ff7ab6', '#5cc8ff', '#f5cd46', '#a082ff', '#1ed760', '#ffa42b', '#ff6b6b', '#7ee0c3'];
function tagColor(tag) {
  let h = 0;
  for (const ch of tag.toLowerCase()) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return TAG_COLORS[h % TAG_COLORS.length];
}
const isBarter = o => o.cost.some(c => !isMoney(c.itemTpl));
const priceKind = o => o.cost.length === 0 ? null : isBarter(o) ? ['BARTER', 'var(--pink)'] : ['BUY', 'var(--blue)'];

/** Name of any quest id: one of yours ("Trader: Quest") or a game quest ("Quest (Prapor)"). */
function questLabel(id) {
  const mine = allQuests().find(x => x.q.id === id);
  if (mine) return `${mine.t.file.name}: ${mine.q.name}`;
  const g = S.gameQuests.get(id);
  if (g) return `${g.n}${g.t ? ` (${g.t})` : ''}`;
  return `unknown quest ${id}`;
}
const chainLabel = c => c.game ? `${c.game.n}${c.game.t ? ` (${c.game.t}, game quest)` : ' (game quest)'}` : `${c.t.file.name}: ${c.q.name}`;
const chainOn = c => c.game || c.t.file.enabled;

function thumb(t) {
  if (t.img) return `<img class="thumb" src="${esc(t.img)}" alt="">`;
  const dark = t.dark ? ' dark' : '';
  return `<div class="thumb${dark}" style="--tc:${t.color || '#3a3a3a'}">${esc(t.text || '')}</div>`;
}

// =====================================================================
// Rendering: whole screen
// =====================================================================

let lightPending = false;
function renderLight() {
  if (lightPending) return;
  lightPending = true;
  requestAnimationFrame(() => {
    lightPending = false;
    renderTraders();
    renderHeader();
    if (S.page !== 'trader') renderPage(false);
    renderBottom();
  });
}

function renderAll(animate) {
  renderTraders();
  renderHeader();
  renderPage(animate);
  renderDetails(animate, true);
  renderBottom();
}

function animateIn(el) {
  el.classList.remove('enter');
  void el.offsetWidth;
  el.classList.add('enter');
}

// ---------------------------------------------------------------- traders (left)

function renderTraders() {
  const html = S.traders.map((t, i) => {
    const f = t.file;
    const errors = S.checks.filter(c => c.trader === t && c.level === 'error').length;
    const img = t.images[f.avatar];
    return `<div class="trader ${t === S.t ? 'sel' : ''} ${f.enabled ? '' : 'off'}" data-act="selTrader" data-arg="${i}">
      ${img ? `<img src="${esc(img)}" alt="">` : '<div class="ph"></div>'}
      <div style="min-width:0"><div class="line1"><span class="name">${esc(f.name)}</span>
        ${f.enabled ? '' : ui.badge('OFF', '#b3b3b3')}${t.dirty ? ui.badge('•', 'var(--pink)') : ''}${errors ? ui.badge(errors + ' ✖', 'var(--red)') : ''}</div>
      <div class="sub">${f.offers.length} offers · ${f.quests.length} quests</div></div></div>`;
  }).join('');
  $('#traders').innerHTML = html || '<div class="empty">No traders yet<br>click ＋</div>';
}

// ---------------------------------------------------------------- header

function renderHeader() {
  const t = S.t;
  const f = t?.file;
  const img = t && t.images[f.avatar];
  const avatar = $('#headerAvatar');
  if (img) { if (avatar.getAttribute('src') !== img) avatar.src = img; avatar.hidden = false; } else avatar.hidden = true;
  $('#headerKind').textContent = f && !f.enabled ? 'TRADER · SWITCHED OFF' : 'TRADER';
  $('#headerKind').style.color = f && !f.enabled ? 'var(--orange)' : '';
  $('#headerTitle').textContent = f?.name || 'No trader';
  $('#headerSub').textContent = !f ? 'Click ＋ on the left to make a trader.' :
    `${f.offers.length} offer(s) · ${f.quests.length} quest(s) · restocks every ${f.refreshMinutesMin}–${f.refreshMinutesMax} min · ${f.unlockedByDefault ? 'unlocked from the start' : 'locked at start'}`;
  $('#header').style.setProperty('--hc', headerColor(t?.avatarColor));
  document.querySelectorAll('#chips .chip').forEach(c => c.classList.toggle('on', c.dataset.arg === S.page));
}

function headerColor(hex) {
  if (!hex) return '#3b3b3b';
  const n = parseInt(hex.slice(1), 16);
  let r = n >> 16, g = (n >> 8) & 255, b = n & 255;
  const light = r * .3 + g * .59 + b * .11;
  const k = light > 150 ? .6 : light < 40 ? 1.6 : 1; // keep it colorful but not glaring
  r = clamp(Math.round(r * k), 0, 255); g = clamp(Math.round(g * k), 0, 255); b = clamp(Math.round(b * k), 0, 255);
  return `rgb(${r},${g},${b})`;
}

// ---------------------------------------------------------------- page (center)

function renderPage(animate) {
  const page = $('#page');
  const scroll = page.scrollTop;
  B = new Map([...B].filter(([k, v]) => v.zone !== 'page'));
  const zoneStart = bindSeq;
  let html = '';
  if (!S.t && S.page !== 'checks') html = '<div class="empty">No trader selected.</div>';
  else if (S.page === 'trader') html = pageTrader();
  else if (S.page === 'offers') html = pageOffers();
  else if (S.page === 'quests') html = pageQuests();
  else html = pageChecks();
  for (const [k, v] of B) if (Number(k.slice(1)) > zoneStart) v.zone = 'page';
  page.innerHTML = html;
  applyColumns();
  if (animate) { page.scrollTop = 0; animateIn(page); } else page.scrollTop = scroll;
}

function pageTrader() {
  const f = S.t.file;
  const set = (k, after) => v => { f[k] = v; after?.(); };
  const cur = { USD: '$', EUR: '€' }[f.currency] || '₽';
  const loyalty = f.loyaltyLevels.map((l, i) => {
    const cell = (k, step, min, max) => {
      const b = bind(() => l[k], v => { l[k] = clamp(v, min, max); }, 'light');
      return `<td><input type="number" data-b="${b}" value="${esc(l[k])}" step="${step}" min="${min}" max="${max}"></td>`;
    };
    return `<tr><td>LL${i + 1}</td>${cell('minLevel', 1, 1, 79)}${cell('minSalesSum', 10000, 0, 1e12)}${cell('minStanding', .01, -10, 10)}${cell('buyPriceCoef', 1, 0, 100)}</tr>`;
  }).join('');
  const unlock = f.unlockedByDefault ? '' : `<div class="field"><label>Unlocked by</label><div class="itemline">
      <span class="name ${f.unlockQuestId ? '' : 'missing'}">${esc(f.unlockQuestId ? questLabel(f.unlockQuestId) : 'nothing yet — pick the quest that unlocks this trader')}</span>
      <button class="outline" data-act="pickTraderUnlock">Pick quest…</button></div></div>
      ${ui.hint("The game unlocks traders with a quest reward: completing that quest (one of yours or one of the game's) unlocks this trader. For a level requirement, use a quest that unlocks at that level.")}`;
  return `<div class="big-fields">${card('t-main', 'Trader', `
    ${ui.toggle('Trader is ON (off = the server skips it; nothing is deleted)', () => f.enabled, set('enabled'), { label: 'In the game', refresh: 'light' })}
    ${ui.text('Name', () => f.name, set('name'))}
    ${ui.text('Nickname', () => f.nickname, set('nickname'))}
    ${ui.text('Surname', () => f.surname, set('surname'))}
    ${ui.text('Location', () => f.location, set('location'))}
    ${ui.area('Description', () => f.description, set('description'))}
    <div class="field"><label>Currency</label>${ui.chips('', [['RUB', '₽ Roubles'], ['USD', '$ Dollars'], ['EUR', '€ Euros']], () => f.currency, set('currency'), { refresh: 'page' })}</div>
    ${ui.toggle('Available from the start', () => f.unlockedByDefault, set('unlockedByDefault'), { label: 'Unlocked', refresh: 'page' })}
    ${unlock}
    ${ui.toggle("List this trader's offers on the flea market", () => f.listOnFlea, set('listOnFlea'), { label: 'Flea market', refresh: 'light' })}
    ${ui.num('Restock every (min)', () => f.refreshMinutesMin, set('refreshMinutesMin'), { min: 1, max: 10080 })}
    ${ui.num('…up to (min)', () => f.refreshMinutesMax, set('refreshMinutesMax'), { min: 1, max: 10080 })}`)}
  ${card('t-loyalty', 'Loyalty levels', `
    ${ui.hint(`What a player needs for each loyalty level (LL1 – LL4). Offers can require a loyalty level. "Spent" is counted in the trader's currency (${{ USD: 'dollars', EUR: 'euros' }[f.currency] || 'roubles'}) — the currency only decides this and what the trader pays when players sell to them; each offer's price is set on the offer. "Buys at %" = how much of an item's value the trader pays.`)}
    <table class="loyalty"><tr><th></th><th>Player level</th><th>Spent (${cur})</th><th>Standing</th><th>Buys at %</th></tr>${loyalty}</table>
    <div class="toolbar">
      <button class="outline" data-act="addLoyalty" ${f.loyaltyLevels.length >= 4 ? 'disabled' : ''}>+ Loyalty level</button>
      <button class="danger" data-act="removeLoyalty" ${f.loyaltyLevels.length <= 1 ? 'disabled' : ''}>Remove last</button>
    </div>`)}</div>`;
}

const COLUMNS = {
  offers: [['Title', null], ['Unlock', 230], ['LL', 60], ['Stock', 120]],
  quests: [['Title', null], ['Level', 80], ['Ways', 70]],
  checks: [['Title', null], ['When', 90]],
};
function colWidths(list) {
  const saved = S.ui.cols?.[list] || [];
  return COLUMNS[list].slice(1).map((c, i) => clamp(saved[i] || c[1], 50, 700));
}
function applyColumns() {
  document.querySelectorAll('.list[data-list]').forEach(el => {
    const w = colWidths(el.dataset.list);
    el.style.setProperty('--cols', `minmax(160px,1fr) ${w.map(x => x + 'px').join(' ')}`);
  });
}
function listHead(list) {
  return `<div class="list-head"><div>#</div>${COLUMNS[list].map((c, i) => `<div>${i > 0 ? `<span class="grip" data-grip="${list}|${i - 1}" title="Drag to resize"></span>` : ''}<span class="head-text">${esc(c[0])}</span></div>`).join('')}</div>`;
}

/** A line of small dark boxes: [{ label, text, color }] — label is drawn in the color (e.g. the way letter). */
function boxes(list) {
  return `<div class="boxes">${list.map(b => `<span class="box" style="--c:${b.color || 'var(--muted)'}">${b.label ? `<b>${esc(b.label)}</b>` : ''}${esc(b.text)}</span>`).join('')}</div>`;
}

function row({ act, arg, sel, three, thumb: th, title, titleColor, badges = [], line2, line3, line3Color, boxes2, boxes3, cols = [], colColors = [] }) {
  return `<div class="row ${sel ? 'sel' : ''} ${three ? 'three' : ''}" data-act="${act}" data-arg="${arg}">
    <div class="idx">${Number(arg) + 1}</div>
    <div class="cell">${thumb(th)}<div class="text">
      <div class="line1"><span class="title" ${titleColor ? `style="color:${titleColor}"` : ''}>${esc(title)}</span><span class="badges">${badges.map(b => ui.badge(b[0], b[1])).join('')}</span></div>
      ${boxes2 ? boxes(boxes2) : line2 ? `<div class="line2">${esc(line2)}</div>` : ''}
      ${boxes3 ? boxes(boxes3) : line3 ? `<div class="line3" ${line3Color ? `style="color:${line3Color}"` : ''}>${esc(line3)}</div>` : ''}
    </div></div>
    ${cols.map((c, i) => `<div class="col" ${colColors[i] ? `style="color:${colColors[i]}"` : ''}>${esc(c)}</div>`).join('')}
  </div>`;
}

function offerUnlock(t, o) {
  const mine = questsUnlocking(t, o);
  if (mine.length) return { kind: 'mine', text: '🔒 ' + mine.map(u => u.q.name).join(', '), badge: ['QUEST', 'var(--orange)'] };
  if (o.unlockedByQuestId) return { kind: 'game', text: '🔒 ' + questLabel(o.unlockedByQuestId), badge: ['GAME QUEST', 'var(--orange)'] };
  return { kind: 'start', text: 'From start', badge: null };
}

function pageOffers() {
  const t = S.t;
  const rows = t.file.offers.map((o, i) => {
    const u = offerUnlock(t, o);
    const price = priceKind(o);
    const badges = [];
    if (u.badge) badges.push(u.badge);
    if (price) badges.push(price);
    if (o.loyaltyLevel > 1) badges.push([`LL${o.loyaltyLevel}`, 'var(--violet)']);
    return row({
      act: 'selOffer', arg: i, sel: o === S.offer,
      thumb: { text: initials(item(o.itemTpl)?.s || itemName(o.itemTpl)), color: u.kind !== 'start' ? 'var(--orange)' : '#3a3a3a', dark: u.kind !== 'start' },
      title: itemName(o.itemTpl), badges,
      line2: costText(o),
      cols: [u.text, `LL${o.loyaltyLevel}`, (o.unlimited ? 'Unlimited' : `${o.stock} / restock`) + (o.buyLimit > 0 ? ` · max ${o.buyLimit}` : '')],
      colColors: [u.kind !== 'start' ? 'var(--orange)' : 'var(--green)'],
    });
  }).join('');
  return `<div class="toolbar sticky">
      <button class="primary" data-act="addOffer">+ Add offer</button>
      <button class="outline" data-act="dupOffer" ${S.offer ? '' : 'disabled'}>Duplicate</button>
      <button class="danger" data-act="removeOffer" ${S.offer ? '' : 'disabled'}>Remove</button>
      <button class="icon-btn" data-act="moveOffer" data-arg="-1" title="Move up">▲</button>
      <button class="icon-btn" data-act="moveOffer" data-arg="1" title="Move down">▼</button>
    </div>
    <div class="list" data-list="offers">${listHead('offers')}${rows || '<div class="empty">No offers yet — click + Add offer</div>'}</div>`;
}

function pageQuests() {
  const t = S.t;
  const allTags = [...new Set(t.file.quests.flatMap(q => q.tags))].sort((a, b) => a.localeCompare(b));
  if (S.tagFilter && !allTags.includes(S.tagFilter)) S.tagFilter = null;
  const rows = t.file.quests.map((q, i) => {
    if (S.tagFilter && !q.tags.includes(S.tagFilter)) return '';
    const w = ways(q);
    const badges = [[`${w.length} WAY${w.length > 1 ? 'S' : ''}`, 'var(--violet)']];
    if (q.failOnDeath) badges.push(['HARDCORE', 'var(--red)']);
    if (q.prerequisiteQuestIds.length) badges.push([`AFTER ${q.prerequisiteQuestIds.length} QUEST${q.prerequisiteQuestIds.length > 1 ? 'S' : ''}`, 'var(--blue)']);
    for (const tag of q.tags) badges.push([tag.toUpperCase(), tagColor(tag)]);
    if (S.checks.some(c => c.target === q && c.level === 'error')) badges.push(['✖ PROBLEM', 'var(--red)']);
    const img = q.image && t.images[q.image];
    return row({
      act: 'selQuest', arg: i, sel: q === S.quest, three: true,
      thumb: img ? { img } : { text: String(q.minLevel), color: 'var(--violet)' },
      title: q.name, badges,
      boxes2: q.conditions.length
        ? w.map(o => ({ label: w.length > 1 ? wayLetter(o) : '', color: WAY_COLOR[o], text: q.conditions.filter(c => (c.option || 1) === o).map(shortCondition).join(' + ') }))
        : [{ text: 'No objectives yet' }],
      boxes3: q.rewards.length ? rewardBoxes(t, q) : [{ text: 'No rewards yet' }],
      cols: [`Lvl ${q.minLevel}`, String(w.length)],
    });
  }).join('');
  const tagBar = allTags.length ? `<div class="toolbar"><span class="muted small">Tags:</span>
      <button class="chip ${!S.tagFilter ? 'on' : ''}" data-act="tagFilter" data-arg="">All</button>
      ${allTags.map(tag => `<button class="chip ${S.tagFilter === tag ? 'on' : ''}" style="--c:${tagColor(tag)}" data-act="tagFilter" data-arg="${esc(tag)}">${esc(tag)}</button>`).join('')}</div>` : '';
  return `<div class="toolbar sticky">
      <button class="primary" data-act="addQuest">+ Add quest</button>
      <button class="outline" data-act="dupQuest" ${S.quest ? '' : 'disabled'}>Duplicate</button>
      <button class="danger" data-act="removeQuest" ${S.quest ? '' : 'disabled'}>Remove</button>
      <button class="icon-btn" data-act="moveQuest" data-arg="-1" title="Move up">▲</button>
      <button class="icon-btn" data-act="moveQuest" data-arg="1" title="Move down">▼</button>
    </div>${tagBar}
    <div class="list" data-list="quests">${listHead('quests')}${rows || '<div class="empty">No quests yet — click + Add quest</div>'}</div>`;
}

function pageChecks() {
  const all = [...S.history, ...[...S.checks].sort((a, b) => LEVELS.indexOf(a.level) - LEVELS.indexOf(b.level))];
  const shown = all.filter(e => !S.checkFilter || e.level === S.checkFilter);
  const count = l => all.filter(e => e.level === l).length;
  const filter = (lvl, text) => `<button class="chip ${S.checkFilter === lvl ? 'on' : ''}" data-act="checkFilter" data-arg="${lvl || ''}">${text}</button>`;
  const rows = shown.map((e, i) => row({
    act: 'selCheck', arg: i, sel: e === S.check,
    thumb: { text: LEVEL_ICON[e.level], color: `var(--${LEVEL_COLOR[e.level]})`, dark: e.level !== 'info' },
    title: e.message, line2: e.where, cols: [e.time],
  })).join('');
  S.shownChecks = shown;
  return `<div class="toolbar sticky">
      <button class="primary" data-act="runChecks">Run checks</button>
      ${filter(null, 'All')}${filter('error', `Errors (${count('error')})`)}${filter('warning', `Warnings (${count('warning')})`)}
      ${filter('info', `Info (${count('info')})`)}${filter('ok', 'OK')}
    </div>
    <div class="list" data-list="checks">${listHead('checks')}${rows || '<div class="empty">Nothing to show</div>'}</div>`;
}

// ---------------------------------------------------------------- details (right)

function renderDetails(animate, toTop) {
  const box = $('#details');
  const scroll = box.scrollTop;
  B = new Map([...B].filter(([, v]) => v.zone === 'page'));
  L = new Map();
  let title = '', html = '';
  if (S.page === 'checks') [title, html] = detailsCheck();
  else if (!S.t) [title, html] = ['', '<div class="empty">Pick or create a trader on the left.</div>'];
  else if (S.page === 'trader') [title, html] = detailsTrader();
  else if (S.page === 'offers') [title, html] = detailsOffer();
  else [title, html] = detailsQuest();
  $('#detailsTitle').textContent = title;
  box.innerHTML = html;
  box.scrollTop = toTop ? 0 : scroll;
  if (animate) animateIn(box);
}

function detailsTrader() {
  const t = S.t, f = t.file;
  const img = t.images[f.avatar];
  const locked = f.offers.filter(o => offerUnlock(t, o).kind !== 'start').length;
  const minLvl = f.quests.length ? Math.min(...f.quests.map(q => q.minLevel)) : 0;
  return [f.name.toUpperCase(), `
    ${img ? `<img class="bigavatar" src="${esc(img)}" alt="">` : '<div class="bigavatar"></div>'}
    <div class="toolbar"><button class="primary" data-act="chooseAvatar">Choose icon from PC…</button><button class="outline" data-act="openFolder">Open folder</button></div>
    ${ui.hint('Any png or jpg; it\'s cropped to a square. The game shows the new icon after you Save and restart the SPT server.')}
    ${card('t-summary', 'Summary', `<div class="req">Sells ${f.offers.length} thing(s), ${locked} of them unlocked by quests.
${f.quests.length} quest(s)${f.quests.length ? `, from level ${minLvl}` : ''}.
Pays ${f.loyaltyLevels[0]?.buyPriceCoef ?? 0}% of an item's value when players sell to them.
${f.unlockedByDefault ? 'Unlocked from the start.' : `Locked at start — unlocked by ${f.unlockQuestId ? questLabel(f.unlockQuestId) : 'nothing yet!'}.`}
${f.enabled ? 'Switched ON — loads into the game.' : 'Switched OFF — the server skips this trader.'}</div>`)}`];
}

function detailsOffer() {
  const t = S.t, o = S.offer;
  if (!o) return ['Offers & barters', '<div class="empty">Pick an offer, or click + Add offer.</div>'];
  const unl = questsUnlocking(t, o);
  const u = offerUnlock(t, o);
  const isWeapon = item(o.itemTpl)?.c === 'Weapon';
  const price = priceKind(o);
  const status = u.kind === 'mine'
    ? `<div class="status" style="--c:var(--orange)"><h3>🔒 Locked — unlocked by your quest<span class="grow"></span><button class="outline" data-act="goQuest" data-arg="${esc(unl[0].q.id)}">Go to quest</button></h3>
        <div class="req">${unl.map(x => `• ${esc(x.q.name)} (level ${x.q.minLevel})`).join('\n')}</div></div>`
    : u.kind === 'game'
      ? `<div class="status" style="--c:var(--orange)"><h3>🔒 Locked — unlocked by a game quest</h3><div class="req">• ${esc(questLabel(o.unlockedByQuestId))}</div></div>`
      : `<div class="status" style="--c:var(--green)"><h3>✔ For sale from the start</h3>${ui.hint(`Anyone with LL${o.loyaltyLevel} with ${esc(t.file.name)} can buy it.`)}</div>`;
  const unlockBody = `
    ${ui.chips('', [['start', 'From the start'], ['mine', 'After one of my quests'], ['game', 'After a game quest']], () => u.kind, v => setOfferUnlock(o, v))}
    ${u.kind === 'mine' ? `<div class="switches">${t.file.quests.map(q => ui.toggle(`${q.name} · lvl ${q.minLevel}`, () => unl.some(x => x.q === q), v => toggleQuestUnlocksOffer(q, o, v))).join('')}</div>
      ${ui.hint('Switching a quest on gives it an "Unlock offer" reward for this offer (you\'ll see it in the quest\'s rewards).')}` : ''}
    ${u.kind === 'game' ? `<div class="itemline"><span class="name">${esc(o.unlockedByQuestId ? questLabel(o.unlockedByQuestId) : '(pick a quest)')}</span><button class="outline" data-act="pickOfferGameQuest">Pick game quest…</button></div>` : ''}
    ${o.loyaltyLevel > 1 ? ui.hint(`Also needs LL${o.loyaltyLevel} with ${esc(t.file.name)}.`) : ''}`;
  const costs = o.cost.map((c, i) => {
    const k = bind(() => c.count, v => { c.count = Math.max(1, v); }, 'light');
    const lk = listRef(o.cost);
    return `<div class="row"><div class="cell">${thumb({ text: isMoney(c.itemTpl) ? MONEY_SYMBOL[c.itemTpl] : initials(item(c.itemTpl)?.s), color: isMoney(c.itemTpl) ? 'var(--yellow)' : '#3a3a3a', dark: isMoney(c.itemTpl) })}
      <div class="text"><div class="title">${esc(isMoney(c.itemTpl) ? MONEY_NAME[c.itemTpl] : itemName(c.itemTpl))}</div></div>
      <input type="number" data-b="${k}" value="${c.count}" min="1" style="width:140px"><button class="icon-btn" data-act="removeAt" data-arg="${lk}|${i}" title="Remove">✕</button></div></div>`;
  }).join('');
  return [itemName(o.itemTpl), `${status}
    ${card('o-unlock', 'How it unlocks', unlockBody)}
    ${card('o-item', 'Item', `
      ${ui.item('Item', () => o.itemTpl, v => { o.itemTpl = v; }, 'all')}
      ${isWeapon || o.useDefaultPreset === false ? ui.toggle('Sell the assembled gun (not a bare receiver)', () => o.useDefaultPreset, v => { o.useDefaultPreset = v; }, { label: 'Weapon preset', refresh: 'light' }) : ''}
      ${ui.num('Loyalty level', () => o.loyaltyLevel, v => { o.loyaltyLevel = v; }, { min: 1, max: 4 })}
      ${ui.toggle('Unlimited stock', () => o.unlimited, v => { o.unlimited = v; }, { label: 'Stock' })}
      ${o.unlimited ? '' : ui.num('Stock per restock', () => o.stock, v => { o.stock = v; }, { min: 1, max: 100000 })}
      ${ui.num('Buy limit per player', () => o.buyLimit, v => { o.buyLimit = v; }, { min: 0, max: 100000 })}
      ${ui.hint('<b>Stock per restock</b> = how many the trader has after each restock (shared by everyone). <b>Buy limit</b> = how many one player may buy per restock (0 = no limit).')}`)}
    ${card('o-price', `Price / barter ${price ? ui.badge(price[0], price[1]) : ''}`, `
      ${ui.hint('Everything listed is needed to buy it. Only money = <b>BUY</b>; any item in the list = <b>BARTER</b>.')}
      <div class="mini">${costs || '<div class="empty" style="padding:14px">No price yet</div>'}</div>
      <div class="toolbar" style="margin-top:8px">
        <button class="chip" data-act="addCost" data-arg="${CUR.RUB}">+ ₽ Roubles</button>
        <button class="chip" data-act="addCost" data-arg="${CUR.USD}">+ $ Dollars</button>
        <button class="chip" data-act="addCost" data-arg="${CUR.EUR}">+ € Euros</button>
        <button class="outline" data-act="addCost" data-arg="">+ Barter item…</button>
      </div>`)}`];
}

function setOfferUnlock(o, kind) {
  const t = S.t;
  if (kind === 'start' || kind === 'game') {
    for (const q of t.file.quests) q.rewards = q.rewards.filter(r => !(r.type === 'UnlockOffer' && r.offerId === o.id));
    if (kind === 'start') delete o.unlockedByQuestId;
    else setTimeout(() => ACT.pickOfferGameQuest(), 0);
  }
  if (kind === 'mine') {
    delete o.unlockedByQuestId;
    if (!questsUnlocking(t, o).length && t.file.quests[0]) toggleQuestUnlocksOffer(t.file.quests[0], o, true);
  }
}

function toggleQuestUnlocksOffer(q, o, on) {
  q.rewards = q.rewards.filter(r => !(r.type === 'UnlockOffer' && r.offerId === o.id));
  if (on) q.rewards.push(fillReward({ type: 'UnlockOffer', offerId: o.id }));
}

function detailsQuest() {
  const t = S.t, q = S.quest;
  if (!q) return ['Quests', '<div class="empty">Pick a quest, or click + Add quest.</div>'];
  const req = requirements(q);
  const reqLines = [req.effective > req.own ? `Player level ${req.effective}  (set to ${req.own}, but an earlier quest needs ${req.effective})` : `Player level ${req.own}`];
  if (!req.chain.length) reqLines.push('No quests needed before it.');
  else {
    reqLines.push(`Finish these ${req.chain.length} quest(s) first:`);
    for (const c of [...req.chain].sort((a, b) => b.depth - a.depth)) {
      const n = c.game ? 1 : ways(c.q).length;
      reqLines.push(`${'  '.repeat(c.depth)}• ${chainLabel(c)}${c.game ? '' : `  (lvl ${c.q.minLevel})`}${n > 1 ? `  — any of its ${n} ways (${ways(c.q).map(wayLetter).join('/')})` : ''}${chainOn(c) ? '' : `  ✖ ${c.t.file.name} is switched OFF`}`);
    }
  }
  for (const id of req.missing) reqLines.push(`✖ Needs a quest that doesn't exist (${id}) — remove it below.`);
  if (req.cycle) reqLines.push('✖ The required quests go in a circle — this quest can never unlock.');
  const reqBad = req.missing.length || req.cycle || req.chain.some(c => !chainOn(c));

  const img = q.image && t.images[q.image];
  const mineSwitches = allQuests().filter(x => x.q !== q).map(({ t: ot, q: oq }) => {
    const n = ways(oq).length;
    const text = `${oq.name} · ${ot.file.name} · lvl ${oq.minLevel}${n > 1 ? ` · any of ${n} ways` : ''}${ot.file.enabled ? '' : ' · trader OFF'}`;
    return ui.toggle(text, () => q.prerequisiteQuestIds.includes(oq.id), v => {
      q.prerequisiteQuestIds = q.prerequisiteQuestIds.filter(x => x !== oq.id);
      if (v) q.prerequisiteQuestIds.push(oq.id);
    });
  }).join('');
  const others = q.prerequisiteQuestIds.filter(id => !allQuests().some(x => x.q.id === id)).map(id => {
    const g = S.gameQuests.get(id);
    return `<div class="itemline"><span class="name ${g ? '' : 'missing'}">${g ? '🎮 ' + esc(questLabel(id)) : `✖ Missing quest ${esc(id)}`}</span><button class="icon-btn" data-act="dropPrereq" data-arg="${esc(id)}" title="Remove">✕</button></div>`;
  }).join('');

  // ---- ways as tabs
  const w = ways(q);
  if (!w.includes(S.way)) S.way = w[0];
  const tabs = w.map(o => `<button class="waytab ${o === S.way ? 'on' : ''}" style="--c:${WAY_COLOR[o]}" data-act="selWay" data-arg="${o}">Way ${wayLetter(o)}<span>${q.conditions.filter(c => (c.option || 1) === o).length}</span></button>`).join('') +
    (w.length < 4 ? '<button class="waytab add" data-act="addWay" title="Give the player another way to finish this quest">＋ Way</button>' : '');
  const waysHint = w.length <= 1
    ? 'Every objective below must be done. Want the player to choose (e.g. hand in <i>or</i> kill <i>or</i> pay)? Click <b>＋ Way</b>.'
    : `<b>${w.length} ways</b> — the player finishes ANY ONE way (all objectives inside it). In game each way is its own quest; when one is turned in, the others are closed and disappear.`;
  const objectives = q.conditions.map((c, i) => (c.option || 1) !== S.way ? '' : `<div class="row ${c === S.cond ? 'sel' : ''}" data-act="selCond" data-arg="${i}"><div class="cell">
      ${thumb({ text: TYPE_SHORT[c.type]?.[0] || '?', color: TYPE_COLOR[c.type] || '#999', dark: true })}
      <div class="text"><div class="line1"><span class="title">${esc(conditionTitle(c))}</span></div></div>
      <span class="side">${esc(conditionDetail(c))}</span></div></div>`).join('');
  const rewards = q.rewards.map((r, i) => {
    const d = rewardRow(t, r);
    return `<div class="row ${r === S.reward ? 'sel' : ''}" data-act="selReward" data-arg="${i}"><div class="cell">${thumb(d.thumb)}
      <div class="text"><div class="title">${esc(d.title)}</div></div><span class="side">${esc(d.side || '')}</span></div></div>`;
  }).join('');
  const knownTags = [...new Set(allQuests().flatMap(x => x.q.tags))].filter(tag => !q.tags.includes(tag));

  return [q.name, `
    ${card('q-req', `<span style="color:${reqBad ? 'var(--red)' : 'var(--violet)'}">Unlock requirements</span>`, `<div class="req">${esc(reqLines.join('\n'))}</div>`)}
    ${card('q-info', 'Quest', `
      ${ui.text('Name', () => q.name, v => { q.name = v; $('#detailsTitle').textContent = v; })}
      ${ui.num('Unlocks at level', () => q.minLevel, v => { q.minLevel = v; }, { min: 1, max: 79 })}
      ${ui.area('Description', () => q.description, v => { q.description = v; })}
      ${ui.area('When completed', () => q.successMessage, v => { q.successMessage = v; }, { rows: 2 })}
      ${ui.toggle('Fails if the player dies, goes missing or leaves a raid (can be restarted)', () => q.failOnDeath, v => { q.failOnDeath = v; }, { label: 'Hardcore', refresh: 'light' })}
      <div class="field top"><label>Tags</label><div>
        <div class="chips">${q.tags.map(tag => `<span class="tag" style="--c:${tagColor(tag)}">${esc(tag)}<button data-act="removeTag" data-arg="${esc(tag)}" title="Remove">✕</button></span>`).join('')}
          <input type="text" id="tagInput" placeholder="+ add tag (Enter)" style="width:150px"></div>
        ${knownTags.length ? `<div class="chips" style="margin-top:6px">${knownTags.map(tag => `<button class="chip" data-act="addTag" data-arg="${esc(tag)}">+ ${esc(tag)}</button>`).join('')}</div>` : ''}
        ${ui.hint('Your own labels (e.g. "Kappa path", "Main 1") — shown on the quest list and usable as a filter. The game never sees them.')}</div></div>
      <div class="field"><label>Image</label><div class="toolbar" style="padding:0">
        <button class="outline" data-act="chooseQuestImage">Choose quest image…</button>
        ${img ? '<button class="danger" data-act="removeQuestImage">Remove image</button>' : ''}</div></div>
      ${img ? `<img class="preview" src="${esc(img)}" alt="">` : ''}`)}
    ${card('q-prereq', 'Required quests', `
      ${ui.hint('Switch on every quest that must be finished first. For a quest with several ways, <b>any one finished way counts</b>. Game quests (Prapor, Therapist…) can be required too.')}
      <div class="switches">${mineSwitches || '<div class="hint">No other quests of yours yet.</div>'}</div>
      ${others ? `<div class="switches" style="margin-top:8px">${others}</div>` : ''}
      <div class="toolbar" style="margin-top:8px"><button class="outline" data-act="addGamePrereq">+ Game quest…</button></div>`)}
    ${card('q-obj', 'Objectives', `
      <div class="waytabs">${tabs}</div>
      ${ui.hint(waysHint)}
      <div class="mini">${objectives || '<div class="empty" style="padding:14px">No objectives in this way yet</div>'}</div>
      <div class="toolbar" style="margin-top:8px">
        <button class="primary" data-act="addCond">+ Objective</button>
        <button class="outline" data-act="dupCond" ${S.cond ? '' : 'disabled'}>Duplicate</button>
        <button class="danger" data-act="removeCond" ${S.cond ? '' : 'disabled'}>Remove</button>
        <button class="icon-btn" data-act="moveCond" data-arg="-1">▲</button><button class="icon-btn" data-act="moveCond" data-arg="1">▼</button>
        ${w.length > 1 ? `<button class="danger" data-act="removeWay" style="margin-left:auto">Delete way ${wayLetter(S.way)}</button>` : ''}
      </div>`)}
    ${S.cond && (S.cond.option || 1) === S.way ? objectiveEditor(S.cond) : ''}
    ${card('q-rew', 'Rewards', `
      ${ui.hint('Every way gives the same rewards.')}
      <div class="mini">${rewards || '<div class="empty" style="padding:14px">No rewards yet</div>'}</div>
      <div class="toolbar" style="margin-top:8px">
        <button class="primary" data-act="addReward">+ Reward</button>
        <button class="outline" data-act="dupReward" ${S.reward ? '' : 'disabled'}>Duplicate</button>
        <button class="danger" data-act="removeReward" ${S.reward ? '' : 'disabled'}>Remove</button>
        <button class="icon-btn" data-act="moveReward" data-arg="-1">▲</button><button class="icon-btn" data-act="moveReward" data-arg="1">▼</button>
      </div>
      ${S.reward ? rewardEditor(t, S.reward) : ''}`)}`];
}

function objectiveEditor(c) {
  const type = c.type;
  const color = TYPE_COLOR[type] || '#999';
  const kill = type === 'Kill', items = ['HandoverItem', 'FindItem', 'UseItem'].includes(type);
  const money = isMoneyList(c.itemTpls);
  const open = S.open.get(c) || new Set();
  const amountLabel = { Kill: 'How many kills', Extract: 'How many extracts', UseItem: 'How many uses', FindItem: 'How many to find', Skill: 'Level to reach' }[type] || (money ? 'How much to pay' : 'How many');

  const needs = (key, text, list, content) => {
    const on = list.length > 0 || open.has(key);
    return `${ui.toggle(text, () => on, v => {
      const set = S.open.get(c) || new Set();
      if (v) set.add(key); else { set.delete(key); list.length = 0; }
      S.open.set(c, set);
    })}${on ? `<div style="margin:6px 0 12px 54px">${content()}</div>` : ''}`;
  };

  return card('q-objedit', `<span style="color:${color}">Way ${wayLetter(c.option || 1)} · ${esc((TYPE_LONG[type] || type).toUpperCase())}</span>`, `
    ${ui.chips('Type', TYPES.map(x => [x, TYPE_SHORT[x], TYPE_COLOR[x]]), () => type, v => { c.type = v; })}
    ${kill ? ui.select('Kill who', KILL_TARGETS, () => c.killTarget, v => { c.killTarget = v; }) : ''}
    ${kill && c.killTarget === 'Boss' ? ui.multichips('Which bosses count (none picked = any boss)', BOSSES, c.bossRoles, { color: 'var(--red)' }) : ''}
    ${type === 'Skill' ? ui.select('Skill', SKILLS, () => c.skill, v => { c.skill = v; }) : ''}
    ${ui.num(amountLabel, () => c.count, v => { c.count = v; }, { min: 1, max: type === 'Skill' ? 51 : 1e9 })}
    ${['HandoverItem', 'FindItem'].includes(type) && !money ? ui.toggle('Items must be found in raid', () => c.foundInRaid, v => { c.foundInRaid = v; }, { label: 'Found in raid', refresh: 'light' }) : ''}
    ${items ? itemList(
      { HandoverItem: 'What to hand over — any one of these counts (items or money)', FindItem: 'What to find — any one of these counts', UseItem: 'What to use — any one of these counts (food, drinks, meds)' }[type],
      c.itemTpls, type === 'UseItem' ? 'Meds' : 'all', type === 'HandoverItem') : ''}
    ${kill ? needs('weapon', 'Must kill with a specific weapon or grenade', c.weaponTpls, () => itemList('Any one of these counts', c.weaponTpls, 'weapons+')) : ''}
    ${kill ? needs('caliber', 'Must use specific ammo (caliber)', c.calibers, () => caliberList(c.calibers)) : ''}
    ${kill || type === 'Extract' ? needs('wearing', 'Must be wearing something', c.wearingTpls, () => itemList('Wearing any one of these', c.wearingTpls, 'Gear')) : ''}
    ${kill || type === 'Extract' || type === 'UseItem' ? needs('maps', 'Only on specific maps', c.locations, () => ui.multichips('', MAPS, c.locations, { color: 'var(--green)' })) : ''}
    ${kill ? needs('body', 'Only hits to certain body parts (e.g. headshots)', c.bodyParts, () => ui.multichips('', BODY_PARTS, c.bodyParts, { color: 'var(--red)' })) : ''}
    ${kill ? distanceField(c, open) : ''}
    ${kill ? timeField(c, open) : ''}
    ${type === 'Extract' ? ui.multichips('Which exits count (none picked = survived or run-through)', EXIT_STATUSES, c.exitStatuses, { color: 'var(--orange)' }) : ''}
    ${['Kill', 'Extract', 'UseItem'].includes(type) ? ui.toggle(`All of it in a single raid (the count restarts every raid)`, () => c.oneRaid, v => { c.oneRaid = v; }, { refresh: 'light' }) : ''}
    ${objectiveText(c)}
    ${ui.select('Belongs to way', ways(S.quest).concat(ways(S.quest).length < 4 ? [[1, 2, 3, 4].find(n => !ways(S.quest).includes(n))] : []).map(n => [n, `Way ${wayLetter(n)}`]),
      () => c.option || 1, v => { c.option = Number(v); S.way = c.option; })}
    ${kill ? ui.hint('A kill can require a weapon/grenade AND worn gear at the same time. Weapons listed together are "any one of".') : ''}
  `, { cls: 'objective-card', style: `--c:${color}` });
}

/** The text the game shows for an objective, Tarkov style (same as the server writes when "Text" is empty). */
function tarkovText(c) {
  const items = c.itemTpls.map(itemName).join(' or ') || '…';
  const where = c.locations.length ? ' on ' + c.locations.map(mapName).join(' or ') : '';
  const wearing = c.wearingTpls.length ? ' while wearing ' + c.wearingTpls.map(itemName).join(' or ') : '';
  const raid = c.oneRaid ? ' in one raid' : '';
  switch (c.type) {
    case 'HandoverItem':
      return isMoneyList(c.itemTpls) ? `Hand over ${c.itemTpls.map(id => MONEY_NAME[id].replace(/^\S+ /, '')).join(' or ')}` :
        `Hand over the ${c.foundInRaid ? 'found in raid ' : ''}item: ${items}`;
    case 'FindItem': return `Find in raid: ${items}`;
    case 'UseItem': return `Use ${items} during a raid${where}${raid}`;
    case 'Skill': return `Reach the required ${skillName(c.skill)} skill level`;
    case 'Extract': return `Survive and extract${where}${wearing}${raid}`;
    case 'Kill': {
      const who = { Savage: 'Scavs', AnyPmc: 'PMC operatives', Usec: 'USEC PMC operatives', Bear: 'BEAR PMC operatives',
        Boss: c.bossRoles.length ? c.bossRoles.map(bossName).join(' or ') : 'bosses' }[c.killTarget] || 'any target';
      const weapon = c.weaponTpls.length ? ' while using ' + c.weaponTpls.map(itemName).join(' or ') : '';
      const ammo = c.calibers.length ? ` with ${c.calibers.map(caliberName).join(' or ')} ammo` : '';
      const parts = c.bodyParts.length ? (c.bodyParts.length === 1 && c.bodyParts[0] === 'Head' ? ' with headshots' :
        ' with shots to the ' + c.bodyParts.map(p => (BODY_PARTS.find(b => b[0] === p) || [p, p])[1].toLowerCase()).join(' or ')) : '';
      const dist = c.distance > 0 ? (c.distanceCompare === '<=' ? ` from less than ${c.distance} meters away` : ` from over ${c.distance} meters away`) : '';
      const time = c.daytimeFrom !== c.daytimeTo ? ` between ${String(c.daytimeFrom).padStart(2, '0')}:00 and ${String(c.daytimeTo).padStart(2, '0')}:00` : '';
      return `Eliminate ${who}${weapon}${ammo}${parts}${dist}${wearing}${where}${time}${raid}`;
    }
    default: return c.type;
  }
}

function objectiveText(c) {
  const k = bind(() => c.text, v => { c.text = v; }, 'light');
  return `<div class="field top"><label>Objective text</label><div>
    <input type="text" data-b="${k}" value="${esc(c.text)}" placeholder="${esc(tarkovText(c))}" spellcheck="false">
    ${ui.hint(c.text ? 'Your own text is used in game.' : 'Empty = the game shows the grey text above (Tarkov style), updated automatically.')}
    <div class="toolbar" style="padding:0"><button class="outline" data-act="useTarkovText">${c.text ? 'Reset to Tarkov text' : 'Edit the Tarkov text'}</button></div>
  </div></div>`;
}

function distanceField(c, open) {
  const on = c.distance > 0 || open.has('distance');
  return `${ui.toggle('Only kills from a certain distance', () => on, v => {
    const set = S.open.get(c) || new Set();
    if (v) { set.add('distance'); if (!c.distance) c.distance = 50; } else { set.delete('distance'); c.distance = 0; }
    S.open.set(c, set);
  })}${on ? `<div style="margin:6px 0 12px 54px">
    ${ui.chips('', [['>=', 'At least'], ['<=', 'Within']], () => c.distanceCompare, v => { c.distanceCompare = v; })}
    ${ui.num('Meters', () => c.distance, v => { c.distance = v; }, { min: 1, max: 2000 })}</div>` : ''}`;
}

function timeField(c, open) {
  const on = c.daytimeFrom !== c.daytimeTo || open.has('time');
  return `${ui.toggle('Only at certain in-raid hours (e.g. night)', () => on, v => {
    const set = S.open.get(c) || new Set();
    if (v) { set.add('time'); if (c.daytimeFrom === c.daytimeTo) { c.daytimeFrom = 21; c.daytimeTo = 7; } } else { set.delete('time'); c.daytimeFrom = 0; c.daytimeTo = 0; }
    S.open.set(c, set);
  })}${on ? `<div style="margin:6px 0 12px 54px">
    ${ui.num('From hour (0-23)', () => c.daytimeFrom, v => { c.daytimeFrom = v; }, { min: 0, max: 23 })}
    ${ui.num('To hour (0-23)', () => c.daytimeTo, v => { c.daytimeTo = v; }, { min: 0, max: 23 })}
    ${ui.hint('21 → 7 = at night. Uses the in-raid clock.')}</div>` : ''}`;
}

function itemList(label, list, filter, moneyButtons) {
  const lk = listRef(list);
  const rows = list.map((id, i) => {
    const it = item(id);
    return `<div class="row"><div class="cell">${thumb({ text: isMoney(id) ? MONEY_SYMBOL[id] : initials(it?.s), color: isMoney(id) ? 'var(--yellow)' : !it && S.items.size ? 'var(--red)' : '#3a3a3a', dark: isMoney(id) })}
      <div class="text"><div class="title">${esc(isMoney(id) && !it ? MONEY_NAME[id] : itemName(id))}</div></div>
      <span class="side">${esc(it ? (it.c === 'Other' ? it.s : `${it.c} · ${it.s}`) : '')}</span>
      <button class="icon-btn" data-act="removeAt" data-arg="${lk}|${i}" title="Remove">✕</button></div></div>`;
  }).join('');
  const money = moneyButtons ? [[CUR.RUB, '+ ₽'], [CUR.USD, '+ $'], [CUR.EUR, '+ €'], [CUR.GP, '+ GP coin'], [CUR.LEGA, '+ Lega medal']]
    .map(([id, t]) => `<button class="chip" data-act="addTo" data-arg="${lk}|${id}">${t}</button>`).join('') : '';
  return `<div class="stack"><label>${esc(label)}</label><div class="mini">${rows || '<div class="empty" style="padding:12px">Nothing added yet</div>'}</div>
    <div class="toolbar" style="margin-top:6px"><button class="outline" data-act="pickTo" data-arg="${lk}|${filter}">+ Add…</button>${money}</div></div>`;
}

function caliberList(list) {
  const lk = listRef(list);
  const rows = list.map((cal, i) => `<div class="row"><div class="cell">${thumb({ text: '•', color: 'var(--yellow)', dark: true })}
    <div class="text"><div class="title">${esc(caliberName(cal))}</div></div><span class="side">${esc(cal)}</span>
    <button class="icon-btn" data-act="removeAt" data-arg="${lk}|${i}">✕</button></div></div>`).join('');
  return `<div class="stack"><label>Any of these calibers (pick any bullet of the caliber)</label><div class="mini">${rows || '<div class="empty" style="padding:12px">Nothing added yet</div>'}</div>
    <div class="toolbar" style="margin-top:6px"><button class="outline" data-act="pickCaliber" data-arg="${lk}">+ Add ammo…</button></div></div>`;
}

function rewardEditor(t, r) {
  const offers = t.file.offers.map(o => [o.id, `${itemName(o.itemTpl)} — ${costText(o)}`]);
  const offer = t.file.offers.find(o => o.id === r.offerId);
  return `<div style="margin-top:14px">
    ${ui.chips('Type', REWARD_TYPES, () => r.type, v => {
      if (v === r.type) return;
      r.type = v;
      r.value = { Experience: 1000, TraderStanding: 0.02, Skill: 100, StashRows: 1 }[v] ?? r.value; // sensible starting value
    })}
    ${r.type === 'Experience' ? ui.num('XP', () => r.value, v => { r.value = v; }, { min: 0, max: 1e9, step: 100 }) : ''}
    ${r.type === 'TraderStanding' ? ui.num('Standing', () => r.value, v => { r.value = v; }, { min: -1, max: 1, step: .01 }) : ''}
    ${r.type === 'Item' ? ui.item('Item', () => r.itemTpl, v => { r.itemTpl = v; }) + ui.num('How many', () => r.count, v => { r.count = v; }, { min: 1, max: 100000 }) +
      ui.toggle('Found in raid', () => r.foundInRaid, v => { r.foundInRaid = v; }, { label: 'Found in raid', refresh: 'light' }) +
      ui.toggle('Give it when the quest is accepted (not when completed)', () => r.onStart, v => { r.onStart = v; }, { label: 'When', refresh: 'light' }) : ''}
    ${r.type === 'Skill' ? ui.select('Skill', SKILLS, () => r.skill, v => { r.skill = v; }) + ui.num('Skill points (100 = 1 level)', () => r.value, v => { r.value = v; }, { min: 1, max: 5100, step: 10 }) : ''}
    ${r.type === 'StashRows' ? ui.num('Extra stash rows', () => r.value, v => { r.value = v; }, { min: 1, max: 50 }) : ''}
    ${r.type === 'UnlockOffer' ? ui.select('Offer', offers, () => r.offerId, v => { r.offerId = v; }) +
      ui.hint(`The offer appears in ${esc(t.file.name)}'s shop once this quest is completed. How many are for sale is set on the offer itself (Offers page → Stock per restock / Buy limit)${offer ? `: currently <b>${offer.unlimited ? 'unlimited' : offer.stock + ' per restock'}${offer.buyLimit ? `, max ${offer.buyLimit} per player` : ''}</b>` : ''}.`) +
      (offer ? `<button class="outline" data-act="goOffer" data-arg="${esc(offer.id)}">Go to offer</button>` : '') : ''}
  </div>`;
}

// ---------------------------------------------------------------- checks details

function detailsCheck() {
  const e = S.check;
  return ['Details', `
    <div class="card"><h3>Selected</h3>${e ? `<div class="hint">${esc(e.where)}</div><div class="req">${esc(e.message)}</div>
      ${e.trader ? '<div class="toolbar" style="margin-top:10px"><button class="primary" data-act="goCheck">Go to it</button></div>' : ''}` : ui.hint('Select a line to see it in full. Double-click a line to jump there.')}</div>
    <div class="card"><h3>What the colors mean</h3><div class="req">✖ Error — won't work: the server skips it, or the quest can never be done / unlocked.
⚠ Warning — works, but probably not what you meant.
i Info — good to know.
✔ OK — checked and fine.

Checks run when the editor opens, a moment after every change, and before saving.</div></div>`];
}

// ---------------------------------------------------------------- bottom

function renderBottom() {
  const errors = S.checks.filter(c => c.level === 'error').length;
  const warnings = S.checks.filter(c => c.level === 'warning').length;
  const sum = $('#checkSummary');
  sum.textContent = errors + warnings === 0 ? '✔ All checks pass' : `✖ ${errors} error(s)   ⚠ ${warnings} warning(s)`;
  sum.style.color = errors ? 'var(--red)' : warnings ? 'var(--orange)' : 'var(--green)';
  $('#checksButton').textContent = errors ? `✖ ${errors}` : warnings ? `⚠ ${warnings}` : '✔ Checks';
  $('#checksChip').textContent = errors + warnings ? `Checks & log (${errors + warnings})` : 'Checks & log';
  const unsaved = S.traders.filter(t => t.dirty).length;
  $('#unsaved').textContent = unsaved ? `${unsaved} unsaved` : '';
  renderUndo();
  if (unsaved !== renderBottom.last) { renderBottom.last = unsaved; host.call('setUnsaved', { count: unsaved }).catch(() => { }); }
}

// =====================================================================
// Text helpers
// =====================================================================

function costText(o) {
  if (!o.cost.length) return 'no price set!';
  return o.cost.map(c => isMoney(c.itemTpl) ? `${fmt(c.count)} ${MONEY_SYMBOL[c.itemTpl]}` : `${fmt(c.count)} × ${itemName(c.itemTpl)}`).join(' + ');
}
function killWho(c) {
  return { Boss: c.bossRoles.length ? c.bossRoles.map(bossName).join(' / ') : 'Bosses', AnyPmc: 'PMCs', Usec: 'USEC', Bear: 'BEAR', Savage: 'Scavs' }[c.killTarget] || 'Anyone';
}
function itemsText(l) { return l.length ? l.map(shortName).join(' / ') : '(pick items)'; }
function shortCondition(c) {
  switch (c.type) {
    case 'Kill': return `Kill ${c.count} ${killWho(c)}`;
    case 'Extract': return `Extract ${c.count}×`;
    case 'UseItem': return `Use ${c.count}× ${itemsText(c.itemTpls)}`;
    case 'FindItem': return `Find ${c.count}× ${itemsText(c.itemTpls)}`;
    case 'Skill': return `${skillName(c.skill)} level ${c.count}`;
    default: return isMoneyList(c.itemTpls) ? `Pay ${fmt(c.count)} ${moneyShort(c.itemTpls[0])}` : `Hand in ${c.count}× ${itemsText(c.itemTpls)}`;
  }
}
function conditionTitle(c) {
  switch (c.type) {
    case 'Kill': return `Kill ${c.count} × ${killWho(c)}`;
    case 'Extract': return `Extract ${c.count} time(s)`;
    case 'UseItem': return `Use ${c.count} × ${itemsText(c.itemTpls)}`;
    case 'FindItem': return `Find ${c.count} × ${itemsText(c.itemTpls)}`;
    case 'Skill': return `Reach ${skillName(c.skill)} level ${c.count}`;
    default: return isMoneyList(c.itemTpls) ? `Pay ${fmt(c.count)} ${MONEY_NAME[c.itemTpls[0]]}` : `Hand over ${c.count} × ${itemsText(c.itemTpls)}`;
  }
}
function conditionDetail(c) {
  const d = [];
  if (c.type === 'Kill') {
    if (c.weaponTpls.length) d.push('with ' + c.weaponTpls.map(shortName).join(' or '));
    if (c.calibers.length) d.push(c.calibers.map(caliberName).join('/') + ' ammo');
    if (c.bodyParts.length) d.push(c.bodyParts.map(p => (BODY_PARTS.find(b => b[0] === p) || [p, p])[1].toLowerCase()).join('/') + ' hits');
    if (c.distance > 0) d.push((c.distanceCompare === '<=' ? 'within ' : '≥ ') + c.distance + ' m');
    if (c.daytimeFrom !== c.daytimeTo) d.push(`${c.daytimeFrom}:00–${c.daytimeTo}:00`);
  }
  if (c.type === 'Extract' && c.exitStatuses.length) d.push(c.exitStatuses.map(s => (EXIT_STATUSES.find(x => x[0] === s) || [s, s])[1].toLowerCase()).join('/'));
  if (c.oneRaid && ['Kill', 'Extract', 'UseItem'].includes(c.type)) d.push('in one raid');
  if (c.wearingTpls.length && ['Kill', 'Extract'].includes(c.type)) d.push('wearing ' + c.wearingTpls.map(shortName).join(' or '));
  if (c.locations.length) d.push('on ' + c.locations.map(mapName).join(', '));
  if (['HandoverItem', 'FindItem'].includes(c.type) && c.foundInRaid && !isMoneyList(c.itemTpls)) d.push('found in raid');
  return d.join(' · ');
}
function rewardBoxes(t, q) {
  return q.rewards.map(r => {
    switch (r.type) {
      case 'Experience': return { text: `${fmt(r.value)} XP`, color: '#a082ff' };
      case 'TraderStanding': return { text: `${r.value >= 0 ? '+' : ''}${r.value} Standing`, color: '#509bf5' };
      case 'Item': return { text: `${r.count}× ${shortName(r.itemTpl)}${r.onStart ? ' (on accept)' : ''}`, color: '#ffa42b' };
      case 'Skill': return { text: `+${fmt(r.value)} ${skillName(r.skill)}`, color: '#f5cd46' };
      case 'StashRows': return { text: `+${fmt(r.value)} Stash rows`, color: '#ff7ab6' };
      case 'UnlockOffer': {
        const o = t.file.offers.find(x => x.id === r.offerId);
        return o ? { label: isBarter(o) ? 'BARTER' : 'BUY', text: shortName(o.itemTpl), color: isBarter(o) ? 'var(--pink)' : 'var(--blue)' } : { label: 'UNLOCK', text: '?', color: 'var(--red)' };
      }
      default: return { text: r.type };
    }
  });
}

function rewardsText(t, q) {
  if (!q.rewards.length) return '—';
  return q.rewards.map(r => ({
    Experience: `${fmt(r.value)} XP`,
    TraderStanding: `${r.value >= 0 ? '+' : ''}${r.value} standing`,
    Item: `${r.count}× ${shortName(r.itemTpl)}`,
    UnlockOffer: (o => o ? `${isBarter(o) ? 'BARTER' : 'BUY'} ${shortName(o.itemTpl)}` : 'unlock ?')(t.file.offers.find(o => o.id === r.offerId)),
    Skill: `+${fmt(r.value)} ${skillName(r.skill)}`,
    StashRows: `+${fmt(r.value)} stash rows`,
  }[r.type] || r.type)).join('  ·  ');
}
function rewardRow(t, r) {
  switch (r.type) {
    case 'Experience': return { thumb: { text: 'XP', color: '#a082ff' }, title: `${fmt(r.value)} XP` };
    case 'TraderStanding': return { thumb: { text: '+', color: '#509bf5' }, title: `${r.value >= 0 ? '+' : ''}${r.value} standing with ${t.file.name}` };
    case 'Item': return { thumb: { text: initials(item(r.itemTpl)?.s), color: '#ffa42b', dark: true }, title: `${r.count} × ${itemName(r.itemTpl)}`, side: [r.onStart ? 'on accept' : '', r.foundInRaid ? 'found in raid' : ''].filter(Boolean).join(' · ') };
    case 'Skill': return { thumb: { text: 'SK', color: '#f5cd46', dark: true }, title: `+${fmt(r.value)} ${skillName(r.skill)} skill points` };
    case 'StashRows': return { thumb: { text: '▦', color: '#ff7ab6', dark: true }, title: `+${fmt(r.value)} stash rows` };
    case 'UnlockOffer': {
      const o = t.file.offers.find(x => x.id === r.offerId);
      return { thumb: { text: o && isBarter(o) ? 'BAR' : 'BUY', color: '#1ed760', dark: true }, title: o ? `${isBarter(o) ? 'BARTER' : 'BUY'}: ${itemName(o.itemTpl)}` : 'Unlock: (pick an offer)', side: o ? (o.unlimited ? 'unlimited' : `${o.stock} per restock`) : '' };
    }
    default: return { thumb: { text: '?' }, title: r.type };
  }
}

// =====================================================================
// Checks (pre-flight: what would break before you restart SPT)
// =====================================================================

const LEVELS = ['error', 'warning', 'info', 'ok'];
const LEVEL_ICON = { error: '✖', warning: '!', info: 'i', ok: '✔' };
const LEVEL_COLOR = { error: 'red', warning: 'orange', info: 'blue', ok: 'green' };

function nowText() { return new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }); }
function entry(level, where, message, trader = null, target = null) { return { level, where, message, trader, target, time: nowText() }; }
function log(level, where, message, trader, target) {
  S.history.unshift(entry(level, where, message, trader, target));
  S.history.length = Math.min(S.history.length, 200);
}

function requirements(quest) {
  const byId = new Map();
  for (const { t, q } of allQuests()) if (!byId.has(q.id)) byId.set(q.id, { t, q });
  const chain = [], missing = [], seen = new Set();
  let cycle = false, level = Math.max(1, quest.minLevel);
  const walk = (q, depth, path) => {
    for (const id of q.prerequisiteQuestIds) {
      if (id === quest.id || path.has(id)) { cycle = true; continue; }
      if (seen.has(id)) continue;
      const found = byId.get(id);
      if (!found) {
        const g = S.gameQuests.get(id);
        if (g) { seen.add(id); chain.push({ game: g, depth }); }
        else if (!missing.includes(id)) missing.push(id);
        continue;
      }
      seen.add(id);
      chain.push({ ...found, depth });
      level = Math.max(level, found.q.minLevel);
      walk(found.q, depth + 1, new Set([...path, id]));
    }
  };
  walk(quest, 1, new Set([quest.id]));
  // Without the game's database, game quest ids can't be told apart from deleted quests.
  const unknownOk = S.gameQuests.size === 0;
  return { own: Math.max(1, quest.minLevel), effective: level, chain, missing: unknownOk ? [] : missing, unknown: unknownOk ? missing : [], cycle };
}

function runChecks() {
  const out = [];
  const add = (...a) => out.push(entry(...a));
  const hasItems = S.items.size > 0;
  const known = id => validId(id) && (!hasItems || S.items.has(id));

  const byTraderId = new Map();
  for (const t of S.traders) byTraderId.set(t.file.id, [...(byTraderId.get(t.file.id) || []), t]);
  for (const [id, list] of byTraderId) if (list.length > 1)
    for (const t of list) add('error', t.file.name, `Trader id ${id} is used by ${list.length} traders (${list.map(x => x.file.name).join(', ')}). Only the first one loads.`, t);
  const byQuestId = new Map();
  for (const x of allQuests()) byQuestId.set(x.q.id, [...(byQuestId.get(x.q.id) || []), x]);
  for (const [id, list] of byQuestId) if (list.length > 1)
    for (const { t, q } of list) add('error', `${t.file.name} › ${q.name}`, `Quest id ${id} is used by ${list.length} quests. Only one loads — use Duplicate (it makes new ids) instead of copying files.`, t, q);

  for (const t of S.traders) {
    const f = t.file;
    const before = out.length;
    if (!validId(f.id)) add('warning', f.name, `Trader id '${f.id}' is not a valid 24-character id. The server gives it a new one on start.`, t);
    if (!f.name.trim()) add('error', f.name || t.folder, 'Trader has no name.', t);
    if (!t.images[f.avatar]) add('warning', f.name, `Icon '${f.avatar}' not found in the trader folder — use "Choose icon from PC".`, t);
    if (f.refreshMinutesMin > f.refreshMinutesMax) add('warning', f.name, `Restock min (${f.refreshMinutesMin} min) is bigger than max (${f.refreshMinutesMax} min).`, t);
    if (!f.loyaltyLevels.length) add('error', f.name, 'No loyalty levels — the trader needs at least LL1.', t);
    for (let i = 1; i < f.loyaltyLevels.length; i++) {
      const a = f.loyaltyLevels[i - 1], b = f.loyaltyLevels[i];
      if (b.minLevel < a.minLevel || b.minStanding < a.minStanding || b.minSalesSum < a.minSalesSum)
        add('warning', f.name, `LL${i + 1} needs less than LL${i} — loyalty levels should only go up.`, t);
    }
    if (!f.offers.length) add('info', f.name, 'Trader sells nothing yet (Offers page → + Add offer).', t);
    if (!f.enabled) add('info', f.name, "Switched OFF — the server won't load this trader, its offers or its quests.", t);
    if (!f.unlockedByDefault && !f.unlockQuestId) add('warning', f.name, 'Locked at start and no quest unlocks it — players can never use this trader. Pick a quest under "Unlocked by".', t);
    if (!f.unlockedByDefault && f.unlockQuestId && !allQuests().some(x => x.q.id === f.unlockQuestId) && S.gameQuests.size && !S.gameQuests.has(f.unlockQuestId))
      add('error', f.name, `Unlocked by quest ${f.unlockQuestId}, which doesn't exist — the trader stays locked.`, t);
    if (out.length === before) add('ok', f.name, `Trader OK — ${f.offers.length} offer(s), ${f.quests.length} quest(s).`, t);

    for (const o of f.offers) {
      const where = `${f.name} › offer ${itemName(o.itemTpl)}`;
      if (!validId(o.itemTpl)) add('error', where, 'Offer has no item (or an invalid item id) — it will be skipped.', t, o);
      else if (!known(o.itemTpl)) add('error', where, `Item id ${o.itemTpl} doesn't exist in the game — the offer will be skipped.`, t, o);
      if (!o.cost.length) add('error', where, 'Offer has no price — it will be skipped. Add roubles or barter items.', t, o);
      for (const c of o.cost) {
        if (!known(c.itemTpl)) add('error', where, `Price item '${c.itemTpl}' is not a valid item — that part of the price is dropped.`, t, o);
        if (!(c.count >= 1)) add('error', where, `Price amount of ${itemName(c.itemTpl)} is ${c.count} — must be at least 1.`, t, o);
      }
      if (o.loyaltyLevel < 1 || o.loyaltyLevel > f.loyaltyLevels.length) add('warning', where, `Needs LL${o.loyaltyLevel} but the trader has ${f.loyaltyLevels.length} loyalty level(s).`, t, o);
      if (!o.unlimited && o.stock < 1) add('error', where, 'Limited stock of 0 — nobody can buy it.', t, o);
      if (!o.unlimited && o.buyLimit > o.stock) add('info', where, `Buy limit (${o.buyLimit}) is higher than the stock (${o.stock}); the stock is the real limit.`, t, o);
      if (o.unlockedByQuestId && !questsUnlocking(t, o).length && S.gameQuests.size && !S.gameQuests.has(o.unlockedByQuestId) && !allQuests().some(x => x.q.id === o.unlockedByQuestId))
        add('error', where, `Unlocked by quest ${o.unlockedByQuestId}, which doesn't exist — it will never be for sale.`, t, o);
      const unl = questsUnlocking(t, o);
      if (unl.length > 1) add('info', where, `Unlocked by ${unl.length} quests (${unl.map(u => u.q.name).join(', ')}) — each quest unlocks its own copy.`, t, o);
    }

    for (const q of f.quests) checkQuest(t, q, add, known);
  }
  S.checks = out;
}

function checkQuest(t, q, add, known) {
  const where = `${t.file.name} › ${q.name}`;
  let errors = 0;
  const A = (level, msg) => { if (level === 'error') errors++; add(level, where, msg, t, q); };
  if (!validId(q.id)) A('error', `Quest id '${q.id}' is not valid — the quest is skipped.`);
  if (!q.name.trim()) A('warning', 'Quest has no name.');
  if (q.minLevel < 1 || q.minLevel > 79) A('warning', `Unlock level ${q.minLevel} — player levels go from 1 to 79.`);
  if (!q.conditions.length) A('error', 'No objectives — the quest would complete the moment it\'s accepted.');

  for (const c of q.conditions) {
    const what = `Way ${wayLetter(c.option || 1)} · ${TYPE_LONG[c.type] || c.type}`;
    if (!TYPES.includes(c.type)) { A('error', `${what}: unknown objective type — skipped by the server.`); continue; }
    if (!(c.count >= 1)) A('error', `${what}: amount is ${c.count} — must be at least 1.`);
    const needsItems = ['HandoverItem', 'FindItem', 'UseItem'].includes(c.type);
    if (needsItems && !c.itemTpls.length) A('error', `${what}: no items picked — the objective is skipped, so this way can't be done.`);
    if (needsItems) for (const id of c.itemTpls) if (!known(id)) A('error', `${what}: item '${id}' doesn't exist.`);
    if (c.type === 'FindItem' && isMoneyList(c.itemTpls)) A('warning', `${what}: money can't really be "found in raid" — use Hand over for payments.`);
    if (c.type === 'Skill' && !SKILLS.some(s => s[0] === c.skill)) A('error', `${what}: no skill picked — the objective is skipped.`);
    if (c.type === 'Skill' && c.count > 51) A('warning', `${what}: skills only go up to level 51.`);
    if (c.type === 'UseItem') {
      for (const id of c.itemTpls) { const it = item(id); if (it && !['Food', 'Meds'].includes(it.c)) A('warning', `${what}: ${it.n} is not food, drink or medicine — it can't be "used" in raid.`); }
      A('info', `${what}: uses the game's UseItem counter — test it once in raid.`);
    }
    if (c.type === 'Kill') {
      if (!KILL_TARGETS.some(k => k[0] === c.killTarget)) A('error', `${what}: unknown target '${c.killTarget}'.`);
      for (const id of c.weaponTpls) {
        const it = item(id);
        if (!known(id)) A('error', `${what}: weapon '${id}' doesn't exist.`);
        else if (it && !['Weapon', 'Grenade'].includes(it.c)) A('warning', `${what}: ${it.n} is not a weapon or grenade — kills can never count with it.`);
      }
      if (c.weaponTpls.length && c.calibers.length) {
        const guns = c.weaponTpls.map(item).filter(i => i?.c === 'Weapon');
        if (guns.length && guns.every(g => g.k && !c.calibers.includes(g.k)))
          A('error', `${what}: none of the weapons shoot ${c.calibers.map(caliberName).join('/')} — impossible.`);
      }
    }
    if (['Kill', 'Extract'].includes(c.type)) for (const id of c.wearingTpls) {
      const it = item(id);
      if (!known(id)) A('error', `${what}: worn item '${id}' doesn't exist.`);
      else if (it && !['Gear', 'Weapon'].includes(it.c)) A('warning', `${what}: ${it.n} can't be worn (not gear) — this can never be met.`);
    }
    for (const m of c.locations) if (!MAPS.some(x => x[0].toLowerCase() === String(m).toLowerCase())) A('warning', `${what}: unknown map id '${m}'.`);
  }
  const w = ways(q);
  if (w.length > 1) A('info', `${w.length} ways to complete (${w.map(wayLetter).join(', ')}). In game each way is its own quest; when one is turned in, the others are closed and disappear.`);

  if (!q.rewards.length) A('info', 'No rewards.');
  for (const r of q.rewards) {
    if (r.type === 'Experience' && r.value < 0) A('warning', `Negative XP reward (${r.value}).`);
    if (r.type === 'TraderStanding' && Math.abs(r.value) > 1) A('warning', `Standing reward ${r.value} — standing is usually small (0.01 – 0.2). 1.0 = a whole loyalty bar.`);
    if (r.type === 'Item' && !known(r.itemTpl)) A('error', 'Item reward has no valid item — skipped.');
    if (r.type === 'Item' && !(r.count >= 1)) A('error', `Item reward amount is ${r.count}.`);
    if (r.type === 'UnlockOffer' && !t.file.offers.some(o => o.id === r.offerId)) A('error', '"Unlock offer" reward points at an offer that doesn\'t exist — pick one.');
    if (r.type === 'Skill' && !SKILLS.some(s => s[0] === r.skill)) A('error', 'Skill reward has no skill picked — skipped.');
    if (r.type === 'Item' && r.onStart && ways(q).length > 1) A('info', `${itemName(r.itemTpl)} is given on accept — only by way A (otherwise players could collect it once per way).`);
  }

  const req = requirements(q);
  for (const id of req.missing) A('error', `Requires quest ${id}, which no longer exists — this quest can never unlock. Remove it under Required quests.`);
  if (req.cycle) A('error', 'Required quests go in a circle (this quest ends up requiring itself) — it can never unlock.');
  if (req.effective > req.own) A('info', `Set to unlock at level ${req.own}, but its required quests need level ${req.effective} — so really level ${req.effective}.`);
  for (const id of q.prerequisiteQuestIds) {
    const before = req.chain.find(c => !c.game && c.q.id === id);
    if (before && ways(before.q).length > 1)
      A('info', `Requires "${before.q.name}", which has ${ways(before.q).length} ways (${ways(before.q).map(wayLetter).join(', ')}): this quest unlocks after ANY one of them is completed.`);
  }
  for (const c of req.chain) if (!c.game && !c.t.file.enabled && t.file.enabled)
    A('error', `Requires "${c.q.name}" from ${c.t.file.name}, which is switched OFF — this quest can never unlock.`);
  if (errors === 0) {
    const chain = req.chain.length ? `after ${req.chain.length} quest(s): ${[...req.chain].sort((a, b) => b.depth - a.depth).map(c => c.game ? c.game.n : c.q.name).join(' → ')}` : 'no quests before it';
    A('ok', `Quest will work — unlocks at level ${req.effective}, ${chain}.`);
  }
}

let checkTimer = 0;
function checksSoon() {
  clearTimeout(checkTimer);
  checkTimer = setTimeout(() => { runChecks(); renderLight(); }, 350);
}

// =====================================================================
// Selection
// =====================================================================

function selectTrader(t, animate = true) {
  S.t = t;
  S.offer = t?.file.offers[0] || null;
  S.quest = t?.file.quests[0] || null;
  S.way = S.quest ? ways(S.quest)[0] : 1;
  S.cond = S.quest?.conditions.find(c => (c.option || 1) === S.way) || null;
  S.reward = S.quest?.rewards[0] || null;
  if (animate) renderAll(true);
}
function selectQuest(q) {
  S.quest = q;
  S.way = q ? ways(q)[0] : 1;
  S.cond = q?.conditions.find(c => (c.option || 1) === S.way) || null;
  S.reward = q?.rewards[0] || null;
}
function showPage(page) {
  if (S.page === page) return;
  S.page = page;
  S.ui.page = page;
  renderHeader();
  renderPage(true);
  renderDetails(true, true);
}

function markDirty(t = S.t) {
  if (!t) return;
  t.dirty = true;
  checksSoon();
  if (!H.pendingSel) H.pendingSel = snapshotSel(); // where this change happens (restored by undo)
  clearTimeout(H.timer);
  H.timer = setTimeout(commitHistory, 450);
}

// =====================================================================
// Undo / redo (Ctrl+Z, Ctrl+Shift+Z or Ctrl+Y)
// =====================================================================

const H = { undo: [], redo: [], current: null, timer: 0, pendingSel: null };

function snapshotData() { return JSON.stringify(S.traders.map(t => t.file)); }
function snapshotSel() {
  const t = S.t;
  return {
    trader: S.traders.indexOf(t), page: S.page, way: S.way,
    offer: t ? t.file.offers.indexOf(S.offer) : -1, quest: t ? t.file.quests.indexOf(S.quest) : -1,
    cond: S.quest ? S.quest.conditions.indexOf(S.cond) : -1, reward: S.quest ? S.quest.rewards.indexOf(S.reward) : -1,
  };
}
function resetHistory() {
  clearTimeout(H.timer);
  H.undo = []; H.redo = [];
  H.current = { data: snapshotData(), sel: snapshotSel() };
  renderUndo();
}
/** Store the state after the latest change as one undo step. */
function commitHistory() {
  clearTimeout(H.timer);
  if (!H.current) return resetHistory();
  const data = snapshotData();
  const where = H.pendingSel || snapshotSel();
  H.pendingSel = null;
  if (data === H.current.data) return;
  H.undo.push({ data: H.current.data, sel: where });
  if (H.undo.length > 200) H.undo.shift();
  H.redo = [];
  H.current = { data, sel: where };
  renderUndo();
}
function restore(state) {
  const files = JSON.parse(state.data);
  S.traders.forEach((t, i) => {
    t.file = files[i];
    const json = JSON.stringify(t.file);
    t.dirty = t.migrated || json !== t.savedJson;
  });
  const s = state.sel;
  S.t = S.traders[s.trader] || S.traders[0] || null;
  S.page = s.page;
  const f = S.t?.file;
  S.offer = f?.offers[s.offer] || f?.offers[0] || null;
  S.quest = f?.quests[s.quest] || f?.quests[0] || null;
  S.way = s.way || 1;
  S.cond = S.quest?.conditions[s.cond] || null;
  S.reward = S.quest?.rewards[s.reward] || null;
  runChecks();
  renderAll(false);
  renderUndo();
}
function undo() {
  commitHistory();
  if (!H.undo.length) return toast('Nothing to undo');
  H.redo.push({ data: H.current.data, sel: snapshotSel() });
  H.current = H.undo.pop();
  restore(H.current);
  toast('Undone');
}
function redo() {
  commitHistory();
  if (!H.redo.length) return toast('Nothing to redo');
  H.undo.push({ data: H.current.data, sel: snapshotSel() });
  H.current = H.redo.pop();
  restore(H.current);
  toast('Redone');
}
function renderUndo() {
  const u = $('#undoBtn'), r = $('#redoBtn');
  if (u) u.disabled = !H.undo.length && !(H.current && snapshotData() !== H.current.data);
  if (r) r.disabled = !H.redo.length;
}

// =====================================================================
// Events
// =====================================================================

document.addEventListener('input', e => {
  const el = e.target;
  const b = B.get(el.dataset.b);
  if (!b || el.type === 'checkbox' || el.tagName === 'SELECT') return;
  let v = el.value;
  if (el.type === 'number') {
    if (v === '' || isNaN(Number(v))) return; // still typing
    v = Number(v);
  }
  b.set(v);
  markDirty();
  if (b.refresh === 'details') renderDetails(false);
  renderLight();
});

document.addEventListener('change', e => {
  const el = e.target;
  const b = B.get(el.dataset.b);
  if (!b) return;
  if (el.type === 'checkbox') b.set(el.checked);
  else if (el.tagName === 'SELECT') b.set(el.value);
  else if (el.type === 'number') { el.value = b.get(); return; } // snap back to the stored (clamped) value
  else return;
  markDirty();
  if (b.refresh === 'details') renderDetails(false);
  else if (b.refresh === 'page') { renderPage(false); renderDetails(false); }
  renderLight();
});

document.addEventListener('click', e => {
  const chip = e.target.closest('[data-chip]');
  if (chip) {
    const b = B.get(chip.dataset.chip);
    if (b) {
      const cur = b.get();
      b.set(typeof cur === 'number' ? Number(chip.dataset.v) : chip.dataset.v);
      markDirty();
      if (b.refresh === 'page') renderPage(false);
      renderDetails(false);
      renderLight();
    }
    return;
  }
  const el = e.target.closest('[data-act]');
  if (!el) return;
  const fn = ACT[el.dataset.act];
  if (fn) fn(el.dataset.arg ?? '', el, e);
});

document.addEventListener('dblclick', e => {
  const el = e.target.closest('[data-act="selCheck"]');
  if (el) ACT.goCheck();
});

document.addEventListener('keydown', e => {
  if (e.target.id === 'tagInput' && e.key === 'Enter') { ACT.addTag(e.target.value); return; }
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); ACT.save(); return; }
  if ((e.ctrlKey || e.metaKey) && $('#modal').hidden) {
    const k = e.key.toLowerCase();
    if (k === 'z' || k === 'y') {
      e.preventDefault();
      document.activeElement?.blur?.();
      if (k === 'y' || e.shiftKey) redo(); else undo();
      return;
    }
  }
  if (e.key === 'Escape' && !$('#modal').hidden) { closeModal(null); return; }
  const typing = /INPUT|TEXTAREA|SELECT/.test(document.activeElement?.tagName || '');
  if (typing || !$('#modal').hidden) return;
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    const d = e.key === 'ArrowDown' ? 1 : -1;
    const lists = { offers: [S.t?.file.offers, 'offer'], quests: [S.t?.file.quests, 'quest'] };
    const [list, key] = lists[S.page] || [];
    if (!list?.length) return;
    e.preventDefault();
    const i = clamp(list.indexOf(S[key]) + d, 0, list.length - 1);
    if (key === 'quest') selectQuest(list[i]); else S.offer = list[i];
    renderPage(false);
    renderDetails(true, true);
    document.querySelector('#page .row.sel')?.scrollIntoView({ block: 'nearest' });
  }
});

// ---- column resizing + right panel width
let drag = null;
document.addEventListener('mousedown', e => {
  const grip = e.target.closest('[data-grip]');
  if (grip) {
    const [list, i] = grip.dataset.grip.split('|');
    drag = { kind: 'col', list, i: Number(i), x: e.clientX, w: colWidths(list)[Number(i)] };
    e.preventDefault();
    return;
  }
  if (e.target.id === 'splitter') {
    drag = { kind: 'split', x: e.clientX, w: $('#right').getBoundingClientRect().width };
    e.target.classList.add('drag');
    e.preventDefault();
  }
});
document.addEventListener('mousemove', e => {
  if (!drag) return;
  if (drag.kind === 'col') {
    const cols = (S.ui.cols ||= {});
    const w = colWidths(drag.list);
    w[drag.i] = clamp(drag.w + (drag.x - e.clientX), 50, 700); // dragging the left edge left = wider
    cols[drag.list] = w;
    applyColumns();
  } else {
    const w = clamp(drag.w + (drag.x - e.clientX), 380, Math.max(400, window.innerWidth - 760));
    S.ui.right = w;
    document.documentElement.style.setProperty('--right', w + 'px');
  }
});
document.addEventListener('mouseup', () => {
  if (!drag) return;
  $('#splitter').classList.remove('drag');
  drag = null;
  saveUi();
});

// =====================================================================
// Actions
// =====================================================================

const move = (list, obj, d) => {
  const i = list.indexOf(obj), j = i + Number(d);
  if (i < 0 || j < 0 || j >= list.length) return false;
  [list[i], list[j]] = [list[j], list[i]];
  return true;
};
const clone = o => JSON.parse(JSON.stringify(o));

const ACT = {
  page: arg => showPage(arg),
  selTrader: arg => { const t = S.traders[Number(arg)]; if (t && t !== S.t) selectTrader(t); },
  selOffer: arg => { S.offer = S.t.file.offers[Number(arg)]; renderPage(false); renderDetails(true, true); },
  selQuest: arg => { const q = S.t.file.quests[Number(arg)]; if (q === S.quest) return; selectQuest(q); renderPage(false); renderDetails(true, true); },
  selCond: arg => { S.cond = S.quest.conditions[Number(arg)]; renderDetails(false); },
  selReward: arg => { S.reward = S.quest.rewards[Number(arg)]; renderDetails(false); },
  selCheck: arg => { S.check = S.shownChecks?.[Number(arg)] || null; renderPage(false); renderDetails(false, true); },
  checkFilter: arg => { S.checkFilter = arg || null; renderPage(false); },
  tagFilter: arg => { S.tagFilter = arg || null; renderPage(false); },
  fold(key) {
    const folded = (S.ui.folded ||= {});
    folded[key] = !folded[key];
    saveUi();
    renderPage(false);
    renderDetails(false);
  },
  selWay: arg => { S.way = Number(arg); S.cond = S.quest.conditions.find(c => (c.option || 1) === S.way) || null; renderDetails(false); },
  addWay() {
    const w = ways(S.quest);
    const next = [1, 2, 3, 4].find(n => !w.includes(n)) || null;
    if (!next || !S.quest.conditions.length) {
      if (!S.quest.conditions.length) toast('Add an objective to way A first');
      return;
    }
    const c = fillCond({ option: next });
    S.quest.conditions.push(c);
    S.way = next; S.cond = c;
    changed(false);
    toast(`Way ${wayLetter(next)} added — set up its objective below`);
  },
  async removeWay() {
    const n = S.quest.conditions.filter(c => (c.option || 1) === S.way).length;
    if (!await confirmBox('Delete way', `Delete way ${wayLetter(S.way)} and its ${n} objective(s)?`, 'Delete')) return;
    S.quest.conditions = S.quest.conditions.filter(c => (c.option || 1) !== S.way);
    S.way = ways(S.quest)[0];
    S.cond = S.quest.conditions.find(c => (c.option || 1) === S.way) || null;
    changed(false);
  },
  useTarkovText() { if (!S.cond) return; S.cond.text = S.cond.text ? '' : tarkovText(S.cond); changed(false); },
  addTag(tag) {
    tag = (tag || '').trim();
    if (!tag || !S.quest || S.quest.tags.includes(tag)) return;
    S.quest.tags.push(tag);
    changed(false);
  },
  removeTag(tag) { S.quest.tags = S.quest.tags.filter(x => x !== tag); changed(false); },
  async addGamePrereq() {
    const id = await pickQuest('Pick a quest that must be finished first', true);
    if (id && id !== S.quest.id && !S.quest.prerequisiteQuestIds.includes(id)) { S.quest.prerequisiteQuestIds.push(id); changed(false); }
  },
  async pickOfferGameQuest() {
    const id = await pickQuest('Which game quest unlocks this offer?', false);
    if (id) { S.offer.unlockedByQuestId = id; changed(false); }
  },
  async pickTraderUnlock() {
    const id = await pickQuest(`Which quest unlocks ${S.t.file.name}?`, true);
    if (id) { S.t.file.unlockQuestId = id; changed(true); renderPage(false); }
  },
  runChecks: () => { runChecks(); renderAll(false); toast('Checks done'); },

  // ---- offers
  async addOffer() {
    const tpl = await pickItem('Weapon');
    if (!tpl) return;
    const o = fillOffer({ itemTpl: tpl, cost: [{ itemTpl: CUR.RUB, count: 50000 }] });
    const list = S.t.file.offers;
    list.splice(S.offer ? list.indexOf(S.offer) + 1 : list.length, 0, o);
    S.offer = o;
    changed(true);
  },
  dupOffer() {
    if (!S.offer) return;
    const o = clone(S.offer); o.id = newId(); delete o.unlockedByQuestId;
    const list = S.t.file.offers; list.splice(list.indexOf(S.offer) + 1, 0, o); S.offer = o;
    changed(true);
  },
  async removeOffer() {
    if (!S.offer) return;
    const used = questsUnlocking(S.t, S.offer);
    if (used.length && !await confirmBox('Remove offer', `${used.map(u => u.q.name).join(', ')} unlock(s) this offer. Remove it anyway? Those rewards will point at nothing.`, 'Remove')) return;
    const list = S.t.file.offers, i = list.indexOf(S.offer);
    list.splice(i, 1); S.offer = list[Math.min(i, list.length - 1)] || null;
    changed(true);
  },
  moveOffer: d => { if (S.offer && move(S.t.file.offers, S.offer, d)) changed(false); },
  async addCost(tpl) {
    if (!S.offer) return;
    if (!tpl) { tpl = await pickItem('all'); if (!tpl) return; }
    const existing = S.offer.cost.find(c => c.itemTpl === tpl);
    if (!existing) S.offer.cost.push({ itemTpl: tpl, count: isMoney(tpl) ? (tpl === CUR.RUB ? 50000 : 500) : 1 });
    changed(false);
  },
  goQuest: id => { const q = S.t.file.quests.find(x => x.id === id); if (!q) return; selectQuest(q); S.page = 'quests'; renderAll(true); },
  goOffer: id => { const o = S.t.file.offers.find(x => x.id === id); if (!o) return; S.offer = o; S.page = 'offers'; renderAll(true); },

  // ---- quests
  addQuest() {
    const q = fillQuest({ name: 'New quest' });
    const list = S.t.file.quests;
    list.splice(S.quest ? list.indexOf(S.quest) + 1 : list.length, 0, q);
    selectQuest(q);
    changed(true);
  },
  dupQuest() {
    if (!S.quest) return;
    const q = clone(S.quest); q.id = newId(); q.name += ' (copy)'; delete q.image; fillQuest(q);
    q.conditions.forEach(c => c.id = newId()); q.rewards.forEach(r => r.id = newId());
    const list = S.t.file.quests; list.splice(list.indexOf(S.quest) + 1, 0, q); selectQuest(q);
    changed(true);
  },
  async removeQuest() {
    if (!S.quest) return;
    const users = allQuests().filter(x => x.q.prerequisiteQuestIds.includes(S.quest.id));
    if (!await confirmBox('Remove quest', `Remove "${S.quest.name}"?${users.length ? `\n\n${users.map(u => u.q.name).join(', ')} require(s) it and would never unlock (the checks will show them).` : ''}`, 'Remove')) return;
    const list = S.t.file.quests, i = list.indexOf(S.quest);
    list.splice(i, 1); selectQuest(list[Math.min(i, list.length - 1)] || null);
    changed(true);
  },
  moveQuest: d => { if (S.quest && move(S.t.file.quests, S.quest, d)) changed(false); },
  dropPrereq: id => { S.quest.prerequisiteQuestIds = S.quest.prerequisiteQuestIds.filter(x => x !== id); changed(false); },
  addCond() {
    const c = fillCond({ option: S.way || 1 });
    const list = S.quest.conditions; list.splice(S.cond ? list.indexOf(S.cond) + 1 : list.length, 0, c); S.cond = c;
    changed(false);
  },
  dupCond() { if (!S.cond) return; const c = clone(S.cond); c.id = newId(); const l = S.quest.conditions; l.splice(l.indexOf(S.cond) + 1, 0, c); S.cond = c; changed(false); },
  removeCond() {
    if (!S.cond) return;
    const l = S.quest.conditions; l.splice(l.indexOf(S.cond), 1);
    S.cond = l.find(c => (c.option || 1) === S.way) || null;
    if (!S.cond) { S.way = ways(S.quest)[0]; S.cond = l.find(c => (c.option || 1) === S.way) || null; }
    changed(false);
  },
  moveCond: d => { if (S.cond && move(S.quest.conditions, S.cond, d)) changed(false); },
  addReward() {
    const r = fillReward({ value: 1000 });
    const l = S.quest.rewards; l.splice(S.reward ? l.indexOf(S.reward) + 1 : l.length, 0, r); S.reward = r;
    changed(false);
  },
  dupReward() { if (!S.reward) return; const r = clone(S.reward); r.id = newId(); const l = S.quest.rewards; l.splice(l.indexOf(S.reward) + 1, 0, r); S.reward = r; changed(false); },
  removeReward() { if (!S.reward) return; const l = S.quest.rewards, i = l.indexOf(S.reward); l.splice(i, 1); S.reward = l[Math.min(i, l.length - 1)] || null; changed(false); },
  moveReward: d => { if (S.reward && move(S.quest.rewards, S.reward, d)) changed(false); },
  async chooseQuestImage() {
    try {
      const r = await host.call('chooseQuestImage', { folder: S.t.folder, questId: S.quest.id });
      if (!r) return;
      S.t.images[r.file] = r.url; S.quest.image = r.file;
      changed(false); toast('Quest image saved');
    } catch (err) { errorBox(err); }
  },
  removeQuestImage() { delete S.quest.image; changed(false); },

  // ---- generic list helpers (inline ✕, pickers, chips)
  removeAt(arg) { const [k, i] = arg.split('|'); const l = L.get(k); if (!l) return; l.splice(Number(i), 1); changed(false); },
  addTo(arg) { const [k, id] = arg.split('|'); const l = L.get(k); if (l && !l.includes(id)) { l.push(id); changed(false); } },
  toggleIn(arg) {
    const [k, v] = arg.split('|'); const l = L.get(k); if (!l) return;
    const i = l.findIndex(x => String(x).toLowerCase() === v.toLowerCase());
    if (i >= 0) l.splice(i, 1); else l.push(v);
    changed(false);
  },
  async pickTo(arg) {
    const [k, filter] = arg.split('|'); const l = L.get(k); if (!l) return;
    const id = await pickItem(filter);
    if (id && !l.includes(id)) { l.push(id); changed(false); }
  },
  async pickCaliber(k) {
    const l = L.get(k); if (!l) return;
    const id = await pickItem('Ammo');
    if (!id) return;
    const cal = item(id)?.k;
    if (!cal) return toast('That item has no caliber — pick a bullet');
    if (!l.includes(cal)) { l.push(cal); changed(false); }
  },
  async pickInto(arg) {
    const [k, filter] = arg.split('|'); const b = B.get(k); if (!b) return;
    const id = await pickItem(filter);
    if (id) { b.set(id); changed(false); }
  },

  // ---- trader
  addLoyalty() { const l = S.t.file.loyaltyLevels; const last = l[l.length - 1]; l.push({ minLevel: Math.min(79, last.minLevel + 10), minSalesSum: last.minSalesSum + 1000000, minStanding: +(last.minStanding + .2).toFixed(2), buyPriceCoef: Math.max(0, last.buyPriceCoef - 5) }); changed(true); },
  removeLoyalty() { S.t.file.loyaltyLevels.pop(); changed(true); },
  async chooseAvatar() {
    try {
      const r = await host.call('chooseAvatar', { folder: S.t.folder });
      if (!r) return;
      S.t.images[r.file] = r.url; S.t.file.avatar = r.file; S.t.avatarColor = r.avatarColor;
      changed(true); toast('New icon saved — restart the SPT server to see it in game');
    } catch (err) { errorBox(err); }
  },
  openFolder: () => host.call('openFolder', { folder: S.t?.folder || '' }),
  async newTrader() {
    const name = await promptBox('New trader', 'Trader name');
    if (!name) return;
    try {
      const t = normalize({ ...(await host.call('newTrader', { name })), dirty: false });
      t.savedJson = JSON.stringify(t.file);
      S.traders.push(t);
      resetHistory();
      S.page = 'trader';
      selectTrader(t);
      runChecks(); renderAll(true);
      log('ok', name, `Created trader ${name}.`, t);
    } catch (err) { errorBox(err); }
  },
  async deleteTrader() {
    const t = S.t; if (!t) return;
    if (!await confirmBox('Remove trader', `Remove "${t.file.name}"?\n\nIts folder is moved to "deleted_traders" (nothing is erased), so you can put it back later.`, 'Remove')) return;
    try {
      const target = await host.call('deleteTrader', { folder: t.folder });
      S.traders.splice(S.traders.indexOf(t), 1);
      resetHistory();
      selectTrader(S.traders[0] || null);
      log('info', t.file.name, `Moved to ${target}`);
      runChecks(); renderAll(true);
    } catch (err) { errorBox(err); }
  },

  // ---- files
  async browse() {
    if (!await discardOk()) return;
    try { const snap = await host.call('browseModFolder'); if (snap) applySnapshot(snap); } catch (err) { errorBox(err); }
  },
  async reload() {
    if (!await discardOk()) return;
    try { applySnapshot(await host.call('reload')); toast('Reloaded'); } catch (err) { errorBox(err); }
  },
  async save() {
    runChecks();
    const dirty = S.traders.filter(t => t.dirty);
    if (!dirty.length) { renderAll(false); return toast('Nothing to save'); }
    const errors = S.checks.filter(c => c.level === 'error' && dirty.includes(c.trader));
    if (errors.length) {
      const list = errors.slice(0, 6).map(e => `• ${e.where}: ${e.message}`).join('\n') + (errors.length > 6 ? `\n… and ${errors.length - 6} more` : '');
      const ok = await confirmBox('Check before saving', `${errors.length} problem(s) will stop things from working:\n\n${list}`, 'Save anyway', 'Show me');
      if (!ok) { S.checkFilter = 'error'; S.page = 'checks'; renderAll(true); return; }
    }
    let saved = 0;
    for (const t of dirty) {
      try {
        await host.call('saveTrader', { folder: t.folder, text: JSON.stringify(t.file, (k, v) => v === null ? undefined : v, 2) });
        t.dirty = false; t.migrated = false; t.savedJson = JSON.stringify(t.file); saved++;
        const e = S.checks.filter(c => c.trader === t && c.level === 'error').length;
        log(e ? 'warning' : 'ok', t.file.name, e ? `Saved with ${e} error(s) — see the checks.` : 'Saved. Restart the SPT server to apply.', t);
      } catch (err) { log('error', t.file.name, 'Could not save: ' + err.message, t); }
    }
    renderAll(false);
    status(`Saved ${saved} trader(s) at ${nowText()}. Restart the SPT server to apply.`);
    toast(`Saved ${saved} trader(s)`);
  },
  goCheck() {
    const e = S.check; if (!e?.trader) return;
    if (e.trader !== S.t) selectTrader(e.trader, false);
    if (e.target && S.t.file.offers.includes(e.target)) { S.offer = e.target; S.page = 'offers'; }
    else if (e.target && S.t.file.quests.includes(e.target)) { selectQuest(e.target); S.page = 'quests'; }
    else S.page = 'trader';
    renderAll(true);
  },
  appearance: (arg, el) => appearanceMenu(el),
  undo: () => undo(),
  redo: () => redo(),
};

/** After a structural change: mark unsaved, re-check soon, redraw. */
function changed(pageToo) {
  markDirty();
  renderPage(false);
  renderDetails(false);
  renderLight();
  if (pageToo) document.querySelector('#page .row.sel')?.scrollIntoView({ block: 'nearest' });
}

// =====================================================================
// Appearance (background picture, darkness, button color)
// =====================================================================

function applyBackground(url) {
  S.background = url || null;
  $('#bg').style.backgroundImage = url ? `url("${url}")` : '';
  document.body.classList.toggle('has-bg', !!url);
}
function applyUi() {
  const u = S.ui;
  const root = document.documentElement.style;
  const accent = u.accent || '#1ed760';
  root.setProperty('--accent', accent);
  const n = parseInt(accent.slice(1), 16);
  const light = (n >> 16) * .3 + ((n >> 8) & 255) * .59 + (n & 255) * .11;
  root.setProperty('--on-accent', light > 150 ? '#000' : '#fff');
  root.setProperty('--dim', String((u.dim ?? 55) / 100));
  if (u.right) root.setProperty('--right', clamp(u.right, 380, 1400) + 'px');
  if (u.page && !S.pageRestored) { S.page = u.page; S.pageRestored = true; }
}
let saveUiTimer = 0;
function saveUi() {
  clearTimeout(saveUiTimer);
  saveUiTimer = setTimeout(() => host.call('saveUi', { ui: S.ui }).catch(() => { }), 300);
}

function appearanceMenu(anchor) {
  closePopover();
  const u = S.ui;
  const dim = u.dim ?? 55;
  const pop = document.createElement('div');
  pop.className = 'popover';
  pop.innerHTML = `
    <button data-pop="bg">🖼  Background picture…</button>
    ${[[25, 'Picture: bright'], [55, 'Picture: medium'], [75, 'Picture: dark']].map(([v, t]) =>
      `<button data-pop="dim" data-v="${v}" ${S.background ? '' : 'disabled'}>${t}${dim === v ? '<span class="check">✓</span>' : ''}</button>`).join('')}
    <button data-pop="nobg" ${S.background ? '' : 'disabled'}>Remove picture</button>
    <hr>
    <label class="menu">Button color <input type="color" value="${u.accent || '#1ed760'}" data-pop="color" style="margin-left:auto"></label>
    <button data-pop="green" ${u.accent ? '' : 'disabled'}>Button color: default green</button>`;
  document.body.appendChild(pop);
  const r = anchor.getBoundingClientRect();
  pop.style.top = (r.bottom + 6) + 'px';
  pop.style.left = Math.min(window.innerWidth - pop.offsetWidth - 8, r.left) + 'px';
  pop.addEventListener('click', async e => {
    const b = e.target.closest('[data-pop]');
    if (!b || b.dataset.pop === 'color') return;
    const what = b.dataset.pop;
    if (what === 'bg') { const url = await host.call('chooseBackground').catch(errorBox); if (url) applyBackground(url); }
    if (what === 'nobg') { await host.call('clearBackground'); applyBackground(null); }
    if (what === 'dim') { u.dim = Number(b.dataset.v); applyUi(); saveUi(); }
    if (what === 'green') { delete u.accent; applyUi(); saveUi(); }
    closePopover();
  });
  pop.querySelector('[data-pop=color]').addEventListener('input', e => { u.accent = e.target.value; applyUi(); saveUi(); });
  setTimeout(() => document.addEventListener('mousedown', outside), 0);
  function outside(e) { if (!pop.contains(e.target)) closePopover(); }
  closePopover.fn = outside;
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
$('#modal').addEventListener('mousedown', e => { if (e.target.id === 'modal') closeModal(null); });

function confirmBox(title, message, ok = 'OK', cancel = 'Cancel') {
  return openModal(`<div class="dialog small"><h2>${esc(title)}</h2><div class="req">${esc(message)}</div>
    <div class="buttons"><button class="outline" data-m="0">${esc(cancel)}</button><button class="primary" data-m="1">${esc(ok)}</button></div></div>`,
    m => m.querySelectorAll('[data-m]').forEach(b => b.onclick = () => closeModal(b.dataset.m === '1')));
}
function errorBox(err) {
  return openModal(`<div class="dialog small"><h2>Something went wrong</h2><div class="req">${esc(err?.message || err)}</div>
    <div class="buttons"><button class="primary" data-m>OK</button></div></div>`, m => m.querySelector('[data-m]').onclick = () => closeModal(null));
}
function promptBox(title, label, initial = '') {
  return openModal(`<div class="dialog small"><h2>${esc(title)}</h2><div class="stack"><label>${esc(label)}</label><input type="text" id="promptInput" value="${esc(initial)}"></div>
    <div class="buttons"><button class="outline" data-m="0">Cancel</button><button class="primary" data-m="1">OK</button></div></div>`, m => {
    const input = m.querySelector('#promptInput');
    input.focus();
    const done = ok => closeModal(ok && input.value.trim() ? input.value.trim() : null);
    input.onkeydown = e => { if (e.key === 'Enter') done(true); };
    m.querySelectorAll('[data-m]').forEach(b => b.onclick = () => done(b.dataset.m === '1'));
  });
}
async function discardOk() {
  if (!S.traders.some(t => t.dirty)) return true;
  return confirmBox('Unsaved changes', 'You have unsaved changes. Discard them?', 'Discard');
}

/** Item search. filter: all | weapons+ | Weapon | Grenade | Ammo | Gear | Food | Meds | Money */
function pickItem(filter = 'all') {
  let cat = filter, query = '';
  const all = [...S.items.values()].sort((a, b) => a.n.localeCompare(b.n));
  const match = it => (cat === 'all' || (cat === 'weapons+' ? it.c === 'Weapon' || it.c === 'Grenade' : it.c === cat)) &&
    (!query || it.n.toLowerCase().includes(query) || (it.s || '').toLowerCase().includes(query) || it.i.startsWith(query));
  const draw = m => {
    const found = all.filter(match).slice(0, 400);
    m.querySelector('.picker-list').innerHTML = found.map(it => `<div class="row" data-pick="${it.i}"><div class="cell">
      ${thumb({ text: initials(it.s), color: '#3a3a3a' })}<div class="text"><div class="title">${esc(it.n)}</div><div class="line2">${esc(it.s)}</div></div></div>
      <div class="col">${esc(it.c === 'Ammo' && it.k ? caliberName(it.k) : it.c)}</div></div>`).join('') ||
      `<div class="empty">${S.items.size ? 'Nothing found' : 'Item database not loaded — paste an item id below'}</div>`;
    m.querySelectorAll('[data-cat]').forEach(c => c.classList.toggle('on', c.dataset.cat === cat));
  };
  return openModal(`<div class="dialog"><h2>Pick an item</h2>
    <div class="picker-search">🔍<input type="text" id="pickQuery" placeholder="What are you looking for? (name, short name or id)"></div>
    <div class="chips" style="margin-top:10px">${CATEGORY_FILTERS.map(([v, t]) => `<button class="chip" data-cat="${v}">${t}</button>`).join('')}</div>
    <div class="picker-list"></div>
    <div class="buttons"><input type="text" id="pickId" placeholder="or paste an item id" style="max-width:260px;margin-right:auto">
      <button class="outline" data-m="0">Cancel</button><button class="primary" data-m="1">Use pasted id</button></div></div>`, m => {
    const q = m.querySelector('#pickQuery');
    q.focus();
    let timer = 0;
    q.oninput = () => { clearTimeout(timer); timer = setTimeout(() => { query = q.value.trim().toLowerCase(); draw(m); }, 80); };
    m.querySelectorAll('[data-cat]').forEach(c => c.onclick = () => { cat = c.dataset.cat; draw(m); });
    m.querySelector('.picker-list').onclick = e => { const r = e.target.closest('[data-pick]'); if (r) closeModal(r.dataset.pick); };
    m.querySelector('[data-m="0"]').onclick = () => closeModal(null);
    m.querySelector('[data-m="1"]').onclick = () => {
      const id = m.querySelector('#pickId').value.trim().toLowerCase();
      if (validId(id)) closeModal(id); else toast('An item id is 24 characters of 0-9 / a-f');
    };
    draw(m);
  });
}

/** Pick one of your quests (any trader) and/or one of the game's quests. */
function pickQuest(title, includeMine = true) {
  let query = '';
  const mine = includeMine ? allQuests().filter(x => x.q !== S.quest).map(x => ({ id: x.q.id, n: x.q.name, t: x.t.file.name, mine: true })) : [];
  const game = [...S.gameQuests.values()].map(g => ({ id: g.i, n: g.n, t: g.t, mine: false }));
  const all = [...mine, ...game];
  const draw = m => {
    const found = all.filter(x => !query || x.n.toLowerCase().includes(query) || (x.t || '').toLowerCase().includes(query) || x.id.startsWith(query)).slice(0, 400);
    m.querySelector('.picker-list').innerHTML = found.map(x => `<div class="row" data-pick="${x.id}"><div class="cell">
      ${thumb({ text: x.mine ? 'MY' : (x.t || '?').slice(0, 3), color: x.mine ? 'var(--violet)' : '#3a3a3a' })}<div class="text"><div class="title">${esc(x.n)}</div><div class="line2">${esc(x.t || '')}</div></div></div>
      <div class="col">${x.mine ? 'your quest' : 'game quest'}</div></div>`).join('') ||
      `<div class="empty">${S.gameQuests.size ? 'Nothing found' : 'Game quest list not loaded — paste a quest id below'}</div>`;
  };
  return openModal(`<div class="dialog"><h2>${esc(title)}</h2>
    <div class="picker-search">🔍<input type="text" id="pickQuery" placeholder="Search by quest or trader name"></div>
    <div class="picker-list"></div>
    <div class="buttons"><input type="text" id="pickId" placeholder="or paste a quest id" style="max-width:260px;margin-right:auto">
      <button class="outline" data-m="0">Cancel</button><button class="primary" data-m="1">Use pasted id</button></div></div>`, m => {
    const q = m.querySelector('#pickQuery');
    q.focus();
    let timer = 0;
    q.oninput = () => { clearTimeout(timer); timer = setTimeout(() => { query = q.value.trim().toLowerCase(); draw(m); }, 80); };
    m.querySelector('.picker-list').onclick = e => { const r = e.target.closest('[data-pick]'); if (r) closeModal(r.dataset.pick); };
    m.querySelector('[data-m="0"]').onclick = () => closeModal(null);
    m.querySelector('[data-m="1"]').onclick = () => {
      const id = m.querySelector('#pickId').value.trim().toLowerCase();
      if (validId(id)) closeModal(id); else toast('A quest id is 24 characters of 0-9 / a-f');
    };
    draw(m);
  });
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
// Start
// =====================================================================

host.call('init').then(applySnapshot).catch(errorBox);
