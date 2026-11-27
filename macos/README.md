# Cometsnap for macOS

Native Swift app (AppKit + ScreenCaptureKit), same feel and features as the Windows version.

| Area | What it does |
|---|---|
| Menu bar | Comet icon. Click: flyout with a starry header, big Region / Fullscreen / Delayed / Record buttons, search and the latest captures 3 per row (click copies, double-click edits, drag into any app, hover for edit / copy / Finder / trash; ⌘-click, Shift-click, a box on empty space or ⌘A pick several, and dragging any of them drags them all). Right-click: full menu. |
| Capture | **⌘⇧2** (or **⌘⇧4** after the takeover offer, or **Print Screen / F13**) freezes every screen. The takeover offer on first run hands all the macOS screenshot keys to Cometsnap: **⌘⇧4** area or window, **⌘⇧3** whole screen, **⌘⇧5** the Cometsnap panel (switch back any time in Settings). Hover highlights the window under the cursor, click grabs it, or drag any area (edges snap to windows, hold ⌥ to turn snapping off, hold Space to move the selection). Release = copied and saved, no toolbar in the way. |
| Loupe | 15×15 real pixels with grid, X/Y, size and hex color (**C** copies it). Sits in a corner and jumps away when the cursor comes close. |
| Keys in the overlay | arrows 1 px, ⇧ arrows 10 px, Return confirm, Esc cancel, **F** whole screen, **W** window detection on/off. |
| Edit card | Small "COPIED!" card in the corner. Click anywhere on it to edit, or drag it straight into any app. Never steals focus, fades out by itself. |
| Editor | 13 tools: Select V, Arrow A, Line L, Box R, Circle E, Text T, Bubble K, Marker H, Pen P, Steps N, Blur B, Spotlight S, Crop C. Colors 1–8, thickness, undo/redo. **Copy** (⌘C / ⌘↩) and **Save** (⌘S) both close it. Shapes stay editable (hidden `.cometsnap` folder beside the image, same layout as Windows). |
| Recording | **⌘⇧7** MP4 (H.264 + Mac sound), **⌘⇧8** GIF. Pick a window or area, press again (or click the menu bar icon or the pill) to stop. The file is copied and shows in the flyout. |
| Scrolling capture | **⌘⇧0**: click a window (or drag over the part that scrolls) and Cometsnap scrolls it for you, gluing one tall image. Sticky headers and toolbars appear once. Stops at the end of the page; Esc or moving the mouse stops it earlier. Needs the Accessibility permission once (to turn the scroll wheel). |
| Copy text | **⌘⇧9**: drag over any text on screen and the text lands on the clipboard. Also the text button on the Copied card. Uses Apple's Vision on the Mac, offline. Click the card to see the text and copy just a part. |
| Trimmer | Trim the start and end of an MP4 or GIF, or cut parts out of the middle. Opens from the Copied card (**Trim**) or a double-click on a recording in the panel. Space/K play, ←/→ one frame (⇧ 1 s), I/O start/end, ⌫ cuts the selection, ⌘Z/⇧⌘Z. Nothing is lost: the original stays in the hidden `.cometsnap` folder, cuts stay adjustable, Reset brings it all back. |
| Little extras | The capture rides a comet into the menu bar. A tiny meteor shower at 10, 50, 100, 250, 500, 1,000… snaps and a recap on the first snap of each month. A pumpkin comet from Oct 18 to 31 and a snowy one all December. The editor's **Constellation** button joins numbered steps with faint star lines. One more secret in the menu bar panel. Switch the celebrations or seasonal looks off in Settings. |
| Languages | English, French, German, Italian, Spanish, Brazilian Portuguese, Simplified Chinese and Hindi. Follows the Mac's language, with a Language picker in Settings that applies right away. |
| Other | Start at login, sound off by default, captures in `~/Pictures/Cometsnap/yyyy-MM/`, settings in `~/Library/Application Support/Cometsnap/settings.json`. |

## Install

Paste in Terminal (downloads the latest build into Applications and opens it, no Gatekeeper detour;
run it again to update):

```bash
curl -fsSL https://raw.githubusercontent.com/mike-risky/cometsnap/main/macos/install.sh | bash
```

Or download [Cometsnap-macOS.dmg](https://github.com/mike-risky/cometsnap/releases/download/macos-nightly/Cometsnap-macOS.dmg)
and follow [FIRST-LAUNCH.txt](FIRST-LAUNCH.txt). Opened from the disk image, Cometsnap offers to move
itself into Applications.

## Build

On a Mac with Xcode 15+:

```bash
cd macos
swift test                       # unit tests of the shared logic
scripts/make-app.sh 0.1.0        # build/Cometsnap.app, Cometsnap-macOS.dmg, Cometsnap-macOS.zip
```

GitHub Actions (`.github/workflows/macos.yml`) builds the universal app on every change under `macos/`,
publishes `macos-nightly` for `main` and `macos-test-prN` for pull requests.

## Signing

Builds are ad-hoc signed until signing secrets are added. macOS therefore asks for Gatekeeper
approval on first launch (see [FIRST-LAUNCH.txt](FIRST-LAUNCH.txt)) and may ask for screen access
again after an update. CI is ready for a Developer ID certificate plus notarization, which removes
both: see [SIGNING.md](SIGNING.md).
