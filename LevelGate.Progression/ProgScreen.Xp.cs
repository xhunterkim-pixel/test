using System;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// The XP animation. The screen remembers the total XP it last showed (Advanced > ShownXp, hidden); when the profile has
    /// more on open, it plays out in beats, each one landing before the next starts (a second of stillness in between):
    ///
    ///   1. EARN   the "+N" chip slides in; the bar fills; each level up pops the number (white flash, LEVEL UP caption) and
    ///             the bar starts over. Many levels accelerate as they go; the level-up sound plays on the first and the
    ///             last one only (at most one every 0.6 s — eight chimes in a row was too much).
    ///   2. RANK   only when a new rank was reached: the emblem shrinks, swaps while it's smallest (the swap hides in the
    ///             motion), overshoots and settles in a warm glow; the rank name fades across.
    ///   3. UNLOCK the level cards slide to the first new level; left to right, each new card flashes orange, LOCKED turns
    ///             into NEW, and the CURRENT marker walks along. Pages forward as needed (at most the last two pages).
    ///   4. LAND   your new level is selected: the rewards list shows what just unlocked.
    ///
    /// Click or Space skips to the end; Esc closes (it finishes too). The screen's own keys wait until it's done.
    /// </summary>
    internal static partial class ProgScreen
    {
        private enum StepKind { Fill, LevelUp, Pause, Rank, Page, Unlock, Land }

        private struct XpStep
        {
            public StepKind Kind;
            public int Level;        // Fill: level being filled · LevelUp / Unlock / Land: that level · Rank: the new rank's level · Page: page
            public float From, To;   // Fill: bar 0..1
            public float Dur;
            public bool Loud;        // LevelUp: plays the sound
        }

        private static readonly List<XpStep> _xpSteps = new List<XpStep>();
        private static bool _xpPending, _xpRunning;
        private static int _xpFrom, _xpTo, _xpStep, _xpLa, _xpLb;
        private static float _xpT, _xpPop = -1, _xpRankPop = -1, _xpSoundAt = -10, _xpTickAt = -10;
        private static bool _xpRankSwapped;
        private static Component _xpGain;
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
            _xpCardLevel = 0;
            ResetXpVisuals();
            int now = ProgData.TotalExp(), shown = ProgressionPlugin.ShownXp.Value;
            if (now < 0 || !ProgData.HasExpTable) return; // not known yet: next time (the baseline stays)
            if (!ProgressionPlugin.XpAnimation.Value || shown <= 0 || now <= shown)
            {
                if (shown != now) { ProgressionPlugin.ShownXp.Value = now; if (shown > now) L.Info($"xp: shown XP {shown} > profile {now} (new profile?) — reset"); }
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
            _xpPop = _xpRankPop = -1;
            if (_xpSquare != null) { _xpSquare.rectTransform.localScale = Vector3.one; _xpSquare.color = Ui.Hex(Orange); }
            var fill = _xpFill != null ? _xpFill.GetComponent<Image>() : null;
            if (fill != null) fill.color = Ui.Hex(Orange);
            if (_headBadge != null) _headBadge.Root.localScale = Vector3.one;
            if (_xpRankGlow != null) _xpRankGlow.color = new Color(0, 0, 0, 0);
            if (_headTextGroup != null) _headTextGroup.alpha = 1;
            if (_xpCaption != null) Ui.SetText(_xpCaption, "CURRENT LEVEL");
            if (_xpGain != null) _xpGain.gameObject.SetActive(false);
        }

        private static float TotalDur() { float t = 0; foreach (var s in _xpSteps) t += s.Dur; return t; }

        private static void Add(StepKind kind, float dur, int level = 0, float from = 0, float to = 0, bool loud = false)
            => _xpSteps.Add(new XpStep { Kind = kind, Dur = dur, Level = level, From = from, To = to, Loud = loud });

        private static void BuildXpSteps(int from, int to, int la, int lb)
        {
            _xpSteps.Clear();
            int levels = lb - la;
            float Frac(int exp, int level) => ProgData.ExpInLevel(exp, level, out int h, out int n) ? Mathf.Clamp01(h / (float)n) : 1f;
            float fa = Frac(from, la), fb = Frac(to, lb);

            // ---- 1. EARN
            if (levels == 0)
            {
                Add(StepKind.Fill, Mathf.Clamp(.5f + (fb - fa) * 1.2f, .6f, 1.6f), la, fa, fb);
                return;
            }
            // a whole level's fill: slower for a few, quicker for many — and it speeds up as it goes (a ramp, not a metronome)
            float full = Mathf.Clamp(1.1f / Mathf.Sqrt(levels), .14f, .8f);
            float pop = levels > 5 ? .16f : .38f;
            Add(StepKind.Fill, Mathf.Max(.25f, full * 1.2f * (1 - fa) + .1f), la, fa, 1);
            for (int l = la + 1; l <= lb; l++)
            {
                int i = l - la;
                bool loud = i == 1 || l == lb; // the first and the last level up are heard
                Add(StepKind.LevelUp, l == lb ? .45f : pop, l, loud: loud);
                if (l < lb) Add(StepKind.Fill, full * Mathf.Lerp(1f, .45f, i / (float)levels), l, 0, 1);
                else if (fb > 0) Add(StepKind.Fill, Mathf.Clamp(.35f + fb * .9f, .35f, 1.2f), l, 0, fb);
            }
            // the bar beat stays within ~5 s however many levels
            float bar = TotalDur();
            if (bar > 5f)
                for (int i = 0; i < _xpSteps.Count; i++) { var s = _xpSteps[i]; s.Dur = Mathf.Max(.07f, s.Dur * 5f / bar); _xpSteps[i] = s; }

            // ---- 2. RANK (only when it changed)
            Add(StepKind.Pause, 1f);
            if (TierOf(lb).Name != TierOf(la).Name)
            {
                Add(StepKind.Rank, 1.1f, lb);
                Add(StepKind.Pause, 1f);
            }

            // ---- 3. UNLOCK: the new levels' cards, left to right, page by page (at most the last two pages)
            int firstShown = la + 1;
            int lastPage = (lb - 1) / PerPage;
            if (lastPage - (firstShown - 1) / PerPage > 1) firstShown = (lastPage - 1) * PerPage + 1;
            int shownCount = lb - firstShown + 1;
            float each = shownCount <= 5 ? .34f : .24f;
            int page = (la - 1) / PerPage;
            if (firstShown > la + 1) Add(StepKind.Unlock, 0, firstShown - 1); // the skipped ones, silently (off screen)
            for (int l = firstShown; l <= lb; l++)
            {
                int p = (l - 1) / PerPage;
                if (p != page) { Add(StepKind.Page, .55f, p); page = p; }
                Add(StepKind.Unlock, each, l);
            }

            // ---- 4. LAND
            Add(StepKind.Pause, .45f);
            Add(StepKind.Land, .1f, lb);
        }

        /// <summary>Runs the animation (every frame while open). True while it plays: the screen's own keys wait.</summary>
        private static bool RunXpAnim()
        {
            var input = UnityInput.Current;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            AnimatePops(dt);
            if (_xpPending)
            {
                if (Time.unscaledTime - _openedAt < .45f) return true; // the screen fades in first
                _xpPending = false; _xpRunning = true; _xpStep = 0; _xpT = 0;
                ShowGain(true);
                L.Info("xp: animation started");
            }
            if (!_xpRunning) { FadeGain(); return false; }
            if (input.GetKeyDown(KeyCode.Escape)) { Close("Escape"); return true; }
            if (input.GetMouseButtonDown(0) || input.GetKeyDown(KeyCode.Space)) { FinishXpAnim("skipped"); return true; }
            if (_xpStep >= _xpSteps.Count) { FinishXpAnim("done"); return false; }
            var st = _xpSteps[_xpStep];
            bool enter = _xpT == 0;
            _xpT += dt;
            float k = Mathf.Clamp01(_xpT / Mathf.Max(.01f, st.Dur));
            switch (st.Kind)
            {
                case StepKind.Fill:
                {
                    // the first fill eases in, the last one slows into place, the ones between run straight through
                    bool lastFill = _xpStep + 1 >= _xpSteps.Count || _xpSteps[_xpStep + 1].Kind != StepKind.LevelUp;
                    float e = lastFill ? 1 - Mathf.Pow(1 - k, 3) : (_xpStep == 0 ? k * k : k);
                    float frac = Mathf.Lerp(st.From, st.To, e);
                    if (ProgData.ExpInLevel(0, st.Level, out _, out int need)) ShowXpState(st.Level, Mathf.RoundToInt(frac * need), need, true);
                    _xpFill.anchorMax = new Vector2(frac, 1);
                    PlaceGain();
                    break;
                }
                case StepKind.LevelUp:
                    if (!enter) break;
                    ProgData.ExpInLevel(0, st.Level, out _, out int n2);
                    ShowXpState(st.Level, 0, Mathf.Max(1, n2), n2 > 0);
                    _xpPop = 0;
                    Ui.SetText(_xpCaption, $"<color={Orange}>LEVEL UP</color>");
                    if (st.Loud && Time.unscaledTime - _xpSoundAt > .6f) { _xpSoundAt = Time.unscaledTime; Sounds.Play("QuestCompleted", "QuestFinished", "TradeOperationComplete", "ButtonClick"); }
                    UpdateHeader(st.Level, _xpLa); // the unlocked count climbs; the rank waits for its own beat
                    L.Debug($"xp: level up → {st.Level}");
                    break;
                case StepKind.Rank:
                    if (enter)
                    {
                        _xpRankPop = 0; _xpRankSwapped = false;
                        Sounds.Play("InsuranceInsured", "DeathmatchWin", "MenuDropdownSelect", "ButtonClick");
                        L.Info($"xp: new rank {TierOf(st.Level).Name}");
                    }
                    // the swap happens while the emblem is smallest (see AnimatePops); the rank name fades across
                    if (!_xpRankSwapped && _xpRankPop >= .2f) { _xpRankSwapped = true; UpdateHeader(_xpLb, _xpLb); }
                    HeadText().alpha = k < .2f ? 1 - k / .2f : Mathf.Clamp01((k - .2f) / .35f);
                    break;
                case StepKind.Page:
                    if (!enter) break;
                    _xpPaging = true;
                    try { ShowPage(st.Level, 1); } finally { _xpPaging = false; }
                    break;
                case StepKind.Unlock:
                    if (!enter) break;
                    _xpCardLevel = st.Level;
                    UpdateSelection();
                    if (st.Dur > 0)
                    {
                        foreach (var c in _cards) if (c.Level == st.Level) c.Flash();
                        if (Time.unscaledTime - _xpTickAt > .15f) { _xpTickAt = Time.unscaledTime; Sounds.Play("ButtonOver", "ButtonClick"); }
                    }
                    break;
                case StepKind.Land:
                    if (!enter) break;
                    _xpCardLevel = 0;
                    if ((st.Level - 1) / PerPage != _page) { _xpPaging = true; try { ShowPage((st.Level - 1) / PerPage, 1); } finally { _xpPaging = false; } }
                    Sounds.Click();
                    ShowLevel(st.Level, true);
                    break;
            }
            if (k >= 1) { _xpStep++; _xpT = 0; }
            return true;
        }

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

        /// <summary>The level square's pop, the bar's flash, the rank emblem's swap and glow, the cards' flashes.</summary>
        private static void AnimatePops(float dt)
        {
            if (_xpPop >= 0)
            {
                _xpPop += dt;
                float p = Mathf.Clamp01(_xpPop / .42f);
                float s = 1 + .26f * Mathf.Sin(p * Mathf.PI) * (1 - p * .5f);
                _xpSquare.rectTransform.localScale = new Vector3(s, s, 1);
                _xpSquare.color = Color.Lerp(Color.white, Ui.Hex(Orange), Mathf.Clamp01(_xpPop / .3f));
                var fill = _xpFill.GetComponent<Image>();
                if (fill != null) fill.color = Color.Lerp(Color.white, Ui.Hex(Orange), Mathf.Clamp01(_xpPop / .35f));
                if (_xpPop > 1.1f)
                {
                    _xpPop = -1;
                    _xpSquare.rectTransform.localScale = Vector3.one;
                    _xpSquare.color = Ui.Hex(Orange);
                    if (fill != null) fill.color = Ui.Hex(Orange);
                    Ui.SetText(_xpCaption, "CURRENT LEVEL");
                }
            }
            if (_xpRankPop >= 0 && _headBadge != null)
            {
                _xpRankPop += dt;
                // 0–0.2 s: shrinks to 60% (the new emblem goes in at the bottom of it) · 0.2–0.75 s: overshoots to ~118%,
                // settles · a warm glow blooms behind it and fades by 1.1 s
                float t = _xpRankPop, s;
                if (t < .2f) s = Mathf.Lerp(1, .6f, EaseIn(t / .2f));
                else if (t < .75f) { float u = (t - .2f) / .55f; s = .6f + .58f * EaseOutBack(u); if (u > .6f) s = Mathf.Lerp(1.18f, 1, (u - .6f) / .4f); }
                else s = 1;
                _headBadge.Root.localScale = new Vector3(s, s, 1);
                var glow = RankGlow();
                float g = t < .2f ? 0 : Mathf.Clamp01(1 - (t - .2f) / .9f);
                if (glow != null) glow.color = new Color(1f, .45f, .2f, .6f * g * Mathf.Clamp01((t - .2f) / .12f));
                if (t >= 1.1f) { _xpRankPop = -1; _headBadge.Root.localScale = Vector3.one; if (glow != null) glow.color = new Color(0, 0, 0, 0); }
            }
            if (_cards != null) foreach (var c in _cards) c.TickFlash();
        }

        private static float EaseIn(float x) => x * x;
        private static float EaseOutBack(float x) { const float c = 1.7f; x -= 1; return 1 + (c + 1) * x * x * x + c * x * x; }

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

        /// <summary>"+12 400" after the EXP tag while it plays (slides in from the left); fades out after.</summary>
        private static void ShowGain(bool on)
        {
            if (_xpGain == null && _xpRight != null)
                _xpGain = Ui.Label(Ui.Rect(_xpRight, "Gain", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -60), new Vector2(260, -34)), "Text", "", TStrong, Ui.Hex(Orange), TextAnchor.MiddleLeft, true);
            if (_xpGain == null) return;
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
            if (a <= 0) _xpGain.gameObject.SetActive(false);
        }

        private static void PlaceGain()
        {
            if (_xpGain == null) return;
            if (_xpGainIn < 1) { _xpGainIn = Mathf.Min(1, _xpGainIn + Time.unscaledDeltaTime / .3f); Ui.SetColor(_xpGain, Ui.Hex(Orange, _xpGainIn)); }
            float slide = (1 - (1 - Mathf.Pow(1 - _xpGainIn, 3))) * -14f; // eases in from 14 px to the left
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

        /// <summary>Jumps to the end state and remembers the XP as shown. Safe to call any time.</summary>
        private static void FinishXpAnim(string why)
        {
            if (!XpAnimating) return;
            bool leveled = _xpLb > _xpLa;
            _xpPending = _xpRunning = false;
            _xpSteps.Clear();
            _xpCardLevel = 0;
            ProgressionPlugin.ShownXp.Value = _xpTo;
            if (_headTextGroup != null) _headTextGroup.alpha = 1;
            if (_xpRankPop >= 0) { _xpRankPop = -1; if (_headBadge != null) _headBadge.Root.localScale = Vector3.one; if (_xpRankGlow != null) _xpRankGlow.color = new Color(0, 0, 0, 0); }
            UpdateXp();
            if (leveled)
            {
                // land on the new level (on close: the next open starts there)
                int page = (_xpLb - 1) / PerPage;
                if (why == "screen closed") { _level = _xpLb; _page = page; }
                else
                {
                    if (page != _page) { _xpPaging = true; try { ShowPage(page, 0); } finally { _xpPaging = false; } }
                    ShowLevel(_xpLb, true);
                }
            }
            else UpdateSelection();
            PlaceGain();
            if (_xpGain != null && _xpGain.gameObject.activeSelf) { _xpGainIn = 1; Ui.SetColor(_xpGain, Ui.Hex(Orange)); _xpGainFadeAt = Time.unscaledTime + 1.6f; }
            L.Info($"xp: animation {why}; shown XP now {_xpTo}");
        }
    }
}
