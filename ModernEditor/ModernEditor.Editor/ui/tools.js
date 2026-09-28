'use strict';
/* Modern Editor — tools across the trader pages (app.js) and the item pages (items.js):
   · dynamic quest levels: a quest can follow an item's Level Gate level (+ offset); kept in sync here and applied
     again by the server mod at every start (Level Gate's file is only read there)
   · "Used By": where an item is sold, bartered, asked for or given, on every trader
   · ⚡ Barters / Quests: offers and unlock quests for many items at once, by their level
   · Go To… (Ctrl+K): any page, trader, offer, quest or item
   · the start page's tools, the move-over from the old mods, quests in the Progression view */

// =====================================================================
// Dynamic quest levels
// =====================================================================

/** The level Level Gate gives an item right now (unsaved edits included); no limit = 1, like the server. */
const itemLevelNow = tpl => LG.levelOf(tpl) ?? 1;
/** A dynamic quest's level: its item's level + offset (1–79). */
const dynamicLevel = q => clamp(itemLevelNow(q.levelFromItem) + (Number(q.levelOffset) || 0), 1, 79);

/** Levels changed in Level Limits (or loaded): dynamic quests follow, the trader pages' Level column too. */
function lgLevelsChanged(initial = false) {
  if (!S.lgStarted && !initial) return;
  if (!LG.loaded()) return;
  let moved = 0;
  for (const t of S.traders) {
    let dirty = false;
    for (const q of t.file.quests) {
      if (!validId(q.levelFromItem)) continue;
      const l = dynamicLevel(q);
      if (q.minLevel !== l) { q.minLevel = l; dirty = true; moved++; }
    }
    // on load the file just catches up with Level Gate (the server does the same at start) — not an unsaved change
    if (dirty && !initial) markDirty(t);
  }
  if (S.addons?.levelgate) S.addons.levelgate.items = Object.fromEntries(LG.levels());
  if (moved && !initial) { runChecks(); if (!lgActive()) { renderPage(false); renderDetails(false); } renderTraders(); renderBottom(); }
  if (moved && !initial && !lgLevelsChanged.told) { lgLevelsChanged.told = true; toast(`${moved} quest level${moved === 1 ? '' : 's'} followed ${moved === 1 ? 'its item' : 'their items'} — save to keep it`); setTimeout(() => { lgLevelsChanged.told = false; }, 4000); }
}

/** The item a quest most likely belongs to: what it unlocks, else what it gives, else what it asks for. */
function questItem(t, q) {
  for (const r of q.rewards) if (r.type === 'UnlockOffer') { const o = t.file.offers.find(x => x.id === r.offerId); if (o?.itemTpl) return o.itemTpl; }
  for (const r of q.rewards) if (r.type === 'Item' && r.itemTpl && !isMoney(r.itemTpl)) return r.itemTpl;
  for (const c of q.conditions) for (const id of c.itemTpls || []) if (!isMoney(id)) return id;
  return '';
}

/** What each quest looked tied to (questItem) the last time it was checked, by quest id. */
const questItemSeen = new Map();
/** Remembers every quest's item without changing anything (on load / reload / undo). */
function seedQuestItems() { questItemSeen.clear(); for (const t of S.traders) for (const q of t.file.quests) questItemSeen.set(q.id, questItem(t, q)); }
/** The offer a dynamic quest unlocks got another item (or the quest now unlocks another offer): if the quest followed the old
 *  item, it follows the new one. A quest set to follow some other item on purpose is left alone. */
function followQuestItems(t) {
  let moved = 0;
  for (const q of t?.file.quests || []) {
    const now = questItem(t, q), was = questItemSeen.get(q.id);
    questItemSeen.set(q.id, now);
    if (was === undefined || was === now || !validId(now) || !validId(q.levelFromItem) || q.levelFromItem !== was) continue;
    q.levelFromItem = now;
    if (LG.loaded()) q.minLevel = dynamicLevel(q);
    moved++;
  }
  return moved;
}

