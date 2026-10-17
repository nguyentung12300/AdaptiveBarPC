---
name: "AdaptiveBar Studio — Designer"
description: "Native WPF profile editor with a charcoal workbench and violet interaction cues."
# Scope: AdaptiveBarPC.Designer only. px below serializes WPF device-independent units.
# WPF #AARRGGBB is converted to CSS #RRGGBB (opaque) or rgba(R,G,B,A/255).
# Alpha examples: AccentGlow #407C5CFC; Workbench dot #20FFFFFF;
# canvas glow #A038424F / #2038424F / #0038424F; destructive fill #12FF5C5C.
colors:
  editor-primary-background: "#6242E8"
  editor-primary-hover: "#7052EF"
  editor-primary-pressed: "#5335CD"
  accent-primary: "#7C5CFC"
  editor-accent-text: "#C2B3FF"
  editor-border-focus: "#9F87FF"
  editor-selection-start: "#302951"
  editor-selection-end: "#25273E"
  editor-material-start: "#1E242A"
  editor-material-end: "#181D22"
  accent-glow: "rgba(124, 92, 252, 0.2509803922)"
  editor-background: "#11161B"
  editor-panel: "#181D22"
  editor-panel-raised: "#1D2329"
  editor-input: "#1B2127"
  editor-border: "#343B43"
  primary-text: "#F5F5F7"
  secondary-text: "#A1A1AA"
  text-muted: "#ADB8CA"
  control-hover: "#282834"
  control-pressed: "#343444"
  workbench-background: "#101419"
  workbench-dot: "rgba(255, 255, 255, 0.1254901961)"
  canvas-glow-center: "rgba(56, 66, 79, 0.6274509804)"
  canvas-glow-middle: "rgba(56, 66, 79, 0.1254901961)"
  canvas-glow-edge: "rgba(56, 66, 79, 0)"
  chassis-top: "#39434E"
  chassis-upper: "#20262D"
  chassis-well: "#171C21"
  chassis-bottom: "#303840"
  chassis-border: "#46505B"
  editor-error: "#FF6B6B"
  editor-error-text: "#FFA5AC"
  editor-warning: "#FFC857"
  status-success: "#34D399"
  status-warning: "#FFB020"
  destructive-background: "rgba(255, 92, 92, 0.0705882353)"
  destructive-text: "#FF8A8A"
typography:
  title:
    fontFamily: "Segoe UI"
    fontSize: "14px"
    fontWeight: 600
  body:
    fontFamily: "Segoe UI"
    fontSize: "14px"
    fontWeight: 400
  context:
    fontFamily: "Segoe UI"
    fontSize: "13px"
    fontWeight: 400
  label:
    fontFamily: "Segoe UI"
    fontSize: "14px"
    fontWeight: 400
  label-strong:
    fontFamily: "Segoe UI"
    fontSize: "14px"
    fontWeight: 600
  navigation:
    fontFamily: "Segoe UI"
    fontSize: "13px"
    fontWeight: 400
  navigation-selected:
    fontFamily: "Segoe UI"
    fontSize: "13px"
    fontWeight: 600
  code:
    fontFamily: "Consolas, Segoe UI"
    fontSize: "11px"
    fontWeight: 400
rounded:
  step: "4px"
  menu: "6px"
  control: "8px"
  card: "12px"
  hardware: "16px"
spacing:
  gap-4: "4px"
  gap-6: "6px"
  gap-8: "8px"
  gap-10: "10px"
  gap-12: "12px"
  inset-14: "14px"
  inset-16: "16px"
  inset-20: "20px"
  canvas-24: "24px"
