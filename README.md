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
- Hover to detect the window under the cursor, click to grab it, or drag any area (edges snap to windows)
- Magnifier loupe with pixel grid, X/Y, selection size and hex color; it sits in a corner and dodges your cursor
- Pixel-precise keyboard control: arrows = 1 px, Shift + arrows = 10 px, Enter to confirm, Esc to cancel
- Quick bar to annotate right on the frozen screen: circle, arrow, text, rectangle
- Copied to the clipboard and auto-saved to `Pictures\Cometsnap\yyyy-MM\`
- Left-click the tray icon for the flyout: big capture buttons and your last 30 captures.
  Click a thumbnail to copy it, double-click to open it, drag it straight into Word, Outlook, Teams or a browser
- A comet flies into the tray with a little whoosh after every capture (both can be turned off in settings)
- Fullscreen, active window and delayed captures, multi-monitor and mixed DPI

## Keys

| Where | Key | Action |
|---|---|---|
| Anywhere | **Print Screen** (or **Ctrl+Shift+S**) | Region / window capture |
| Anywhere | **Ctrl+Print Screen** | Fullscreen of the monitor under the mouse |
| Anywhere | **Alt+Print Screen** | Active window |
| Overlay | click | Grab the highlighted window |
| Overlay | drag | Free region (hold **Alt** to disable edge snapping, hold **Space** to move it) |
| Overlay | arrows / **Shift**+arrows | Move the cursor 1 px / 10 px |
| Overlay | **Enter** | Copy (in hover mode: copies the highlighted window right away) |
| Overlay | **Esc** / right click | Drop the selection, press again to close |
| Overlay | **F** | Whole monitor |
| Overlay | **W** | Toggle window detection |
| Overlay | **C** | Copy the hex color under the cursor |
| Selection | arrows / **Ctrl**+arrows | Move / resize the selection |
| Selection | double click | Copy |
| Selection | **E** / **A** / **T** / **R** | Circle / arrow / text / rectangle (Shift = perfect circle, square, 45° arrow) |
| Selection | **1**–**6** | Pick a color |
| Selection | **Ctrl+Z** / **Ctrl+Y** | Undo / redo |
| Selection | **Ctrl+S** | Save as… |

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
