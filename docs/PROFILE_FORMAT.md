# Profile format — v0.2

## Profile

```json
{
  "id": "browser",
  "displayName": "Browser",
  "priority": 50,
  "isDefault": false,
  "match": {
    "processNames": ["chrome", "msedge"],
    "executablePaths": ["C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"],
    "windowTitleContains": ""
  },
  "theme": {
    "background": "#E80D1117",
    "tileBackground": "#FF1D2733",
    "accent": "#FF55A7FF",
    "primaryText": "#FFF7FAFC",
    "secondaryText": "#FF91A4B7"
  },
  "fixedLeft": [],
  "fixedRight": [],
  "pages": []
}
```

Highest matching `priority` wins. Desktop uses the `desktop` profile. Other unmatched applications use the non-desktop `isDefault` profile; the bundled profile is named **Other Apps** in Designer, while Runtime shows the current application's friendly name. If that profile is absent, AdaptiveBarPC keeps the legacy generated generic layout.

Process names are compared without requiring `.exe`.

`executablePaths` matches the foreground process by executable path, case-insensitively. Designer's **Choose App…** action stores the selected executable path and also keeps its process name as a compatibility fallback. Existing profiles that only use `processNames` continue to work unchanged.

### Appearance

`appearance` is optional. Profiles without it continue to use the legacy `theme` values.

```json
"appearance": {
  "preset": "fluid",
  "accent": "#FF55A7FF",
  "secondaryAccent": "#FF9C6BFF",
  "intensity": 1.0,
  "motionEnabled": true
}
```

Recognized preset IDs are `fluid`, `oled`, `cyber`, `rgb`, and `pixel`. Missing or unknown preset IDs resolve to `fluid`. `appearance.accent` overrides the legacy theme accent when present, `secondaryAccent` defaults to the effective primary accent, and `intensity` is clamped to `0..1`.

In Designer → **Appearance → My Themes**, enter a name and choose **Save Copy** to keep the current palette, preset, intensity, motion and bar background settings, together with a still thumbnail of the bar preview. Click a saved preview to apply the theme to Designer and the running bar. Saving the same name creates a new copy with a suffix such as `(2)`; it never overwrites an existing theme. Profiles, fixed widgets and Input Tools are not changed when applying a theme.

The library is stored in `%LOCALAPPDATA%\AdaptiveBarPC\Themes` (beside `appearance.json`). Each theme owns its own copy of the background file, so moving the original image/GIF does not break the saved theme. File copying and package processing run in the background; the library controls show a busy state until the operation finishes.

**Export…** on a saved theme creates a portable `*.adaptivebar-theme.zip` package containing `theme.json`, `preview.png`, and optional `background.png` / `.jpg` / `.jpeg` / `.gif` / `.mp4`. The full original media file is included, including GIF animation; the thumbnail is static. Background fit, opacity, zoom and horizontal/vertical offsets are preserved. **Import Theme Package…** adds the package to the library without applying it automatically; click its preview to apply. The package uses a relative bundled background filename, which is resolved to the imported library asset when applied. Background files are limited to 128 MiB, previews to 4 MiB and manifests to 256 KiB. Invalid packages, duplicate/path-traversal archive entries and missing assets fail without replacing an existing theme.

For settings-only files, expand **Theme JSON (settings only)** and use **Save JSON…** / **Load JSON…**. This preserves the previous `*.adaptivebar-theme.json` workflow: media files are referenced by path, not embedded, and relative paths keep using the profile asset root. Invalid or unsupported theme files leave the current theme unchanged.

Theme files have `"format": "adaptivebar-theme"`, `"version": 1`, and the visual sections `theme`, `appearance`, and `backgroundMedia`. Library manifests also have a `name` (1–80 characters). The `theme` object is required; omitted `appearance` and `backgroundMedia` use defaults. Packages exclude profile definitions, widget/action settings and Input Tools. Their preview is a snapshot of the visible bar, so check it before sharing if your widget labels contain private text.

Pixel media buttons use the custom Surface color for their faces, Secondary Text for Previous/Next, Primary Accent for Play/Pause, and Secondary Accent for the pixel shadow. The media title and artist follow Primary Text and Secondary Text respectively in both Designer and Runtime.

### Global bar background media

Designer stores the full-bar background in `%LOCALAPPDATA%\AdaptiveBarPC\appearance.json`, alongside the global palette, fixed widgets, and shared input tools. PNG, JPG, GIF, and MP4 are supported. Offsets are device-independent pixels and can be adjusted live in the Appearance tab.

```json
"backgroundMedia": {
  "source": "Assets/media/background.gif",
  "fit": "cover",
  "loop": true,
  "opacity": 0.65,
  "enabled": true,
  "offsetX": 120,
  "offsetY": -8,
  "zoom": 1.4
}
```

An empty or missing `source` is valid and renders only the configured bar color. `zoom` accepts `0.1..4.0`, where `1.0` is 100%. Relative paths use the same root as profile assets.

