# Changelog

## Unreleased

### Added

- Native **AdaptiveBar Studio** workspace: reference-based three-column shell, contextual Explorer menus, profile creation from an application executable, width steppers, and a hardware preview canvas with 10–200% zoom, Fit, anchored Ctrl-wheel zoom and mouse/touch pan. View navigation stays outside profile data and Undo history. See [Studio usage](DESIGNER_STUDIO.md).
- Designer **Undo / Redo** toolbar controls and Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z shortcuts. Keeps 100 session edits across widgets, pages, fixed/Input Tools and theme/background settings; groups pointer drags and typing, preserves saved-state markers and updates global settings when restored. See [Designer history](DESIGNER_HISTORY.md) for session boundaries.
- Designer **Appearance → My Themes**: save named theme copies with bar-preview thumbnails and click to apply them without changing profiles, fixed widgets or Input Tools.
- Portable `.adaptivebar-theme.zip` export/import bundles the theme, preview and original background image/GIF/video. Saved themes keep their own media copies, and duplicate names create a new copy instead of overwriting. Settings-only theme JSON remains available in its own expandable section.

### Fixed

- Media Play/Pause paints its accent border as an overlay, so it no longer shrinks the button face. Pixel transport faces now all measure 54 × 54 DIP inside their 56 × 56 DIP hit areas, with matching spacing and the original raised bevels.
- Restored the original media artwork and transport frames after visual review: Pixel retains its black outlines and raised bevels. Transport/seek hit areas retain 56 DIP height.
- Studio explicitly sets the native Windows 11 caption and text colors so the system accent cannot turn the title bar bright blue. Native window controls remain intact; Windows 10 retains its dark-mode fallback.
- The user-supplied NT logo now appears in the Studio header, window icons and both executable icons, with nine icon sizes from 16 to 256 pixels. The shared vector lives in `Resources/Branding.xaml`; regenerate the ICO with `powershell -NoProfile -File tools/Generate-BrandIcon.ps1`.

