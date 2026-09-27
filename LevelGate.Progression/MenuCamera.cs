using System;
using System.Linq;
using UnityEngine;

namespace LevelGate.Progression
{
    /// <summary>
    /// The main menu's 3D background ("Environment UI/EnvironmentUISceneFactory", seen through
    /// FactoryCameraContainer/MainMenuCamera — or MoxoPixel Menu Overhaul's AlignmentCamera) swings
    /// smoothly to the side while the Progression screen is open, like the game does for Character /
    /// Traders, and back when it closes. The turn is applied after the game has moved the camera each
    /// frame (LateUpdate), so the game's own camera animation keeps working. A blur / depth-of-field
    /// effect on that camera, if there is one, is switched on while open.
    /// </summary>
    internal static class MenuCamera
    {
        private static Camera _cam;
        private static Quaternion _base, _written;
        private static float _angle, _from, _to, _start = -10, _duration = .8f;
        private static bool _active, _logged;
        private static Behaviour _blur;
        private static bool _blurWas;

        private static Camera Find()
        {
            var env = GameObject.Find("Environment UI");
            if (env == null) { L.Debug("camera: 'Environment UI' not in the scene"); return null; }
            var cams = env.GetComponentsInChildren<Camera>(false).Where(c => c.enabled).ToArray();
            if (!_logged)
            {
                _logged = true;
                L.Info("camera: menu background cameras: " + (cams.Length == 0 ? "none active" : string.Join(" | ", cams.Select(c =>
                    $"{MenuHook.Path(c.transform)} (depth {c.depth}; {string.Join(",", c.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name).ToArray())})").ToArray())));
            }
            // the one that draws last is what you see
            return cams.OrderByDescending(c => c.depth).FirstOrDefault();
        }

        /// <summary>Swing away (open) or back (close). instant: no animation (the game is switching screens itself).</summary>
        public static void Turn(bool open, bool instant = false)
        {
            float degrees = ProgressionPlugin.CameraTurn.Value;
            if (Mathf.Abs(degrees) < .1f && open) return;
            try
            {
                if (open)
                {
                    _cam = Find();
                    if (_cam == null) return;
                    _base = _written = _cam.transform.rotation;
                    _active = true;
                    Blur(true);
                    L.Debug($"camera: turning '{_cam.name}' {degrees:0}°");
                }
                else Blur(false);
                if (!_active) return;
                _from = _angle;
                _to = open ? degrees : 0;
                _start = instant ? -10 : Time.unscaledTime;
                if (instant) { _angle = _to; Apply(); if (!open) Stop(); }
            }
            catch (Exception e) { L.ErrorOnce("turning the menu camera", e); }
        }

        /// <summary>Every frame after the game's own updates.</summary>
        public static void LateTick()
        {
            if (!_active) return;
            if (_cam == null) { Stop(); return; }
            float t = Mathf.Clamp01((Time.unscaledTime - _start) / _duration);
            float e = t < .5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2; // ease in-out
            _angle = Mathf.Lerp(_from, _to, e);
            // the game moved the camera itself since our last write: that's the new base
            if (Quaternion.Angle(_cam.transform.rotation, _written) > .01f) _base = _cam.transform.rotation;
            Apply();
            if (t >= 1 && Mathf.Abs(_to) < .01f) Stop();
        }

        private static void Apply()
        {
            if (_cam == null) return;
            _written = Quaternion.AngleAxis(_angle, Vector3.up) * _base;
            _cam.transform.rotation = _written;
        }

        private static void Stop()
        {
            if (_cam != null) _cam.transform.rotation = _base;
            _active = false;
            _angle = 0;
        }

        /// <summary>A blur-type effect already on the background camera (name has Blur / DepthOfField / Bokeh).</summary>
        private static void Blur(bool on)
        {
            if (!ProgressionPlugin.BlurBackground.Value) return;
            try
            {
                if (on)
                {
                    _blur = _cam?.GetComponents<Behaviour>().FirstOrDefault(b => b != null && (b.GetType().Name.Contains("Blur") || b.GetType().Name.Contains("DepthOfField") || b.GetType().Name.Contains("Bokeh")) && !b.GetType().Name.Contains("Motion")); // motion blur does nothing on a still camera
                    if (_blur == null) { L.Debug("camera: no blur effect on the background camera (the screen's own shade dims it instead)"); return; }
                    _blurWas = _blur.enabled;
                    _blur.enabled = true;
                    L.Debug($"camera: blur on ({_blur.GetType().Name})");
                }
                else if (_blur != null)
                {
                    _blur.enabled = _blurWas;
                    _blur = null;
                }
            }
            catch (Exception e) { L.ErrorOnce("background blur", e); }
        }
    }
}
