/* Modern Editor 2.0.8 — custom builds: sell a gun with the parts you pick (or armor with its plates, a helmet with its
   visor…). An offer's build is o.parts: [{ id, tpl, parentId ('root' = the offer's item), slotId }], o.buildName.
   The server mod turns it into the trader's item tree. Slots and what fits come from the game's items.json (host:
   itemSlots); a build can start from the game's presets or your own builds saved in game (host: buildSources). */
'use strict';

const SLOT_CACHE = new Map();       // tpl → [{ name, required, fits[], nested[] }]
const slotsOf = tpl => SLOT_CACHE.get(tpl);
async function loadSlots(tpl) {
  if (!tpl || SLOT_CACHE.has(tpl)) return SLOT_CACHE.get(tpl);
  try { const r = await host.call('itemSlots', { tpl }); SLOT_CACHE.set(tpl, r?.slots || []); }
  catch { SLOT_CACHE.set(tpl, []); }
  return SLOT_CACHE.get(tpl);
}
const hasSlots = tpl => (slotsOf(tpl) || []).length > 0;

/** "mod_pistol_grip" → "Pistol Grip", "mod_scope_000" → "Scope 1", "front_plate" → "Front Plate". */
function slotLabel(name) {
  let s = String(name).replace(/^mod_/, '').replace(/_/g, ' ');
  const m = s.match(/^(.*?)\s*(\d{3})$/);
  if (m) s = `${m[1]} ${Number(m[2]) + 1}`;
  return s.replace(/\b\w/g, c => c.toUpperCase()).replace(/\bNvg\b/, 'NVG').replace(/\bLl\b/, 'Tactical');
}

/** A build's worth: the item and every part (handbook / traders pay / flea). */
function buildValue(o, kind) {
  return itemValue(o.itemTpl, kind) + (o.parts || []).reduce((sum, p) => sum + itemValue(p.tpl, kind), 0);
}

/** The offer card: what is sold (bare, the game's default preset or your build) and the Edit Build button. */
function buildCard(o) {
  const tpl = o.itemTpl;
  if (!slotsOf(tpl)) { loadSlots(tpl).then(() => { if (S.offer === o && hasSlots(tpl)) renderDetails(); }); return ''; }
  if (!hasSlots(tpl)) return '';
  const parts = o.parts || [];
  const byParent = p => parts.filter(x => x.parentId === p);
  const tree = (pid, depth) => byParent(pid).map(p => `<div class="bpart" style="--d:${depth}"><span class="bslot">${esc(slotLabel(p.slotId))}</span>
      ${itemPic(p.tpl) ? `<img src="${itemPic(p.tpl)}" alt="" ${picFail}>` : ''}<span class="bname">${esc(itemName(p.tpl))}</span></div>${tree(p.id, depth + 1)}`).join('');
  const what = parts.length ? `<b>${esc(o.buildName || 'Custom build')}</b> · ${parts.length} part${parts.length === 1 ? '' : 's'}`
    : o.useDefaultPreset && item(tpl)?.ph ? 'The game\'s default preset (turn off "Sell the Assembled Gun" for a bare item)' : 'Sold bare (no parts)';
  const missing = parts.length ? missingRequired(tpl, parts) : [];
  return card('o-build', `Build ${parts.length ? ui.badge('CUSTOM', 'var(--accent)') : ''}`, `
    <div class="bwhat">${what}</div>
    ${missing.length ? `<div class="warn-line">⚠ Empty required slot${missing.length > 1 ? 's' : ''}: ${esc(missing.join(', '))} — the game may refuse the item.</div>` : ''}
    ${parts.length ? `<div class="btree">${tree('root', 0)}</div>` : ''}
    <div class="toolbar" style="margin-top:8px"><button class="primary" data-act="editBuild">🔧 ${parts.length ? 'Edit Build…' : 'Make a Custom Build…'}</button>
      ${parts.length ? '<button class="outline" data-act="useBuildPrice">Price = Build\'s Handbook Value</button><button class="danger" data-act="clearBuild">Remove Build</button>' : ''}</div>
    ${ui.hint('Pick the parts the trader sells it with, slot by slot (scope, stock, grip, magazine, plates…), or start from one of the game\'s presets or one of your own builds saved in game (the weapon modding screen\'s Save Build). Players buy it fully assembled like this.')}`);
}

