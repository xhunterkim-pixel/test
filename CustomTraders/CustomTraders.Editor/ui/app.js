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
const MONEY_NAME = { [CUR.RUB]: '₽ Roubles', [CUR.USD]: '$ Dollars', [CUR.EUR]: '€ Euros', [CUR.GP]: 'GP Coins', [CUR.LEGA]: 'Lega Medals' };
const MONEY_SYMBOL = { [CUR.RUB]: '₽', [CUR.USD]: '$', [CUR.EUR]: '€', [CUR.GP]: 'GP', [CUR.LEGA]: 'LM' };

const TYPES = ['HandoverItem', 'FindItem', 'Kill', 'Extract', 'UseItem', 'Skill'];
const TYPE_SHORT = { HandoverItem: 'Hand Over', FindItem: 'Find', Kill: 'Kill', Extract: 'Extract', UseItem: 'Use Item', Skill: 'Skill Level' };
const TYPE_LONG = { HandoverItem: 'Hand Over Items / Money', FindItem: 'Find in Raid', Kill: 'Kill', Extract: 'Extract', UseItem: 'Use Items in Raid', Skill: 'Reach a Skill Level' };
const TYPE_COLOR = { HandoverItem: 'var(--blue)', FindItem: 'var(--green)', Kill: 'var(--red)', Extract: 'var(--orange)', UseItem: 'var(--pink)', Skill: 'var(--yellow)' };
const SKILLS = [
  ['Endurance', 'Endurance'], ['Strength', 'Strength'], ['Vitality', 'Vitality'], ['Health', 'Health'], ['StressResistance', 'Stress Resistance'],
  ['Metabolism', 'Metabolism'], ['Immunity', 'Immunity'], ['Perception', 'Perception'], ['Intellect', 'Intellect'], ['Attention', 'Attention'],
  ['Charisma', 'Charisma'], ['Memory', 'Memory'], ['Surgery', 'Surgery'], ['AimDrills', 'Aim Drills'], ['TroubleShooting', 'Troubleshooting'],
  ['CovertMovement', 'Covert Movement'], ['Search', 'Search'], ['MagDrills', 'Mag Drills'], ['LightVests', 'Light Vests'], ['HeavyVests', 'Heavy Vests'],
  ['WeaponTreatment', 'Weapon Maintenance'], ['RecoilControl', 'Recoil Control'], ['Crafting', 'Crafting'], ['HideoutManagement', 'Hideout Management'],
  ['Pistol', 'Pistols'], ['Revolver', 'Revolvers'], ['SMG', 'SMGs'], ['Assault', 'Assault Rifles'], ['Shotgun', 'Shotguns'], ['Sniper', 'Bolt-Action Rifles'],
  ['DMR', 'Marksman Rifles'], ['LMG', 'LMGs'], ['HMG', 'HMGs'], ['Launcher', 'Launchers'], ['AttachedLauncher', 'UBGL'], ['Throwing', 'Throwables'], ['Melee', 'Melee'],
];
const skillName = id => (SKILLS.find(s => s[0] === id) || [id, id || '(Pick a Skill)'])[1];
const BODY_PARTS = [['Head', 'Head'], ['Chest', 'Chest'], ['Stomach', 'Stomach'], ['LeftArm', 'Left Arm'], ['RightArm', 'Right Arm'], ['LeftLeg', 'Left Leg'], ['RightLeg', 'Right Leg']];
const EXIT_STATUSES = [['Survived', 'Survived'], ['Runner', 'Run-Through'], ['Killed', 'Killed'], ['MissingInAction', 'Missing in Action'], ['Left', 'Left the Raid']];
const WAY_COLOR = [null, '#a082ff', '#509bf5', '#f5cd46', '#ff7ab6'];

const MAPS = [
  ['bigmap', 'Customs'], ['factory4_day', 'Factory (Day)'], ['factory4_night', 'Factory (Night)'], ['Woods', 'Woods'],
  ['Shoreline', 'Shoreline'], ['Interchange', 'Interchange'], ['laboratory', 'The Lab'], ['RezervBase', 'Reserve'],
  ['Lighthouse', 'Lighthouse'], ['TarkovStreets', 'Streets of Tarkov'], ['Sandbox', 'Ground Zero'],
  ['Sandbox_high', 'Ground Zero (21+)'], ['Labyrinth', 'The Labyrinth'],
];
const KILL_TARGETS = [
  ['Any', 'Anyone (PMCs, Scavs, Bosses…)'], ['AnyPmc', 'Any PMC'], ['Usec', 'USEC PMCs'], ['Bear', 'BEAR PMCs'],
  ['Savage', 'Scavs (Incl. Raiders, Rogues, Bosses)'], ['Boss', 'Bosses (Pick Which)'],
];
const BOSSES = [
  ['bossBully', 'Reshala'], ['bossKilla', 'Killa'], ['bossKojaniy', 'Shturman'], ['bossGluhar', 'Glukhar'],
  ['bossSanitar', 'Sanitar'], ['bossTagilla', 'Tagilla'], ['bossKnight', 'Knight'], ['followerBigPipe', 'Big Pipe'],
  ['followerBirdEye', 'Birdeye'], ['bossZryachiy', 'Zryachiy'], ['bossBoar', 'Kaban'], ['bossKolontay', 'Kollontay'],
  ['bossPartisan', 'Partisan'], ['sectantPriest', 'Cultist Priest'], ['pmcBot', 'Raider'], ['exUsec', 'Rogue'],
];
const REWARD_TYPES = [
  ['Experience', 'XP', '#a082ff'], ['TraderStanding', 'Standing', '#509bf5'], ['Item', 'Item', '#ffa42b'], ['UnlockOffer', 'Unlock Offer', '#1ed760'],
  ['Skill', 'Skill XP', '#f5cd46'], ['StashRows', 'Stash Rows', '#ff7ab6'],
];
const CATEGORY_FILTERS = [
  ['all', 'All'], ['weapons+', 'Weapons + Grenades'], ['Weapon', 'Weapons'], ['Melee', 'Melee'], ['Grenade', 'Grenades'], ['Ammo', 'Ammo'],
  ['AmmoBox', 'Ammo Packs'], ['WeaponPart', 'Weapon Parts'], ['Gear', 'Gear'], ['Container', 'Containers'], ['Key', 'Keys'],
  ['Barter', 'Barter Items'], ['Food', 'Food & Drink'], ['Meds', 'Medical'], ['QuestItem', 'Quest Items'], ['Special', 'Special'],
  ['Money', 'Money'], ['Other', 'Other'],
];
const CAT_NAME = Object.fromEntries(CATEGORY_FILTERS);
const catName = c => ({ Weapon: 'Weapon', Grenade: 'Grenade', Food: 'Food & Drink', Meds: 'Medical', AmmoBox: 'Ammo Pack', WeaponPart: 'Weapon Part', Key: 'Key', Barter: 'Barter Item', Container: 'Container', QuestItem: 'Quest Item' }[c] || CAT_NAME[c] || c || '');

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
  way: 1, tagFilters: { offers: null, quests: null },
  gameQuestImages: [], deletedCount: 0,
  open: new WeakMap(),  // objective -> Set of opened "must ..." sections
  picked: new Set(),    // offers / quests selected together (Ctrl/Shift-click or drag in empty space)
  search: { offers: '', quests: '', checks: '', mods: '' },
  mods: [], modOff: new Map(), modMemory: {}, modSel: null,
  tradersOpen: false,
  traderRate: 60,       // best % of the handbook value a game trader pays
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
const itemName = id => {
  if (!id) return '(Nothing Picked Yet)';
  if (MONEY_NAME[id] && !item(id)) return MONEY_NAME[id];
  if (item(id)) return item(id).n;
  const m = modOf(id);
  if (m) return `${m.name} [${m.mod}]`;
  return S.items.size ? `(unknown ${id})` : id;
};

// ---- items from other mods (Mods page)
/** Which mod an item id comes from, and whether that mod is usable right now:
 *  on = imported + switched on, off = switched off, gone = imported but the id isn't in it anymore,
 *  missing = the mod folder is gone, removed = the import was deleted (id remembered). */
function modOf(id) {
  if (!id) return null;
  const it = S.items.get(id);
  if (it) return it.m ? { mod: it.m, state: 'on', name: it.n } : null;
  const off = S.modOff?.get(id);
  if (off) return { mod: off.m, state: 'off', name: off.n };
  const mem = S.modMemory?.[id];
  if (!mem) return null;
  const imp = S.mods.find(m => m.name === mem[0]);
  return { mod: mem[0], state: !imp ? 'removed' : imp.error ? 'missing' : 'gone', name: mem[1] };
}
const MOD_STATE = {
  on: ['ON', 'var(--green)'], off: ['SWITCHED OFF', 'var(--orange)'], gone: ['ID NOT IN MOD', 'var(--red)'],
  missing: ['FOLDER MISSING', 'var(--red)'], removed: ['NOT IMPORTED', 'var(--red)'],
};
/** Every item id a trader uses, with where (for "used by" lists and the checks). */
function itemUses(t) {
  const out = [];
  const f = t.file;
  for (const o of f.offers) {
    out.push({ id: o.itemTpl, where: `Offer: ${itemName(o.itemTpl)}`, target: o });
    for (const c of o.cost) out.push({ id: c.itemTpl, where: `Price of ${itemName(o.itemTpl)}`, target: o });
  }
  for (const q of f.quests) {
    for (const c of q.conditions)
      for (const k of ['itemTpls', 'weaponTpls', 'wearingTpls'])
        for (const id of c[k] || []) out.push({ id, where: `${q.name} · Way ${wayLetter(c.option || 1)} objective`, target: q });
    for (const r of q.rewards) if (r.type === 'Item') out.push({ id: r.itemTpl, where: `${q.name} · reward`, target: q });
  }
  return out.filter(u => u.id && !isMoney(u.id));
}
/** mod name -> { state, uses[] } for one trader. */
function traderMods(t) {
  const map = new Map();
  for (const u of itemUses(t)) {
    const m = modOf(u.id);
    if (!m) continue;
    const e = map.get(m.mod) || { mod: m.mod, state: 'on', uses: [] };
    if (m.state !== 'on') e.state = m.state;
    e.uses.push({ ...u, trader: t, state: m.state });
    map.set(m.mod, e);
  }
  return map;
}
/** Mods used by one offer / quest. */
function objMods(t, obj) {
  const names = new Set(), bad = new Set();
  for (const u of itemUses(t)) if (u.target === obj) { const m = modOf(u.id); if (m) { names.add(m.mod); if (m.state !== 'on') bad.add(m.mod); } }
  return { names: [...names], bad: [...bad] };
}
const modCell = mods => mods.names.length ? (mods.bad.length ? '✖ MODDED' : 'MODDED') : 'ORIGINAL';
const modColor = mods => mods.names.length ? (mods.bad.length ? 'var(--red)' : 'var(--accent)') : 'var(--muted)';
function shortName(id) {
  const it = item(id);
  if (!it) return itemName(id);
  return it.s && it.n.length > 24 ? it.s : it.n;
}
const initials = s => (s || '?').replace(/\s+/g, '').slice(0, 3);
const isMoney = id => MONEY.has(id);
const moneyShort = id => [CUR.RUB, CUR.USD, CUR.EUR].includes(id) ? MONEY_SYMBOL[id] : MONEY_NAME[id];
const isMoneyList = l => l.length > 0 && l.every(isMoney);

// ---- prices (from the game's handbook / trader rates / flea prices, loaded by the host)
/** Roubles per 1 unit of a currency (handbook value of the dollar / euro item). */
function rubPer(cur) {
  if (cur === CUR.RUB || !cur) return 1;
  return item(cur)?.h || { [CUR.USD]: 140, [CUR.EUR]: 155 }[cur] || 0;
}
const TRADER_CUR = { RUB: CUR.RUB, USD: CUR.USD, EUR: CUR.EUR };
const traderMoney = (t = S.t) => TRADER_CUR[t?.file.currency] || CUR.RUB;
/** Value of one item in roubles. kind: 'p' = what the best trader pays a player for it, 'h' = handbook, 'f' = flea. */
function itemValue(id, kind = 'p') {
  if (isMoney(id)) return rubPer(id);
  const it = item(id);
  return it?.[kind] || 0;
}
/** Roubles → a currency, rounded like a trader price. */
function toCurrency(rub, cur) {
  const per = rubPer(cur);
  if (!per || !rub) return 0;
  const v = rub / per;
  return cur === CUR.RUB ? Math.max(1, Math.round(v / 10) * 10) : Math.max(1, Math.round(v));
}
/** What everything in a price is worth in roubles (traders' sell value). */
const costValue = cost => cost.reduce((sum, c) => sum + itemValue(c.itemTpl) * c.count, 0);
const money = (rub, cur = CUR.RUB) => `${fmt(toCurrency(rub, cur))} ${MONEY_SYMBOL[cur] || ''}`.trim();
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
  def(q, 'failOnDeath', false); def(q, 'tags', []); def(q, 'notes', '');
  q.conditions.forEach(fillCond);
  q.rewards.forEach(fillReward);
  return q;
}
function fillOffer(o) {
  def(o, 'id', newId()); def(o, 'itemTpl', ''); def(o, 'useDefaultPreset', true); def(o, 'loyaltyLevel', 1);
  def(o, 'unlimited', true); def(o, 'stock', 1); def(o, 'buyLimit', 0); def(o, 'cost', []); def(o, 'notes', ''); def(o, 'tags', []);
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

/** Item list, game quests and imported mods (also sent alone by the Mods page, without touching the traders). */
function applyItems(snap) {
  S.items = new Map((snap.items || []).map(i => [i.i, i]));
  if (snap.gameQuests) S.gameQuests = new Map(snap.gameQuests.map(q => [q.i, q]));
  S.itemsStatus = snap.itemsStatus || '';
  S.traderRate = snap.traderRate || S.traderRate || 60;
  if (snap.gameQuestImages) S.gameQuestImages = snap.gameQuestImages;
  S.mods = snap.mods || [];
  S.modOff = new Map((snap.modItemsOff || []).map(i => [i.i, i]));
  S.modMemory = snap.modMemory || {};
}

function applySnapshot(snap) {
  S.modFolder = snap.modFolder;
  S.ui = snap.ui || S.ui || {};
  applyItems(snap);
  S.deletedCount = snap.deletedCount || 0;
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
    return `<div class="field top"><label>${esc(label)}${opts.extra || ''}</label><textarea data-b="${k}" rows="${opts.rows || 4}" spellcheck="false" ${opts.placeholder ? `placeholder="${esc(opts.placeholder)}"` : ''}>${esc(get())}</textarea></div>`;
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
  badge: (text, color, cls = '') => `<span class="badge ${cls}" style="--c:${color}">${esc(text)}</span>`,
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
  const custom = S.ui.tagColors?.[tag];
  if (custom) return custom;
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

/** Picture of a game item (tarkov.dev keeps one for every item id); kind: icon | grid-image | 512. */
function itemPic(id, kind = 'icon') {
  if (!id || isMoney(id) && !item(id) || view().noItemPics) return null;
  const url = `https://assets.tarkov.dev/${id}-${kind}.webp`;
  return badPics.has(url) ? null : url;
}
/** When a picture can't load (modded item, offline…), the initials under it show instead. */
const picFail = `onerror="picFailed(this)"`;
const badPics = new Set();
/** A picture that can't load is remembered, so redraws don't try (and flicker) again. */
function picFailed(img) { badPics.add(img.getAttribute('src')); img.remove(); }
function thumb(t) {
  if (t.img) return `<img class="thumb${t.wide ? ' wide' : ''}" src="${esc(t.img)}" alt="" loading="lazy">`;
  const dark = t.dark ? ' dark' : '';
  const pic = t.item ? itemPic(t.item) : null;
  return `<div class="thumb${dark}${t.wide ? ' wide' : ''}${pic ? ' pic' : ''}" style="--tc:${t.color || '#3a3a3a'}">${esc(t.text || '')}${pic ? `<img src="${pic}" alt="" loading="lazy" ${picFail}>` : ''}</div>`;
}
/** The quest's picture: your own file, else the game picture you picked, else the game's default. */
const DEFAULT_QUEST_IMAGE = '65899d03adeac0191c51e880';
function gameImageUrl(name) { return S.gameQuestImages.find(g => g.n === name)?.u || null; }
function questPic(t, q) {
  if (q.image && t.images[q.image]) return { url: t.images[q.image], kind: 'mine' };
  if (q.gameImage && gameImageUrl(q.gameImage)) return { url: gameImageUrl(q.gameImage), kind: 'game' };
  const d = gameImageUrl(DEFAULT_QUEST_IMAGE);
  return d ? { url: d, kind: 'default' } : null;
}
function randomGameImage(except) {
  const list = S.gameQuestImages.filter(g => g.n !== except);
  return list.length ? list[Math.floor(Math.random() * list.length)].n : null;
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
    return `<div class="trader ${t === S.t ? 'sel' : ''} ${f.enabled ? '' : 'off'}" data-act="selTrader" data-arg="${i}" data-trader="${i}">
      ${img ? `<img src="${esc(img)}" alt="">` : '<div class="ph"></div>'}
      <div style="min-width:0"><div class="line1"><span class="name">${esc(f.name)}</span>
        ${f.enabled ? '' : ui.badge('OFF', '#b3b3b3')}${t.dirty ? ui.badge('•', 'var(--pink)') : ''}${errors ? ui.badge(errors + ' ✖', 'var(--red)') : ''}${traderModBadge(t)}</div>
      <div class="sub">${f.offers.length} Offers · ${f.quests.length} Quests</div></div></div>`;
  }).join('');
  $('#traders').innerHTML = html || '<div class="empty">No Traders Yet</div>';
  $('#traderMenu').hidden = !S.tradersOpen;
  $('#trashBtn').textContent = `🗑  Deleted Traders${S.deletedCount ? ` (${S.deletedCount})` : ''}`;

  // the trader you're working on, bottom left (click = all traders)
  const t = S.t, f = t?.file;
  const img = t && t.images[f.avatar];
  const me = $('#me');
  me.classList.toggle('open', S.tradersOpen);
  me.dataset.trader = t ? S.traders.indexOf(t) : '';
  me.innerHTML = t ? `${img ? `<img src="${esc(img)}" alt="">` : '<div class="ph"></div>'}
      <div style="min-width:0"><div class="name">${esc(f.name)}${t.dirty ? ' <span style="color:var(--pink)">•</span>' : ''}</div>
      <div class="sub">${f.enabled ? `${f.offers.length} Offers · ${f.quests.length} Quests` : 'Switched OFF'}</div></div><div class="arrow">▲</div>`
    : `<div class="ph"></div><div><div class="name">No Trader</div><div class="sub">Click to Add One</div></div><div class="arrow">▲</div>`;

  // page links with counts
  const errors = S.checks.filter(c => c.level === 'error').length, warnings = S.checks.filter(c => c.level === 'warning').length;
  $('#navOffers').textContent = f ? f.offers.length : '';
  $('#navQuests').textContent = f ? f.quests.length : '';
  $('#navChecks').textContent = errors ? `✖ ${errors}` : warnings ? `⚠ ${warnings}` : '✔';
  const modBad = modList().some(e => modStatus(e) !== 'on' && e.uses.length);
  $('#navMods').textContent = S.mods.length || modBad ? (modBad ? '⚠ ' : '') + `${S.mods.filter(m => m.enabled).length}/${S.mods.length}` : '';
  $('#navMods').className = modBad ? 'bad' : '';
  $('#navChecks').className = errors ? 'bad' : '';
  document.querySelectorAll('#nav .nav').forEach(n => n.classList.toggle('on', n.dataset.arg === S.page));
}