components:
  button-primary:
    backgroundColor: "{colors.editor-primary-background}"
    textColor: "{colors.primary-text}"
    typography: "{typography.label-strong}"
    rounded: "{rounded.control}"
    padding: "6px 14px"
  button-primary-hover:
    backgroundColor: "{colors.editor-primary-hover}"
  button-primary-active:
    backgroundColor: "{colors.editor-primary-pressed}"
  button-standard:
    backgroundColor: "{colors.editor-panel-raised}"
    textColor: "{colors.primary-text}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "5px 12px"
  button-standard-hover:
    backgroundColor: "{colors.control-hover}"
  button-standard-active:
    backgroundColor: "{colors.control-pressed}"
  button-secondary:
    backgroundColor: "{colors.editor-panel-raised}"
    textColor: "{colors.secondary-text}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "4px 10px"
  button-destructive:
    backgroundColor: "{colors.destructive-background}"
    textColor: "{colors.destructive-text}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "4px 10px"
  text-field:
    backgroundColor: "{colors.editor-input}"
    textColor: "{colors.primary-text}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "5px 8px"
  navigation-tab:
    backgroundColor: "transparent"
    textColor: "{colors.text-muted}"
    typography: "{typography.navigation}"
    padding: "8px 10px"
  navigation-tab-selected:
    textColor: "{colors.primary-text}"
    typography: "{typography.navigation-selected}"
  explorer-row:
    backgroundColor: "transparent"
    textColor: "{colors.primary-text}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "0 10px"
  explorer-row-selected:
    textColor: "#FFFFFF"
  inspector-card:
    rounded: "{rounded.card}"
    padding: "16px"
  save-status:
    backgroundColor: "{colors.editor-input}"
    textColor: "{colors.text-muted}"
    typography: "{typography.context}"
    rounded: "{rounded.control}"
    padding: "10px 16px"
  canvas-frame:
    rounded: "{rounded.hardware}"
    padding: "20px 16px"
  width-step:
    backgroundColor: "{colors.editor-panel-raised}"
    textColor: "{colors.primary-text}"
    rounded: "{rounded.step}"
    width: "28px"
    height: "21px"
  studio-header:
    height: "68px"
  studio-status:
    height: "56px"
  chassis-header:
    height: "52px"
---

# Design System: AdaptiveBar Studio — Designer

## Overview

**Creative North Star: "AdaptiveBar Studio workbench"**

The approved Studio workbench is a quiet charcoal editor with violet selection and action cues. Mouse-first control density, clear text and a centered hardware preview support precise editing; the shell stays visually steady while the selected profile changes.

This system applies only to the native WPF Designer. It documents the three-column workspace and its local preview boundary; it establishes no new runtime bar theme. Canonical widget templates and profile appearance resources remain local to the preview. The explicit mouse-first exception in `PRODUCT.md` supersedes the earlier blanket Designer touch floor; current source determines exact tokens.

**Key Characteristics:**

- Charcoal workspace, panels and raised cards with restrained violet emphasis.
- Segoe interface typography, clear labels and visible selection indicators.
- Mouse-first controls with scrollable navigation and inspector content.
- Static canvas depth around a separate, profile-driven runtime preview.

Extracted from current `src/AdaptiveBarPC.Designer/App.xaml`, `MainWindow.xaml`, `MainWindow.Canvas.cs`, `MainWindow.Studio.cs`, `DesignerViewModel.cs` and `StudioItemConverter.cs`. Shared brush aliases were resolved from `src/AdaptiveBarPC.App/Resources/ThemeTokens.xaml` only where used by the Designer. Current source is authoritative for token values. See `PRODUCT.md` for confirmed product context and validation limits.

The frontmatter is normative for the durable rules extracted here. Its `px` values represent WPF DIPs (1/96 inch), not measured device pixels. WPF thicknesses are translated from horizontal/vertical order to CSS top/right/bottom/left order. Minimum heights remain in prose and sidecar extensions/CSS because the component schema has no `minHeight`; fixed heights are recorded only where explicit in source. Gradient stop colors live here; brush geometry, stops and component assignments live in the sidecar rather than pretending a gradient is a flat `backgroundColor`.

The sidecar's HTML/CSS are static browser illustrations of WPF patterns, not a web implementation or runtime-template replacement. Its eight-step OKLCH ramps are generated swatch aids from each token's RGB hue/chroma, not shipped palette scales. For translucent colors they show the uncomposited color with the original alpha.

## Colors

Cool charcoal layers carry the workspace; violet marks active choices and primary actions, with separate functional status colors.

### Primary

