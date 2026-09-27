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

        private static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private static readonly List<(string Name, UnityWebRequest Req)> _loading = new List<(string, UnityWebRequest)>();
        private static AudioSource _source;
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
                    L.Info($"sounds: {name}.mp3 loaded ({clip.length:0.00} s)");
                }
                catch (Exception e) { L.Error("sound " + name, e); }
                finally { req.Dispose(); }
            }
        }

        /// <summary>F12 > General > GameSounds: the game's own UI sounds instead of the mp3s (played on the pop / the swap).</summary>
        public static bool UseGame => ProgressionPlugin.GameSounds?.Value ?? false;

        public static void Stop() { if (_source != null) _source.Stop(); }

        /// <summary>Plays one of our sounds; false if it isn't loaded (the caller can fall back).</summary>
        public static bool Play(string name)
        {
            if (!_clips.TryGetValue(name, out var clip) || clip == null) return false;
            try
            {
                if (_source == null)
                {
                    var go = new GameObject("LevelGateProgressionSfx");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _source = go.AddComponent<AudioSource>();
                    _source.spatialBlend = 0; _source.playOnAwake = false; _source.ignoreListenerPause = true;
                }
                _source.PlayOneShot(clip, Mathf.Clamp01(ProgressionPlugin.SoundVolume.Value));
                return true;
            }
            catch (Exception e) { L.ErrorOnce("playing " + name, e); return false; }
        }
    }
}
