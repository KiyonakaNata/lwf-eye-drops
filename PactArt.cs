// LWF Eye Drops — 契約の絵を描き替える土台（コイントス・偽社員証で共通）
//
// -- 本体の仕様（2026-11-10 に 0.31.1 の逆コンパイル・0.31.2 の実機で確認）-------------
// - 契約の絵は PactProperty.Sprite。PactBase.LoadSpriteAsync が読み、GetSprite() がそのまま返す。
//   使う側は3か所（PactCellComponents.SetPact・PactDisplayService.CreateSnapshot・TradePactSnapshotFactory）。
//   GetSprite は1行なのでパッチするとインライン化で素通りされうる → 読み込みの後に Sprite 自体を差し替える
// - 候補の使い魔は SponsorFamLotteryPact.GetCandidates()（private）。並びは説明文と同じ。
//   コイントス（SponsorFamLotteryPact）はスポンサーの使い魔、偽社員証（BuyFamsPact・子クラス）はスポンサー外の使い魔
// - 図鑑はランの契約が無い（スナップショットが spriteAddress から元の絵を読む）ので、元の絵のまま
// - 元の絵はアトラスに詰めて置かれている（Sprite.packed）。rect は元の大きさで、絵の中身（メッシュ）は rect の
//   一部だけ（コイントスは 443x324 の下寄りに 443x273）。配置の数値は「中身の左上原点・下向き」で測る
// - ⚠ 本体は例外が出るとゲームごと止める（Telemetry.ErrorDiagnosticLogHandler）。MOD の処理は必ず try の中で
//
// -- 描き方 ------------------------------------------------------------
// 4倍の RenderTexture に GL で描き、CPU で 4×4 平均して縮める（立ち絵・Spine のアトラスにミップマップが無く、
// そのまま縮めると荒れる）。描画先は乗算済みアルファで、縮めるときに戻す。
// 元の絵の一部（コイン・社員証）は、元の絵のメッシュを三角形ごとに半平面で切って描く（アトラスは詰めて
// 置かれているので、四角く切ると隣の絵が混ざりうる）。
// 絵は同梱しない（手元のゲームの絵をその場で組み立てる）。

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Cysharp.Threading.Tasks;
using Fams.EditIfFamAdd;
using HarmonyLib;
using Pact;
using Pact.Pacts.GetItemPacts;
using UnityEngine;

namespace LwfEyeDrops
{
    internal static class PactArt
    {
        internal const int SuperSample = 4;

        // 元の絵の中身（メッシュ）の範囲。元の絵の左下原点・px
        internal struct Content
        {
            public float Left, Top, Bottom;

            // 中身の左上原点・下向きの座標 → 描画先（左下原点・px × SuperSample）
            public Vector2 ToTarget(float x, float yDown)
            {
                return new Vector2((Left + x) * SuperSample, (Top - yDown) * SuperSample);
            }
        }

        // 半平面 a*x + b*y + c >= 0 の側を残す（元の絵の左下原点・px）
        internal struct HalfPlane
        {
            public float A, B, C;
            public HalfPlane(float a, float b, float c) { A = a; B = b; C = c; }
            public float Eval(Vector4 v) { return A * v.x + B * v.y + C; }
        }

        private static readonly HashSet<Sprite> Made = new HashSet<Sprite>();
        private static MethodInfo _getCandidates;
        private static PropertyInfo _spriteProperty;
        private static Material _material;
        private static ManualLogSource Log { get { return EyeDropsPlugin.Log; } }

