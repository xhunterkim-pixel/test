'use strict';
/* Modern Editor — quest texts: ✨ Generate and "Keep Text Up to Date".
   Written the way Tarkov's traders talk: short, in character, a story instead of a checklist (the game lists the
   objectives under the text anyway). What goes in:
     · an opener that knows the quest chain ("You really thought one job was enough?") and the player's level
     · why the trader wants it — from the items asked for, the targets and the maps
     · the job in one plain line ("Bring me three Killa helmets."), several ways as "…or… your call"
     · the stakes and the pay: what it unlocks in the shop, money, items, standing
     · a sign-off with attitude, hardcore as a warning
   The trader's voice comes from what they sell (medic, gunsmith, outfitter, fixer, broker).
   The same quest + seed always gives the same text; ✨ counts the seed up for another version. */

// ---------------------------------------------------------------- small helpers

/** A repeatable random series from a text (the quest id + its seed). */
function seededRandom(text) {
  let h = 2166136261;
  for (let i = 0; i < text.length; i++) h = Math.imul(h ^ text.charCodeAt(i), 16777619);
  return () => {
    h = Math.imul(h ^ (h >>> 15), 2246822507);
    h = Math.imul(h ^ (h >>> 13), 3266489909);
    h ^= h >>> 16;
    return (h >>> 0) / 4294967296;
  };
}
const capFirst = text => text.charAt(0).toUpperCase() + text.slice(1);
const oneOf = (rnd, list) => list[Math.floor(rnd() * list.length) % list.length];
const andList = list => list.length <= 1 ? (list[0] || '') : `${list.slice(0, -1).join(', ')} and ${list[list.length - 1]}`;
const orList = list => list.length <= 1 ? (list[0] || '') : `${list.slice(0, -1).join(', ')} or ${list[list.length - 1]}`;
const fillIn = (text, words) => text.replace(/\{(\w+)\}/g, (m, k) => words[k] ?? '');
const sentence = s => { s = s.trim(); if (!s) return ''; s = capFirst(s); return /[.!?…]$/.test(s) ? s : s + '.'; };
const NUMBER_WORDS = ['', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'eleven', 'twelve'];
const countWord = n => NUMBER_WORDS[n] || fmt(n);
/** Item names the way a trader says them: the short name for long names. */
const nameOf = id => { const it = item(id); if (!it) return shortName(id); return it.n.length > 28 && it.s ? it.s : it.n; };
/** "5,000,000 roubles", "2,000 dollars". */
const moneySaid = (tpl, n) => `${fmt(n)} ${({ [CUR.RUB]: 'roubles', [CUR.USD]: 'dollars', [CUR.EUR]: 'euros' })[tpl] || (MONEY_NAME[tpl] || moneyShort(tpl))}`;
/** "80 rounds of M855", "three Salewa", "a Salewa". */
const countSaid = (id, n) => itemGroup(id) === 'Ammo' && n > 1 ? `${fmt(n)} rounds of ${nameOf(id)}` : n > 1 ? `${countWord(n)} ${nameOf(id)}` : `a ${nameOf(id)}`;
const itemsSaid = (ids, n) => {
  const list = ids.filter(id => !isMoney(id));
  if (!list.length) return 'the goods';
  return list.length === 1 ? countSaid(list[0], n) : `${n > 1 ? countWord(n) + ' of ' : ''}${orList(list.map(nameOf))}`;
};
const mapsSaid = c => c.locations?.length ? orList(c.locations.map(mapName)) : '';

// ---------------------------------------------------------------- the trader's voice (from what they sell)

