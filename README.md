# Hit Error Meter — A Dance of Fire and Ice mod

A [UnityModManager](https://www.nexusmods.com/site/mods/21) mod for **A Dance of Fire and Ice** that shows the exact hit error in milliseconds for every tile, with a combo counter that has its own colour / size / shake feedback.

## Features

- **Per-hit hit error** — displays `+12.34 ms` / `-5.67 ms` above the judgement bar, coloured by judgement (green = Perfect, red = TooEarly/TooLate, orange = in-between).
- **Combo counter** — counts your current streak, coloured by the same judgement states.
- **Combo colour tiers** — grey for the first 20 hits, orange once you hold a non-red streak ≥ 20, gold once a perfect streak reaches ≥ 50.
- **Font-size milestones** — the combo label grows at 50 / 100 / 200 / 500 / 1000 combo (capped at 2.0×).
- **Combo text shake** — a damped sine wobble (0.30 s) on every hit, amplitude / frequency scale with combo (up to 10 px / 26 Hz). Only active while the combo is orange or gold.
- Everything resets on fail / level end / player-state change.

## Install

1. Install [UnityModManager](https://www.nexusmods.com/site/mods/21) for the game (version 0.22.14 or newer).
2. Download `HitError.zip` from [Releases](../../releases) and drop it into the UMM **Mods** tab, or unzip it to
   `...\A Dance of Fire and Ice\UMM\Mods\HitError\` (so that `HitError.dll` and `Info.json` sit in that folder).
3. If you replace the DLL manually, delete any `*.cache` files in that folder, then restart the game (not just the level).

## Build

Requires Windows + .NET Framework 4.0 `csc` (ships with Windows) and the game installed.

```powershell
.\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice"
```

The output goes to `dist\HitError.dll`. Set the `ADOFAI_DIR` environment variable to skip `-GameDir`.

## Known limitations

- **Key-block shake is not implemented.** The `KeyShakeDriver` class is an experimental attempt at vibrating JipperKeyViewer key blocks; it is currently a no-op. The mod works fine without JipperKeyViewer installed.
- The label position is tuned for the default judgement-bar layout (128 px above the bar centre). Different UI scales may need tweaking in `HitErrorDisplay`.
- Logs `[HitError]` lines to `Player.log`, which is useful when debugging but noisy for daily play.

## Game / mod compatibility

- Game: current Steam build (tested 2026-10-23), UMM `0.22.14.0`.
- Harmony is the copy bundled with UnityModManager (`UMM\Core\0Harmony.dll`).

## AI disclosure

Parts of this mod were developed with the help of generative AI. It is published as-is, for personal use and modding reference.

## License

MIT — see [LICENSE](LICENSE).

---

# 中文说明

给《冰与火之舞》（A Dance of Fire and Ice）做的 UnityModManager 小 mod：**每次落地都显示精确到 0.01ms 的误差数字**，外加带颜色/字号/振动反馈的连击计数。

功能：逐击误差显示、连击计数、连击颜色分级（前 20 灰 → 连续非红 ≥20 橙 → 连续 Perfect ≥50 金）、字号里程碑（50/100/200/500/1000 逐级放大，封顶 2 倍）、连击文字阻尼振动（0.30 秒归位，随连击加幅、加频）。失败/关卡结束自动清零。

安装：装好 UMM 后，把 Releases 里的 `HitError.zip` 拖进 UMM 的 Mods 页面，或解压到 `UMM\Mods\HitError\`。手动替换 DLL 时记得删同目录 `*.cache` 再重启游戏。

已知问题：**键块振动（JipperKeyViewer）尚未实现**，`KeyShakeDriver` 目前是空转的实验代码，不装 JipperKeyViewer 也能正常用。日志会往 `Player.log` 写 `[HitError]` 行。
