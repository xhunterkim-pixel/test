using System;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// A small message at the top of the screen ("Refreshing item icons… 120 / 903", "Item icons refreshed"), so a
    /// setting that works in the background (F12 > Graphics) says what it's doing and when it's done.
    /// Its own overlay canvas: shows on the main menu and over the Progression screen alike. Never in a raid.
    /// </summary>
    internal static class Toast
    {
        private static GameObject _root;
        private static CanvasGroup _group;
        private static Component _text;
        private static float _hideAt;
        private static string _shown;

        /// <summary>Shows text for a few seconds (the same text again just keeps it up).</summary>
        public static void Show(string text, float seconds = 4f)
        {
            try
            {
                if (MenuHook.InRaid()) return;
                if (_root == null) Build();
                if (_root == null) return;
                if (text != _shown) { Ui.SetText(_text, text); _shown = text; L.Debug("toast: " + text); }
                _root.SetActive(true);
                _hideAt = Time.unscaledTime + seconds;
            }
            catch (Exception e) { L.ErrorOnce("toast", e); }
        }

        public static void Tick()
        {
            if (_root == null || !_root.activeSelf) return;
            float left = _hideAt - Time.unscaledTime;
            _group.alpha = Mathf.Clamp01(left / .3f); // fades out over the last 0.3 s
            if (left <= 0 || MenuHook.InRaid()) { _root.SetActive(false); _shown = null; }
        }

        private static void Build()
        {
            _root = new GameObject("LevelGateProgressionToast", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;
            _group = _root.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false; _group.interactable = false;

            var box = Ui.Box(_root.transform, "Box", new Vector2(.5f, 1), new Vector2(0, -52), new Vector2(560, 40));
            Ui.Img(box, Ui.Hex("#0f1416", .94f));
            Ui.Img(Ui.Rect(box, "Edge", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(3, 0)), Ui.Hex("#e0562f"));
            _text = Ui.Label(Ui.Rect(box, "Text", Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-12, 0)), "Text", "", 14, Ui.Hex("#d9dfdc"), TextAnchor.MiddleLeft);
            _root.SetActive(false);
        }
    }
}
