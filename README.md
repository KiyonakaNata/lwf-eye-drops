[English](README.en.md)

# LWF Eye Drops

**Lazy Witch's Factory** 瞬きのときに出る影や縦線を洗い流す MOD

![LWF Eye Drops](img/icon.png)

**[最新版をダウンロード](https://github.com/KiyonakaNata/lwf-eye-drops/releases/latest)** ／ **[Thunderstore](https://thunderstore.io/c/lazy-witchs-factory/p/KiyonakaNata/LwfEyeDrops/)**（MOD管理ソフトでお手軽導入）

---

## できること

### モモコ

![モモコ](img/momoko.png)

- 瞬き・半眼のときの影や縦線を消す

### 採掘ゴブリン・鍛冶師サラマンダー

![採掘ゴブリン・鍛冶師サラマンダー](img/familiars.png)

- 瞬きのときの縦線を消す

---

## 入れかた（手で入れる場合）

1. **BepInEx 5** を入れる — [配布元](https://github.com/BepInEx/BepInEx/releases)
   - `BepInEx_win_x64_5.4.x.zip` を落とす
   - 中身をゲームのフォルダ（`LazyWitchsFactory.exe` と同じ場所）へ展開する

     > **ゲームのフォルダの場所**（Steam）
     > ライブラリでゲームを右クリック → 管理 → ローカルファイルを閲覧

2. 一度ゲームを起動して終了すると、`BepInEx/plugins` などが作られる
3. このMODの zip の中の `LwfEyeDrops.dll` を **`BepInEx/plugins/` に入れる**
4. ゲームを起動する。`BepInEx/LogOutput.log` に `[boot] LWF Eye Drops` の行が出れば動いている

## 消しかた

**このMODだけ消す**

- `BepInEx/plugins/LwfEyeDrops.dll`
- 設定も消すなら `BepInEx/config/kiyonakanata.lwfeyedrops.cfg`

**BepInEx ごと消す**（他のMODも全部止まる）

- `BepInEx` フォルダ
- ゲームのフォルダにある `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`

---

## 設定

`BepInEx/config/kiyonakanata.lwfeyedrops.cfg`（ゲームを一度起動すると作られる）

MOD管理ソフトで入れた場合は、管理ソフトの Config editor から開く

変更はゲームの再起動後に反映

**[1. General]**

| 項目 | 既定 | 値 |
|---|---|---|
| Enabled | `true` | |

**[2. Characters]**

| 項目 | キャラクター | 既定 | 値 |
|---|---|---|---|
| Momoko | モモコ | `true` | `false` で対象から外す |
| Mining Goblin | 採掘ゴブリン | `true` | 同上 |
| Blacksmith Salamander | 鍛冶師サラマンダー | `true` | 同上 |

---

## 動作の条件

| | |
|---|---|
| Lazy Witch's Factory | **ver 0.31.2** で動作確認 |
| BepInEx | **5.4.23.5** で確認（5.4.x なら動くはず） |

本体の更新で動かなくなったら、このMODの寿命なので消すこと

## うまく動かないとき

まず `BepInEx/LogOutput.log` を見る

| ログ | いまどうなっているか |
|---|---|
| `[boot] LWF Eye Drops ...` が無い | **読まれていない** — DLL の置き場所を確認 |
| `[boot]` の行に `disabled` | **止まっている** — ゲームの版が合っていない |
| `Skipped` か `skipped` の行がある | **そのキャラクターには効いていない** — ゲームの版が合っていない |

**不具合の報告**には次の2つを添えること

- 画面の写真
- `BepInEx/LogOutput.log`

---

## 免責

- **非公式のMOD** — 公式のサポート対象外
- MODを入れた状態で起きた不具合・クラッシュ・セーブデータの破損などは自己責任
- [公式のMODに関する方針](https://store.steampowered.com/news/app/3971650/view/699897618302503133)に従って作成

ソースコードは MIT ライセンス