/** Quest editor: "Unlocks at Level" — a number, or following an item. */
function questLevelFields(t, q) {
  const on = validId(q.levelFromItem) || q.levelFromItem === '';
  const toggle = ui.toggle('Level Follows an Item (Level Gate)', () => on, v => {
    if (v) { q.levelFromItem = questItem(t, q); q.levelOffset = q.levelOffset || 0; if (validId(q.levelFromItem) && LG.loaded()) q.minLevel = dynamicLevel(q); }
    else { delete q.levelFromItem; delete q.levelOffset; }
  }, { label: 'Dynamic Level', refresh: 'page' });
  if (!on) return ui.num('Unlocks at Level', () => q.minLevel, v => { q.minLevel = v; }, { min: 1, max: 79 }) + toggle +
    ui.hint('Switch on to keep this quest at the level of the item it unlocks: move the item in Level Limits and the quest moves with it (also when changed in game — the server reads Level Gate\'s file at every start).');
  const item = q.levelFromItem, il = LG.levelOf(item), loaded = LG.loaded();
  return `<div class="field"><label>Unlocks at Level</label><div class="dyn-level"><b>${q.minLevel}</b><span class="muted small">follows ${esc(item ? shortName(item) : 'an item')}${item ? ` (Level Gate level ${il ?? '— no limit (1)'})` : ''}${Number(q.levelOffset) ? ` ${q.levelOffset > 0 ? '+' : '−'} ${Math.abs(q.levelOffset)}` : ''}</span></div></div>
    ${toggle}
    ${ui.item('Item', () => q.levelFromItem, v => { q.levelFromItem = v; if (LG.loaded()) q.minLevel = dynamicLevel(q); })}
    ${ui.num('Offset (Levels)', () => Number(q.levelOffset) || 0, v => { q.levelOffset = v; if (LG.loaded() && validId(q.levelFromItem)) q.minLevel = dynamicLevel(q); }, { min: -78, max: 78, refresh: 'page' })}
    ${ui.hint(!loaded ? '⚠ Level Gate\'s file isn\'t loaded (Level Limits page) — the level above is the one last saved.'
      : !validId(item) ? '✖ Pick the item whose level this quest follows.'
      : `The quest unlocks at the item's level${Number(q.levelOffset) ? ` ${q.levelOffset > 0 ? '+' : '−'} ${Math.abs(q.levelOffset)}` : ''}. Change the item's level in Level Limits and this follows; the server applies it again at every start.`)}
    ${(() => { const tied = questItem(t, q); return validId(tied) && tied !== item ? `<div class="warn-line">It unlocks ${esc(shortName(tied))}, but its level follows ${esc(item ? shortName(item) : 'nothing')}.</div>
      <div class="toolbar" style="padding:0 0 6px"><button class="primary" data-act="followTied">Follow ${esc(shortName(tied))}</button></div>` : ''; })()}
    ${validId(item) ? `<div class="toolbar" style="padding:0 0 6px"><button class="outline" data-act="goLevel" data-arg="${esc(item)}">Change ${esc(shortName(item))}'s Level ›</button></div>` : ''}`;
}

/** Level Limits: open an item there (from Go To…, Add-ons, a dynamic quest). */
function goItemLevels(id) {
  const st = LG.state;
  st.cat = 'all'; st.filter = 'all'; st.band = null; st.search = ''; $('#search').value = '';
  st.sel = id; st.picked = new Set(); st.limit = 100000; st.page = 'items';
  st.keepSel = true;
  hideHome();
  shellShowPage('levels');
  requestAnimationFrame(() => document.querySelector(`#page .row[data-row="${id}"]`)?.scrollIntoView({ block: 'center' }));
}

// =====================================================================
// Used By: where every item appears on your traders
// =====================================================================

let usedCache = null, usedStamp = '';
function usedIndex() {
  const stamp = S.traders.map(t => `${t.folder}:${t.edited || 0}:${t.file.offers.length}:${t.file.quests.length}`).join('|');
  if (usedCache && stamp === usedStamp) return usedCache;
  const m = new Map();
  const add = (id, u) => { if (!id || isMoney(id)) return; (m.get(id) || m.set(id, []).get(id)).push(u); };
  for (const t of S.traders) {
    for (const o of t.file.offers) {
      add(o.itemTpl, { t, o, kind: 'sold', text: `Sold · ${costText(o)}${o.loyaltyLevel > 1 ? ` · LL${o.loyaltyLevel}` : ''}` });
      for (const c of o.cost) add(c.itemTpl, { t, o, kind: 'price', text: `Barter price of ${itemName(o.itemTpl)} (${c.count}×)` });
    }
    for (const q of t.file.quests) {
      const seen = new Set();
      for (const c of q.conditions) for (const k of ['itemTpls', 'weaponTpls', 'wearingTpls']) for (const id of c[k] || []) if (!seen.has(id + k)) { seen.add(id + k); add(id, { t, q, kind: 'objective', text: `Objective of ${q.name}: ${shortCondition(c)}` }); }
      for (const r of q.rewards) if (r.type === 'Item') add(r.itemTpl, { t, q, kind: 'reward', text: `Reward of ${q.name} (${r.count}×)` });
      if (validId(q.levelFromItem)) add(q.levelFromItem, { t, q, kind: 'dynamic', text: `${q.name} follows its level${Number(q.levelOffset) ? ` (${q.levelOffset > 0 ? '+' : ''}${q.levelOffset})` : ''}` });
    }
  }
  usedCache = m; usedStamp = stamp;
  return m;
}

/** Small badge on the Level Limits rows: SOLD / IN QUESTS. */
function usedBadge(id) {
  const uses = usedIndex().get(id);
  if (!uses) return '';
  const sold = uses.filter(u => u.kind === 'sold').length, quests = new Set(uses.filter(u => u.q).map(u => u.q)).size;
  const tip = uses.slice(0, 8).map(u => `${u.t.file.name}: ${u.text}`).join('\n') + (uses.length > 8 ? `\n… ${uses.length - 8} more` : '');
  return `<span class="badge used" style="--c:var(--blue)" title="${esc(tip)}">${sold ? `SOLD${sold > 1 ? ' ×' + sold : ''}` : ''}${sold && quests ? ' · ' : ''}${quests ? `${quests} QUEST${quests > 1 ? 'S' : ''}` : ''}${!sold && !quests ? 'USED' : ''}</span>`;
}

