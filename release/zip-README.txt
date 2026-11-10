LWF Eye Drops
Lazy Witch's Factory の見た目の引っかかりを洗い流して、ピントを合わせる MOD
Wash out the visual snags and bring Lazy Witch's Factory into focus

────────────────────────────────
入れかた
────────────────────────────────

1. BepInEx 5 を落として、ゲームのフォルダ
   （LazyWitchsFactory.exe と同じ場所）へ展開する

     https://github.com/BepInEx/BepInEx/releases
     BepInEx_win_x64_5.4.x.zip

2. 一度ゲームを起動して終了すると、BepInEx/plugins などが作られる

3. LwfEyeDrops.dll を BepInEx/plugins/ に入れる

4. ゲームを起動する。BepInEx/LogOutput.log に
   [boot] LWF Eye Drops の行が出れば動いている

MOD管理ソフトを使うなら Thunderstore から

     https://thunderstore.io/c/lazy-witchs-factory/p/KiyonakaNata/LwfEyeDrops/

────────────────────────────────
できること
────────────────────────────────

モモコ → 瞬き・半眼のときの影や縦線を消す
採掘ゴブリン・鍛冶師サラマンダー → 瞬きのときの縦線を消す
コイントス → 絵に、当たる候補の使い魔2体を描く
偽社員証 → 絵に、当たる候補の使い魔3体を描く

キャラクター・契約ごとの切り替えは BepInEx/config/kiyonakanata.lwfeyedrops.cfg
変更はゲームの再起動後に反映

────────────────────────────────
消しかた
────────────────────────────────

BepInEx/plugins/LwfEyeDrops.dll を消す
設定も消すなら BepInEx/config/kiyonakanata.lwfeyedrops.cfg

────────────────────────────────

Lazy Witch's Factory ver 0.31.2 / BepInEx 5.4.23.5 で確認
非公式のMOD、公式のサポート対象外

説明は GitHub

     https://github.com/KiyonakaNata/lwf-eye-drops


────────────────────────────────
English
────────────────────────────────

Install

1. Unpack BepInEx 5 (win x64) into the game folder

     https://github.com/BepInEx/BepInEx/releases

2. Start the game once and quit, so BepInEx/plugins is created

3. Put LwfEyeDrops.dll into BepInEx/plugins/

4. Start the game. BepInEx/LogOutput.log has a [boot] LWF Eye Drops line

With a mod manager, install from Thunderstore

     https://thunderstore.io/c/lazy-witchs-factory/p/KiyonakaNata/LwfEyeDrops/

Features

Momoko → no shadows or vertical streaks above the eyelashes while blinking or with half-closed eyes
Mining Goblin, Blacksmith Salamander → no shadows or vertical streaks above the eyes while blinking
Coin Toss → the art shows the two familiars it can give
Fake Employee ID → the art shows the three familiars it can give

Pick characters and pacts in BepInEx/config/kiyonakanata.lwfeyedrops.cfg
Changes apply after restarting the game

Remove

Delete BepInEx/plugins/LwfEyeDrops.dll
Delete BepInEx/config/kiyonakanata.lwfeyedrops.cfg to drop the settings

Tested with Lazy Witch's Factory ver 0.31.2 / BepInEx 5.4.23.5
Unofficial mod, not supported by the developer

     https://github.com/KiyonakaNata/lwf-eye-drops
