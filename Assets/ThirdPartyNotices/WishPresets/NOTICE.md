# Preset wish stroke data: third-party notices

`Assets/Resources/MoodiumBubblePlay/WishPresets/wish_presets.json` has pen strokes derived from two sources.

## Chinese: Make Me a Hanzi stroke medians

- Data: hanzi-writer-data 2.0.1 (https://github.com/chanind/hanzi-writer-data), derived from Make Me a Hanzi
  (https://github.com/skishore/makemeahanzi), which is derived from fonts by Arphic Technology.
- License: Arphic Public License. The full text is in `ARPHICPL.TXT` next to this file.
- Used for these characters: 平 安 喜 乐 天 开 心 好 事 发 生 身 体 健 康 烦 恼 退 散.
- Changes: the stroke medians were smoothed, resampled and laid out on the wish paper (Source/tools/build_presets.py).
  The derived stroke data stays under the Arphic Public License.

## English: EMS Felix single-line font

- Font: EMS Felix by Sheldon B. Michaels (SVG conversion by Windell H. Oskay), a derivative of Felipa by Fontstage.
  Distributed in hersheytext 2.0.0 (https://github.com/techninja/hersheytext, MIT).
- License: SIL Open Font License 1.1 (https://openfontlicense.org).
- Changes: the glyph paths for the five English wishes were converted to pen strokes. No font file ships in the app.