/** Level Limits details: every place the item is used, click one to go there. */
let usedShown = [];
function usedByCard(id) {
  const uses = usedIndex().get(id) || [];
  usedShown = uses;
  const icon = { sold: ['🛒', 'var(--green)'], price: ['⇄', 'var(--pink)'], objective: ['◎', 'var(--orange)'], reward: ['★', 'var(--yellow)'], dynamic: ['⟲', 'var(--violet)'] };
  return `<div class="card used-card"><h3>Used By Your Traders <span class="muted small">${uses.length || ''}</span></h3>
    ${uses.length ? `<div class="mini">${uses.slice(0, 60).map((u, i) => `<div class="row" data-act="goUsed" data-arg="${i}"><div class="cell"><span class="use-ico" style="color:${icon[u.kind][1]}">${icon[u.kind][0]}</span>
      <div class="text"><div class="title">${esc(u.t.file.name)}${u.q ? ` › ${esc(u.q.name)}` : ''}</div><div class="line2">${esc(u.text)}${u.q ? ` · quest level ${u.q.minLevel}` : ''}</div></div></div></div>`).join('')}</div>`
      : '<div class="hint">None of your traders sell it or use it in a quest yet.</div>'}
    <div class="toolbar" style="padding:8px 0 0"><button class="primary" data-act="batch">⚡ Make a Barter / Quest for It…</button></div></div>`;
}

function goToUse(i) {
  const u = usedShown[Number(i)];
  if (!u) return;
  hideHome();
  selectTrader(u.t, false);
  if (u.o) { S.offer = u.o; S.page = 'offers'; } else { selectQuest(u.q); S.page = 'quests'; }
  S.ui.page = S.page; S.picked = new Set();
  document.body.classList.remove('prog-mode');
  renderAll(true);
}

// =====================================================================
// ⚡ Barters / Quests for many items at once
// =====================================================================

/** The loyalty level a player of this level has with the trader (its LL player levels). */
const llForLevel = (t, level) => Math.max(1, t.file.loyaltyLevels.filter(l => l.minLevel <= level).length);

/** A barter price worth about `value` ₽: 1–5 of one barter item, picked with some variety per item. */
function autoBarter(value, seedId) {
  const pool = [...S.items.values()].filter(it => it.g === 'Barter' && it.p > 0 && !it.x && !it.m);
  if (!pool.length || !value) return null;
  const scored = pool.map(it => { const n = clamp(Math.round(value / it.p), 1, 5); return { it, n, err: Math.abs(n * it.p - value) / value }; }).sort((a, b) => a.err - b.err).slice(0, 6);
  let h = 0; for (const ch of seedId) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  const pick = scored[h % scored.length];
  return [{ itemTpl: pick.it.i, count: pick.n }];
}

const BATCH_OBJECTIVES = [
  ['pay', 'Pay Money (Share of the Price)'], ['killPmc', 'Kill PMCs'], ['killScav', 'Kill Scavs'], ['extract', 'Survive and Extract'], ['handover', 'Hand Over the Barter Items (Found in Raid)'],
];