function traderModBadge(t) {
  const mods = [...traderMods(t).values()];
  if (!mods.length) return '';
  const bad = mods.some(m => m.state !== 'on');
  return `<span class="badge ${bad ? 'keep' : ''}" style="--c:${bad ? 'var(--red)' : 'var(--accent)'}" title="Uses items from: ${esc(mods.map(m => m.mod).join(', '))}">${bad ? '✖ ' : ''}MODDED</span>`;
}

// ---------------------------------------------------------------- header

function renderHeader() {
  const t = S.t;
  const f = t?.file;
  const img = t && t.images[f.avatar];
  const avatar = $('#headerAvatar');
  if (img) { if (avatar.getAttribute('src') !== img) avatar.src = img; avatar.hidden = false; } else avatar.hidden = true;
  $('#headerKind').textContent = (f && !f.enabled ? 'TRADER · SWITCHED OFF' : 'TRADER') + ' · ' + { trader: 'PROFILE', offers: 'OFFERS & BARTERS', quests: 'QUESTS', checks: 'CHECKS & LOG', mods: 'MODS' }[S.page];
  $('#headerKind').style.color = f && !f.enabled ? 'var(--orange)' : '';
  $('#headerTitle').textContent = f?.name || 'No trader';
  const needs = t ? [...traderMods(t).keys()] : [];
  $('#headerSub').textContent = !f ? 'Click the trader box at the bottom left to add one.' :
    `${f.offers.length} Offer${f.offers.length === 1 ? '' : 's'} · ${f.quests.length} Quest${f.quests.length === 1 ? '' : 's'} · Restocks Every ${f.refreshMinutesMin}–${f.refreshMinutesMax} Min · ${f.unlockedByDefault ? 'Unlocked From the Start' : 'Locked at Start'}${needs.length ? ` · Needs Mods: ${needs.join(', ')}` : ''}`;
  $('#header').style.setProperty('--hc', headerColor(t?.avatarColor));
  document.querySelectorAll('#nav .nav').forEach(n => n.classList.toggle('on', n.dataset.arg === S.page));
  const box = $('#searchBox'), input = $('#search');
  box.hidden = S.page === 'trader';
  input.placeholder = { offers: 'Search Offers & Barters, “Modded” (Ctrl+F)', quests: 'Search Quests, Tags, Notes (Ctrl+F)', checks: 'Search the Log (Ctrl+F)', mods: 'Search Mods & Their Items (Ctrl+F)' }[S.page] || '';
  if (document.activeElement !== input) input.value = S.search[S.page] || '';
  box.classList.toggle('has', !!input.value);
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

/** Replace only what changed (rows whose text / pictures are the same stay untouched — no flicker while typing). */
function patchChildren(parent, html) {
  const tpl = document.createElement('template');
  tpl.innerHTML = html;
  patchNodes(parent, tpl.content);
}
function patchNodes(parent, next) {
  const olds = [...parent.childNodes], news = [...next.childNodes];
  for (let i = 0; i < news.length; i++) {
    const o = olds[i], n = news[i];
    if (!o) { parent.appendChild(n); continue; }
    if (o.isEqualNode(n)) continue;
    // same kind of container (list, row…): keep it and patch inside, so unchanged pictures stay loaded
    if (o.nodeType === 1 && n.nodeType === 1 && o.tagName === n.tagName && o.className === n.className && o.tagName === 'DIV' && n.childNodes.length && !o.querySelector('input,textarea,select')) {
      for (const a of [...o.attributes]) if (!n.hasAttribute(a.name)) o.removeAttribute(a.name);
      for (const a of [...n.attributes]) if (o.getAttribute(a.name) !== a.value) o.setAttribute(a.name, a.value);
      patchNodes(o, n);
      continue;
    }
    parent.replaceChild(n, o);
  }
  for (let i = olds.length - 1; i >= news.length; i--) olds[i].remove();
}

function renderPage(animate) {
  const page = $('#page');
  const scroll = page.scrollTop;
  B = new Map([...B].filter(([k, v]) => v.zone !== 'page'));
  const zoneStart = bindSeq;
  let html = '';
  if (!S.t && S.page !== 'checks' && S.page !== 'mods') html = '<div class="empty">No trader selected.</div>';
  else if (S.page === 'mods') html = pageMods();
  else if (S.page === 'trader') html = pageTrader();
  else if (S.page === 'offers') html = pageOffers();
  else if (S.page === 'quests') html = pageQuests();
  else html = pageChecks();
  for (const [k, v] of B) if (Number(k.slice(1)) > zoneStart) v.zone = 'page';
  if (animate) page.innerHTML = html;
  else patchChildren(page, html);
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
  const unlock = f.unlockedByDefault ? '' : `<div class="field"><label>Unlocked By</label><div class="itemline">
      <span class="name ${f.unlockQuestId ? '' : 'missing'}">${esc(f.unlockQuestId ? questLabel(f.unlockQuestId) : 'Nothing Yet — Pick the Quest That Unlocks This Trader')}</span>
      <button class="outline" data-act="pickTraderUnlock">Pick Quest…</button></div></div>
      ${ui.hint("The game unlocks traders with a quest reward: completing that quest (one of yours or one of the game's) unlocks this trader. For a level requirement, use a quest that unlocks at that level.")}`;
  return `<div class="big-fields">${card('t-main', 'Trader Profile', `
    ${ui.toggle('Trader Is ON (Off = the Server Skips It; Nothing Is Deleted)', () => f.enabled, set('enabled'), { label: 'In the Game', refresh: 'light' })}
    ${ui.text('Name', () => f.name, set('name'))}
    ${ui.text('Nickname', () => f.nickname, set('nickname'))}
    ${ui.text('Surname', () => f.surname, set('surname'))}
    ${ui.text('Location', () => f.location, set('location'))}
    ${ui.area('Description', () => f.description, set('description'))}
    <div class="field"><label>Currency</label>${ui.chips('', [['RUB', '₽ Roubles'], ['USD', '$ Dollars'], ['EUR', '€ Euros']], () => f.currency, set('currency'), { refresh: 'page' })}</div>
    ${ui.toggle('Available From the Start', () => f.unlockedByDefault, set('unlockedByDefault'), { label: 'Unlocked', refresh: 'page' })}
    ${unlock}
    ${ui.toggle("List This Trader's Offers on the Flea Market", () => f.listOnFlea, set('listOnFlea'), { label: 'Flea Market', refresh: 'light' })}
    ${ui.num('Restock Every (Min)', () => f.refreshMinutesMin, set('refreshMinutesMin'), { min: 1, max: 10080 })}
    ${ui.num('…Up To (Min)', () => f.refreshMinutesMax, set('refreshMinutesMax'), { min: 1, max: 10080 })}`)}
  ${card('t-loyalty', 'Loyalty Levels', `
    ${ui.hint(`What a player needs for each loyalty level (LL1 – LL4). Offers can require a loyalty level. "Spent" is counted in the trader's currency (${{ USD: 'dollars', EUR: 'euros' }[f.currency] || 'roubles'}) — the currency only decides this and what the trader pays when players sell to them; each offer's price is set on the offer. "Buys at %" = how much of an item's value the trader pays.`)}
    <table class="loyalty"><tr><th></th><th>Player Level</th><th>Spent (${cur})</th><th>Standing</th><th>Buys At %</th></tr>${loyalty}</table>
    <div class="toolbar">
      <button class="outline" data-act="addLoyalty" ${f.loyaltyLevels.length >= 4 ? 'disabled' : ''}>+ Loyalty Level</button>
      <button class="danger" data-act="removeLoyalty" ${f.loyaltyLevels.length <= 1 ? 'disabled' : ''}>Remove Last</button>
    </div>`)}</div>`;
}

const COLUMNS = {
  offers: [['title', 'Title'], ['unlock', 'Unlock', 190], ['ll', 'LL', 56], ['stock', 'Stock', 110], ['mod', 'Modded', 100], ['notes', 'Notes', 150]],
  quests: [['title', 'Title'], ['level', 'Level', 80], ['ways', 'Ways', 70], ['mod', 'Modded', 100], ['notes', 'Notes', 150]],
  mods: [['title', 'Mod'], ['items', 'Items', 90], ['used', 'Used By', 170]],
  checks: [['title', 'Title'], ['when', 'When', 90]],
};
const view = () => (S.ui.view ||= {});
/** view()["quests.notes"]: true = hidden, false = always shown, unset = default (Notes: only when something has notes). */
function colShown(list, key) {
  if (key === 'title') return true;
  const v = view()[`${list}.${key}`];
  if (v !== undefined) return !v;
  return key !== 'notes' || !!S.hasNotes?.[list];
}
const shownColumns = list => COLUMNS[list].filter(c => colShown(list, c[0]));
function colWidths(list) {
  const saved = S.ui.colw || {};
  return shownColumns(list).slice(1).map(c => clamp(saved[`${list}.${c[0]}`] || c[2], 50, 700));
}
function applyColumns() {
  document.querySelectorAll('.list[data-list]').forEach(el => {
    const w = colWidths(el.dataset.list);
    el.style.setProperty('--cols', `minmax(160px,1fr) ${w.map(x => x + 'px').join(' ')}`);
    el.classList.toggle('no-idx', !!view().hideIdx);
  });
}
function listHead(list) {
  return `<div class="list-head"><div>#</div>${shownColumns(list).map((c, i) => `<div>${i > 0 ? `<span class="grip" data-grip="${list}|${c[0]}" title="Drag to Resize · Double-Click to Reset"></span>` : ''}<span class="head-text">${esc(c[1])}</span></div>`).join('')}</div>`;
}

/** A line of small dark boxes: [{ label, text, color }] — label is drawn in the color (e.g. the way letter). */
function boxes(list) {
  return `<div class="boxes">${list.map(b => {
    const pics = (b.icons || []).map(itemPic).filter(Boolean).slice(0, 3).map(u => `<img class="bi" src="${u}" alt="" loading="lazy" ${picFail}>`).join('');
    return `<span class="box" style="--c:${b.color || 'var(--muted)'}">${b.label ? `<b>${esc(b.label)}</b>` : ''}${pics}${esc(b.text)}</span>`;
  }).join('')}</div>`;
}

function row({ list, act, arg, sel, three, thumb: th, title, titleColor, badges = [], line2, line3, line3Color, boxes2, boxes3, cols = {}, colColors = {}, colTitles = {} }) {
  const shown = list ? shownColumns(list).slice(1) : [];
  return `<div class="row ${sel ? 'sel' : ''} ${three ? 'three' : ''}" data-act="${act}" data-arg="${arg}" data-row="${arg}">
    <div class="idx">${Number(arg) + 1}</div>
    <div class="cell">${thumb(th)}<div class="text">
      <div class="line1"><span class="title" ${titleColor ? `style="color:${titleColor}"` : ''}>${esc(title)}</span><span class="badges">${badges.map(b => ui.badge(b[0], b[1], b[2])).join('')}</span></div>
      ${boxes2 ? boxes(boxes2) : line2 ? `<div class="line2">${esc(line2)}</div>` : ''}
      ${boxes3 ? boxes(boxes3) : line3 ? `<div class="line3" ${line3Color ? `style="color:${line3Color}"` : ''}>${esc(line3)}</div>` : ''}
    </div></div>
    ${shown.map(([k]) => `<div class="col ${k}" ${colColors[k] ? `style="color:${colColors[k]}"` : ''} ${k === 'notes' && cols[k] ? `title="${esc(cols[k])}"` : colTitles[k] ? `title="${esc(colTitles[k])}"` : ''}>${cols[k] && typeof cols[k] === 'object' ? cols[k].html : esc(cols[k] ?? '')}</div>`).join('')}
  </div>`;
}

function offerUnlock(t, o) {
  const mine = questsUnlocking(t, o);
  if (mine.length) return { kind: 'mine', text: '🔒 ' + mine.map(u => u.q.name).join(', '), badge: ['QUEST', 'var(--orange)'] };
  if (o.unlockedByQuestId) return { kind: 'game', text: '🔒 ' + questLabel(o.unlockedByQuestId), badge: ['GAME QUEST', 'var(--orange)'] };
  return { kind: 'start', text: 'From Start', badge: null };
}

/** Search box: every word typed must appear somewhere in the text. */
function matches(page, ...texts) {
  const q = (S.search[page] || '').trim().toLowerCase();
  if (!q) return true;
  const hay = texts.flat().join(' ').toLowerCase();
  return q.split(/\s+/).every(w => hay.includes(w));
}
/** Offers / quests picked together (drag in empty space, Ctrl- or Shift-click). */
const picked = primary => S.picked.size > 1 ? [...S.picked] : primary ? [primary] : [];
const isPicked = (o, primary) => S.picked.size > 1 ? S.picked.has(o) : o === primary;
const countLabel = (text, n) => n > 1 ? `${text} (${n})` : text;

/** Your tags of one list (offers or quests) as filter chips; right-click a tag to rename / recolor / remove it. */
function tagBar(page, list) {
  const all = [...new Set(list.flatMap(x => x.tags))].sort((a, b) => a.localeCompare(b));
  if (S.tagFilters[page] && !all.includes(S.tagFilters[page])) S.tagFilters[page] = null;
  if (!all.length) return '';
  const cur = S.tagFilters[page];
  return `<div class="toolbar"><span class="muted small">Tags:</span>
    <button class="chip ${!cur ? 'on' : ''}" data-act="tagFilter" data-arg="">All</button>
    ${all.map(tag => `<button class="chip tagchip ${cur === tag ? 'on' : ''}" style="--c:${tagColor(tag)}" data-act="tagFilter" data-arg="${esc(tag)}" data-tag="${esc(tag)}" title="Right-click: Rename, Color, Remove">${esc(tag)} <small>${list.filter(x => x.tags.includes(tag)).length}</small></button>`).join('')}</div>`;
}

function pageOffers() {
  const t = S.t;
  const shown = [];
  (S.hasNotes ||= {}).offers = t.file.offers.some(o => o.notes);
  const tags = tagBar('offers', t.file.offers);
  const rows = t.file.offers.map((o, i) => {
    const u = offerUnlock(t, o);
    const price = priceKind(o);
    if (S.tagFilters.offers && !o.tags.includes(S.tagFilters.offers)) return '';
    if (!matches('offers', itemName(o.itemTpl), item(o.itemTpl)?.s || '', costText(o), u.text, o.notes, o.tags, price?.[0] || '', `LL${o.loyaltyLevel}`, objMods(t, o).names.length ? ['modded', ...objMods(t, o).names] : 'original')) return '';
    shown.push(o);
    const badges = [];
    if (u.badge) badges.push(u.badge);
    if (price) badges.push(price);
    if (o.loyaltyLevel > 1) badges.push([`LL${o.loyaltyLevel}`, 'var(--violet)']);
    if (!view().hideTags) for (const tag of o.tags) badges.push([tag.toUpperCase(), tagColor(tag)]);
    const mods = objMods(t, o);
    if (mods.bad.length) badges.push(['✖ MOD MISSING', 'var(--red)', 'keep']);
    return row({
      list: 'offers', act: 'selOffer', arg: i, sel: isPicked(o, S.offer),
      thumb: { text: initials(item(o.itemTpl)?.s || itemName(o.itemTpl)), color: u.kind !== 'start' ? 'var(--orange)' : '#3a3a3a', dark: u.kind !== 'start', item: o.itemTpl, wide: true },
      title: itemName(o.itemTpl), badges,
      line2: costText(o),
      cols: { unlock: u.text, ll: `LL${o.loyaltyLevel}`, stock: (o.unlimited ? 'Unlimited' : `${o.stock} / Restock`) + (o.buyLimit > 0 ? ` · Max ${o.buyLimit}` : ''), notes: o.notes, mod: modCell(mods) },
      colColors: { unlock: u.kind !== 'start' ? 'var(--orange)' : 'var(--green)', mod: modColor(mods) },
      colTitles: { mod: mods.names.join(', ') },
    });
  }).join('');
  S.shownRows = shown;
  const n = picked(S.offer).length;
  return `<div class="toolbar sticky">
      <button class="primary" data-act="addOffer">+ Add Offer</button>
      <button class="outline" data-act="dupOffer" ${n ? '' : 'disabled'}>${countLabel('Duplicate', n)}</button>
      <button class="danger" data-act="removeOffer" ${n ? '' : 'disabled'} title="Del">${countLabel('Remove', n)}</button>
      <button class="icon-btn" data-act="moveOffer" data-arg="-1" title="Move Up">▲</button>
      <button class="icon-btn" data-act="moveOffer" data-arg="1" title="Move Down">▼</button>
      ${S.search.offers || S.tagFilters.offers ? `<span class="muted small">${shown.length} of ${t.file.offers.length} shown</span>` : ''}
    </div>${tags}
    <div class="list" data-list="offers">${listHead('offers')}${rows || `<div class="empty">${t.file.offers.length ? 'Nothing matches the search / tag' : 'No offers yet — click + Add Offer'}</div>`}</div>`;
}

function pageQuests() {
  const t = S.t;
  const v = view();
  const tagsHtml = tagBar('quests', t.file.quests);
  const shown = [];
  (S.hasNotes ||= {}).quests = t.file.quests.some(q => q.notes);
  const rows = t.file.quests.map((q, i) => {
    if (S.tagFilters.quests && !q.tags.includes(S.tagFilters.quests)) return '';
    if (!matches('quests', q.name, q.tags, q.notes, q.description, q.conditions.map(shortCondition), rewardBoxes(t, q).map(b => `${b.label || ''} ${b.text}`), `level ${q.minLevel}`, q.failOnDeath ? 'hardcore' : '')) return '';
    shown.push(q);
    const w = ways(q);
    const badges = [];
    if (!v.hideWaysTag) badges.push([`${w.length} WAY${w.length > 1 ? 'S' : ''}`, 'var(--violet)']);
    if (q.failOnDeath) badges.push(['HARDCORE', 'var(--red)']);
    if (q.prerequisiteQuestIds.length) badges.push([`AFTER ${q.prerequisiteQuestIds.length} QUEST${q.prerequisiteQuestIds.length > 1 ? 'S' : ''}`, 'var(--blue)']);
    if (!v.hideTags) for (const tag of q.tags) badges.push([tag.toUpperCase(), tagColor(tag)]);
    if (S.checks.some(c => c.target === q && c.level === 'error')) badges.push(['✖ PROBLEM', 'var(--red)', 'keep']);
    const mods = objMods(t, q);
    if (mods.bad.length) badges.push(['✖ MOD MISSING', 'var(--red)', 'keep']);
    const pic = questPic(t, q);
    return row({
      list: 'quests', act: 'selQuest', arg: i, sel: isPicked(q, S.quest), three: true,
      thumb: pic ? { img: pic.url } : { text: String(q.minLevel), color: 'var(--violet)' },
      title: q.name, badges,
      boxes2: q.conditions.length
        ? w.map(o => {
          const conds = q.conditions.filter(c => (c.option || 1) === o);
          return { label: w.length > 1 ? wayLetter(o) : '', color: WAY_COLOR[o], text: conds.map(shortCondition).join(' + '), icons: conds.flatMap(condIcons) };
        })
        : [{ text: 'No Objectives Yet' }],
      boxes3: v.hideRewards ? null : q.rewards.length ? rewardBoxes(t, q) : [{ text: 'No Rewards Yet' }],
      cols: { level: `Lvl ${q.minLevel}`, ways: String(w.length), notes: q.notes, mod: modCell(mods) },
      colColors: { mod: modColor(mods) },
      colTitles: { mod: mods.names.join(', ') },
    });
  }).join('');
  S.shownRows = shown;
  const n = picked(S.quest).length;
  return `<div class="toolbar sticky">
      <button class="primary" data-act="addQuest">+ Add Quest</button>
      <button class="outline" data-act="dupQuest" ${n ? '' : 'disabled'}>${countLabel('Duplicate', n)}</button>
      <button class="danger" data-act="removeQuest" ${n ? '' : 'disabled'} title="Del">${countLabel('Remove', n)}</button>
      <button class="icon-btn" data-act="moveQuest" data-arg="-1" title="Move Up">▲</button>
      <button class="icon-btn" data-act="moveQuest" data-arg="1" title="Move Down">▼</button>
      ${S.search.quests || S.tagFilters.quests ? `<span class="muted small">${shown.length} of ${t.file.quests.length} shown</span>` : ''}
    </div>${tagsHtml}
    <div class="list" data-list="quests">${listHead('quests')}${rows || `<div class="empty">${t.file.quests.length ? 'Nothing matches the search / tag' : 'No quests yet — click + Add Quest'}</div>`}</div>`;
}

function pageChecks() {
  const all = [...S.history, ...[...S.checks].sort((a, b) => LEVELS.indexOf(a.level) - LEVELS.indexOf(b.level))];
  const shown = all.filter(e => (!S.checkFilter || e.level === S.checkFilter) && matches('checks', e.message, e.where));
  const count = l => all.filter(e => e.level === l).length;
  const filter = (lvl, text) => `<button class="chip ${S.checkFilter === lvl ? 'on' : ''}" data-act="checkFilter" data-arg="${lvl || ''}">${text}</button>`;
  const rows = shown.map((e, i) => row({
    list: 'checks', act: 'selCheck', arg: i, sel: e === S.check,
    thumb: { text: LEVEL_ICON[e.level], color: `var(--${LEVEL_COLOR[e.level]})`, dark: e.level !== 'info' },
    title: e.message, line2: e.where, cols: { when: e.time },
  })).join('');
  S.shownChecks = shown;
  return `<div class="toolbar sticky">
      <button class="primary" data-act="runChecks">Run Checks</button>
      ${filter(null, 'All')}${filter('error', `Errors (${count('error')})`)}${filter('warning', `Warnings (${count('warning')})`)}
      ${filter('info', `Info (${count('info')})`)}${filter('ok', 'OK')}
    </div>
    <div class="list" data-list="checks">${listHead('checks')}${rows || '<div class="empty">Nothing to Show</div>'}</div>`;
}

// ---------------------------------------------------------------- mods page

/** Imported mods + mods your traders use that aren't imported (anymore). */
function modList() {
  const usage = new Map();
  for (const t of S.traders) for (const [name, e] of traderMods(t)) usage.set(name, [...(usage.get(name) || []), ...e.uses]);
  const list = S.mods.map(m => ({ name: m.name, imp: m, uses: usage.get(m.name) || [] }));
  for (const [name, uses] of usage) if (!S.mods.some(m => m.name === name)) list.push({ name, imp: null, uses });
  return list;
}
function modStatus(e) { return !e.imp ? 'removed' : e.imp.error ? 'missing' : e.imp.enabled ? 'on' : 'off'; }
function modItems(name) {
  const on = [...S.items.values()].filter(i => i.m === name);
  return on.length ? on : [...S.modOff.values()].filter(i => i.m === name);
}
const modRemembered = name => Object.values(S.modMemory).filter(v => v[0] === name).length;

function pageMods() {
  const list = modList().filter(e => matches('mods', e.name, e.imp?.folder || '', modItems(e.name).slice(0, 3000).map(i => i.n)));
  if (!S.modSel || !modList().some(e => e.name === S.modSel)) S.modSel = list[0]?.name || null;
  S.shownMods = list;
  const rows = list.map((e, i) => {
    const st = modStatus(e);
    const traders = new Set(e.uses.map(u => u.trader));
    const first = modItems(e.name)[0];
    const toggle = e.imp ? `<button class="modcheck ${e.imp.enabled ? 'on' : ''}" data-act="modToggle" data-arg="${esc(e.name)}" title="${e.imp.enabled ? 'On — click to switch off' : 'Off — click to switch on'}">${e.imp.enabled ? '✓' : ''}</button>` : '';
    return row({
      list: 'mods', act: 'selMod', arg: i, sel: e.name === S.modSel,
      thumb: { text: initials(e.name), color: st === 'on' ? 'var(--accent)' : '#3a3a3a', dark: st === 'on', item: first?.i, wide: true },
      title: e.name, badges: [MOD_STATE[st]].concat(e.uses.length && st !== 'on' ? [['USED — CHECK IT', 'var(--red)', 'keep']] : []),
      line2: e.imp ? (e.imp.error || e.imp.folder) : `Not imported · ${modRemembered(e.name)} remembered id(s)`,
      cols: { items: { html: `${toggle}<span>${e.imp ? fmt(e.imp.count) : '—'}</span>` }, used: e.uses.length ? `${traders.size} Trader${traders.size === 1 ? '' : 's'} · ${e.uses.length} Use${e.uses.length === 1 ? '' : 's'}` : 'Not Used' },
      colColors: { used: e.uses.length ? (st === 'on' ? 'var(--accent)' : 'var(--red)') : 'var(--muted)' },
    });
  }).join('');
  return `<div class="toolbar sticky">
      <button class="primary" data-act="modsScanAll" title="Looks through every folder in ...\\user\\mods for item files and en.json">Scan My Mods Folder</button>
      <button class="outline" data-act="modAddFolder">Import a Mod Folder…</button>
      <button class="outline" data-act="modRescan" ${S.mods.length ? '' : 'disabled'}>Rescan All</button>
    </div>
    ${ui.hint('Mods that add their own items (e.g. WTT-ContentBackport, ISB-Aishi). Imported items show up in every item picker (Modded filter) so they can be sold, bartered, used in objectives and given as rewards. ✓ = switched on. Traders that use a mod get a <b>MODDED</b> tag, and the checks warn when a mod they use is switched off, removed or missing.')}
    <div class="list" data-list="mods">${listHead('mods')}${rows || '<div class="empty">No mods imported yet — click Scan My Mods Folder</div>'}</div>`;
}

function detailsMod() {
  const e = modList().find(x => x.name === S.modSel);
  if (!e) return ['Mods', '<div class="empty">Import mods with the buttons on the left.</div>'];
  const st = modStatus(e);
  const items = modItems(e.name);
  const [stText, stColor] = MOD_STATE[st];
  const explain = {
    on: e.uses.length ? 'Switched on. Your traders use its items, so players need this mod installed.' : 'Switched on. Its items can be picked everywhere.',
    off: e.uses.length ? 'Switched OFF, but your traders still use its items (below). The editor can’t check them while it’s off — switch it back on, or replace those items.' : 'Switched off: its items are hidden from the pickers. Nothing uses it.',
    missing: 'The mod folder is gone (moved or uninstalled). Items from it can’t be checked — if the mod isn’t installed, the game skips these offers / objectives.',
    removed: 'This mod isn’t imported anymore, but its item ids are still used below. Without the mod installed, the game skips them. Import it again, or replace the items.',
  }[st];
  S.modUses = e.uses;
  const uses = e.uses.map((u, i) => `<div class="row" data-act="goUse" data-arg="${i}"><div class="cell">${thumb({ text: initials(itemName(u.id)), item: u.id, color: '#3a3a3a' })}
    <div class="text"><div class="title">${esc(itemName(u.id))}</div><div class="line2">${esc(u.trader.file.name)} › ${esc(u.where)}</div></div>
    <span class="side" style="color:${MOD_STATE[u.state][1]}">${esc(MOD_STATE[u.state][0])}</span></div></div>`).join('');
  const itemRows = items.slice(0, 150).map(it => `<div class="row"><div class="cell">${thumb({ text: initials(it.s || it.n), item: it.i, color: '#3a3a3a', wide: true })}
    <div class="text"><div class="title">${esc(it.n)}</div><div class="line2">${esc(it.s || '')} · ${esc(it.i)}</div></div><span class="side">${esc(catName(it.c))}</span></div></div>`).join('');
  return [e.name, `
    <div class="status" style="--c:${stColor}"><h3>${esc(stText)}<span class="grow"></span>${e.imp ? ui.toggle('Use Its Items', () => e.imp.enabled, () => { }, {}).replace('data-b=', 'data-mod-toggle="' + esc(e.name) + '" data-x=') : ''}</h3><div class="hint" style="margin:0">${explain}</div></div>
    ${card('m-info', 'Import', `
      <div class="req">${e.imp ? `Folder: ${esc(e.imp.folder)}\n${fmt(e.imp.count)} item(s) found` : `Not imported · ${modRemembered(e.name)} id(s) remembered from an earlier import`}</div>
      <div class="toolbar" style="margin-top:10px">
        ${e.imp ? `<button class="outline" data-act="modRescan">Rescan</button><button class="danger" data-act="modRemove" data-arg="${esc(e.name)}">Remove Import</button>`
          : `<button class="primary" data-act="modAddFolder">Import It Again…</button><button class="danger" data-act="modForget" data-arg="${esc(e.name)}" title="The checks won’t be able to name this mod anymore">Forget Its IDs</button>`}
      </div>`)}
    ${card('m-used', `Used By ${e.uses.length ? `(${e.uses.length})` : ''}`, uses ? `<div class="mini">${uses}</div>${ui.hint('Click one to jump to it.')}` : ui.hint('None of your traders use this mod’s items.'))}
    ${card('m-items', `Items (${fmt(items.length)})`, itemRows ? `<div class="mini" style="max-height:420px">${itemRows}</div>${items.length > 150 ? ui.hint(`… and ${fmt(items.length - 150)} more — find them in any item picker (Modded filter).`) : ''}` : ui.hint('No items.'))}`];
}

// ---------------------------------------------------------------- details (right)

function renderDetails(animate, toTop) {
  const box = $('#details');
  const scroll = box.scrollTop;
  B = new Map([...B].filter(([, v]) => v.zone === 'page'));
  L = new Map();
  let title = '', html = '';
  if (S.page === 'checks') [title, html] = detailsCheck();
  else if (S.page === 'mods') [title, html] = detailsMod();
  else if (!S.t) [title, html] = ['', '<div class="empty">Pick or create a trader on the left.</div>'];
  else if (S.page === 'trader') [title, html] = detailsTrader();
  else if (S.picked.size > 1) [title, html] = detailsMulti();
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
    <div class="toolbar"><button class="primary" data-act="chooseAvatar">Choose Icon From PC…</button><button class="outline" data-act="openFolder">Open Folder</button></div>
    ${ui.hint('Any png or jpg; it\'s cropped to a square. The game shows the new icon after you Save and restart the SPT server.')}
    ${card('t-summary', 'Summary', `<div class="req">Sells ${f.offers.length} thing(s), ${locked} of them unlocked by quests.
${f.quests.length} quest(s)${f.quests.length ? `, from level ${minLvl}` : ''}.
Pays ${f.loyaltyLevels[0]?.buyPriceCoef ?? 0}% of an item's value when players sell to them.
${f.unlockedByDefault ? 'Unlocked from the start.' : `Locked at start — unlocked by ${f.unlockQuestId ? questLabel(f.unlockQuestId) : 'nothing yet!'}.`}
${f.enabled ? 'Switched ON — loads into the game.' : 'Switched OFF — the server skips this trader.'}</div>`)}`];
}

function detailsOffer() {
  const t = S.t, o = S.offer;
  if (!o) return ['Offers & Barters', '<div class="empty">Pick an offer, or click + Add Offer.</div>'];
  const unl = questsUnlocking(t, o);
  const u = offerUnlock(t, o);
  const isWeapon = item(o.itemTpl)?.c === 'Weapon';
  const price = priceKind(o);
  const status = u.kind === 'mine'
    ? `<div class="status" style="--c:var(--orange)"><h3>🔒 Locked — Unlocked by Your Quest<span class="grow"></span><button class="outline" data-act="goQuest" data-arg="${esc(unl[0].q.id)}">Go to Quest</button></h3>
        <div class="req">${unl.map(x => `• ${esc(x.q.name)} (level ${x.q.minLevel})`).join('\n')}</div></div>`
    : u.kind === 'game'
      ? `<div class="status" style="--c:var(--orange)"><h3>🔒 Locked — Unlocked by a Game Quest</h3><div class="req">• ${esc(questLabel(o.unlockedByQuestId))}</div></div>`
      : `<div class="status" style="--c:var(--green)"><h3>✔ For Sale From the Start</h3>${ui.hint(`Anyone with LL${o.loyaltyLevel} with ${esc(t.file.name)} can buy it.`)}</div>`;
  const unlockBody = `
    ${ui.chips('', [['start', 'From the Start'], ['mine', 'After One of My Quests'], ['game', 'After a Game Quest']], () => u.kind, v => setOfferUnlock(o, v))}
    ${u.kind === 'mine' ? `<div class="switches">${t.file.quests.map(q => ui.toggle(`${q.name} · lvl ${q.minLevel}`, () => unl.some(x => x.q === q), v => toggleQuestUnlocksOffer(q, o, v))).join('')}</div>
      ${ui.hint('Switching a quest on gives it an "Unlock offer" reward for this offer (you\'ll see it in the quest\'s rewards).')}` : ''}
    ${u.kind === 'game' ? `<div class="itemline"><span class="name">${esc(o.unlockedByQuestId ? questLabel(o.unlockedByQuestId) : '(Pick a Quest)')}</span><button class="outline" data-act="pickOfferGameQuest">Pick Game Quest…</button></div>` : ''}
    ${o.loyaltyLevel > 1 ? ui.hint(`Also needs LL${o.loyaltyLevel} with ${esc(t.file.name)}.`) : ''}`;
  const costs = o.cost.map((c, i) => {
    const k = bind(() => c.count, v => { c.count = Math.max(1, v); }, 'light');
    const lk = listRef(o.cost);
    return `<div class="row"><div class="cell">${thumb({ text: isMoney(c.itemTpl) ? MONEY_SYMBOL[c.itemTpl] : initials(item(c.itemTpl)?.s), color: isMoney(c.itemTpl) ? 'var(--yellow)' : '#3a3a3a', dark: isMoney(c.itemTpl), item: isMoney(c.itemTpl) ? null : c.itemTpl })}
      <div class="text"><div class="title">${esc(isMoney(c.itemTpl) ? MONEY_NAME[c.itemTpl] : itemName(c.itemTpl))}</div></div>
      <input type="number" data-b="${k}" value="${c.count}" min="1" style="width:140px"><button class="icon-btn" data-act="removeAt" data-arg="${lk}|${i}" title="Remove">✕</button></div></div>`;
  }).join('');
  // what the thing is worth, so nobody has to look prices up
  const cur = traderMoney(t);
  const cells = [['p', `Traders Pay (Best, ${S.traderRate}%)`], ['h', 'Handbook Value'], ['f', 'Flea Price']].map(([k, label]) => {
    const v = offerValue(o, k);
    return `<div class="value"><small>${label}</small><b>${v ? money(v, cur) : '—'}</b>${v ? `<button class="outline" data-act="usePrice" data-arg="${k}">Use</button>` : ''}</div>`;
  }).join('');
  const barterValue = costValue(o.cost), worth = offerValue(o, 'p');
  const compare = isBarter(o) && barterValue && worth
    ? ui.hint(`The price is worth about <b>${money(barterValue, cur)}</b> to a trader — ${barterValue >= worth * 1.1 ? 'more than' : barterValue <= worth * .9 ? '<b>less</b> than' : 'about the same as'} the item itself (${money(worth, cur)}).`) : '';
  const values = S.items.size && (worth || offerValue(o, 'h')) ? `<div class="value-grid">${cells}</div>${o.useDefaultPreset && item(o.itemTpl)?.ph ? ui.hint('Values are for the whole assembled gun (default preset).') : ''}${compare}` : '';

  const it = item(o.itemTpl);
  const big = itemPic(o.itemTpl, '512');
  const hero = `<div class="hero">
      <div class="hero-pic">${esc(initials(it?.s || itemName(o.itemTpl)))}${big ? `<img src="${big}" alt="" ${picFail}>` : ''}</div>
      <div class="hero-text"><div class="kind">${esc((it?.c || 'Item').toUpperCase())}${it?.k ? ' · ' + esc(caliberName(it.k)) : ''}</div>
        <div class="hero-name">${esc(it?.s || itemName(o.itemTpl))}</div>
        <div class="muted small">${esc(costText(o))}${o.tags.length ? ' · ' + esc(o.tags.join(', ')) : ''}</div></div></div>`;
  return [itemName(o.itemTpl), `${hero}${status}
    ${card('o-unlock', 'How It Unlocks', unlockBody)}
    ${card('o-item', 'Item', `
      ${ui.item('Item', () => o.itemTpl, v => { o.itemTpl = v; }, 'all')}
      ${isWeapon || o.useDefaultPreset === false ? ui.toggle('Sell the Assembled Gun (Not a Bare Receiver)', () => o.useDefaultPreset, v => { o.useDefaultPreset = v; }, { label: 'Weapon Preset', refresh: 'details' }) : ''}
      ${ui.num('Loyalty Level', () => o.loyaltyLevel, v => { o.loyaltyLevel = v; }, { min: 1, max: 4 })}
      ${ui.toggle('Unlimited Stock', () => o.unlimited, v => { o.unlimited = v; }, { label: 'Stock' })}
      ${o.unlimited ? '' : ui.num('Stock per Restock', () => o.stock, v => { o.stock = v; }, { min: 1, max: 100000 })}
      ${ui.num('Buy Limit per Player', () => o.buyLimit, v => { o.buyLimit = v; }, { min: 0, max: 100000 })}
      ${ui.hint('<b>Stock per Restock</b> = how many the trader has after each restock (shared by everyone). <b>Buy Limit</b> = how many one player may buy per restock (0 = no limit).')}`)}
    ${card('o-price', `Price / Barter ${price ? ui.badge(price[0], price[1]) : ''}`, `
      ${values}
      ${ui.hint('Everything listed is needed to buy it. Only money = <b>BUY</b>; any item in the list = <b>BARTER</b>. New offers start at what traders pay for the item.')}
      <div class="mini">${costs || '<div class="empty" style="padding:14px">No Price Yet</div>'}</div>
      <div class="toolbar" style="margin-top:8px">
        <button class="chip" data-act="addCost" data-arg="${CUR.RUB}">+ ₽ Roubles</button>
        <button class="chip" data-act="addCost" data-arg="${CUR.USD}">+ $ Dollars</button>
        <button class="chip" data-act="addCost" data-arg="${CUR.EUR}">+ € Euros</button>
        <button class="outline" data-act="addCost" data-arg="">+ Barter Item…</button>
      </div>`)}
    ${card('o-notes', 'Tags & Notes', `
      ${tagField(o, S.t.file.offers, 'Group offers your way (e.g. "ARs", "Snipers", "Pistols", "Ammo") — filter by them above the list. Right-click a tag to rename, recolor or remove it.')}
      ${ui.area('Notes', () => o.notes, v => { o.notes = v; }, { rows: 3, placeholder: 'Only you see these (shown in the Notes column).' })}`)}`];
}

/** An offer's worth in roubles: the whole default preset for guns sold assembled. */
function offerValue(o, kind) {
  const it = item(o.itemTpl);
  if (!it) return 0;
  if (o.useDefaultPreset && it.ph) return kind === 'p' ? Math.round(it.ph * S.traderRate / 100) : kind === 'h' ? it.ph : (it.pf || 0);
  return itemValue(o.itemTpl, kind);
}
/** Starting price of a new offer: what traders pay for it, in the trader's currency. */
function defaultCost(o) {
  const cur = traderMoney();
  const v = offerValue(o, 'p');
  return v ? [{ itemTpl: cur, count: toCurrency(v, cur) }] : [{ itemTpl: CUR.RUB, count: 50000 }];
}

/** Tags of one offer / quest: chips (right-click = rename, color…), a box to add one, and tags used elsewhere. */
function tagField(obj, pool, hint) {
  const known = [...new Set(pool.flatMap(x => x.tags))].filter(tag => !obj.tags.includes(tag)).sort((a, b) => a.localeCompare(b));
  return `<div class="field top"><label>Tags</label><div>
    <div class="chips">${obj.tags.map(tag => `<span class="tag" style="--c:${tagColor(tag)}" data-tag="${esc(tag)}">${esc(tag)}<button data-act="removeTag" data-arg="${esc(tag)}" title="Remove">✕</button></span>`).join('')}
      <input type="text" id="tagInput" placeholder="+ Add Tag (Enter)" style="width:150px"></div>
    ${known.length ? `<div class="chips" style="margin-top:6px">${known.map(tag => `<button class="chip tagchip" style="--c:${tagColor(tag)}" data-act="addTag" data-arg="${esc(tag)}" data-tag="${esc(tag)}">+ ${esc(tag)}</button>`).join('')}</div>` : ''}
    ${ui.hint(hint)}</div></div>`;
}
function multiTagField(list) {
  const tags = [...new Set(list.flatMap(x => x.tags))].sort((a, b) => a.localeCompare(b));
  return `<div class="field top"><label>Tags</label><div>
    <div class="chips">${tags.map(tag => `<span class="tag" style="--c:${tagColor(tag)}" data-tag="${esc(tag)}">${esc(tag)} <small>${list.filter(x => x.tags.includes(tag)).length}/${list.length}</small><button data-act="multiTag" data-arg="-${esc(tag)}" title="Remove From All">✕</button></span>`).join('')}
      <input type="text" id="multiTagInput" placeholder="+ Tag All (Enter)" style="width:150px"></div>
    ${ui.hint('Tags group things: questlines ("Main 1", "Kappa Path") or offers ("ARs", "Snipers", "Pistols").')}</div></div>`;
}

// ---- multi-select panel
function detailsMulti() {
  const offers = S.page === 'offers';
  const list = [...S.picked];
  const names = list.map(x => '• ' + (offers ? itemName(x.itemTpl) : x.name));
  const shownNames = names.slice(0, 40).join('\n') + (names.length > 40 ? `\n… and ${names.length - 40} more` : '');
  let edit;
  if (offers) {
    const same = list.every(o => o.loyaltyLevel === list[0].loyaltyLevel) ? list[0].loyaltyLevel : 0;
    edit = card('m-edit', 'Change All at Once', `
      ${ui.chips('Loyalty Level', [1, 2, 3, 4].map(n => [n, 'LL' + n]), () => same, v => { list.forEach(o => { o.loyaltyLevel = Number(v); }); }, { refresh: 'page' })}
      ${ui.chips('Stock', [['u', 'Unlimited'], ['l', 'Limited']], () => list.every(o => o.unlimited) ? 'u' : list.every(o => !o.unlimited) ? 'l' : '', v => { list.forEach(o => { o.unlimited = v === 'u'; }); }, { refresh: 'page' })}
      ${multiTagField(list)}`);
  } else {
    const sameLvl = list.every(q => q.minLevel === list[0].minLevel) ? list[0].minLevel : '';
    edit = card('m-edit', 'Change All at Once', `
      ${ui.num('Unlocks at Level', () => sameLvl, v => { list.forEach(q => { q.minLevel = v; }); }, { min: 1, max: 79, refresh: 'page' })}
      ${multiTagField(list)}`);
  }
  return [`${list.length} ${offers ? 'Offers' : 'Quests'} Selected`, `
    <div class="status" style="--c:var(--accent)"><h3>${list.length} Picked</h3><div class="req">${esc(shownNames)}</div></div>
    <div class="toolbar">
      <button class="outline" data-act="${offers ? 'dupOffer' : 'dupQuest'}">Duplicate All</button>
      <button class="danger" data-act="${offers ? 'removeOffer' : 'removeQuest'}">Remove All (Del)</button>
      <button class="ghost" data-act="clearPicked">Pick Just One</button>
    </div>
    ${edit}
    ${ui.hint('Ctrl-click or Shift-click rows, or hold the left mouse button in an empty space and drag, to pick several. <b>Del</b> removes them; <b>Ctrl+Z</b> brings them back.')}`];
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
  if (!q) return ['Quests', '<div class="empty">Pick a quest, or click + Add Quest.</div>'];
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

  const pic = questPic(t, q);
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
    (w.length < 4 ? '<button class="waytab add" data-act="addWay" title="Give the Player Another Way to Finish This Quest">＋ Way</button>' : '');
  const waysHint = w.length <= 1
    ? 'Every objective below must be done. Want the player to choose (e.g. hand in <i>or</i> kill <i>or</i> pay)? Click <b>＋ Way</b>.'
    : `<b>${w.length} ways</b> — the player finishes ANY ONE way (all objectives inside it). In game each way is its own quest; when one is turned in, the others are closed and disappear.`;
  const objectives = q.conditions.map((c, i) => (c.option || 1) !== S.way ? '' : `<div class="row ${c === S.cond ? 'sel' : ''}" data-act="selCond" data-arg="${i}"><div class="cell">
      ${thumb({ text: TYPE_SHORT[c.type]?.[0] || '?', color: TYPE_COLOR[c.type] || '#999', dark: true, item: condIcons(c)[0] })}
      <div class="text"><div class="line1"><span class="title">${esc(conditionTitle(c))}</span></div></div>
      <span class="side">${esc(conditionDetail(c))}</span></div></div>`).join('');
  const rewards = q.rewards.map((r, i) => {
    const d = rewardRow(t, r);
    return `<div class="row ${r === S.reward ? 'sel' : ''}" data-act="selReward" data-arg="${i}"><div class="cell">${thumb(d.thumb)}
      <div class="text"><div class="title">${esc(d.title)}</div></div><span class="side">${esc(d.side || '')}</span></div></div>`;
  }).join('');

  return [q.name, `
    ${card('q-req', `<span style="color:${reqBad ? 'var(--red)' : 'var(--violet)'}">Unlock Requirements</span>`, `<div class="req">${esc(reqLines.join('\n'))}</div>`)}
    ${card('q-info', 'Quest', `
      ${ui.text('Name', () => q.name, v => { q.name = v; $('#detailsTitle').textContent = v; })}
      ${ui.num('Unlocks at Level', () => q.minLevel, v => { q.minLevel = v; }, { min: 1, max: 79 })}
      ${ui.area('Description', () => q.description, v => { q.description = v; }, { extra: '<br><button class="outline gen" data-act="genText" data-arg="description" title="Write it from the objectives">✨ Generate</button>', placeholder: 'Empty = a description is made from the objectives. Or click ✨ Generate.' })}
      ${ui.area('When Completed', () => q.successMessage, v => { q.successMessage = v; }, { rows: 2, extra: '<br><button class="outline gen" data-act="genText" data-arg="successMessage">✨ Generate</button>' })}
      ${ui.toggle('Fails If the Player Dies, Goes Missing or Leaves a Raid (Can Be Restarted)', () => q.failOnDeath, v => { q.failOnDeath = v; }, { label: 'Hardcore', refresh: 'light' })}
      ${tagField(q, allQuests().map(x => x.q), 'Your own labels (e.g. "Kappa Path", "Main 1") — shown on the list and usable as a filter. Right-click a tag to rename, recolor or remove it. The game never sees them.')}
      <div class="field top"><label>Picture</label><div>
        ${pic ? `<img class="preview" src="${esc(pic.url)}" alt="">` : ''}
        ${ui.hint(pic?.kind === 'mine' ? 'Your own picture.' : pic?.kind === 'game' ? "One of the game's quest pictures." : pic ? "No picture picked — the game's default picture is used." : 'No picture — the game shows its default one.')}
        <div class="toolbar" style="padding:0">
          ${S.gameQuestImages.length ? '<button class="primary" data-act="pickGameImage">Pick a Game Picture…</button><button class="outline" data-act="randomGameImage" title="Another random game picture">🎲 Random</button>' : ''}
          <button class="outline" data-act="chooseQuestImage">From PC…</button>
          ${q.image || q.gameImage ? '<button class="danger" data-act="removeQuestImage">Remove</button>' : ''}</div></div></div>
      ${ui.area('Notes', () => q.notes, v => { q.notes = v; }, { rows: 2, placeholder: 'Only you see these (shown in the Notes column).' })}`)}
    ${card('q-prereq', 'Required Quests', `
      ${ui.hint('Switch on every quest that must be finished first. For a quest with several ways, <b>any one finished way counts</b>. Game quests (Prapor, Therapist…) can be required too.')}
      <div class="switches">${mineSwitches || '<div class="hint">No other quests of yours yet.</div>'}</div>
      ${others ? `<div class="switches" style="margin-top:8px">${others}</div>` : ''}
      <div class="toolbar" style="margin-top:8px"><button class="outline" data-act="addGamePrereq">+ Game Quest…</button></div>`)}
    ${card('q-obj', 'Objectives', `
      <div class="waytabs">${tabs}</div>
      ${ui.hint(waysHint)}
      <div class="mini">${objectives || '<div class="empty" style="padding:14px">No Objectives in This Way Yet</div>'}</div>
      <div class="toolbar" style="margin-top:8px">
        <button class="primary" data-act="addCond">+ Objective</button>
        <button class="outline" data-act="dupCond" ${S.cond ? '' : 'disabled'}>Duplicate</button>
        <button class="danger" data-act="removeCond" ${S.cond ? '' : 'disabled'}>Remove</button>
        <button class="icon-btn" data-act="moveCond" data-arg="-1">▲</button><button class="icon-btn" data-act="moveCond" data-arg="1">▼</button>
        ${w.length > 1 ? `<button class="danger" data-act="removeWay" style="margin-left:auto">Delete Way ${wayLetter(S.way)}</button>` : ''}
      </div>`)}
    ${S.cond && (S.cond.option || 1) === S.way ? objectiveEditor(S.cond) : ''}
    ${card('q-rew', 'Rewards', `
      ${ui.hint('Every way gives the same rewards.')}
      <div class="mini">${rewards || '<div class="empty" style="padding:14px">No Rewards Yet</div>'}</div>
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
  const amountLabel = { Kill: 'How Many Kills', Extract: 'How Many Extracts', UseItem: 'How Many Uses', FindItem: 'How Many to Find', Skill: 'Level to Reach' }[type] || (money ? 'How Much to Pay' : 'How Many');

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
    ${kill ? ui.select('Kill Who', KILL_TARGETS, () => c.killTarget, v => { c.killTarget = v; }) : ''}
    ${kill && c.killTarget === 'Boss' ? ui.multichips('Which Bosses Count (None Picked = Any Boss)', BOSSES, c.bossRoles, { color: 'var(--red)' }) : ''}
    ${type === 'Skill' ? ui.select('Skill', SKILLS, () => c.skill, v => { c.skill = v; }) : ''}
    ${ui.num(amountLabel, () => c.count, v => { c.count = v; }, { min: 1, max: type === 'Skill' ? 51 : 1e9 })}
    ${['HandoverItem', 'FindItem'].includes(type) && !money ? ui.toggle('Items Must Be Found in Raid', () => c.foundInRaid, v => { c.foundInRaid = v; }, { label: 'Found in Raid', refresh: 'light' }) : ''}
    ${items ? itemList(
      { HandoverItem: 'What to Hand Over — Any One of These Counts (Items or Money)', FindItem: 'What to Find — Any One of These Counts', UseItem: 'What to Use — Any One of These Counts (Food, Drinks, Meds)' }[type],
      c.itemTpls, type === 'UseItem' ? 'Meds' : 'all', type === 'HandoverItem') : ''}
    ${kill ? needs('weapon', 'Must Kill With a Specific Weapon or Grenade', c.weaponTpls, () => itemList('Any One of These Counts', c.weaponTpls, 'weapons+')) : ''}
    ${kill ? needs('caliber', 'Must Use Specific Ammo (Caliber)', c.calibers, () => caliberList(c.calibers)) : ''}
    ${kill || type === 'Extract' ? needs('wearing', 'Must Be Wearing Something', c.wearingTpls, () => itemList('Wearing Any One of These', c.wearingTpls, 'Gear')) : ''}
    ${kill || type === 'Extract' || type === 'UseItem' ? needs('maps', 'Only on Specific Maps', c.locations, () => ui.multichips('', MAPS, c.locations, { color: 'var(--green)' })) : ''}
    ${kill ? needs('body', 'Only Hits to Certain Body Parts (e.g. Headshots)', c.bodyParts, () => ui.multichips('', BODY_PARTS, c.bodyParts, { color: 'var(--red)' })) : ''}
    ${kill ? distanceField(c, open) : ''}
    ${kill ? timeField(c, open) : ''}
    ${type === 'Extract' ? ui.multichips('Which Exits Count (None Picked = Survived or Run-Through)', EXIT_STATUSES, c.exitStatuses, { color: 'var(--orange)' }) : ''}
    ${['Kill', 'Extract', 'UseItem'].includes(type) ? ui.toggle(`All of It in a Single Raid (the Count Restarts Every Raid)`, () => c.oneRaid, v => { c.oneRaid = v; }, { refresh: 'light' }) : ''}
    ${objectiveText(c)}
    ${ui.select('Belongs to Way', ways(S.quest).concat(ways(S.quest).length < 4 ? [[1, 2, 3, 4].find(n => !ways(S.quest).includes(n))] : []).map(n => [n, `Way ${wayLetter(n)}`]),
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
  return `<div class="field top"><label>Objective Text</label><div>
    <input type="text" data-b="${k}" value="${esc(c.text)}" placeholder="${esc(tarkovText(c))}" spellcheck="false">
    ${ui.hint(c.text ? 'Your own text is used in game.' : 'Empty = the game shows the grey text above (Tarkov style), updated automatically.')}
    <div class="toolbar" style="padding:0"><button class="outline" data-act="useTarkovText">${c.text ? 'Reset to Tarkov Text' : 'Edit the Tarkov Text'}</button></div>
  </div></div>`;
}

function distanceField(c, open) {
  const on = c.distance > 0 || open.has('distance');
  return `${ui.toggle('Only Kills From a Certain Distance', () => on, v => {
    const set = S.open.get(c) || new Set();
    if (v) { set.add('distance'); if (!c.distance) c.distance = 50; } else { set.delete('distance'); c.distance = 0; }
    S.open.set(c, set);
  })}${on ? `<div style="margin:6px 0 12px 54px">
    ${ui.chips('', [['>=', 'At Least'], ['<=', 'Within']], () => c.distanceCompare, v => { c.distanceCompare = v; })}
    ${ui.num('Meters', () => c.distance, v => { c.distance = v; }, { min: 1, max: 2000 })}</div>` : ''}`;
}

function timeField(c, open) {
  const on = c.daytimeFrom !== c.daytimeTo || open.has('time');
  return `${ui.toggle('Only at Certain In-Raid Hours (e.g. Night)', () => on, v => {
    const set = S.open.get(c) || new Set();
    if (v) { set.add('time'); if (c.daytimeFrom === c.daytimeTo) { c.daytimeFrom = 21; c.daytimeTo = 7; } } else { set.delete('time'); c.daytimeFrom = 0; c.daytimeTo = 0; }
    S.open.set(c, set);
  })}${on ? `<div style="margin:6px 0 12px 54px">
    ${ui.num('From Hour (0-23)', () => c.daytimeFrom, v => { c.daytimeFrom = v; }, { min: 0, max: 23 })}
    ${ui.num('To Hour (0-23)', () => c.daytimeTo, v => { c.daytimeTo = v; }, { min: 0, max: 23 })}
    ${ui.hint('21 → 7 = at night. Uses the in-raid clock.')}</div>` : ''}`;
}

function itemList(label, list, filter, moneyButtons) {
  const lk = listRef(list);
  const rows = list.map((id, i) => {
    const it = item(id);
    return `<div class="row"><div class="cell">${thumb({ text: isMoney(id) ? MONEY_SYMBOL[id] : initials(it?.s), color: isMoney(id) ? 'var(--yellow)' : !it && S.items.size ? 'var(--red)' : '#3a3a3a', dark: isMoney(id), item: isMoney(id) ? null : id })}
      <div class="text"><div class="title">${esc(isMoney(id) && !it ? MONEY_NAME[id] : itemName(id))}</div></div>
      <span class="side">${esc(it ? (it.c === 'Other' ? it.s : `${it.c} · ${it.s}`) : '')}</span>
      <button class="icon-btn" data-act="removeAt" data-arg="${lk}|${i}" title="Remove">✕</button></div></div>`;
  }).join('');
  const money = moneyButtons ? [[CUR.RUB, '+ ₽'], [CUR.USD, '+ $'], [CUR.EUR, '+ €'], [CUR.GP, '+ GP Coin'], [CUR.LEGA, '+ Lega Medal']]
    .map(([id, t]) => `<button class="chip" data-act="addTo" data-arg="${lk}|${id}">${t}</button>`).join('') : '';
  return `<div class="stack"><label>${esc(label)}</label><div class="mini">${rows || '<div class="empty" style="padding:12px">Nothing Added Yet</div>'}</div>
    <div class="toolbar" style="margin-top:6px"><button class="outline" data-act="pickTo" data-arg="${lk}|${filter}">+ Add…</button>${money}</div></div>`;
}

function caliberList(list) {
  const lk = listRef(list);
  const rows = list.map((cal, i) => `<div class="row"><div class="cell">${thumb({ text: '•', color: 'var(--yellow)', dark: true })}
    <div class="text"><div class="title">${esc(caliberName(cal))}</div></div><span class="side">${esc(cal)}</span>
    <button class="icon-btn" data-act="removeAt" data-arg="${lk}|${i}">✕</button></div></div>`).join('');
  return `<div class="stack"><label>Any of These Calibers (Pick Any Bullet of the Caliber)</label><div class="mini">${rows || '<div class="empty" style="padding:12px">Nothing Added Yet</div>'}</div>
    <div class="toolbar" style="margin-top:6px"><button class="outline" data-act="pickCaliber" data-arg="${lk}">+ Add Ammo…</button></div></div>`;
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
    ${r.type === 'Item' ? ui.item('Item', () => r.itemTpl, v => { r.itemTpl = v; }) + ui.num('How Many', () => r.count, v => { r.count = v; }, { min: 1, max: 100000 }) +
      ui.toggle('Found in Raid', () => r.foundInRaid, v => { r.foundInRaid = v; }, { label: 'Found in Raid', refresh: 'light' }) +
      ui.toggle('Give It When the Quest Is Accepted (Not When Completed)', () => r.onStart, v => { r.onStart = v; }, { label: 'When', refresh: 'light' }) : ''}
    ${r.type === 'Skill' ? ui.select('Skill', SKILLS, () => r.skill, v => { r.skill = v; }) + ui.num('Skill Points (100 = 1 Level)', () => r.value, v => { r.value = v; }, { min: 1, max: 5100, step: 10 }) : ''}
    ${r.type === 'StashRows' ? ui.num('Extra Stash Rows', () => r.value, v => { r.value = v; }, { min: 1, max: 50 }) : ''}
    ${r.type === 'UnlockOffer' ? ui.select('Offer', offers, () => r.offerId, v => { r.offerId = v; }) +
      ui.hint(`The offer appears in ${esc(t.file.name)}'s shop once this quest is completed. How many are for sale is set on the offer itself (Offers page → Stock per restock / Buy limit)${offer ? `: currently <b>${offer.unlimited ? 'unlimited' : offer.stock + ' per restock'}${offer.buyLimit ? `, max ${offer.buyLimit} per player` : ''}</b>` : ''}.`) +
      (offer ? `<button class="outline" data-act="goOffer" data-arg="${esc(offer.id)}">Go to Offer</button>` : '') : ''}
  </div>`;
}

// ---------------------------------------------------------------- checks details

function detailsCheck() {
  const e = S.check;
  return ['Details', `
    <div class="card"><h3>Selected</h3>${e ? `<div class="hint">${esc(e.where)}</div><div class="req">${esc(e.message)}</div>
      ${e.trader ? '<div class="toolbar" style="margin-top:10px"><button class="primary" data-act="goCheck">Go to It</button></div>' : ''}` : ui.hint('Select a line to see it in full. Double-click a line to jump there.')}</div>
    <div class="card"><h3>What the Colors Mean</h3><div class="req">✖ Error — won't work: the server skips it, or the quest can never be done / unlocked.
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
  sum.textContent = errors + warnings === 0 ? '✔ All Checks Pass' : `✖ ${errors} Error${errors === 1 ? '' : 's'}   ⚠ ${warnings} Warning${warnings === 1 ? '' : 's'}`;
  sum.style.color = errors ? 'var(--red)' : warnings ? 'var(--orange)' : 'var(--green)';
  $('#checksButton').textContent = errors ? `✖ ${errors}` : warnings ? `⚠ ${warnings}` : '✔ Checks';
  const unsaved = S.traders.filter(t => t.dirty).length;
  $('#unsaved').textContent = unsaved ? `${unsaved} Unsaved` : '';
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
const plural = (n, one, many) => (Number(n) === 1 ? one : many);
function killWho(c) {
  const n = c.count;
  if (c.killTarget === 'Boss') return c.bossRoles.length ? c.bossRoles.map(bossName).join(' / ') : plural(n, 'Boss', 'Bosses');
  return { AnyPmc: plural(n, 'PMC', 'PMCs'), Usec: plural(n, 'USEC PMC', 'USEC PMCs'), Bear: plural(n, 'BEAR PMC', 'BEAR PMCs'), Savage: plural(n, 'Scav', 'Scavs') }[c.killTarget] || plural(n, 'Enemy', 'Enemies');
}
function itemsText(l) { return l.length ? l.map(shortName).join(' / ') : '(Pick Items)'; }
const hour = h => String(h).padStart(2, '0') + ':00';
const partName = p => (BODY_PARTS.find(b => b[0] === p) || [p, p])[1];
/** Every filter an objective has, as short phrases ("With M4A1", "On Customs", "In One Raid"...). */
function conditionExtras(c) {
  const d = [];
  if (c.type === 'Kill') {
    if (c.calibers.length) d.push(`Using ${c.calibers.map(caliberName).join(' / ')} Ammo`);
    if (c.bodyParts.length) d.push(c.bodyParts.length === 1 && c.bodyParts[0] === 'Head' ? 'Headshots Only' : `Hits to ${c.bodyParts.map(partName).join(' / ')}`);
    if (c.distance > 0) d.push(c.distanceCompare === '<=' ? `Within ${c.distance} m` : `From ${c.distance} m+`);
  }
  if (c.type === 'Extract' && c.exitStatuses.length) d.push(`As ${c.exitStatuses.map(x => (EXIT_STATUSES.find(e => e[0] === x) || [x, x])[1]).join(' / ')}`);
  if (['Kill', 'Extract'].includes(c.type) && c.wearingTpls.length) d.push(`While Wearing ${c.wearingTpls.map(shortName).join(' / ')}`);
  if (['Kill', 'Extract', 'UseItem'].includes(c.type) && c.locations.length) d.push(`${c.type === 'Extract' ? 'From' : 'On'} ${c.locations.map(mapName).join(' / ')}`);
  if (c.type === 'Kill' && c.daytimeFrom !== c.daytimeTo) d.push(`Between ${hour(c.daytimeFrom)}–${hour(c.daytimeTo)}`);
  if (['Kill', 'Extract', 'UseItem'].includes(c.type) && c.oneRaid) d.push('In One Raid');
  return d;
}
/** One objective in one line, with everything that's switched on: "Kill 10 PMCs With M4A1, On Customs, In One Raid". */
function shortCondition(c) {
  let base;
  switch (c.type) {
    case 'Kill': base = `Kill ${c.count} ${killWho(c)}${c.weaponTpls.length ? ` With ${c.weaponTpls.map(shortName).join(' / ')}` : ''}`; break;
    case 'Extract': base = c.count > 1 ? `Extract ${c.count} Times` : 'Survive and Extract'; break;
    case 'UseItem': base = `Use ${c.count}× ${itemsText(c.itemTpls)}`; break;
    case 'FindItem': base = `Find ${c.count}× ${itemsText(c.itemTpls)} in Raid`; break;
    case 'Skill': base = `Reach ${skillName(c.skill)} Level ${c.count}`; break;
    default: base = isMoneyList(c.itemTpls) ? `Pay ${fmt(c.count)} ${moneyShort(c.itemTpls[0])}`
      : `Hand In ${c.count}× ${itemsText(c.itemTpls)}${c.foundInRaid ? ' (Found in Raid)' : ''}`;
  }
  return [base, ...conditionExtras(c)].join(', ');
}
/** Items shown as small pictures next to an objective. */
function condIcons(c) {
  if (c.type === 'Kill') return c.weaponTpls.slice(0, 2);
  if (['HandoverItem', 'FindItem', 'UseItem'].includes(c.type)) return c.itemTpls.filter(id => !isMoney(id)).slice(0, 2);
  if (c.type === 'Extract') return c.wearingTpls.slice(0, 1);
  return [];
}
function conditionTitle(c) {
  switch (c.type) {
    case 'Kill': return `Kill ${c.count} ${killWho(c)}`;
    case 'Extract': return c.count > 1 ? `Extract ${c.count} Times` : 'Survive and Extract';
    case 'UseItem': return `Use ${c.count}× ${itemsText(c.itemTpls)}`;
    case 'FindItem': return `Find ${c.count}× ${itemsText(c.itemTpls)} in Raid`;
    case 'Skill': return `Reach ${skillName(c.skill)} Level ${c.count}`;
    default: return isMoneyList(c.itemTpls) ? `Pay ${fmt(c.count)} ${MONEY_NAME[c.itemTpls[0]]}` : `Hand In ${c.count}× ${itemsText(c.itemTpls)}`;
  }
}
function conditionDetail(c) {
  const d = conditionExtras(c);
  if (c.type === 'Kill' && c.weaponTpls.length) d.unshift('With ' + c.weaponTpls.map(shortName).join(' / '));
  if (c.type === 'HandoverItem' && c.foundInRaid && !isMoneyList(c.itemTpls)) d.push('Found in Raid');
  return d.join(' · ');
}
function rewardBoxes(t, q) {
  return q.rewards.map(r => {
    switch (r.type) {
      case 'Experience': return { text: `${fmt(r.value)} XP`, color: '#a082ff' };
      case 'TraderStanding': return { text: `${r.value >= 0 ? '+' : ''}${r.value} Standing`, color: '#509bf5' };
      case 'Item': return { text: `${r.count}× ${shortName(r.itemTpl)}${r.onStart ? ' (On Accept)' : ''}`, color: '#ffa42b', icons: [r.itemTpl] };
      case 'Skill': return { text: `+${fmt(r.value)} ${skillName(r.skill)}`, color: '#f5cd46' };
      case 'StashRows': return { text: `+${fmt(r.value)} Stash Rows`, color: '#ff7ab6' };
      case 'UnlockOffer': {
        const o = t.file.offers.find(x => x.id === r.offerId);
        return o ? { label: isBarter(o) ? 'BARTER' : 'BUY', text: shortName(o.itemTpl), color: isBarter(o) ? 'var(--pink)' : 'var(--blue)', icons: [o.itemTpl] } : { label: 'UNLOCK', text: '?', color: 'var(--red)' };
      }
      default: return { text: r.type };
    }
  });
}

function rewardRow(t, r) {
  switch (r.type) {
    case 'Experience': return { thumb: { text: 'XP', color: '#a082ff' }, title: `${fmt(r.value)} XP` };
    case 'TraderStanding': return { thumb: { text: '+', color: '#509bf5' }, title: `${r.value >= 0 ? '+' : ''}${r.value} Standing With ${t.file.name}` };
    case 'Item': return { thumb: { text: initials(item(r.itemTpl)?.s), color: '#ffa42b', dark: true, item: r.itemTpl }, title: `${r.count} × ${itemName(r.itemTpl)}`, side: [r.onStart ? 'On Accept' : '', r.foundInRaid ? 'Found in Raid' : ''].filter(Boolean).join(' · ') };
    case 'Skill': return { thumb: { text: 'SK', color: '#f5cd46', dark: true }, title: `+${fmt(r.value)} ${skillName(r.skill)} Skill Points` };
    case 'StashRows': return { thumb: { text: '▦', color: '#ff7ab6', dark: true }, title: `+${fmt(r.value)} Stash Rows` };
    case 'UnlockOffer': {
      const o = t.file.offers.find(x => x.id === r.offerId);
      return { thumb: { text: o && isBarter(o) ? 'BAR' : 'BUY', color: '#1ed760', dark: true, item: o?.itemTpl }, title: o ? `${isBarter(o) ? 'BARTER' : 'BUY'}: ${itemName(o.itemTpl)}` : 'Unlock: (Pick an Offer)', side: o ? (o.unlimited ? 'Unlimited' : `${o.stock} per Restock`) : '' };
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
  const known = id => validId(id) && (!hasItems || S.items.has(id) || !!modOf(id));

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
    for (const m of traderMods(t).values()) {
      const n = m.uses.length, list = [...new Set(m.uses.map(u => u.where))].slice(0, 4).join('; ') + (m.uses.length > 4 ? '…' : '');
      const target = { modPage: m.mod };
      if (m.state === 'on') add('info', f.name, `Uses ${n} item(s) from the mod ${m.mod} — players need ${m.mod} installed, or those offers / objectives are skipped.`, t, target);
      else if (m.state === 'off') add('warning', f.name, `Uses ${n} item(s) from ${m.mod}, which is switched OFF on the Mods page (${list}).`, t, target);
      else if (m.state === 'removed') add('warning', f.name, `Uses ${n} item(s) from ${m.mod}, which is no longer imported (${list}). Import it again on the Mods page, or replace them.`, t, target);
      else if (m.state === 'missing') add('warning', f.name, `Uses ${n} item(s) from ${m.mod}, but that mod's folder is gone (${list}). If the mod isn't installed, the game skips them.`, t, target);
      else add('warning', f.name, `${n} item id(s) used from ${m.mod} aren't in that mod anymore — it was updated or changed (${list}).`, t, target);
    }
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
        else if (it && !['Weapon', 'Grenade', 'Melee'].includes(it.c)) A('warning', `${what}: ${it.n} is not a weapon or grenade — kills can never count with it.`);
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
  S.picked = new Set();
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
  S.picked = new Set();
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
  S.picked = new Set();
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
// Picking several rows (Ctrl-click, Shift-click, drag in empty space)
// =====================================================================

function setPrimary(kind, obj) {
  if (kind === 'offer') S.offer = obj;
  else if (obj !== S.quest) selectQuest(obj);
}
function pickRow(kind, i, e) {
  const list = kind === 'offer' ? S.t.file.offers : S.t.file.quests;
  const obj = list[i];
  if (!obj) return;
  const primary = S[kind];
  if (e?.ctrlKey || e?.metaKey) {
    const set = S.picked.size > 1 ? S.picked : new Set(primary ? [primary] : []);
    if (set.has(obj) && set.size > 1) { set.delete(obj); if (obj === primary) setPrimary(kind, [...set].pop()); }
    else { set.add(obj); setPrimary(kind, obj); }
    S.picked = set;
    S.anchor = obj;
  } else if (e?.shiftKey && primary) {
    const rows = S.shownRows || list;
    const a = rows.indexOf(S.anchor && rows.includes(S.anchor) ? S.anchor : primary), b = rows.indexOf(obj);
    S.picked = new Set(rows.slice(Math.min(a, b), Math.max(a, b) + 1));
    setPrimary(kind, obj);
  } else {
    if (obj === primary && S.picked.size <= 1) return;
    S.picked = new Set();
    S.anchor = obj;
    setPrimary(kind, obj);
  }
  if (S.picked.size <= 1) S.picked = new Set();
  renderPage(false);
  renderDetails(true, true);
}
function pickMany(objs) {
  const kind = S.page === 'offers' ? 'offer' : 'quest';
  if (!objs.length) return;
  S.picked = objs.length > 1 ? new Set(objs) : new Set();
  setPrimary(kind, objs[objs.length - 1]);
  renderPage(false);
  renderDetails(true, true);
}

// =====================================================================
// Writing quest texts (✨ Generate)
// =====================================================================

const GEN = {
  hello: ['Listen up, mercenary.', 'Got a minute? I have work for you.', 'You look like someone who gets things done.', 'Word is you can handle yourself out there.',
    'I need a favour, and I pay well for favours.', 'Business is business, and right now business needs you.'],
  outro: ["Don't keep me waiting.", "Get it done and you won't regret it.", "Come back when it's finished.", "Good luck out there — you'll need it.", 'I trust you know what to do.'],
  done: ["Good work. I won't forget this.", 'Excellent — exactly what I needed.', "You actually did it. Here's what I promised.", 'Nice job, mercenary. Your payment is ready.',
    "That's how it's done. Pleasure doing business.", 'Well done. Come see me again soon — there will be more work.'],
};
const LOWER = new Set(['With', 'On', 'In', 'From', 'While', 'Wearing', 'Using', 'Ammo', 'Between', 'One', 'Raid', 'Headshots', 'Only', 'Hits', 'To', 'Found', 'As', 'Survive',
  'And', 'Extract', 'Times', 'Level', 'Reach', 'Hand', 'Pay', 'Kill', 'Find', 'Use', 'Enemies', 'Enemy', 'Scavs', 'Scav', 'Bosses', 'Boss', 'Within', 'Survived', 'Killed']);
const lowerWords = text => text.replace(/\b[A-Z][a-z]+\b/g, w => LOWER.has(w) ? w.toLowerCase() : w).replace(/\(found in raid\)/g, '(found in raid)');
const capFirst = text => text.charAt(0).toUpperCase() + text.slice(1);
function genPick(list, q) {
  S.genN = (S.genN || 0) + 1;
  let h = 0;
  for (const ch of q.id) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return list[(h + S.genN) % list.length];
}
function genDescription(t, q) {
  const w = ways(q);
  const line = o => q.conditions.filter(c => (c.option || 1) === o).map(c => lowerWords(shortCondition(c))).join(', and then ');
  let body;
  if (!q.conditions.length) body = "I'll tell you the details when you're ready.";
  else if (w.length === 1) body = `Here's the job: ${line(w[0])}.`;
  else body = `There's more than one way to handle this — pick whichever suits you:\n${w.map(o => `${wayLetter(o)}) ${capFirst(line(o))}.`).join('\n')}`;
  const hardcore = q.failOnDeath ? "\n\nAnd don't get yourself killed — if you die, go missing or leave a raid early, the job's off and you start over." : '';
  return `${genPick(GEN.hello, q)}\n\n${body}${hardcore}\n\n${genPick(GEN.outro, q)}`;
}
function genSuccess(t, q) {
  return genPick(GEN.done, q);
}

// =====================================================================
// Menus: ⚙ view options, right-click
// =====================================================================

function popoverAt(pop, x, y) {
  document.body.appendChild(pop);
  pop.style.left = clamp(x, 8, window.innerWidth - pop.offsetWidth - 8) + 'px';
  pop.style.top = clamp(y, 8, window.innerHeight - pop.offsetHeight - 8) + 'px';
  setTimeout(() => document.addEventListener('mousedown', outside), 0);
  function outside(e) { if (!pop.contains(e.target)) closePopover(); }
  closePopover.fn = outside;
}

function viewMenu(anchor) {
  closePopover();
  const pop = document.createElement('div');
  pop.className = 'popover';
  const draw = () => {
    const v = view();
    const opt = (key, text, on) => `<button data-v="${key}">${esc(text)}${on ? '<span class="check">✓</span>' : ''}</button>`;
    const list = S.page === 'offers' ? 'offers' : S.page === 'checks' ? 'checks' : 'quests';
    const cols = COLUMNS[list].slice(1).map(([k, name]) => opt(`${list}.${k}`, `${name} Column`, colShown(list, k))).join('');
    pop.innerHTML = `<div class="menu-title">${S.page === 'offers' ? 'Offers & Barters' : S.page === 'checks' ? 'Checks & Log' : 'Quests'} List</div>
      ${opt('hideIdx', '# Column', !v.hideIdx)}${cols}
      ${list === 'quests' ? `<hr>${opt('hideWaysTag', '"1 Way / 2 Ways" Tag', !v.hideWaysTag)}${opt('hideTags', 'Your Tags on Rows', !v.hideTags)}${opt('hideRewards', 'Rewards Line', !v.hideRewards)}` : ''}
      <hr>${opt('noItemPics', 'Item Pictures (From tarkov.dev)', !v.noItemPics)}${opt('grey', 'Calm Grey Colors (Boxes & Tags)', !!S.ui.grey)}`;
  };
  draw();
  pop.addEventListener('click', e => {
    const b = e.target.closest('[data-v]');
    if (!b) return;
    const key = b.dataset.v;
    const [list, col] = key.split('.');
    if (key === 'grey') S.ui.grey = !S.ui.grey;
    else if (col) view()[key] = colShown(list, col); // hide what's shown, show what's hidden
    else view()[key] = !view()[key];
    applyUi(); saveUi(); renderPage(false); renderDetails(false); draw();
  });
  const r = anchor.getBoundingClientRect();
  popoverAt(pop, r.right - 280, r.bottom + 6);
}

function contextMenu(x, y, items) {
  closePopover();
  const pop = document.createElement('div');
  pop.className = 'popover ctx';
  pop.innerHTML = items.map((it, i) => it === '-' ? '<hr>' : `<button data-i="${i}" class="${it.danger ? 'danger-item' : ''}" ${it.disabled ? 'disabled' : ''}>${esc(it.text)}${it.key ? `<span class="key">${esc(it.key)}</span>` : ''}</button>`).join('');
  pop.addEventListener('click', e => {
    const b = e.target.closest('[data-i]');
    if (!b) return;
    closePopover();
    items[Number(b.dataset.i)].run();
  });
  popoverAt(pop, x, y);
}

/** The offer or quest whose tags the details panel edits. */
const tagTarget = () => (S.page === 'offers' ? S.offer : S.page === 'quests' ? S.quest : null);
/** Every offer or quest of the current trader that can carry the tag (per page). */
const tagPool = () => (S.page === 'offers' ? S.t?.file.offers : S.t?.file.quests) || [];

const TAG_PALETTE = ['#ff7ab6', '#5cc8ff', '#f5cd46', '#a082ff', '#1ed760', '#ffa42b', '#ff6b6b', '#7ee0c3', '#b3b3b3', '#ffffff'];
function tagMenu(x, y, tag) {
  const pool = tagPool();
  const n = pool.filter(o => o.tags.includes(tag)).length;
  const kind = S.page === 'offers' ? 'Offer' : 'Quest';
  closePopover();
  const pop = document.createElement('div');
  pop.className = 'popover ctx';
  pop.innerHTML = `<div class="menu-title">Tag “${esc(tag)}” · ${n} ${kind}${n === 1 ? '' : 's'}</div>
    <button data-t="only">Show Only This</button>
    <button data-t="rename">Rename…</button>
    <div class="menu-title">Color</div>
    <div class="swatches">${TAG_PALETTE.map(c => `<button class="swatch ${tagColor(tag) === c ? 'on' : ''}" style="--c:${c}" data-t="color" data-c="${c}"></button>`).join('')}
      <button class="swatch auto" data-t="color" data-c="" title="Automatic">A</button></div>
    <hr><button data-t="remove" class="danger-item">Remove From All ${kind}s</button>`;
  pop.addEventListener('click', async e => {
    const b = e.target.closest('[data-t]');
    if (!b) return;
    const what = b.dataset.t;
    closePopover();
    if (what === 'only') ACT.tagFilter(tag);
    if (what === 'color') {
      const colors = (S.ui.tagColors ||= {});
      if (b.dataset.c) colors[tag] = b.dataset.c; else delete colors[tag];
      saveUi(); renderPage(false); renderDetails(false);
    }
    if (what === 'rename') {
      const name = (await promptBox('Rename Tag', `New name for “${tag}” (changes it on all ${n} ${kind.toLowerCase()}${n === 1 ? '' : 's'} of ${S.t.file.name})`, tag) || '').trim();
      if (!name || name === tag) return;
      for (const o of pool) if (o.tags.includes(tag)) o.tags = [...new Set(o.tags.map(x => x === tag ? name : x))];
      const colors = S.ui.tagColors || {};
      if (colors[tag]) { colors[name] = colors[tag]; delete colors[tag]; saveUi(); }
      const page = S.page === 'offers' ? 'offers' : 'quests';
      if (S.tagFilters[page] === tag) S.tagFilters[page] = name;
      changed(false);
      toast(`Renamed to “${name}”`);
    }
    if (what === 'remove') {
      for (const o of pool) o.tags = o.tags.filter(x => x !== tag);
      changed(false);
      toast(`Removed “${tag}” — Ctrl+Z to undo`);
    }
  });
  popoverAt(pop, x, y);
}

/** Gallery of the game's own quest pictures. */
function pickGameImage(current) {
  let shown = 120;
  const draw = m => {
    const list = S.gameQuestImages;
    m.querySelector('.gallery').innerHTML = list.slice(0, shown).map(g =>
      `<button class="gpic ${g.n === current ? 'on' : ''}" data-pick="${esc(g.n)}" title="${esc(g.n)}"><img src="${esc(g.u)}" alt="" loading="lazy"></button>`).join('') +
      (list.length > shown ? '<button class="outline more" data-more>Show More…</button>' : '');
  };
  return openModal(`<div class="dialog wide"><h2>Pick a Game Picture <span class="muted small">${S.gameQuestImages.length} pictures from SPT_Data\\images\\quests</span></h2>
    <div class="gallery scroll"></div>
    <div class="buttons"><button class="outline" data-m="0">Cancel</button><button class="primary" data-m="r">🎲 Random</button></div></div>`, m => {
    draw(m);
    m.querySelector('.gallery').onclick = e => {
      if (e.target.closest('[data-more]')) { shown += 240; draw(m); return; }
      const b = e.target.closest('[data-pick]');
      if (b) closeModal(b.dataset.pick);
    };
    m.querySelector('[data-m="0"]').onclick = () => closeModal(null);
    m.querySelector('[data-m="r"]').onclick = () => closeModal(randomGameImage(current));
  });
}

/** Deleted traders (the deleted_traders folder): put back or erase for good. */
async function deletedTradersBox() {
  let list = [];
  try { list = await host.call('listDeleted'); } catch (err) { return errorBox(err); }
  const rows = () => list.length ? list.map(d => `<div class="row"><div class="cell">${thumb({ text: '🗑', color: '#3a3a3a' })}
      <div class="text"><div class="title">${esc(d.name)}</div><div class="line2">Deleted ${esc(d.when)} · ${fmt(d.kb)} KB · ${esc(d.folder)}</div></div>
      <button class="outline" data-restore="${esc(d.folder)}">Restore</button><button class="danger" data-purge="${esc(d.folder)}">Delete Forever</button></div></div>`).join('')
    : '<div class="empty">The trash is empty.</div>';
  await openModal(`<div class="dialog"><h2>Deleted Traders</h2>
    ${ui.hint('Removed traders are moved here first (…\\CustomTraders\\deleted_traders). <b>Restore</b> puts one back; <b>Delete Forever</b> erases the folder and can’t be undone.')}
    <div class="picker-list deleted">${rows()}</div>
    <div class="buttons"><button class="danger" data-m="all" ${list.length ? '' : 'disabled'} style="margin-right:auto">Empty Trash</button><button class="primary" data-m="0">Close</button></div></div>`, m => {
    const redraw = () => {
      m.querySelector('.deleted').innerHTML = rows();
      m.querySelector('[data-m="all"]').disabled = !list.length;
      S.deletedCount = list.length; renderTraders();
    };
    m.querySelector('[data-m="0"]').onclick = () => closeModal(null);
    m.querySelector('[data-m="all"]').onclick = async () => {
      const b = m.querySelector('[data-m="all"]');
      if (b.dataset.sure !== '1') { b.dataset.sure = '1'; b.textContent = `Really Erase All ${list.length}?`; return; }
      list = await host.call('purgeDeleted', { name: '*' }).catch(errorBox) || [];
      redraw();
      toast('Trash emptied');
    };
    m.querySelector('.deleted').onclick = async e => {
      const purge = e.target.closest('[data-purge]'), restore = e.target.closest('[data-restore]');
      if (purge) {
        if (purge.dataset.sure !== '1') { purge.dataset.sure = '1'; purge.textContent = 'Click Again to Erase'; return; }
        list = await host.call('purgeDeleted', { name: purge.dataset.purge }).catch(errorBox) || list;
        redraw();
      }
      if (restore) {
        if (S.traders.some(t => t.dirty) && !confirm('Restoring reloads the traders — unsaved changes are lost. Continue?')) return;
        try {
          const snap = await host.call('restoreDeleted', { name: restore.dataset.restore });
          closeModal(null);
          applySnapshot(snap);
          toast('Trader restored');
        } catch (err) { errorBox(err); }
      }
    };
  });
}

document.addEventListener('contextmenu', e => {
  if (/INPUT|TEXTAREA/.test(e.target.tagName)) return; // keep copy / paste
  e.preventDefault();
  const tagEl = e.target.closest('[data-tag]');
  if (tagEl && S.t) return tagMenu(e.clientX, e.clientY, tagEl.dataset.tag);
  const tr = e.target.closest('[data-trader]');
  if (tr && tr.dataset.trader !== '') {
    const i = tr.dataset.trader, t = S.traders[Number(i)];
    if (!t) return;
    return contextMenu(e.clientX, e.clientY, [
      { text: 'Open', run: () => ACT.selTrader(i) },
      { text: 'Duplicate', run: () => ACT.duplicateTrader(i) },
      { text: t.file.enabled ? 'Switch Off' : 'Switch On', run: () => ACT.toggleTraderOn(i) },
      { text: 'Open Folder', run: () => host.call('openFolder', { folder: t.folder }) },
      '-',
      { text: 'Delete', key: 'Del', danger: true, run: () => ACT.deleteTrader(i) },
    ]);
  }
  const row = e.target.closest('#page .row[data-row]');
  if (row && (S.page === 'offers' || S.page === 'quests')) {
    const kind = S.page === 'offers' ? 'offer' : 'quest';
    const obj = (kind === 'offer' ? S.t.file.offers : S.t.file.quests)[Number(row.dataset.row)];
    if (!isPicked(obj, S[kind])) pickRow(kind, Number(row.dataset.row), null);
    const n = picked(S[kind]).length;
    const noun = kind === 'offer' ? (n > 1 ? `${n} Offers` : 'Offer') : (n > 1 ? `${n} Quests` : 'Quest');
    return contextMenu(e.clientX, e.clientY, [
      { text: `Duplicate ${noun}`, run: () => (kind === 'offer' ? ACT.dupOffer() : ACT.dupQuest()) },
      { text: 'Move Up', disabled: n > 1, run: () => (kind === 'offer' ? ACT.moveOffer(-1) : ACT.moveQuest(-1)) },
      { text: 'Move Down', disabled: n > 1, run: () => (kind === 'offer' ? ACT.moveOffer(1) : ACT.moveQuest(1)) },
      { text: 'Select All', key: 'Ctrl+A', run: () => pickMany(S.shownRows || []) },
      '-',
      { text: `Remove ${noun}`, key: 'Del', danger: true, run: () => (kind === 'offer' ? ACT.removeOffer() : ACT.removeQuest()) },
    ]);
  }
});

// close the trader list when clicking elsewhere
document.addEventListener('mousedown', e => {
  if (S.tradersOpen && !e.target.closest('#traderMenu, #me, .popover, #modal')) { S.tradersOpen = false; renderTraders(); }
}, true);

// search box (outside the page, so typing never loses focus)
let searchTimer = 0;
$('#search').addEventListener('input', e => {
  const v = e.target.value;
  $('#searchBox').classList.toggle('has', !!v);
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => { S.search[S.page] = v; renderPage(false); }, 90);
});
$('#search').addEventListener('keydown', e => {
  if (e.key === 'Escape') { e.stopPropagation(); ACT.clearSearch(); e.target.blur(); }
  if (e.key === 'Enter') e.target.blur();
});

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
  if (el.dataset.modToggle) { ACT.modToggle(el.dataset.modToggle); return; }
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
  if (e.target.id === 'multiTagInput' && e.key === 'Enter') { ACT.multiTag(e.target.value); return; }
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'f' && $('#modal').hidden && S.page !== 'trader') { e.preventDefault(); $('#search').focus(); $('#search').select(); return; }
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
  if (e.key === 'Escape') {
    if (document.querySelector('.popover')) closePopover();
    else if (S.tradersOpen) { S.tradersOpen = false; renderTraders(); }
    else if (S.picked.size) ACT.clearPicked();
    return;
  }
  if (e.key === 'Delete') {
    e.preventDefault();
    if (document.querySelector('.popover')) return;
    if (S.tradersOpen) {
      const hot = document.querySelector('#traders .trader:hover');
      return ACT.deleteTrader(hot ? hot.dataset.trader : String(S.traders.indexOf(S.t)));
    }
    if (S.page === 'offers') ACT.removeOffer();
    else if (S.page === 'quests') ACT.removeQuest();
    return;
  }
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'a' && (S.page === 'offers' || S.page === 'quests')) {
    e.preventDefault();
    pickMany(S.shownRows || []);
    return;
  }
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    const d = e.key === 'ArrowDown' ? 1 : -1;
    const lists = { offers: [S.t?.file.offers, 'offer'], quests: [S.t?.file.quests, 'quest'] };
    const [list, key] = lists[S.page] || [];
    if (!list?.length) return;
    e.preventDefault();
    S.picked = new Set();
    const i = clamp(list.indexOf(S[key]) + d, 0, list.length - 1);
    if (key === 'quest') selectQuest(list[i]); else S.offer = list[i];
    renderPage(false);
    renderDetails(true, true);
    document.querySelector('#page .row.sel')?.scrollIntoView({ block: 'nearest' });
  }
});

