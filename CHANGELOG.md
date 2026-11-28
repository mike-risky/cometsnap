# What's new in Cometsnap

All the changes worth knowing about, newest first. The same list, in 8 languages,
lives on [cometsnap.com/changelog](https://cometsnap.com/changelog).

Downloads are on the [Releases page](https://github.com/mike-risky/cometsnap/releases).
Changes merged after the last release show up under "Latest build" first: they are already in the
`nightly`, `macos-nightly` and `linux-nightly` builds and become part of the next numbered release.

## Latest build

### New
- **Share-ready style, your way.** Make screenshots look the way they do in social posts, and build the look
  yourself in the new **Style studio** with a big live preview: rounded or square corners, a border in any color,
  a drop shadow you move pixel by pixel (left, right, up, down) with its own blur, opacity and color, a laser line
  or neon glow in any color, and a backdrop (one color, a gradient, the capture blurred behind itself, or deep space
  with stars). Turn each part on or off and mix them freely, or start from Clean, Showcase, Frosted, Neon, Laser,
  Comet or Polaroid. Three switches decide where it applies: **Style on copy** (clipboard), **Style on export**
  (saved files and what you drag out of the tray) and **Style per shot**, a ✨ button on the Copied card that
  styles just that capture. Copy and export stay raw by default, so capture is as instant as ever. Windows, macOS and Linux.
- **Scan a QR code.** Press Ctrl+Alt+Q (⌘⇧1 on macOS, or pick it in the tray menu), drag over a QR code and what
  it holds is on your clipboard. Barcodes work too (EAN, UPC, Code 128, Data Matrix and more). It reads offline,
  nothing is uploaded. When the code is a web link, the Copied card shows an **Open link** button; links never open
  on their own. The shortcut can be changed in Settings. Windows, macOS and Linux.

### Improved
- **A quick mission briefing on first launch.** The "Replace your old screen capture" window now also explains
  Cometsnap in four short lines (press, let go and it's copied and saved, click the Copied card to edit, find
  your snaps in the tray) plus the handy shortcuts: video, GIF, copy text and the color picker. It shows the
  keys you actually have, on Windows, macOS and Linux, in all 8 languages.

## v1.3.0 — 28 November 2026

### New
- **Change any shortcut, on every platform.** Settings has a shortcut editor on Windows, macOS and Linux:
  click a shortcut and press the new keys.
- **Name your captures your way.** A file name pattern with tags such as `{date}`, `{time}`, `{year}`,
  `{month}`, `{day}`, `{hour}`, `{minute}`, `{second}` and `{counter}`. A `/` makes a subfolder.
  The default keeps the old names.
- **Where captures go.** Choose PNG or JPG, ask where to save each time, or open every capture straight
  in the editor. The clipboard copy still happens instantly, before anything else.
- **Capture colors.** Pick the look of the capture screen in Settings: Comet blue, Sun yellow, Terracotta,
  Flame orange or Stardust, with a live preview.
- **Color picker** (Win+Shift+C, ⌘⇧6 on macOS, Super+Shift+C on Linux): click any pixel and its color lands
  on the clipboard as `#RRGGBB` (Shift+click for `rgb(...)`). In any capture, **C** copies the color under the cursor.

### Smaller download (Windows)
- The installer is now about **6 MB** instead of 62 MB. It only downloads the free Microsoft .NET Desktop Runtime
  if your PC doesn't have it yet.
- The portable exe is compressed: about **83 MB** instead of 238 MB.
- Cometsnap now runs on .NET 10 (Microsoft ended support for .NET 8 in November 2026).

### Fixed
- **Windows:** on two screens, the first capture after a quiet spell could feel stuck on one screen.
  The first drag is now always caught, and Cometsnap stays warm in the background at no cost.
- **macOS:** the first-launch notes now lead with a one-line Terminal fix that opens the app normally,
  without the Privacy & Security detour.

## v1.2.0 — 27 November 2026

### Improved
- **Scrolling capture, reworked:** start any capture, drag over the part that scrolls and, while still
  holding the button, scroll with the wheel. Cometsnap glues one tall image; let go to finish, Esc cancels.
  Sticky headers and footers appear only once.

## v1.1.0 — 27 November 2026

### New
- **Copy text (OCR)** on Windows, macOS and Linux: drag over any text on screen and it lands on the clipboard.
  It runs offline, nothing is uploaded.
- **Scrolling capture** on Windows, macOS and Linux: one tall image of a long page.
- **Pick several captures** in the tray panel (Ctrl+click, Shift+click, a box or Ctrl+A) and drag them out
  together into Explorer, Outlook, Teams or Slack. Copy and Delete work on the whole selection.

### macOS
- **Painless install:** a one-line Terminal install, and the app offers to move itself into Applications
  so the screen permission sticks.
- **Really takes over the screenshot shortcuts:** ⌘⇧3, ⌘⇧4 and ⌘⇧5 switch over at once, survive logout and
  come back to macOS if you untick the option.

## v1.0.0 — 26 November 2026

The first release, on Windows, macOS and Linux.

- **Instant capture:** Print Screen freezes the screen, hover to grab a window or drag any area.
  The moment you let go it's copied and saved.
- **Magnifier loupe** with pixel grid, size and color, which dodges your cursor.
- **Editor:** arrows, boxes, text, speech bubbles, highlighter, numbered steps, blur, spotlight and crop.
  Shapes stay editable.
- **Tray panel** with your last 30 captures: click to copy, double-click to edit, drag into any app.
- **Screen recording:** MP4 with sound and microphone, or an animated GIF, with trim and cut afterwards.
- **Replace your old capture tool:** the first run offers to take over Print Screen from Snipping Tool and friends.
- **Settings window** and **8 languages:** English, French, German, Italian, Spanish, Portuguese,
  Simplified Chinese and Hindi.
- **Little delights:** a comet trail into the tray, milestone meteor showers, seasonal comets, Constellation
  mode and a secret for anyone who knows ↑ ↑ ↓ ↓ ← → ← → B A.
- Installer and portable exe for Windows (x64 and ARM64), a universal DMG for macOS, AppImage and .deb for Linux.