- **Primary violet** (`editor-primary-background`, hover and pressed variants): Save and insertion actions.
- **Selection violet** (`accent-primary`): selected tab underlines, list edge indicators and focused fields.
- **Light violet ink** (`editor-accent-text`): accent labels on dark surfaces; **focus violet** (`editor-border-focus`) outlines focused controls.
- **Selection material** (`editor-selection-start`, `editor-selection-end`): opaque horizontal violet gradient for selected rows, checked canvas tools and highlighted menu/stepper states. **Violet edge** (`accent-glow`) is the translucent primary-button border.

### Neutral

- **Workspace charcoal**, **panel charcoal**, **raised charcoal** and **input charcoal** (`editor-background`, `editor-panel`, `editor-panel-raised`, `editor-input`): shell, sidebars, tab strips/buttons and editable wells. Header, status, inspector cards and canvas toolbar use the diagonal `editor-material-start` / `editor-material-end` gradient.
- **Slate border** (`editor-border`): opaque thin separators and control outlines.
- **Primary ink**, **secondary ink**, **muted ink** (`primary-text`, `secondary-text`, `text-muted`): foreground hierarchy; the Designer overrides muted ink locally.
- **Hover charcoal** and **pressed charcoal** (`control-hover`, `control-pressed`): shared standard-button states.
- **Canvas charcoal** and **dot white** (`workbench-background`, `workbench-dot`): the tiled preview workspace. `canvas-glow-*` supplies its continuous radial wash; `chassis-*` supplies the frame's vertical material and border.

### Functional status

- `status-success` and `status-warning` accompany Saved/Unsaved; `editor-warning` marks validation warnings.
- `editor-error` and the lighter `editor-error-text` distinguish error outlines and readable messages.
- `destructive-background` and `destructive-text` identify delete actions. Red is functional feedback, not a second brand accent.

**The Preview Boundary Rule.** Designer shell colors stay stable when a preview preset changes; theme resources belong inside PreviewSurface.

Alpha conversion is essential: XAML's first byte is opacity, while CSS eight-digit hex puts opacity last. The frontmatter uses explicit CSS rgba for translucent values; do not paste XAML eight-digit strings into CSS. Source mappings are recorded in its comments. Alpha ramps and snippets do not establish contrast compliance.

## Typography

**Interface font:** Segoe UI. **Code font:** Consolas with Segoe UI fallback, used for identifiers and numeric/color details.

The hierarchy is compact Windows interface type. No independent display family, modular ratio, line-height or tracking token is established by the source.

### Hierarchy

- **Title:** `typography.title`, inspector group headings.
- **Body / context:** 14-DIP Normal `typography.body` for controls and tab content; 13-DIP Normal `typography.context` for helpers, descriptions and breadcrumb context.
- **Label / strong label:** `typography.label` for 14-DIP Normal inspector field labels; `typography.label-strong` for 14-DIP SemiBold primary buttons.
- **Navigation:** 13-DIP Normal `typography.navigation`, with `typography.navigation-selected` applying SemiBold only to `TabHeader`. `SelectedContent` explicitly resets to 14-DIP Normal; selecting a tab must not embolden its body.
- **Code:** `typography.code`, profile/page identifiers and numeric details.

The app wordmark (18 DIP, Bold), widget header (15 DIP, SemiBold), hardware-surface header (16 DIP, SemiBold) and empty-state heading (20 DIP, SemiBold) are observed local treatments, not a reusable display scale. Explorer section captions and width hints use 12 DIP; inspector action captions also use 12 DIP. Older Library/Input/Appearance captions retain local smaller sizes, not a new body scale. Native Segoe MDL2 icons are permitted by the AdaptiveBar UI guidance; Spacer uses a six-dot native Path template. The all-caps hardware-surface header follows the pinned reference and approved brief; it is a preview-specific detail, not a general heading rule.

**The Interface Type Rule.** Use the observed Segoe interface roles for controls and labels; do not turn a one-off heading into a new display type system.

## Layout

The native window opens at 1600 × 1000 DIP with a 960 × 480 minimum. A 68-DIP header spans Explorer/Library/Input, the flexible canvas, and Properties/Appearance. The status region has a 56-DIP main row and an optional diagnostics area.

At window widths of at least 1200 DIP, the sidebars are 332 and 334 DIP; below that threshold they are 240 and 285 DIP and left-tab text labels collapse. The breadcrumb collapses below 1300 DIP. These are discrete WPF window-width rules, not mobile or browser breakpoints. Sidebar content scrolls.

