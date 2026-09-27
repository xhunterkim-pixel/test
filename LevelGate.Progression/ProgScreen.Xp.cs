using System;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// The XP animation (Call of Duty after-action style, in the screen's own look). The screen remembers the total XP it last
    /// showed (Advanced > ShownXp, hidden). When the profile has more on open, the XP block plays it out:
    ///   bar fills → level up (the number pops, "LEVEL UP", the bar flashes and starts over) → new rank (the emblem swaps with a
    ///   glow) → … → the bar stops at your XP now.
    /// Many levels at once speed up so the whole thing stays within ~7 s. Click or Space skips; closing finishes it.
    /// </summary>
    internal static partial class ProgScreen
    {
        private enum StepKind { Fill, LevelUp }

        private struct XpStep
        {
            public StepKind Kind;
            public int Level;        // Fill: the level being filled · LevelUp: the new level
            public float From, To;   // Fill: bar 0..1
            public float Dur;
            public bool NewRank;     // LevelUp: the rank emblem changes too
        }

        private static readonly List<XpStep> _xpSteps = new List<XpStep>();
        private static bool _xpPending, _xpRunning;
        private static int _xpFrom, _xpTo, _xpStep, _xpShownLevel;
        private static float _xpT, _xpPop = -1, _xpRankPop = -1;
        private static Component _xpGain;
        private static Image _xpRankGlow;

        private static bool XpAnimating => _xpPending || _xpRunning;

        /// <summary>On open: plays only if the profile has more XP than the screen last showed (and the setting is on).</summary>
        private static void PrepareXpAnim()
        {
            _xpPending = _xpRunning = false;
            // a pop cut short by closing: back to rest
            _xpPop = _xpRankPop = -1;
            if (_xpSquare != null) { _xpSquare.rectTransform.localScale = Vector3.one; _xpSquare.color = Ui.Hex(Orange); }
            if (_headBadge != null) _headBadge.Root.localScale = Vector3.one;
            if (_xpRankGlow != null) _xpRankGlow.color = new Color(0, 0, 0, 0);
            if (_xpCaption != null) Ui.SetText(_xpCaption, "CURRENT LEVEL");
            if (_xpGain != null) _xpGain.gameObject.SetActive(false);
            int now = ProgData.TotalExp(), shown = ProgressionPlugin.ShownXp.Value;
            if (now < 0 || !ProgData.HasExpTable) return; // not known yet: next time (the baseline stays)
            if (!ProgressionPlugin.XpAnimation.Value || shown <= 0 || now <= shown)
            {
                if (shown != now) { ProgressionPlugin.ShownXp.Value = now; if (shown > now) L.Info($"xp: shown XP {shown} > profile {now} (new profile?) — reset"); }
                return;
            }
            int la = ProgData.LevelOfExp(shown), lb = ProgData.LevelOfExp(now);
            if (la <= 0 || lb <= 0) return;
            BuildXpSteps(shown, now, la, lb);
            _xpFrom = shown; _xpTo = now;
            _xpPending = true;
            // the screen opens on the old state; the animation starts once the loading screen / fade-in is done
            _xpShownLevel = la;
            ShowXpAt(shown, la);
            UpdateHeader(la);
            L.Info($"xp: +{Thousands(now - shown)} EXP since the screen last showed it ({shown} → {now}); level {la} → {lb}; animation {_xpSteps.Count} step(s)");
        }

        private static void BuildXpSteps(int from, int to, int la, int lb)
        {
            _xpSteps.Clear();
            int levels = lb - la;
            float full = Mathf.Clamp(1.2f / Mathf.Sqrt(levels + 1), .16f, .9f); // one whole level's fill
            float pop = levels > 6 ? .22f : .5f;
            float Frac(int exp, int level) => ProgData.ExpInLevel(exp, level, out int h, out int n) ? Mathf.Clamp01(h / (float)n) : 1f;
            float fa = Frac(from, la);
            if (levels == 0)
            {
                float fb = Frac(to, lb);
                _xpSteps.Add(new XpStep { Kind = StepKind.Fill, Level = la, From = fa, To = fb, Dur = Mathf.Clamp(.5f + (fb - fa) * 1.2f, .6f, 1.6f) });
                return;
            }
            _xpSteps.Add(new XpStep { Kind = StepKind.Fill, Level = la, From = fa, To = 1, Dur = Mathf.Max(.2f, full * (1 - fa) + .15f) });
            for (int l = la + 1; l <= lb; l++)
            {
                bool rank = TierOf(l).Name != TierOf(l - 1).Name;
                _xpSteps.Add(new XpStep { Kind = StepKind.LevelUp, Level = l, Dur = pop + (rank ? .75f : 0), NewRank = rank });
                float end = l == lb ? Frac(to, lb) : 1;
                if (l == lb && end <= 0) break;
                _xpSteps.Add(new XpStep { Kind = StepKind.Fill, Level = l, From = 0, To = end, Dur = l == lb ? Mathf.Clamp(.4f + end * .9f, .4f, 1.3f) : full });
            }
            // many levels at once: squeezed into ~7 s (rank changes keep at least half a second)
            float total = 0;
            foreach (var st in _xpSteps) total += st.Dur;
            if (total > 7f)
            {
                float k = 7f / total;
                for (int i = 0; i < _xpSteps.Count; i++)
                {
                    var st = _xpSteps[i];
                    st.Dur = st.NewRank ? Mathf.Max(.5f, st.Dur * k) : Mathf.Max(.08f, st.Dur * k);
                    _xpSteps[i] = st;
                }
            }
        }

        private static void RunXpAnim()
        {
            var input = UnityInput.Current;
            if (_xpPending)
            {
                if (Time.unscaledTime - _openedAt < .45f) return; // let the screen fade in first
                _xpPending = false; _xpRunning = true; _xpStep = 0; _xpT = 0;
                ShowGain(true);
                L.Info("xp: animation started");
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            AnimatePops(dt);
            if (!_xpRunning) { FadeGain(); return; }
            if (input.GetMouseButtonDown(0) || input.GetKeyDown(KeyCode.Space)) { FinishXpAnim("skipped"); return; }
            if (_xpStep >= _xpSteps.Count) { FinishXpAnim("done"); return; }
            var st = _xpSteps[_xpStep];
            bool enter = _xpT == 0;
            _xpT += dt;
            float k = Mathf.Clamp01(_xpT / Mathf.Max(.01f, st.Dur));
            if (st.Kind == StepKind.Fill)
            {
                // the last fill slows into place, the ones in between run straight through
                bool last = _xpStep == _xpSteps.Count - 1;
                float e = last ? 1 - Mathf.Pow(1 - k, 3) : (_xpStep == 0 ? k * k * (3 - 2 * k) : k);
                float frac = Mathf.Lerp(st.From, st.To, e);
                if (ProgData.ExpInLevel(0, st.Level, out _, out int need))
                    ShowXpState(st.Level, Mathf.RoundToInt(frac * need), need, true);
                _xpFill.anchorMax = new Vector2(frac, 1);
                PlaceGain();
            }
            else if (enter)
            {
                // LEVEL UP: the number pops, the caption says it, the bar flashes full and starts over
                _xpShownLevel = st.Level;
                ProgData.ExpInLevel(0, st.Level, out _, out int need);
                ShowXpState(st.Level, 0, Mathf.Max(1, need), need > 0);
                _xpPop = 0;
                Ui.SetText(_xpCaption, $"<color={Orange}>LEVEL UP</color>");
                Sounds.Play("QuestCompleted", "QuestFinished", "TradeOperationComplete", "ButtonClick");
                if (st.NewRank)
                {
                    _xpRankPop = 0;
                    UpdateHeader(st.Level); // new emblem + rank name
                    Sounds.Play("InsuranceInsured", "DeathmatchWin", "MenuDropdownSelect", "ButtonClick");
                    L.Info($"xp: new rank {TierOf(st.Level).Name} at level {st.Level}");
                }
                else UpdateHeader(st.Level); // the unlocked count grows with each level
                L.Debug($"xp: level up → {st.Level}");
            }
            if (k >= 1) { _xpStep++; _xpT = 0; }
        }

        /// <summary>The level square's pop, the bar's flash and the rank emblem's glow (run on after the steps too).</summary>
        private static void AnimatePops(float dt)
        {
            if (_xpPop >= 0)
            {
                _xpPop += dt;
                float p = Mathf.Clamp01(_xpPop / .42f);
                float s = 1 + .28f * Mathf.Sin(p * Mathf.PI) * (1 - p * .5f);
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
                float p = Mathf.Clamp01(_xpRankPop / .7f);
                // shrinks in, overshoots, settles; a warm glow behind it blooms and fades
                float s = p < .25f ? Mathf.Lerp(1, .7f, p / .25f) : 1 + .22f * Mathf.Sin((p - .25f) / .75f * Mathf.PI) * (1 - p);
                _headBadge.Root.localScale = new Vector3(s, s, 1);
                var glow = RankGlow();
                if (glow != null) glow.color = new Color(1f, .45f, .2f, .55f * Mathf.Sin(p * Mathf.PI));
                if (p >= 1) { _xpRankPop = -1; _headBadge.Root.localScale = Vector3.one; if (glow != null) glow.color = new Color(0, 0, 0, 0); }
            }
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

        /// <summary>"+12 400 EXP" after the EXP tag while it plays; fades out after.</summary>
        private static void ShowGain(bool on)
        {
            if (_xpGain == null && _xpRight != null)
                _xpGain = Ui.Label(Ui.Rect(_xpRight, "Gain", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -60), new Vector2(260, -34)), "Text", "", TStrong, Ui.Hex(Orange), TextAnchor.MiddleLeft, true);
            if (_xpGain == null) return;
            _xpGain.gameObject.SetActive(on);
            if (!on) return;
            Ui.SetText(_xpGain, $"+{Thousands(_xpTo - _xpFrom)}");
            _xpGainFadeAt = -1;
        }

        private static float _xpGainFadeAt = -1;

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
            var rt = (RectTransform)_xpGain.transform;
            rt.offsetMin = new Vector2(_xpTextWidth + 12 + 40 + 12, rt.offsetMin.y);
            rt.offsetMax = new Vector2(_xpTextWidth + 12 + 40 + 12 + 260, rt.offsetMax.y);
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
            bool ran = _xpRunning;
            _xpPending = _xpRunning = false;
            _xpSteps.Clear();
            ProgressionPlugin.ShownXp.Value = _xpTo;
            UpdateXp();
            PlaceGain();
            if (_xpGain != null && _xpGain.gameObject.activeSelf) { Ui.SetColor(_xpGain, Ui.Hex(Orange)); _xpGainFadeAt = Time.unscaledTime + 1.6f; }
            if (!ran) { _xpPop = _xpRankPop = -1; }
            L.Info($"xp: animation {why}; shown XP now {_xpTo}");
        }
    }
}
