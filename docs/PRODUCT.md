# AdaptiveBarPC

AdaptiveBarPC is an offline contextual Touch Bar / AdaptiveBar-style control surface for Windows. App-specific layouts are user-editable JSON profiles. Version 0.2 uses `pages[]` and preserves legacy top-level `items[]` profiles; unknown fields must not break loading, and invalid profiles are logged without stopping the core.

The runtime bar must remain nonactivating: taps, long presses and swipes must not steal focus. Preserve `WS_EX_NOACTIVATE` and `WM_MOUSEACTIVATE` handling. Long press suppresses the primary action after the secondary action fires. Normal runtime controls target at least 56 DIP in WPF, with horizontal interaction preferred. Animations are optional and theme changes are visual only. Core services work offline, UI callbacks must not block, Win32/COM stays isolated, and shell actions remain disabled by default without hidden elevation.

AdaptiveBar Studio is the native WPF Designer for selecting or creating a profile, choosing pages, adding and arranging widgets, editing properties and appearance, previewing and saving. The approved workflow is Explorer/Library/Input → canvas preview → Properties/Appearance, using real profile data and the existing commands, serializer and history. Save state and diagnostics report actual document state. Preview zoom and pan are editing-view state.

**Explicit Designer exception:** the user primarily edits with a mouse, so the runtime's 56-DIP minimum does not apply to Designer shell controls. The mouse-first density uses smaller rows, fields, buttons and stacked width chevrons while retaining keyboard focus, accessible names and validation feedback. This exception supersedes the earlier blanket Designer touch-target wording; it does not relax the runtime's 56-DIP targets, nonactivation, long-press behavior or JSON compatibility.

The completed mouse-first polish refines the Designer shell: charcoal surfaces, violet cues and Segoe UI text. It preserves that visual direction rather than establishing a new world. `DESIGN.md` documents the shell, with current source authoritative for exact values. Profile appearance and canonical runtime preview templates remain distinct; screenshot example names, content and version labels are not fixed product data.

The Designer models a 1280 × 72-DIP preview. This is a logical editing surface, not a claim about a particular physical device. Available validation evidence for this handoff is offscreen WPF rendering and bitmap DPI scaling only. Physical Win32/nonactivation behavior, multi-touch, mixed-monitor DPI, hardware interaction and live screen-reader operation are not verified by that evidence. This documentation pass runs no build or device tests.

This document records the supported product context and documented architecture invariants. It establishes no new target audience, hardware specification or runtime theme.