async function batchDialog(list) {
  list = list.filter(x => S.items.has(x.id) || S.modOff?.has(x.id));
  if (!list.length) return toast('Pick items that are in the item list');
  if (!S.modFolder) return toast('Pick your SPT folder first (Browse… at the top)');
  const o = { ...{ trader: S.t ? S.traders.indexOf(S.t) : -1, offers: true, price: 'money', mult: 1, llByLevel: true, stock: 0, quests: true, obj: 'pay', amount: 0.5,
    xpPerLevel: 250, dynamic: true, offset: 0, chain: false, tag: `Batch ${new Date().toLocaleDateString()}` }, ...(S.ui.batch || {}) };
  if (!S.traders[o.trader]) o.trader = S.traders.length ? 0 : -1;
  const sorted = [...list].sort((a, b) => a.level - b.level);
  const opt = (v, t, cur) => `<option value="${esc(v)}" ${String(cur) === String(v) ? 'selected' : ''}>${esc(t)}</option>`;
  const body = () => `<div class="dialog wide batch"><h2>⚡ Barters / Quests <span class="muted small">${list.length} item${list.length === 1 ? '' : 's'}, levels ${sorted[0].level}–${sorted[sorted.length - 1].level}</span></h2>
    <div class="batch-grid">
      <div class="batch-col">
        <div class="field"><label>Trader</label><select id="bTrader">${S.traders.map((t, i) => opt(i, t.file.name, o.trader)).join('')}${opt(-1, '＋ New Trader…', o.trader)}</select></div>
        <h3>Offers</h3>
        <label class="switch"><input type="checkbox" id="bOffers" ${o.offers ? 'checked' : ''}><span class="track"></span><span>Sell Each Item</span></label>
        <div class="field"><label>Price</label><select id="bPrice">${opt('money', 'Money (What Traders Pay × Multiplier)', o.price)}${opt('barter', 'Barter (Barter Items of That Value)', o.price)}${opt('both', 'Both (a Money Offer and a Barter)', o.price)}</select></div>
        <div class="field"><label>Price Multiplier</label><input type="number" id="bMult" step="0.1" min="0.1" value="${o.mult}"></div>
        <label class="switch"><input type="checkbox" id="bLL" ${o.llByLevel ? 'checked' : ''}><span class="track"></span><span>Loyalty Level by the Item's Level (the Trader's LL Levels)</span></label>
        <div class="field"><label>Stock per Restock</label><input type="number" id="bStock" min="0" value="${o.stock}" title="0 = unlimited"></div>
      </div>
      <div class="batch-col">
        <h3>Unlock Quests</h3>
        <label class="switch"><input type="checkbox" id="bQuests" ${o.quests ? 'checked' : ''}><span class="track"></span><span>A Quest per Item That Unlocks It${o.offers ? '' : ' (Gives It as a Reward)'}</span></label>
        <div class="field"><label>Objective</label><select id="bObj">${BATCH_OBJECTIVES.map(([v, t]) => opt(v, t, o.obj)).join('')}</select></div>
        <div class="field"><label id="bAmountLabel">${o.obj === 'pay' ? 'Share of the Price' : o.obj === 'handover' ? '× the Barter' : 'Base Count'}</label><input type="number" id="bAmount" step="0.1" min="0" value="${o.amount}"></div>
        <div class="field"><label>XP per Item Level</label><input type="number" id="bXp" step="50" min="0" value="${o.xpPerLevel}"></div>
        <label class="switch"><input type="checkbox" id="bDyn" ${o.dynamic ? 'checked' : ''}><span class="track"></span><span>Dynamic Level — the Quest Follows Its Item's Level</span></label>
        <div class="field"><label>Level Offset</label><input type="number" id="bOffset" min="-78" max="78" value="${o.offset}" title="−2 = two levels before the item unlocks"></div>
        <label class="switch"><input type="checkbox" id="bChain" ${o.chain ? 'checked' : ''}><span class="track"></span><span>Chain Them (Each Needs the One Before, in Level Order)</span></label>
      </div>
    </div>
    <div class="field"><label>Tag Everything</label><input type="text" id="bTag" value="${esc(o.tag)}" placeholder="e.g. Early Guns (to find / bulk edit them later)"></div>
    <div class="batch-preview mini">${sorted.slice(0, 40).map(x => { const it = S.items.get(x.id) || S.modOff?.get(x.id); return `<div class="row"><div class="cell">${thumb({ text: initials(it?.s || it?.n), item: x.id, color: '#3a3a3a' })}<div class="text"><div class="title">${esc(it?.n || x.id)}</div><div class="line2">Level ${x.level}${it?.p ? ` · traders pay ${fmt(it.p)} ₽` : ''}</div></div></div></div>`; }).join('')}${sorted.length > 40 ? `<div class="muted small" style="padding:6px">… and ${sorted.length - 40} more</div>` : ''}</div>
    <div class="buttons"><span class="muted small" id="bSay" style="margin-right:auto"></span><button class="outline" data-m="0">Cancel</button><button class="primary" data-m="1">Make Them</button></div></div>`;
  const read = m => {
    const v = id => m.querySelector(id);
    Object.assign(o, { trader: Number(v('#bTrader').value), offers: v('#bOffers').checked, price: v('#bPrice').value, mult: Number(v('#bMult').value) || 1,
      llByLevel: v('#bLL').checked, stock: Math.max(0, Number(v('#bStock').value) || 0), quests: v('#bQuests').checked, obj: v('#bObj').value,
      amount: Math.max(0, Number(v('#bAmount').value) || 0), xpPerLevel: Math.max(0, Number(v('#bXp').value) || 0), dynamic: v('#bDyn').checked,
      offset: clamp(Number(v('#bOffset').value) || 0, -78, 78), chain: v('#bChain').checked, tag: v('#bTag').value.trim() });
    const t = S.traders[o.trader];
    v('#bSay').textContent = `${o.offers ? list.length * (o.price === 'both' ? 2 : 1) : 0} offer(s) and ${o.quests ? list.length : 0} quest(s) for ${t ? t.file.name : 'a new trader'}`;
    v('#bAmountLabel').textContent = o.obj === 'pay' ? 'Share of the Price' : o.obj === 'handover' ? '× the Barter' : 'Base Count';
  };
  const ok = await openModal(body(), m => {
    const dlg = m.querySelector('.dialog'); // (#modal itself is reused by every dialog)
    dlg.addEventListener('input', () => read(m)); dlg.addEventListener('change', () => read(m));
    read(m);
    m.querySelector('[data-m="0"]').onclick = () => closeModal(false);
    m.querySelector('[data-m="1"]').onclick = () => { read(m); closeModal(true); };
  });
  if (!ok) return;
  S.ui.batch = { ...o, trader: undefined }; saveUi();
  if (!o.offers && !o.quests) return toast('Nothing to make — switch on offers and / or quests');
  let t = S.traders[o.trader];
  if (!t) {
    const name = await promptBox('New Trader', 'Name of the trader the offers / quests go to');
    if (!name) return;
    try {
      t = normalize({ ...(await host.call('newTrader', { name })), dirty: false });
      t.savedJson = JSON.stringify(t.file);
      S.traders.push(t);
      resetHistory();
    } catch (err) { return errorBox(err); }
  }
  const made = batchMake(t, sorted, o);
  S.t = t; S.picked = new Set();
  markDirty(t); commitHistory(); runChecks();
  renderTraders(); renderBottom();
  if (lgActive()) LG.renderPage(); else renderAll(false);
  const go = await confirmBox('Made Them', `${made.offers} offer(s) and ${made.quests} quest(s) added to ${t.file.name}${o.tag ? `, tagged “${o.tag}”` : ''}.\n\nNothing is saved yet — Save All writes them. Ctrl+Z on the Quests / Offers page takes them back.`, `Open ${made.quests ? 'the Quests' : 'the Offers'}`, 'Stay Here');
  if (go) { if (made.quests) selectQuest(made.firstQuest); else S.offer = made.firstOffer; showPage(made.quests ? 'quests' : 'offers'); }
}