        internal static void Patch(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(PactBase), "LoadSpriteAsync");
            if (target == null) throw new MissingMethodException("PactBase.LoadSpriteAsync");
            if (AccessTools.Field(typeof(PactBase), "_property") == null) throw new MissingFieldException("PactBase._property");
            _getCandidates = AccessTools.Method(typeof(SponsorFamLotteryPact), "GetCandidates");
            if (_getCandidates == null) throw new MissingMethodException("SponsorFamLotteryPact.GetCandidates");
            _spriteProperty = AccessTools.Property(typeof(PactProperty), "Sprite");
            if (_spriteProperty == null || _spriteProperty.GetSetMethod(true) == null) throw new MissingMemberException("PactProperty.Sprite");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(PactArt), "AfterLoad"));
        }

        private static void AfterLoad(PactBase __instance, ref UniTask __result)
        {
            try
            {
                if (__instance == null || !EyeDropsPlugin.Enabled.Value) return;
                Func<Sprite, IReadOnlyList<FamType>, Sprite> make;
                var type = __instance.GetType();
                if (type == typeof(SponsorFamLotteryPact)) { if (!EyeDropsPlugin.CoinToss.Value) return; make = CoinTossIcon.Make; }
                else if (type == typeof(BuyFamsPact)) { if (!EyeDropsPlugin.FakeEmployeeId.Value) return; make = FakeIdIcon.Make; }
                else return;
                var pact = __instance;
                __result = __result.ContinueWith(() => Apply(pact, make));
            }
            catch (Exception e)
            {
                Log.LogWarning("[pact] skipped: " + e);
            }
        }

        private static void Apply(PactBase pact, Func<Sprite, IReadOnlyList<FamType>, Sprite> make)
        {
            try
            {
                var property = Traverse.Create(pact).Field("_property").GetValue<PactProperty>();
                if (property == null) return;
                var original = property.Sprite;
                if (original == null || Made.Contains(original)) return;
                var fams = _getCandidates.Invoke(pact, null) as IReadOnlyList<FamType>;
                if (fams == null) return;
                var sprite = make(original, fams);
                if (sprite == null) return;
                Made.Add(sprite);
                _spriteProperty.SetValue(property, sprite, null);
            }
            catch (Exception e)
            {
                Log.LogWarning("[pact] skipped " + pact.GetType().Name + ": " + e);
            }
        }

        internal static Material SpriteMaterial
        {
            get
            {
                if (_material == null)
                {
                    // 出力は乗算済みアルファ（Blend One OneMinusSrcAlpha）。縮めるときに戻す
                    var shader = Shader.Find("Sprites/Default");
                    if (shader == null) throw new InvalidOperationException("Sprites/Default shader not found");
                    _material = new Material(shader);
                    _material.hideFlags = HideFlags.HideAndDontSave;
                }
                return _material;
            }
        }

        internal static Content Measure(Sprite original)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var v in original.vertices)
            {
                float x = v.x * original.pixelsPerUnit + original.pivot.x;
                float y = v.y * original.pixelsPerUnit + original.pivot.y;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            if (minY > maxY) throw new InvalidOperationException("original sprite has no vertices");
            return new Content { Left = minX, Top = maxY, Bottom = minY };
        }

        // 元の絵と同じ大きさの絵を作る。draw は GL の描画先（左下原点・px × SuperSample）が用意された状態で呼ばれる
        internal static Sprite Compose(Sprite original, string name, Action<Content> draw)
        {
            int w = Mathf.RoundToInt(original.rect.width), h = Mathf.RoundToInt(original.rect.height);
            int bw = w * SuperSample, bh = h * SuperSample;
            var content = Measure(original);

            var rt = RenderTexture.GetTemporary(bw, bh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            var big = new Texture2D(bw, bh, TextureFormat.RGBA32, false);
            Color32[] pixels;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.clear);
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0, bw, 0, bh);
                    draw(content);
                }
                finally { GL.PopMatrix(); }
                big.ReadPixels(new Rect(0, 0, bw, bh), 0, 0, false);
                pixels = big.GetPixels32();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(big);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = name + " (Eye Drops)";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(Downsample(pixels, bw, w, h));
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

            var pivot = new Vector2(original.pivot.x / original.rect.width, original.pivot.y / original.rect.height);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, original.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        // 元の絵のメッシュのうち、すべての半平面の内側に入る部分だけ描く
        internal static void DrawOriginal(Sprite original, params HalfPlane[] planes)
        {
            DrawOriginalFaded(original, float.NegativeInfinity, float.NegativeInfinity, planes);
        }

        // 上と同じ。ただし y が fadeTop から fadeBottom へ下がるにつれて透明にしていく（元の絵の左下原点・px）。
        // 切り口が絵の薄い所（コインの残像など）を横切るとき、四角い縁が見えないようにする
        internal static void DrawOriginalFaded(Sprite original, float fadeTop, float fadeBottom, params HalfPlane[] planes)
        {
            bool fade = fadeTop > fadeBottom;
            var verts = original.vertices;
            var uvs = original.uv;
            var tris = original.triangles;
            float ppu = original.pixelsPerUnit, k = SuperSample;

            var mat = SpriteMaterial;
            mat.mainTexture = original.texture;
            mat.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            GL.Color(Color.white);
            var poly = new List<Vector4>(8);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                poly.Clear();
                for (int j = 0; j < 3; j++)
                {
                    int i = tris[t + j];
                    poly.Add(new Vector4(verts[i].x * ppu + original.pivot.x, verts[i].y * ppu + original.pivot.y, uvs[i].x, uvs[i].y));
                }
                foreach (var p in planes) poly = Clip(poly, p);
                if (!fade)
                {
                    EmitFan(poly, k, float.NegativeInfinity, float.NegativeInfinity);
                    continue;
                }
                // 薄くしていく帯の上と下で分ける（分ければ、各三角形の中でアルファが y に比例して落ちる）
                EmitFan(Clip(poly, new HalfPlane(0, 1, -fadeTop)), k, fadeTop, fadeBottom);
                EmitFan(Clip(poly, new HalfPlane(0, -1, fadeTop)), k, fadeTop, fadeBottom);
            }
            GL.End();
        }

        private static void EmitFan(List<Vector4> poly, float k, float fadeTop, float fadeBottom)
        {
            for (int j = 1; j + 1 < poly.Count; j++)
            {
                Emit(poly[0], k, fadeTop, fadeBottom); Emit(poly[j], k, fadeTop, fadeBottom); Emit(poly[j + 1], k, fadeTop, fadeBottom);
            }
        }

        private static void Emit(Vector4 v, float k, float fadeTop, float fadeBottom)
        {
            float a = fadeTop > fadeBottom ? Mathf.Clamp01((v.y - fadeBottom) / (fadeTop - fadeBottom)) : 1f;
            GL.Color(new Color(1, 1, 1, a));
            GL.TexCoord2(v.z, v.w);
            GL.Vertex3(v.x * k, v.y * k, 0);
        }

        // 多角形を半平面で切る（Sutherland–Hodgman）。v = (x, y, u, v)
        private static List<Vector4> Clip(List<Vector4> poly, HalfPlane plane)
        {
            var result = new List<Vector4>(poly.Count + 2);
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                float da = plane.Eval(a), db = plane.Eval(b);
                if (da >= 0) result.Add(a);
                if ((da >= 0) != (db >= 0)) result.Add(Vector4.Lerp(a, b, da / (da - db)));
            }
            return result;
        }

        // SuperSample 四方の平均で縮める。描画先は乗算済みアルファなので、平均してから戻す
        internal static Color32[] Downsample(Color32[] src, int srcWidth, int w, int h)
        {
            var dst = new Color32[w * h];
            int n = SuperSample * SuperSample;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < SuperSample; sy++)
                    {
                        int row = (y * SuperSample + sy) * srcWidth + x * SuperSample;
                        for (int sx = 0; sx < SuperSample; sx++)
                        {
                            var c = src[row + sx];
                            r += c.r; g += c.g; b += c.b; a += c.a;
                        }
                    }
                    if (a == 0) continue;
                    dst[y * w + x] = new Color32(
                        (byte)Math.Min(255, r * 255 / a), (byte)Math.Min(255, g * 255 / a),
                        (byte)Math.Min(255, b * 255 / a), (byte)(a / n));
                }
            }
            return dst;
        }

