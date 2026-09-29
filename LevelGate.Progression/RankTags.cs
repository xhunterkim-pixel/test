using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace LevelGate.Progression
{
    /// <summary>
    /// 1.0.6: Rank Tags. The server mod (LevelGateProgression) adds one dogtag item per rank, each with its own id, shaped
    /// like the game's Physical Bitcoin (a coin), and mails each when its rank is reached. Here in game every model the game
    /// builds for one of them (loot, inspect, the icon renderer: all go through ObjectsFactory.CreateItemAsync) is re-skinned:
    /// the coin's own renderers are hidden and a steel coin of the same size takes their place, its faces showing the rank's
    /// emblem — animated from the emblem sheet (the gif's frames); it starts on the final frame, so the icon (rendered the
    /// moment the model is made) is the finished emblem. Pooled models are put back to a plain coin when reused for a real one.
    /// </summary>
    internal static class RankTags
    {
        private const string Prefix = "6c67706d72616e6b000000"; // + 2 hex = the rank (0-based), same as the server mod
        private static int _mainThread;
        private static readonly ConcurrentQueue<Action> _later = new ConcurrentQueue<Action>();
        private static int _skinned;

        /// <summary>Rank k's (0-based) item id.</summary>
        public static string TplOf(int k) => Prefix + k.ToString("x2");

        /// <summary>The rank (0-based) of one of our tags, or -1.</summary>
        public static int RankOf(string tpl)
        {
            if (tpl == null || tpl.Length != 24 || !tpl.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return -1;
            return int.TryParse(tpl.Substring(22), System.Globalization.NumberStyles.HexNumber, null, out int k) ? k : -1;
        }

        public static void Apply(Harmony harmony)
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            try
            {
                var factory = AccessTools.TypeByName("EFT.ObjectsFactory");
                var m = factory == null ? null : AccessTools.Method(factory, "CreateItemAsync");
                if (m == null || m.ReturnType != typeof(Task<GameObject>)) { L.Info("rank tags: ObjectsFactory.CreateItemAsync not found — Rank Tags keep the plain coin look"); return; }
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(RankTags).GetMethod(nameof(AfterCreate), BindingFlags.Static | BindingFlags.NonPublic)));
                L.Info("rank tags: hooked " + factory.FullName + ".CreateItemAsync (Rank Tags get their emblem)");
            }
            catch (Exception e) { L.Error("rank tags hook", e); }
        }

        /// <summary>Main thread, every frame: skins that finished on another thread.</summary>
        public static void Tick()
        {
            while (_later.TryDequeue(out var a)) { try { a(); } catch (Exception e) { L.ErrorOnce("rank tag (later)", e); } }
        }

        private static void AfterCreate(object[] __args, ref Task<GameObject> __result)
        {
            try
            {
                if (__result == null || __args == null || __args.Length == 0) return;
                int rank = RankOf(Refl.Get(__args[0], "StringTemplateId") as string);
                if (rank < 0 && _skinned == 0) return; // nothing of ours in any pool yet: nothing to put back
                var src = __result;
                if (src.IsCompleted) { if (src.Status == TaskStatus.RanToCompletion) Dress(src.Result, rank); return; }
                var tcs = new TaskCompletionSource<GameObject>();
                src.ContinueWith(t =>
                {
                    if (t.IsFaulted) { tcs.SetException(t.Exception.InnerExceptions); return; }
                    if (t.IsCanceled) { tcs.SetCanceled(); return; }
                    Dress(t.Result, rank);
                    tcs.SetResult(t.Result);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                __result = tcs.Task;
            }
            catch (Exception e) { L.ErrorOnce("rank tag create", e); }
        }

        private static void Dress(GameObject go, int rank)
        {
            if (go == null) return;
            if (Thread.CurrentThread.ManagedThreadId != _mainThread) { _later.Enqueue(() => Dress(go, rank)); return; }
            try
            {
                var skin = go.GetComponent<RankTagSkin>();
                if (rank < 0) { if (skin != null) skin.Undo(); return; } // a pooled coin reused for a real item
                if (skin == null) skin = go.AddComponent<RankTagSkin>();
                if (skin.Set(rank)) _skinned++;
            }
            catch (Exception e) { L.ErrorOnce("rank tag skin", e); }
        }

        // ---------------------------------------------------------------- the look (built once per rank)

        // ---------------------------------------------------------------- tuning (F12 › CURRENTLY TESTING)

        internal static float Shine => Mathf.Clamp01((ProgressionPlugin.TestRankTagShine?.Value ?? 20) / 100f);
        internal static float Steel => Mathf.Clamp01((ProgressionPlugin.TestRankTagSteel?.Value ?? 40) / 100f);
        /// <summary>Goes up on every dial change: the skins shown re-apply their look.</summary>
        internal static int TuneVersion;
        private const int LookVersion = 2; // 1.0.7: bump when the look changes (the stash icons are drawn again once)

        /// <summary>A Rank Tag dial moved: the looks are made again and the skins shown pick them up; the stash icons redraw.</summary>
        public static void Retune()
        {
            foreach (var l in _looks.Values) if (l?.Sheet != null) UnityEngine.Object.Destroy(l.Sheet);
            _looks.Clear();
            TuneVersion++;
            RedrawIcons("look changed");
        }

        /// <summary>The game keeps item icons in a cache on disk: when the look changed since they were drawn, draw them again.</summary>
        public static void CheckIcons()
        {
            try
            {
                string want = $"{LookVersion}|{ProgressionPlugin.TestRankTagShine?.Value}|{ProgressionPlugin.TestRankTagSteel?.Value}";
                string file = Path.Combine(Path.GetDirectoryName(typeof(RankTags).Assembly.Location) ?? ".", "ranktag_icons.txt");
                if (File.Exists(file) && File.ReadAllText(file).Trim() == want) return;
                RedrawIcons("first start with this look");
                File.WriteAllText(file, want);
            }
            catch (Exception e) { L.ErrorOnce("rank tag icons", e); }
        }

        private static void RedrawIcons(string why)
        {
            var tpls = new List<string>();
            for (int k = 0; k < 16; k++) tpls.Add(TplOf(k));
            L.Info($"rank tags: stash icons redrawn ({why}; on the main menu)");
            GameItems.RepairAll(tpls);
        }

        /// <summary>
        /// The coin's own material is the Physical Bitcoin's: gold, and very reflective. Every property of its shader is set
        /// by what its name says: spec / reflection colours to a neutral grey scaled by Shine, gloss / smoothness down,
        /// emission off, tints white, maps other than the emblem neutral. Logged once per shader (what it had), so the
        /// next log shows exactly what the game's shader offers.
        /// </summary>
        internal static void Tune(Material m, string mainProp)
        {
            var sh = m.shader;
            if (sh == null) return;
            bool log = _loggedShaders.Add(sh.name);
            var seen = log ? new List<string>() : null;
            float shine = Shine;
            int n = sh.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string name = sh.GetPropertyName(i), ln = name.ToLowerInvariant();
                var type = sh.GetPropertyType(i);
                try
                {
                    switch (type)
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Color:
                        case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        {
                            if (type == UnityEngine.Rendering.ShaderPropertyType.Vector && !ln.Contains("color")) break;
                            if (log) seen.Add($"{name}={m.GetColor(name)}");
                            if (ln.Contains("emis")) m.SetColor(name, Color.black);
                            else if (ln.Contains("spec")) m.SetColor(name, Grey(.08f + .5f * shine));
                            else if (ln.Contains("refl") || ln.Contains("cube") || ln.Contains("fresnel") || ln.Contains("rim") || ln.Contains("env")) m.SetColor(name, Grey(.02f + .35f * shine));
                            else if (name == "_Color" || name == "_BaseColor" || ln.Contains("tint") || ln.Contains("diffuse") || ln.Contains("albedo")) m.SetColor(name, Color.white);
                            break;
                        }
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                        {
                            if (log) seen.Add($"{name}={m.GetFloat(name):0.###}");
                            float lo = 0, hi = 1;
                            if (type == UnityEngine.Rendering.ShaderPropertyType.Range) { var r = sh.GetPropertyRangeLimits(i); lo = r.x; hi = r.y; }
                            if (ln.Contains("gloss") || ln.Contains("smooth") || ln.Contains("shin")) m.SetFloat(name, Mathf.Lerp(lo, hi, .15f + .55f * shine));
                            else if (ln.Contains("metal")) m.SetFloat(name, Mathf.Lerp(lo, hi, .3f + .5f * shine));
                            else if (ln.Contains("refl") || ln.Contains("fresnel") || ln.Contains("env") || ln.Contains("cube")) m.SetFloat(name, Mathf.Lerp(lo, hi, .03f + .4f * shine));
                            else if (ln.Contains("emis")) m.SetFloat(name, lo);
                            break;
                        }
                        case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        {
                            var dim = sh.GetPropertyTextureDimension(i);
                            if (log) seen.Add($"{name}({dim})={(m.GetTexture(name) != null ? m.GetTexture(name).name : "none")}");
                            if (name == mainProp || dim != UnityEngine.Rendering.TextureDimension.Tex2D) break; // cubemaps stay (their strength is the colour above)
                            if (ln.Contains("bump") || ln.Contains("normal")) m.SetTexture(name, FlatNormal());
                            else if (ln.Contains("emis")) m.SetTexture(name, Texture2D.blackTexture);
                            else if (ln.Contains("spec") || ln.Contains("gloss") || ln.Contains("metal") || ln.Contains("smap") || ln.Contains("mask") || ln.Contains("refl")) m.SetTexture(name, Grey());
                            else if (ln.Contains("detail")) m.SetTexture(name, null);
                            break;
                        }
                    }
                }
                catch (Exception e) { if (log) seen.Add($"{name}: {e.GetBaseException().Message}"); }
            }
            if (log) L.Info($"rank tags: coin shader '{sh.name}' ({n} properties): {string.Join(", ", seen.ToArray())}");
        }

        private static readonly HashSet<string> _loggedShaders = new HashSet<string>();
        private static Color Grey(float v) => new Color(v, v, v, 1);

        internal sealed class Look
        {
            public Texture2D Sheet; // the emblem sheet on steel
            public int Frames, Columns, Rows, Ms;
        }

        private static readonly Dictionary<int, Look> _looks = new Dictionary<int, Look>();
        private static Mesh _coin;
        private static Texture2D _flatNormal, _grey;

        internal static Look LookOf(int rank)
        {
            if (_looks.TryGetValue(rank, out var have)) return have;
            Look look = null;
            try
            {
                if (Emblems.FileOf(rank, out var path, out int frames, out int cols, out int size, out int ms))
                {
                    float t0 = Time.realtimeSinceStartup;
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (ImageConversion.LoadImage(tex, File.ReadAllBytes(path), false))
                    {
                        // the emblem on the coin's steel (the art's see-through parts show the metal)
                        var px = tex.GetPixels32();
                        int sv = Mathf.RoundToInt(Mathf.Lerp(28, 190, Steel));
                        var steel = new Color32((byte)sv, (byte)(sv + 3), (byte)(sv + 6), 255);
                        // alpha: many of the game's shaders read the main texture's alpha as the gloss / reflection mask (the
                        // coin's full alpha was the 1.0.6 mirror): the emblem a bit less shiny than the steel
                        byte gSteel = (byte)Mathf.RoundToInt(255 * Mathf.Lerp(.05f, .8f, Shine)), gArt = (byte)Mathf.RoundToInt(255 * Mathf.Lerp(.03f, .55f, Shine));
                        for (int i = 0; i < px.Length; i++)
                        {
                            var c = px[i];
                            int a = c.a;
                            px[i] = new Color32((byte)((c.r * a + steel.r * (255 - a)) / 255), (byte)((c.g * a + steel.g * (255 - a)) / 255), (byte)((c.b * a + steel.b * (255 - a)) / 255),
                                                (byte)((gArt * a + gSteel * (255 - a)) / 255));
                        }
                        tex.SetPixels32(px);
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.filterMode = FilterMode.Bilinear;
                        tex.anisoLevel = 4;
                        tex.Apply(true, true); // mipmapped, then no longer readable (no CPU copy kept)
                        look = new Look { Sheet = tex, Frames = frames, Columns = Math.Max(1, cols), Rows = Math.Max(1, tex.height / Math.Max(1, size)), Ms = Math.Max(10, ms) };
                        L.Info($"rank tags: rank {rank + 1} emblem ready ({Path.GetFileName(path)}, {frames} frames) in {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
                    }
                }
                else L.Info($"rank tags: no emblem sheet for rank {rank + 1} — plain steel");
            }
            catch (Exception e) { L.Error("rank tag emblem " + (rank + 1), e); }
            _looks[rank] = look;
            return look;
        }

        /// <summary>A coin, 1 unit across and 1 thick, faces along ±Z. Face UVs: the unit square (the emblem's cell); the rim:
        /// one corner of the cell (the steel around the art).</summary>
        internal static Mesh Coin()
        {
            if (_coin != null) return _coin;
            const int n = 64;
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? .5f : -.5f;
                var normal = side == 0 ? Vector3.forward : Vector3.back;
                int c = v.Count;
                v.Add(new Vector3(0, 0, z)); nr.Add(normal); uv.Add(new Vector2(.5f, .5f));
                for (int i = 0; i <= n; i++)
                {
                    float a = i * Mathf.PI * 2 / n, x = Mathf.Cos(a), y = Mathf.Sin(a);
                    v.Add(new Vector3(x * .5f, y * .5f, z)); nr.Add(normal);
                    // the back mirrored, so the emblem reads the right way round from behind too
                    uv.Add(new Vector2(.5f + (side == 0 ? x : -x) * .5f, .5f + y * .5f));
                }
                for (int i = 0; i < n; i++)
                {
                    if (side == 0) { tri.Add(c); tri.Add(c + 2 + i); tri.Add(c + 1 + i); }
                    else { tri.Add(c); tri.Add(c + 1 + i); tri.Add(c + 2 + i); }
                }
            }
            // the rim
            int r0 = v.Count;
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2 / n, x = Mathf.Cos(a), y = Mathf.Sin(a);
                var o = new Vector3(x, y, 0);
                v.Add(new Vector3(x * .5f, y * .5f, .5f)); nr.Add(o); uv.Add(new Vector2(.02f, .02f));
                v.Add(new Vector3(x * .5f, y * .5f, -.5f)); nr.Add(o); uv.Add(new Vector2(.02f, .02f));
            }
            for (int i = 0; i < n; i++)
            {
                int a = r0 + i * 2, b = a + 2;
                tri.Add(a); tri.Add(b); tri.Add(a + 1);
                tri.Add(b); tri.Add(b + 1); tri.Add(a + 1);
            }
            _coin = new Mesh { name = "LevelGateRankTagCoin" };
            _coin.SetVertices(v); _coin.SetNormals(nr); _coin.SetUVs(0, uv); _coin.SetTriangles(tri, 0);
            _coin.RecalculateTangents();
            _coin.RecalculateBounds();
            return _coin;
        }

        /// <summary>A flat normal map (works both for plain and DXT5nm-style unpacking) and a mid grey (spec / gloss maps).</summary>
        internal static Texture2D FlatNormal() => _flatNormal ?? (_flatNormal = Solid(new Color(.5f, .5f, 1f, .5f), false));
        internal static Texture2D Grey() => _grey ?? (_grey = Solid(new Color(.45f, .45f, .45f, .5f), true));

        private static Texture2D Solid(Color c, bool srgb)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false, !srgb);
            t.SetPixel(0, 0, c); t.Apply(false, true);
            return t;
        }
    }

    /// <summary>On a coin model the game made for a Rank Tag: hides the coin's own renderers, shows the emblem coin in their
    /// place and plays the emblem. Undo puts the plain coin back (the model is pooled and may come back as a real coin).</summary>
    internal sealed class RankTagSkin : MonoBehaviour
    {
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private GameObject _coin;
        private Material _mat;
        private RankTags.Look _look;
        private int _rank = -1, _shown = -1;
        private float _playFrom;
        private string _texProp = "_MainTex";

        public bool Set(int rank)
        {
            if (_rank == rank && _coin != null) { _playFrom = Time.unscaledTime + 1f; ShowFrame(_look != null ? _look.Frames - 1 : 0); return false; }
            Undo();
            // the coin's own size and facing, from its meshes (in this model's own space)
            var root = transform;
            var toLocal = root.worldToLocalMatrix;
            Bounds b = default; bool any = false;
            Material src = null;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var m = toLocal * r.transform.localToWorldMatrix;
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
                if (src == null && r.sharedMaterial != null) src = r.sharedMaterial;
                if (r.enabled) { r.enabled = false; _hidden.Add(r); }
            }
            if (!any || src == null) { L.Info("rank tags: the coin model had no mesh to size from — left as it is"); Restore(); return false; }
            var size = b.size;
            int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            float thick = Mathf.Max(.0005f, size[thin]);
            float across = Mathf.Max(size[(thin + 1) % 3], size[(thin + 2) % 3]);
            var axis = thin == 0 ? Vector3.right : thin == 1 ? Vector3.up : Vector3.forward;

            _coin = new GameObject("RankTagCoin");
            _coin.layer = gameObject.layer;
            var t = _coin.transform;
            t.SetParent(root, false);
            t.localPosition = b.center;
            t.localRotation = Quaternion.FromToRotation(Vector3.forward, axis);
            t.localScale = new Vector3(across, across, thick);
            _coin.AddComponent<MeshFilter>().sharedMesh = RankTags.Coin();
            var mr = _coin.AddComponent<MeshRenderer>();
            _mat = new Material(src) { name = "RankTag" + (rank + 1) };
            _look = RankTags.LookOf(rank);
            if (_look != null)
            {
                if (!_mat.HasProperty(_texProp)) _texProp = _mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                _mat.SetTexture(_texProp, _look.Sheet);
                _mat.SetTextureScale(_texProp, new Vector2(1f / _look.Columns, 1f / _look.Rows));
                if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", Color.white);
            }
            RankTags.Tune(_mat, _texProp);
            _tuned = RankTags.TuneVersion;
            mr.sharedMaterial = _mat;
            _rank = rank;
            _shown = -1;
            _playFrom = Time.unscaledTime + 1f; // stands on the finished emblem first (the icon is taken now)
            ShowFrame(_look != null ? _look.Frames - 1 : 0);
            L.Debug($"rank tags: rank {rank + 1} coin skinned ({across * 1000:0.0} x {thick * 1000:0.0} mm, shader {src.shader?.name}, texture {_texProp}, {_hidden.Count} renderer(s) hidden, layer {gameObject.layer})");
            return true;
        }

        public void Undo()
        {
            Restore();
            if (_coin != null) Destroy(_coin);
            if (_mat != null) Destroy(_mat);
            _coin = null; _mat = null; _look = null; _rank = -1;
        }

        private void Restore()
        {
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();
        }

        private void ShowFrame(int f)
        {
            if (_mat == null || _look == null || f == _shown) return;
            _shown = f;
            int col = f % _look.Columns, row = f / _look.Columns;
            _mat.SetTextureOffset(_texProp, new Vector2(col / (float)_look.Columns, 1f - (row + 1) / (float)_look.Rows));
        }

        private int _tuned;

        private void Update()
        {
            if (_mat != null && _tuned != RankTags.TuneVersion)
            {
                // an F12 dial moved: the new look, live
                _tuned = RankTags.TuneVersion;
                _look = RankTags.LookOf(_rank);
                if (_look != null) { _mat.SetTexture(_texProp, _look.Sheet); _mat.SetTextureScale(_texProp, new Vector2(1f / _look.Columns, 1f / _look.Rows)); }
                RankTags.Tune(_mat, _texProp);
                _shown = -1;
            }
            if (_look == null || _mat == null) return;
            if (ProgressionPlugin.Low || Time.unscaledTime < _playFrom) { ShowFrame(_look.Frames - 1); return; } // Performance Mode: stands still
            ShowFrame((int)((Time.unscaledTime - _playFrom) * 1000f / _look.Ms) % _look.Frames);
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
