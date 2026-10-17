# Architecture — v0.2

## Runtime flow

```text
Windows foreground window
        |
        v
ForegroundAppService
        |
        v
ProfileService <------- Profiles/*.json
        |
        v
MainViewModel
  |         |
  |         +---- page state / theme
  v
MainWindow
  |  non-activating touch surface
  |  swipe + visual transitions
  v
LongPressButton / Slider
        |
        v
ActionExecutor
   |       |        |
   |       |        +--> Process / URI launch
   |       +-----------> Core Audio
   +-------------------> SendInput / media keys
```

## v0.2 additions

### Pages

`ProfileDefinition.Pages` contains one or more `PageDefinition` objects. The ViewModel owns current page index and projects the selected page into `Items`.

Legacy profiles with top-level `Items` are wrapped into a synthetic single `Main` page.

### LongPressButton

A reusable WPF control tracks press duration. It exposes:

- normal `Command` from `Button`;
- `SecondaryCommand`;
- `LongPressMilliseconds`.

If the secondary action fires, the following normal click is suppressed.

### Gesture handling

`MainWindow` owns horizontal swipe gesture interpretation. It calls `MainViewModel.NextPage()` / `PreviousPage()` and does not contain profile-specific logic.

### Theme

Theme values live in the profile data model. `MainViewModel` converts them into WPF brushes and exposes them as bindable state.

### Animation

`MainViewModel.LayoutChanged` is a presentation event. `MainWindow` responds with a short fade/translate animation. Behavior remains correct with animation disabled.

## Core service boundaries

### ForegroundAppService

Reads foreground HWND, process and window title only.

### ProfileService

- Loads JSON.
- Hot reloads profile files.
- Selects the highest-priority match.

It does not execute actions and does not know about WPF controls.

### ActionExecutor

Executes profile-defined actions. It is the only app-level entry point for tap/long-press actions.

### Audio providers

`AudioProviderFactory` selects Windows Core Audio or VoiceMeeter. The `auto`
backend uses VoiceMeeter when its configured bus is reachable and otherwise falls
back to Windows audio. Both providers retain volume and mute in memory when their
backend is unavailable; Windows endpoint discovery is retried on each operation.
`WindowsAudioProvider` releases COM endpoint objects after each operation and
treats `E_NOTFOUND` as a normal endpoint-less state.
Profile file changes are debounced with a one-shot timer; disposal prevents pending reloads.

### MonitorDockService

Positions the window using physical screen bounds and `SetWindowPos`.

---

## v0.2.2 hardening changes

### Widget template extraction

Widget `DataTemplate` objects for `ButtonItemViewModel`, `SliderItemViewModel`,
and `SpacerItemViewModel` (plus the `FlexSliderStyle` and `BooleanToVisibilityConverter`)
have been moved from `MainWindow.xaml` inline resources into:

```
src/AdaptiveBarPC.App/Resources/BarControls.xaml
```

This file is a `ResourceDictionary` merged into `App.xaml`'s `Application.Resources`.

**Convention:** Add a new `DataTemplate DataType="{x:Type vm:FooItemViewModel}"` to
`BarControls.xaml` whenever a new widget ViewModel type is introduced. Do not put
widget templates back into `MainWindow.xaml`.

### ProfileService debounce

`OnProfileChanged` now uses a `CancellationTokenSource`-based debounce pattern
(cancels the previous pending task before starting a new one). `Dispose()` cancels
any in-flight debounce.

### LongPressButton state machine

Mouse handlers guard with `e.StylusDevice is null` to skip WPF-promoted synthetic
mouse events that follow a touch sequence. `OnTouchLeave` clears `_touchActive` to
prevent ghost-state when a finger drags outside the button boundary.

---

## v0.3 additions

### MediaSessionService

Application-wide singleton initialized asynchronously in `App.OnStartup`:
- Wraps Windows Runtime `GlobalSystemMediaTransportControlsSessionManager`.
- Subscribes to `CurrentSessionChanged` and hooks `MediaPropertiesChanged`, `PlaybackInfoChanged`, and `TimelinePropertiesChanged` on the active session.
- Monotonic sequence tagging prevents out-of-order state publication from asynchronous WinRT callbacks.
- Resets to `MediaSessionState.Empty` when no player is active or when an active session terminates.
- Exposes async transport methods (`PlayPauseAsync`, `PreviousAsync`, `NextAsync`) without blocking the UI thread or altering keyboard focus.
- Fully disposed on application exit.

### MediaItemViewModel

WPF ViewModel projecting media state into bindable UI properties:
- Subscribes to `MediaSessionService.StateChanged` and marshals all updates to the UI thread via `Dispatcher`.
- Implements wall-clock progress interpolation: when playing, position advances using elapsed wall-clock time (`DateTimeOffset.UtcNow - _baseTimestamp`) clamped to `[0, Duration]`.
- Driven by a 200 ms (5 Hz) `DispatcherTimer` that runs only while media is active, playing, and duration is valid.
- Commands evaluate `CanExecute` based on reported SMTC session capabilities (e.g. previous/next are disabled for video streams lacking playlist support).
- Implements `IDisposable`: unhooks events and stops timers immediately when disposed.

### Widget Lifecycle in MainViewModel

`MainViewModel.BuildCurrentPage()` disposes any `IDisposable` bar items prior to clearing `Items`, preventing event subscriber accumulation and background timer leaks during page or profile transitions.

### Media UI DataTemplate

Defined in `Resources/BarControls.xaml`:
- Horizontal pill design (16 px corner radius, 76 px height) matching AdaptiveBar aesthetics.
- Large touch targets (48 px high buttons) with `Focusable="False"` to guarantee non-activation.
- Dynamic Segoe MDL2 Assets icons (`\uE768` Play, `\uE769` Pause, `\uE892` Previous, `\uE893` Next).
- Text trimming (`TextTrimming="CharacterEllipsis"`) on title and artist.
- Clean empty state (`No media`) when unavailable, preserving widget dimensions to avoid visual jumping.
- Seeking by touch and album artwork are intentionally deferred to future versions.