- Designer now uses mouse-oriented density: 42-DIP navigation rows, compact toolbar buttons and stacked width steppers. Sidebar icons reflect existing profile metadata and widget configuration; the canvas, chassis, inspector and validation strip follow the Studio reference. Selected tabs no longer make their entire content semibold. The runtime bar retains its touch targets.
- Designer compact layouts keep commands and navigation reachable with native scrolling and visible keyboard focus. Width errors have inline help and diagnostics support keyboard and pointer navigation. Shell accent/error text remains readable independently of the runtime preset.
- Expanded volume and configurable sliders expose only their named functional RangeValue control to UI Automation; decorative tracks no longer advertise a second adjustable control.
- Volume and sensor icons refresh when values, labels, metrics or configured icons change, including Pixel. Button, toggle, slider and sensor glyph visibility and fallback labels also follow icon configuration changes.
- Sliders retain the first touch contact while other controls remain independently usable. Unload releases capture and clears interaction state, flushes the last pending value and allows a fresh gesture after reload.
- Multi-state buttons refresh their icon and text visibility when cycling states or replacing the state list, including Pixel glyphs.
- Sensors treat the configured width as a minimum and expand to fit the readout, so narrow existing profiles retain their numeric value and unit.
- GIF decoding and frame composition now run in the background using Windows imaging, with frozen frames played by a native WPF animation clock. Replaced or unloaded GIFs cancel pending work; motion, visibility and loop controls still apply. Removed the synchronous WpfAnimatedGif dependency.
- Application mixer mute now exposes its app name and on/off state to UI Automation, including changes received from the audio provider.
- Regular sliders and application mixers retain their full 56 DIP touch height. Minimum rendered widths of 160 and 280 DIP preserve at least 72 DIP for the track without rejecting or rewriting older narrow profiles.
- Shared widget text and icons resolve theme colors from their local resources, so Designer previews update with presets and custom colors.
- Media Previous/Next buttons now disable when the active session does not support the corresponding command, or when no session is available.
- Media transport buttons and the seek surface no longer lose their bottom 2 DIP to the enclosing border. The full 56 DIP hit area and button outline remain visible in runtime and Designer, including Pixel bevels.
- UI Automation seek now commits to the media session without a pointer drag; toggle buttons expose their checked state and retain non-activating, single-command dispatch.
- Decorative media widgets now share the effective motion switch with the bar background, including live changes in Designer. Static PNG/JPEG decoding runs off the UI thread and ignores results for replaced sources or unloaded controls.
- Designer primary buttons and checked toggles have readable text across their enabled states. Checked Pixel buttons retain their contrasting face during hover/press.
- Media widgets reserve at least 360 DIP when active and keep a 72-by-56 DIP minimum seek hit area even with long metadata. Existing narrower profile widths remain readable data and render at the minimum; the item strip handles overflow.
- Touch controls now use 56 DIP targets, with a 72 DIP minimum/default bar height and matching Designer preview. Existing height settings below 72 are clamped without rewriting profile data.
- Screen-reader automation can invoke bar buttons without activating the bar or duplicating touch/long-press actions. Inspector fields and icon controls have accessible names; media framing controls show keyboard focus.
- The global `enableAnimations` switch also stops RGB theme motion. Designer helper text has improved contrast, and the window can fit shorter work areas at increased display scaling.
- Designer media/sticker/background imports copy and validate files off the UI thread, show import status and prevent overlapping imports. Changing selection while an import runs does not apply it to another widget.
- Emoji picker now uses 32 bundled Microsoft Fluent Emoji 3D images at 32 DIP, with 56 DIP touch buttons, instead of small theme-font labels. Artwork stays full-color in Pixel and other themes, works offline, and still types the original Unicode emoji without sending Enter. Missing images fall back to a larger emoji-font label.
- Prevent system-wide mouse stalls while AdaptiveBar changes profiles or renders widgets: the touch mouse filter now owns a dedicated message loop, and ordinary mouse input bypasses window-rectangle queries. Touch filtering and non-activating window behavior are preserved. This follows [Microsoft's low-level mouse hook guidance](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc).
- Added a native-input regression check with the window thread kept busy. In a local Windows run, the original hook failed the 500 ms response check (809.8 ms including input delivery); the fixed hook responded in 2.3 ms. These are hook-response measurements, not end-to-end application-switch timings.

## 0.3.2

### Fixed

- **Standard Profile Button Activation Regression**:
  - **Root Cause**:
    1. On non-activating windows (`WS_EX_NOACTIVATE` with `WM_MOUSEACTIVATE -> MA_NOACTIVATE`), WPF's default `ButtonBase` mouse capture fails for promoted touch events (`IsMouseCaptured` is false). As a result, WPF never synthesized an `OnClick()` invocation upon `TouchUp`.
    2. `LongPressButton` intercepted `OnTouchDown` and `OnTouchUp` to manage its hold timer, but never explicitly invoked `OnClick()` or `Command.Execute()` on touch release, falsely expecting WPF to synthesize a mouse click.
    3. In mouse handlers, `if (!_touchActive && e.StylusDevice is null)` was used. On Windows systems with touchscreens or digitizers, tablet/mouse events have a non-null `StylusDevice`, causing mouse clicks to be discarded.
  - **Resolution**:
    - Re-architected `LongPressButton` with a clean, decoupled state machine.
    - Explicitly invokes `RaiseClickFromTouch()` (calling `OnClick()`) on `OnTouchUp` when `!_longPressFired`.
    - Tracks touch release timestamp (`_lastTouchUpTick`) and suppresses synthetic promoted mouse clicks within 250ms, preventing double-firing.
    - Removed `StylusDevice is null` gating so mouse clicks fire reliably on all systems.
    - Preserved long-press secondary action dispatch, primary action suppression, and drag-outside cancellation.
    - Preserved 100% focus immunity (`WS_EX_NOACTIVATE` / `MA_NOACTIVATE`).
- **Regression Test Coverage**:
  - Added automated test suite in `tests/AdaptiveBarPC.Tests` verifying primary command firing, touch promotion suppression, long-press secondary execution, repeated mouse/touch clicks, and profile rebuild command wiring.

## 0.3.1

### Changed

- **Continuous Horizontal Control Surface (Visual Polish Pass)**:
  - Transformed the segmented card/dashboard aesthetic into a sleek, unified Apple Touch Bar / AdaptiveBar style surface.
  - Standardized all widgets (`ButtonItemViewModel`, `SliderItemViewModel`, `MediaItemViewModel`) to uniform 48 px touch height and 12 px pill corner radius.
  - Replaced the loud window border with a subtle, low-opacity highlight (`#25FFFFFF`); accent colors are now reserved for active controls and indicators.
  - Context indicator panel collapsed to a compact header (profile title + page indicator) with a thin vertical divider, removing horizontal clutter.
  - Compact volume slider control: inline pill featuring dynamic speaker glyph (Segoe MDL2), sleek 4 px track, round 16 px thumb, and real-time numeric percentage.
  - Refined media widget: compact 32/38/32 px transport buttons, prominent accent Play/Pause pill, clean two-row track info, and ultra-thin 3 px capsule progress track with zero wasted vertical space.
  - Default bar height reduced to 88 pixels in `AppConfig` and `appsettings.json` while maintaining full touch target accessibility (>=48 px).

### Compatibility

- 100% backward-compatible with all v0.1, v0.2, and v0.3 profiles and configuration schemas.
- Preserved all non-activating window behavior (`WS_EX_NOACTIVATE`), touch focus immunity, and hot-reload.

## 0.3.0

### Added

- **Windows Media Session Widget (`"type": "media"`)**:
  - Horizontal AdaptiveBar/Touch Bar style control displaying active media session.
  - Native integration with Windows Global System Media Transport Controls (`GlobalSystemMediaTransportControlsSessionManager`).
  - Displays track title, artist name, elapsed position, and total duration.
  - Interactive transport controls: Previous, Play/Pause toggle with dynamic Segoe MDL2 glyph (`\uE769` / `\uE768`), and Next.
  - Local wall-clock progress interpolation between discrete GSMTC timeline updates (5 Hz `DispatcherTimer`).
  - Dynamic capability handling: buttons disable automatically when the active session does not support a command (e.g. browser video streams lacking previous/next).
  - Clean empty state (`No media`) when no media session is active or when player closes, without layout jumping.
  - Integrated into the default Desktop profile (`00-desktop.json`).
- **Disposable BarItemViewModel Lifecycle**:
  - `MainViewModel` now cleanly disposes widgets implementing `IDisposable` on page/profile rebuilds and application exit, preventing event or timer leaks.

### Changed

- Updated `TargetFramework` to `net8.0-windows10.0.19041.0` to enable Windows Runtime (WinRT) GSMTC projection APIs.
- Replaced redundant separate Prev/Play/Next buttons in the example Desktop profile with the new unified media widget.

### Compatibility

- Existing v0.1 and v0.2 profiles remain 100% compatible.
- Old media action types (`"type": "media"`, `"value": "play_pause"` etc.) remain fully supported.
- Non-activating window behavior (`WS_EX_NOACTIVATE`) and touch focus immunity are strictly preserved.

## 0.2.2

### Fixed

- **LongPressButton double-fire bug**: After a long press, WPF synthesizes mouse
  events from the touch-up. The old code reset `_longPressFired` during those
  synthetic events, causing the primary action to fire after the secondary already
  had. Fixed by checking `e.StylusDevice is null` in all mouse handlers — promoted
  events are now identified and skipped.
- **LongPressButton touch ghost-state**: Dragging a finger off the button (`OnTouchLeave`)
  did not clear `_touchSequence`, leaving the control permanently stuck in touch-mode
  and ignoring subsequent mouse input. Fixed by clearing `_touchActive` in `OnTouchLeave`.
- **ProfileService silent exception loss**: The `Task.Delay(...).ContinueWith(...)`
  debounce swallowed all exceptions from `Reload()`. Replaced with a proper
  `CancellationTokenSource`-based cancellable debounce; `OperationCanceledException`
  is handled intentionally, all other exceptions are logged.
- **ProfileService multiple simultaneous reloads**: Rapid file-system watcher events
  now collapse into a single reload via debounce cancellation.
- **ProfileService disposal**: `Dispose()` now cancels any in-flight debounce and
  disables the watcher before disposing it.

### Refactored

- Extracted all widget DataTemplates and the `FlexSliderStyle` into
  `Resources/BarControls.xaml` (merged via App.xaml). `MainWindow.xaml` no longer
  owns widget-specific templates.
- `ApplyTheme` parameter type aligned with the non-nullable `ProfileDefinition.Theme`
  property — the redundant `??= new ThemeDefinition()` fallback removed.
- `NativeWindow.WS_EX_NOACTIVATE` and `WS_EX_TOOLWINDOW` changed from `long` to
  `uint` (correct Win32 DWORD type); `unchecked` cast in the 32-bit code path removed.
- Removed dead `target ?? "secondary"` null-coalescing operator in
  `MonitorDockService.SelectScreen` — `AppConfig.TargetMonitor` is non-nullable.
- Added comment in `ActionExecutor.MapKey` explaining the `char → ushort` virtual-key
  code identity for letter and digit keys.

### No behavior or profile format changes

- v0.1 `items[]` profiles and v0.2 `pages[]` profiles load identically.
- All action types, theme fields, and `appsettings.json` keys are unchanged.

## 0.2.1
- Fixed `MouseEventArgs` ambiguity between WinForms and WPF.
- Fixed `Brush` ambiguity between `System.Drawing` and `System.Windows.Media`.
- No behavior/profile format changes from 0.2.0.

## 0.2.0

### Added

- Swipeable multi-page profiles.
- Page name and `x/y` page indicator.
- Left/right fallback page buttons.
- Long-press secondary actions.
- Configurable global and per-control long-press duration.
- Per-profile background, tile, accent, primary text and secondary text colors.
- Fade/slide transition when profile/page changes.
- New example profiles demonstrating pages/themes/long press.

### Compatibility

- v0.1 top-level `items` profiles still load as a single `Main` page.
- Existing action types are unchanged.

### Known limitations

- Volume slider controls Windows master volume, not per-app volume yet.
- Touch swipe behavior depends partly on the touchscreen driver; arrow navigation is always available.
- No GUI profile editor yet.