Spacing follows the extracted gap/inset tokens, not an enforced multiple-of-four grid: six-, ten- and fourteen-DIP adjustments are present. Inspector cards use 16-DIP padding and bottom gap (the selected-widget header overrides padding to 12). The canvas tiles a 16-DIP square with a 0.65-DIP-radius dot centered at 8,8. The chassis has a 24-DIP outer margin, 16-DIP horizontal / 20-DIP vertical padding, and a 52-DIP header followed by a 16-DIP gap. The viewport reserves 72 DIP above and below; the toolbar sits 24 DIP from the canvas top.

The actual preview is 1280 × 72 DIP; framing mode exposes a 1440 × 216 stage while the preview dimensions stay fixed. Fit measures the whole chassis. Manual zoom is clamped to 10–200%; 100% means one content DIP per viewport DIP. Select/Pan, Space-pan, scroll and anchored Ctrl+wheel zoom affect the viewport, not profile appearance.

**The Designer Density Rule.** Designer shell controls are mouse-first and exempt from the runtime's 56-DIP minimum by explicit user decision. Preserve keyboard focus, accessible names and text feedback; runtime touch targets, nonactivation and JSON compatibility remain unchanged.

## Elevation & Depth

Panels use tonal layering and one-DIP outlines; inspector cards, header, status and canvas toolbar share a subtle diagonal LinearGradientBrush (`EditorMaterial`, 0,0 → 1,1). Selection uses a horizontal LinearGradientBrush (`EditorSelection`, 0,0 → 1,0). The chassis uses a vertical gradient, with `chassis-top`, `chassis-upper`, `chassis-well` and `chassis-bottom` stops at 0, 0.06, 0.85 and 1. These effects do not imply animated elevation or blur across the window.

### Shadow Vocabulary

- **Preview chassis:** WPF `DropShadowEffect` with BlurRadius 24, ShadowDepth 8, Opacity 0.45, black.
- **Floating picker:** WPF `DropShadowEffect` with BlurRadius 20, ShadowDepth 4, Opacity 0.85, black.

These native parameters live in the sidecar, outside the frontmatter schema. CSS shadow examples are illustrative approximations of WPF rendering, not equivalent kernels or physical measurements. The canvas wash is a continuous RadialGradientBrush on a noninteractive Border, with RadiusX/RadiusY 0.55, default center/origin 0.5,0.5, and `canvas-glow-center`, `canvas-glow-middle`, `canvas-glow-edge` stops at 0, 0.65 and 1. It fades into the dotted canvas without an ellipse-shaped element edge.

**The Quiet Depth Rule.** Separate panels with tone and thin borders; reserve diffuse shadows for the preview chassis and floating pickers.

State changes in the shared buttons are immediate triggers. No duration/easing scale is established. The ComboBox uses `PopupAnimation="None"`; the polish adds no ungated dropdown animation. Runtime animations continue to respect `enableAnimations`.

## Shapes

Use the frontmatter's gently rounded controls, larger inspector cards and larger hardware chassis. Compact steppers and dropdown items use the smaller step radius; context menus use the menu radius. Borders are normally one DIP.

Keyboard focus has a shared light-violet, two-DIP dashed rectangle with a two-DIP inset and six-DIP corner radius. Some controls also change their own border. Selected Explorer rows add a four-DIP left marker; selected tabs add a two-DIP bottom rule. These cues accompany color.

## Components

### Buttons

Direct mouse-first actions with quiet state changes.

- **Primary:** shared violet state fills, primary ink, strong-label typography and the primary padding token.
- **Standard / secondary:** raised charcoal with shared hover/pressed fills; secondary uses quieter text and tighter padding.
- **Destructive:** translucent red, red text and border; stronger red fill/border on hover and press.
- **States:** focus border plus shared keyboard-focus adorner; shared action buttons dim to opacity 0.38 when disabled.
- **Sizing:** shared action styles use MinHeight 40 DIP; inspector Move Left/Move Right/Delete override this to 66 DIP. Icon utilities use Width/MinHeight 34 DIP with transparent resting background (row menus widen to 44 DIP); canvas tools use 40 DIP. Menu and dropdown rows use MinHeight 34 DIP. Icon-picker tiles remain 56 DIP square as a local utility treatment.

