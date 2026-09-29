using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LevelGate.Progression
{
    /// <summary>
    /// 1.0.2 testing phase: extra detail in Progression.log (trace lines), kept cheap — nothing here runs per frame except a
    /// few comparisons; everything is written by the log's background thread. What is logged:
    ///  · at start: the PC (CPU, GPU, RAM, screen, OS), Unity / game version and every other BepInEx plugin loaded
    ///  · the game's own errors and exceptions (Unity's log), de-duplicated, the first 400 of a session
    ///  · every key the plugin reacts to and every mouse click, with the UI object under the mouse and the game screen
    ///  · hitches (frames over 100 ms) anywhere in the menus, with the game screen and how long since it changed
    ///  · every 10 s: frame rate (avg / worst), garbage collections, memory; every 60 s: what logging itself cost (% of time)
    ///  · each game screen: how long it stayed open
    /// </summary>
    internal static class Diag
    {
        private static bool _started;
        private static float _statsAt, _perfAt;
        private static int _frames;
        private static float _worst, _sum;
        private static int _gc0;
        private static string _lastScreen = "";
        private static float _screenAt;
        private static int _gameErrors;
        private static readonly Dictionary<string, float> _errSeen = new Dictionary<string, float>();

        private static readonly KeyCode[] Keys =
        {
            KeyCode.Escape, KeyCode.Space, KeyCode.Return, KeyCode.Tab, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow,
            KeyCode.A, KeyCode.D, KeyCode.W, KeyCode.S, KeyCode.Q, KeyCode.E, KeyCode.P, KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown,
            KeyCode.F12, KeyCode.F10, KeyCode.Delete, KeyCode.Backspace,
        };

        public static void Start()
        {
            if (_started) return;
            _started = true;
            try
            {
                L.Info($"system: {SystemInfo.operatingSystem} · CPU {SystemInfo.processorType} ({SystemInfo.processorCount} threads) · RAM {SystemInfo.systemMemorySize} MB · " +
                       $"GPU {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB, {SystemInfo.graphicsDeviceType}) · screen {Screen.width}x{Screen.height} @ {Screen.currentResolution.refreshRateRatio.value:0} Hz, {(Screen.fullScreen ? Screen.fullScreenMode.ToString() : "windowed")} · " +
                       $"Unity {Application.unityVersion} · game {Application.version} · quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} · vSync {QualitySettings.vSyncCount} · target fps {Application.targetFrameRate}");
                var plugins = BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Select(p => $"{p.Metadata.Name} {p.Metadata.Version}").OrderBy(x => x).ToArray();
                L.Info($"plugins loaded ({plugins.Length}): {string.Join(", ", plugins)}");
            }
            catch (Exception e) { L.Debug("system info: " + e.Message); }
            Application.logMessageReceivedThreaded += OnUnityLog;
            _gc0 = GC.CollectionCount(0);
        }

        /// <summary>The game's errors and exceptions (not its warnings): the first 400, each distinct one at most every 30 s.</summary>
        private static void OnUnityLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (msg != null && msg.StartsWith("[Progression]")) return; // ours: already in the file
            if (_gameErrors >= 400) return;
            try
            {
                string key = (msg ?? "").Length > 160 ? msg.Substring(0, 160) : msg ?? "";
                float now = (float)(DateTime.UtcNow - DateTime.MinValue).TotalSeconds;
                lock (_errSeen)
                {
                    if (_errSeen.TryGetValue(key, out var at) && now - at < 30f) return;
                    _errSeen[key] = now;
                }
                _gameErrors++;
                string where = string.IsNullOrEmpty(stack) ? "" : " @ " + string.Join(" < ", stack.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Take(3).ToArray());
                L.Trace($"game {type.ToString().ToLowerInvariant()}: {msg}{where}{(_gameErrors == 400 ? " (limit reached: no more game errors logged)" : "")}");
            }
            catch { }
        }

        public static void Tick()
        {
            if (!L.Verbose) return;
            float dt = Time.unscaledDeltaTime, now = Time.realtimeSinceStartup;
            _frames++; _sum += dt; if (dt > _worst) _worst = dt;

            // game screens: how long each one stayed
            var scr = MenuHook.CurrentScreen ?? "";
            if (scr != _lastScreen)
            {
                if (_lastScreen.Length > 0) L.Trace($"screen: left {_lastScreen} after {now - _screenAt:0.0} s → {(scr.Length > 0 ? scr : "?")}");
                _lastScreen = scr; _screenAt = now;
            }

            // hitches outside our screen (ours logs its own "slow frame" with the cause)
            if (dt > .1f && !ProgScreen.IsOpen && now > 20f)
                L.Trace($"hitch: {dt * 1000:0} ms on {(scr.Length > 0 ? scr : "?")} ({now - _screenAt:0.0} s after it opened){(GameItems.RepairLeft > 0 ? $", {GameItems.RepairLeft} icon redraws queued" : "")}");

            // keys and clicks
            if (Input.anyKeyDown)
            {
                foreach (var k in Keys)
                    if (Input.GetKeyDown(k)) L.Trace($"key: {k} (screen {scr}{(ProgScreen.IsOpen ? ", Progression open" : "")}{(ProgressionPlugin.Typing() ? ", typing" : "")})");
                for (int b = 0; b < 3; b++)
                    if (Input.GetMouseButtonDown(b)) L.Trace($"click: {(b == 0 ? "left" : b == 1 ? "right" : "middle")} at {Input.mousePosition.x:0},{Input.mousePosition.y:0} on {UnderMouse()} (screen {scr}{(ProgScreen.IsOpen ? ", Progression open" : "")})");
            }
            if (Input.mouseScrollDelta.y != 0 && ProgScreen.IsOpen) { _scroll += Input.mouseScrollDelta.y; _scrollAt = now; }
            if (_scroll != 0 && now - _scrollAt > .4f) { L.Trace($"scroll: {_scroll:+0;-0} on {UnderMouse()}"); _scroll = 0; }

            // every 10 s: frame rate, garbage collections, memory
            if (now >= _statsAt)
            {
                if (_frames > 0 && _statsAt > 0)
                {
                    int gc = GC.CollectionCount(0);
                    L.Trace($"stats: {scr}{(ProgScreen.IsOpen ? " + Progression" : "")} · {_frames / Math.Max(.001f, _sum):0} fps avg, worst frame {_worst * 1000:0} ms · GC {gc - _gc0} · managed {GC.GetTotalMemory(false) / 1048576} MB · " +
                            $"icons: {GameItems.RepairLeft} redraw(s) queued");
                    _gc0 = gc;
                }
                _frames = 0; _sum = 0; _worst = 0; _statsAt = now + 10f;
            }
            // every 60 s: what the log itself costs
            if (now >= _perfAt)
            {
                if (_perfAt > 0) L.Trace($"logging cost: {L.CostMs:0} ms for {L.CostLines} lines in {now:0} s = {L.CostMs / 10.0 / Math.Max(1f, now):0.000}% of the time");
                _perfAt = now + 60f;
            }
        }

        private static float _scroll, _scrollAt;
        private static readonly List<RaycastResult> _hits = new List<RaycastResult>();

        /// <summary>The UI object under the mouse, as a short path (its parent / itself), or "nothing".</summary>
        private static string UnderMouse()
        {
            try
            {
                var es = EventSystem.current;
                if (es == null) return "?";
                _hits.Clear();
                es.RaycastAll(new PointerEventData(es) { position = Input.mousePosition }, _hits);
                if (_hits.Count == 0) return "nothing";
                var t = _hits[0].gameObject.transform;
                string path = t.name;
                for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
                return path;
            }
            catch { return "?"; }
        }
    }
}