/** Adds the offers / quests (pure data; the caller marks the trader changed). */
function batchMake(t, sorted, o) {
  const f = t.file, cur = traderMoney(t);
  let offers = 0, quests = 0, prev = null, firstQuest = null, firstOffer = null;
  for (const x of sorted) {
    const it = S.items.get(x.id) || S.modOff?.get(x.id);
    const name = it?.s && (it.n || '').length > 24 ? it.s : it?.n || x.id;
    const ll = o.llByLevel ? llForLevel(t, x.level) : 1;
    const made = [];
    if (o.offers) {
      const base = fillOffer({ itemTpl: x.id, loyaltyLevel: ll, unlimited: !o.stock, stock: o.stock || 1, tags: o.tag ? [o.tag] : [] });
      const money = defaultCost(base).map(c => ({ ...c, count: isMoney(c.itemTpl) ? Math.max(1, Math.round(c.count * o.mult / (c.itemTpl === CUR.RUB ? 10 : 1)) * (c.itemTpl === CUR.RUB ? 10 : 1)) : c.count }));
      if (o.price !== 'barter') { base.cost = money; f.offers.push(base); made.push(base); }
      if (o.price !== 'money') {
        const worth = (offerValue(base, 'p') || 0) * o.mult;
        const barter = autoBarter(worth, x.id);
        if (barter) { const b = fillOffer({ ...clone(base), id: newId(), cost: barter }); f.offers.push(b); made.push(b); }
        else if (o.price === 'barter') { base.cost = money; f.offers.push(base); made.push(base); }
      }
      offers += made.length;
      firstOffer ||= made[0];
    }
    if (o.quests) {
      const q = fillQuest({ name: `${o.offers ? 'Unlock' : 'Earn'}: ${name}`, minLevel: clamp(x.level + o.offset, 1, 79), tags: o.tag ? [o.tag] : [] });
      if (o.dynamic) { q.levelFromItem = x.id; q.levelOffset = o.offset; }
      const price = made[0] ? costValue(made[0].cost.filter(c => isMoney(c.itemTpl))) || offerValue(made[0], 'p') : (it?.p || 10000);
      const count = Math.max(1, Math.round((o.amount || 1) + x.level / 10));
      const c = fillCond({ option: 1 });
      if (o.obj === 'pay') { c.type = 'HandoverItem'; c.itemTpls = [cur]; c.count = Math.max(1, toCurrency(price * (o.amount || 0.5), cur)); c.foundInRaid = false; }
      else if (o.obj === 'killPmc') { c.type = 'Kill'; c.killTarget = 'AnyPmc'; c.count = count; }
      else if (o.obj === 'killScav') { c.type = 'Kill'; c.killTarget = 'Savage'; c.count = count * 2; }
      else if (o.obj === 'extract') { c.type = 'Extract'; c.count = Math.max(1, Math.round(count / 2)); }
      else {
        const barter = made.find(m => !isMoneyList(m.cost.map(z => z.itemTpl)))?.cost || autoBarter(price, x.id) || [{ itemTpl: cur, count: 1 }];
        c.type = 'HandoverItem'; c.itemTpls = [barter[0].itemTpl]; c.count = Math.max(1, Math.round(barter[0].count * (o.amount || 1))); c.foundInRaid = true;
      }
      q.conditions.push(c);
      if (o.xpPerLevel) q.rewards.push(fillReward({ type: 'Experience', value: Math.round(o.xpPerLevel * x.level) }));
      if (made.length) for (const m of made) q.rewards.push(fillReward({ type: 'UnlockOffer', offerId: m.id }));
      else q.rewards.push(fillReward({ type: 'Item', itemTpl: x.id, count: 1, foundInRaid: false }));
      if (o.chain && prev) q.prerequisiteQuestIds.push(prev.id);
      const pic = randomGameImage(); if (pic) q.gameImage = pic;
      f.quests.push(q); quests++; prev = q; firstQuest ||= q;
      if (o.dynamic && LG.loaded()) q.minLevel = dynamicLevel(q);
    }
  }
  return { offers, quests, firstQuest, firstOffer };
}

// =====================================================================
// Go To… (Ctrl+K)
// =====================================================================