// ---- column resizing, panel widths, drag-select
let drag = null;
document.addEventListener('mousedown', e => {
  if (e.button !== 0) return;
  const grip = e.target.closest('[data-grip]');
  if (grip) {
    const [list, key] = grip.dataset.grip.split('|');
    const i = shownColumns(list).slice(1).findIndex(c => c[0] === key);
    drag = { kind: 'col', list, key, x: e.clientX, w: colWidths(list)[i], el: grip };
    grip.classList.add('drag');
    document.body.classList.add('col-drag');
    e.preventDefault();
    return;
  }
  if (e.target.classList.contains('splitter')) {
    const left = e.target.id === 'splitLeft';
    drag = { kind: left ? 'left' : 'right', el: e.target, x: e.clientX, w: $(left ? '#left' : '#right').getBoundingClientRect().width };
    e.target.classList.add('drag');
    e.preventDefault();
    return;
  }
  // drag in empty space of a list = pick every row the box touches
  const page = $('#page');
  if ((S.page === 'offers' || S.page === 'quests') && S.t && e.target.closest('#page') &&
      !e.target.closest('.row, button, input, textarea, select, label, .list-head, .toolbar, .empty')) {
    const r = page.getBoundingClientRect();
    const base = e.ctrlKey || e.shiftKey ? picked(S.page === 'offers' ? S.offer : S.quest) : [];
    drag = { kind: 'marquee', x0: e.clientX, y0: e.clientY - r.top + page.scrollTop, base, moved: false, el: null, hits: [] };
    e.preventDefault();
  }
});
function marqueeMove(e) {
  const page = $('#page');
  const r = page.getBoundingClientRect();
  if (e.clientY > r.bottom - 24) page.scrollTop += 14;
  else if (e.clientY < r.top + 24) page.scrollTop -= 14;
  const y1 = clamp(e.clientY, r.top, r.bottom) - r.top + page.scrollTop;
  const top = Math.min(drag.y0, y1), bottom = Math.max(drag.y0, y1);
  const left = Math.min(drag.x0, e.clientX), right = Math.max(drag.x0, e.clientX);
  if (!drag.moved && bottom - top < 5 && right - left < 5) return;
  if (!drag.el) { drag.el = document.createElement('div'); drag.el.className = 'marquee'; document.body.appendChild(drag.el); document.body.classList.add('dragging'); }
  drag.moved = true;
  const visTop = Math.max(top - page.scrollTop + r.top, r.top), visBottom = Math.min(bottom - page.scrollTop + r.top, r.bottom);
  Object.assign(drag.el.style, { left: left + 'px', width: (right - left) + 'px', top: visTop + 'px', height: Math.max(0, visBottom - visTop) + 'px' });
  const list = S.page === 'offers' ? S.t.file.offers : S.t.file.quests;
  drag.hits = [];
  page.querySelectorAll('.row[data-row]').forEach(row => {
    const b = row.getBoundingClientRect();
    const rt = b.top - r.top + page.scrollTop, rb = b.bottom - r.top + page.scrollTop;
    const hit = rt < bottom && rb > top && b.left < right && b.right > left;
    const obj = list[Number(row.dataset.row)];
    if (hit) drag.hits.push(obj);
    row.classList.toggle('sel', hit || drag.base.includes(obj));
  });
}
document.addEventListener('mousemove', e => {
  if (!drag) return;
  if (drag.kind === 'marquee') return marqueeMove(e);
  if (drag.kind === 'col') {
    const w = clamp(drag.w + (drag.x - e.clientX), 50, 700); // dragging the left edge left = wider
    (S.ui.colw ||= {})[`${drag.list}.${drag.key}`] = w;
    applyColumns();
  } else if (drag.kind === 'left') {
    const w = clamp(drag.w + (e.clientX - drag.x), 220, 460);
    S.ui.left = w;
    document.documentElement.style.setProperty('--left', w + 'px');
  } else {
    const w = clamp(drag.w + (drag.x - e.clientX), 360, Math.max(400, window.innerWidth - 700));
    S.ui.right = w;
    document.documentElement.style.setProperty('--right', w + 'px');
  }
});
document.addEventListener('mouseup', () => {
  if (!drag) return;
  const d = drag;
  drag = null;
  if (d.kind === 'marquee') {
    d.el?.remove();
    document.body.classList.remove('dragging');
    if (!d.moved) { if (S.picked.size) ACT.clearPicked(); return; }
    const list = S.page === 'offers' ? S.t.file.offers : S.t.file.quests;
    const all = list.filter(o => d.hits.includes(o) || d.base.includes(o));
    if (all.length) pickMany(all); else renderPage(false);
    return;
  }
  d.el?.classList.remove('drag');
  document.body.classList.remove('col-drag');
  saveUi();
});
document.addEventListener('dblclick', e => {
  const grip = e.target.closest('[data-grip]');
  if (grip) { delete S.ui.colw?.[grip.dataset.grip.replace('|', '.')]; applyColumns(); saveUi(); return; }
  if (!e.target.classList.contains('splitter')) return;
  if (e.target.id === 'splitLeft') { delete S.ui.left; document.documentElement.style.setProperty('--left', '250px'); }
  else { delete S.ui.right; document.documentElement.style.setProperty('--right', '560px'); }
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

/** Mods page: ask the host, then refresh items + checks (traders and unsaved edits stay). */
async function modCall(method, args, message) {
  try {
    const r = await host.call(method, args);
    if (!r) return; // dialog cancelled
    applyItems(r);
    if (r.added?.length) S.modSel = r.added[0];
    runChecks();
    renderAll(false);
    toast(message(r));
  } catch (err) { errorBox(err); }
}

const ACT = {
  page: arg => showPage(arg),
  selTrader: arg => {
    const t = S.traders[Number(arg)];
    S.tradersOpen = false;
    if (t && t !== S.t) selectTrader(t); else renderTraders();
  },
  toggleTraders() {
    if (!S.traders.length) return ACT.newTrader();
    S.tradersOpen = !S.tradersOpen;
    renderTraders();
    if (S.tradersOpen) document.querySelector('#traders .trader.sel')?.scrollIntoView({ block: 'nearest' });
  },
  selOffer: (arg, el, e) => pickRow('offer', Number(arg), e),
  selQuest: (arg, el, e) => pickRow('quest', Number(arg), e),
  clearPicked() { S.picked = new Set(); renderPage(false); renderDetails(true, true); },
  multiTag(arg) {
    const list = [...S.picked];
    if (arg.startsWith('-')) { const tag = arg.slice(1); list.forEach(q => { q.tags = q.tags.filter(x => x !== tag); }); }
    else { const tag = arg.trim(); if (!tag) return; list.forEach(q => { if (!q.tags.includes(tag)) q.tags.push(tag); }); }
    changed(false);
  },
  usePrice(kind) {
    const o = S.offer; if (!o) return;
    const cur = traderMoney();
    const v = offerValue(o, kind); if (!v) return;
    const line = o.cost.find(c => isMoney(c.itemTpl));
    const target = line ? line.itemTpl : cur;
    const count = toCurrency(v, target);
    if (line) line.count = count; else o.cost.unshift({ itemTpl: target, count });
    changed(false);
    toast(`Price set to ${fmt(count)} ${MONEY_SYMBOL[target]}`);
  },
  genText(field) {
    const q = S.quest; if (!q) return;
    q[field] = field === 'description' ? genDescription(S.t, q) : genSuccess(S.t, q);
    changed(false);
  },
  clearSearch() { S.search[S.page] = ''; $('#search').value = ''; renderHeader(); renderPage(false); },
  viewMenu: (arg, el) => viewMenu(el),
  selMod: arg => { S.modSel = S.shownMods?.[Number(arg)]?.name || null; renderPage(false); renderDetails(true, true); },
  goUse(arg) {
    const u = S.modUses?.[Number(arg)]; if (!u) return;
    if (u.trader !== S.t) selectTrader(u.trader, false);
    if (S.t.file.offers.includes(u.target)) { S.offer = u.target; S.page = 'offers'; }
    else { selectQuest(u.target); S.page = 'quests'; }
    S.picked = new Set();
    renderAll(true);
  },
  modsScanAll: () => modCall('modsScanAll', {}, r => r.added?.length ? `Imported ${r.added.length} mod(s): ${r.added.join(', ')}` : 'No new mods with items found'),
  modAddFolder: () => modCall('modAddFolder', {}, r => `Imported ${r.added?.[0] || 'the mod'}`),
  modRescan: () => modCall('modRescan', {}, () => 'Rescanned'),
  modToggle(name) {
    const m = S.mods.find(x => x.name === name); if (!m) return;
    modCall('modSet', { name, enabled: !m.enabled }, () => `${name} switched ${m.enabled ? 'OFF' : 'ON'}`);
  },
  async modRemove(name) {
    const e = modList().find(x => x.name === name);
    if (e?.uses.length && !await confirmBox('Remove Import', `Your traders use ${e.uses.length} item(s) from ${name}. Remove the import anyway? (The checks will list them as “not imported”.)`, 'Remove')) return;
    modCall('modRemove', { name }, () => `Removed ${name}`);
  },
  modForget: name => modCall('modForget', { name }, () => `Forgot ${name}'s ids`),
  selCond: arg => { S.cond = S.quest.conditions[Number(arg)]; renderDetails(false); },
  selReward: arg => { S.reward = S.quest.rewards[Number(arg)]; renderDetails(false); },
  selCheck: arg => { S.check = S.shownChecks?.[Number(arg)] || null; renderPage(false); renderDetails(false, true); },
  checkFilter: arg => { S.checkFilter = arg || null; renderPage(false); },
  tagFilter: arg => { S.tagFilters[S.page === 'offers' ? 'offers' : 'quests'] = arg || null; renderPage(false); },
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
    const o = tagTarget();
    if (!tag || !o || o.tags.includes(tag)) return;
    o.tags.push(tag);
    changed(false);
    setTimeout(() => $('#tagInput')?.focus(), 0);
  },
  removeTag(tag) { const o = tagTarget(); if (!o) return; o.tags = o.tags.filter(x => x !== tag); changed(false); },
  async pickGameImage() {
    const name = await pickGameImage(S.quest.gameImage);
    if (name) { S.quest.gameImage = name; delete S.quest.image; changed(false); }
  },
  randomGameImage() { const n = randomGameImage(S.quest.gameImage); if (n) { S.quest.gameImage = n; delete S.quest.image; changed(false); } },
  async deletedTraders() { S.tradersOpen = false; renderTraders(); await deletedTradersBox(); },
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
    const tpl = await pickItem('all');
    if (!tpl) return;
    const o = fillOffer({ itemTpl: tpl });
    o.cost = defaultCost(o);
    const list = S.t.file.offers;
    list.splice(S.offer ? list.indexOf(S.offer) + 1 : list.length, 0, o);
    S.offer = o;
    changed(true);
  },
  dupOffer() {
    const list = picked(S.offer); if (!list.length) return;
    const offers = S.t.file.offers;
    const copies = list.map(x => { const o = clone(x); o.id = newId(); delete o.unlockedByQuestId; return o; });
    offers.splice(Math.max(...list.map(x => offers.indexOf(x))) + 1, 0, ...copies);
    S.offer = copies[copies.length - 1];
    S.picked = copies.length > 1 ? new Set(copies) : new Set();
    changed(true);
    if (copies.length > 1) toast(`Duplicated ${copies.length} offers`);
  },
  async removeOffer() {
    const list = picked(S.offer); if (!list.length) return;
    const used = list.flatMap(o => questsUnlocking(S.t, o));
    if (used.length && !await confirmBox('Remove Offer', `${[...new Set(used.map(u => u.q.name))].join(', ')} unlock(s) ${list.length > 1 ? 'some of these offers' : 'this offer'}. Remove anyway? Those "Unlock Offer" rewards are removed too.`, 'Remove')) return;
    const ids = new Set(list.map(o => o.id));
    for (const q of S.t.file.quests) q.rewards = q.rewards.filter(r => !(r.type === 'UnlockOffer' && ids.has(r.offerId)));
    const offers = S.t.file.offers, first = Math.min(...list.map(o => offers.indexOf(o)));
    S.t.file.offers = offers.filter(o => !list.includes(o));
    S.offer = S.t.file.offers[Math.min(first, S.t.file.offers.length - 1)] || null;
    S.picked = new Set();
    changed(true);
    toast(`Removed ${list.length} offer${list.length === 1 ? '' : 's'} — Ctrl+Z to undo`);
  },
  moveOffer: d => { if (S.offer && move(S.t.file.offers, S.offer, d)) changed(false); },
  async addCost(tpl) {
    if (!S.offer) return;
    if (!tpl) { tpl = await pickItem('all'); if (!tpl) return; }
    const existing = S.offer.cost.find(c => c.itemTpl === tpl);
    if (!existing) {
      // money: whatever the price is still short of what the item is worth
      const missing = offerValue(S.offer, 'p') - costValue(S.offer.cost);
      const count = !isMoney(tpl) ? 1 : missing > 0 ? toCurrency(missing, tpl) : (tpl === CUR.RUB ? 50000 : 500);
      S.offer.cost.push({ itemTpl: tpl, count });
    }
    changed(false);
  },
  goQuest: id => { const q = S.t.file.quests.find(x => x.id === id); if (!q) return; selectQuest(q); S.page = 'quests'; renderAll(true); },
  goOffer: id => { const o = S.t.file.offers.find(x => x.id === id); if (!o) return; S.offer = o; S.page = 'offers'; renderAll(true); },

  // ---- quests
  addQuest() {
    const q = fillQuest({ name: 'New Quest' });
    const pic = randomGameImage();
    if (pic) q.gameImage = pic;
    const list = S.t.file.quests;
    list.splice(S.quest ? list.indexOf(S.quest) + 1 : list.length, 0, q);
    selectQuest(q);
    changed(true);
  },
  dupQuest() {
    const list = picked(S.quest); if (!list.length) return;
    const quests = S.t.file.quests;
    const copies = list.map(x => {
      const q = clone(x); q.id = newId(); q.name += ' (Copy)'; delete q.image; fillQuest(q);
      q.conditions.forEach(c => c.id = newId()); q.rewards.forEach(r => r.id = newId());
      return q;
    });
    quests.splice(Math.max(...list.map(x => quests.indexOf(x))) + 1, 0, ...copies);
    selectQuest(copies[copies.length - 1]);
    S.picked = copies.length > 1 ? new Set(copies) : new Set();
    changed(true);
    if (copies.length > 1) toast(`Duplicated ${copies.length} quests`);
  },
  async removeQuest() {
    const list = picked(S.quest); if (!list.length) return;
    const ids = new Set(list.map(q => q.id));
    const users = allQuests().filter(x => !ids.has(x.q.id) && x.q.prerequisiteQuestIds.some(id => ids.has(id)));
    if (users.length && !await confirmBox('Remove Quest', `${users.map(u => u.q.name).join(', ')} require(s) ${list.length > 1 ? 'these quests' : `"${list[0].name}"`} and would never unlock (the checks will show them). Remove anyway?`, 'Remove')) return;
    const quests = S.t.file.quests, first = Math.min(...list.map(q => quests.indexOf(q)));
    S.t.file.quests = quests.filter(q => !list.includes(q));
    selectQuest(S.t.file.quests[Math.min(first, S.t.file.quests.length - 1)] || null);
    S.picked = new Set();
    changed(true);
    toast(`Removed ${list.length} quest${list.length === 1 ? '' : 's'} — Ctrl+Z to undo`);
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
      S.t.images[r.file] = r.url; S.quest.image = r.file; delete S.quest.gameImage;
      changed(false); toast('Quest image saved');
    } catch (err) { errorBox(err); }
  },
  removeQuestImage() { delete S.quest.image; delete S.quest.gameImage; changed(false); },

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
    S.tradersOpen = false;
    const name = await promptBox('New Trader', 'Trader Name');
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
  async deleteTrader(arg) {
    const t = arg !== undefined && arg !== '' ? S.traders[Number(arg)] : S.t; if (!t) return;
    if (!await confirmBox('Remove Trader', `Remove "${t.file.name}"?\n\nIts folder is moved to "deleted_traders" (nothing is erased), so you can put it back later.`, 'Remove')) return;
    try {
      const target = await host.call('deleteTrader', { folder: t.folder });
      S.deletedCount++;
      S.traders.splice(S.traders.indexOf(t), 1);
      resetHistory();
      if (t === S.t) selectTrader(S.traders[0] || null, false);
      log('info', t.file.name, `Moved to ${target}`);
      runChecks(); renderAll(true);
      toast(`Removed ${t.file.name}`);
    } catch (err) { errorBox(err); }
  },
  async duplicateTrader(arg) {
    const src = arg !== undefined && arg !== '' ? S.traders[Number(arg)] : S.t; if (!src) return;
    const f = clone(src.file);
    const ids = new Map();
    const fresh = old => { if (!ids.has(old)) ids.set(old, newId()); return ids.get(old); };
    f.id = newId();
    f.name = `${f.name} Copy`; f.nickname = f.name;
    f.offers.forEach(o => { o.id = fresh(o.id); });
    f.quests.forEach(q => { q.id = fresh(q.id); q.conditions.forEach(c => { c.id = newId(); }); q.rewards.forEach(r => { r.id = newId(); }); });
    // links inside the trader follow the copies; links to other traders / game quests stay
    for (const q of f.quests) {
      q.prerequisiteQuestIds = q.prerequisiteQuestIds.map(id => ids.get(id) || id);
      for (const r of q.rewards) if (r.type === 'UnlockOffer') r.offerId = ids.get(r.offerId) || r.offerId;
    }
    if (f.unlockQuestId) f.unlockQuestId = ids.get(f.unlockQuestId) || f.unlockQuestId;
    try {
      const text = JSON.stringify(f, (k, v) => v === null ? undefined : v, 2);
      const t = normalize({ ...(await host.call('duplicateTrader', { folder: src.folder, name: f.name, text })), dirty: false });
      t.savedJson = JSON.stringify(t.file);
      S.traders.splice(S.traders.indexOf(src) + 1, 0, t);
      resetHistory();
      S.tradersOpen = false;
      selectTrader(t, false);
      runChecks(); renderAll(true);
      log('ok', t.file.name, `Duplicated ${src.file.name} (new ids for the trader, its offers and quests).`, t);
      toast(`Made ${t.file.name}`);
    } catch (err) { errorBox(err); }
  },
  toggleTraderOn(arg) {
    const t = S.traders[Number(arg)]; if (!t) return;
    t.file.enabled = !t.file.enabled;
    markDirty(t);
    renderAll(false);
    toast(`${t.file.name} switched ${t.file.enabled ? 'ON' : 'OFF'} — save to apply`);
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
      t.file.requiredMods = [...traderMods(t).keys()].sort(); // the server names them if an item is missing
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
    if (e.target?.modPage) { S.modSel = e.target.modPage; S.page = 'mods'; renderAll(true); return; }
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
  commitHistory(); // every button action is its own undo step (typing is grouped)
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
  if (u.right) root.setProperty('--right', clamp(u.right, 360, 1400) + 'px');
  if (u.left) root.setProperty('--left', clamp(u.left, 220, 460) + 'px');
  document.body.classList.toggle('grey', !!u.grey);
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

/** Item search. filter: all | weapons+ | a category (Weapon, Ammo, Gear, Meds…) | mods | mod:<name>.
 *  The last items you picked are listed first; dev / template items ("DO NOT USE"…) are hidden unless asked for. */
function pickItem(filter = 'all') {
  let cat = filter, query = '', showHidden = false;
  const all = [...S.items.values()].sort((a, b) => a.n.localeCompare(b.n));
  const inCat = it => cat === 'all' || (cat === 'weapons+' ? ['Weapon', 'Grenade', 'Melee'].includes(it.c)
    : cat === 'mods' ? !!it.m : cat.startsWith('mod:') ? it.m === cat.slice(4) : it.c === cat);
  const match = it => inCat(it) && (showHidden || !it.x || query === it.i) &&
    (!query || it.n.toLowerCase().includes(query) || (it.s || '').toLowerCase().includes(query) || it.i.startsWith(query) || (it.m || '').toLowerCase().includes(query));
  const hiddenCount = all.filter(it => it.x).length;
  const mods = [...new Set(all.filter(it => it.m).map(it => it.m))].sort();
  const rowHtml = it => `<div class="row" data-pick="${it.i}"><div class="cell">
      ${thumb({ text: initials(it.s || it.n), color: '#3a3a3a', item: it.c === 'Money' ? null : it.i, wide: true })}<div class="text"><div class="title">${esc(it.n)}</div><div class="line2">${esc(it.s)}${it.m ? ` · <span style="color:var(--accent)">${esc(it.m)}</span>` : ''}</div></div></div>
      <div class="col" ${it.m ? 'style="color:var(--accent)"' : ''}>${esc(it.m ? 'MODDED' : it.c === 'Ammo' && it.k ? caliberName(it.k) : catName(it.c))}</div></div>`;
  const draw = m => {
    const found = all.filter(match);
    const recent = !query ? (S.ui.recentItems || []).map(id => S.items.get(id)).filter(it => it && inCat(it)).slice(0, 5) : [];
    m.querySelector('.picker-list').innerHTML =
      (recent.length ? `<div class="pick-group">Recently Used</div>${recent.map(rowHtml).join('')}<div class="pick-group">${esc(cat === 'all' ? 'All Items' : 'All ' + (CAT_NAME[cat] || (cat === 'mods' ? 'Modded' : cat.startsWith('mod:') ? cat.slice(4) : '')))} (${fmt(found.length)})</div>` : '') +
      (found.slice(0, 400).map(rowHtml).join('') + (found.length > 400 ? `<div class="empty" style="padding:14px">Showing 400 of ${fmt(found.length)} — type to narrow it down</div>` : '') ||
      `<div class="empty">${S.items.size ? 'Nothing found' : 'Item database not loaded — paste an item id below'}</div>`);
    m.querySelectorAll('[data-cat]').forEach(c => c.classList.toggle('on', c.dataset.cat === cat));
    m.querySelector('[data-hidden]').classList.toggle('on', showHidden);
  };
  const done = id => {
    if (id) { const r = (S.ui.recentItems || []).filter(x => x !== id); r.unshift(id); S.ui.recentItems = r.slice(0, 12); saveUi(); }
    closeModal(id);
  };
  return openModal(`<div class="dialog"><h2>Pick an Item</h2>
    <div class="picker-search">🔍<input type="text" id="pickQuery" placeholder="What are you looking for? (name, short name, id or mod)"></div>
    <div class="chips" style="margin-top:10px">${CATEGORY_FILTERS.map(([v, t]) => `<button class="chip" data-cat="${v}">${t}</button>`).join('')}
      ${mods.length ? `<button class="chip" data-cat="mods" style="--c:var(--accent)">Modded</button>${mods.map(n => `<button class="chip" data-cat="mod:${esc(n)}" style="--c:var(--accent)">${esc(n)}</button>`).join('')}` : ''}
      <button class="chip" data-hidden title="Dev / template items that players can't really hold">Show Hidden (${fmt(hiddenCount)})</button></div>
    <div class="picker-list"></div>
    <div class="buttons"><input type="text" id="pickId" placeholder="or paste an item id" style="max-width:260px;margin-right:auto">
      <button class="outline" data-m="0">Cancel</button><button class="primary" data-m="1">Use Pasted Id</button></div></div>`, m => {
    const q = m.querySelector('#pickQuery');
    q.focus();
    let timer = 0;
    q.oninput = () => { clearTimeout(timer); timer = setTimeout(() => { query = q.value.trim().toLowerCase(); draw(m); }, 80); };
    m.querySelectorAll('[data-cat]').forEach(c => c.onclick = () => { cat = c.dataset.cat; draw(m); });
    m.querySelector('[data-hidden]').onclick = () => { showHidden = !showHidden; draw(m); };
    m.querySelector('.picker-list').onclick = e => { const r = e.target.closest('[data-pick]'); if (r) done(r.dataset.pick); };
    m.querySelector('[data-m="0"]').onclick = () => closeModal(null);
    m.querySelector('[data-m="1"]').onclick = () => {
      const id = m.querySelector('#pickId').value.trim().toLowerCase();
      if (validId(id)) done(id); else toast('An item id is 24 characters of 0-9 / a-f');
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