const VOICES = {
  medic: {
    title: ['my dear', 'colleague', 'young man'],
    first: ['Another new face. Sit down, and please don\'t bleed on anything.', 'So you\'re the one they sent. You look healthy enough — good, I need someone who can walk.'],
    back: ['Back again? Good. The clinic doesn\'t run itself.', 'You helped me before, so I\'ll be direct.'],
    bye: ['Take care of yourself out there. I have enough patients already.', 'Come back in one piece. I\'m running low on sutures.', 'The sooner you go, the more people I can save.'],
    thanks: ['You have no idea how many people this will help.', 'Good. The patients will live another day thanks to you.', 'Thank you. Truly — this changes things here.'],
  },
  gunsmith: {
    title: ['kid', 'soldier', 'shooter'],
    first: ['You want real hardware? Everyone does. Prove you know which end the bullet comes out of.', 'Walk in here with nothing and expect my good stuff? No. You earn it, like everyone else.'],
    back: ['Not bad last time. But I don\'t hand out rifles for "not bad".', 'You again. Fine — you\'ve shown you can shoot. Let\'s see if you can think.'],
    bye: ['Don\'t keep me waiting.', 'Get it done, and my crates open up for you.', 'Refuse, and… well, you know where the door is.'],
    thanks: ['Clean work. I like that.', 'Now that\'s how it\'s done.', 'Ha. Maybe you\'re worth arming properly after all.'],
  },
  outfitter: {
    title: ['friend', 'operator', 'mercenary'],
    first: ['You look under-dressed for Tarkov. Let\'s fix that — but not for free.', 'Everyone wants plates. Nobody wants to work for them.'],
    back: ['Your last job held up. So does my gear, when people earn it.', 'You\'re still breathing, which says something. Here\'s the next one.'],
    bye: ['Suit up and get moving.', 'Don\'t come back with holes in you — or at least not in my gear.', 'The sooner it\'s done, the sooner you\'re kitted out.'],
    thanks: ['Good job. You\'ve earned a proper kit.', 'That\'s what I call reliable.', 'Excellent. My shelves are yours to browse.'],
  },
  fixer: {
    title: ['buddy', 'partner', 'kid'],
    first: ['Close the door. Whatever we talk about stays between us, got it?', 'You and me could make some real money together. But first, a little test.'],
    back: ['You really thought one favour made us partners? Not yet, buddy. Not yet.', 'You kept your mouth shut last time. I like that. Here\'s something bigger.'],
    bye: ['Remember — you never heard this from me.', 'Quiet and quick, that\'s how I like it.', 'Do this right, and there\'s plenty more where that came from.'],
    thanks: ['Pleasure doing business.', 'Nice. Nobody saw anything, right? Good.', 'Now that\'s how deals get done in this city.'],
  },
  broker: {
    title: ['kid', 'mercenary', 'friend'],
    first: ['So you\'re the one everyone\'s been talking about? We\'ll see about that.', 'New face. Everyone starts somewhere — usually at the bottom.'],
    back: ['You really thought I\'d just take you on after a few completed jobs? No, kid. You\'ve still got some running to do.', 'Not bad so far. But that was the easy part.'],
    bye: ['It\'s all in your hands.', 'Refuse, and… well, you get the idea.', 'Don\'t make me regret picking you.'],
    thanks: ['Good work. I won\'t forget this.', 'Well, well. You actually pulled it off.', 'Exactly what I needed. You\'re moving up, kid.'],
  },
};
function traderVoice(t) {
  const count = {};
  for (const o of t.file.offers) { const g = itemGroup(o.itemTpl); count[g] = (count[g] || 0) + 1; }
  const sum = keys => keys.reduce((n, k) => n + (count[k] || 0), 0);
  const scores = {
    medic: sum(['Medical', 'Food']),
    gunsmith: sum(['Weapons', 'WeaponParts', 'Ammo', 'Grenades', 'Melee']),
    outfitter: sum(['Armor', 'Headwear', 'Rigs', 'Backpacks', 'Gear']),
    fixer: sum(['Barter', 'Keys', 'Electronics', 'Containers', 'Special']),
  };
  const best = Object.entries(scores).sort((a, b) => b[1] - a[1])[0];
  return best && best[1] > 0 ? best[0] : 'broker';
}

// ---------------------------------------------------------------- why the trader wants it