#if DEV
        // 開発用: 元の絵を読んで、組の一覧ぶん作って PNG に書き出す（BepInEx/cache/<folder>/）
        // ⚠ 本体は例外が出るとゲームごと止める。try の外で投げうる処理を置かない
        internal static System.Collections.IEnumerator Dump(string address, string folder, List<List<FamType>> sets,
            Func<Sprite, IReadOnlyList<FamType>, Sprite> make, float delay)
        {
            yield return new WaitForSeconds(delay);
            UniTask<Sprite> load;
            try { load = AddressableSystems.AddressablesLoader.LoadAssetAsync<Sprite>(address); }
            catch (Exception e) { Log.LogWarning("[dump] " + folder + ": " + e); yield break; }
            while (load.Status == UniTaskStatus.Pending) yield return null;
            try
            {
                var original = load.GetAwaiter().GetResult();
                var dir = System.IO.Path.Combine(BepInEx.Paths.CachePath, folder);
                System.IO.Directory.CreateDirectory(dir);
                foreach (var set in sets)
                {
                    var name = string.Join("+", set.ConvertAll(f => f.ToString()).ToArray());
                    try
                    {
                        var s = make(original, set);
                        if (s == null) { Log.LogWarning("[dump] " + name + ": not made"); continue; }
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), ToPng(s.texture));
                    }
                    catch (Exception e) { Log.LogWarning("[dump] " + name + ": " + e); }
                }
                Log.LogInfo("[dump] " + folder + " done: " + dir);
            }
            catch (Exception e) { Log.LogWarning("[dump] " + folder + ": " + e); }
        }

        // 開発用: w×h の絵を（SuperSample 倍で）描いて PNG にする
        internal static byte[] RenderToPng(int w, int h, Action draw)
        {
            int bw = w * SuperSample, bh = h * SuperSample;
            var rt = RenderTexture.GetTemporary(bw, bh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            var big = new Texture2D(bw, bh, TextureFormat.RGBA32, false);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.clear);
                GL.PushMatrix();
                try { GL.LoadPixelMatrix(0, bw, 0, bh); draw(); }
                finally { GL.PopMatrix(); }
                big.ReadPixels(new Rect(0, 0, bw, bh), 0, 0, false);
                tex.SetPixels32(Downsample(big.GetPixels32(), bw, w, h));
                return tex.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(big);
                UnityEngine.Object.Destroy(tex);
            }
        }

        private static byte[] ToPng(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0, false);
                return copy.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(copy);
            }
        }
#endif
    }
}
