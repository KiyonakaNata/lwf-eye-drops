// LWF Eye Drops — コイントスの絵
//
// 契約「コイントス」（SponsorFamLotteryPact）の絵は、サラマンダーとゴブリンとコインの固定の1枚
// （PactProperty.csv の spriteAddress = SponsorFamLotteryPact）。実際の候補はラン中のスポンサー
// （ベルフェゴールとゲヘナを除く）の使い魔で、絵と食い違う。絵の2人を候補の2人に描き替える。
// 差し替えの仕組み（読み込み後に PactProperty.Sprite を替える）は PactArt.cs。
//
// -- 本体の仕様（2026-11-10 に 0.31.1 の逆コンパイル・0.31.2 の実機で確認）-------------
// - スポンサーは3体＋ゲヘナで、この契約はベルフェゴールのものなので候補はふつう2体（ベルゼブブは未実装）
// - 元の絵の2人は、会話シーンの立ち絵（Resources/ingameadventure/texture/character/pic_fam_*。Utage の
//   DicingTextures）の「missing」の表情を切り出したもの。サラマンダーは 0.195 倍、ゴブリンは 0.18 倍
//   （元の絵と立ち絵を突き合わせて求めた）
// - 元の絵は 443x324 で、中身は下寄りの 443x273（上 51px は透明）
//
// -- 何をするか --------------------------------------------------------
// 1. 候補の2人の立ち絵を、元の絵と同じ大きさ・位置で描く（左＝1人目・右＝2人目）
//    - 大きさ: サラマンダーとゴブリンは元の倍率。ほかは目の間隔が元の2人と同じになる倍率（レイヴンは顔が
//      斜めで目の間隔が使えないので目測）
//    - 位置: 頭の中心と目の高さを、元の絵のその枠の人物にそろえる（目の中点で合わせるとレイヴンとゴブリンが
//      横にずれて見えた）
//    - 表情: missing。無いキャラは近いもの（スライム guruguru・アルラウネ charmed・レイヴン uee）
//    - 人物は中身の下端で切る（元の絵も胸の下で切っている）
// 2. コインは元の絵から切り抜いて上に重ねる
// 候補が2人でないときは元の絵のまま。

using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Fams.EditIfFamAdd;
using HarmonyLib;
using UnityEngine;

namespace LwfEyeDrops
{
    internal static class CoinTossIcon
    {
        private const string ResourceDir = "ingameadventure/texture/character/";

        // コインの範囲（中身の左上原点・下向き・px）。コインの下の残像は y 93〜130 まで薄く伸び、x 205〜234 では
        // y≈115 からサラマンダーの角とつながる → 下端は角の手前の 115。四角く切ると残像の途中（アルファ 30〜46）で
        // 切れて、明るい地で四角い縁が見えたので、92〜115 で徐々に透明にする
        private const float CoinLeft = 185f, CoinRight = 272f, CoinBottom = 115f, CoinFadeTop = 92f;

        private sealed class Figure
        {
            public string Asset;      // 立ち絵（DicingTextures）の名前
            public string Expression; // 表情（パターン名の末尾）
            public float EyeX, EyeY;  // 両目の中点（立ち絵の左上原点・px）
            public float HeadDx;      // 頭の中心 − 目の中点（横・立ち絵 px）
            public float Scale;
        }

        private sealed class Slot
        {
            public float HeadX, EyeY; // 頭の中心の横位置・目の高さ（中身の左上原点・下向き・px）
        }

        // 目の間隔を元の2人（サラマンダー 280px×0.195・ゴブリン 300px×0.18 の平均 54.3px）にそろえる倍率
        private const float EyeSpanTarget = 54.3f;