Static images and GIF frames load in the background for both bar backgrounds and decorative media widgets. Changing the source or unloading the view discards the previous load. GIFs preserve frame timing, transparency and disposal; `loop: false` holds the last frame after one pass. Disabling effective motion or hiding the view pauses playback, and enabling it resumes playback.

## Pages

```json
"pages": [
  {
    "id": "main",
    "displayName": "Main",
    "items": []
  },
  {
    "id": "tabs",
    "displayName": "Tabs",
    "items": []
  }
]
```

Swipe left/right or use the arrow buttons to change pages.

### Fixed widgets

`fixedLeft` and `fixedRight` are profile-level widget arrays. They stay mounted while pages change, so only the center page content is rebuilt during a swipe.

```json
"fixedLeft": [
  { "id": "volume", "type": "slider", "label": "Volume", "action": { "type": "volume" } }
],
"fixedRight": [
  { "id": "mute", "type": "button", "label": "Mute", "action": { "type": "mute" } }
]
```

In Designer, a widget can be moved between `Current Page`, `Fixed Left`, and `Fixed Right`. Legacy page widgets with `"system": true` remain page-scoped for backward compatibility.

### v0.1 compatibility

A profile with only top-level `items` is still valid:

```json
{
  "id": "legacy",
  "items": [ ... ]
}
```

AdaptiveBarPC treats it as a single page named `Main`.

## Controls

### Icons

Controls that expose `icon` accept `glyph`, `none`, or `asset`:

```json
"icon": { "kind": "glyph", "value": "E767" }
"icon": { "kind": "none", "value": "" }
"icon": { "kind": "asset", "value": "Assets/icons/volume.png" }
```

Relative asset paths resolve from the parent of the active `Profiles` directory, so `Assets/icons/volume.png` is portable beside `Profiles/`. Explicit absolute file paths are also supported. Relative `..` traversal, missing files, malformed paths, and undecodable images fail safely and use the widget's normal fallback presentation.

Asset icons use WPF's built-in bitmap decoder. Common raster formats supported by the installed Windows/WPF codecs work; SVG is not decoded by this Phase 6 path.

### Button

```json
{
  "type": "button",
  "label": "Refresh",
  "width": 92,
  "action": { "type": "key", "value": "CTRL+R" },
  "secondaryAction": { "type": "key", "value": "CTRL+SHIFT+R" },
  "longPressMs": 700
}
```

- `action`: normal tap.
- `secondaryAction`: long press.
- `longPressMs`: optional per-button override.
- Buttons with a secondary action show a small accent dot.

### Slider

```json
{
  "type": "slider",
  "label": "MASTER VOL",
  "width": 230,
  "min": 0,
  "max": 100,
  "action": { "type": "volume" }
}
```

v0.2 currently supports master volume.

The slider renders at a minimum of 160 DIP, preserving a track hit area of at least 72×56 DIP. Existing `width` values down to 80 remain valid and are not rewritten; narrower bars can scroll horizontally.

### Application audio mixer

```json
{
  "type": "dynamiclist",
  "provider": "audioSessions"
}
```

The provider creates one mixer control per application; do not place `audio_session` directly in a profile. Runtime and Designer use the same 280 DIP minimum rendered width, with a full-height slider and a 56×56 DIP mute button. Long application names are trimmed. Mute exposes the application name and its on/off state through UI Automation.

### Sensor

```json
{
  "type": "sensor",
  "label": "CPU",
  "metric": "cpu_load",
  "width": 48,
  "unit": "%",
  "format": "{0:F0}"
}
```

The configured `width` is the minimum rendered width. Runtime and Designer expand the sensor when its icon, value and unit need more space. Existing narrow widths remain valid and are not rewritten in the profile; overflow uses the bar's horizontal scrolling.

### Media (v0.3)

```json
{
  "type": "media",
  "label": "Media",
  "width": 520
}
```

A horizontal AdaptiveBar/Touch Bar style control that displays current playing media and transport controls.

- **Source**: Metadata (title, artist, timeline) and controls come directly from Windows Global System Media Transport Controls (GSMTC).
- **Supported players**: Works with any media player that integrates with Windows System Media Transport Controls (e.g. Spotify desktop, Google Chrome, Microsoft Edge, modern Windows media apps).
- **Progress interpolation**: Progress bar is smoothly interpolated locally using wall-clock time (5 Hz) between authoritative GSMTC timeline updates.
- **Session capabilities**: Transport buttons (Previous, Play/Pause, Next) automatically enable/disable based on whether the active media session supports that capability (e.g. web browser video streams without playlist support disable Next/Previous).
- **Empty state**: When no media player is open or playing, displays a compact `No media` placeholder without shifting page layout.
- **Artwork and seeking**: Album art thumbnails have a fallback; both touch/mouse dragging and UI Automation can seek. Automation commits immediately, while a pointer drag commits on release.
- **Minimum width**: Active media renders at a minimum of 360 DIP to preserve transport buttons and a seek hit area of at least 72×56 DIP. Smaller legacy `width` values still load without rewriting the profile; horizontal scrolling handles overflow when the bar is narrow. Long title/artist text is trimmed.