const PAGE_NAMES = [['trader', 'Trader'], ['offers', 'Offers & Barters'], ['quests', 'Quests'], ['levels', 'Level Limits'], ['stats', 'Item Stats'], ['prog', 'Progression'], ['mods', 'Mods'], ['addons', 'Add-ons'], ['checks', 'Checks & Log']];
function paletteBox() {
  let query = '', found = [];
  const search = () => {
    const q = query.trim().toLowerCase(), words = q.split(/\s+/).filter(Boolean);
    const hit = text => words.every(w => text.toLowerCase().includes(w));
    const out = [];
    for (const [k, n] of PAGE_NAMES) if (!q || hit(n + ' page')) out.push({ kind: 'Page', text: n, go: () => { hideHome(); showPage(k); } });
    if (q) {
      for (const t of S.traders) {
        if (hit(t.file.name)) out.push({ kind: 'Trader', text: t.file.name, sub: `${t.file.offers.length} offers · ${t.file.quests.length} quests`, go: () => { hideHome(); selectTrader(t, false); showPage('trader'); renderAll(true); } });
        for (const q2 of t.file.quests) if (hit(`${q2.name} ${q2.tags.join(' ')} level ${q2.minLevel}`)) out.push({ kind: 'Quest', text: q2.name, sub: `${t.file.name} · level ${q2.minLevel}`, go: () => { hideHome(); selectTrader(t, false); selectQuest(q2); S.page = ''; showPage('quests'); } });
        for (const o of t.file.offers) if (hit(`${itemName(o.itemTpl)} ${o.tags.join(' ')}`)) out.push({ kind: 'Offer', text: itemName(o.itemTpl), sub: `${t.file.name} · ${costText(o)}`, item: o.itemTpl, go: () => { hideHome(); selectTrader(t, false); S.offer = o; S.page = ''; showPage('offers'); } });
        if (out.length > 200) break;
      }
      let n = 0;
      for (const it of S.items.values()) {
        if (n > 60) break;
        if (it.x || !hit(`${it.n} ${it.s || ''} ${it.i}`)) continue;
        n++;
        const l = LG.levelOf(it.i);
        out.push({ kind: 'Item', text: it.n, sub: `${it.s || ''}${l ? ` · Level ${l}` : ''} — open in Level Limits`, item: it.i, go: () => goItemLevels(it.i) });
      }
    }
    return out.slice(0, 120);
  };
  const draw = m => {
    found = search();
    m.querySelector('.picker-list').innerHTML = found.map((r, i) => `<div class="row ${i === 0 ? 'sel' : ''}" data-go="${i}"><div class="cell">${thumb({ text: r.kind.slice(0, 2).toUpperCase(), color: { Page: 'var(--accent)', Trader: 'var(--violet)', Quest: 'var(--orange)', Offer: 'var(--blue)', Item: '#3a3a3a' }[r.kind], dark: r.kind !== 'Item', item: r.item })}
      <div class="text"><div class="title">${esc(r.text)}</div>${r.sub ? `<div class="line2">${esc(r.sub)}</div>` : ''}</div></div><div class="col">${r.kind}</div></div>`).join('') || '<div class="empty">Nothing found</div>';
  };
  return openModal(`<div class="dialog"><h2>Go To…</h2>
    <div class="picker-search">🔍<input type="text" id="pickQuery" placeholder="A page, trader, quest, offer or item (Enter = the first one)"></div>
    <div class="picker-list"></div>
    <div class="buttons"><button class="outline" data-m="0">Close</button></div></div>`, m => {
    const input = m.querySelector('#pickQuery');
    input.focus();
    let timer = 0;
    input.oninput = () => { clearTimeout(timer); timer = setTimeout(() => { query = input.value; draw(m); }, 70); };
    input.onkeydown = e => { if (e.key === 'Enter' && found[0]) { closeModal(null); found[0].go(); } };
    m.querySelector('.picker-list').onclick = e => { const r = e.target.closest('[data-go]'); if (r) { closeModal(null); found[Number(r.dataset.go)].go(); } };
    m.querySelector('[data-m="0"]').onclick = () => closeModal(null);
    draw(m);
  });
}

// =====================================================================
// Start page: tools, notes (moving the old mods' files over, settings taken over)
// =====================================================================

function homeTools() {
  const lgLoaded = S.lgStarted && LG.loaded(), st = S.lgStarted ? LG.state : {};
  const quests = S.traders.reduce((n, t) => n + t.file.quests.length, 0), dyn = S.traders.reduce((n, t) => n + t.file.quests.filter(q => validId(q.levelFromItem)).length, 0);
  const tool = (page, color, art, name, lines) => `<button class="hcard tool" data-act="homeTool" data-arg="${page}" style="--hc:${color}"><div class="tool-art">${art}</div>
    <div class="htext"><div class="hname">${name}</div>${lines.map(l => `<div class="hsub">${l}</div>`).join('')}</div></button>`;
  return tool('levels', '#b8901c', 'LV', 'Level Limits', lgLoaded ? [`${fmt(LG.levels().size)} items limited`, `${st.disabled?.size || 0} switched off`] : ['⚠ Level Gate\'s file not found', 'open it to pick the file'])
    + tool('stats', '#1e8a4a', '+', 'Item Stats', [`${fmt(Object.keys(st.meds || {}).length)} meds, stims & food`, `${Object.keys(st.statEdits || {}).length} edited`])
    + tool('prog', '#2c6f96', '▲', 'Progression', ['what unlocks at each level', `${quests} quests · ${dyn} dynamic`])
    + `<button class="hcard tool" data-act="openLogs" style="--hc:#6b5b95"><div class="tool-art">📄</div><div class="htext"><div class="hname">Logs</div><div class="hsub">open the Logs folder</div><div class="hsub">send the newest file if something breaks</div></div></button>`
    + `<button class="hcard tool" data-act="palette" style="--hc:#555"><div class="tool-art">🔎</div><div class="htext"><div class="hname">Go To…</div><div class="hsub">any trader, quest, offer or item</div><div class="hsub">Ctrl+K</div></div></button>`;
}

