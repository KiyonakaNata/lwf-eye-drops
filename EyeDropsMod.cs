// LWF Eye Drops
//
// モモコ（事務所・マップ上）のまぶたが下がるとき（瞬き・半目）、まつげの上に出る縦の筋と四角い面を消す BepInEx Mod。
// 1.1.0 から、契約の絵（コイントス・偽社員証）に当たる候補の使い魔を描く機能も持つ。そちらは PactArt.cs から読む
// （このファイルは起動処理と、まぶたの処理）。
//
// -- 本体の仕様（2026-11-09 に 0.31.2 で確認）---------------------------
// 瞬きは Spine のアニメ（f/act_idle など）が face_eyeblink ボーンを縦に縮めて行う。
// まつげは1枚の重み付きメッシュ（f_l_face_eyelash / f_r_face_eyelash）で、絵には「まつげの黒い線」と「まぶた（肌色・髪の影のピンク）」が
// 一緒に描かれている。まぶたの上の列は顔に付いて動かず、まつげの線は下がる。間の細い帯（テクスチャ上で数テクセル）が
// 数十倍に引き伸ばされて、下にある白目・黒目を隠す。この引き伸ばしが縦の筋と四角い面になる。
//
// まぶたの絵の下には、頭の絵（肌と髪の影）がそのまま描かれている（まぶたの絵は頭の絵と同じ色・同じ形）。
//
// 本体の spine-unity は改造版で、描画は SpineActor が別スレッドで回す。描画用の SkeletonData は SpineActorAsset が
// SkeletonDataAsset.ReadSkeletonData で読み直す（何度も）。読んだ直後にマテリアルの一覧と作業領域の大きさを決めるので、
// ReadSkeletonData の戻り値に当てる。領域の UV は読み込み時に上下が反転済み（Unity の向き・下が 0）。
//
// -- 何をするか（抜本案）-------------------------------------------------
// まぶたを「引き伸ばす絵」でなく「頭の絵がそのまま見えている所」として扱う。
//   1. まつげのメッシュ: 伸びる辺を割って、まつげの線から下はまつげと一緒に動かし、開く隙間には何も描かない（頭の絵が見える）
//   2. まつげのテクスチャ: 自前の写しを作り、肌色・ピンクの画素を透明にして黒い線だけ残す
//   3. 目の部品（白目・黒目・ハイライト）: まつげの線より上を Spine の切り抜き（ClippingAttachment）で隠す。
//      切り抜きは白目のスロットに置き、白目の絵は次のスロット（閉じ目「＝＝」の枠）へ移す。白目を出し入れするアニメには
//      移した先のスロットの出し入れを足す
// 目を開けているときの見た目は変わらない。
//
// 途中までの手法（隙間に絵を貼る・半目用の複製に切り替える）は hold/ に退避してある。経緯は README。
//
// -- 書き方の制約 -------------------------------------------------------
// C# 5 コンパイラ（csc.exe / .NET Framework 4.0）でビルドするため、
// 文字列補間・?. 演算子・式形式メンバ・out var・nameof・タプルは使えない。

using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Spine;
using Spine.Unity;

