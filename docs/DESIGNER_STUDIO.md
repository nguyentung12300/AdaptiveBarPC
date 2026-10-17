# AdaptiveBar Studio

AdaptiveBar Studio is the native WPF profile editor. The Studio shell has its own dark palette; the bar preview continues to use the runtime widget templates and the selected Fluid, OLED, Cyber, RGB or Pixel preset.

The completed mouse-first polish refines the Designer shell. Designer controls do not require the runtime's 56-DIP touch floor. Keyboard focus, accessible names and validation feedback remain required. Runtime targets stay at least 56 DIP; nonactivation (`WS_EX_NOACTIVATE` / `WM_MOUSEACTIVATE`), long-press suppression and JSON compatibility are unchanged. This exception supersedes the earlier blanket Designer touch-target wording.

## Workspace

- **Explorer** contains Profiles, Pages and Page Widgets. Use each row's `…` button for actions on that row. Selecting a different profile still prompts to save, discard or cancel when the current profile has unsaved edits.
- **Profiles +** asks for an existing `.exe` and creates a profile with a Main page and an explicit application match. Existing files and IDs are preserved; repeated creation receives a numbered suffix. This does not launch the executable. Use a profile's menu to assign another application or delete it.
- **Pages +** inserts a page. The page menu opens Rename in the inspector, moves the page, or asks before deleting it.
- **Page Widgets +** opens Library. Double-click a widget or drag it onto the preview to insert it. The item menu and inspector share the same move/delete commands.
- **Input** continues to manage shared text slots and stickers. **Appearance** retains presets, custom colors, media framing, saved themes and import/export.

The shell has a 68-DIP header, 332/334-DIP Explorer/inspector panels at wide sizes, 56-DIP tab minima and a 56-DIP status row. Below 1200 DIP, panels become 240/285 DIP and left-tab text collapses; below 1300 DIP, the breadcrumb hides. Rows, text fields and ComboBoxes have 42-DIP minima, standard buttons 40 DIP, utility/menu controls 34 DIP, and inspector Actions 66 DIP. Canvas tools use 40 DIP. Segoe UI body text is 14 DIP Normal; tabs and supporting text use 13 DIP, with selected-tab SemiBold confined to the header. Legacy local caption sizes remain local exceptions; see [design tokens](DESIGN.md).

## Canvas

The initial view is **Fit**. The displayed percentage is the real preview scale in device-independent pixels (DIP), relative to the 1280 × 72 DIP bar. Fit includes the hardware frame and its padding. It recalculates after resizing the viewport or opening the media framing stage.

Use the percentage menu, the zoom buttons, or **Ctrl + mouse wheel** to select a manual scale between 10% and 200%. Mouse-wheel zoom preserves the point under the pointer; the buttons and presets preserve the viewport center where the scroll bounds allow it. Manual scale survives resizing the window. The four-corners button returns to Fit; it does not toggle fullscreen.

Select mode edits widgets. Pan mode drags the viewport, including with native touch scrolling. Hold **Space** temporarily to pan when focus is outside text fields and other controls that use Space; release it to return. **Escape** ends panning and selects the selection tool. Losing capture or deactivating the window also ends an active pan. Scrollbars remain available when the preview exceeds the viewport.

Dragging a widget changes its order; dragging its right edge changes its logical width. Zoom and scroll offsets are included in the coordinate conversion. Preview navigation and selection do not change the JSON or add Undo entries.

## Properties and history

The widget header describes the selected type; Spacer uses a native six-dot template. Placement chooses its page or fixed region. Layout uses the type's actual width bounds; flexible types accept `0` for Auto. Expandable sliders retain their fixed width. Width uses stacked up/down chevrons at the right of the numeric field: each repeat button is 28 × 21 DIP, and their one-DIP outer margin gives the field group a 44-DIP height. The chevrons step by one DIP, repeat while held, and one hold is one Undo entry. Invalid input remains visible with an explanation and blocks saving until corrected.

- **Ctrl+S:** save the profile.
- **Ctrl+Z:** undo an edit.
- **Ctrl+Y / Ctrl+Shift+Z:** redo.
- Search fields and invalid text drafts retain local text undo.

The status bar shows validation and operation results. A green circle with a check marks the normal validation state; warning/error states change both icon and color alongside the text summary. Activate Diagnostics to expand the issue list; press Enter on a selected issue, click its Go to issue button, or double-click it to navigate to the widget. See [history behavior](DESIGNER_HISTORY.md) for the full edit and persistence boundary.

## Verification and limits

Transport seam follow-up: the stale Debug assembly reproduced the reported gaps: button starts at 0/57/114 DIP and Play/Pause margin 1 DIP on each side. After rebuilding both configurations, Debug and Release use starts at 0/56/112 DIP and equal 54-DIP faces. Regression coverage now checks both joins in the actual media template and renders their edges at 100%, 125%, 150%, 200% and 400%. Use the rebuilt executable from the intended configuration when verifying changes; updating Release alone leaves an already-built Debug executable unchanged.

Branding follow-up (2026-09-27): Studio uses the supplied NT logo and an explicit `#181D22` native caption. Both executables embed the same multi-size ICO. The initial targeted `--media-brand` render run is local-only audit output. Native `PrintWindow` captured the caption at RGB 24/29/34; its offscreen WPF client is blank, so the client was verified separately with `RenderTargetBitmap`. The full suite passed 586/586, including media edge hit tests, live colors and session capabilities. Physical touch hardware was not exercised. The user rejected the flattened media styling; the original shared media template has been restored, including Pixel outlines and raised bevels. The initial media captures therefore no longer represent the current styling. Logo and native caption changes remain in place.

Final builder handoff (2026-09-27): Release build completed with **0 warnings and 0 errors**; the full suite passed **586/586 tests** after the final source corrections. This supersedes the preliminary result. Captures were refreshed locally for review; they are not part of the shipped application.

The independent fallback finish review identified three fixes: selected-tab font weight leaking into body content, a visible oval edge on the canvas glow, and stale documentation. Final source scopes SemiBold to `TabHeader`, resets selected content to 14-DIP Normal, and paints a continuous radial brush on a noninteractive Border. The final review cleared both visual fixes with no regressions; this bounded documentation update addresses the remaining documentation fix. No build, render or test was run by this documentation pass.

The redesign is tested with isolated profiles, real WPF control templates and offscreen windows. Canvas checks cover 25%, 50%, 100% and 200%, nonzero scroll offsets, anchored zoom, fitting an expanded media stage, pan capture cleanup, edit history and save/reload. Profile checks cover cancellation, naming collisions and failed writes. Rendering covers wide, medium and compact layouts, the five presets, empty state and invalid drafts.

150% and 200% images use RenderTargetBitmap DPI scaling. They verify vector rendering, not a physical monitor switch. Real AdaptiveBar hardware, mixed-DPI monitor transitions and live multi-touch remain manual checks. Existing runtime long-press issues listed in [the button audit](UI_AUDIT_BUTTONS_2026-09-27.md) are outside this Designer redesign; this work does not claim to fix them.

Build and test:

```powershell
dotnet build AdaptiveBarPC.sln -c Release --no-restore --verbosity minimal
dotnet test AdaptiveBarPC.sln -c Release --no-build --no-restore --verbosity minimal
```

The rendering harness uses copies of the shipped profiles in a unique directory, never the user's live profile files. Captures are written to local ignored review-output directories.