function homeNotes() {
  const out = [];
  if (S.migration) {
    const m = S.migration;
    out.push(`<div class="home-note warn"><h3>Move Your Files Into Modern Editor</h3>
      <div class="hint">Modern Editor keeps everything in <b>${esc(m.to)}</b>. Still in the old mods:</div>
      <div class="req">${m.from.map(f => `• ${esc(f.mod)}: ${esc(f.what)}`).join('\n')}</div>
      <div class="hint">Moving copies them over (each trader keeps its own trader.json, item_stats.json stays its own file), then puts the old mod folders aside in user\\ModernEditor_old_mods (as a backup) so the server doesn't load them twice. Level Gate's own file isn't touched. Close the SPT server first.${m.serverMod ? '' : '<br><b>Also copy ModernEditor.dll into that folder</b> (Install\\SPT_Runtime\\user\\mods\\ModernEditor in the download).'}</div>
      <div class="toolbar"><button class="primary" data-act="migrate">Move My Files</button><button class="ghost" data-act="migrateLater">Not Now</button></div></div>`);
  } else if (S.modFolder && S.serverMod === false) {
    out.push(`<div class="home-note warn"><h3>Server Mod Missing</h3><div class="hint">ModernEditor.dll isn't in <b>${esc(S.modFolder)}</b> yet — copy it there from the download (Install\\SPT_Runtime\\user\\mods\\ModernEditor), or the game won't get your traders and item stats.</div></div>`);
  }
  const junk = oldFilesToShow();
  if (junk.length) {
    const total = junk.reduce((n, f) => n + (f.size || 0), 0);
    out.push(`<div class="home-note warn" id="cleanNote"><h3>Old Files Found — Clean Them Up?</h3>
      <div class="hint">Modern Editor replaced these (${junk.length} item${junk.length === 1 ? '' : 's'}, ${sizeText(total)}). Pick what to remove — it goes to the <b>Recycle Bin</b>, so you can still get it back. Level Gate and your ModernEditor files are never listed.</div>
      <div class="clean-list">${junk.map((f, i) => `<label class="clean-row"><input type="checkbox" data-clean="${i}" checked><span><b>${esc(f.what)}</b><br><span class="muted small">${esc(f.path)} · ${sizeText(f.size)}</span></span></label>`).join('')}</div>
      <div class="toolbar"><button class="primary" data-act="cleanup">Clean Up Selected</button><button class="ghost" data-act="cleanupLater">Not Now</button></div></div>`);
  }
  if (S.imported?.length && !S.ui.importNoteSeen)
    out.push(`<div class="home-note"><h3>Welcome to Modern Editor</h3><div class="hint">Your settings from the ${esc(S.imported.join(' and the '))} came along: folders, imported mods, tags, notes, your own orders and categories, colors and column widths.</div>
      <div class="toolbar"><button class="ghost" data-act="importSeen">Got It</button></div></div>`);
  return out.join('');
}

// =====================================================================
// Old files (Clean Up) and the log
// =====================================================================

const sizeText = n => n >= 1048576 ? `${(n / 1048576).toFixed(1)} MB` : n >= 1024 ? `${Math.round(n / 1024)} KB` : `${n || 0} B`;
/** What the last scan found, minus what "Not Now" put aside (until something new turns up). */
function oldFilesToShow() {
  const list = S.oldFiles || [];
  const later = new Set(S.ui.cleanupLater || []);
  return list.some(f => !later.has(f.path)) ? list : [];
}
async function scanOldFiles() {
  try { S.oldFiles = await host.call('cleanupScan') || []; } catch (e) { S.oldFiles = []; }
  if (S.home) renderHome();
}

/** Everything the page shows or runs into also goes to the log file (Logs\ next to ModernEditor.exe), in small batches. */
const pageLog = (() => {
  let lines = [], timer = null, busy = false;
  const flush = () => {
    timer = null;
    if (!lines.length) return;
    const batch = lines; lines = [];
    for (const level of ['error', 'warn', 'info']) {
      const text = batch.filter(l => l.level === level).map(l => l.text).join('\n');
      if (text) host.call('log', { level, text }).catch(() => { });
    }
  };
  return (level, text) => {
    if (busy) return;
    busy = true;
    try { lines.push({ level, text: String(text).slice(0, 4000) }); } finally { busy = false; }
    if (level === 'error') flush(); else if (!timer) timer = setTimeout(flush, 600);
  };
})();
window.addEventListener('error', e => pageLog('error', `${e.message} at ${(e.filename || '').split('/').pop()}:${e.lineno}:${e.colno}${e.error?.stack ? '\n' + e.error.stack : ''}`));
window.addEventListener('unhandledrejection', e => pageLog('error', `unhandled: ${e.reason?.stack || e.reason}`));
for (const [name, level] of [['error', 'error'], ['warn', 'warn']]) {
  const orig = console[name].bind(console);
  console[name] = (...a) => { orig(...a); pageLog(level, a.map(x => x?.stack || (typeof x === 'object' ? JSON.stringify(x) : String(x))).join(' ')); };
}
{
  // what the user saw (toasts, status line, the Checks & Log page's log, error boxes) and where they went
  const wrap = (name, fn) => { const orig = window[name]; if (typeof orig === 'function') window[name] = function (...a) { try { fn(...a); } catch { } return orig.apply(this, a); }; };
  wrap('toast', t => pageLog('info', 'toast: ' + t));
  wrap('status', t => pageLog('info', 'status: ' + t));
  wrap('log', (level, where, msg) => pageLog(level === 'error' ? 'error' : level === 'warning' ? 'warn' : 'info', `log ${where}: ${msg}`));
  wrap('errorBox', err => pageLog('error', 'error box: ' + (err?.stack || err?.message || err)));
  wrap('showPage', p => pageLog('info', `page: ${p}${S.t ? ` (trader ${S.t.file.name})` : ''}`));
}

