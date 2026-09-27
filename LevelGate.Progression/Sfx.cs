using System;
using System.Collections.Generic;
using System.IO;
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
        public const float EmblemPeak = 1.45f, EmblemLength = 4.28f;
        /// <summary>Emblemup.mp3's two early hits (the glow pulses on them).</summary>
        public static readonly float[] EmblemHits = { .25f, 1.05f };

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
            var dir = Path.Combine(Path.GetDirectoryName(typeof(Sfx).Assembly.Location) ?? ".", "sounds");
            foreach (var name in new[] { "levelup", "emblemup" })
            {
                var path = Path.Combine(dir, name + ".mp3");
                if (!File.Exists(path)) { L.Warn($"sounds: {path} not found — the game's own sound is used instead"); continue; }
                try
                {
                    var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.MPEG);
                    req.SendWebRequest();
                    _loading.Add((name, req));
                }
                catch (Exception e) { L.Error("loading sound " + name, e); }
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
                    L.Info($"sounds: {name}.mp3 loaded ({clip.length:0.00} s; starts at {onset:0.000} s, {Extra(name) * 1000:0} ms later than in the file — timing adjusted)");
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

        private static readonly AudioSource[] _pool = new AudioSource[4];
        private static int _next;

        /// <summary>Plays one of our sounds (pitch: higher and shorter); false if it isn't loaded (the caller can fall back).</summary>
        public static bool Play(string name, float pitch = 1)
        {
            if (!_clips.TryGetValue(name, out var clip) || clip == null) return false;
            try
            {
                if (_pool[0] == null)
                {
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
                return true;
            }
            catch (Exception e) { L.ErrorOnce("playing " + name, e); return false; }
        }
    }
}
