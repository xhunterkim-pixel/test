/* LevelGate Editor — item stats: what the lists show (ammo pen, armor class, slots, med / food
   effects) and the Item Stats tab that edits meds, stims and food.
   S.meds[id]      = the item as the game has it (SPT database + other mods like BalancedMeds)
   S.statEdits[id] = LevelGate's edit (the whole item, same shape) → user\mods\LevelGate\item_stats.json,
                     applied by the LevelGate server mod when the server starts. */
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
const KEEP = ['medUseTime', 'MaxHpResource', 'hpResourceRate', 'foodUseTime', 'MaxResource', 'effects_health', 'effects_damage', 'buffName', 'effects_buffs'];

const r2 = n => Math.round(n * 100) / 100;
const sgn = n => (n > 0 ? '+' : n < 0 ? '−' : '') + r2(Math.abs(n));
const skillName = s => (s || '').replace(/([a-z])([A-Z])/g, '$1 $2');

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
function statsShort(it) {
  const st = it.st || {};
  const parts = [];
  if (st.pen !== undefined) parts.push(`Pen ${st.pen} · Dmg ${st.dmg}${st.pc > 1 ? `×${st.pc}` : ''}`);
  if (st.ac) parts.push(`Class ${st.ac}`);
  if (st.slots && !st.pen) parts.push(`${st.slots} slots`);
  const kind = medKind(it), m = kind && effMed(it.i);
  if (m) {
    if (kind === 'medkit') parts.push(`${m.MaxHpResource ?? 0} HP`, ...curesText(m, kind).slice(0, 2));
    else if (kind === 'food') parts.push(...healthText(m), ...((m.effects_buffs || []).length ? [`${m.effects_buffs.length} effect(s)`] : []));
    else if (kind === 'stim') parts.push(`${(m.effects_buffs || []).length} effects`, `${Math.max(0, ...(m.effects_buffs || []).map(b => Number(b.Duration) || 0))} s`);
    else parts.push(...curesText(m, kind).slice(0, 3), ...(m.MaxHpResource > 1 ? [`${m.MaxHpResource} uses`] : []));
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
  if (st.ac) row('Armor Class', `<b>${st.ac}</b>${st.dur ? ` · ${fmt(st.dur)} durability` : ''}`);
  if (st.slots) row('Storage', `${st.slots} slots`);
  const kind = medKind(it), m = kind && effMed(it.i);
  if (m) {
    const use = m.medUseTime ?? m.foodUseTime;
    if (use !== undefined) row('Use Time', `${use} s`);
    if (kind === 'medkit') row('HP Resource', `${m.MaxHpResource ?? 0}${m.hpResourceRate ? ` · heals up to ${m.hpResourceRate} HP per use` : ''}`);
    else if (kind === 'food') { if (m.MaxResource > 1) row('Uses', m.MaxResource); }
    else if (m.MaxHpResource > 1) row('Uses', m.MaxHpResource);
    const health = healthText(m);
    if (health.length) row(kind === 'food' ? 'Gives' : 'Also', esc(health.join(', ')));
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
    return !q || matches(it, q);
  });
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
  $('#nav').innerHTML = entry('all', 'All Meds & Food', '#ffffff') + MED_KINDS.map(([k, n, c]) => entry(k, n, c)).join('') + entry('edited', 'Edited by LevelGate', 'var(--violet)');
  $('#navModsBtn').hidden = true;
}