### Spacer

```json
{ "type": "spacer", "width": 16 }
```

### Emoji, Clipboard, text slots and stickers

`Emoji` and `Clipboard` are built-in tools. They appear automatically while a writable, non-password text field has focus and do not need to be added to a page.

Reusable text slots and stickers are stored once in `%LOCALAPPDATA%\AdaptiveBarPC\appearance.json` under `inputItems` and are shared by every profile:

```json
"inputItems": [
  { "id": "email-slot", "type": "text_slot", "label": "Email", "width": 112, "action": { "type": "clipboard_text", "value": "hello@example.com" } },
  { "id": "wave-sticker", "type": "sticker", "label": "Wave", "width": 72, "source": "Assets/stickers/wave.png" }
]
```

- `Emoji` opens a temporary horizontal emoji picker on the bar. Emoji can be inserted repeatedly until Back is pressed.
- The picker displays bundled Microsoft Fluent Emoji 3D artwork (32 DIP) on 56 DIP buttons, independently of the theme font/text color. It works offline and inserts the original Unicode character sequence, not a sticker image or Enter; the receiving app determines how the emoji looks in chat. Keep `Assets/emoji` with the application when distributing it; missing artwork falls back to a larger Segoe UI Emoji label.
- `Clipboard` opens recent unique text copied while AdaptiveBarPC is running. Selecting an item pastes it and returns to the current page.
- `text_slot` stores reusable text globally, writes it to the Windows clipboard, then pastes it into the foreground application.
- `sticker` previews a configured PNG/JPEG and pastes it as a bitmap into the foreground application.
- Clipboard history is held in memory, newest first, and includes the current clipboard value when AdaptiveBarPC starts. Files, images and history from before startup are not included.
- When a writable, non-password text field has keyboard focus, AdaptiveBarPC temporarily replaces the page body with `Emoji`, `Clipboard`, `Text`, and `Sticker` shortcuts. Leaving the field restores the previous page.
- The automatic `Text` and `Sticker` shortcuts use global `inputItems`. Legacy page-scoped `text_slot` and `sticker` entries are still included for backward compatibility.
- In Designer, use the separate **Input** tab to manage global text slots and stickers. Existing page-scoped input items can be moved to `Input Tools (All Profiles)` from Properties.

## Actions

### Keyboard chord

```json
{ "type": "key", "value": "CTRL+SHIFT+T" }
```

Names include CTRL, ALT, SHIFT, WIN, arrows, ENTER, ESC, SPACE, TAB, BACKSPACE, DELETE, HOME, END, PAGEUP, PAGEDOWN, F1..F12, A..Z and 0..9.

### Unicode text

```json
{ "type": "text", "value": "Hello 👋" }
```

Sends UTF-16 Unicode input to the application that already owns keyboard focus. It does not replace the current clipboard.

### Clipboard text

```json
{ "type": "clipboard_text", "value": "Reusable text" }
```

Writes the value to the Windows clipboard and sends `CTRL+V`. If the clipboard cannot be updated, the paste is suppressed so stale clipboard content is never inserted.

### Clipboard image

```json
{ "type": "clipboard_image", "value": "C:/path/to/sticker.png" }
```

Loads a PNG/JPEG, writes it to the Windows clipboard as a bitmap, then sends `CTRL+V`. Invalid images, files over 20 MB, or images over 16 megapixels are rejected before paste.

### Media

```json
{ "type": "media", "value": "play_pause" }
```

Values: `play_pause`, `next`, `previous`, `volume_up`, `volume_down`, `mute`.

`volume_up`, `volume_down` and `mute` operate on the configured master audio backend.

### Master volume / mute

```json
{ "type": "volume", "value": "up" }
{ "type": "volume", "value": "down" }
{ "type": "mute" }
```

Volume actions adjust the configured master audio backend in 5-point steps.

### Launch

```json
{ "type": "launch", "value": "spotify:" }
```

Can launch an executable, file, URL or registered URI.

### Open URL

```json
{ "type": "url", "value": "https://example.com" }
```

The `url` action accepts absolute HTTP/HTTPS URLs and uses the same launch permission as `launch`.

### Page navigation

```json
{ "type": "page", "value": "next" }
{ "type": "page", "value": "previous" }
{ "type": "page", "value": "tabs" }
```

Any other non-empty value is matched against the current profile's `page.id`.

### Shell

```json
{ "type": "shell", "value": "powershell -NoProfile -File C:\\Tools\\x.ps1" }
```

Disabled unless `allowShellCommands=true`.

### Close AdaptiveBarPC

```json
{ "type": "close_app" }
```
