/* LevelGate Editor — item stats: what the lists show (ammo pen, armor class, slots, med / food
   effects) and the Item Stats tab that edits meds, stims and food.
   S.meds[id]      = the item as the game has it (SPT database + other mods like BalancedMeds)
   S.statEdits[id] = LevelGate's edit (the whole item, same shape) → user\mods\LevelGate\item_stats.json,
                     applied by the ItemStatEditor server mod when the server starts.
   S.medsDefault[id] = Escape From Tarkov's own values (the SPT database before any mod). */
'use strict';

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
const STAT_SORTS = [['custom', 'Custom Order'], ['name', 'Name'], ['type', 'Type'], ['level', 'Level'], ['edited', 'Edited First']];
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
  $('#nav').innerHTML = entry('all', 'All Meds & Food', '#ffffff') + MED_KINDS.map(([k, n, c]) => entry(k, n, c)).join('') + entry('edited', 'Edited by You', 'var(--violet)');
  $('#navModsBtn').hidden = true;
}

function statsPage() {
  const d = S.statSort.dir, byName = (a, b) => (a.n || '').localeCompare(b.n || '');
  const kindIx = it => MED_KINDS.findIndex(k => k[0] === S.meds[it.i].kind);
  const cmp = {
    custom: customCmp('stats'),
    name: (a, b) => d * byName(a, b),
    type: (a, b) => d * (kindIx(a) - kindIx(b)) || byName(a, b),
    level: (a, b) => { const la = levelOf(a.i), lb = levelOf(b.i); if (la === undefined && lb === undefined) return byName(a, b); if (la === undefined) return 1; if (lb === undefined) return -1; return d * (la - lb) || byName(a, b); },
    edited: (a, b) => Number(!!S.statEdits[b.i]) - Number(!!S.statEdits[a.i]) || byName(a, b),
  }[S.statSort.key] || ((a, b) => byName(a, b));
  S.shown = statIds(S.statCat).map(id => S.items.get(id)).sort(cmp);
  const ids = S.shown.map(it => it.i);
  const head = ([k, t]) => `<div><span class="head-text sortable ${S.statSort.key === k ? 'on' : ''}" data-act="statSortBy" data-arg="${k}">${t}${S.statSort.key === k && k !== 'custom' ? `<span class="sort-arrow">${d > 0 ? '▲' : '▼'}</span>` : ''}</span></div>`;
  const warn = !S.serverMod
    ? `<div class="warn-line">⚠ The ItemStatEditor server mod wasn't found in ${esc(S.statsFile ? S.statsFile.replace(/[\\/]item_stats\.json$/, '') : 'SPT\\user\\mods\\ItemStatEditor')} — item stat edits are saved but only take effect with it installed (ItemStatEditor.Server.dll from the download).</div>` : '';
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
      ${levelCell(id)}
    </div>`;
  }).join('');
  return `<div class="toolbar sticky">${warn}
      <span class="muted small">${fmt(ids.length)} shown${S.picked.size > 1 ? ` · ${S.picked.size} picked` : ''} · Ctrl+drag to reorder · edits apply after restarting the SPT server${S.statSources.length ? ` · your edits win over ${esc(S.statSources.map(x => x.replace(/ \(.*/, '')).join(' and '))}` : ''}</span></div>
    ${tagBar(statIds(S.statCat))}
    <div class="list st"><div class="list-head">${head(['custom', '#'])}${head(['name', 'Item'])}${head(['type', 'Type'])}<div>Effects</div>${head(['level', 'Level'])}</div>${rows || `<div class="empty">${Object.keys(S.meds).length ? 'Nothing matches.' : 'No meds loaded — the stats come from the SPT database (pick the config inside your SPT folder).'}</div>`}</div>`;
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
      <br><br>Edits are saved to <span class="mono">${esc(S.statsFile || 'SPT\\user\\mods\\ItemStatEditor\\item_stats.json')}</span> and applied by the ItemStatEditor server mod when the server starts (after other mods, so they win). The Level Limits tab shows the edited stats too.
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
    S.statSort = S.statSort.key === key && key !== 'custom' ? { key, dir: -S.statSort.dir } : { key, dir: key === 'edited' ? -1 : 1 };
    saveUi(); renderPage(); renderHeader();
  },
  statManyDefault() { const ids = [...S.picked].filter(x => S.meds[x]); ids.forEach(x => ACT.statResetDefault(x)); toast(`${ids.length} reset to Escape From Tarkov's values`); },
  statManyUndo() { const ids = [...S.picked].filter(x => S.statEdits[x]); ids.forEach(x => { S.sel = x; ACT.statReset(); }); toast(`${ids.length} edit(s) undone`); },
  statCat(key) { S.statCat = key; S.page = 'items'; saveUi(); renderAll(); $('#page').scrollTop = 0; },
  editStats(id) { S.tab = 'stats'; S.statCat = 'all'; S.sel = id; S.picked = new Set(); saveUi(); renderAll(); document.querySelector(`#page .row[data-row="${id}"]`)?.scrollIntoView({ block: 'center' }); },
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