        private static readonly Dictionary<FamType, Figure> Figures = new Dictionary<FamType, Figure>
        {
            { FamType.Crafter,     new Figure { Asset = "pic_fam_salamander", Expression = "missing",  EyeX = 1666, EyeY = 1860, HeadDx = 8,   Scale = 0.195f } },
            { FamType.Worker,      new Figure { Asset = "pic_fam_Goblin",     Expression = "missing",  EyeX = 1518, EyeY = 2135, HeadDx = 38,  Scale = 0.180f } },
            { FamType.Smelter,     new Figure { Asset = "pic_fam_Slime",      Expression = "guruguru", EyeX = 1715, EyeY = 2040, HeadDx = 8,   Scale = EyeSpanTarget / 290f } },
            { FamType.Farmer,      new Figure { Asset = "pic_fam_Alraune",    Expression = "charmed",  EyeX = 1699, EyeY = 2060, HeadDx = 17,  Scale = EyeSpanTarget / 290f } },
            { FamType.Transporter, new Figure { Asset = "pic_fam_Raven",      Expression = "uee",      EyeX = 1571, EyeY = 2060, HeadDx = -55, Scale = 0.19f } },
        };

        // 元の絵の左の枠（サラマンダー: 0.195 倍・立ち絵の原点が (-224,-136)）と右の枠（ゴブリン: 0.18 倍・(51,-155)）
        private static readonly Slot[] Slots =
        {
            new Slot { HeadX = (1666 + 8) * 0.195f - 224, EyeY = 1860 * 0.195f - 136 },
            new Slot { HeadX = (1518 + 38) * 0.180f + 51, EyeY = 2135 * 0.180f - 155 },
        };

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Utage.DicingTextures> Dicing = new Dictionary<string, Utage.DicingTextures>();
        private static ManualLogSource Log { get { return EyeDropsPlugin.Log; } }

        // PactArt から呼ばれる。作れないときは null（元の絵のまま）
        internal static Sprite Make(Sprite original, IReadOnlyList<FamType> fams)
        {
            if (fams.Count != 2 || !Figures.ContainsKey(fams[0]) || !Figures.ContainsKey(fams[1])) return null;
            var key = fams[0] + "+" + fams[1];
            Sprite sprite;
            if (Cache.TryGetValue(key, out sprite) && sprite != null) return sprite;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var left = Figures[fams[0]];
            var right = Figures[fams[1]];
            LoadDicing(left.Asset);
            LoadDicing(right.Asset);
            sprite = PactArt.Compose(original, "SponsorFamLotteryPact " + key, c =>
            {
                DrawFigure(left, Slots[0], c);
                DrawFigure(right, Slots[1], c);
                // コイン
                PactArt.DrawOriginalFaded(original, c.Top - CoinFadeTop, c.Top - CoinBottom,
                    new PactArt.HalfPlane(1, 0, -(c.Left + CoinLeft)),
                    new PactArt.HalfPlane(-1, 0, c.Left + CoinRight),
                    new PactArt.HalfPlane(0, 1, -(c.Top - CoinBottom)));
            });
            Cache[key] = sprite;
            Log.LogInfo("[coin] built " + key + " in " + watch.ElapsedMilliseconds + " ms");
            return sprite;
        }