function statsPage() {
  const ids = statIds(S.statCat).sort((a, b) => (S.items.get(a).n || '').localeCompare(S.items.get(b).n || ''));
  S.shown = ids.map(id => S.items.get(id));
  const warn = !S.serverMod
    ? `<div class="warn-line">⚠ The LevelGate server mod wasn't found in ${esc(S.statsFile ? S.statsFile.replace(/[\\/]item_stats\.json$/, '') : 'SPT\\user\\mods\\LevelGate')} — item stat edits are saved but only take effect with it installed (LevelGate.Server.dll from the download).</div>` : '';
  const rows = ids.map((id, i) => {
    const it = S.items.get(id), m = effMed(id), k = MED_KIND[m.kind];
    const changed = JSON.stringify(S.statEdits[id] ?? null) !== JSON.stringify(S.statSaved[id] ?? null);
    return `<div class="row ${id === S.sel ? 'sel' : ''} ${changed ? 'changed' : ''}" data-row="${id}">
      <div class="idx">${i + 1}</div>
      <div class="cell">${icon(it)}<div class="text"><div class="line1"><span class="title">${esc(it.n)}</span><span class="dirty" title="Changed — not saved yet">•</span>
        <div class="badges">${S.statEdits[id] ? '<span class="badge" style="--c:var(--violet)">EDITED</span>' : ''}${it.m ? `<span class="badge" style="--c:var(--pink)">${esc(it.m)}</span>` : ''}</div></div>
        <div class="line2">${esc(it.s || '')}</div></div></div>
      <div class="col"><i class="dot" style="--c:${k.color}"></i>${esc(k.name)}</div>
      <div class="col stats" title="${esc(statsShort(it))}">${esc(statsShort(it).replace(/ · ✎$/, ''))}</div>
      <div class="col num">${levelOf(id) !== undefined ? 'Lvl ' + levelOf(id) : '<span class="muted">—</span>'}</div>
    </div>`;
  }).join('');
  return `<div class="toolbar sticky">${warn}
      <span class="muted small">${fmt(ids.length)} shown · edits apply after restarting the SPT server${S.statSources.length ? ` · LevelGate's edits win over ${esc(S.statSources.map(x => x.replace(/ \(.*/, '')).join(' and '))}` : ''}</span></div>
    <div class="list st">${`<div class="list-head"><div>#</div><div>Item</div><div>Type</div><div>Effects</div><div>Level</div></div>`}${rows || `<div class="empty">${Object.keys(S.meds).length ? 'Nothing matches.' : 'No meds loaded — the stats come from the SPT database (pick the config inside your SPT folder).'}</div>`}</div>`;
}

// ---- the editor (right panel)
function statsDetails() {
  const d = $('#details');
  const id = S.sel && S.meds[S.sel] ? S.sel : null;
  if (!id) {
    $('#detailsTitle').textContent = 'Item Stats';
    d.innerHTML = `<div class="card"><h3>Meds, Stims & Food</h3><div class="hint">Pick an item to change how long it takes to use, how many uses / HP it has, what it treats, energy and hydration, and a stim's effects.
      <br><br>Edits are saved to <span class="mono">${esc(S.statsFile || 'SPT\\user\\mods\\LevelGate\\item_stats.json')}</span> and applied by the LevelGate server mod when the server starts (after other mods, so they win). The Level Limits tab shows the edited stats too.</div></div>`;
    return;
  }
  const keep = document.activeElement?.dataset?.key;
  const it = S.items.get(id), m = effMed(id), kind = m.kind, k = MED_KIND[kind];
  $('#detailsTitle').textContent = it.n;
  const num = (label, key, field, step = 1, hint = '') => `<div class="field"><label>${esc(label)}</label><div class="numrow"><input type="number" step="${step}" data-key="${key}" data-sf="${field}" value="${m[field] ?? ''}">${hint ? `<span class="muted small">${hint}</span>` : ''}</div></div>`;
  const resource = kind === 'medkit' ? num('HP Resource', 'hp', 'MaxHpResource', 1, 'total HP it can heal') + num('HP per Use', 'rate', 'hpResourceRate', 1, 'most it heals in one use')
    : kind === 'food' ? num('Uses', 'res', 'MaxResource', 1) : num('Uses', 'hp', 'MaxHpResource', 1);
  const useField = kind === 'food' ? 'foodUseTime' : 'medUseTime';
  const health = HEALTH.map(([f, n]) => `<div class="field"><label>${n}</label><div class="numrow"><input type="number" step="1" data-key="eh-${f}" data-eh="${f}" value="${m.effects_health?.[f]?.value ?? ''}" placeholder="none"><span class="muted small">${kind === 'food' ? 'per use' : 'when used'}</span></div></div>`).join('');
  const dmg = DAMAGE.map(([t, n]) => {
    const e = m.effects_damage?.[t];
    const on = !!e;
    const f = (field, label) => `<label class="mini-f">${label}<input type="number" step="1" data-key="ed-${t}-${field}" data-ed="${t}" data-edf="${field}" value="${e?.[field] ?? ''}" ${on ? '' : 'disabled'}></label>`;
    return `<div class="dmg-row ${on ? 'on' : ''}"><label class="switch"><input type="checkbox" data-key="ed-${t}" data-edon="${t}" ${on ? 'checked' : ''}><span class="track"></span><span>${n}</span></label>
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
    ${S.statEdits[id] ? `<div class="edited-bar">✎ Edited by LevelGate <button class="outline" data-act="statReset">Reset to Game Values</button></div>` : ''}
    <div class="card"><h3>Use</h3>${num('Use Time', 'use', useField, 0.5, 'seconds')}${resource}</div>
    <div class="card"><h3>${kind === 'food' ? 'Energy & Hydration' : 'Energy & Hydration'}</h3>${health}</div>
    ${kind === 'food' ? '' : `<div class="card"><h3>${kind === 'medkit' || kind === 'medical' ? 'Treats' : 'Removes'}</h3><div class="hint">${kind === 'medkit' || kind === 'medical' ? '"Cost" = HP / uses spent to treat it.' : 'Switched on = the effect is suppressed for that long (painkillers, stims).'}</div>${dmg}</div>`}
    <div class="card"><h3>Effects Over Time<span class="grow"></span><button class="outline small-btn" data-act="buffAdd">+ Add Effect</button></h3>
      ${m.buffName ? `<div class="hint">Buff list "${esc(m.buffName)}"${sharing.length ? ` — also used by ${esc(sharing.join(', '))}, which change with it` : ''}.</div>` : '<div class="hint">No effects over time yet (stims, some food). Adding one creates a list just for this item.</div>'}
      ${buffs || ''}</div>`;
  if (keep) d.querySelector(`[data-key="${keep}"]`)?.focus();
}

/** Changes the edited item (as one undo step). */
function editStat(id, mutate) {
  const before = S.statEdits[id] ? JSON.stringify(S.statEdits[id]) : null;
  const m = JSON.parse(JSON.stringify(effMed(id)));
  mutate(m);
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
  statCat(key) { S.statCat = key; S.page = 'items'; saveUi(); renderAll(); $('#page').scrollTop = 0; },
  editStats(id) { S.tab = 'stats'; S.statCat = 'all'; S.sel = id; S.picked = new Set(); saveUi(); renderAll(); document.querySelector(`#page .row[data-row="${id}"]`)?.scrollIntoView({ block: 'center' }); },
  statReset() { const id = S.sel; editStat(id, m => { for (const k of KEEP) { if (S.meds[id][k] === undefined) delete m[k]; else m[k] = JSON.parse(JSON.stringify(S.meds[id][k])); } }); },
  buffAdd() {
    const id = S.sel;
    editStat(id, m => {
      if (!m.buffName) m.buffName = `LevelGate_${id}`;
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