const WHY = {
  Medical: ['The clinic shelves are empty, and the wounded keep coming.', 'Half the camp has infections, and I have nothing left to treat them with.', 'The supply runs from the port stopped weeks ago.'],
  Food: ['People here are hungry, and hungry people do stupid things.', 'Food is scarcer than bullets these days.', 'Nobody fights well on an empty stomach, and my people haven\'t eaten in days.'],
  Weapons: ['I\'ve got a buyer who pays well and asks no questions.', 'My guards are carrying junk. That has to change.', 'A client of mine is arming up for something big.'],
  WeaponParts: ['I\'m building something special for a client, and I\'m short on parts.', 'The workshop needs parts, and the usual suppliers have gone quiet.'],
  Ammo: ['Rounds are currency out there, and my stock is running dry.', 'The next ammo delivery isn\'t coming. Somebody has to fix that.'],
  Armor: ['My people need protection, and plates don\'t grow on trees.', 'Somebody has to keep my couriers alive.'],
  Headwear: ['I lost three men to headshots last week. That ends now.', 'A good helmet is worth more than a good gun in this city.'],
  Rigs: ['My runners need something to carry their magazines in, and it\'s not going to be plastic bags.'],
  Backpacks: ['Couriers need room to carry, and I need couriers.'],
  Electronics: ['I\'m putting together some equipment of my own.', 'The comms are down, and I need parts to bring them back.'],
  Barter: ['Little things like these keep the whole city running — if you know who to sell them to.', 'I\'ve got a deal lined up. I just need the goods.'],
  Keys: ['There\'s a room I\'d very much like to see the inside of.'],
  Other: ['I have my reasons, and they\'re not your concern.', 'Don\'t ask why. Just know it matters.'],
};
const WHY_KILL = {
  Pmc: ['Those PMC dogs have been shooting up my couriers{on}.', 'The PMCs think this city belongs to them. Remind them it doesn\'t.', 'Somebody\'s been ambushing my people{on}, and my money\'s on the PMCs.'],
  Usec: ['The USEC boys have gotten cocky{on}. Knock them down a peg.', 'USEC has been poking around my business{on}.'],
  Bear: ['BEAR operators have been hunting my people{on}. Return the favour.', 'The BEARs have set up shop{on}, and I want them gone.'],
  Savage: ['The scavs have gotten bold{on} — they robbed one of my runners yesterday.', 'Scav gangs are choking the roads{on}.'],
  Boss: ['{boss} has been a thorn in my side for far too long.', 'Everyone\'s afraid of {boss}. Show them there\'s no reason to be.'],
  Any: ['Things have gotten out of hand{on}.', 'I need the area cleared{on}, whoever\'s in the way.'],
};
const WHY_OTHER = {
  Extract: ['Plenty of people go into Tarkov. Few come back out. I need one who does.', 'Anyone can walk in. Walking out is the hard part.'],
  Find: ['I need to know they\'re still out there before I send anyone after them.', 'A client wants proof these still exist.'],
  Use: ['I need to know how these hold up in the field, not on paper.', 'Field testing — somebody has to do it.'],
  Skill: ['Talent is nothing without practice.', 'I only work with people who know their craft.'],
  Pay: ['Nothing in this city is free — not even a favour from me.', 'Doors open for money. This one too.'],
};

// ---------------------------------------------------------------- the job in one plain line

