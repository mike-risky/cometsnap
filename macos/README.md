# Cometsnap for macOS

Native Swift app (AppKit + ScreenCaptureKit), same feel and features as the Windows version.

| Area | What it does |
|---|---|
| Menu bar | Comet icon. Click: flyout with a starry header, big Region / Fullscreen / Delayed / Record buttons, search and the latest captures 3 per row (click copies, double-click edits, drag into any app, hover for edit / copy / Finder / trash). Right-click: full menu. |
| Capture | **⌘⇧2** (or **⌘⇧4** after the takeover offer, or **Print Screen / F13**) freezes every screen. Hover highlights the window under the cursor, click grabs it, or drag any area (edges snap to windows, hold ⌥ to turn snapping off, hold Space to move the selection). Release = copied and saved, no toolbar in the way. |
| Loupe | 15×15 real pixels with grid, X/Y, size and hex color (**C** copies it). Sits in a corner and jumps away when the cursor comes close. |
| Keys in the overlay | arrows 1 px, ⇧ arrows 10 px, Return confirm, Esc cancel, **F** whole screen, **W** window detection on/off. |
| Edit card | Small "COPIED!" card in the corner. Click anywhere on it to edit, or drag it straight into any app. Never steals focus, fades out by itself. |
| Editor | 13 tools: Select V, Arrow A, Line L, Box R, Circle E, Text T, Bubble K, Marker H, Pen P, Steps N, Blur B, Spotlight S, Crop C. Colors 1–8, thickness, undo/redo. **Copy** (⌘C / ⌘↩) and **Save** (⌘S) both close it. Shapes stay editable (hidden `.cometsnap` folder beside the image, same layout as Windows). |
| Recording | **⌘⇧7** MP4 (H.264 + Mac sound), **⌘⇧8** GIF. Pick a window or area, press again (or click the menu bar icon or the pill) to stop. The file is copied and shows in the flyout. |
| Little extras | The capture rides a comet into the menu bar. A tiny meteor shower at 10, 50, 100, 250, 500, 1,000… snaps and a recap on the first snap of each month. A pumpkin comet from Oct 18 to 31 and a snowy one all December. The editor's **Constellation** button joins numbered steps with faint star lines. One more secret in the menu bar panel. Switch the celebrations or seasonal looks off in Settings. |
| Other | Start at login, sound off by default, captures in `~/Pictures/Cometsnap/yyyy-MM/`, settings in `~/Library/Application Support/Cometsnap/settings.json`. |

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

Builds are ad-hoc signed. macOS therefore asks for Gatekeeper approval on first launch (see
[FIRST-LAUNCH.txt](FIRST-LAUNCH.txt)) and may ask for screen access again after an update.
A Developer ID certificate plus notarization would remove both.
