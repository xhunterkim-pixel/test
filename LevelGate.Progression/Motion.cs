using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelGate.Progression
{
    /// <summary>
    /// The screen's one motion system: every animation's timing, easing and rhythm comes from here, so the screen moves as one
    /// piece instead of many effects each with their own numbers.
    ///
    /// - Durations are TOKENS (Micro … Epic), never raw numbers, scaled by one F12 Motion Speed and instant under Reduce
    ///   Motion / Performance Mode (<see cref="D"/>).
    /// - One EASING library (<see cref="Eval"/>): entrances ease out, exits ease in, emphasis overshoots, loops are sine.
    /// - TWEENS are keyed by owner + channel (<see cref="To"/>): a new one on the same channel replaces the old one and starts
    ///   from wherever that one had got to, so hover on → off → on never jumps. They end themselves when their owner (a
    ///   Unity object) is destroyed.
    /// - STAGGER (<see cref="Stagger"/>): lists come in one after another at one shared step, capped so long lists don't drag.
    /// - SEQUENCES (<see cref="Seq"/>): then / wait / call / together, for choreographed beats.
    /// - One CLOCK (<see cref="Wave"/>, <see cref="Flicker"/>): every pulse, breath and flicker reads the same time, so
    ///   they stay in phase with each other.
    /// Ticked once per frame from ProgScreen.Tick (unscaled time: works while the game is paused or slowed).
    /// </summary>
    internal static class Motion
    {
        // ---------------------------------------------------------------- tokens

        /// <summary>Tiny feedback: a tick, a flicker step.</summary>
        public const float Micro = .08f;
        /// <summary>Hover, press, small state changes.</summary>
        public const float Fast = .15f;
        /// <summary>Selection, borders, most transitions.</summary>
        public const float Base = .25f;
        /// <summary>Panels, sweeps, lights settling.</summary>
        public const float Slow = .4f;
        /// <summary>The hero: the big picture's load-in, the card flood.</summary>
        public const float Hero = .6f;
        /// <summary>Beats of the level-up / new-rank sequence.</summary>
        public const float Epic = 1.2f;

        /// <summary>F12 > CURRENTLY TESTING > Motion Speed (1 = as designed; 2 = twice as fast).</summary>
        public static float Speed => Mathf.Clamp((ProgressionPlugin.TestMotionSpeed?.Value ?? 145) / 100f, .25f, 4f);

        /// <summary>Reduce Motion / Performance Mode: everything lands at once.</summary>
        public static bool Still => ProgScreen.Calm;

        /// <summary>A token's duration now (speed applied; 0 when still).</summary>
        public static float D(float token) => Still ? 0 : token / Speed;

        /// <summary>The delay of item i in a list coming in one after another (shared step, capped).</summary>
        public static float Stagger(int i, int cap = 12)
        {
            if (Still) return 0;
            float step = (ProgressionPlugin.TestStagger?.Value ?? 30) / 1000f;
            return Mathf.Min(i, cap) * step / Speed;
        }

        // ---------------------------------------------------------------- the clock

        public static float Now => Time.unscaledTime;

        /// <summary>The shared pulse: -1…1 with this period (seconds), plus an offset in periods. Everything that breathes uses it.</summary>
        public static float Wave(float period, float offset = 0) => Still ? 0 : Mathf.Sin((Now / Mathf.Max(.01f, period) + offset) * Mathf.PI * 2);

        /// <summary>A light flicker, 1 − depth … 1, shared by every flickering light (seed spreads different lights apart).</summary>
        public static float Flicker(float depth = .15f, float seed = 0)
        {
            if (Still) return 1;
            float t = Now * 1.7f + seed * 7.13f;
            float n = Mathf.Sin(t * 17f) * .5f + Mathf.Sin(t * 29f + 1.3f) * .3f + Mathf.Sin(t * 53f + 2.1f) * .2f;
            return 1 - depth * (.5f + .5f * n);
        }

        // ---------------------------------------------------------------- easing

        public enum Ease { Linear, OutCubic, OutQuint, OutExpo, OutBack, InCubic, InOutSine, InOutCubic, Spring, Pulse }

        public static float Eval(Ease e, float t)
        {
            t = Mathf.Clamp01(t);
            switch (e)
            {
                case Ease.OutCubic: return 1 - Mathf.Pow(1 - t, 3);
                case Ease.OutQuint: return 1 - Mathf.Pow(1 - t, 5);
                case Ease.OutExpo: return t >= 1 ? 1 : 1 - Mathf.Pow(2, -10 * t);
                case Ease.OutBack: { const float c1 = 1.70158f, c3 = c1 + 1; return 1 + c3 * Mathf.Pow(t - 1, 3) + c1 * Mathf.Pow(t - 1, 2); }
                case Ease.InCubic: return t * t * t;
                case Ease.InOutSine: return -(Mathf.Cos(Mathf.PI * t) - 1) / 2;
                case Ease.InOutCubic: return t < .5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
                case Ease.Spring: return 1 - Mathf.Exp(-6 * t) * Mathf.Cos(t * 10);   // settles with one soft bounce
                case Ease.Pulse: return Mathf.Sin(t * Mathf.PI);                         // 0 → 1 → 0
                default: return t;
            }
        }

        // ---------------------------------------------------------------- tweens

        private sealed class Tween
        {
            public object Owner; public string Channel;
            public float Start, Delay, Dur, From, To, Value;
            public Ease Ease; public Action<float> Apply; public Action Done;
            public bool Dead;
        }

        private static readonly List<Tween> _tweens = new List<Tween>();
        private static readonly Dictionary<(object, string), Tween> _byKey = new Dictionary<(object, string), Tween>();
        private static readonly Dictionary<(object, string), float> _last = new Dictionary<(object, string), float>();

        /// <summary>
        /// Animates a value on owner's channel from where it is now (the last value this channel had, else `from`) to `to`.
        /// Replaces whatever that channel was doing. apply is called every frame with the eased value; done once at the end.
        /// </summary>
        public static void To(object owner, string channel, float from, float to, float dur, Ease ease, Action<float> apply, float delay = 0, Action done = null)
        {
            var key = (owner, channel);
            if (_last.TryGetValue(key, out var at)) from = at;
            if (_byKey.TryGetValue(key, out var old)) { old.Dead = true; _byKey.Remove(key); }
            if (dur <= 0 && delay <= 0) { _last[key] = to; apply?.Invoke(to); done?.Invoke(); return; }
            var tw = new Tween { Owner = owner, Channel = channel, Start = Now, Delay = delay, Dur = Mathf.Max(.0001f, dur), From = from, To = to, Value = from, Ease = ease, Apply = apply, Done = done };
            _tweens.Add(tw); _byKey[key] = tw;
        }

        /// <summary>Where a channel is (its last value), or `fallback` if it never ran.</summary>
        public static float ValueOf(object owner, string channel, float fallback = 0) => _last.TryGetValue((owner, channel), out var v) ? v : fallback;

        /// <summary>Forgets where a channel was, so the next To starts from its own `from` (a flash that jumps to its peak).</summary>
        public static void Reset(object owner, string channel) { _last.Remove((owner, channel)); }

        public static bool Running(object owner, string channel) => _byKey.ContainsKey((owner, channel));

        /// <summary>Stops a channel where it is (or every channel of the owner).</summary>
        public static void Kill(object owner, string channel = null)
        {
            foreach (var t in _tweens) if (ReferenceEquals(t.Owner, owner) && (channel == null || t.Channel == channel)) t.Dead = true;
        }

        private static bool Gone(object owner) => owner is UnityEngine.Object uo ? uo == null : owner == null;

        // ---------------------------------------------------------------- sequences

        /// <summary>A choreographed sequence: steps run one after another; Together runs its steps at the same time.</summary>
        public sealed class Sequence
        {
            internal readonly List<(float At, Action Start)> Steps = new List<(float, Action)>();
            internal float Cursor;
            private readonly object _owner;
            internal Sequence(object owner) { _owner = owner; }

            /// <summary>Then: a tween that starts when the previous step ends.</summary>
            public Sequence Then(string channel, float from, float to, float dur, Ease ease, Action<float> apply)
            {
                float at = Cursor; var o = _owner;
                Steps.Add((at, () => To(o, channel, from, to, dur, ease, apply)));
                Cursor += dur;
                return this;
            }

            /// <summary>With: a tween that starts together with the previous step (doesn't move the cursor further).</summary>
            public Sequence With(string channel, float from, float to, float dur, Ease ease, Action<float> apply, float offset = 0)
            {
                float at = Mathf.Max(0, Cursor - dur) + offset; var o = _owner;
                Steps.Add((at, () => To(o, channel, from, to, dur, ease, apply)));
                return this;
            }

            public Sequence Wait(float t) { Cursor += t; return this; }
            public Sequence Call(Action a) { Steps.Add((Cursor, a)); return this; }

            /// <summary>Starts it (steps are fired by Motion.Tick at their times).</summary>
            public void Play() { float t0 = Now; foreach (var s in Steps) _pending.Add((t0 + s.At, _owner, s.Start)); }
        }

        private static readonly List<(float At, object Owner, Action Start)> _pending = new List<(float, object, Action)>();

        public static Sequence Seq(object owner) => new Sequence(owner);

        // ---------------------------------------------------------------- the frame

        /// <summary>Once per frame (ProgScreen.Tick): fires due sequence steps, advances every tween.</summary>
        public static void Tick()
        {
            float now = Now;
            if (_pending.Count > 0)
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    var p = _pending[i];
                    if (now < p.At) continue;
                    _pending.RemoveAt(i--);
                    if (Gone(p.Owner)) continue;
                    try { p.Start?.Invoke(); } catch (Exception e) { L.ErrorOnce("motion sequence", e); }
                }
            }
            for (int i = 0; i < _tweens.Count; i++)
            {
                var t = _tweens[i];
                if (t.Dead || Gone(t.Owner)) { Drop(i--); continue; }
                float u = (now - t.Start - t.Delay) / t.Dur;
                if (u < 0) continue;
                float v = Mathf.LerpUnclamped(t.From, t.To, Eval(t.Ease, u));
                t.Value = v;
                _last[(t.Owner, t.Channel)] = v;
                try { t.Apply?.Invoke(v); } catch (Exception e) { L.ErrorOnce("motion tween " + t.Channel, e); t.Dead = true; }
                if (u >= 1)
                {
                    _last[(t.Owner, t.Channel)] = t.To;
                    var done = t.Done;
                    Drop(i--);
                    try { done?.Invoke(); } catch (Exception e) { L.ErrorOnce("motion done " + t.Channel, e); }
                }
            }
            // forget the last values of owners that are gone (tiles rebuilt while browsing)
            if (_last.Count > 2000)
            {
                var gone = new List<(object, string)>();
                foreach (var k in _last.Keys) if (Gone(k.Item1)) gone.Add(k);
                foreach (var k in gone) _last.Remove(k);
            }
        }

        private static void Drop(int i)
        {
            var t = _tweens[i];
            if (_byKey.TryGetValue((t.Owner, t.Channel), out var cur) && cur == t) _byKey.Remove((t.Owner, t.Channel));
            _tweens.RemoveAt(i);
        }

        /// <summary>The screen was thrown away / closed for a raid: nothing left running.</summary>
        public static void Clear() { _tweens.Clear(); _byKey.Clear(); _last.Clear(); _pending.Clear(); }

        public static int ActiveCount => _tweens.Count;
    }
}
