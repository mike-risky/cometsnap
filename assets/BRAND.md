# Cometsnap brand v1

Agreed with Mike on 26 Nov 2026.

## Logo: D1 "Bold Flat"
A cratered rock with a dark outline. Three layers of flat flame stream behind it, toward the bottom-left. There are no gradients, so it stays crisp at 16 px.

| File | Use |
|---|---|
| `cometsnap-icon.svg` | Master vector (64×64 viewBox). Scale it freely. |
| `cometsnap.ico` | App, exe and tray icon. Contains 16, 20, 24, 32, 40, 48, 64 and 256 px. |
| `png/cometsnap-{16…1024}.png` | Transparent PNGs for the About box, website, store and README. |

Rules:
- Keep the motion direction: the rock is top-right and the fire goes to the bottom-left. Don't flip or rotate it.
- On dark backgrounds use it as is. On light backgrounds it also works, because the rock has its own dark outline.
- Leave clear space of at least ¼ of the icon size around it.
- Tray states (from the plan): during the capture animation, use the normal icon. While recording, use the normal icon plus a pulsing red dot (`#E5484D`) at the bottom-right.

## Colors: "Electric"
| Token | Hex | Role |
|---|---|---|
| night | `#07113A` | App background, deep space |
| surface | `#0F1D57` | Panels, flyout, cards |
| line | `#22348A` | Borders, dividers |
| blue | `#3D6BFF` | Interface accent: selection border and glow, focus, links |
| terracotta | `#E2553A` | Brand and primary buttons (Copy, Save), outer flame |
| flame orange | `#F09625` | Middle flame (a mix of terracotta and yellow) |
| yellow | `#FFD60A` | Highlights: size tag, celebrations, inner flame |
| rock | `#5E4C4A` | Comet rock |
| rock dark | `#1F2559` | Craters |
| text | `#FFFFFF` | Main text |
| text muted | `#A9B6F0` | Secondary text |
| record red | `#E5484D` | Recording only |

Rule of thumb: blue is for the tool and the fire colors are for the brand. The selection border is blue, and the main action button is terracotta.

## Type
- **H1 / display:** Bungee Shade (Google Fonts, OFL). Use it for big titles only, at 28 px or larger, because the shading turns muddy when small.
- **H2 / H3:** Bungee, the solid sibling of Bungee Shade, so the headings feel related.
- **UI and body text:** Segoe UI Variable inside the Windows app (it's native and crisp). Use Outfit on the website.
- **Wordmark:** "COMETSNAP" set in Bungee Shade. In two-tone use, "COMET" is white and "SNAP" is yellow `#FFD60A`.

Both Bungee fonts are SIL Open Font License, so they can be bundled in the exe.
