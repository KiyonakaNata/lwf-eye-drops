// LWF Eye Drops — 偽社員証の絵
//
// 契約「偽社員証」（BuyFamsPact）の絵は、ゴブリン（マップ上のちびキャラ）と社員証の固定の1枚
// （spriteAddress = BuyFams）。実際の候補は、今回のスポンサーが提供しない使い魔3体（そこから2体が当たる）。
// ゴブリンを候補の3体に描き替える。差し替えの仕組みは PactArt.cs、ちびキャラの描き方は SpineFigure.cs。
//
// -- 本体の仕様（2026-11-10 に 0.31.1 の逆コンパイル・0.31.2 の実機で確認）-------------
// - 候補は GetNonSponsorFams: 実装済みの使い魔のうち、スポンサー（ゲヘナ含む）が提供しないもの。
//   スポンサーはベルフェゴール＋2体＋ゲヘナなので、ゴブリン・サラマンダー・スライム・アルラウネ・レイヴンの5種から
//   2体を除いた3体になる
// - 元の絵のゴブリンは、マップ上の使い魔の正面の待機（f/act_idle の 0 秒）を、中身の左上原点で (2,2)〜(204,277)
//   （高さ 275px）に置いたもの
// - 社員証の左の縁は x = 244 − 0.25·y（中身の左上原点・下向き）。外側に 2〜3px の暗い縁取りがあり、その外は
//   ゴブリンまで 2px 以上透明
//
// -- 何をするか（ユーザーとラフで決めた・2026-11-10）--------------------
// - 3体を一塊の三角に置く: 奥の左に1体目、奥の右に3体目、手前の真ん中に2体目。描く順は奥→手前。
//   奥の2体の顔が手前の頭の上に出る高さにする（アルラウネが手前でもスライムが隠れない）
// - 大きさはそろえる（ユーザー指定）: 元のゴブリンの縮尺 × 0.55 × キャラごとの係数。手前と奥は同じ大きさ
//   （手前を大きくすると手前だけ目立つ）。係数は「頭の幅でそろえる値」と「全体の高さでそろえる値」の中間
//   （頭だけでそろえると、ハンマー・羽のサラマンダーと籠のレイヴンが道具ごと大きくなりすぎた）
// - 社員証は一番手前（ユーザー指定）: 元の絵から縁の線の 3px 外より右を切り抜き、最後に重ねる
// - 塊は 1.1 倍にして、左端を枠の左（x=6）、下端を中身の下端にそろえる。元の絵の中身（390x306）は 443x324 の
//   枠の右下寄り（x 53〜・y 18〜）にあり、ゲームは枠ごと表示する。中身の中に収めると左が 55px ほど空いて見えた
//   （ユーザー指摘）。元の絵はゴブリンが大きいので目立たない
// - だめだった並べ方: 奥の2体を上端・左端・社員証の縁に寄せて広げる（手前が目立ちすぎ、一塊に見えない）
// 候補が3体でないときは元の絵のまま。

using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Fams.EditIfFamAdd;
using UnityEngine;

namespace LwfEyeDrops
{
    internal static class FakeIdIcon
    {
        private const string Skin = "f";
        private const string Animation = "f/act_idle";
        private const float VanillaHeight = 275f;  // 元の絵のゴブリンの高さ（px）

        // 社員証の左の縁の 3px 外: x = CardX0 + CardSlope·y（中身の左上原点・下向き）
        private const float CardX0 = 241f, CardSlope = -0.25f;

        private struct Place
        {
            public float CenterX, Bottom, Size;  // 中身の左上原点・下向き・px。Size は元のゴブリンの縮尺に掛ける
            public Place(float cx, float bottom, float size) { CenterX = cx; Bottom = bottom; Size = size; }
        }

        // 候補の 1・2・3 体目の置き場所（奥の左・手前・奥の右）と、描く順（奥から）。塊の形だけを決める（位置は下で寄せる）
        private static readonly Place[] Places = { new Place(62, 196, 0.55f), new Place(114, 296, 0.55f), new Place(168, 188, 0.55f) };
        private const float ClusterScale = 1.1f;   // 塊ごとの拡大
        private const float FrameLeft = 6f;        // 塊の左端（枠の左からの px）
        private const float ContentBottom = 304f;  // 塊の下端（中身の左上原点・下向き）

        // 見た目の大きさをそろえる係数（同じ縮尺の書き出しで、頭の幅・全体の高さを目で測って決めた）
        private static readonly Dictionary<FamType, float> SizeFactor = new Dictionary<FamType, float>
        {
            { FamType.Worker, 1.0f }, { FamType.Crafter, 0.97f }, { FamType.Smelter, 0.98f },
            { FamType.Farmer, 0.91f }, { FamType.Transporter, 1.2f },
        };
        private static readonly int[] DrawOrder = { 0, 2, 1 };

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static ManualLogSource Log { get { return EyeDropsPlugin.Log; } }

