# Launcher category icons — 0.2 local preview

Rows without independent artwork use host-authored WPF vector icons. The category comes from current typed row metadata, never display names, aliases, authored scripts or source filenames. Group headers remain non-actionable text. This is presentation only: no command IDs, persistence schema, feature gates or execution behavior change.

| Category | Artwork / normal theme color |
|---|---|
| Application fallback | Four tiles / blue |
| Window management (including custom sizes) | Split window / purple |
| Window layout | Layout grid / indigo |
| Extension | Puzzle / teal |
| Custom command | Code brackets / green |
| Shell | Terminal prompt / blue |
| Calculator | Calculator / amber |
| Calculation history | Clock / brown |
| Settings | Gear / slate |
| Recycle Bin | Bin / rose |
| Other command | Bolt / brown |

A 28-DIP icon slot plus 10-DIP spacing produces one 38-DIP column for all normal result rows. Title and subtitle share the same text origin; the slot stays reserved when application icons are off. Existing application artwork remains a centered 24-DIP image and takes precedence as soon as its bounded asynchronous loader publishes. Missing/loading/unsupported artwork uses the application category vector instead of a font character. Currency cards retain their intentionally separate card layout.

`Show application icons` still gates application visibility and resource IO; it does not turn off host-rendered command icons. Category artwork performs no file/network/plugin reads or command execution. ThemeService owns only eleven frozen DrawingImage resources per refresh, with lighter lines/tinted tiles in dark mode. High-contrast resources use opaque system window/text colors, not category colors; the window-colored tile remains readable on a selected row. Icons never take pointer/keyboard focus; existing row text/type labels describe the result.

Acceptance: 14 pure classification cases plus owned WPF templates verify all eleven categories, frozen 28-DIP drawings, 38-DIP title/subtitle alignment, dynamic theme resources, application-off IO/visibility separation and late application-image precedence. Light/dark/high-contrast-resource PNG atlases are drawn from the same owned frozen resources without the real window's ancestor offset/clip; actual row-template/ancestor/alignment checks run separately. They are not desktop screenshots. The high-contrast probe does **not** establish physical Windows high-contrast acceptance. Native mixed-DPI/manual visuals and broader accessibility remain pending. Existing full owned window/settings/default/tray/models regressions are tracked in acceptance.md.