function killWhoSaid(c) {
  const n = c.count;
  if (c.killTarget === 'Boss') return c.bossRoles?.length ? orList(c.bossRoles.map(bossName)) : n === 1 ? 'a boss' : `${countWord(n)} bosses`;
  const [one, many] = { AnyPmc: ['a PMC', 'PMCs'], Usec: ['a USEC operator', 'USEC operators'], Bear: ['a BEAR operator', 'BEAR operators'], Savage: ['a scav', 'scavs'] }[c.killTarget] || ['a hostile', 'hostiles'];
  return n === 1 ? one : `${countWord(n)} ${many}`;
}
/** The main thing to do, said plainly — the details (ammo, distance, gear…) are shown by the game under the text. */
/** "an M4A1", "a Kedr": by the sound of the first letter (letters read out like M, S, X take "an"). */
const aOrAn = w => /^[aeiou]/i.test(w) || /^[AEFHILMNORSX](?![a-z])/.test(w) ? 'an' : 'a';
function jobSaid(c, rnd, short = false) {
  const where = mapsSaid(c);
  switch (c.type) {
    case 'Kill': {
      const guns = [...(c.weaponTpls || []).map(nameOf)];
      let s = `${oneOf(rnd, ['take out', 'put down', 'deal with'])} ${killWhoSaid(c)}${where ? ` on ${where}` : ''}`;
      if (guns.length === 1) s += ` with ${aOrAn(guns[0])} ${guns[0]}`;
      if (c.bodyParts?.length === 1 && c.bodyParts[0] === 'Head') s += ' — headshots only';
      if (c.oneRaid) s += ', all in one raid';
      return s;
    }
    case 'Extract': return `${c.count > 1 ? `get out alive ${countWord(c.count)} times` : 'get in and get back out alive'}${where ? ` from ${where}` : ''}`;
    case 'UseItem': return `use ${itemsSaid(c.itemTpls, c.count)}${where ? ` on ${where}` : ''}`;
    case 'FindItem': return `find ${itemsSaid(c.itemTpls, c.count)} out there`;
    case 'Skill': return `get your ${skillName(c.skill)} up to level ${c.count}`;
    default:
      if (isMoneyList(c.itemTpls)) return `${oneOf(rnd, ['pay me', 'bring me'])} ${moneySaid(c.itemTpls[0], c.count)}`;
      return `bring me ${itemsSaid(c.itemTpls, c.count)}${c.foundInRaid ? (short ? ', found in raid' : oneOf(rnd, [' — found in raid, not some flea market junk', ', found in raid. I\'ll know if they aren\'t'])) : ''}`;
  }
}
function whyLine(q, rnd) {
  const c = q.conditions[0];
  if (!c) return '';
  if (c.type === 'Kill') {
    const on = mapsSaid(c) ? ` on ${mapsSaid(c)}` : '';
    const key = { Boss: 'Boss', Savage: 'Savage', Usec: 'Usec', Bear: 'Bear', AnyPmc: 'Pmc' }[c.killTarget] || 'Any';
    return fillIn(oneOf(rnd, WHY_KILL[key]), { on: '', boss: c.bossRoles?.length ? bossName(c.bossRoles[0]) : 'That boss' });
  }
  const other = { Extract: 'Extract', FindItem: 'Find', UseItem: 'Use', Skill: 'Skill' }[c.type] || (isMoneyList(c.itemTpls) ? 'Pay' : null);
  if (other) return oneOf(rnd, WHY_OTHER[other]);
  return oneOf(rnd, WHY[itemGroup(c.itemTpls[0])] || WHY.Other);
}

// ---------------------------------------------------------------- what the player gets

function rewardSaid(t, q, rnd, done) {
  const unlocks = [], barters = [], pay = [];
  let standing = 0;
  for (const r of q.rewards) {
    if (r.onStart) continue;
    if (r.type === 'UnlockOffer') { const o = t.file.offers.find(x => x.id === r.offerId); if (o) (isBarter(o) ? barters : unlocks).push(nameOf(o.itemTpl)); }
    else if (r.type === 'Item') pay.push(isMoney(r.itemTpl) ? moneySaid(r.itemTpl, r.count) : countSaid(r.itemTpl, r.count));
    else if (r.type === 'TraderStanding') standing += Number(r.value) || 0;
  }
  const shop = [...unlocks, ...barters];
  const parts = [];
  if (done) {
    if (shop.length) parts.push(oneOf(rnd, [`${capFirst(andList(shop))} ${shop.length > 1 ? 'are' : 'is'} on my shelf for you now.`, `From today, ${andList(shop)} ${shop.length > 1 ? 'are' : 'is'} yours to buy.`]));
    if (pay.length) parts.push(oneOf(rnd, [`Here's your cut: ${andList(pay)}.`, `${capFirst(andList(pay))}, as agreed. Count it if you like.`]));
  } else {
    if (shop.length) parts.push(oneOf(rnd, [`Help me out, and ${andList(shop)} ${shop.length > 1 ? 'go' : 'goes'} on my shelf — for you.`, `Do this, and I'll start selling you ${andList(shop)}.`]));
    if (pay.length) parts.push(oneOf(rnd, [`There's ${andList(pay)} in it for you.`, `You'll walk away with ${andList(pay)}.`]));
    if (standing > 0 && !parts.length) parts.push(oneOf(rnd, ['Help me out, and you\'ll move up fast.', 'Do this right, and we\'ll be doing a lot more business.']));
    if (standing < 0) parts.push('Some of my partners won\'t like it — that\'s my problem, not yours.');
  }
  return parts.join(' ');
}

