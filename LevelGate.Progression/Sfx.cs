using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LevelGate.Progression
{
    /// <summary>
    /// The screen's own sounds (sounds\levelup.mp3, sounds\emblemup.mp3 next to the DLL), loaded once at start and played
    /// on a 2D AudioSource. The XP animation is timed to them: Length / the peaks below.
    /// If a file is missing or can't be decoded, Play falls back to one of the game's UI sounds.
    /// </summary>
    internal static class Sfx
    {
        // where each sound hits hardest (measured from the files' waveforms), in seconds from its start
        public const float LevelUpPeak = .45f, LevelUpLength = 1.46f;
        /// <summary>1.0.19: the rank-up sound is sounds\new_rank_prestige.wav when it's there (peak 0.29 s, hits at 0 and
        /// 0.1 s, 1.31 s long — measured from the trimmed file), else the old emblemup.mp3 (peak 1.45 s).</summary>
        private static bool _prestige;
        public static float EmblemPeak => _prestige ? .29f : 1.45f;
        public static float EmblemLength => _prestige ? 1.31f : 4.28f;
        /// <summary>Emblemup.mp3's two early hits (the glow pulses on them).</summary>
        public static float[] EmblemHits => _prestige ? PrestigeHits : OldHits;
        private static readonly float[] PrestigeHits = { .0f, .095f }, OldHits = { .25f, 1.05f };

        // where the sound starts in the file (first sample over 0.02), measured with ffmpeg: Unity's decoder can put silence in
        // front of it (encoder delay), which would make every peak land late — measured again at load and made up for
        private static readonly Dictionary<string, float> _refOnset = new Dictionary<string, float> { { "levelup", .158f }, { "emblemup", .087f } };
        private static readonly Dictionary<string, float> _extra = new Dictionary<string, float>();

        /// <summary>Extra silence Unity's decode puts before the sound (0 if none / unknown).</summary>
        public static float Extra(string name) => _extra.TryGetValue(name, out var e) ? e : 0;

        /// <summary>How long before the moment it should hit a sound must start, at this pitch.</summary>
        public static float Lead(string name, float pitch = 1)
            => ((name == "emblemup" ? EmblemPeak : LevelUpPeak) + Extra(name)) / Mathf.Max(.5f, pitch);

        private static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private static readonly List<(string Name, UnityWebRequest Req)> _loading = new List<(string, UnityWebRequest)>();
        private static bool _started;

        public static void Load()
        {
            if (_started) return;
            _started = true;
            LoadOne("levelup");
            var dir = Path.Combine(Path.GetDirectoryName(typeof(Sfx).Assembly.Location) ?? ".", "sounds");
            _prestige = File.Exists(Path.Combine(dir, "new_rank_prestige.wav"));
            if (_prestige) { _refOnset["emblemup"] = .002f; LoadOne("emblemup", true, "new_rank_prestige"); }
            else LoadOne("emblemup");
            foreach (var name in UiSounds) LoadOne(name, true); // 1.0.19: the screen's own UI sounds (.wav)
        }

        /// <summary>1.0.19: the UI sounds (sounds\&lt;name&gt;.wav): the file name says when each plays. Trimmed so each starts
        /// on its first sound (no silence before it) and levelled against each other. A missing one: the game's sound instead.</summary>
        public static readonly string[] UiSounds =
        {
            "hover", "hover_over_progression_menu", "enter_progression_ui", "select_level_card", "using_a_or_d_for_level_card",
            "select_level_item", "pressing_q_or_e_for_next_pages", "going_before_level_1_or_level_79", "expand_group_items", "collapse_group_items",
        };

        private static void LoadOne(string name, bool wav = false, string file = null)
        {
            var dir = Path.Combine(Path.GetDirectoryName(typeof(Sfx).Assembly.Location) ?? ".", "sounds");
            var path = Path.Combine(dir, (file ?? name) + (wav ? ".wav" : ".mp3"));
            if (!File.Exists(path)) { L.Warn($"sounds: {path} not found — the game's own sound is used instead"); return; }
            try
            {
                var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, wav ? AudioType.WAV : AudioType.MPEG);
                ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = false; // decoded into memory: nothing is read from disk when it plays
                req.SendWebRequest();
                _loading.Add((name, req));
            }
            catch (Exception e) { L.Error("loading sound " + name, e); }
        }

        /// <summary>
        /// 1.0.4: on every open — a clip the game let go of (a raid can unload audio data, or reset the audio device) is
        /// loaded again, so it's ready before the XP animation needs it. (A real level up after a raid played no sound.)
        /// </summary>
        public static void Check()
        {
            if (!_started) return;
            foreach (var name in new[] { "levelup", "emblemup" }.Concat(UiSounds))
            {
                if (_loading.Exists(l => l.Name == name)) continue;
                _clips.TryGetValue(name, out var clip);
                if (clip != null && clip.loadState == AudioDataLoadState.Loaded) continue;
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded && clip.LoadAudioData()) { L.Info($"sounds: {name} was unloaded by the game — loading its audio again"); continue; }
                if (clip != null && clip.loadState == AudioDataLoadState.Loading) continue;
                if (clip == null && !_clips.ContainsKey(name)) continue; // never loaded (missing file: already said at start)
                bool prestige = name == "emblemup" && _prestige, wav = prestige || !_refOnset.ContainsKey(name);
                L.Info($"sounds: {name} is {(clip == null ? "gone" : clip.loadState.ToString())} — reading it from disk again");
                _clips.Remove(name);
                LoadOne(name, wav, prestige ? "new_rank_prestige" : null);
            }
        }

        /// <summary>Collects finished loads (every frame, cheap).</summary>
        public static void Tick()
        {
            for (int i = _loading.Count - 1; i >= 0; i--)
            {
                var (name, req) = _loading[i];
                if (!req.isDone) continue;
                _loading.RemoveAt(i);
                try
                {
                    if (req.result != UnityWebRequest.Result.Success) { L.Warn($"sounds: {name}.mp3: {req.error}"); continue; }
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip == null) { L.Warn($"sounds: {name}.mp3 couldn't be decoded"); continue; }
                    clip.name = name;
                    _clips[name] = clip;
                    float onset = Onset(clip);
                    if (onset >= 0 && _refOnset.TryGetValue(name, out var reference)) _extra[name] = Mathf.Clamp(onset - reference, 0, .2f);
                    if (name == "emblemup" && _prestige) L.Info($"sounds: new_rank_prestige.wav loaded as the rank-up sound ({clip.length:0.00} s; peak at {EmblemPeak:0.00} s)");
                    else if (_refOnset.ContainsKey(name)) L.Info($"sounds: {name}.mp3 loaded ({clip.length:0.00} s; starts at {onset:0.000} s, {Extra(name) * 1000:0} ms later than in the file — timing adjusted)");
                    else L.Info($"sounds: {name}.wav loaded ({clip.length:0.00} s, sound starts at {Math.Max(0, onset) * 1000:0} ms)");
                }
                catch (Exception e) { L.Error("sound " + name, e); }
                finally { req.Dispose(); }
            }
        }

        /// <summary>F12 > General > GameSounds: the game's own UI sounds instead of the mp3s (played on the pop / the swap).</summary>
        public static bool UseGame => ProgressionPlugin.GameSounds?.Value ?? false;

        public static void Stop() { foreach (var s in _pool) if (s != null) s.Stop(); }

        /// <summary>First sample louder than 0.02 (seconds), or -1.</summary>
        private static float Onset(AudioClip clip)
        {
            try
            {
                int frames = Mathf.Min(clip.samples, clip.frequency / 2), ch = Mathf.Max(1, clip.channels);
                var data = new float[frames * ch];
                if (!clip.GetData(data, 0)) return -1;
                for (int i = 0; i < data.Length; i++) if (Mathf.Abs(data[i]) > .02f) return i / ch / (float)clip.frequency;
            }
            catch (Exception e) { L.Debug("sound onset: " + e.Message); }
            return -1;
        }

        private static readonly AudioSource[] _pool = new AudioSource[8]; // 1.0.19: 8 (UI sounds overlap: hover over hover)
        private static int _next;

        /// <summary>Plays one of our sounds (pitch: higher and shorter); false if it isn't loaded (the caller can fall back).</summary>
        public static bool Play(string name, float pitch = 1)
        {
            if (!_clips.TryGetValue(name, out var clip) || clip == null) { L.Info($"sfx: {name} not ready (not loaded) — the game's sound instead"); Check(); return false; }
            try
            {
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    L.Info($"sfx: {name} not ready ({clip.loadState}) — the game's sound instead");
                    Check();
                    return false;
                }
                if (_pool[0] == null || !_pool[0].isActiveAndEnabled)
                {
                    if (_pool[0] != null) { L.Info("sfx: our sound player was switched off — made again"); UnityEngine.Object.Destroy(_pool[0].gameObject); }
                    var go = new GameObject("LevelGateProgressionSfx");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    for (int i = 0; i < _pool.Length; i++)
                    {
                        var s = go.AddComponent<AudioSource>();
                        s.spatialBlend = 0; s.playOnAwake = false; s.ignoreListenerPause = true;
                        _pool[i] = s;
                    }
                }
                // its own source each (a pitch per sound; a new one doesn't cut the tail of the last)
                var src = _pool[_next++ % _pool.Length];
                src.Stop();
                src.clip = clip; src.pitch = pitch; src.volume = Mathf.Clamp01(ProgressionPlugin.SoundVolume.Value);
                src.Play();
                // what the player hears: nothing when there's no listener, it's muted or the volume is 0 (all logged, so a silent
                // level up can be told apart from one that played)
                var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
                string odd = !src.isPlaying ? "didn't start" : listener == null ? "no audio listener in the scene" : AudioListener.volume < .05f ? $"game master volume {AudioListener.volume:0.00}"
                           : src.volume < .05f ? "Sound Volume is 0" : null;
                string line = $"sfx: {name} (pitch {pitch:0.00}, volume {src.volume:0.00}, listener {(listener != null ? listener.name : "none")} at {AudioListener.volume:0.00}{(AudioListener.pause ? ", paused" : "")})";
                if (odd != null) L.Info(line + " — " + odd); else L.Trace(line);
                return src.isPlaying;
            }
            catch (Exception e) { L.ErrorOnce("playing " + name, e); return false; }
        }
    }
}