namespace LwfEyeDrops
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class EyeDropsPlugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "kiyonakanata.lwfeyedrops";
        internal const string PluginName = "LWF Eye Drops";
        internal const string PluginVersion = "1.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Momoko;
        internal static ConfigEntry<bool> MiningGoblin;
        internal static ConfigEntry<bool> BlacksmithSalamander;
        internal static ConfigEntry<bool> CoinToss;
        internal static ConfigEntry<bool> FakeEmployeeId;
        private Harmony _harmony;

        // 本体は起動中に例外が出るとゲームごと止める。MOD の失敗は MOD だけで止める
        private void Awake()
        {
            Log = Logger;
            try
            {
                // 項目名はゲームの英語表記（CharacterTable）。変更が効くのは次にキャラのデータが読まれたとき（＝ゲームの再起動後）
                Enabled = Config.Bind("1. General", "Enabled", true, "");
                Momoko = Config.Bind("2. Characters", "Momoko", true, "");
                MiningGoblin = Config.Bind("2. Characters", "Mining Goblin", true, "");
                BlacksmithSalamander = Config.Bind("2. Characters", "Blacksmith Salamander", true, "");
                // 項目名はゲームの英語表記（PactTable）。変更が効くのは次に契約の絵が読まれたとき
                CoinToss = Config.Bind("3. Pacts", "Coin Toss", true, "");
                FakeEmployeeId = Config.Bind("3. Pacts", "Fake Employee ID", true, "");

                var post = new HarmonyMethod(typeof(EyeDropsPlugin), "AfterRead");
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
                int n = 0;
                // ReadSkeletonData は internal（公開されていない）
                foreach (var mi in typeof(SkeletonDataAsset).GetMethods(flags))
                {
                    if (mi.Name != "ReadSkeletonData" || mi.ReturnType != typeof(SkeletonData)) continue;
                    if (_harmony == null) _harmony = new Harmony(PluginGuid);
                    _harmony.Patch(mi, postfix: post);
                    n++;
                }
                if (n == 0) throw new MissingMethodException("SkeletonDataAsset.ReadSkeletonData");
                Log.LogInfo("[boot] " + PluginName + " " + PluginVersion + " (game " + UnityEngine.Application.version + ") patches=" + n);
            }
            catch (Exception e)
            {
                Log.LogWarning("[boot] " + PluginName + " " + PluginVersion
                    + " disabled (the game version may have changed): " + e);
                enabled = false;
            }

            // 契約の絵（コイントス・偽社員証）はまぶたと別の仕組み。片方が本体の変更で外れても、もう片方は動かす
            if (Enabled == null) return;
            try
            {
                if (_harmony == null) _harmony = new Harmony(PluginGuid);
                PactArt.Patch(_harmony);
                Log.LogInfo("[boot] pact art patched");
#if DEV
                StartCoroutine(CoinTossIcon.DumpAll());
                StartCoroutine(FakeIdIcon.DumpAll());
                StartCoroutine(FakeIdIcon.DumpFigures());
#endif
            }
            catch (Exception e)
            {
                Log.LogWarning("[boot] pact art disabled (the game version may have changed): " + e);
            }
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        // 本体は同じデータを何度も読み直す。ログは最初の1回だけ出す（2回目からは、どこにも出ない書き手に渡す）。
        // 読んだ SkeletonData 自体は覚えない（覚えると、読み直しのたびに古いデータが解放されずに残る）
        private static readonly HashSet<string> Logged = new HashSet<string>();
        private static readonly ManualLogSource Quiet = new ManualLogSource(PluginName);

        private static void AfterRead(SkeletonData __result)
        {
            var sd = __result;
            if (sd == null || !Enabled.Value) return;
            var log = Logged.Add(sd.Name + "/" + sd.Hash) ? Log : Quiet;
            try
            {
                if (Momoko.Value) EyelidRig.Apply(sd, log);
                FlatLids.Apply(sd, log, MiningGoblin.Value, BlacksmithSalamander.Value);
            }
            catch (Exception e)
            {
                log.LogWarning("Skipped " + sd.Name + ": " + e);
            }
        }
    }

    internal static class EyelidRig
    {
        private const string LashSlotKey = "f_face_eyelash";
        private const string WhiteSlotKey = "f_face_eyewhite";
        private const string EqSlotKey = "f_face_==";
        private const string SkipAnimationKey = "spawn";
        private const float StretchThreshold = 1.5f;
        private const float SampleStep = 1f / 30f;

        // まつげの黒い帯の中心をたどる折れ線（まつげの領域内の 0〜1 座標・u の小さい順）。切り抜きの境目に使う。
        // 事務所版の絵（279x110）で黒い帯の上下の端を列ごとに測って決めた。マップ版も同じ絵（縮小）なので共通
        private static readonly float[] LashLineA =
        {
            0.009f, 0.459f, 0.041f, 0.450f, 0.084f, 0.450f, 0.149f, 0.441f, 0.213f, 0.441f,
            0.278f, 0.455f, 0.321f, 0.473f, 0.357f, 0.482f, 0.378f, 0.486f
        };
        private static readonly float[] LashLineB =
        {
            0.568f, 0.459f, 0.600f, 0.441f, 0.643f, 0.409f, 0.708f, 0.395f, 0.772f, 0.391f,
            0.837f, 0.400f, 0.901f, 0.409f, 0.944f, 0.423f, 0.980f, 0.441f, 0.998f, 0.459f
        };

        // 本体は描画用に同じデータを何度も読み直す。辺の検出は重いので結果（p0,q0,p1,q1,...）を覚えておく
        private static readonly Dictionary<string, int[]> EdgeCache = new Dictionary<string, int[]>();

        private sealed class Plan
        {
            public Skin Skin;
            public MeshAttachment Mesh;
            public ClippingAttachment Clip;
            public Attachment White;
            public int Edges;
        }

        public static void Apply(SkeletonData sd, ManualLogSource log)
        {
            SlotData lash = FindSlot(sd, LashSlotKey), white = FindSlot(sd, WhiteSlotKey), eq = FindSlot(sd, EqSlotKey);
            if (lash == null) return;   // モモコではない
            // 並びは「白目・＝＝・黒目とハイライト…・まつげ」のはず。違えば何もしない（半端に当てると目の部品がまぶたの上に出る）
            if (white == null || eq == null || eq.Index != white.Index + 1 || eq.Index >= lash.Index
                || !string.IsNullOrEmpty(eq.AttachmentName) || string.IsNullOrEmpty(white.AttachmentName)
                || string.IsNullOrEmpty(lash.AttachmentName) || DrawOrderTouches(sd, white.Index, lash.Index))
            {
                log.LogWarning(sd.Name + ": unexpected eye slots, skipped");
                return;
            }
            string whiteKey = white.AttachmentName, lashKey = lash.AttachmentName;
            var endSlot = sd.Slots.Items[lash.Index - 1];

            // 先に全部組み立てて、最後にまとめて差し替える（途中で失敗したら何も変えない）
            var plans = new List<Plan>();
            foreach (var skin in sd.Skins)
            {
                var mesh = skin.GetAttachment(lash.Index, lashKey) as MeshAttachment;
                var whiteAtt = skin.GetAttachment(white.Index, whiteKey);
                if (mesh == null || mesh.Bones == null || mesh.SourceMesh != null || whiteAtt == null) continue;
                if (skin.GetAttachment(eq.Index, whiteKey) != null) throw new InvalidOperationException("name clash in " + skin.Name);
                var plan = Build(sd, skin, lash, mesh, endSlot, white, whiteAtt);
                plan.Skin = skin;
                plans.Add(plan);
            }
            if (plans.Count == 0) return;
            var timelines = MoveWhiteTimelines(sd, white.Index, eq.Index, whiteKey);

            foreach (var p in plans)
            {
                p.Skin.SetAttachment(lash.Index, lashKey, p.Mesh);
                p.Skin.SetAttachment(white.Index, whiteKey, p.Clip);
                p.Skin.SetAttachment(eq.Index, whiteKey, p.White);
            }
            eq.AttachmentName = whiteKey;
            foreach (var kv in timelines) kv.Key.SetTimelines(kv.Value, kv.Key.Bones);

            foreach (var p in plans)
            {
                // テクスチャの写しが作れなくても、形と切り抜きだけで動く（まつげのすぐ上の帯の色がまつげに付いてくるだけ）
                try { LashTexture.Apply(p.Mesh, log); }
                catch (Exception e) { log.LogWarning("[tex] " + p.Mesh.Name + ": " + e.GetType().Name + ": " + e.Message); }
                log.LogInfo(sd.Name + ": " + p.Skin.Name + "/" + p.Mesh.Name + " split=" + p.Edges
                    + " clip=" + p.Clip.WorldVerticesLength / 2);
            }
            log.LogInfo(sd.Name + ": eye-white timelines moved in " + timelines.Count + " animations");
        }

        private static SlotData FindSlot(SkeletonData sd, string key)
        {
            SlotData found = null;
            foreach (var s in sd.Slots)
            {
                if (!s.Name.EndsWith(key, StringComparison.Ordinal)) continue;
                if (found != null) return null;   // 2つあったら当てない
                found = s;
            }
            return found;
        }

        // 描く順を入れ替えるアニメが、白目〜まつげのスロットの並びを崩すか（崩すと切り抜きが別の部品に掛かる）
        private static bool DrawOrderTouches(SkeletonData sd, int first, int last)
        {
            foreach (var an in sd.Animations)
            {
                foreach (var tl in an.Timelines)
                {
                    var dt = tl as DrawOrderTimeline;
                    if (dt == null) continue;
                    foreach (var order in dt.DrawOrders)
                    {
                        if (order == null) continue;
                        int at = Array.IndexOf(order, first);
                        if (at < 0 || at + (last - first) >= order.Length) return true;
                        for (int i = 0; i <= last - first; i++) if (order[at + i] != first + i) return true;
                    }
                }
            }
            return false;
        }

        private static Plan Build(SkeletonData sd, Skin skin, SlotData lash, MeshAttachment original, SlotData endSlot,
            SlotData white, Attachment whiteAtt)
        {
            var sk = new Skeleton(sd);
            sk.SetSkin(skin);
            string cacheKey = sd.Hash + "/" + original.Name + "/" + original.WorldVerticesLength + "/" + original.Triangles.Length;
            int[] pairs;
            if (!EdgeCache.TryGetValue(cacheKey, out pairs))
            {
                pairs = FindStretchedEdges(sd, sk, lash.Index, original);
                EdgeCache[cacheKey] = pairs;
            }
            if (pairs.Length == 0) throw new InvalidOperationException("no stretched edges in " + original.Name);

            sk.SetupPose();
            sk.UpdateWorldTransform(Physics.Pose);
            var mesh = CloneMesh(original);
            Split(sk, lash.Index, mesh, pairs, true, null);
            var clip = BuildClip(sk, lash, mesh, endSlot);
            var whiteRegion = whiteAtt as RegionAttachment;
            var whiteOut = whiteRegion != null ? MoveDroop(sk, lash, mesh, pairs, white, whiteRegion) : whiteAtt;
            return new Plan { Mesh = mesh, Clip = clip, White = whiteOut, Edges = pairs.Length / 2 };
        }

        // ---- 伸びる辺の検出 ------------------------------------------------

        // 全アニメを SampleStep 刻みで回し、スロットのボーン座標で長さが StretchThreshold 倍を超えた辺を拾う。
        // 静止位置から大きく動いた方を q（まつげ側）、もう一方を p（動かない側）として p,q の組を返す
        internal static int[] FindStretchedEdges(SkeletonData sd, Skeleton sk, int slotIndex, MeshAttachment m)
        {
            sk.SetupPose();
            sk.UpdateWorldTransform(Physics.Pose);
            var rest = LocalVerts(sk, slotIndex, m);
            var bestRatio = new Dictionary<long, float>();
            var bestPose = new Dictionary<long, float[]>();
            var tri = m.Triangles;
            foreach (var an in sd.Animations)
            {
                if (an.Name.IndexOf(SkipAnimationKey, StringComparison.Ordinal) >= 0) continue;
                int steps = (int)Math.Floor(an.Duration / SampleStep + 1e-4f);
                for (int si = 0; si <= steps; si++)
                {
                    sk.SetupPose();
                    an.Apply(sk, 0, si * SampleStep, true, null, 1, MixFrom.Setup, false, false, false);
                    sk.UpdateWorldTransform(Physics.Pose);
                    float[] w = null;
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        for (int e = 0; e < 3; e++)
                        {
                            int a = tri[i + e], b = tri[i + (e + 1) % 3];
                            float l0 = Len(rest, a, b);
                            if (l0 <= 0) continue;
                            if (w == null) w = LocalVerts(sk, slotIndex, m);
                            float ratio = Len(w, a, b) / l0;
                            if (ratio < StretchThreshold) continue;
                            long key = Key(a, b);
                            float cur;
                            if (!bestRatio.TryGetValue(key, out cur) || cur < ratio)
                            {
                                bestRatio[key] = ratio;
                                bestPose[key] = w;
                            }
                        }
                    }
                }
            }
            var pairs = new List<int>();
            foreach (var kv in bestPose)
            {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
                if (Dist(rest, kv.Value, a) > Dist(rest, kv.Value, b)) { pairs.Add(b); pairs.Add(a); }
                else { pairs.Add(a); pairs.Add(b); }
            }
            return pairs.ToArray();
        }

        // 基準のボーンの座標での頂点位置（キャラ全体の移動を差し引くため）
        // 頂点の位置を測る基準のボーン。顔のボーン（名前が face）があればそれ、無ければスロットのボーン。
        // 2026-11-09: スロットのボーンで測っていたら、サラマンダーのまぶたの肌（スロットのボーンが root）で、
        //             体ごと動く分が差し引けず、動く側・動かない側の判定と塗る色の位置がずれた
        internal static Bone RefBone(Skeleton sk, int slotIndex)
        {
            var face = sk.FindBone("face");
            return face != null ? face : sk.Slots.Items[slotIndex].Bone;
        }

        internal static float[] LocalVerts(Skeleton sk, int slotIndex, MeshAttachment m)
        {
            var slot = sk.Slots.Items[slotIndex];
            var w = new float[m.WorldVerticesLength];
            m.ComputeWorldVertices(sk, slot, 0, w.Length, w, 0, 2);
            var bp = RefBone(sk, slotIndex).AppliedPose;
            for (int i = 0; i < w.Length; i += 2)
            {
                float lx, ly;
                bp.WorldToLocal(w[i], w[i + 1], out lx, out ly);
                w[i] = lx;
                w[i + 1] = ly;
            }
            return w;
        }

        // ---- メッシュ --------------------------------------------------------

        // 元のデータは触らず、複製に手を入れる
        internal static MeshAttachment CloneMesh(MeshAttachment m)
        {
            var c = new MeshAttachment(m.Name, new Sequence(m.Sequence));
            c.Bones = (int[])m.Bones.Clone();
            c.Vertices = (float[])m.Vertices.Clone();
            c.WorldVerticesLength = m.WorldVerticesLength;
            c.RegionUVs = (float[])m.RegionUVs.Clone();
            c.Triangles = (int[])m.Triangles.Clone();
            c.HullLength = m.HullLength;
            if (m.Edges != null) c.Edges = (int[])m.Edges.Clone();
            c.Width = m.Width;
            c.Height = m.Height;
            c.Path = m.Path;
            c.SetColor(m.GetColor());
            c.UpdateSequence();
            return c;
        }

        // 伸びる辺 p-q ごとに頂点 c を足す。c は「q の重みで、静止時に p の位置・p の UV」。
        //   ・p から上（まぶた）は動かない。p-q 間の絵（まつげのすぐ上の細い帯）は c 側に付いてまつげと一緒に下がる
        //   ・p と c の間（静止時は重なっていて面積 0）が、まぶたが下がると開く。ここの三角形は作らない（下の頭の絵が見える）
        // skipHullEdges: p が輪郭の頂点の辺を割らない（モモコ用）。gapOut: 隙間の三角形を捨てずにここへ入れる（頂点番号3つずつ）
        internal static void Split(Skeleton sk, int slotIndex, MeshAttachment m, int[] pairs, bool skipHullEdges, List<int> gapOut)
        {
            int n = m.WorldVerticesLength / 2;
            var srcBones = m.Bones;
            var srcVerts = m.Vertices;
            var srcUVs = m.RegionUVs;

            var boneStarts = new int[n];
            var vertStarts = new int[n];
            for (int v = 0, bi = 0, vi = 0; v < n; v++)
            {
                boneStarts[v] = bi;
                vertStarts[v] = vi;
                int cnt = srcBones[bi];
                bi += 1 + cnt;
                vi += cnt * 3;
            }

            var world = new float[n * 2];
            m.ComputeWorldVertices(sk, sk.Slots.Items[slotIndex], 0, world.Length, world, 0, 2);

            var bones = new List<int>(srcBones);
            var verts = new List<float>(srcVerts);
            var uvs = new List<float>(srcUVs);
            var cutOf = new Dictionary<long, int>();
            var cutAt = new List<int>();     // 足した頂点 c ごとに、静止時に重なる元の頂点 p
            int hull = m.HullLength / 2;     // 頂点は輪郭のものが先に並ぶ

            for (int k = 0; k < pairs.Length; k += 2)
            {
                int p = pairs[k], q = pairs[k + 1];
                long key = Key(p, q);
                if (cutOf.ContainsKey(key)) continue;
                // p が輪郭の頂点（絵の縁）の辺は割らない。そこはまぶたの色でなく、まつげの線の端そのもの（目尻・目頭の先）で、
                // 割ると線の端がちぎれて欠片になる。割らなければ元どおり線の端が伸びて尖った形になる
                if (skipHullEdges && p < hull) continue;

                // q の重みのまま、各ボーンの局所座標を Δ = p - q だけずらす（静止時に p と重なる）
                float dx = world[p * 2] - world[q * 2];
                float dy = world[p * 2 + 1] - world[q * 2 + 1];
                int cnt = srcBones[boneStarts[q]];
                bones.Add(cnt);
                for (int j = 0; j < cnt; j++)
                {
                    int boneIndex = srcBones[boneStarts[q] + 1 + j];
                    var bp = sk.Bones.Items[boneIndex].AppliedPose;
                    float det = bp.A * bp.D - bp.B * bp.C;
                    int o = vertStarts[q] + j * 3;
                    bones.Add(boneIndex);
                    verts.Add(srcVerts[o] + (bp.D * dx - bp.B * dy) / det);
                    verts.Add(srcVerts[o + 1] + (-bp.C * dx + bp.A * dy) / det);
                    verts.Add(srcVerts[o + 2]);
                }
                uvs.Add(srcUVs[p * 2]);
                uvs.Add(srcUVs[p * 2 + 1]);
                cutOf[key] = n + cutAt.Count;
                cutAt.Add(p);
            }

            var tris = new List<int>();
            var src = m.Triangles;
            for (int i = 0; i < src.Length; i += 3)
            {
                int v0 = src[i], v1 = src[i + 1], v2 = src[i + 2];
                int c01 = Cut(cutOf, v0, v1), c12 = Cut(cutOf, v1, v2), c20 = Cut(cutOf, v2, v0);
                int cuts = (c01 >= 0 ? 1 : 0) + (c12 >= 0 ? 1 : 0) + (c20 >= 0 ? 1 : 0);
                if (cuts == 0)
                {
                    Tri(tris, gapOut, n, cutAt, v0, v1, v2);
                    continue;
                }
                if (cuts == 3)
                {
                    Tri(tris, gapOut, n, cutAt, v0, c01, c20);
                    Tri(tris, gapOut, n, cutAt, c01, v1, c12);
                    Tri(tris, gapOut, n, cutAt, c20, c12, v2);
                    Tri(tris, gapOut, n, cutAt, c01, c12, c20);
                    continue;
                }
                // 巻き順を保ったまま回して、切れた辺を a-b（1本）または a-b と d-a（2本）にそろえる
                int va = v0, vb = v1, vd = v2, cab = c01, cbd = c12, cda = c20;
                for (int r = 0; r < 3; r++)
                {
                    bool ok = cuts == 1 ? cab >= 0 : (cab >= 0 && cda >= 0);
                    if (ok) break;
                    int tv = va; va = vb; vb = vd; vd = tv;
                    int tc = cab; cab = cbd; cbd = cda; cda = tc;
                }
                if (cuts == 1)
                {
                    Tri(tris, gapOut, n, cutAt, va, cab, vd);
                    Tri(tris, gapOut, n, cutAt, cab, vb, vd);
                }
                else
                {
                    Tri(tris, gapOut, n, cutAt, va, cab, cda);
                    Tri(tris, gapOut, n, cutAt, cab, vb, vd);
                    Tri(tris, gapOut, n, cutAt, cab, vd, cda);
                }
            }

            m.Bones = bones.ToArray();
            m.Vertices = verts.ToArray();
            m.RegionUVs = uvs.ToArray();
            m.Triangles = tris.ToArray();
            m.WorldVerticesLength = (n + cutAt.Count) * 2;
            m.UpdateSequence();
        }

        private static int Cut(Dictionary<long, int> cutOf, int a, int b)
        {
            int c;
            return cutOf.TryGetValue(Key(a, b), out c) ? c : -1;
        }

        // 三角形を足す。静止時に面積 0（足した頂点 c を、重なる元の頂点 p に置き換えると2頂点が同じ）のものは隙間なので足さない。
        // 面積のしきい値で測ると、本体がデータを 0.01 倍で読むため小さい元の三角形まで落とす
        private static void Tri(List<int> l, List<int> gapOut, int n, List<int> cutAt, int a, int b, int c)
        {
            int ra = a >= n ? cutAt[a - n] : a, rb = b >= n ? cutAt[b - n] : b, rc = c >= n ? cutAt[c - n] : c;
            if (ra == rb || rb == rc || rc == ra)
            {
                if (gapOut != null) { gapOut.Add(a); gapOut.Add(b); gapOut.Add(c); }
                return;
            }
            l.Add(a);
            l.Add(b);
            l.Add(c);
        }

        // ---- 切り抜き --------------------------------------------------------

        // 目の部品を「まつげの線より下」だけ残す切り抜きを作る。形は、目の周りの大きな四角から、
        // 左右の目それぞれ「まつげの線より上」を切り欠いたもの。まつげの線の頂点はまつげのメッシュと同じ重みで動く
        private static ClippingAttachment BuildClip(Skeleton sk, SlotData lash, MeshAttachment mesh, SlotData endSlot)
        {
            var slot = sk.Slots.Items[lash.Index];
            int n = mesh.WorldVerticesLength / 2;
            var world = new float[n * 2];
            mesh.ComputeWorldVertices(sk, slot, 0, world.Length, world, 0, 2);
            var boneStarts = new int[n];
            var vertStarts = new int[n];
            for (int v = 0, bi = 0, vi = 0; v < n; v++)
            {
                boneStarts[v] = bi;
                vertStarts[v] = vi;
                int cnt = mesh.Bones[bi];
                bi += 1 + cnt;
                vi += cnt * 3;
            }

            var bones = new List<int>();
            var verts = new List<float>();
            int count = 0;

            // 絵の上向き（まつげ→まぶた）と右向き（u の増える向き）を、メッシュ自身から決める
            float ax, ay, bx, by, cx, cy;
            UvToWorld(mesh, world, 0.2f, 0.45f, out ax, out ay);
            UvToWorld(mesh, world, 0.2f, 0.15f, out bx, out by);
            UvToWorld(mesh, world, 0.8f, 0.45f, out cx, out cy);
            float ux = bx - ax, uy = by - ay, ul = (float)Math.Sqrt(ux * ux + uy * uy);
            ux /= ul; uy /= ul;
            float rx = uy, ry = -ux;
            if ((cx - ax) * rx + (cy - ay) * ry < 0) { rx = -rx; ry = -ry; }

            float rMin = float.MaxValue, rMax = float.MinValue, tMin = float.MaxValue, tMax = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float r = world[i * 2] * rx + world[i * 2 + 1] * ry, t = world[i * 2] * ux + world[i * 2 + 1] * uy;
                rMin = Math.Min(rMin, r); rMax = Math.Max(rMax, r);
                tMin = Math.Min(tMin, t); tMax = Math.Max(tMax, t);
            }
            float margin = (rMax - rMin) * 0.6f;
            float left = rMin - margin, right = rMax + margin, top = tMax + margin * 0.5f, bottom = tMin - margin * 2f;

            var faceBone = slot.Bone;
            // 四角の左上から、左下・右下・右上
            AddFixed(bones, verts, faceBone, left, top, rx, ry, ux, uy); count++;
            AddFixed(bones, verts, faceBone, left, bottom, rx, ry, ux, uy); count++;
            AddFixed(bones, verts, faceBone, right, bottom, rx, ry, ux, uy); count++;
            AddFixed(bones, verts, faceBone, right, top, rx, ry, ux, uy); count++;
            // 上の辺を右から左へ戻りながら、u の大きい方の目・小さい方の目の順に切り欠く
            foreach (var line in new[] { LashLineB, LashLineA })
            {
                int m2 = line.Length / 2;
                float x, y;
                UvToWorld(mesh, world, line[(m2 - 1) * 2], line[(m2 - 1) * 2 + 1], out x, out y);
                AddFixed(bones, verts, faceBone, x * rx + y * ry, top, rx, ry, ux, uy); count++;
                for (int i = m2 - 1; i >= 0; i--)
                {
                    AddOnMesh(bones, verts, mesh, boneStarts, vertStarts, line[i * 2], line[i * 2 + 1]);
                    count++;
                }
                UvToWorld(mesh, world, line[0], line[1], out x, out y);
                AddFixed(bones, verts, faceBone, x * rx + y * ry, top, rx, ry, ux, uy); count++;
            }

            var clip = new ClippingAttachment("lwf_eyelid_clip");
            clip.EndSlot = endSlot;
            clip.Bones = bones.ToArray();
            clip.Vertices = verts.ToArray();
            clip.WorldVerticesLength = count * 2;
            return clip;
        }

        // 顔のボーンに固定した頂点を足す。(r, t) は絵の右向き・上向きで測った位置
        private static void AddFixed(List<int> bones, List<float> verts, Bone bone, float r, float t,
            float rx, float ry, float ux, float uy)
        {
            float lx, ly;
            bone.AppliedPose.WorldToLocal(r * rx + t * ux, r * ry + t * uy, out lx, out ly);
            bones.Add(1);
            bones.Add(bone.Data.Index);
            verts.Add(lx);
            verts.Add(ly);
            verts.Add(1f);
        }

        // まつげの絵の上の点 (u, v) を、その点を含む三角形の3頂点の重みを混ぜた頂点として足す（メッシュのその点と同じに動く）
        private static void AddOnMesh(List<int> bones, List<float> verts, MeshAttachment mesh, int[] boneStarts, int[] vertStarts, float u, float v)
        {
            int i0, i1, i2;
            float b0, b1, b2;
            Locate(mesh, u, v, out i0, out i1, out i2, out b0, out b1, out b2);
            int at = bones.Count, total = 0;
            bones.Add(0);
            var idx = new[] { i0, i1, i2 };
            var bary = new[] { b0, b1, b2 };
            for (int k = 0; k < 3; k++)
            {
                if (Math.Abs(bary[k]) < 1e-5f) continue;
                int cnt = mesh.Bones[boneStarts[idx[k]]];
                for (int j = 0; j < cnt; j++)
                {
                    int o = vertStarts[idx[k]] + j * 3;
                    bones.Add(mesh.Bones[boneStarts[idx[k]] + 1 + j]);
                    verts.Add(mesh.Vertices[o]);
                    verts.Add(mesh.Vertices[o + 1]);
                    verts.Add(mesh.Vertices[o + 2] * bary[k]);
                    total++;
                }
            }
            bones[at] = total;
        }

        private static void UvToWorld(MeshAttachment mesh, float[] world, float u, float v, out float x, out float y)
        {
            int i0, i1, i2;
            float b0, b1, b2;
            Locate(mesh, u, v, out i0, out i1, out i2, out b0, out b1, out b2);
            x = world[i0 * 2] * b0 + world[i1 * 2] * b1 + world[i2 * 2] * b2;
            y = world[i0 * 2 + 1] * b0 + world[i1 * 2 + 1] * b1 + world[i2 * 2 + 1] * b2;
        }

        // 絵の上の点 (u, v) を含む三角形と重心座標。どの三角形にも入らない点は、一番近い三角形で外挿する
        private static void Locate(MeshAttachment mesh, float u, float v, out int i0, out int i1, out int i2,
            out float b0, out float b1, out float b2)
        {
            var uv = mesh.RegionUVs;
            var tri = mesh.Triangles;
            float best = float.MinValue;
            i0 = i1 = i2 = 0;
            b0 = b1 = b2 = 0;
            for (int i = 0; i < tri.Length; i += 3)
            {
                int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                float ax = uv[a * 2], ay = uv[a * 2 + 1], bx = uv[b * 2], by = uv[b * 2 + 1], cx = uv[c * 2], cy = uv[c * 2 + 1];
                float den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
                if (Math.Abs(den) < 1e-9f) continue;
                float w0 = ((by - cy) * (u - cx) + (cx - bx) * (v - cy)) / den;
                float w1 = ((cy - ay) * (u - cx) + (ax - cx) * (v - cy)) / den;
                float w2 = 1 - w0 - w1;
                float score = Math.Min(w0, Math.Min(w1, w2));
                if (score <= best) continue;
                best = score;
                i0 = a; i1 = b; i2 = c;
                b0 = w0; b1 = w1; b2 = w2;
                if (score >= 0) return;
            }
        }

        // ---- まつげの垂れた部分 ----------------------------------------------

        // まつげの外側の垂れた部分（目尻の下へ下りる線・下まつげ）は、半分だけ動く重みになっている。深く閉じると、
        // まつげの線の方が先に下がって、垂れた部分がまつげの線の上へはみ出す（元は伸びたまぶたの絵がこれを隠していた）。
        // はみ出しを目の部品と同じ切り抜きで隠すため、垂れた部分の三角形をまつげのメッシュから外し、
        // 白目と一緒の付け物（切り抜きの中のスロットに置く）へ移す。絵は元のページのまま（垂れた部分にまぶたの色は無い）
        private static Attachment MoveDroop(Skeleton sk, SlotData lash, MeshAttachment mesh, int[] pairs, SlotData white, RegionAttachment whiteAtt)
        {
            int n = mesh.WorldVerticesLength / 2;
            var boneStarts = new int[n];
            var vertStarts = new int[n];
            for (int v = 0, bi = 0, vi = 0; v < n; v++)
            {
                boneStarts[v] = bi;
                vertStarts[v] = vi;
                int cnt = mesh.Bones[bi];
                bi += 1 + cnt;
                vi += cnt * 3;
            }
            // まつげと一緒に動くボーン = 割った辺（p が輪郭でないもの）の q 側の頂点が乗っているボーン。
            // p が輪郭の辺には、垂れた部分どうしの辺も混ざっている（q が半分だけ動く頂点）ので数えない
            var moving = new HashSet<int>();
            int hull = mesh.HullLength / 2;
            for (int k = 1; k < pairs.Length; k += 2)
            {
                if (pairs[k - 1] < hull) continue;
                int q = pairs[k], cnt = mesh.Bones[boneStarts[q]];
                for (int j = 0; j < cnt; j++)
                    if (mesh.Vertices[vertStarts[q] + j * 3 + 2] > 0.5f) moving.Add(mesh.Bones[boneStarts[q] + 1 + j]);
            }
            var uv = mesh.RegionUVs;
            var partial = new bool[n];
            var below = new bool[n];
            for (int v = 0; v < n; v++)
            {
                float mw = 0;
                int cnt = mesh.Bones[boneStarts[v]];
                for (int j = 0; j < cnt; j++)
                    if (moving.Contains(mesh.Bones[boneStarts[v] + 1 + j])) mw += mesh.Vertices[vertStarts[v] + j * 3 + 2];
                partial[v] = mw > 0.05f && mw < 0.95f;
                below[v] = uv[v * 2 + 1] >= LineV(uv[v * 2]) - 0.03f;
            }

            var keep = new List<int>();
            var droop = new List<int>();
            var tri = mesh.Triangles;
            for (int i = 0; i < tri.Length; i += 3)
            {
                int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                bool isDroop = (partial[a] || partial[b] || partial[c]) && below[a] && below[b] && below[c];
                var l = isDroop ? droop : keep;
                l.Add(a); l.Add(b); l.Add(c);
            }
            if (droop.Count == 0) return whiteAtt;

            // ページ全体を領域にした付け物を作る（白目とまつげの絵は同じページの別の場所にある）
            var whiteRegion = whiteAtt.Sequence.GetRegion(0) as AtlasRegion;
            var lashRegion = mesh.Sequence.GetRegion(0) as AtlasRegion;
            if (whiteRegion == null || lashRegion == null || whiteRegion.page != lashRegion.page) return whiteAtt;
            var pageRegion = PageRegion(whiteRegion);

            var bones = new List<int>();
            var verts = new List<float>();
            var uvs = new List<float>();
            var tris = new List<int>();

            // 白目の四角（白目のスロットのボーンに固定）
            var whiteSlot = sk.Slots.Items[white.Index];
            var wv = new float[8];
            whiteAtt.ComputeWorldVertices(whiteSlot, whiteAtt.GetOffsets(whiteSlot.AppliedPose), wv, 0, 2);
            var wuv = whiteAtt.Sequence.GetUVs(0);
            for (int i = 0; i < 4; i++)
            {
                float lx, ly;
                whiteSlot.Bone.AppliedPose.WorldToLocal(wv[i * 2], wv[i * 2 + 1], out lx, out ly);
                bones.Add(1); bones.Add(whiteSlot.Bone.Data.Index);
                verts.Add(lx); verts.Add(ly); verts.Add(1f);
                uvs.Add(wuv[i * 2]); uvs.Add(1f - wuv[i * 2 + 1]);
            }
            tris.AddRange(new[] { 0, 1, 2, 2, 3, 0 });

            // 垂れた部分の三角形（重みはまつげのメッシュのまま）
            var luv = mesh.Sequence.GetUVs(0);
            var map = new Dictionary<int, int>();
            foreach (int v in droop)
            {
                int d;
                if (!map.TryGetValue(v, out d))
                {
                    d = uvs.Count / 2;
                    int cnt = mesh.Bones[boneStarts[v]];
                    for (int j = 0; j <= cnt; j++) bones.Add(mesh.Bones[boneStarts[v] + j]);
                    for (int j = 0; j < cnt * 3; j++) verts.Add(mesh.Vertices[vertStarts[v] + j]);
                    uvs.Add(luv[v * 2]); uvs.Add(1f - luv[v * 2 + 1]);
                    map[v] = d;
                }
                tris.Add(d);
            }

            var seq = new Sequence(mesh.Sequence);
            seq.Regions[0] = pageRegion;
            var combined = new MeshAttachment(whiteAtt.Name, seq);
            combined.Bones = bones.ToArray();
            combined.Vertices = verts.ToArray();
            combined.WorldVerticesLength = uvs.Count;
            combined.RegionUVs = uvs.ToArray();
            combined.Triangles = tris.ToArray();
            combined.HullLength = 0;
            combined.SetColor(whiteAtt.GetColor());
            combined.UpdateSequence();

            mesh.Triangles = keep.ToArray();
            return combined;
        }

        // まつげの黒い帯の中心の高さ（領域内の v）。折れ線の外は端の値
        private static float LineV(float u)
        {
            var line = u < 0.47f ? LashLineA : LashLineB;
            int m = line.Length / 2;
            if (u <= line[0]) return line[1];
            for (int i = 1; i < m; i++)
            {
                if (u > line[i * 2]) continue;
                float t = (u - line[(i - 1) * 2]) / (line[i * 2] - line[(i - 1) * 2]);
                return line[(i - 1) * 2 + 1] + (line[i * 2 + 1] - line[(i - 1) * 2 + 1]) * t;
            }
            return line[(m - 1) * 2 + 1];
        }

        // ---- 白目の引っ越し --------------------------------------------------

        // 白目のスロットは切り抜きに使うので、白目の絵は次のスロット（＝＝の枠）で出す。
        // 白目を出し入れするアニメごとに、移した先のスロットの出し入れを作る:
        //   ＝＝を出している間は＝＝、それ以外で白目を出している間は白目、どちらでもなければ何も出さない
        // 元の白目のスロットの出し入れはそのまま残す（同じ名前で切り抜きが出し入れされる）
        private static Dictionary<Animation, ExposedList<Timeline>> MoveWhiteTimelines(SkeletonData sd, int whiteIndex, int eqIndex, string whiteKey)
        {
            var result = new Dictionary<Animation, ExposedList<Timeline>>();
            foreach (var an in sd.Animations)
            {
                AttachmentTimeline tw = null, te = null;
                int eqAt = -1;
                for (int i = 0; i < an.Timelines.Count; i++)
                {
                    var at = an.Timelines.Items[i] as AttachmentTimeline;
                    if (at == null) continue;
                    if (at.SlotIndex == whiteIndex) tw = at;
                    if (at.SlotIndex == eqIndex) { te = at; eqAt = i; }
                }
                if (tw == null && te == null) continue;

                var times = new List<float>();
                if (tw != null) times.AddRange(tw.Frames);
                if (te != null) times.AddRange(te.Frames);
                times.Sort();
                var keyTimes = new List<float>();
                var keyNames = new List<string>();
                foreach (var time in times)
                {
                    if (keyTimes.Count > 0 && Math.Abs(keyTimes[keyTimes.Count - 1] - time) < 1e-5f) continue;
                    string w = StateAt(tw, time, whiteKey), e = StateAt(te, time, null);
                    keyTimes.Add(time);
                    keyNames.Add(e != null ? e : (w == whiteKey ? whiteKey : null));
                }
                var nt = new AttachmentTimeline(keyTimes.Count, eqIndex);
                for (int i = 0; i < keyTimes.Count; i++) nt.SetFrame(i, keyTimes[i], keyNames[i]);

                var list = new ExposedList<Timeline>(an.Timelines.Count + 1);
                foreach (var tl in an.Timelines) list.Add(tl);
                if (eqAt >= 0) list.Items[eqAt] = nt; else list.Add(nt);
                result[an] = list;
            }
            return result;
        }

        // その時刻にタイムラインが出している付け物の名前（最初のキーより前・タイムライン無しは setup）
        private static string StateAt(AttachmentTimeline tl, float time, string setup)
        {
            if (tl == null) return setup;
            string name = setup;
            for (int i = 0; i < tl.Frames.Length; i++)
            {
                if (tl.Frames[i] <= time + 1e-5f) name = tl.AttachmentNames[i];
            }
            return name;
        }

        // ページ全体を領域にした写し。これを領域にしたメッシュは、RegionUVs に (ページの u, 1 - ページの v) を入れると
        // ページのどこでも指せる（上下の向きがどちらの読み込み方でも同じ式になる）
        internal static AtlasRegion PageRegion(AtlasRegion region)
        {
            var r = region.Clone();
            r.u = 0; r.u2 = 1; r.v = 1; r.v2 = 0;
            r.width = r.originalWidth = region.page.width;
            r.height = r.originalHeight = region.page.height;
            r.offsetX = 0; r.offsetY = 0; r.degrees = 0; r.rotate = false;
            return r;
        }

        private static long Key(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        private static float Len(float[] v, int a, int b)
        {
            float dx = v[a * 2] - v[b * 2], dy = v[a * 2 + 1] - v[b * 2 + 1];
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float Dist(float[] r0, float[] w, int a)
        {
            float dx = r0[a * 2] - w[a * 2], dy = r0[a * 2 + 1] - w[a * 2 + 1];
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }

    // 使い魔（ゴブリン・サラマンダー）のまぶた。モモコと違って画面に何十体も出るので、毎フレームの負荷になる切り抜きは使わない。
    // まぶたの絵（肌色の帯）を、下にある頭の絵そのもの（肌と髪の影）を映す「窓」に置き換える。変えるのはメッシュだけ（読み込み時の1回）。
    //   1. 頂点を「動かない」「まつげと一緒に動く」に分ける（一番大きく動いたコマでの動きが、最大の 15% 未満／60% 超）
    //   2. 動かない頂点と動く頂点をまたぐ三角形（＝まぶたが下がると伸びる所）は、頭の絵（同じ向きの front/head・left/head）を貼る。
    //      動かない頂点は静止位置の頭の絵、動く頂点は一番下がった位置の頭の絵を指す。一番下がったコマで下の頭の絵とぴったり重なる
    //      （途中のコマは少し縦に縮むが、目のあった所の頭の絵は肌色の一色なので見分けがつかない）
    //   3. 貼る範囲は、白目の絵（eye_white）の横幅までに切り詰める。その外は何も描かない（下の髪の線などがそのまま見える）
    //   4. 動かない頂点だけの三角形（まぶたの絵の上の縁）も、静止位置の頭の絵を貼る（下の頭の絵と同じ見た目になり、
    //      上の縁の明るい半透明の画素が影の上に線になって残らない。まぶたの絵が隠していた黒目の上の縁は隠れたまま）。
    //      そこに描かれていた暗い線（眉のような短い線）は、四角で描き戻す。暗い線を探すためにテクスチャを1回読む
    //   5. 動く頂点だけの三角形（まつげ・まぶたの下の縁）は元のまま
    // メッシュをページ全体の座標に移して指すので、テクスチャは書き換えないし、マテリアルも増えない。
    // 対象のスロット: 名前が /eye2（ゴブリン。まつげとまぶたが1枚）・/eye_l_skin・/eye_r_skin（サラマンダー。まぶたの肌だけ）で、
    // 付け物が重み付きメッシュのもの。レイヴンやスライムの eye2 は四角い絵なので当たらない
    internal static class FlatLids
    {
        private static readonly string[] SlotKeys = { "/eye2", "/eye_l_skin", "/eye_r_skin" };
        // まぶたの絵を丸ごとまつげと一緒に下げるか（サラマンダー。下の「2 つの方式」）
        private static readonly bool[] Slide = { false, true, true };
        // どのキャラのスロットか（0 = 採掘ゴブリン, 1 = 鍛冶師サラマンダー）
        private static readonly int[] Owner = { 0, 1, 1 };
        // 貼る絵を、まつげ側へ少しはみ出させる量（まつげが下がる距離に対する割合）。まつげの上の縁は半透明なので、その下まで入れておく
        private const float Overlap = 0.04f;
        // ずらしの方式でのはみ出し。まぶたの絵の上の縁は半透明で、その下に白目が透けると薄い線になるので、縁の下まで入れる
        private const float SlideOverlap = 0.15f;
        // 白目の横幅に足す余白（幅に対する割合）
        private const float TrimMargin = 0.02f;
        // 開発用（手元の検証でページの画素を渡す。0 行目が UV の v = 0 の側）
        internal static Func<AtlasPage, UnityEngine.Color32[]> DevPagePixels;

        private sealed class Info
        {
            public float[] Rest, Low;     // 基準のボーンの座標での頂点位置（静止・一番下がったコマ）
            public bool[] Fixed, Moving;  // 動かない頂点か・まつげと一緒に動く頂点か
            public float Travel;          // まつげが下がる距離
        }
        private static readonly Dictionary<string, Info> Cache = new Dictionary<string, Info>();

        private sealed class Plan
        {
            public Skin Skin;
            public int Slot;
            public string Key;
            public MeshAttachment Mesh;
            public int Windows, Lines, Dropped;
            public int WhiteSlot;
            public string WhiteKey;
            public MeshAttachment WhiteMesh;   // 白目の絵の差し替え（まつげに隠れている所を切り落としたもの）。無ければ null
        }

        public static void Apply(SkeletonData sd, ManualLogSource log, bool goblin, bool salamander)
        {
            var plans = new List<Plan>();
            foreach (var slot in sd.Slots)
            {
                int ki = -1;
                for (int k = 0; k < SlotKeys.Length; k++) if (slot.Name.EndsWith(SlotKeys[k], StringComparison.Ordinal)) ki = k;
                if (ki < 0 || string.IsNullOrEmpty(slot.AttachmentName)) continue;
                if (!(Owner[ki] == 0 ? goblin : salamander)) continue;
                string prefix = slot.Name.Substring(0, slot.Name.LastIndexOf('/') + 1);
                var head = sd.FindSlot(prefix + "head");
                var white = sd.FindSlot(prefix + "eye_white");
                if (head == null || string.IsNullOrEmpty(head.AttachmentName)) continue;
                foreach (var skin in sd.Skins)
                {
                    var mesh = skin.GetAttachment(slot.Index, slot.AttachmentName) as MeshAttachment;
                    var headAtt = skin.GetAttachment(head.Index, head.AttachmentName) as RegionAttachment;
                    if (mesh == null || mesh.Bones == null || mesh.SourceMesh != null || headAtt == null) continue;
                    var mr = mesh.Sequence.GetRegion(0) as AtlasRegion;
                    var hr = headAtt.Sequence.GetRegion(0) as AtlasRegion;
                    if (mr == null || hr == null || mr.page != hr.page) continue;
                    var whiteAtt = white != null && !string.IsNullOrEmpty(white.AttachmentName)
                        ? skin.GetAttachment(white.Index, white.AttachmentName) as RegionAttachment : null;
                    var plan = Build(sd, skin, slot, mesh, head, headAtt, white, whiteAtt, Slide[ki], log);
                    if (plan != null) plans.Add(plan);
                }
            }
            foreach (var p in plans)
            {
                p.Skin.SetAttachment(p.Slot, p.Key, p.Mesh);
                string trimmed = "";
                if (p.WhiteMesh != null && p.Skin.GetAttachment(p.WhiteSlot, p.WhiteKey) is RegionAttachment)
                {
                    p.Skin.SetAttachment(p.WhiteSlot, p.WhiteKey, p.WhiteMesh);
                    trimmed = " white=" + p.WhiteMesh.Triangles.Length / 3;
                }
                log.LogInfo(sd.Name + ": " + p.Skin.Name + "/" + p.Mesh.Name + " windows=" + p.Windows + " lines=" + p.Lines + trimmed);
            }
        }

        // 組み立て中の頂点。重み付きの頂点はボーンごとの (局所 x, 局所 y, 重み) の並び
        private sealed class V
        {
            public List<int> Bones = new List<int>();
            public List<float> Data = new List<float>();   // x, y, w ずつ
            public float U, Vv;                            // ページ全体の座標（Vv は 1 - ページの v）
            public float Rx, Ry, Lx, Ly;                   // 基準のボーンの座標での位置（静止・一番下がったコマ）
        }

        private static Plan Build(SkeletonData sd, Skin skin, SlotData slot, MeshAttachment original, SlotData head, RegionAttachment headAtt,
            SlotData white, RegionAttachment whiteAtt, bool slide, ManualLogSource log)
        {
            var sk = new Skeleton(sd);
            sk.SetSkin(skin);
            string cacheKey = sd.Hash + "/" + original.Name + "/" + original.WorldVerticesLength + "/" + original.Triangles.Length;
            Info info;
            if (!Cache.TryGetValue(cacheKey, out info))
            {
                info = Analyze(sd, sk, slot.Index, original);
                Cache[cacheKey] = info;
            }
            if (info == null) return null;

            sk.SetupPose();
            sk.UpdateWorldTransform(Physics.Pose);
            var refPose = EyelidRig.RefBone(sk, slot.Index).AppliedPose;

            // 元の頂点を読む（UV はページ全体の座標へ）
            int n = original.WorldVerticesLength / 2;
            var pageUV = original.Sequence.GetUVs(0);
            var vs = new List<V>();
            for (int v = 0, bi = 0, vi = 0; v < n; v++)
            {
                var x = new V { U = pageUV[v * 2], Vv = 1f - pageUV[v * 2 + 1],
                    Rx = info.Rest[v * 2], Ry = info.Rest[v * 2 + 1], Lx = info.Low[v * 2], Ly = info.Low[v * 2 + 1] };
                int cnt = original.Bones[bi++];
                for (int j = 0; j < cnt; j++)
                {
                    x.Bones.Add(original.Bones[bi++]);
                    x.Data.Add(original.Vertices[vi++]); x.Data.Add(original.Vertices[vi++]); x.Data.Add(original.Vertices[vi++]);
                }
                vs.Add(x);
            }

            // 2 つの方式:
            //   置き換え（ゴブリン）: 動かない頂点を含む三角形を、そのまま頭の絵の窓に置き換える。まぶたの絵は描かなくなる
            //   ずらし（サラマンダー）: まぶたの絵は丸ごとまつげと一緒に下げ、絵の上の縁に開く隙間だけを窓にする。
            //     目を開けているときは隙間の面積が 0 で、元と 1 画素も変わらない。まぶたの絵の三角形に透明な所が多く、
            //     置き換えると目を開けたときに黒目の上へ窓がはみ出すので、こちらを使う
            var keep = new List<int>();          // 元の絵のまま描く三角形
            var polys = new List<int[]>();       // 窓にする多角形（頂点番号の並び）
            var still3 = new List<int>();        // 動かない頂点だけの三角形（暗い線を探す）
            var parent = new Dictionary<int, int>();   // ずらしで足した頂点 → 静止時に重なる動かない頂点
            var src = original.Triangles;
            Dictionary<int, int> copyOf = null;        // ずらし: 動かない頂点 → 動く写し
            var extraFixed = new HashSet<int>();       // 後から足した動かない頂点
            var extFrom = new Dictionary<int, int>();  // 後から足した動かない頂点 → 元になった動かない頂点
            if (!slide)
            {
                for (int i = 0; i < src.Length; i += 3)
                {
                    int a = src[i], b = src[i + 1], d = src[i + 2];
                    int nf = (info.Fixed[a] ? 1 : 0) + (info.Fixed[b] ? 1 : 0) + (info.Fixed[d] ? 1 : 0);
                    int nm = (info.Moving[a] ? 1 : 0) + (info.Moving[b] ? 1 : 0) + (info.Moving[d] ? 1 : 0);
                    // 動かない頂点だけの三角形も窓にする（静止位置の頭の絵＝下の頭の絵と同じ見た目）。
                    // 2026-11-09: ここを描かないようにしたら、まぶたの絵が元から隠していた黒目の上の縁が見えて、眉の横に細い黒い線が出た
                    if (nf == 3) { still3.Add(a); still3.Add(b); still3.Add(d); }
                    if (nf == 3 || (nf >= 1 && nm >= 1)) polys.Add(new[] { a, b, d });
                    else { keep.Add(a); keep.Add(b); keep.Add(d); }
                }
            }
            else
            {
                // 動かない頂点 p ごとに、静止時に p と重なる「動く写し」を足す。重みは、p と三角形を共有する動く頂点（無ければ一番近い動く頂点）のもの
                copyOf = new Dictionary<int, int>();
                for (int i = 0; i < src.Length; i += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int pv = src[i + e];
                        if (!info.Fixed[pv] || copyOf.ContainsKey(pv)) continue;
                        int donor = -1;
                        for (int k = 0; k < 3; k++) if (info.Moving[src[i + k]]) donor = src[i + k];
                        if (donor >= 0) copyOf[pv] = AddCopy(sk, refPose, vs, pv, donor, parent);
                    }
                }
                for (int pv = 0; pv < n; pv++)
                {
                    if (!info.Fixed[pv] || copyOf.ContainsKey(pv)) continue;
                    int donor = -1;
                    float bestD = float.MaxValue;
                    for (int q = 0; q < n; q++)
                    {
                        if (!info.Moving[q]) continue;
                        float ddx = vs[q].Rx - vs[pv].Rx, ddy = vs[q].Ry - vs[pv].Ry, dd = ddx * ddx + ddy * ddy;
                        if (dd < bestD) { bestD = dd; donor = q; }
                    }
                    if (donor >= 0) copyOf[pv] = AddCopy(sk, refPose, vs, pv, donor, parent);
                }
                // 元の三角形は写しの方を指す（絵が丸ごと下がる）。動かない頂点どうしの辺ごとに、p と写しの間の四角を窓にする
                var seen = new HashSet<long>();
                for (int i = 0; i < src.Length; i += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int a = src[i + e], b = src[i + (e + 1) % 3];
                        int v = src[i + e], c;
                        keep.Add(copyOf.TryGetValue(v, out c) ? c : v);
                        if (!info.Fixed[a] || !info.Fixed[b] || !copyOf.ContainsKey(a) || !copyOf.ContainsKey(b)) continue;
                        if (!seen.Add(a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a)) continue;
                        polys.Add(new[] { a, b, copyOf[b], copyOf[a] });
                    }
                }
            }
            if (polys.Count == 0) return null;

            // 頭の絵: 基準のボーンの座標 → 世界 → 頭の絵のページ上の位置（一次の対応）
            var headSlot = sk.Slots.Items[head.Index];
            var hv = new float[8];
            headAtt.ComputeWorldVertices(headSlot, headAtt.GetOffsets(headSlot.AppliedPose), hv, 0, 2);
            var huv = headAtt.Sequence.GetUVs(0);
            float ex = hv[6] - hv[0], ey = hv[7] - hv[1], fx = hv[2] - hv[0], fy = hv[3] - hv[1];
            float det = ex * fy - ey * fx;
            if (Math.Abs(det) < 1e-12f) return null;

            // まつげの動く向きと、それに垂直な軸。横幅の切り詰めは、垂直な軸で白目の絵の端から端まで
            float dx = 0, dy = 0;
            for (int i = 0; i < n; i++) if (info.Moving[i]) { dx += vs[i].Lx - vs[i].Rx; dy += vs[i].Ly - vs[i].Ry; }
            float dl = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dl <= 0) return null;
            float ax = -dy / dl, ay = dx / dl;
            float ox = dx / dl * info.Travel * Overlap, oy = dy / dl * info.Travel * Overlap;
            float lo = float.MinValue, hi = float.MaxValue;
            // 白目の塊ごとの切り取りの形（半平面 nx, ny, limit の並び）。空なら幅だけで切る
            var shapes = new List<float[]>();
            MeshAttachment whiteMesh = null;
            if (whiteAtt != null)
            {
                var whiteSlot = sk.Slots.Items[white.Index];
                var wv = new float[8];
                whiteAtt.ComputeWorldVertices(whiteSlot, whiteAtt.GetOffsets(whiteSlot.AppliedPose), wv, 0, 2);
                lo = float.MaxValue; hi = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    float lx, ly;
                    refPose.WorldToLocal(wv[i * 2], wv[i * 2 + 1], out lx, out ly);
                    float t = lx * ax + ly * ay;
                    lo = Math.Min(lo, t); hi = Math.Max(hi, t);
                }
                // 白目の絵は左右の目が 1 枚に入っている。このメッシュが掛かる目の分だけの幅にする
                // （両目の間は白目が無く、覆うと前髪に肌色が被さる）。画素が読めなければ絵全体の幅のまま
                float cLo = float.MaxValue, cHi = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    if (!info.Fixed[i]) continue;
                    float t = vs[i].Rx * ax + vs[i].Ry * ay;
                    cLo = Math.Min(cLo, t); cHi = Math.Max(cHi, t);
                }
                float texel;
                // 形で切るのは、ずらしの方式（サラマンダー）だけ。置き換えの方式（ゴブリン）は白目の絵全体の幅のまま。
                // 2026-11-09: ゴブリンも形で切ったら、切った縁に白目の半透明の画素が薄い縦線になって出た。
                //             ゴブリンは前髪が目の部品より上にあり、両目の間も頭の絵の肌色なので、広く覆って困らない
                texel = 0;
                var meshRegion = (AtlasRegion)original.Sequence.GetRegion(0);
                var blobs = slide ? VisibleWhite(sd, skin, sk, refPose, white, whiteAtt, wv, ax, ay, meshRegion, out texel, log) : null;
                if (blobs != null && cHi > cLo)
                {
                    float mx = dx / dl, my = dy / dl;
                    float bl = float.MaxValue, bh = float.MinValue;
                    var whiteQuads = new List<float>();   // 白目の差し替えの四角（基準のボーンの座標 x, y を 4 点ずつ）
                    for (int bi = 0; bi < blobs.Count; bi += 2)
                    {
                        List<float> all = blobs[bi], vis = blobs[bi + 1];
                        if (vis.Count == 0) continue;
                        float b0 = float.MaxValue, b1 = float.MinValue, dBottom = float.MinValue;
                        for (int i = 0; i < vis.Count; i += 2)
                        {
                            float t = vis[i] * ax + vis[i + 1] * ay;
                            b0 = Math.Min(b0, t); b1 = Math.Max(b1, t);
                        }
                        for (int i = 0; i < all.Count; i += 2) dBottom = Math.Max(dBottom, all[i] * mx + all[i + 1] * my);
                        // 白目の差し替え: 目を開けているときに見えている画素の「上の縁より下」だけを残す。
                        // 余白は 1 画素ぶん（画素の半分＋なめらかにする分の半分）
                        foreach (var st in Strips(vis, ax, ay, mx, my, texel * TopMargin, texel * SideMargin))
                        {
                            float left = st[9], right = st[10], bottom = dBottom + texel * 1.5f;
                            whiteQuads.AddRange(new[]
                            {
                                left * ax + st[11] * mx, left * ay + st[11] * my, right * ax + st[12] * mx, right * ay + st[12] * my,
                                right * ax + bottom * mx, right * ay + bottom * my, left * ax + bottom * mx, left * ay + bottom * my
                            });
                        }
                        float overlap = Math.Min(b1, cHi) - Math.Max(b0, cLo);
                        if (overlap < (b1 - b0) * 0.3f) continue;
                        bl = Math.Min(bl, b0); bh = Math.Max(bh, b1);
                        // 窓の形: 見えている白目の画素と、白目とまぶたの間のスロットにある四角い絵（黒目・ハイライト）の不透明な画素。
                        // まぶたの絵は、白目の上にはみ出した黒目の上の縁も隠している。黒目は縁の半透明の 1 画素ぶん上へ広げる
                        var shapePts = new List<float>(vis);
                        for (int si = white.Index + 1; si < slot.Index; si++)
                        {
                            var sdata = sd.Slots.Items[si];
                            if (string.IsNullOrEmpty(sdata.AttachmentName)) continue;
                            var ra = skin.GetAttachment(si, sdata.AttachmentName) as RegionAttachment;
                            if (ra == null) continue;
                            var rslot = sk.Slots.Items[si];
                            var rv = new float[8];
                            ra.ComputeWorldVertices(rslot, ra.GetOffsets(rslot.AppliedPose), rv, 0, 2);
                            float tx;
                            var op = OpaquePoints(ra, rv, refPose, meshRegion, 128, out tx, log);
                            if (op == null) continue;
                            float ct = 0;
                            for (int i = 0; i < op.Count; i += 2) ct += (op[i] * ax + op[i + 1] * ay) * 2f / op.Count;
                            if (ct < b0 || ct > b1) continue;
                            for (int i = 0; i < op.Count; i += 2) { shapePts.Add(op[i] - mx * texel); shapePts.Add(op[i + 1] - my * texel); }
                        }
                        // 白目の差し替えより少しだけ広く（継ぎ目から白目がのぞかないように）
                        shapes.AddRange(Strips(shapePts, ax, ay, mx, my, texel * (TopMargin + 0.1f), texel * (SideMargin + 0.1f)));
                    }
                    if (bh > bl) { lo = bl; hi = bh; }
                    if (whiteQuads.Count > 0) whiteMesh = TrimmedWhite(sk, refPose, white, whiteAtt, wv, whiteQuads);
                }
                float margin = shapes.Count > 0 ? texel * (SideMargin + 0.1f) : (hi - lo) * TrimMargin;
                lo -= margin;
                hi += margin;

                // ずらし: 動かない頂点の列が白目の幅より短いと、端で白目が細く見える。列の両端から白目の幅の端まで窓を延ばす
                if (slide && copyOf != null && copyOf.Count > 0)
                {
                    int pMin = -1, pMax = -1;
                    float tMin = float.MaxValue, tMax = float.MinValue;
                    foreach (var kv in copyOf)
                    {
                        float t = vs[kv.Key].Rx * ax + vs[kv.Key].Ry * ay;
                        if (t < tMin) { tMin = t; pMin = kv.Key; }
                        if (t > tMax) { tMax = t; pMax = kv.Key; }
                    }
                    foreach (var end in new[] { new KeyValuePair<int, float>(pMin, lo - tMin), new KeyValuePair<int, float>(pMax, hi - tMax) })
                    {
                        int pv = end.Key;
                        float delta = end.Value;
                        if (pv < 0 || (pv == pMin ? delta >= 0 : delta <= 0)) continue;
                        int e = Add(vs, Shifted(sk, refPose, vs[pv], ax * delta, ay * delta));
                        int ce = Add(vs, Shifted(sk, refPose, vs[copyOf[pv]], ax * delta, ay * delta));
                        extraFixed.Add(e);
                        extFrom[e] = pv;
                        parent[ce] = e;
                        polys.Add(new[] { e, pv, copyOf[pv], ce });
                    }
                }
            }

            // ずらし: まぶたの絵（細い弧）の厚みを、動かない頂点ごとに画素から測る。測れたら、まぶたの絵は描かず、
            // 窓を「動かない頂点 〜 写し＋厚み」にして、弧のあった所ごと頭の絵で置き換える。
            // まぶたの絵は頭の絵より少し明るい色で、まつげと一緒に下がると、まつげの上に明るい弧として見える。
            // 目を開けているときの弧は、白目の上にはみ出した黒目の縁を隠している。窓の厚みの分がその役を引き継ぐ
            Dictionary<int, float> thick = null;
            if (slide && copyOf != null && copyOf.Count > 0)
            {
                int pw, ph;
                var pagePx = PagePixels((AtlasRegion)original.Sequence.GetRegion(0), out pw, out ph, log);
                if (pagePx != null) thick = ArcThickness(vs, n, copyOf, pagePx, pw, ph, ax, ay, dx / dl, dy / dl);
                if (thick != null)
                {
                    // 延ばした端も同じ厚みにする。すぼめると、端でまつげのすぐ上に白目の縁が細くのぞく
                    foreach (var kv in extFrom) thick[kv.Key] = thick[kv.Value];
                    keep.Clear();
                }
            }

            // 窓: 同じ位置・重みの写しを作って UV を頭の絵にし、横幅で切る
            var tris = new List<int>();
            var windowOf = new Dictionary<int, int>();
            int windowTris = 0;
            foreach (var poly in polys)
            {
                var pts = new List<int>();
                foreach (int v in poly)
                {
                    int f;
                    if (!windowOf.TryGetValue(v, out f))
                    {
                        // 動かない頂点は静止位置の頭の絵。それ以外は少しはみ出させて、一番下がった位置の頭の絵。
                        // ずらしの写しは、動いた分だけ余計に進める（静止時は動かない頂点と重なったままで、はみ出しは 0）
                        bool still = (v < n && info.Fixed[v]) || extraFixed.Contains(v);
                        int pv;
                        V c;
                        if (still) c = Shifted(sk, refPose, vs[v], 0, 0);
                        else if (!parent.TryGetValue(v, out pv)) c = Shifted(sk, refPose, vs[v], ox, oy);
                        else if (thick != null) c = Shifted(sk, refPose, vs[v], dx / dl * thick[pv], dy / dl * thick[pv]);
                        else c = Blend(vs[pv], vs[v], 1f + SlideOverlap);
                        float wx, wy;
                        refPose.LocalToWorld(still ? c.Rx : c.Lx, still ? c.Ry : c.Ly, out wx, out wy);
                        float px = wx - hv[0], py = wy - hv[1];
                        float s = (px * fy - py * fx) / det, tt = (ex * py - ey * px) / det;
                        c.U = huv[0] + s * (huv[6] - huv[0]) + tt * (huv[2] - huv[0]);
                        c.Vv = 1f - (huv[1] + s * (huv[7] - huv[1]) + tt * (huv[3] - huv[1]));
                        f = Add(vs, c);
                        windowOf[v] = f;
                    }
                    pts.Add(f);
                }
                pts = Clip(vs, pts, ax, ay, lo, true);
                pts = Clip(vs, pts, ax, ay, hi, false);
                if (shapes.Count == 0)
                {
                    for (int k = 2; k < pts.Count; k++) { tris.Add(pts[0]); tris.Add(pts[k - 1]); tris.Add(pts[k]); windowTris++; }
                    continue;
                }
                foreach (var shape in shapes)
                {
                    var q = pts;
                    for (int k = 0; k < 9 && q.Count >= 3; k += 3) q = Clip(vs, q, shape[k], shape[k + 1], shape[k + 2], true);
                    for (int k = 2; k < q.Count; k++) { tris.Add(q[0]); tris.Add(q[k - 1]); tris.Add(q[k]); windowTris++; }
                }
            }
            if (windowTris == 0) return null;

            // 窓を先に描いて、元のままの三角形（まつげなど）をその上に重ねる
            tris.AddRange(keep);

            // 動かない頂点だけの三角形に描かれていた暗い線（眉のような短い線）を、四角で描き戻す。画素が読めなければ線は出ない
            int lines = 0, dropped = still3.Count / 3;
            if (still3.Count > 0)
            {
                int pw, ph;
                var px = PagePixels((AtlasRegion)original.Sequence.GetRegion(0), out pw, out ph, log);
                if (px != null) lines = DarkLines(sk, refPose, vs, still3, px, pw, ph, tris);
            }

            // 使っていない頂点を詰めて、メッシュにする
            var remap = new int[vs.Count];
            for (int i = 0; i < remap.Length; i++) remap[i] = i < n ? i : -1;   // 元の頂点は順番ごと残す（輪郭の並び）
            int next = n;
            foreach (int v in tris) if (remap[v] < 0) remap[v] = next++;
            var order = new V[next];
            for (int i = 0; i < vs.Count; i++) if (remap[i] >= 0) order[remap[i]] = vs[i];
            var bones = new List<int>();
            var verts = new List<float>();
            var uvs = new float[next * 2];
            for (int i = 0; i < next; i++)
            {
                bones.Add(order[i].Bones.Count);
                bones.AddRange(order[i].Bones);
                verts.AddRange(order[i].Data);
                uvs[i * 2] = order[i].U;
                uvs[i * 2 + 1] = order[i].Vv;
            }
            var mesh = EyelidRig.CloneMesh(original);
            mesh.Sequence.Regions[0] = EyelidRig.PageRegion((AtlasRegion)mesh.Sequence.GetRegion(0));
            mesh.Bones = bones.ToArray();
            mesh.Vertices = verts.ToArray();
            mesh.RegionUVs = uvs;
            var outTris = new int[tris.Count];
            for (int i = 0; i < outTris.Length; i++) outTris[i] = remap[tris[i]];
            mesh.Triangles = outTris;
            mesh.WorldVerticesLength = next * 2;
            mesh.UpdateSequence();
            return new Plan
            {
                Skin = skin, Slot = slot.Index, Key = slot.AttachmentName, Mesh = mesh, Windows = windowTris, Lines = lines, Dropped = dropped,
                WhiteSlot = white != null ? white.Index : -1, WhiteKey = white != null ? white.AttachmentName : null, WhiteMesh = whiteMesh
            };
        }

        private static int Add(List<V> vs, V v)
        {
            vs.Add(v);
            return vs.Count - 1;
        }

        // 動かない頂点 pv と静止時に重なり、動く頂点 donor の重みで動く頂点を足す（UV は pv のまま）
        private static int AddCopy(Skeleton sk, BonePose refPose, List<V> vs, int pv, int donor, Dictionary<int, int> parent)
        {
            var c = Shifted(sk, refPose, vs[donor], vs[pv].Rx - vs[donor].Rx, vs[pv].Ry - vs[donor].Ry);
            c.U = vs[pv].U;
            c.Vv = vs[pv].Vv;
            int i = Add(vs, c);
            parent[i] = pv;
            return i;
        }

        // 同じ重みのまま、位置を基準のボーンの座標で (sx, sy) だけずらした写し（UV は呼び出し側で入れる）
        private static V Shifted(Skeleton sk, BonePose refPose, V from, float sx, float sy)
        {
            var c = new V { U = from.U, Vv = from.Vv };
            c.Rx = from.Rx + sx; c.Ry = from.Ry + sy;
            c.Lx = from.Lx + sx; c.Ly = from.Ly + sy;
            float ax, ay, bx, by;
            refPose.LocalToWorld(from.Rx, from.Ry, out ax, out ay);
            refPose.LocalToWorld(c.Rx, c.Ry, out bx, out by);
            float wx = bx - ax, wy = by - ay;
            for (int j = 0; j < from.Bones.Count; j++)
            {
                var bp = sk.Bones.Items[from.Bones[j]].AppliedPose;
                float det = bp.A * bp.D - bp.B * bp.C;
                c.Bones.Add(from.Bones[j]);
                c.Data.Add(from.Data[j * 3] + (bp.D * wx - bp.B * wy) / det);
                c.Data.Add(from.Data[j * 3 + 1] + (-bp.C * wx + bp.A * wy) / det);
                c.Data.Add(from.Data[j * 3 + 2]);
            }
            return c;
        }

        // 多角形を、軸 (ax, ay) で測った位置が limit 以上（keepAbove）または以下の側だけ残す。位置は一番下がったコマで測る。
        // 頂点は動く向きにしか動かないので、どのコマでも同じ切り方になる。切り口の頂点は両端の頂点の重みを混ぜて作る
        private static List<int> Clip(List<V> vs, List<int> poly, float ax, float ay, float limit, bool keepAbove)
        {
            if (limit == float.MinValue || limit == float.MaxValue) return poly;
            var res = new List<int>();
            for (int i = 0; i < poly.Count; i++)
            {
                int a = poly[i], b = poly[(i + 1) % poly.Count];
                float ta = vs[a].Lx * ax + vs[a].Ly * ay - limit, tb = vs[b].Lx * ax + vs[b].Ly * ay - limit;
                if (!keepAbove) { ta = -ta; tb = -tb; }
                if (ta >= 0) res.Add(a);
                if ((ta >= 0) != (tb >= 0)) res.Add(Add(vs, Blend(vs[a], vs[b], ta / (ta - tb))));
            }
            return res;
        }

        private static V Blend(V a, V b, float t)
        {
            var c = new V();
            c.Rx = a.Rx + (b.Rx - a.Rx) * t; c.Ry = a.Ry + (b.Ry - a.Ry) * t;
            c.Lx = a.Lx + (b.Lx - a.Lx) * t; c.Ly = a.Ly + (b.Ly - a.Ly) * t;
            c.U = a.U + (b.U - a.U) * t; c.Vv = a.Vv + (b.Vv - a.Vv) * t;
            // 同じボーンの分は 1 つにまとめる（重みの和と、重みを掛けた位置の和）。負の重み（外へ延ばすとき）が混ざるボーンはまとめない
            for (int pass = 0; pass < 2; pass++)
            {
                var src = pass == 0 ? a : b;
                float k = pass == 0 ? 1 - t : t;
                for (int j = 0; j < src.Bones.Count; j++)
                {
                    float wgt = src.Data[j * 3 + 2] * k;
                    int at = -1;
                    for (int i = 0; i < c.Bones.Count; i++)
                        if (c.Bones[i] == src.Bones[j] && c.Data[i * 3 + 2] > 0 && wgt > 0) { at = i; break; }
                    if (at < 0)
                    {
                        c.Bones.Add(src.Bones[j]);
                        c.Data.Add(src.Data[j * 3]); c.Data.Add(src.Data[j * 3 + 1]); c.Data.Add(wgt);
                        continue;
                    }
                    float w0 = c.Data[at * 3 + 2], sum = w0 + wgt;
                    c.Data[at * 3] = (c.Data[at * 3] * w0 + src.Data[j * 3] * wgt) / sum;
                    c.Data[at * 3 + 1] = (c.Data[at * 3 + 1] * w0 + src.Data[j * 3 + 1] * wgt) / sum;
                    c.Data[at * 3 + 2] = sum;
                }
            }
            return c;
        }

        // 四角い絵の不透明な画素の位置（基準のボーンの座標 x, y の並び）。wv は四角の世界座標（左下・左上・右上・右下）。
        // texel は 1 画素ぶんの長さ。画素が読めない・ページが違うときは null
        private static List<float> OpaquePoints(RegionAttachment att, float[] wv, BonePose refPose, AtlasRegion meshRegion,
            int minAlpha, out float texel, ManualLogSource log)
        {
            texel = 0;
            var region = att.Sequence.GetRegion(0) as AtlasRegion;
            if (region == null || region.page != meshRegion.page) return null;
            int w, h;
            var px = PagePixels(region, out w, out h, log);
            if (px == null) return null;
            var uv = att.Sequence.GetUVs(0);
            // 画素の位置 (x, y) → 四角の中の割合 (s, t): 左下から右下へ s、左下から左上へ t
            float x0 = uv[0] * w, y0 = uv[1] * h, ex = uv[6] * w - x0, ey = uv[7] * h - y0, fx = uv[2] * w - x0, fy = uv[3] * h - y0;
            float det = ex * fy - ey * fx;
            if (Math.Abs(det) < 1e-9f) return null;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i += 2)
            {
                minX = Math.Min(minX, uv[i] * w); maxX = Math.Max(maxX, uv[i] * w);
                minY = Math.Min(minY, uv[i + 1] * h); maxY = Math.Max(maxY, uv[i + 1] * h);
            }
            var res = new List<float>();
            for (int y = Math.Max(0, (int)Math.Floor(minY)); y < Math.Min(h, (int)Math.Ceiling(maxY)); y++)
            {
                for (int x = Math.Max(0, (int)Math.Floor(minX)); x < Math.Min(w, (int)Math.Ceiling(maxX)); x++)
                {
                    if (px[y * w + x].a < minAlpha) continue;
                    float cx = x + 0.5f - x0, cy = y + 0.5f - y0;
                    float sv = (cx * fy - cy * fx) / det, tv = (ex * cy - ey * cx) / det;
                    float wx = wv[0] + sv * (wv[6] - wv[0]) + tv * (wv[2] - wv[0]);
                    float wy = wv[1] + sv * (wv[7] - wv[1]) + tv * (wv[3] - wv[1]);
                    float lx, ly;
                    refPose.WorldToLocal(wx, wy, out lx, out ly);
                    res.Add(lx);
                    res.Add(ly);
                }
            }
            float ux, uy, vx, vy;
            refPose.WorldToLocal(wv[0], wv[1], out ux, out uy);
            refPose.WorldToLocal(wv[6], wv[7], out vx, out vy);
            texel = (float)Math.Sqrt((vx - ux) * (vx - ux) + (vy - uy) * (vy - uy)) / Math.Max(1f, (float)Math.Sqrt(ex * ex + ey * ey));
            return res.Count == 0 ? null : res;
        }

        // 白目の絵の画素（縁の半透明も含む・基準のボーンの座標 x, y の並び）を、軸 (ax, ay) の上で離れている塊ごとに分けて返す
        // （左右の目が 1 枚に入っている）。塊ごとに「全部の画素」「目を開けているときに見えている画素」の 2 つを順に入れる。
        // 見えている = 白目より後に描く目の部品のメッシュ（まつげ・まぶたの絵）の不透明な画素に隠れていない。
        // サラマンダーは前髪が目の部品より下にあり、白目の絵の端は髪の線の上まで描かれている（まつげの端がそこを隠している）。
        // 隠れている所は白目から切り落とす。まぶたが下がったとき、そこには下の髪の線がそのまま見える。
        // 2026-11-09: 白目を全部覆っていたら、左目の左端で髪の黒い線に肌色がかかった（ユーザー指摘）
        private static readonly Dictionary<string, List<List<float>>> WhiteCache = new Dictionary<string, List<List<float>>>();
        private static readonly Dictionary<string, float> TexelCache = new Dictionary<string, float>();

        private static List<List<float>> VisibleWhite(SkeletonData sd, Skin skin, Skeleton sk, BonePose refPose, SlotData white,
            RegionAttachment whiteAtt, float[] wv, float ax, float ay, AtlasRegion meshRegion, out float texel, ManualLogSource log)
        {
            string key = sd.Hash + "/" + skin.Name + "/" + whiteAtt.Name;
            List<List<float>> res;
            if (WhiteCache.TryGetValue(key, out res)) { texel = TexelCache[key]; return res; }
            var all = OpaquePoints(whiteAtt, wv, refPose, meshRegion, 8, out texel, log);
            if (all == null) return null;
            int w, h;
            var px = PagePixels(meshRegion, out w, out h, log);

            // 白目を隠す側のメッシュ（白目より後の、同じ向きの目のスロット）を、静止時の位置で集める
            string prefix = white.Name.Substring(0, white.Name.LastIndexOf('/') + 1);
            var covers = new List<float[]>();   // 三角形ごとに x0,y0,u0,v0, x1,y1,u1,v1, x2,y2,u2,v2（u, v は画素の位置）
            for (int si = white.Index + 1; si < sd.Slots.Count; si++)
            {
                var sdata = sd.Slots.Items[si];
                if (!sdata.Name.StartsWith(prefix + "eye", StringComparison.Ordinal) || string.IsNullOrEmpty(sdata.AttachmentName)) continue;
                var m = skin.GetAttachment(si, sdata.AttachmentName) as MeshAttachment;
                if (m == null) continue;
                var mr = m.Sequence.GetRegion(0) as AtlasRegion;
                if (mr == null || mr.page != meshRegion.page) continue;
                var wvs = new float[m.WorldVerticesLength];
                m.ComputeWorldVertices(sk, sk.Slots.Items[si], 0, wvs.Length, wvs, 0, 2);
                var uv = m.Sequence.GetUVs(0);
                var tri = m.Triangles;
                for (int i = 0; i < tri.Length; i += 3)
                {
                    var c = new float[12];
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tri[i + k];
                        refPose.WorldToLocal(wvs[v * 2], wvs[v * 2 + 1], out c[k * 4], out c[k * 4 + 1]);
                        c[k * 4 + 2] = uv[v * 2] * w;
                        c[k * 4 + 3] = uv[v * 2 + 1] * h;
                    }
                    covers.Add(c);
                }
            }

            var pts = new List<float[]>();   // t, x, y, 見えているか
            for (int i = 0; i < all.Count; i += 2)
            {
                float x = all[i], y = all[i + 1];
                bool hidden = false;
                foreach (var c in covers)
                {
                    float den = (c[5] - c[9]) * (c[0] - c[8]) + (c[8] - c[4]) * (c[1] - c[9]);
                    if (Math.Abs(den) < 1e-12f) continue;
                    float w0 = ((c[5] - c[9]) * (x - c[8]) + (c[8] - c[4]) * (y - c[9])) / den;
                    float w1 = ((c[9] - c[1]) * (x - c[8]) + (c[0] - c[8]) * (y - c[9])) / den;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    int ix = (int)(c[2] * w0 + c[6] * w1 + c[10] * w2), iy = (int)(c[3] * w0 + c[7] * w1 + c[11] * w2);
                    if (ix >= 0 && iy >= 0 && ix < w && iy < h && px[iy * w + ix].a >= 230) { hidden = true; break; }
                }
                pts.Add(new[] { x * ax + y * ay, x, y, hidden ? 0f : 1f });
            }
            pts.Sort((p, q) => p[0].CompareTo(q[0]));
            // 1 画素ぶんの 3 倍より空いた所で分ける
            res = new List<List<float>>();
            List<float> cur = new List<float>(), curVis = new List<float>();
            float prev = pts[0][0];
            foreach (var pt in pts)
            {
                if (pt[0] - prev > texel * 3f)
                {
                    res.Add(cur); res.Add(curVis);
                    cur = new List<float>(); curVis = new List<float>();
                }
                cur.Add(pt[1]); cur.Add(pt[2]);
                if (pt[3] > 0) { curVis.Add(pt[1]); curVis.Add(pt[2]); }
                prev = pt[0];
            }
            res.Add(cur); res.Add(curVis);
            WhiteCache[key] = res;
            TexelCache[key] = texel;
            return res;
        }

        // 白目の絵の差し替え。四角（基準のボーンの座標 x, y を 4 点ずつ）の集まりを、白目のスロットのメッシュにする。
        // 絵の外へはみ出す点は、絵の端に寄せる（隣の絵の画素を拾わないように）
        private static MeshAttachment TrimmedWhite(Skeleton sk, BonePose refPose, SlotData white, RegionAttachment whiteAtt,
            float[] wv, List<float> quads)
        {
            var region = whiteAtt.Sequence.GetRegion(0) as AtlasRegion;
            if (region == null) return null;
            var uv = whiteAtt.Sequence.GetUVs(0);
            var bone = sk.Slots.Items[white.Index].Bone.AppliedPose;
            // 四角の世界座標は 左下・左上・右上・右下。P = 左下 + s (右下 - 左下) + t (左上 - 左下)
            float ex = wv[6] - wv[0], ey = wv[7] - wv[1], fx = wv[2] - wv[0], fy = wv[3] - wv[1];
            float det = ex * fy - ey * fx;
            if (Math.Abs(det) < 1e-12f) return null;
            int count = quads.Count / 2;
            var verts = new float[count * 2];
            var uvs = new float[count * 2];
            var tris = new List<int>();
            for (int i = 0; i < count; i++)
            {
                float wx, wy;
                refPose.LocalToWorld(quads[i * 2], quads[i * 2 + 1], out wx, out wy);
                float px = wx - wv[0], py = wy - wv[1];
                float sc = Math.Min(1f, Math.Max(0f, (px * fy - py * fx) / det)), tc = Math.Min(1f, Math.Max(0f, (ex * py - ey * px) / det));
                bone.WorldToLocal(wv[0] + sc * ex + tc * fx, wv[1] + sc * ey + tc * fy, out verts[i * 2], out verts[i * 2 + 1]);
                uvs[i * 2] = uv[0] + sc * (uv[6] - uv[0]) + tc * (uv[2] - uv[0]);
                uvs[i * 2 + 1] = 1f - (uv[1] + sc * (uv[7] - uv[1]) + tc * (uv[3] - uv[1]));
                if (i % 4 == 3) tris.AddRange(new[] { i - 3, i - 2, i - 1, i - 3, i - 1, i });
            }
            var seq = new Sequence(whiteAtt.Sequence);
            seq.Regions[0] = EyelidRig.PageRegion(region);
            var mesh = new MeshAttachment(whiteAtt.Name, seq);
            mesh.Vertices = verts;
            mesh.WorldVerticesLength = verts.Length;
            mesh.RegionUVs = uvs;
            mesh.Triangles = tris.ToArray();
            mesh.HullLength = 0;
            mesh.SetColor(whiteAtt.GetColor());
            mesh.UpdateSequence();
            return mesh;
        }

        // 目の絵（白目と黒目の不透明な画素・点の並び x, y）を覆う切り取りの形を、縦の帯ごとの半平面
        // （nx, ny, limit: 内側は p・n >= limit）の並びで返す。帯は軸 (ax, ay) の向きに等分し、
        // 帯ごとに「その帯の点の上の縁に沿う直線より下」を残す。下は切らない（まつげの下がる向き (mx, my) へ開いたまま）。
        // 窓は、まぶたが下がり切ったコマの形で切る。途中のコマでは窓が縦に縮むので、深い所の幅が浅い所に来る。
        // 「上の縁より下」は縮んでも目の絵を覆えるが、下のすぼまりは覆えなくなる。だから下は切らない。
        // 2026-11-09: 凸包で切ったら、白目の端と黒目の上を結ぶ線の内側（目の絵が無い所）まで覆い、
        //             両目とも左上で前髪に肌色が少しかかった。帯ごとの上の縁に変えた
        private const int StripCount = 6;
        // 白目の差し替えの余白（画素ぶん）。窓はこれより 0.1 画素ぶん広い。
        // 横は 0.3: 白目の横の端は髪の線に接していて、広げると髪の線の縁に肌色がかかる（1.0 → 0.6 → 0.5 → 0.3 と見比べて決めた）。
        // 目を開けているときの白目の縁は、ぼかしの外側がわずかに削れるだけで見分けがつかない
        internal static float TopMargin = 0.75f, SideMargin = 0.3f;
        private static List<float[]> Strips(List<float> pts, float ax, float ay, float mx, float my, float margin, float side)
        {
            int count = pts.Count / 2;
            var t = new float[count];
            var d = new float[count];
            float tMin = float.MaxValue, tMax = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                t[i] = pts[i * 2] * ax + pts[i * 2 + 1] * ay;
                d[i] = pts[i * 2] * mx + pts[i * 2 + 1] * my;
                tMin = Math.Min(tMin, t[i]); tMax = Math.Max(tMax, t[i]);
            }
            var res = new List<float[]>();
            float width = (tMax - tMin) / StripCount;
            if (width <= 0) return res;
            // 帯の境目での上の縁（境目の近くの点のうち一番上）。近くに点が無ければ NaN
            var edge = new float[StripCount + 1];
            for (int k = 0; k <= StripCount; k++)
            {
                float tk = tMin + k * width, best = float.NaN;
                for (int i = 0; i < count; i++)
                    if (Math.Abs(t[i] - tk) <= width * 0.25f && !(best <= d[i])) best = d[i];
                edge[k] = best;
            }
            for (int k = 0; k < StripCount; k++)
            {
                float t0 = tMin + k * width, t1 = t0 + width, top = float.MaxValue;
                bool any = false;
                for (int i = 0; i < count; i++)
                    if (t[i] >= t0 && t[i] <= t1) { any = true; top = Math.Min(top, d[i]); }
                if (!any) continue;
                float b0 = float.IsNaN(edge[k]) ? top : edge[k], b1 = float.IsNaN(edge[k + 1]) ? top : edge[k + 1];
                float slope = (b1 - b0) / width;
                // 帯の中の点が全部入るまで、直線を上へずらす
                float shift = 0;
                for (int i = 0; i < count; i++)
                    if (t[i] >= t0 && t[i] <= t1) shift = Math.Min(shift, d[i] - (b0 + slope * (t[i] - t0)));
                b0 += shift;
                float len = (float)Math.Sqrt(1 + slope * slope);
                float left = k == 0 ? t0 - side : t0, right = k == StripCount - 1 ? t1 + side : t1;
                // 0〜8: 半平面 3 つ（左・右・上）。9, 10: 左右の端の位置。11, 12: 左右の端での上の縁の位置（下がる向きの座標）
                res.Add(new[]
                {
                    ax, ay, left,
                    -ax, -ay, -right,
                    (-slope * ax + mx) / len, (-slope * ay + my) / len, (b0 - slope * t0) / len - margin,
                    left, right,
                    b0 + slope * (left - t0) - margin * len, b0 + slope * (right - t0) - margin * len
                });
            }
            return res;
        }

        // まぶたの絵（細い弧）の、動かない頂点ごとの厚み（まつげの下がる向き (mx, my) に測った不透明な画素の続く長さ）。
        // キーは動かない頂点。弧の下の縁はぎざぎざ（1 画素幅のとげ）なので、左右に半画素ずらした 3 本の真ん中の値を使う。
        // 画素の位置は、メッシュの頂点（静止時の位置とページ上の位置）から一次の式で求める
        private static Dictionary<int, float> ArcThickness(List<V> vs, int n, Dictionary<int, int> copyOf,
            UnityEngine.Color32[] px, int w, int h, float ax, float ay, float mx, float my)
        {
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, s1 = 0, xx = 0, xy = 0, x1 = 0, yx = 0, yy = 0, y1 = 0;
            for (int i = 0; i < n; i++)
            {
                double rx = vs[i].Rx, ry = vs[i].Ry, pxl = vs[i].U * w, pyl = (1 - vs[i].Vv) * h;
                sxx += rx * rx; sxy += rx * ry; syy += ry * ry; sx += rx; sy += ry; s1 += 1;
                xx += pxl * rx; xy += pxl * ry; x1 += pxl;
                yx += pyl * rx; yy += pyl * ry; y1 += pyl;
            }
            double det = sxx * (syy * s1 - sy * sy) - sxy * (sxy * s1 - sy * sx) + sx * (sxy * sy - syy * sx);
            if (Math.Abs(det) < 1e-9) return null;
            var fx = Solve3(sxx, sxy, sx, sxy, syy, sy, sx, sy, s1, xx, xy, x1, det);
            var fy = Solve3(sxx, sxy, sx, sxy, syy, sy, sx, sy, s1, yx, yy, y1, det);
            double scale = Math.Abs(fx[0] * fy[1] - fx[1] * fy[0]);
            if (scale < 1e-12) return null;
            float texel = (float)(1.0 / Math.Sqrt(scale));   // 1 画素ぶんの長さ（静止時の座標で）

            var res = new Dictionary<int, float>();
            var runs = new float[3];
            foreach (var kv in copyOf)
            {
                var pv = vs[kv.Key];
                for (int side = 0; side < 3; side++)
                {
                    float ox = pv.Rx + ax * (side - 1) * texel * 0.6f, oy = pv.Ry + ay * (side - 1) * texel * 0.6f;
                    bool entered = false;
                    float run = 0;
                    for (float step = 0; step <= texel * 14f; step += texel * 0.25f)
                    {
                        float rx = ox + mx * step, ry = oy + my * step;
                        int ix = (int)Math.Floor(fx[0] * rx + fx[1] * ry + fx[2]), iy = (int)Math.Floor(fy[0] * rx + fy[1] * ry + fy[2]);
                        bool solid = ix >= 0 && iy >= 0 && ix < w && iy < h && px[iy * w + ix].a >= 128;
                        if (solid) { entered = true; run = step + texel * 0.25f; }
                        else if (entered) break;
                    }
                    runs[side] = run;
                }
                Array.Sort(runs);
                res[kv.Key] = runs[1];
            }
            return res;
        }

        // ページの画素（0 行目が UV の v = 0 の側）。読めなければ null
        private static UnityEngine.Color32[] PagePixels(AtlasRegion region, out int w, out int h, ManualLogSource log)
        {
            w = h = 0;
            try
            {
                if (DevPagePixels != null)
                {
                    w = region.page.width; h = region.page.height;
                    return DevPagePixels(region.page);
                }
                var mat = region.page.rendererObject as UnityEngine.Material;
                if (mat == null || mat.mainTexture == null) return null;
                w = mat.mainTexture.width; h = mat.mainTexture.height;
                return LashTexture.ReadPage(mat.mainTexture);
            }
            catch (Exception e)
            {
                log.LogWarning("[tex] page pixels: " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        // 動かない三角形（still3）の中の暗い画素を、つながった塊ごとに四角で囲んで tris に足す。足した四角の数を返す。
        // 画素の位置から頂点の位置への対応は、動かない三角形の頂点全部から一次の式で求める（静止時のメッシュは絵をそのまま置いた形）
        // 2026-11-09: 三角形ごとに四角を作っていたら、細長い三角形で対応がゆがみ、眉の線の横に細い黒い線が出た
        private static int DarkLines(Skeleton sk, BonePose refPose, List<V> vs, List<int> still3,
            UnityEngine.Color32[] px, int w, int h, List<int> tris)
        {
            // 暗い画素を集める
            var dark = new HashSet<long>();
            for (int i = 0; i < still3.Count; i += 3)
            {
                V a = vs[still3[i]], b = vs[still3[i + 1]], d = vs[still3[i + 2]];
                float x0 = a.U * w, y0 = (1 - a.Vv) * h, x1 = b.U * w, y1 = (1 - b.Vv) * h, x2 = d.U * w, y2 = (1 - d.Vv) * h;
                float den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
                if (Math.Abs(den) < 1e-9f) continue;
                int minX = (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))), maxX = (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2)));
                int minY = (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))), maxY = (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2)));
                for (int y = Math.Max(0, minY); y <= Math.Min(h - 1, maxY); y++)
                {
                    for (int x = Math.Max(0, minX); x <= Math.Min(w - 1, maxX); x++)
                    {
                        float cx = x + 0.5f, cy = y + 0.5f;
                        float w0 = ((y1 - y2) * (cx - x2) + (x2 - x1) * (cy - y2)) / den;
                        float w1 = ((y2 - y0) * (cx - x2) + (x0 - x2) * (cy - y2)) / den;
                        if (w0 < 0 || w1 < 0 || 1 - w0 - w1 < 0) continue;
                        var c = px[y * w + x];
                        // 画素は PMA。不透明に近く、色を戻して暗いもの
                        if (c.a < 128 || Math.Max(c.r, Math.Max(c.g, c.b)) > c.a * 0.45f) continue;
                        dark.Add(((long)y << 32) | (uint)x);
                    }
                }
            }
            if (dark.Count == 0) return 0;

            // 画素の位置 → 静止時の位置（基準のボーンの座標）の一次の式を、動かない三角形の頂点から最小二乗で求める
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, s1 = 0, rxx = 0, rxy = 0, rx1 = 0, ryx = 0, ryy = 0, ry1 = 0;
            var used = new HashSet<int>();
            foreach (int v in still3)
            {
                if (!used.Add(v)) continue;
                double x = vs[v].U * w, y = (1 - vs[v].Vv) * h;
                sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y; s1 += 1;
                rxx += vs[v].Rx * x; rxy += vs[v].Rx * y; rx1 += vs[v].Rx;
                ryx += vs[v].Ry * x; ryy += vs[v].Ry * y; ry1 += vs[v].Ry;
            }
            double det = sxx * (syy * s1 - sy * sy) - sxy * (sxy * s1 - sy * sx) + sx * (sxy * sy - syy * sx);
            if (Math.Abs(det) < 1e-9) return 0;
            var fx = Solve3(sxx, sxy, sx, sxy, syy, sy, sx, sy, s1, rxx, rxy, rx1, det);
            var fy = Solve3(sxx, sxy, sx, sxy, syy, sy, sx, sy, s1, ryx, ryy, ry1, det);
            var anchor = vs[still3[0]];

            // つながった塊（斜めも含む）ごとに四角で囲む。1〜2 画素の点は捨てる
            int count = 0;
            var todo = new Stack<long>();
            while (dark.Count > 0)
            {
                long first = 0;
                foreach (long k in dark) { first = k; break; }
                dark.Remove(first);
                todo.Push(first);
                int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = int.MinValue, by1 = int.MinValue, size = 0;
                while (todo.Count > 0)
                {
                    long k = todo.Pop();
                    int x = (int)(k & 0xffffffff), y = (int)(k >> 32);
                    size++;
                    bx0 = Math.Min(bx0, x); bx1 = Math.Max(bx1, x); by0 = Math.Min(by0, y); by1 = Math.Max(by1, y);
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            long nk = ((long)(y + oy) << 32) | (uint)(x + ox);
                            if (dark.Remove(nk)) todo.Push(nk);
                        }
                    }
                }
                if (size < 3) continue;
                // 四角の角（線の縁のぼかしの分、半画素ひろげる）
                var cornersX = new[] { bx0 - 0.5f, bx1 + 1.5f, bx1 + 1.5f, bx0 - 0.5f };
                var cornersY = new[] { by0 - 0.5f, by0 - 0.5f, by1 + 1.5f, by1 + 1.5f };
                var q = new int[4];
                for (int i = 0; i < 4; i++)
                {
                    float rx = (float)(fx[0] * cornersX[i] + fx[1] * cornersY[i] + fx[2]);
                    float ry = (float)(fy[0] * cornersX[i] + fy[1] * cornersY[i] + fy[2]);
                    var c = Shifted(sk, refPose, anchor, rx - anchor.Rx, ry - anchor.Ry);
                    c.U = cornersX[i] / w;
                    c.Vv = 1f - cornersY[i] / h;
                    q[i] = Add(vs, c);
                }
                tris.Add(q[0]); tris.Add(q[1]); tris.Add(q[2]);
                tris.Add(q[0]); tris.Add(q[2]); tris.Add(q[3]);
                count++;
            }
            return count;
        }

        // 3 元の連立一次方程式（クラメルの公式）。det は係数の行列式
        private static double[] Solve3(double a11, double a12, double a13, double a21, double a22, double a23,
            double a31, double a32, double a33, double b1, double b2, double b3, double det)
        {
            double x = (b1 * (a22 * a33 - a23 * a32) - a12 * (b2 * a33 - a23 * b3) + a13 * (b2 * a32 - a22 * b3)) / det;
            double y = (a11 * (b2 * a33 - a23 * b3) - b1 * (a21 * a33 - a23 * a31) + a13 * (a21 * b3 - b2 * a31)) / det;
            double z = (a11 * (a22 * b3 - b2 * a32) - a12 * (a21 * b3 - b2 * a31) + b1 * (a21 * a32 - a22 * a31)) / det;
            return new[] { x, y, z };
        }

        // 頂点の位置（静止・一番下がったコマ）と、動かない・動くの分類を調べる
        private static Info Analyze(SkeletonData sd, Skeleton sk, int slotIndex, MeshAttachment m)
        {
            sk.SetupPose();
            sk.UpdateWorldTransform(Physics.Pose);
            var rest = EyelidRig.LocalVerts(sk, slotIndex, m);
            int n = rest.Length / 2;
            float[] low = null;
            float best = 0;
            foreach (var an in sd.Animations)
            {
                if (an.Name.IndexOf("spawn", StringComparison.Ordinal) >= 0) continue;
                int steps = (int)Math.Floor(an.Duration * 30f + 1e-4f);
                for (int si = 0; si <= steps; si++)
                {
                    sk.SetupPose();
                    an.Apply(sk, 0, si / 30f, true, null, 1, MixFrom.Setup, false, false, false);
                    sk.UpdateWorldTransform(Physics.Pose);
                    var w = EyelidRig.LocalVerts(sk, slotIndex, m);
                    float move = 0;
                    for (int i = 0; i < w.Length; i++) move += Math.Abs(w[i] - rest[i]);
                    if (move > best) { best = move; low = w; }
                }
            }
            if (low == null) return null;
            var d = new float[n];
            float maxD = 0;
            for (int i = 0; i < n; i++)
            {
                float dx = low[i * 2] - rest[i * 2], dy = low[i * 2 + 1] - rest[i * 2 + 1];
                d[i] = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d[i] > maxD) maxD = d[i];
            }
            if (maxD <= 0) return null;
            var info = new Info { Rest = rest, Low = low, Fixed = new bool[n], Moving = new bool[n], Travel = maxD };
            for (int i = 0; i < n; i++)
            {
                info.Fixed[i] = d[i] < maxD * 0.15f;
                info.Moving[i] = d[i] > maxD * 0.6f;
            }
            return info;
        }
    }

    // まつげのメッシュに自前のテクスチャを持たせる。元のページからまつげの領域を切り出し、まぶたの色（肌色・髪の影のピンク）を
    // 透明にして黒い線だけ残した小さなテクスチャを作り、領域を新しいページ（自前のマテリアル）に付け替える。
    // 本体の改造版 spine-unity は、ReadSkeletonData の直後に領域のページのマテリアルを一覧に入れるので、付け替えたものもそのまま描かれる
    // （2026-11-09 にゲーム内で確認）
    internal static class LashTexture
    {
        // まぶたの絵の色（事務所版・マップ版で共通）。黒い線は (0,0,0)
        private static readonly float[] Skin = { 255f, 225f, 210f };
        private static readonly float[] Shadow = { 221f, 146f, 145f };

        private static readonly Dictionary<UnityEngine.Texture, UnityEngine.Color32[]> PageCache = new Dictionary<UnityEngine.Texture, UnityEngine.Color32[]>();
        private static readonly Dictionary<string, UnityEngine.Material> MaterialCache = new Dictionary<string, UnityEngine.Material>();

        public static void Apply(MeshAttachment m, ManualLogSource log)
        {
            var regions = m.Sequence.Regions;
            for (int k = 0; k < regions.Length; k++)
            {
                var ar = regions[k] as AtlasRegion;
                if (ar == null || ar.page == null) continue;
                var orig = ar.page.rendererObject as UnityEngine.Material;
                if (orig == null) throw new InvalidOperationException("page has no material");
                var src = orig.mainTexture;
                int W = src.width, H = src.height;
                // 領域の UV は読み込み時に上下が反転済み（Unity の向き・下が 0）。
                // 2026-11-09: 反転前の向き（上が 0）だと思って切り出したら、透明な所を切り出してまつげが丸ごと消えた。
                // 事務所版は atlas の宣言（4096）が実物（2048）の倍で端が半画素に来るので、余白を付けて切り出し、位置は小数のまま合わせる
                double fu0 = Math.Min(ar.u, ar.u2) * W, fu1 = Math.Max(ar.u, ar.u2) * W;
                double fb = Math.Min(ar.v, ar.v2) * H, ft = Math.Max(ar.v, ar.v2) * H;
                int cx0 = Math.Max(0, (int)Math.Floor(fu0) - 1), cx1 = Math.Min(W, (int)Math.Ceiling(fu1) + 1);
                int cy0 = Math.Max(0, (int)Math.Floor(fb) - 1), cy1 = Math.Min(H, (int)Math.Ceiling(ft) + 1);
                int cw = cx1 - cx0, ch = cy1 - cy0;
                string key = src.GetInstanceID() + ":" + cx0 + "," + cy0 + "," + cw + "x" + ch;
                UnityEngine.Material mat;
                if (!MaterialCache.TryGetValue(key, out mat))
                {
                    var page = ReadPage(src);
                    var px = new UnityEngine.Color32[cw * ch];
                    for (int r = 0; r < ch; r++) Array.Copy(page, (cy0 + r) * W + cx0, px, r * cw, cw);
                    // 写しは MaterialCache が持つので、ページの画素は手放す（事務所版は 2048x2048 で 16MB）
                    PageCache.Remove(src);
                    int kept = KeepLines(px);
                    var tex = new UnityEngine.Texture2D(cw, ch, UnityEngine.TextureFormat.RGBA32, false, false);
                    tex.SetPixels32(px);
                    tex.Apply(false, false);
                    tex.filterMode = src.filterMode;
                    tex.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                    tex.name = src.name + " eyelash (LwfEyeDrops)";
                    mat = new UnityEngine.Material(orig);
                    mat.name = orig.name + " (LwfEyeDrops)";
                    mat.mainTexture = tex;
                    MaterialCache[key] = mat;
                    log.LogInfo("[tex] " + src.name + " eyelash " + cw + "x" + ch + " line pixels=" + kept);
                }
                var nr = ar.Clone();
                nr.page = ar.page.Clone();
                nr.page.rendererObject = mat;
                nr.page.width = cw;
                nr.page.height = ch;
                nr.u = (float)((ar.u * W - cx0) / cw);
                nr.u2 = (float)((ar.u2 * W - cx0) / cw);
                nr.v = (float)((ar.v * H - cy0) / ch);
                nr.v2 = (float)((ar.v2 * H - cy0) / ch);
                regions[k] = nr;
            }
            m.UpdateSequence();
        }

        // まぶたの色を透明にして、黒い線だけ残す（画素は PMA）。残した画素の数を返す。
        // 画素の色を「黒・肌色・ピンク」の混ざりとして解き、黒の割合をそのまま不透明度にする（線の縁のぼかしを保つ）
        internal static int KeepLines(UnityEngine.Color32[] px)
        {
            double ss = Dot(Skin, Skin), sk = Dot(Skin, Shadow), kk = Dot(Shadow, Shadow), det = ss * kk - sk * sk;
            int kept = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a == 0) continue;
                double a = c.a / 255.0;
                double r = c.r / a, g = c.g / a, b = c.b / a;   // PMA を戻す
                double sc = Skin[0] * r + Skin[1] * g + Skin[2] * b, kc = Shadow[0] * r + Shadow[1] * g + Shadow[2] * b;
                double u = (sc * kk - kc * sk) / det, w = (kc * ss - sc * sk) / det;
                if (u < 0) { u = 0; w = kc / kk; }
                if (w < 0) { w = 0; u = sc / ss; }
                double t = 1 - Math.Max(0, u) - Math.Max(0, w);
                // 絵の色のわずかなばらつきで薄い黒が残らないよう、端を切る
                t = (t - 0.08) / 0.84;
                if (t <= 0) { px[i] = new UnityEngine.Color32(0, 0, 0, 0); continue; }
                if (t > 1) t = 1;
                px[i] = new UnityEngine.Color32(0, 0, 0, (byte)Math.Round(c.a * t));
                kept++;
            }
            return kept;
        }

        private static double Dot(float[] a, float[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        internal static UnityEngine.Color32[] ReadPage(UnityEngine.Texture src)
        {
            UnityEngine.Color32[] px;
            if (PageCache.TryGetValue(src, out px)) return px;
            var rt = UnityEngine.RenderTexture.GetTemporary(src.width, src.height, 0, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
            UnityEngine.Graphics.Blit(src, rt);
            var prev = UnityEngine.RenderTexture.active;
            UnityEngine.RenderTexture.active = rt;
            var tex = new UnityEngine.Texture2D(src.width, src.height, UnityEngine.TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new UnityEngine.Rect(0, 0, src.width, src.height), 0, 0, false);
            UnityEngine.RenderTexture.active = prev;
            UnityEngine.RenderTexture.ReleaseTemporary(rt);
            px = tex.GetPixels32();
            UnityEngine.Object.Destroy(tex);
            PageCache[src] = px;
            return px;
        }
    }
}