        // PactArt から呼ばれる。作れないときは null（元の絵のまま）
        internal static Sprite Make(Sprite original, IReadOnlyList<FamType> fams)
        {
            if (fams.Count != 3) return null;
            foreach (var f in fams) if (!SizeFactor.ContainsKey(f)) return null;
            var key = fams[0] + "+" + fams[1] + "+" + fams[2];
            Sprite sprite;
            if (Cache.TryGetValue(key, out sprite) && sprite != null) return sprite;
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // 縮尺の基準は元の絵のゴブリン（候補に居なくても読む）
            var reference = SpineFigure.GetPose(FamType.Worker, Skin, Animation, 0);
            float unit = VanillaHeight / reference.Height;
            var poses = new SpineFigure.Pose[3];
            for (int i = 0; i < 3; i++) poses[i] = SpineFigure.GetPose(fams[i], Skin, Animation, 0);

            // 塊の形（中身の左上原点・下向き）: 1.1 倍して、各体の左端と下端を求める
            var scales = new float[3];
            var lefts = new float[3];
            var bottoms = new float[3];
            float minLeft = float.MaxValue, maxBottom = float.MinValue;
            for (int i = 0; i < 3; i++)
            {
                scales[i] = unit * Places[i].Size * SizeFactor[fams[i]] * ClusterScale;
                lefts[i] = Places[i].CenterX * ClusterScale - poses[i].Width * scales[i] / 2;
                bottoms[i] = Places[i].Bottom * ClusterScale;
                minLeft = Math.Min(minLeft, lefts[i]);
                maxBottom = Math.Max(maxBottom, bottoms[i]);
            }

            sprite = PactArt.Compose(original, "BuyFams " + key, c =>
            {
                float k = PactArt.SuperSample;
                // 塊の左端を枠の x=FrameLeft、下端を中身の ContentBottom へ
                float dx = (FrameLeft - c.Left) - minLeft, dy = ContentBottom - maxBottom;
                foreach (int i in DrawOrder)
                {
                    var at = c.ToTarget(lefts[i] + dx, bottoms[i] + dy);
                    SpineFigure.Draw(poses[i], scales[i] * k, at.x, at.y);
                }
                // 社員証: (x − Left) ≥ CardX0 + CardSlope·(Top − y)  →  x + CardSlope·y − Left − CardX0 − CardSlope·Top ≥ 0
                PactArt.DrawOriginal(original, new PactArt.HalfPlane(1, CardSlope, -c.Left - CardX0 - CardSlope * c.Top));
            });
            Cache[key] = sprite;
            Log.LogInfo("[fakeid] built " + key + " in " + watch.ElapsedMilliseconds + " ms");
            return sprite;
        }

#if DEV
        // 開発用: 起動後に全10組を BepInEx/cache/fake_id/ に書き出す（並びは本体の候補の並び＝FamType の順）
        internal static System.Collections.IEnumerator DumpAll()
        {
            var fams = new List<FamType> { FamType.Worker, FamType.Transporter, FamType.Farmer, FamType.Smelter, FamType.Crafter };
            var sets = new List<List<FamType>>();
            for (int a = 0; a < fams.Count; a++)
                for (int b = a + 1; b < fams.Count; b++)
                    for (int d = b + 1; d < fams.Count; d++)
                        sets.Add(new List<FamType> { fams[a], fams[b], fams[d] });
            return PactArt.Dump("BuyFams", "fake_id", sets, Make, 7f);
        }

        // 開発用: 5種を1体ずつ、元のゴブリンの縮尺の2倍で書き出す（fake_id/fig_*.png）。配置を手元で詰めるため
        internal static System.Collections.IEnumerator DumpFigures()
        {
            yield return new WaitForSeconds(9f);
            try
            {
                var dir = System.IO.Path.Combine(BepInEx.Paths.CachePath, "fake_id");
                System.IO.Directory.CreateDirectory(dir);
                var reference = SpineFigure.GetPose(FamType.Worker, Skin, Animation, 0);
                float unit = VanillaHeight / reference.Height;
                Log.LogInfo("[fakeid] unit=" + unit + " px per Spine unit (goblin height " + reference.Height + ")");
                foreach (var fam in SizeFactor.Keys)
                {
                    try
                    {
                        var p = SpineFigure.GetPose(fam, Skin, Animation, 0);
                        float scale = unit * 2;
                        int w = Mathf.CeilToInt(p.Width * scale), h = Mathf.CeilToInt(p.Height * scale);
                        var png = PactArt.RenderToPng(w, h, () => SpineFigure.Draw(p, scale * PactArt.SuperSample, 0, 0));
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "fig_" + fam + ".png"), png);
                        Log.LogInfo("[fakeid] fig " + fam + " " + w + "x" + h + " bounds=(" + p.MinX + "," + p.MinY + ")-(" + p.MaxX + "," + p.MaxY + ")");
                    }
                    catch (Exception e) { Log.LogWarning("[fakeid] fig " + fam + ": " + e); }
                }
            }
            catch (Exception e) { Log.LogWarning("[fakeid] figs: " + e); }
        }
#endif
    }
}
