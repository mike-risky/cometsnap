<p align="center"><img src="assets/png/cometsnap-256.png" width="128" alt="Cometsnap"></p>

<h1 align="center">Cometsnap</h1>

**A free, fun and snappy screenshot and screen recording tool for Windows, macOS and Linux.**
Press Print Screen and it's instant: smart window detection, a pixel-perfect magnifier,
annotations, MP4 and GIF recording, and a panel of your recent shots you can drag into any app.
No paywall, no branding, no forced updates. 🌐 **[cometsnap.com](https://cometsnap.com)**

| | Download | Notes |
|---|---|---|
| **Windows** 10 and 11 | [Installer (x64)](https://github.com/mike-risky/cometsnap/releases/download/nightly/Cometsnap-Setup-x64.exe) · [ARM64](https://github.com/mike-risky/cometsnap/releases/download/nightly/Cometsnap-Setup-arm64.exe) | Portable exe also available |
| **macOS** 14+ | [Cometsnap-macOS.dmg](https://github.com/mike-risky/cometsnap/releases/download/macos-nightly/Cometsnap-macOS.dmg) | Universal: Apple Silicon and Intel |
| **Linux** | [AppImage (x86_64)](https://github.com/mike-risky/cometsnap/releases/download/linux-nightly/Cometsnap-x86_64.AppImage) · [.deb](https://github.com/mike-risky/cometsnap/releases/download/linux-nightly/cometsnap_amd64.deb) | X11 and Wayland, ARM64 builds too |

> 🚀 Early days and moving fast: these are the latest builds, updated with every change.

## Download

### Windows

Get the installer from the [Latest build](https://github.com/mike-risky/cometsnap/releases/tag/nightly) pre-release:
**`Cometsnap-Setup-x64.exe`** (most PCs) or `Cometsnap-Setup-arm64.exe` (Snapdragon / Surface Pro X).

- Installs just for you, no admin rights. It says clearly whether it is a new install, an update
  (`0.1.41 → 0.1.42`) or a reinstall, closes any running Cometsnap first, and keeps your settings and captures.
- When it finishes, Cometsnap starts and offers to replace your old screen capture tool.
- Uninstall from *Settings > Apps*: startup entry removed, Print Screen goes back to Windows, captures kept.

Prefer no install? `Cometsnap-x64.exe` / `Cometsnap-arm64.exe` are portable single files.
- Put a file named `portable.txt` next to the exe to keep settings and captures beside it (USB-friendly).

Windows may show a SmartScreen warning the first time because the exe is not code-signed yet:
click **More info → Run anyway**.

### macOS

One universal app for Apple Silicon and Intel Macs, macOS 14 Sonoma or newer.

- **Latest build:** `Cometsnap-macOS.dmg` from the [Latest macOS build](https://github.com/mike-risky/cometsnap/releases/tag/macos-nightly) pre-release.
  Open the dmg and drag Cometsnap into Applications.
- The app is not signed with a paid Apple certificate yet, so macOS blocks the first launch.
  On macOS 15 and newer: **System Settings → Privacy & Security → Open Anyway**.
  On macOS 14: **right-click Cometsnap → Open → Open**.
- Allow **Screen & System Audio Recording** when asked, then click **Quit & Reopen**.
- Keys: **⌘⇧2** capture (Cometsnap offers to take over **⌘⇧4** too), **⌘⇧7** video, **⌘⇧8** GIF,
  and **Print Screen / F13** on PC keyboards. Source and build notes: [`macos/`](macos/README.md).

### Linux

Works on X11 and Wayland (GNOME, KDE Plasma, Cinnamon, Xfce and others), on x86_64 and ARM64.
Get it from the [Latest Linux build](https://github.com/mike-risky/cometsnap/releases/tag/linux-nightly) pre-release:

- **Any distro:** `Cometsnap-x86_64.AppImage` (or `Cometsnap-aarch64.AppImage` on ARM).
  Make it executable and run it: `chmod +x Cometsnap-x86_64.AppImage && ./Cometsnap-x86_64.AppImage`.
  It adds itself to your app menu on first start, and comes with its own ffmpeg for recordings.
- **Ubuntu, Debian, Mint, Pop!_OS:** `sudo apt install ./cometsnap_amd64.deb` (or `cometsnap_arm64.deb`).

On first start Cometsnap offers to take over Print Screen from your desktop's screenshot tool
(automatic on GNOME, Cinnamon and Xfce; other desktops get the commands to bind).
On Wayland, captures and recordings go through the desktop portal, so the first one asks for permission.

## Features

Described for Windows (keys use Print Screen). The macOS and Linux apps share the same ideas and look;
see [`macos/`](macos/README.md) and [`linux/`](linux/) for their details.

- Lives in the tray: left click opens a flyout with your last 30 captures, right click gives the full menu
- Speaks **English, French, German and Italian**: it follows the Windows display language (English otherwise), and *Options > Language* switches it on the spot. The installer follows Windows too.
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
- After every capture, the capture itself rides a comet into the tray, with an optional soft click (both in *Options*)
- **Drag any capture out** of the tray panel (or the Copied card) and drop it into Teams, Word, Outlook, a browser or Explorer
- Little extras, all discreet: a tiny meteor shower for snap milestones (100th, 1,000th…) and a monthly
  "You captured N snaps" card, a snowy comet in December and a pumpkin comet at Halloween (*Options* to turn off),
  **Constellation** mode in the editor that joins numbered steps with faint star lines, and a secret in the tray
  panel for anyone who knows ↑ ↑ ↓ ↓ ← → ← → B A
- Fullscreen, active window and delayed captures (3, 5 or 10 s), multi-monitor and mixed DPI
- **Screen recording**: MP4 with PC sound and microphone, or an animated GIF, of any area or the whole screen

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
| Trimmer | drag the yellow handles, or **I** / **O** | Trim the start / end of a recording |
| Trimmer | drag across the timeline, then **Del** | Cut that part out (click a cut to put it back) |
| Trimmer | **Space**, **←** / **→**, **Home** / **End** | Play / pause, step a frame (Shift = 1 s), jump to start / end |
| Trimmer | **Ctrl+C** / **Ctrl+S** / **Ctrl+Shift+S** | Copy and close / save and close / save a copy as… |

Recordings open in the trimmer from the card that appears after recording, or with a double-click
in the tray panel. Saving keeps the untouched original hidden next to it, so you can re-open and
change the cuts later.

With *Instant capture* off, the same tool keys work on the frozen screen once a region is selected,
and **Ctrl+E** opens the selection in the editor.

## Roadmap

1. ✅ **Snappy core**: tray, hotkeys, capture overlay, loupe, quick annotations
2. ✅ **Full editor**: all tools, re-editable captures (docs mode with PDF/DOCX/Markdown export comes later)
3. ✅ **Video**: MP4/GIF recording with sound, trim and cut
4. ✅ **Everywhere**: Windows, macOS and Linux apps, in English, French, German and Italian
5. **Smart tools**: OCR, QR reader, scrolling capture
6. **Share**: Imgur, SFTP, S3, OneDrive, Google Drive

## Building

Windows: requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) on Windows.
macOS and Linux: see [`macos/README.md`](macos/README.md) and [`linux/`](linux/).

```powershell
dotnet build Cometsnap.sln
dotnet test Cometsnap.sln
dotnet publish src/Cometsnap.App -c Release -r win-x64 -o out/x64   # portable out/x64/Cometsnap.exe
# Setup (Inno Setup 6.3+): ISCC /DArch=x64 installer\Cometsnap.iss   -> out/Cometsnap-Setup-x64.exe
```

Every push builds `Cometsnap.exe` for x64 and ARM64 with GitHub Actions.

The Linux app lives in `linux/` (Avalonia on .NET 8) and builds on any Linux machine:

```bash
cd linux
dotnet test tests/Cometsnap.Linux.Tests
dotnet run --project src/Cometsnap.Linux
packaging/package.sh x86_64        # build/Cometsnap-x86_64.AppImage and build/cometsnap_amd64.deb
```

## Project layout

```
src/Cometsnap.App      WPF app: tray, flyout, overlay, quick bar
src/Cometsnap.Core     capture engine, window detection, hotkeys, storage, settings
src/Cometsnap.Editor   vector annotation model + rendering
tests/                 unit tests
assets/                icon, sounds
linux/                 Linux app (Avalonia): X11 + Wayland portal capture, editor, recorder, packaging
```

## Brand

Logo, colors and type rules are in [assets/BRAND.md](assets/BRAND.md).
Fonts: Bungee and Bungee Shade by David Jonathan Ross, [SIL Open Font License](assets/fonts/OFL.txt).

## License

[MIT](LICENSE) © Michael Solioz