/** Required slots (of the item and of the picked parts, as far as their slots are loaded) with nothing in them. */
function missingRequired(tpl, parts) {
  const out = [];
  const walk = (t, pid) => (slotsOf(t) || []).forEach(s => {
    const p = parts.find(x => x.parentId === pid && x.slotId === s.name);
    if (!p && s.required) out.push(slotLabel(s.name));
    if (p) walk(p.tpl, p.id);
  });
  walk(tpl, 'root');
  return out;
}

// ---------------------------------------------------------------- the build editor (a dialog)

async function editBuild(o) {
  const tpl = o.itemTpl;
  await loadSlots(tpl);
  let parts = JSON.parse(JSON.stringify(o.parts || []));
  let name = o.buildName || '';
  let open = null, query = '';            // the slot whose part list is open ("parentId|slotId") and its search
  let sources = [];
  host.call('buildSources', { tpl }).then(r => { sources = r || []; draw(); }).catch(() => { });
  // parts of the picked parts: load their slots (then draw again)
  const ensure = () => Promise.all(parts.map(p => loadSlots(p.tpl))).then(() => draw());
  const removeTree = id => { const kids = parts.filter(p => p.parentId === id); parts = parts.filter(p => p.id !== id); kids.forEach(k => removeTree(k.id)); };
  const set = (pid, slot, t) => {
    const old = parts.find(p => p.parentId === pid && p.slotId === slot);
    if (old) removeTree(old.id);
    if (t) parts.push({ id: newId(), tpl: t, parentId: pid, slotId: slot });
    open = null; query = '';
    ensure();
  };
  let m;
  const rows = (t, pid, depth) => (slotsOf(t) || []).map(s => {
    const key = `${pid}|${s.name}`, p = parts.find(x => x.parentId === pid && x.slotId === s.name);
    const pic = p && itemPic(p.tpl);
    let html = `<div class="brow ${p ? 'set' : ''} ${!p && s.required ? 'need' : ''}" style="--d:${depth}">
      <span class="bslot">${esc(slotLabel(s.name))}${s.required ? '<i title="Required">*</i>' : ''}</span>
      <button class="bpick" data-bopen="${esc(key)}" title="${esc(s.fits.length + ' part(s) fit here')}">${pic ? `<img src="${pic}" alt="" ${picFail}>` : ''}<span>${p ? esc(itemName(p.tpl)) : `— empty — <small>${s.fits.length} fit</small>`}</span><b>▾</b></button>
      ${p ? `<button class="icon-btn" data-bclear="${esc(key)}" title="Empty this slot (and what's on the part)">✕</button>` : ''}
      ${p ? `<span class="bval">${money(itemValue(p.tpl, 'h'), CUR.RUB)}</span>` : ''}</div>`;
    if (open === key) {
      const q = query.toLowerCase();
      const found = s.fits.map(id => item(id)).filter(it => it && (!q || it.n.toLowerCase().includes(q) || (it.s || '').toLowerCase().includes(q) || it.i.startsWith(q)))
        .sort((a, b) => a.n.localeCompare(b.n));
      html += `<div class="blist" style="--d:${depth}"><input type="text" class="bsearch" placeholder="Search ${esc(slotLabel(s.name))} parts (${s.fits.length})…" value="${esc(query)}" spellcheck="false">
        <div class="bitems">${found.slice(0, 150).map(it => `<button class="bitem ${p?.tpl === it.i ? 'on' : ''}" data-bset="${esc(key)}" data-tpl="${it.i}">${itemPic(it.i) ? `<img src="${itemPic(it.i)}" alt="" ${picFail}>` : ''}<span>${esc(it.n)}</span><small>${it.h ? money(it.h, CUR.RUB) : ''}</small></button>`).join('')
          || '<div class="muted small" style="padding:8px">Nothing matches.</div>'}${found.length > 150 ? `<div class="muted small" style="padding:6px">${found.length - 150} more — type to narrow it down</div>` : ''}</div></div>`;
    }
    if (p) html += rows(p.tpl, p.id, depth + 1);
    return html;
  }).join('');
  const draw = () => {
    if (!m || m.hidden) return;
    const miss = missingRequired(tpl, parts);
    const total = itemValue(tpl, 'h') + parts.reduce((a, p) => a + itemValue(p.tpl, 'h'), 0);
    const y = m.querySelector('.btree-edit')?.scrollTop || 0;
    m.querySelector('.bbody').innerHTML = `
      <div class="bhead">
        <label>Name <input type="text" class="bname-in" value="${esc(name)}" placeholder="e.g. M4A1 Recon" spellcheck="false"></label>
        <label>Start From <select class="bfrom"><option value="">—</option><option value="empty">Empty (no parts)</option>
          ${sources.map((x, i) => `<option value="${i}">${esc(x.from)}: ${esc(x.name || '(no name)')} · ${x.parts.length} parts</option>`).join('')}</select></label>
      </div>
      <div class="btree-edit">${rows(tpl, 'root', 0) || '<div class="muted">This item has no slots.</div>'}</div>
      <div class="bfoot"><span>${parts.length} part${parts.length === 1 ? '' : 's'} · handbook value <b>${money(total, CUR.RUB)}</b></span>
        ${miss.length ? `<span class="warn-line">⚠ Required: ${esc(miss.join(', '))}</span>` : ''}</div>`;
    m.querySelector('.btree-edit').scrollTop = y;
    const sin = m.querySelector('.bsearch');
    if (sin) { sin.focus(); sin.setSelectionRange(sin.value.length, sin.value.length); }
  };
  const result = openModal(`<div class="dialog build-dlg"><h2>🔧 Build: ${esc(itemName(tpl))}</h2><div class="bbody"></div>
      <div class="buttons"><button class="outline" data-bcancel>Cancel</button><button class="primary" data-bsave>Use This Build</button></div></div>`, mm => {
    m = mm;
    m.addEventListener('click', e => {
      const t = e.target.closest('[data-bopen],[data-bclear],[data-bset],[data-bsave],[data-bcancel]');
      if (!t) return;
      if (t.dataset.bopen) { open = open === t.dataset.bopen ? null : t.dataset.bopen; query = ''; draw(); }
      else if (t.dataset.bclear) { const [pid, slot] = t.dataset.bclear.split('|'); set(pid, slot, null); }
      else if (t.dataset.bset) { const [pid, slot] = t.dataset.bset.split('|'); set(pid, slot, t.dataset.tpl); }
      else if (t.hasAttribute('data-bsave')) closeModal({ parts, name });
      else closeModal(null);
    });
    m.addEventListener('input', e => {
      if (e.target.matches('.bsearch')) { query = e.target.value; draw(); }
      else if (e.target.matches('.bname-in')) name = e.target.value;
    });
    m.addEventListener('change', e => {
      if (!e.target.matches('.bfrom') || !e.target.value) return;
      if (e.target.value === 'empty') parts = [];
      else { const src = sources[Number(e.target.value)]; if (!src) return; parts = JSON.parse(JSON.stringify(src.parts)); if (!name) name = src.name; }
      open = null; ensure();
    });
    m.addEventListener('keydown', e => { if (e.key === 'Escape' && open) { e.stopPropagation(); open = null; draw(); } }, true);
    ensure();
  });
  const r = await result;
  if (!r) return;
  o.parts = r.parts; o.buildName = r.name.trim();
  if (o.parts.length) o.useDefaultPreset = false;
  changed(true);
  toast(o.parts.length ? `Build saved: ${o.parts.length} part(s) — Save to write it` : 'Build removed');
}

Object.assign(ACT, {
  editBuild() { if (S.offer) editBuild(S.offer); },
  clearBuild() { if (!S.offer) return; S.offer.parts = []; S.offer.buildName = ''; changed(true); },
  useBuildPrice() {
    const o = S.offer; if (!o) return;
    const cur = traderMoney(), v = buildValue(o, 'h');
    const line = o.cost.find(c => isMoney(c.itemTpl));
    const count = toCurrency(v, cur);
    if (line) { line.itemTpl = cur; line.count = count; } else o.cost.unshift({ itemTpl: cur, count });
    if (o.priceMax > 0) { o.priceMin = Math.max(1, Math.round(count * 0.8)); o.priceMax = Math.max(o.priceMin, Math.round(count * 1.2)); }
    changed(true);
  },
});