/** Quests (all your traders, switched on) that unlock at a level — shown in the Progression view. */
function questsAtLevel(level) {
  const out = [];
  for (const t of S.traders) if (t.file.enabled) for (const q of t.file.quests) if (q.enabled && requirements(q).effective === level) out.push({ t, q });
  return out;
}
let progQuests = [];
function questsAtLevelHtml(level) {
  const list = progQuests = questsAtLevel(level);
  if (!list.length) return '';
  return `<section class="pcat" style="--c:#ffa42b"><h3><i></i>QUESTS<em>${list.length}</em></h3><div class="pquests">${list.map((x, i) => {
    const pic = questPic(x.t, x.q);
    return `<button class="pquest" data-act="progQuest" data-arg="${i}" title="${esc(x.t.file.name)} › ${esc(x.q.name)}">${pic ? `<img src="${esc(pic.url)}" alt="">` : ''}
      <div><div class="pq-name">${esc(x.q.name)}</div><div class="pq-sub">${esc(x.t.file.name)}${validId(x.q.levelFromItem) ? ' · ⟲ follows ' + esc(shortName(x.q.levelFromItem)) : ''}</div></div></button>`;
  }).join('')}</div></section>`;
}
function goProgQuest(i) {
  const x = progQuests[Number(i)]; if (!x) return;
  selectTrader(x.t, false); selectQuest(x.q);
  document.body.classList.remove('prog-mode');
  showPage('quests');
}

Object.assign(ACT, {
  palette: () => paletteBox(),
  homeTool(page) { hideHome(); showPage(page); },
  followTied() {
    const t = S.t, q = S.quest; if (!t || !q) return;
    const id = questItem(t, q); if (!validId(id)) return;
    q.levelFromItem = id; if (LG.loaded()) q.minLevel = dynamicLevel(q);
    questItemSeen.set(q.id, id);
    markDirty(t); runChecks(); renderPage(false); renderDetails(false);
    toast(`${q.name} now follows ${shortName(id)} — level ${q.minLevel}`);
  },
  goLevel: id => goItemLevels(id),
  importSeen() { S.ui.importNoteSeen = true; saveUi(); renderHome(); },
  async migrate() {
    try {
      const snap = await host.call('migrate');
      applySnapshot(snap);
      await LG.start(snap);
      lgLevelsChanged(true);
      renderAll(false);
      scanOldFiles();
      await openModal(`<div class="dialog"><h2>Files Moved</h2><div class="req">${esc((snap.migrated || []).join('\n') || 'Nothing to move.')}</div>
        <div class="hint">Restart the SPT server. The old mods are in user\\ModernEditor_old_mods if you ever need them.</div>
        <div class="buttons"><button class="primary" data-m>OK</button></div></div>`, m => { m.querySelector('[data-m]').onclick = () => closeModal(null); });
    } catch (err) { errorBox(err); }
  },
  openLogs: () => host.call('openLogs').catch(errorBox),
  async cleanup() {
    const picked = [...document.querySelectorAll('#cleanNote [data-clean]')].filter(x => x.checked).map(x => oldFilesToShow()[Number(x.dataset.clean)]?.path).filter(Boolean);
    if (!picked.length) return toast('Nothing picked');
    try {
      const r = await host.call('cleanup', { paths: picked });
      S.oldFiles = r.left || [];
      renderHome();
      await openModal(`<div class="dialog"><h2>Cleaned Up</h2><div class="req">${esc((r.cleaned || []).join('\n') || 'Nothing removed.')}</div>
        <div class="hint">Everything went to the Recycle Bin — restore it from there if you need it back.</div>
        <div class="buttons"><button class="primary" data-m>OK</button></div></div>`, m => { m.querySelector('[data-m]').onclick = () => closeModal(null); });
    } catch (err) { errorBox(err); }
  },
  cleanupLater() { S.ui.cleanupLater = (S.oldFiles || []).map(f => f.path); saveUi(); renderHome(); },
  async migrateLater() { await host.call('migrateLater').catch(() => { }); S.migration = null; renderHome(); },
});

// the item pages' own actions that open shell things
LG.addActs({
  progQuest: i => goProgQuest(i),
  goLevel: id => goItemLevels(id),
});