// ---------------------------------------------------------------- the texts

function textRandom(q, field) { return seededRandom(`${q.id}|${q.textSeed || 0}|${field}`); }

function genDescription(t, q) {
  const rnd = textRandom(q, 'description');
  const voice = VOICES[traderVoice(t)];
  const w = ways(q);
  const prev = q.prerequisiteQuestIds.map(id => allQuests().find(x => x.q.id === id)?.q?.name || S.gameQuests?.get(id)?.n).filter(Boolean)[0];
  const out = [];
  // who we are to each other
  if (prev) out.push(oneOf(rnd, [...voice.back, `"${prev}" went well. Don't let it go to your head.`]));
  else if (q.minLevel >= 30) out.push(oneOf(rnd, ['You\'ve been around long enough to know how this city works.', 'This isn\'t a job for a rookie. Lucky for you, you\'re not one anymore.']));
  else out.push(oneOf(rnd, voice.first));
  // why
  out.push(whyLine(q, rnd));
  // the job: one line, several ways as "…, or …. Your call."
  if (q.conditions.length) {
    if (w.length === 1) {
      const main = q.conditions.map(c => jobSaid(c, rnd));
      out.push(sentence(main.length > 2 ? `${main.slice(0, 2).join(', then ')} — and a couple more things, you'll see` : main.join(', then ')));
    } else {
      // one short sentence per way: "Bring me… Or take out… Or, if you've got the money, pay me…. Your call."
      const firsts = w.map(o => jobSaid(q.conditions.find(c => (c.option || 1) === o), rnd, true));
      out.push(firsts.map((j, i) => i === 0 ? sentence(j) : sentence(`${i === firsts.length - 1 && firsts.length > 2 ? 'or, if you\'d rather,' : 'or'} ${j}`)).join(' ')
        + ' ' + oneOf(rnd, ['Your call.', 'I don\'t care how, as long as it gets done.', 'Pick whichever suits you.']));
    }
  } else out.push('I\'ll tell you the details when you\'re ready.');
  // the stakes
  const reward = rewardSaid(t, q, rnd, false);
  if (reward) out.push(reward);
  if (q.failOnDeath) out.push(oneOf(rnd, ['And don\'t go dying on me — get killed, go missing or run from a raid, and we start over.', 'One more thing: this only counts if you come back alive every time. Die, and it\'s back to square one.']));
  out.push(oneOf(rnd, voice.bye));
  return out.filter(Boolean).join(' ');
}

function genSuccess(t, q) {
  const rnd = textRandom(q, 'success');
  const voice = VOICES[traderVoice(t)];
  const c = ways(q).length === 1 ? q.conditions[0] : null;   // several ways: no telling which one was done
  let what = '';
  if (c?.type === 'Kill') what = oneOf(rnd, ['Word travels fast — I already heard.', 'I heard the shooting from here.']);
  else if (c && ['HandoverItem', 'FindItem'].includes(c.type) && !isMoneyList(c.itemTpls)) what = oneOf(rnd, [`${capFirst(itemsSaid(c.itemTpls, c.count))}. Exactly what I needed.`, 'Put them over there. Careful.']);
  else if (c?.type === 'Extract') what = 'You made it back. Not everyone does.';
  const out = [what, oneOf(rnd, voice.thanks), rewardSaid(t, q, rnd, true)];
  if (rnd() < .6) out.push(oneOf(rnd, [`See you around, ${oneOf(rnd, voice.title)}.`, 'Come back soon — there\'ll be more work.', 'Don\'t let it go to your head.']));
  return out.filter(Boolean).join(' ');
}

// ---------------------------------------------------------------- keeping the texts up to date

/** Quests with "Keep Text Up to Date" on get new texts whenever something on them changed (called on every edit). */
function refreshAutoTexts(t) {
  for (const q of t?.file.quests || []) {
    if (!q.autoText) continue;
    const d = genDescription(t, q), s = genSuccess(t, q);
    if (q.description !== d) q.description = d;
    if (q.successMessage !== s) q.successMessage = s;
  }
}
