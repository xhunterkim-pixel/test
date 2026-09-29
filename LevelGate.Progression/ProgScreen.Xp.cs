using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// The XP animation. The screen remembers the total XP it last showed, per character (SeenState); when the profile has
    /// more on open, it plays out in beats. Everything but the active part dims, so the eye knows where to look; a warm light
    /// sweeps from one part to the next between beats.
    ///
    ///   1. EARN   "LEVEL 23 → 40" under the numbers, a "+N" pool after the EXP tag that drains as the bar eats it. The bar
    ///             fills (the part just earned glows); Levelup.mp3 starts so its peak lands on the pop; the new number rolls
    ///             in from below as it lands, the square pops, "LEVEL UP" rises over it; the full bar flashes, clears and starts
    ///             over. Each level a little quicker than the last and the sound a little higher (every 3 levels, up to +4
    ///             semitones); the last level up pops bigger and flashes the whole block.
    ///   2. RANK   (a new rank only) Emblemup.mp3: the glow pulses on its two early hits, the emblem swaps on its peak with a
    ///             ring bursting out, the new rank name rises into place.
    ///   3. UNLOCK the cards page to the first new level; left to right each flashes, punches, "+N ITEMS" floats up out of it,
    ///             LOCKED turns into NEW and CURRENT walks along (quicker when it spans more than 2 pages).
    ///   4. SUMMARY a banner over the cards: "17 LEVELS · 254 ITEMS UNLOCKED" (+ the new rank).
    ///   5. LAND   the list and the preview fade over to your new level (its pictures were loaded while it played).
    ///
    /// Space: tap = the next moment, hold = the next part. A click = a tap (not in the first 0.8 s, nor as F12 closes). Esc closes.
    /// </summary>
    internal static partial class ProgScreen
    {
        private enum StepKind { Fill, LevelUp, Pause, Rank, Page, Unlock, Banner, Land }

        private struct XpStep
        {
            public StepKind Kind;
            public int Level;        // Fill: level being filled · LevelUp / Unlock / Land: that level · Rank: the new rank's level · Page: page
            public float From, To;   // Fill: bar 0..1
            public float Dur;
            public bool Loud;        // Fill: starts the level-up sound so its peak lands on the next level up
            public float Pitch;      // Fill (loud): the level-up sound's pitch
            public bool Final;       // LevelUp: the last one
        }

        private static readonly List<XpStep> _xpSteps = new List<XpStep>();
        private static bool _xpPending, _xpRunning;
        private static int _xpFrom, _xpTo, _xpStep, _xpLa, _xpLb, _xpLand, _xpItems;
        private static float _xpT, _xpPop = -1, _xpRankPop = -1, _xpTickAt = -10, _xpStartedAt, _cmSeenAt = -10;
        private static bool _xpRankSwapped, _xpPopBig;
        private static Component _xpGain, _xpBig;
        private static Image _xpRankGlow;
        private static CanvasGroup _headTextGroup;

        /// <summary>While the unlock beat hasn't reached them, the cards show this level as yours (0: your real level).</summary>
        private static int _xpCardLevel;
        /// <summary>The unlock beat pages the cards without selecting a level on each page.</summary>
        private static bool _xpPaging;

        private static bool XpAnimating => _xpPending || _xpRunning;

        /// <summary>On open: plays only if the profile has more XP than the screen last showed (and the setting is on).</summary>
        private static void PrepareXpAnim()
        {
            _xpPending = _xpRunning = false;
            _xpCardLevel = 0; _xpSim = false; _simLevel = 0;
            ResetXpVisuals();
            int now = ProgData.TotalExp(), shown = SeenState.Xp;
            if (now < 0 || shown < 0 || !ProgData.HasExpTable) return; // not known yet: next time (the baseline stays)
            if (!ProgressionPlugin.XpAnimation.Value || now <= shown)
            {
                if (shown != now) { SeenState.Xp = now; if (shown > now) L.Info($"xp: shown XP {shown} > profile {now} — reset"); }
                return;
            }
            int la = ProgData.LevelOfExp(shown), lb = ProgData.LevelOfExp(now);
            if (la <= 0 || lb <= 0) return;
            _xpFrom = shown; _xpTo = now; _xpLa = la; _xpLb = lb;
            BuildXpSteps(shown, now, la, lb);
            _xpPending = true;
            // the screen opens on the old state: your old level, its page, the cards before the level ups
            if (lb > la)
            {
                _xpCardLevel = la;
                _level = la; _page = (la - 1) / PerPage;
            }
            ShowXpAt(shown, la);
            UpdateHeader(la);
            L.Info($"xp: +{Thousands(now - shown)} EXP since the screen last showed it ({shown} → {now}); level {la} → {lb}; {_xpSteps.Count} step(s), ~{TotalDur():0.0} s");
        }

        private static void ResetXpVisuals()
        {
            _xpPop = _xpRankPop = _rollT = _barResetT = _luT = -1;
            if (_xpSquare != null) { _xpSquare.rectTransform.localScale = Vector3.one; _xpSquare.color = Ui.Hex(Orange); }
            var fill = _xpFill != null ? _xpFill.GetComponent<Image>() : null;
            if (fill != null) fill.color = XpFillColor;
            if (_headBadge != null) _headBadge.Root.localScale = Vector3.one;
            if (_xpRankGlow != null) _xpRankGlow.color = new Color(0, 0, 0, 0);
            if (_rankRing != null) _rankRing.color = new Color(0, 0, 0, 0);
            if (_headTextGroup != null) { _headTextGroup.alpha = 1; ((RectTransform)_headTextGroup.transform).anchoredPosition = Vector2.zero; }
            if (_xpCaption != null) { Ui.SetText(_xpCaption, "CURRENT LEVEL"); _xpCaption.gameObject.SetActive(true); }
            if (_xpGain != null) _xpGain.gameObject.SetActive(false);
            if (_xpBig != null) _xpBig.gameObject.SetActive(false);
            if (_xpLevelOld != null) _xpLevelOld.gameObject.SetActive(false);
            if (_xpLevel != null) ((RectTransform)_xpLevel.transform).anchoredPosition = Vector2.zero;
            if (_xpLevelUp != null) _xpLevelUp.gameObject.SetActive(false);
            if (_xpBlockFlash != null) _xpBlockFlash.color = new Color(1, 1, 1, 0);
            if (_banner != null) _banner.SetActive(false);
            _rankFade = 1; _headFocus = 1;
            SetFocus(Focus.None, true);
            ShowPrompt(false, true);
        }

        private static float TotalDur() { float t = 0; foreach (var s in _xpSteps) t += s.Dur; return t; }

        private static void Add(StepKind kind, float dur, int level = 0, float from = 0, float to = 0, bool loud = false, float pitch = 1, bool final = false)
            => _xpSteps.Add(new XpStep { Kind = kind, Dur = dur, Level = level, From = from, To = to, Loud = loud, Pitch = pitch, Final = final });

        /// <summary>The level-up sound's pitch for the k-th level up (0-based): +1 semitone every 3, at most +4.</summary>
        private static float PitchFor(int k) => Mathf.Pow(2f, Mathf.Min(4, k / 3) / 12f);

        private static void BuildXpSteps(int from, int to, int la, int lb)
        {
            _xpSteps.Clear();
            int levels = lb - la;
            float Frac(int exp, int level) => ProgData.ExpInLevel(exp, level, out int h, out int n) ? Mathf.Clamp01(h / (float)n) : 1f;
            float fa = Frac(from, la), fb = Frac(to, lb);
            _xpItems = 0;
            for (int l = la + 1; l <= lb; l++) _xpItems += ProgData.CountAt(l);
            // land on the new level — or the nearest one before it that has rewards (an empty list is no way to end)
            _xpLand = lb;
            for (int l = lb; l > la; l--) if (ProgData.CountAt(l) > 0) { _xpLand = l; break; }

            // ---- 1. EARN
            if (levels == 0)
            {
                Add(StepKind.Fill, Mathf.Clamp(.5f + (fb - fa) * 1.2f, .6f, 1.6f), la, fa, fb);
                return;
            }
            // every level up gets its moment; the sound's peak lands on the pop (the fill starts it that far ahead). Each fill
            // is 4% quicker than the one before (down to 0.45 s) and every 3rd level the sound goes a semitone higher: the run
            // builds instead of repeating.
            float lead0 = Sfx.Lead("levelup", PitchFor(0));
            Add(StepKind.Fill, Mathf.Max(lead0 + .15f, .9f * (1 - fa)), la, fa, 1, loud: true, pitch: PitchFor(0));
            for (int i = 1; i <= levels; i++)
            {
                int l = la + i;
                bool last = l == lb;
                Add(StepKind.LevelUp, last ? .7f : .55f, l, final: last);
                if (!last)
                {
                    float p = PitchFor(i);
                    Add(StepKind.Fill, Mathf.Max(.45f, Sfx.Lead("levelup", p) + .1f, .7f * Mathf.Pow(.96f, i)), l, 0, 1, loud: true, pitch: p);
                }
                else
                {
                    float fill = fb > 0 ? Mathf.Clamp(.35f + fb * .8f, .35f, 1.1f) : 0;
                    if (fill > 0) Add(StepKind.Fill, fill, l, 0, fb);
                    // the sound plays out, then a short breath (0.4 s) before the next beat
                    float p = PitchFor(levels - 1);
                    float rest = (Sfx.LevelUpLength - Sfx.LevelUpPeak) / p - .7f - fill;
                    Add(StepKind.Pause, Mathf.Max(0, rest) + .4f);
                }
            }

            // ---- 2. RANK (only when it changed): Emblemup.mp3 starts, the glow answers its two early hits, the swap lands on its
            // peak, the settle runs to ~2.35 s — then 1.3 s before the cards
            if (TierOf(lb).Name != TierOf(la).Name) Add(StepKind.Rank, RankLead + Sfx.Extra("emblemup") + 1.1f + 1.3f, lb);

            // ---- 3. UNLOCK: every new level's card, left to right, page by page (quicker over many pages)
            int pages = (lb - 1) / PerPage - la / PerPage + 1;
            bool many = pages > 2;
            float each = many ? .18f : levels <= 5 ? .34f : .24f, turn = many ? .35f : .55f;
            int page = (la - 1) / PerPage;
            for (int l = la + 1; l <= lb; l++)
            {
                int p = (l - 1) / PerPage;
                if (p != page) { Add(StepKind.Page, turn, p); page = p; }
                Add(StepKind.Unlock, each, l);
            }

            // ---- 4. SUMMARY, 5. LAND
            Add(StepKind.Pause, .35f);
            Add(StepKind.Land, .45f, _xpLand); // (no summary banner: it didn't fit the game)
        }

        /// <summary>From the emblem sound's start to the moment the emblem starts to shrink (its swap then lands on the peak).</summary>
        private const float RankLead = Sfx.EmblemPeak - .2f;

        private static void PlayLevelUp(float pitch)
        {
            if (Sfx.UseGame || !Sfx.Play("levelup", pitch)) Sounds.Play("QuestCompleted", "QuestFinished", "TradeOperationComplete", "ButtonClick");
        }

        /// <summary>Runs the animation (every frame while open). True while it plays: the screen's own keys wait.</summary>
        private static bool RunXpAnim()
        {
            var input = UnityInput.Current;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            if (ProgressionPlugin.ConfigWindowOpen()) _cmSeenAt = Time.unscaledTime;
            AnimatePops(dt);
            TickFocus(dt);
            if (_xpPending)
            {
                if (Time.unscaledTime - _openedAt < .45f) return true; // the screen fades in first
                if (_xpSim && ProgressionPlugin.ConfigWindowOpen()) return true; // a preview from F12: waits until F12 is closed
                _xpPending = false; _xpRunning = true; _xpStep = 0; _xpT = 0; _xpEntered = -1; _spaceDown = -1;
                _xpStartedAt = Time.unscaledTime;
                ShowGain(true);
                ShowPrompt(true);
                if (_xpLb > _xpLa) Ui.SetText(_xpNext, $"LEVEL <color=#d5d9d6>{_xpLa}</color>  →  <color={Orange}>{_xpLb}</color>");
                PreloadLanding();
                L.Info($"xp: {(_xpSim ? "preview" : "animation")} started (~{TotalDur():0.0} s)");
            }
            if (!_xpRunning) { FadeGain(); return false; }
            if (input.GetKeyDown(KeyCode.Escape)) { Close("Escape"); return true; }
            // a click = a tap — but not in the first 0.8 s, and not the click that closed F12 (it cost two previews at once)
            if (input.GetMouseButtonDown(0) && Time.unscaledTime - _xpStartedAt > .8f && Time.unscaledTime - _cmSeenAt > .3f) SkipAhead(false);
            // Space: tap = the next moment (the next level up, the rank, the cards, the summary, the end); hold = on to the next part
            if (input.GetKeyDown(KeyCode.Space)) { _spaceDown = Time.unscaledTime; _spaceHeld = false; }
            if (_spaceDown >= 0 && !_spaceHeld && input.GetKey(KeyCode.Space) && Time.unscaledTime - _spaceDown >= .35f) { _spaceHeld = true; SkipAhead(true); }
            if (_spaceDown >= 0 && input.GetKeyUp(KeyCode.Space)) { if (!_spaceHeld) SkipAhead(false); _spaceDown = -1; }
            if (!_xpRunning) return true;
            if (_xpStep >= _xpSteps.Count) { FinishXpAnim("done"); return false; }
            var st = _xpSteps[_xpStep];
            bool enter = _xpEntered != _xpStep;
            _xpEntered = _xpStep;
            if (enter) OnBeat(st);
            _xpT += dt;
            StepFrame(st, enter, false);
            if (_xpT >= st.Dur) { _xpStep++; _xpT = 0; }
            return true;
        }

        private static int _xpEntered = -1;
        private static float _spaceDown = -1;
        private static bool _spaceHeld;

        /// <summary>A step starting: sets the focus for its part, and sweeps the light over when the part changes.</summary>
        private static void OnBeat(XpStep st)
        {
            var f = FocusOf(_xpStep);
            if (f == _focusTarget) return;
            var from = FocusRect(_focusTarget);
            SetFocus(f);
            var to = FocusRect(f);
            if (from != null && to != null && f != Focus.None) Sweep(from, to);
        }

        private static Focus FocusOf(int i)
        {
            for (int j = i; j >= 0; j--)
            {
                switch (_xpSteps[j].Kind)
                {
                    case StepKind.Fill: case StepKind.LevelUp: return Focus.Xp;
                    case StepKind.Rank: return Focus.Header;
                    case StepKind.Page: case StepKind.Unlock: case StepKind.Banner: return Focus.Cards;
                    case StepKind.Land: return Focus.None;
                }
            }
            return Focus.Xp;
        }

        /// <summary>One frame of a step (quiet: skipped past — its end state without the show: no sound, pop or flash).</summary>
        private static void StepFrame(XpStep st, bool enter, bool quiet)
        {
            float k = Mathf.Clamp01(_xpT / Mathf.Max(.01f, st.Dur));
            switch (st.Kind)
            {
                case StepKind.Fill:
                {
                    if (enter) { _xpCued = false; _xpRolled = false; }
                    // a fill into a level up starts its sound so the peak lands on the pop
                    if (!quiet && st.Loud && !_xpCued && !Sfx.UseGame && st.Dur - _xpT <= Sfx.Lead("levelup", st.Pitch)) { _xpCued = true; PlayLevelUp(st.Pitch); }
                    bool intoLevelUp = _xpStep + 1 < _xpSteps.Count && _xpSteps[_xpStep + 1].Kind == StepKind.LevelUp;
                    // the new number rolls in so it lands on the pop
                    if (!quiet && intoLevelUp && !_xpRolled && st.Dur - _xpT <= RollTime) { _xpRolled = true; StartRoll(st.Level, st.Level + 1); }
                    float e = intoLevelUp ? k * (.6f + .4f * k) : 1 - Mathf.Pow(1 - k, 3); // into a level up: steady, leaning in · the last: slows into place
                    float frac = Mathf.Lerp(st.From, st.To, e);
                    // right after a Space jump: a quick catch-up instead of a cut
                    float sk = Time.unscaledTime - _skipAt;
                    if (!quiet && sk < _skipDur) frac = Mathf.Lerp(_skipFrom, frac, sk / _skipDur);
                    if (ProgData.ExpInLevel(0, st.Level, out _, out int need))
                    {
                        ShowXpState(st.Level, Mathf.RoundToInt(frac * need), need, true);
                        GainLeft(ProgData.ExpAtLevel(st.Level) + Mathf.RoundToInt(frac * need));
                    }
                    _xpFill.anchorMax = new Vector2(frac, 1);
                    if (!quiet) BarFx(st.From, frac);
                    PlaceGain();
                    break;
                }
                case StepKind.LevelUp:
                    if (!enter) break;
                    ProgData.ExpInLevel(0, st.Level, out _, out int n2);
                    ShowXpState(st.Level, 0, Mathf.Max(1, n2), n2 > 0);
                    GainLeft(ProgData.ExpAtLevel(st.Level));
                    UpdateHeader(st.Level, _xpLa); // the unlocked count climbs; the rank waits for its own beat
                    if (quiet) break;
                    // the full bar flashes, clears, then the next fill starts from empty
                    _xpFill.anchorMax = new Vector2(1, 1);
                    _barResetT = 0;
                    _xpPop = 0; _xpPopBig = st.Final;
                    _luT = 0;
                    if (st.Final) _xpBlockFlashT = 0;
                    if (Sfx.UseGame) PlayLevelUp(1);
                    ScreenFlash(Color.Lerp(Ui.Hex(TierOf(st.Level).Light), Color.white, .45f), .3f, Motion.D(Motion.Slow) * 1.5f); // MW 6
                    L.Debug($"xp: level up → {st.Level}");
                    break;
                case StepKind.Rank:
                    if (quiet) { EndRank(); break; }
                    RankMoment(st.Level, _xpT, st.Dur, RankLead + Sfx.Extra("emblemup")); // the new-rank moment in the F12 style (Full = MW4's splash)
                    if (enter)
                    {
                        _xpRankSwapped = false;
                        if (Sfx.UseGame || !Sfx.Play("emblemup")) _xpRankFallback = true; // game sound: plays at the swap
                        L.Info($"xp: new rank {TierOf(st.Level).Name}");
                    }
                    float lead = RankLead + Sfx.Extra("emblemup");
                    if (_xpRankPop < 0 && _xpT >= lead && !_xpRankSwapped)
                    {
                        _xpRankPop = 0;
                        if (_xpRankFallback) { _xpRankFallback = false; Sounds.Play("InsuranceInsured", "DeathmatchWin", "MenuDropdownSelect", "ButtonClick"); }
                    }
                    // the build-up: the emblem draws in a little, a faint glow gathers — and pulses on the sound's two early hits
                    if (_xpT < lead)
                    {
                        float u = _xpT / lead, pulse = 0;
                        foreach (var hit in Sfx.EmblemHits)
                        {
                            float d = _xpT - hit - Sfx.Extra("emblemup");
                            if (d >= 0 && d < .18f) pulse = Mathf.Max(pulse, Mathf.Sin(d / .18f * Mathf.PI));
                        }
                        _headBadge.Root.localScale = Vector3.one * (1 - .05f * u * u + .03f * pulse);
                        var glow = RankGlow();
                        if (glow != null) glow.color = new Color(1f, .45f, .2f, .22f * u * u + .2f * pulse);
                    }
                    // the swap happens while the emblem is smallest (see AnimatePops); the rank name fades across it
                    if (!_xpRankSwapped && _xpRankPop >= .2f) { _xpRankSwapped = true; UpdateHeader(_xpLb, _xpLb); }
                    if (_xpRankPop >= 0)
                    {
                        float inT = Mathf.Clamp01((_xpRankPop - .2f) / .45f);
                        _rankFade = _xpRankPop < .2f ? 1 - _xpRankPop / .2f : inT;
                        // the new name rises 8 px into place as it fades in
                        ((RectTransform)HeadText().transform).anchoredPosition = new Vector2(0, _xpRankPop < .2f ? 0 : -8f * Mathf.Pow(1 - inT, 3));
                    }
                    break;
                case StepKind.Page:
                    if (!enter) break;
                    _xpPaging = true;
                    try { ShowPage(st.Level, quiet ? 0 : 1); } finally { _xpPaging = false; }
                    break;
                case StepKind.Unlock:
                    if (!enter) break;
                    _xpCardLevel = st.Level;
                    UpdateSelection();
                    if (!quiet && st.Dur > 0)
                    {
                        int n = ProgData.CountAt(st.Level);
                        foreach (var c in _cards) if (c.Level == st.Level) c.Flash(); // (no "+N ITEMS" float: barely visible, looked cheap)
                        _flooded.Add(st.Level); // MW 2: stays lit until the screen closes
                        ScreenFlash(Ui.Hex(TierOf(st.Level).Light), .1f, Motion.D(Motion.Base)); // MW 6: a softer wash per card
                        if (Time.unscaledTime - _xpTickAt > .15f) { _xpTickAt = Time.unscaledTime; Sounds.Play("ButtonOver", "ButtonClick"); }
                    }
                    break;
                case StepKind.Banner:
                    if (quiet) { if (_banner != null) _banner.SetActive(false); break; }
                    if (enter) ShowBanner();
                    TickBanner(_xpT, st.Dur);
                    break;
                case StepKind.Land:
                    if (enter)
                    {
                        _landSwapped = false;
                        if (_banner != null) _banner.SetActive(false);
                        if (!quiet) Sounds.Click();
                    }
                    // the list and the preview fade out (0.15 s), switch to the new level, fade back in (0.25 s)
                    if (!_landSwapped && (quiet || _xpT >= .15f))
                    {
                        _landSwapped = true;
                        _xpCardLevel = _xpSim ? _xpLb : 0; // a preview keeps showing its level until the screen closes
                        if ((st.Level - 1) / PerPage != _page) { _xpPaging = true; try { ShowPage((st.Level - 1) / PerPage, 0); } finally { _xpPaging = false; } }
                        ShowLevel(st.Level, true);
                    }
                    LandFade(quiet ? 1 : _xpT < .15f ? 1 - _xpT / .15f : Mathf.Clamp01((_xpT - .15f) / .25f));
                    break;
            }
        }

        private static bool _landSwapped;

        private static void LandFade(float a)
        {
            if (_content != null)
            {
                var g = _content.GetComponent<CanvasGroup>() ?? _content.gameObject.AddComponent<CanvasGroup>();
                g.alpha = a;
            }
            if (_featPic != null) { var c = _featPic.color; c.a = a; _featPic.color = c; }
        }

        /// <summary>The landing level's pictures, asked for as the animation starts (they're ready when it lands).</summary>
        private static void PreloadLanding()
        {
            if (_xpLand <= 0 || _xpLb <= _xpLa) return;
            foreach (var job in PictureJobs(new[] { (_xpLand - 1) / PerPage })) _landJobs.Enqueue(job);
        }

        // ---------------------------------------------------------------- the bar

        // while it fills: the part just earned glows lighter with a bright leading edge; when the fill stops it melts into
        // the normal orange (so the eye can follow it going up)
        private static Image _barGhost, _barHead;
        private static float _barFxAt = -10, _barResetT = -1;

        private static void BarFx(float from, float to)
        {
            if (_barGhost == null)
            {
                var track = (RectTransform)_xpFill.parent;
                var g = Ui.Rect(track, "Earned", Vector2.zero, new Vector2(0, 1), new Vector2(2, 2), new Vector2(0, -2));
                _barGhost = Ui.Img(g, new Color(1, 1, 1, 0));
                var h = Ui.Rect(track, "Edge", new Vector2(0, 0), new Vector2(0, 1), new Vector2(-14, -6), new Vector2(10, 6));
                _barHead = Ui.Img(h, new Color(1, 1, 1, 0), Ui.Radial());
                _barGhost.raycastTarget = _barHead.raycastTarget = false;
            }
            var gr = _barGhost.rectTransform;
            gr.anchorMin = new Vector2(Mathf.Min(from, to), 0); gr.anchorMax = new Vector2(to, 1);
            var hr = _barHead.rectTransform;
            hr.anchorMin = new Vector2(to, 0); hr.anchorMax = new Vector2(to, 1);
            _barGhost.color = Color.Lerp(Ui.Hex(Orange), Color.white, .55f);
            _barHead.color = new Color(1f, .93f, .85f, .95f);
            _barFxAt = Time.unscaledTime;
        }

        private static void FadeBarFx()
        {
            if (_barGhost == null || _barFxAt < 0) return;
            float t = Time.unscaledTime - _barFxAt;
            if (t < .05f) return; // still filling
            float a = Mathf.Clamp01(1 - (t - .05f) / .45f);
            _barGhost.color = Color.Lerp(Ui.Hex(Orange, 0), Color.Lerp(Ui.Hex(Orange), Color.white, .55f), a);
            _barHead.color = new Color(1f, .93f, .85f, .95f * a);
            if (a <= 0) _barFxAt = -10;
        }

        /// <summary>The rank beat's end state (skipped into or past it).</summary>
        private static void EndRank()
        {
            _xpRankPop = -1; _xpRankSwapped = true; _rankFade = 1;
            UpdateHeader(_xpLb, _xpLb);
            if (_headBadge != null) _headBadge.Root.localScale = Vector3.one;
            if (_xpRankGlow != null) _xpRankGlow.color = new Color(0, 0, 0, 0);
            if (_headTextGroup != null) ((RectTransform)_headTextGroup.transform).anchoredPosition = Vector2.zero;
            if (_rankRing != null) _rankRing.color = new Color(0, 0, 0, 0);
        }

        // ---------------------------------------------------------------- Space / click

        private static float _skipAt = -10, _skipDur = .12f, _skipFrom;

        /// <summary>
        /// Tap: on to the next moment — the next level up (arriving as its sound starts, so the peak still lands on the pop),
        /// then the rank, the cards, the summary, the end. Hold: on to the next part. Everything skipped past gets its end
        /// state quietly, the bar catches up in 0.12 s (0.2 s for a part) instead of cutting, and a tick confirms it.
        /// </summary>
        private static void SkipAhead(bool part)
        {
            int target = -1; float targetT = 0;
            bool rankSeen = false, cardsSeen = false, bannerSeen = false;
            for (int i = _xpStep; i < _xpSteps.Count && target < 0; i++)
            {
                var s = _xpSteps[i];
                float at = -1;
                if (s.Kind == StepKind.Rank && !rankSeen) { rankSeen = true; at = 0; }
                else if ((s.Kind == StepKind.Unlock || s.Kind == StepKind.Page) && !cardsSeen) { cardsSeen = true; at = 0; }
                else if (s.Kind == StepKind.Banner && !bannerSeen) { bannerSeen = true; at = 0; }
                else if (s.Kind == StepKind.Land) at = 0;
                else if (!part && s.Kind == StepKind.Fill && s.Loud) at = Mathf.Max(0, s.Dur - Sfx.Lead("levelup", s.Pitch));
                if (s.Kind == StepKind.Unlock || s.Kind == StepKind.Page) cardsSeen = true;
                if (at < 0) continue;
                if (i == _xpStep && at <= _xpT + .01f) continue; // already there or past it
                // the cards beat as a whole: inside it, a tap goes on to the summary
                if (i == _xpStep && (s.Kind == StepKind.Unlock || s.Kind == StepKind.Page)) continue;
                target = i; targetT = at;
            }
            if (target < 0) { FinishXpAnim("skipped"); return; }
            Sfx.Stop();
            _skipFrom = _xpFill != null ? _xpFill.anchorMax.x : 0;
            // everything before the target: its end state, quietly
            while (_xpStep < target)
            {
                var s = _xpSteps[_xpStep];
                bool enter = _xpEntered != _xpStep;
                _xpEntered = _xpStep;
                _xpT = s.Dur;
                StepFrame(s, enter, true);
                _xpStep++; _xpT = 0;
            }
            _xpT = targetT; // its enter runs next frame (a level up's fill: the sound starts right then)
            _skipAt = Time.unscaledTime; _skipDur = part ? .2f : .12f;
            _xpPop = _rollT = _luT = -1;
            if (_xpLevelOld != null) _xpLevelOld.gameObject.SetActive(false);
            if (_xpLevel != null) ((RectTransform)_xpLevel.transform).anchoredPosition = Vector2.zero;
            if (_xpLevelUp != null) _xpLevelUp.gameObject.SetActive(false);
            if (_xpCaption != null) { Ui.SetText(_xpCaption, "CURRENT LEVEL"); _xpCaption.gameObject.SetActive(true); }
            if (_xpSquare != null) { _xpSquare.rectTransform.localScale = Vector3.one; _xpSquare.color = Ui.Hex(Orange); }
            _xpBump = 0; // a small bump on the level square + a tick: the skip registered
            Sounds.Play("ButtonOver", "ButtonClick");
            L.Debug($"xp: skip{(part ? " (part)" : "")} → step {target} ({_xpSteps[target].Kind}{(_xpSteps[target].Level > 0 ? " " + _xpSteps[target].Level : "")})");
        }

        private static bool _xpCued, _xpRankFallback, _xpRolled;
        private static float _xpBump = -1;

        private static CanvasGroup HeadText()
        {
            if (_headTextGroup != null) return _headTextGroup;
            // the rank name and "Page X of 16" fade together: one group over both
            var go = _headRank.transform.parent.gameObject;
            var holder = new GameObject("RankText", typeof(RectTransform));
            holder.transform.SetParent(go.transform, false);
            var rt = (RectTransform)holder.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            _headRank.transform.SetParent(rt, true);
            _headNext.transform.SetParent(rt, true);
            _headTextGroup = holder.AddComponent<CanvasGroup>();
            _headTextGroup.blocksRaycasts = false;
            return _headTextGroup;
        }

        // ---------------------------------------------------------------- the level number: roll, pop, LEVEL UP

        private const float RollTime = .18f;
        private static Component _xpLevelOld, _xpLevelUp;
        private static float _rollT = -1, _luT = -1, _xpBlockFlashT = -1;
        private static Image _xpBlockFlash;

        /// <summary>The old number slides up and out, the new one up from below — landing on the pop.</summary>
        private static void StartRoll(int from, int to)
        {
            var sq = _xpSquare.rectTransform;
            if (sq.GetComponent<RectMask2D>() == null) sq.gameObject.AddComponent<RectMask2D>();
            if (_xpLevelOld == null) _xpLevelOld = Ui.Label(sq, "Old", "", TLevel, Color.white, TextAnchor.MiddleCenter, true);
            Ui.SetText(_xpLevelOld, from.ToString());
            Ui.SetColor(_xpLevelOld, Color.white);
            _xpLevelOld.gameObject.SetActive(true);
            Ui.SetText(_xpLevel, to.ToString());
            _rollT = 0;
        }

        /// <summary>"+2 000 000" after the EXP tag: the XP still to play, draining as the bar takes it.</summary>
        private static void GainLeft(int shownTotal)
        {
            if (_xpGain == null || !_xpGain.gameObject.activeSelf) return;
            Ui.SetText(_xpGain, $"+{Thousands(Mathf.Max(0, _xpTo - shownTotal))}");
            // CoD-style big counter under the picture: what you've earned so far, counting up as the bar takes it
            if (_xpBig != null) Ui.SetText(_xpBig, $"+{Thousands(Mathf.Clamp(shownTotal - _xpFrom, 0, _xpTo - _xpFrom))} <size=60%><color=#e0562f>EXP</color></size>");
            _gainShown = $"+{Thousands(Mathf.Clamp(shownTotal - _xpFrom, 0, _xpTo - _xpFrom))} <size=55%>XP</size>"; // MW 4: rides the light wall
        }

        /// <summary>The level square's pop, the number's roll, the bar's flash and reset, LEVEL UP, the rank emblem, the cards.</summary>
        private static void AnimatePops(float dt)
        {
            var fill = _xpFill != null ? _xpFill.GetComponent<Image>() : null;
            if (_rollT >= 0)
            {
                _rollT += dt;
                float u = Mathf.Clamp01(_rollT / RollTime), e = 1 - Mathf.Pow(1 - u, 3);
                float h = _xpSquare.rectTransform.rect.height;
                ((RectTransform)_xpLevel.transform).anchoredPosition = new Vector2(0, -h * (1 - e));
                ((RectTransform)_xpLevelOld.transform).anchoredPosition = new Vector2(0, h * e);
                Ui.SetColor(_xpLevelOld, new Color(1, 1, 1, 1 - e));
                if (u >= 1) { _rollT = -1; _xpLevelOld.gameObject.SetActive(false); ((RectTransform)_xpLevel.transform).anchoredPosition = Vector2.zero; }
            }
            if (_xpPop >= 0)
            {
                _xpPop += dt;
                float p = Mathf.Clamp01(_xpPop / (_xpPopBig ? .5f : .42f));
                float s = 1 + (_xpPopBig ? .4f : .26f) * Mathf.Sin(p * Mathf.PI) * (1 - p * .5f);
                _xpSquare.rectTransform.localScale = new Vector3(s, s, 1);
                _xpSquare.color = Color.Lerp(Color.white, Ui.Hex(Orange), Mathf.Clamp01(_xpPop / .3f));
                if (_xpPop > 1.1f) { _xpPop = -1; _xpSquare.rectTransform.localScale = Vector3.one; _xpSquare.color = Ui.Hex(Orange); }
            }
            // the full bar: white flash (0.08 s), fades away (0.15 s), then it's empty for the next fill
            if (_barResetT >= 0 && fill != null)
            {
                _barResetT += dt;
                var c = Color.Lerp(Color.Lerp(Color.white, Ui.Hex(Orange), .55f), XpFillColor, Mathf.Clamp01(_barResetT / .08f)); // a warm flash: visible on the light grey bar
                c.a = _barResetT < .08f ? 1 : Mathf.Clamp01(1 - (_barResetT - .08f) / .15f);
                fill.color = c;
                if (_barResetT >= .23f)
                {
                    _barResetT = -1; fill.color = XpFillColor;
                    if (_xpRunning && _xpStep < _xpSteps.Count && _xpSteps[_xpStep].Kind == StepKind.LevelUp) _xpFill.anchorMax = new Vector2(0, 1);
                }
            }
            // "LEVEL UP" rises over the caption row and fades (0.9 s); the caption comes back after
            if (_luT >= 0)
            {
                if (_xpLevelUp == null && _focusXp != null)
                    _xpLevelUp = Ui.Label(Ui.Rect(_focusXp, "LevelUp", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -16), new Vector2(0, 2)), "Text", "LEVEL UP", 16, Ui.Hex(Orange), TextAnchor.MiddleLeft, true, 2);
                _luT += dt;
                if (_xpLevelUp != null)
                {
                    _xpLevelUp.gameObject.SetActive(true);
                    _xpCaption.gameObject.SetActive(false);
                    float a = _luT < .08f ? _luT / .08f : Mathf.Clamp01(1 - (_luT - .5f) / .4f);
                    Ui.SetColor(_xpLevelUp, Ui.Hex(Orange, a));
                    ((RectTransform)_xpLevelUp.transform).anchoredPosition = new Vector2(0, 6f * (1 - Mathf.Pow(1 - Mathf.Clamp01(_luT / .9f), 2)));
                }
                if (_luT >= .9f)
                {
                    _luT = -1;
                    if (_xpLevelUp != null) _xpLevelUp.gameObject.SetActive(false);
                    _xpCaption.gameObject.SetActive(true);
                }
            }
            // the last level up: a white flash over the whole XP block
            if (_xpBlockFlashT >= 0)
            {
                if (_xpBlockFlash == null && _focusXp != null)
                {
                    _xpBlockFlash = Ui.Img(Ui.Rect(_focusXp, "Flash", Vector2.zero, Vector2.one, new Vector2(-6, -4), new Vector2(6, 4)), new Color(1, 1, 1, 0), Ui.Radial());
                    _xpBlockFlash.raycastTarget = false;
                }
                _xpBlockFlashT += dt;
                if (_xpBlockFlash != null) _xpBlockFlash.color = new Color(1, .95f, .9f, .28f * Mathf.Clamp01(1 - _xpBlockFlashT / .4f));
                if (_xpBlockFlashT >= .4f) _xpBlockFlashT = -1;
            }
            if (_xpRankPop >= 0 && _headBadge != null)
            {
                _xpRankPop += dt;
                // 0–0.2 s: shrinks to 60% (the new emblem goes in at the bottom of it) · 0.2–0.75 s: overshoots to ~118%,
                // settles · a warm glow blooms behind it and fades by 1.1 s, a ring bursts out
                float t = _xpRankPop, s;
                if (t < .2f) s = Mathf.Lerp(1, .6f, EaseIn(t / .2f));
                else if (t < .75f) { float u = (t - .2f) / .55f; s = .6f + .58f * EaseOutBack(u); if (u > .6f) s = Mathf.Lerp(1.18f, 1, (u - .6f) / .4f); }
                else s = 1;
                _headBadge.Root.localScale = new Vector3(s, s, 1);
                var glow = RankGlow();
                float a = t < .2f ? Mathf.Lerp(.22f, .6f, t / .2f) : .6f * Mathf.Clamp01(1 - (t - .2f) / .9f);
                if (glow != null) glow.color = new Color(1f, .45f, .2f, a);
                if (t >= .2f) RankRing(t - .2f);
                if (t >= 1.1f) { _xpRankPop = -1; _headBadge.Root.localScale = Vector3.one; if (glow != null) glow.color = new Color(0, 0, 0, 0); if (_rankRing != null) _rankRing.color = new Color(0, 0, 0, 0); }
            }
            if (_xpBump >= 0 && _xpPop < 0)
            {
                _xpBump += dt;
                float b = 1 + .1f * Mathf.Sin(Mathf.Clamp01(_xpBump / .14f) * Mathf.PI);
                _xpSquare.rectTransform.localScale = new Vector3(b, b, 1);
                if (_xpBump >= .14f) { _xpBump = -1; _xpSquare.rectTransform.localScale = Vector3.one; }
            }
            if (_cards != null) foreach (var c in _cards) c.TickFlash();
            FadeBarFx();
            TickSweep(dt);
            TickPrompt(dt);
        }

        private static float EaseIn(float x) => x * x;
        private static float EaseOutBack(float x) { const float c = 1.7f; x -= 1; return 1 + (c + 1) * x * x * x + c * x * x; }

        private static Image _rankRing;

        /// <summary>A soft ring that bursts out of the emblem at the swap (grows 1× → 2.6× while it fades).</summary>
        private static void RankRing(float t)
        {
            if (_headBadge == null) return;
            if (_rankRing == null)
            {
                var badge = _headBadge.Root;
                var rt = Ui.Box(badge.parent, "RankRing", badge.anchorMin, badge.anchoredPosition, badge.sizeDelta * 1.3f);
                rt.SetSiblingIndex(badge.GetSiblingIndex());
                _rankRing = Ui.Img(rt, new Color(0, 0, 0, 0), Ui.Radial());
                _rankRing.raycastTarget = false;
            }
            float u = Mathf.Clamp01(t / .7f);
            float sc = 1 + 1.6f * (1 - Mathf.Pow(1 - u, 3));
            _rankRing.rectTransform.localScale = new Vector3(sc, sc, 1);
            _rankRing.color = new Color(1f, .6f, .35f, .45f * (1 - u) * (1 - u));
        }

        private static Image RankGlow()
        {
            if (_xpRankGlow != null || _headBadge == null) return _xpRankGlow;
            var badge = _headBadge.Root;
            var rt = Ui.Box(badge.parent, "RankGlow", badge.anchorMin, badge.anchoredPosition, badge.sizeDelta * 2.4f);
            rt.SetSiblingIndex(badge.GetSiblingIndex()); // behind the emblem
            _xpRankGlow = Ui.Img(rt, new Color(0, 0, 0, 0), Ui.Radial());
            _xpRankGlow.raycastTarget = false;
            return _xpRankGlow;
        }

        // ---------------------------------------------------------------- focus: the active part bright, the rest dimmed

        private enum Focus { None, Xp, Header, Cards }
        private static Focus _focusTarget = Focus.None;
        private static float _headFocus = 1, _rankFade = 1;
        private static readonly Dictionary<RectTransform, float> _focusAlpha = new Dictionary<RectTransform, float>();

        private static RectTransform FocusRect(Focus f) =>
            f == Focus.Xp ? (_xpSquare != null ? _xpSquare.rectTransform : null)
            : f == Focus.Header ? (_headBadge != null ? _headBadge.Root : null)
            : f == Focus.Cards ? _bottom : null;

        private static void SetFocus(Focus f, bool instant = false)
        {
            _focusTarget = f;
            if (instant) TickFocus(10f);
        }

        /// <summary>Everything but the active part eases to 45% (0.3 s); none active: all back to full.</summary>
        private static void TickFocus(float dt)
        {
            float step = dt / .3f;
            float Target(Focus own) => _focusTarget == Focus.None || _focusTarget == own ? 1f : .45f;
            void Ease(RectTransform rt, float target)
            {
                if (rt == null) return;
                var g = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();
                g.alpha = Mathf.MoveTowards(g.alpha, target, step);
            }
            Ease(_focusXp, Target(Focus.Xp));
            Ease(_focusList, Target(Focus.None) < 1 ? .45f : 1f);
            Ease(_focusStage, Target(Focus.None) < 1 ? .45f : 1f);
            Ease(_focusSide, Target(Focus.None) < 1 ? .45f : 1f);
            Ease(_bottom, Target(Focus.Cards));
            if (_headBadge != null) Ease(_headBadge.Root, Target(Focus.Header));
            _headFocus = Mathf.MoveTowards(_headFocus, Target(Focus.Header), step);
            if (_headRank != null) HeadText().alpha = _headFocus * _rankFade;
        }

        // ---------------------------------------------------------------- the light that hands over from one part to the next

        private static Image _sweep;
        private static RectTransform _sweepFrom, _sweepTo;
        private static float _sweepT = -1;

        private static void Sweep(RectTransform from, RectTransform to)
        {
            if (_sweep == null)
            {
                var root = _focusXp != null ? _focusXp.parent?.parent as RectTransform : null;
                if (root == null) return;
                var rt = Ui.Box(root, "Sweep", new Vector2(.5f, .5f), Vector2.zero, new Vector2(240, 70));
                _sweep = Ui.Img(rt, new Color(0, 0, 0, 0), Ui.Radial());
                _sweep.raycastTarget = false;
            }
            _sweep.transform.SetAsLastSibling();
            _sweepFrom = from; _sweepTo = to; _sweepT = 0;
        }

        private static void TickSweep(float dt)
        {
            if (_sweepT < 0 || _sweep == null || _sweepFrom == null || _sweepTo == null) return;
            _sweepT += dt;
            float u = Mathf.Clamp01(_sweepT / .5f), e = u * u * (3 - 2 * u);
            _sweep.transform.position = Vector3.Lerp(_sweepFrom.position, _sweepTo.position, e);
            _sweep.color = new Color(1f, .5f, .25f, .32f * Mathf.Sin(u * Mathf.PI));
            if (u >= 1) { _sweepT = -1; _sweep.color = new Color(0, 0, 0, 0); }
        }

        // ---------------------------------------------------------------- the controls prompt (where the page bar is)

        private static Component _prompt;
        private static bool _promptOn;

        private static void ShowPrompt(bool on, bool instant = false)
        {
            _promptOn = on;
            if (instant) TickPrompt(10f);
        }

        private static void TickPrompt(float dt)
        {
            if (_pageBarRt == null) return;
            if (_prompt == null && _promptOn)
            {
                var rt = Ui.Rect(_pageBarRt.parent, "XpPrompt", _pageBarRt.anchorMin, _pageBarRt.anchorMax, _pageBarRt.offsetMin, _pageBarRt.offsetMax);
                _prompt = Ui.Label(rt, "Text", "", TCaps, Grey, TextAnchor.MiddleCenter, false, Caps);
                Ui.SetText(_prompt, "<color=#d9dfdc>SPACE</color>  NEXT          <color=#d9dfdc>HOLD SPACE</color>  SKIP PART          <color=#d9dfdc>ESC</color>  CLOSE");
            }
            var bar = _pageBarRt.GetComponent<CanvasGroup>() ?? _pageBarRt.gameObject.AddComponent<CanvasGroup>();
            float step = dt / .25f;
            bar.alpha = Mathf.MoveTowards(bar.alpha, _promptOn ? 0 : 1, step);
            bar.blocksRaycasts = !_promptOn;
            if (_prompt != null)
            {
                var g = _prompt.GetComponent<CanvasGroup>() ?? _prompt.gameObject.AddComponent<CanvasGroup>();
                g.alpha = Mathf.MoveTowards(g.alpha, _promptOn ? 1 : 0, step);
                g.blocksRaycasts = false;
            }
        }

        // ---------------------------------------------------------------- the summary banner over the cards

        private static GameObject _banner;
        private static Component _bannerText, _bannerSub;

        private static void ShowBanner()
        {
            if (_banner == null && _bottom != null)
            {
                var rt = Ui.Box(_bottom, "Summary", new Vector2(.5f, .5f), new Vector2(0, 20), new Vector2(760, 76));
                Ui.Img(rt, Ui.Hex("#0a0e10", .95f));
                Ui.Img(Ui.Rect(rt, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -2), Vector2.zero), Ui.Hex(Orange));
                Ui.Img(Ui.Rect(rt, "Bottom", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 1)), Ui.Hex(Orange, .5f));
                _bannerText = Ui.Label(Ui.Rect(rt, "Text", new Vector2(0, .42f), Vector2.one, new Vector2(S4, 0), new Vector2(-S4, -4)), "Text", "", TTitle, Text, TextAnchor.MiddleCenter, true, 1);
                _bannerSub = Ui.Label(Ui.Rect(rt, "Sub", Vector2.zero, new Vector2(1, .42f), new Vector2(S4, 6), new Vector2(-S4, 0)), "Text", "", TCaps, Ui.Hex(Orange), TextAnchor.MiddleCenter, false, Caps);
                rt.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
                _banner = rt.gameObject;
            }
            if (_banner == null) return;
            int levels = _xpLb - _xpLa;
            Ui.SetText(_bannerText, $"{levels} LEVEL{(levels == 1 ? "" : "S")}  ·  {_xpItems} ITEM{(_xpItems == 1 ? "" : "S")} UNLOCKED");
            bool rank = TierOf(_xpLb).Name != TierOf(_xpLa).Name;
            Ui.SetText(_bannerSub, rank ? $"NEW RANK  ·  {TierOf(_xpLb).Name.ToUpperInvariant()}" : $"NOW LEVEL {_xpLb}");
            _banner.transform.SetAsLastSibling();
            _banner.SetActive(true);
        }

        /// <summary>In 0.2 s (from 96%), holds, out in the last 0.25 s.</summary>
        private static void TickBanner(float t, float dur)
        {
            if (_banner == null) return;
            float a = t < .2f ? t / .2f : Mathf.Clamp01((dur - t) / .25f);
            _banner.GetComponent<CanvasGroup>().alpha = a;
            float s = t < .2f ? Mathf.Lerp(.96f, 1f, 1 - Mathf.Pow(1 - t / .2f, 3)) : 1f;
            _banner.transform.localScale = new Vector3(s, s, 1);
            if (t >= dur) _banner.SetActive(false);
        }

        // ---------------------------------------------------------------- "+N" after the EXP tag

        /// <summary>The "+N" pool after the EXP tag while it plays (slides in from the left); fades out after.</summary>
        private static void ShowGain(bool on)
        {
            if (_xpGain == null && _xpRight != null)
                _xpGain = Ui.Label(Ui.Rect(_xpRight, "Gain", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -44), new Vector2(260, -18)), "Text", "", TStrong, Ui.Hex(Orange), TextAnchor.MiddleLeft, true);
            if (_xpGain == null) return;
            if (_xpBig == null && _focusStage != null)
            {
                _xpBig = Ui.Label(Ui.Rect(_focusStage, "BigGain", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 34), new Vector2(0, 96)), "Text", "", 52, Ui.Hex("#eceeef"), TextAnchor.MiddleCenter, true, 2);
                _xpBig.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            }
            if (_xpBig != null) { _xpBig.gameObject.SetActive(on && (Polish.BigXpDuringSweep || !Mw(4))); if (on) Ui.SetText(_xpBig, "+0 <size=60%><color=#e0562f>EXP</color></size>"); }
            _xpGain.gameObject.SetActive(on);
            if (!on) return;
            Ui.SetText(_xpGain, $"+{Thousands(_xpTo - _xpFrom)}");
            Ui.SetColor(_xpGain, Ui.Hex(Orange));
            _xpGainFadeAt = -1; _xpGainIn = 0;
            PlaceGain();
        }

        private static float _xpGainFadeAt = -1, _xpGainIn = 1;

        private static void FadeGain()
        {
            if (_xpGain == null || !_xpGain.gameObject.activeSelf || _xpGainFadeAt < 0) return;
            float a = Mathf.Clamp01(1 - (Time.unscaledTime - _xpGainFadeAt) / .6f);
            Ui.SetColor(_xpGain, Ui.Hex(Orange, a));
            if (_xpBig != null) { var cg = _xpBig.GetComponent<CanvasGroup>(); if (cg != null) cg.alpha = a; }
            if (a <= 0) { _xpGain.gameObject.SetActive(false); if (_xpBig != null) { _xpBig.gameObject.SetActive(false); var cg = _xpBig.GetComponent<CanvasGroup>(); if (cg != null) cg.alpha = 1; } }
        }

        private static void PlaceGain()
        {
            if (_xpGain == null) return;
            if (_xpGainIn < 1) { _xpGainIn = Mathf.Min(1, _xpGainIn + Time.unscaledDeltaTime / .3f); Ui.SetColor(_xpGain, Ui.Hex(Orange, _xpGainIn)); }
            float slide = Mathf.Pow(1 - _xpGainIn, 3) * -14f; // eases in from 14 px to the left
            var rt = (RectTransform)_xpGain.transform;
            float x = _xpTextWidth + 12 + 40 + 12 + slide;
            rt.offsetMin = new Vector2(x, rt.offsetMin.y);
            rt.offsetMax = new Vector2(x + 260, rt.offsetMax.y);
        }

        /// <summary>Shows a total XP as the XP block would (the animation's start).</summary>
        private static void ShowXpAt(int exp, int level)
        {
            bool known = ProgData.ExpInLevel(exp, level, out int have, out int need);
            ShowXpState(level, have, need, known);
        }

        // ---------------------------------------------------------------- preview (F12 > 4. Preview): nothing real changes

        private static bool _xpSim;
        /// <summary>The level a preview left on screen (0: none); the real state comes back when the screen closes.</summary>
        private static int _simLevel;
        private static int _simXp;

        /// <summary>
        /// Plays the animation with made-up numbers: "levels" (n level ups), "rank" (up to the next rank), "unlock" (the next n
        /// levels' cards). Never touches the profile, the saved XP or the NEW state; closing the screen brings the real one back.
        /// </summary>
        public static void Preview(string kind, int n)
        {
            try
            {
                if (!IsOpen) Open("preview");
                if (!IsOpen) { Toast.Show("Open the main menu first — previews play on the Progression screen"); return; }
                if (!ProgData.HasExpTable) { Toast.Show("Preview: the XP table isn't loaded yet"); return; }
                // a real animation waiting to play stays unshown (it plays on the next open)
                _xpPending = _xpRunning = false; _xpSteps.Clear(); Sfx.Stop(); ResetXpVisuals();
                int max = ProgData.MaxLevel;
                int la = _simLevel > 0 ? _simLevel : ProgData.PlayerLevel();
                int from = _simLevel > 0 ? _simXp : ProgData.TotalExp();
                if (la <= 0 || from < 0) return;
                ProgData.ExpInLevel(from, la, out int have, out int need);
                float frac = need > 0 ? Mathf.Clamp01(have / (float)need) : 0;
                int lb = la;
                if (kind == "rank")
                {
                    var next = Tiers.FirstOrDefault(t => t.From > la);
                    if (next.Name == null) { Toast.Show("Preview: already at the top rank"); return; }
                    lb = next.From;
                }
                else lb = la + Mathf.Max(1, n);
                lb = Mathf.Min(lb, max);
                if (lb <= la) { Toast.Show("Preview: already at the last level"); return; }
                int to = ProgData.ExpAtLevel(lb) + (ProgData.ExpInLevel(ProgData.ExpAtLevel(lb), lb, out _, out int nb) ? Mathf.RoundToInt(frac * nb) : 0);
                _xpSim = true;
                _xpFrom = from; _xpTo = to; _xpLa = la; _xpLb = lb;
                BuildXpSteps(from, to, la, lb);
                if (kind == "unlock")
                {
                    // the cards beat only (and the summary): the earn and rank steps go
                    _xpSteps.RemoveAll(s => s.Kind == StepKind.Fill || s.Kind == StepKind.LevelUp || s.Kind == StepKind.Rank);
                    while (_xpSteps.Count > 0 && _xpSteps[0].Kind == StepKind.Pause) _xpSteps.RemoveAt(0);
                }
                // start from the preview's "before": its level, page and XP
                _xpCardLevel = la;
                if ((la - 1) / PerPage != _page) ShowPage((la - 1) / PerPage, 0);
                _level = la;
                UpdateSelection();
                ShowXpAt(from, la);
                UpdateHeader(la, la);
                _xpPending = true;
                _openedAt = Mathf.Min(_openedAt, Time.unscaledTime - 1); // no fade-in wait
                L.Info($"preview: {kind} — level {la} → {lb} ({from} → {to} XP, made up; nothing is saved); {_xpSteps.Count} step(s), ~{TotalDur():0.0} s");
                NewTags.Preview(la, lb); // the previewed levels' rewards and cards show NEW, like a real level-up (not saved)
                if (ProgressionPlugin.ConfigWindowOpen()) Toast.Show("Preview ready — close F12 to watch it");
            }
            catch (Exception e) { L.Error("preview", e); }
        }

        private static void FinishPreview(string why)
        {
            if (why != "done") Sfx.Stop();
            EndRank();
            ResetXpVisuals();
            _simLevel = _xpLb; _simXp = _xpTo;
            if (why == "screen closed") { EndPreview(); return; }
            // stays on screen as the preview left it (until the screen closes)
            _xpCardLevel = _xpLb;
            ShowXpAt(_xpTo, _xpLb);
            UpdateHeader(_xpLb, _xpLb);
            UpdateXpNext();
            int page = (_xpLand - 1) / PerPage;
            if (page != _page) { _xpPaging = true; try { ShowPage(page, 0); } finally { _xpPaging = false; } }
            ShowLevel(_xpLand, true);
            LandFade(1);
            L.Info($"preview: {why}; showing level {_xpLb} until the screen closes (nothing saved)");
        }

        /// <summary>Back to the real state (the screen closed).</summary>
        private static void EndPreview()
        {
            if (!_xpSim && _simLevel == 0) return;
            _xpSim = false; _simLevel = 0; _simXp = 0; _xpCardLevel = 0;
            NewTags.EndPreview();
            L.Info("preview: ended — back to your real level / XP");
        }

        /// <summary>Jumps to the end state and remembers the XP as shown. Safe to call any time.</summary>
        private static void FinishXpAnim(string why)
        {
            if (!XpAnimating) return;
            bool leveled = _xpLb > _xpLa;
            _xpPending = _xpRunning = false;
            _xpSteps.Clear();
            _landJobs.Clear();
            SetFocus(Focus.None);
            ShowPrompt(false);
            if (_xpSim) { FinishPreview(why); return; }
            _xpCardLevel = 0;
            SeenState.Xp = _xpTo;
            EndRank();
            if (_banner != null) _banner.SetActive(false);
            if (why != "done") Sfx.Stop(); // skipped / closed: the sound stops with it
            UpdateXp();
            if (leveled)
            {
                // land on the new level (on close: the next open starts there)
                int page = (_xpLand - 1) / PerPage;
                if (why == "screen closed") { _level = _xpLand; _page = page; }
                else
                {
                    if (page != _page) { _xpPaging = true; try { ShowPage(page, 0); } finally { _xpPaging = false; } }
                    ShowLevel(_xpLand, true);
                }
            }
            else UpdateSelection();
            LandFade(1);
            PlaceGain();
            if (_xpGain != null && _xpGain.gameObject.activeSelf) { _xpGainIn = 1; Ui.SetColor(_xpGain, Ui.Hex(Orange)); _xpGainFadeAt = Time.unscaledTime + .8f; }
            L.Info($"xp: animation {why}; shown XP now {_xpTo}");
        }
    }
}
