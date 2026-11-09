# LWF Eye Drops — 公開 repo（D:\Wiki\lwf-eye-drops）に出すものだけを並べる
#
#   powershell -ExecutionPolicy Bypass -File release\publish-tree.ps1 [-Dest D:\Wiki\lwf-eye-drops]
#
# 出すもの:  ソース 1 本（EyeDropsMod.cs）・build.ps1、release\ の LICENSE / README.md / README.en.md / CHANGELOG.md・img\（原本 *.raw.mkv は除く）・
#            release.ps1・zip-README.txt・publish-tree.ps1・thunderstore\
# 出さないもの:
#   - README.md（開発用。設計の記録と手元のパスを含む）
#   - icon\（作業ファイル）、bin\ dist\（生成物）
#
# ⚠ このファイル自身は UTF-8 (BOM あり) で保存すること。

param(
    [string]$Dest = "D:\Wiki\lwf-eye-drops"
)
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path   # ...lraune_hotkeys\release
$mod  = Split-Path -Parent $here                          # ...lraune_hotkeys

if (-not (Test-Path $Dest)) { throw "公開 repo がありません: $Dest（先に git init / clone しておく）" }

function CopyTo($src, $rel) {
    $dst = Join-Path $Dest $rel
    $dir = Split-Path -Parent $dst
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Copy-Item $src -Destination $dst -Force
}

foreach ($f in @('EyeDropsMod.cs','build.ps1')) {
    CopyTo (Join-Path $mod $f) $f
}

CopyTo (Join-Path $here 'LICENSE') 'LICENSE'
CopyTo (Join-Path $here 'README.md') 'README.md'
CopyTo (Join-Path $here 'README.en.md') 'README.en.md'
CopyTo (Join-Path $here 'CHANGELOG.md') 'CHANGELOG.md'
CopyTo (Join-Path $here 'CHANGELOG.ja.md') 'CHANGELOG.ja.md'

$img = Join-Path $Dest 'img'
if (-not (Test-Path $img)) { New-Item -ItemType Directory -Path $img -Force | Out-Null }
# 録画の原本（*.raw.mkv・数十 MB）は出さない
if (Test-Path (Join-Path $here 'img')) { Get-ChildItem (Join-Path $here 'img') -File | Where-Object { $_.Name -notlike '*.raw.mkv' } | Copy-Item -Destination $img -Force }

foreach ($f in @('release.ps1','zip-README.txt','publish-tree.ps1')) {
    CopyTo (Join-Path $here $f) (Join-Path 'release' $f)
}
foreach ($f in @('manifest.json','icon.png','README.md','README.ja.md')) {
    $src = Join-Path $here (Join-Path 'thunderstore' $f)
    if (Test-Path $src) { CopyTo $src (Join-Path 'release' (Join-Path 'thunderstore' $f)) }
}

Write-Host "並べました: $Dest"
Get-ChildItem $Dest -Recurse -File | Where-Object { $_.FullName -notmatch '\\\.git\\' } | ForEach-Object { Write-Host "  $($_.FullName.Substring($Dest.Length + 1))" }