### Inputs / Fields

Dark wells keep editable values distinct from static labels.

Text fields use shared control rounding and input padding with MinHeight 42 DIP. Hover lightens the border; keyboard focus uses selection violet with the source's focused-well fill. Disabled fields dim to opacity 0.45. ComboBoxes retain dark popup rows, the selection gradient and MinHeight 42 DIP (the canvas zoom picker locally uses 40); an open dropdown receives an accent outline. Errors remain adjacent to their field; no invented global error-template token is added.

Width uses stacked up/down Path chevrons inside the numeric field, not −/+ buttons. Each RepeatButton is 28 × 21 DIP, with Delay 400 ms and Interval 90 ms. The stack's one-DIP margin yields a 44-DIP field group; the TextBox's WPF padding is 12,5,38,5. Each step changes one DIP through the existing WidthText/validation/history path; bounds and Auto (0) come from the widget definition. One hold remains one Undo entry.

### Navigation

Underlined tabs and edge-marked rows make location visible.

Tabs retain a 56-DIP minimum in both dimensions, muted idle labels, a faint white hover wash and selected underline/header weight. This is a tab-specific size, not a Designer-wide touch floor. Explorer rows use a 42-DIP minimum height, control rounding, selected gradient and left indicator. Long names trim with tooltips. Menus act on the actual profile/page/widget represented by the row. Profile/page icons come from StudioItemConverter; widget templates use it except for the native six-dot Spacer Path.

### Cards / Containers

Raised, outlined groups organize properties.

Inspector cards use the shared diagonal material gradient, card radius, one-DIP border, padding and bottom gap. They have no shadow. Placement, Layout and Actions are content groups, not separate component themes.

### Chips / Status

The Saved/Unsaved badge is passive status, not a menu. It combines an input-colored rounded well, 13-DIP muted text and a nine-DIP status dot. Its label comes from the real dirty state, including the no-profile state. The validation row uses a 26-DIP circle with radius 13 and a white check, warning or error glyph: normal fill #24845E, warning #65501F, error #763C43. The textual summary remains present. The older 24-DIP quick-color swatches remain local mouse utilities, not a general chip primitive.

### Canvas Preview

A dotted workbench surrounds a hardware-shaped preview.

Keep the outer chassis and canvas toolbar in Designer resources. The inner surface uses canonical runtime widget templates and its own appearance resources. Fit, zoom and pan operate on that framed preview. The passive dimension badge describes the logical preview, not attached hardware. Sidecar examples show the surrounding material only, with no imitation runtime widget design.

## Do's and Don'ts

### Do:

- Do use the Designer semantic brushes for shell surfaces and the lighter accent text brush for violet text.
- Do apply the explicit mouse-first Designer density while retaining keyboard focus visuals, accessible control names and text feedback.
- Do show selection with the existing underline, edge indicator or checked outline as well as color.
- Do let narrow windows scroll and reduce sidebar widths without scaling down the whole editor.
- Do keep runtime appearance resources and canonical widget templates inside the preview boundary.

### Don't:

- Don't promote this Designer palette into a runtime preset or replace profile-specific visuals.
- Don't infer physical pixels or device dimensions from DIP values, canvas zoom or bitmap DPI output.
- Don't convert reference-only version labels, example profile names or decorative menus into product facts.
- Don't apply the Designer density exception to runtime touch targets, nonactivation or JSON compatibility.
- Don't let selected-tab header weight inherit into body content or clip the canvas glow to an ellipse element.

Local utility details (24-DIP quick-color buttons, the 16-DIP checkbox visual and older compact Library/Input/Appearance captions) are not promoted into general primitives or claimed to meet a touch floor. Native Segoe MDL2 icons are allowed platform/fallback details, and the hardware-surface header is a preview-specific detail. Final source corrects body-font inheritance and the canvas oval edge; this refresh records those corrections without claiming a new finish-review score. See `DESIGNER_STUDIO.md` for the builder's final clean Release build, 586/586 tests and refreshed capture run. Offscreen WPF rendering and bitmap DPI scaling do not verify Win32 focus behavior, physical multi-touch, mixed-monitor DPI or live screen-reader operation.