        // 立ち絵1人を描く。座標は描画先の左下原点（px × SuperSample）
        private static void DrawFigure(Figure f, Slot slot, PactArt.Content c)
        {
            var dicing = LoadDicing(f.Asset);
            Utage.DicingTextureData data = null;
            foreach (var d in dicing.TextureDataList)
            {
                if (d.Name.EndsWith("_" + f.Expression, StringComparison.OrdinalIgnoreCase)) { data = d; break; }
            }
            if (data == null) throw new InvalidOperationException(f.Asset + ": expression not found: " + f.Expression);
            Texture2D atlas = null;
            foreach (var t in dicing.AtlasTextures)
            {
                if (t != null && t.name == data.AtlasName) { atlas = t; break; }
            }
            if (atlas == null) throw new InvalidOperationException(f.Asset + ": atlas not found: " + data.AtlasName);

            // セルの並べ方（Utage の ForeachVertexList は呼ぶとゲームが固まったので自前で並べる。
            // 立ち絵の書き出しで、組み上がりが正しいことを確かめた並べ方）:
            // - アトラスは cellSize 四方の枠の格子。枠の中身は padding を除いた (cellSize - 2*padding) 四方
            // - cellIndexList[k] は、絵の k 番目のセル（左下から右へ、行ごとに上へ）に使うアトラスの枠の番号。
            //   枠の番号はアトラスの左下から右へ、行ごとに上へ。transparentIndex の枠は透明なので飛ばす
            var cells = Traverse.Create(data).Field("cellIndexList").GetValue<List<int>>();
            int transparent = Traverse.Create(data).Field("transparentIndex").GetValue<int>();
            if (cells == null) throw new InvalidOperationException(f.Asset + ": cellIndexList not found");
            int cell = dicing.CellSize, pad = dicing.Padding, inner = cell - pad * 2;
            int perRow = atlas.width / cell;
            int columns = (data.Width + inner - 1) / inner;
            if (inner <= 0 || perRow <= 0 || columns <= 0) throw new InvalidOperationException(f.Asset + ": bad cell layout");

            float k = PactArt.SuperSample, s = f.Scale * k;
            // 立ち絵の左上の位置（中身の左上原点・下向き）→ 描画先
            var topLeft = c.ToTarget(slot.HeadX - (f.EyeX + f.HeadDx) * f.Scale, slot.EyeY - f.EyeY * f.Scale);
            float left = topLeft.x, bottom = topLeft.y - data.Height * s;
            float clipBottom = c.Bottom * k;
            float aw = atlas.width, ah = atlas.height;

            var mat = PactArt.SpriteMaterial;
            mat.mainTexture = atlas;
            mat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(Color.white);
            for (int i = 0; i < cells.Count; i++)
            {
                int a = cells[i];
                if (a == transparent) continue;
                float x0 = left + (i % columns) * inner * s, y0 = bottom + (i / columns) * inner * s;
                float x1 = x0 + inner * s, y1 = y0 + inner * s;
                float u0 = ((a % perRow) * cell + pad) / aw, v0 = ((a / perRow) * cell + pad) / ah;
                float u1 = u0 + inner / aw, v1 = v0 + inner / ah;
                // 中身の下端より下は描かない
                if (y1 <= clipBottom) continue;
                if (y0 < clipBottom)
                {
                    v0 += (v1 - v0) * (clipBottom - y0) / (y1 - y0);
                    y0 = clipBottom;
                }
                GL.TexCoord2(u0, v0); GL.Vertex3(x0, y0, 0);
                GL.TexCoord2(u0, v1); GL.Vertex3(x0, y1, 0);
                GL.TexCoord2(u1, v1); GL.Vertex3(x1, y1, 0);
                GL.TexCoord2(u1, v0); GL.Vertex3(x1, y0, 0);
            }
            GL.End();
        }

        private static Utage.DicingTextures LoadDicing(string asset)
        {
            Utage.DicingTextures d;
            if (Dicing.TryGetValue(asset, out d) && d != null) return d;
            d = Resources.Load<Utage.DicingTextures>(ResourceDir + asset);
            if (d == null) throw new InvalidOperationException("standing art not found: " + ResourceDir + asset);
            Dicing[asset] = d;
            return d;
        }

#if DEV
        // 開発用: 起動後に全20組（左右の順も別）を BepInEx/cache/coin_toss/ に書き出す
        internal static System.Collections.IEnumerator DumpAll()
        {
            var fams = new List<FamType>(Figures.Keys);
            var sets = new List<List<FamType>>();
            foreach (var a in fams) foreach (var b in fams) if (a != b) sets.Add(new List<FamType> { a, b });
            return PactArt.Dump("SponsorFamLotteryPact", "coin_toss", sets, Make, 5f);
        }
#endif
    }
}
