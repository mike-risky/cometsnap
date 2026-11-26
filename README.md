<p align="center"><img src="assets/png/cometsnap-256.png" width="128" alt="Cometsnap"></p>

<h1 align="center">Cometsnap</h1>

**A free, fun and snappy screenshot tool for Windows.**
Hotkey to overlay in under 100 ms, smart window detection, a pixel-perfect magnifier,
instant annotations and a tray flyout of your recent shots.
No paywall, no branding, no forced updates.

> 🚧 Early days: Phase 1 ("snappy core") is being built. See the roadmap below.

## Download

Cometsnap is a single portable `.exe`, nothing to install.

- **Latest build:** grab `Cometsnap-x64.exe` (most PCs) or `Cometsnap-arm64.exe`
  (Snapdragon / Surface Pro X) from the [Latest build](https://github.com/mike-risky/cometsnap/releases/tag/nightly) pre-release.
- Put a file named `portable.txt` next to the exe to keep settings and captures beside it (USB-friendly).

Windows may show a SmartScreen warning the first time because the exe is not code-signed yet:
click **More info → Run anyway**.

## Features (Phase 1)

- Lives in the tray: left click opens a flyout with your last 30 captures, right click gives the full menu
- **Print Screen** (or Ctrl+Shift+S) freezes the screen instantly
- Hover to detect the window under the cursor, click to grab it, or drag any area (edges snap to windows).
  The moment you let go it's copied and saved: no toolbar in the way
- First run offers to **replace your old screen capture**: Cometsnap takes over Print Screen from
  Windows' Snipping Tool (or Greenshot, ShareX, Lightshot, Snagit...)
- **Starts with Windows** by default, so Print Screen works right after you log in. Turn it off in
  *Options > Start with Windows* (it also follows the exe if you move or re-download it)
- Magnifier loupe with pixel grid, X/Y, selection size and hex color; it sits in a corner and dodges your cursor
- Pixel-precise keyboard control: arrows = 1 px, Shift + arrows = 10 px, Enter to confirm, Esc to cancel
- A small **Edit** card pops up after each capture (it never steals focus). Click it, or double-click
  a capture in the tray, to open the editor. Big labeled tools: select/move, arrow, line, box, circle,
  text, speech bubble, highlighter, pen, numbered steps, blur, spotlight (greys out the rest) and crop.
  8 colors, 3 sizes, undo/redo, zoom. **Copy & close** or **Save** finishes and closes the editor.
  Shapes stay editable: re-open a capture later and keep going
- Prefer annotating on the frozen screen? Turn off *Options > Instant capture* to get the quick bar back
- Copied to the clipboard and auto-saved to `Pictures\Cometsnap\yyyy-MM\`
- Left-click the tray icon for the flyout: big capture buttons and your last 30 captures.
  Click a thumbnail to copy it, double-click to edit it, drag it straight into Word, Outlook, Teams or a browser
- A comet flies into the tray after every capture, with an optional soft click (both in *Options*)
- Fullscreen, active window and delayed captures, multi-monitor and mixed DPI

## Keys

| Where | Key | Action |
|---|---|---|
| Anywhere | **Print Screen** (or **Ctrl+Shift+S**) | Region / window capture |
| Anywhere | **Ctrl+Print Screen** | Fullscreen of the monitor under the mouse |
| Anywhere | **Alt+Print Screen** | Active window |
| Anywhere | **Shift+Print Screen** | Record a region / window to MP4 (press again, click the tray icon or ■ to stop) |
| Anywhere | **Ctrl+Shift+Print Screen** | Record a region / window to an animated GIF |
| Overlay | click | Capture the highlighted window |
| Overlay | drag | Capture a free region (hold **Alt** to disable edge snapping, hold **Space** to move it) |
| Overlay | arrows / **Shift**+arrows | Move the cursor 1 px / 10 px |
| Overlay | **Enter** | Capture the highlighted window |
| Overlay | **Esc** / right click | Close |
| Overlay | **F** | Capture the whole monitor |
| Overlay | **W** | Toggle window detection |
| Overlay | **C** | Copy the hex color under the cursor |
| Editor | **V** | Select: drag to move, arrows to nudge, **Del** to delete, pick a color or size to change it |
| Editor | **A** / **L** / **R** / **E** | Arrow / line / box / circle (Shift = 45°, square, perfect circle) |
| Editor | **T** / **K** | Text / speech bubble (drag from the target to where the bubble goes) |
| Editor | **H** / **P** / **N** | Highlighter / pen / numbered steps |
| Editor | **B** / **S** / **C** | Blur / spotlight / crop |
| Editor | **1**–**8**, **[** / **]** | Color, size |
| Editor | **Ctrl+Z** / **Ctrl+Y** | Undo / redo (moves, colors and deletes too) |
| Editor | **Ctrl+C** or **Ctrl+Enter** | Copy, save and close |
| Editor | **Ctrl+S** / **Ctrl+Shift+S** | Save and close / save a copy as… |
| Editor | **Ctrl+wheel**, **Ctrl+0**, **Ctrl+1** | Zoom, fit, 100% |

With *Instant capture* off, the same tool keys work on the frozen screen once a region is selected,
and **Ctrl+E** opens the selection in the editor.

## Roadmap

1. **Snappy core**: tray, hotkeys, capture overlay, loupe, quick annotations
2. **Full editor + docs mode**: all tools, re-editable captures, effects, doc sessions with PDF/DOCX/Markdown export
3. **Smart tools**: OCR, color picker, QR reader, scrolling capture
4. **Video**: MP4/GIF recording with sound
5. **Share**: Imgur, SFTP, S3, OneDrive, Google Drive
6. **Extras**: auto-blur of sensitive data, subtitles, Android capture

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet build Cometsnap.sln
dotnet test Cometsnap.sln
dotnet publish src/Cometsnap.App -c Release -r win-x64 -o out   # portable out/Cometsnap.exe
```

Every push builds `Cometsnap.exe` for x64 and ARM64 with GitHub Actions.

## Project layout

```
src/Cometsnap.App      WPF app: tray, flyout, overlay, quick bar
src/Cometsnap.Core     capture engine, window detection, hotkeys, storage, settings
src/Cometsnap.Editor   vector annotation model + rendering
tests/                 unit tests
assets/                icon, sounds
```

## Brand

Logo, colors and type rules are in [assets/BRAND.md](assets/BRAND.md).
Fonts: Bungee and Bungee Shade by David Jonathan Ross, [SIL Open Font License](assets/fonts/OFL.txt).

## License

[MIT](LICENSE) © Michael Solioz
