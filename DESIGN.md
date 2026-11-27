---
name: MAUI Sherpa
description: Crisp, platform-adaptive Blazor Hybrid design language for cross-platform .NET MAUI developer tooling on macOS, Windows, and Linux GTK.
colors:
  surface-canvas-light: "#ffffff"
  surface-card-light: "#f7f7f8"
  surface-well-light: "#efeff1"
  surface-control-light: "#ffffff"
  surface-popover-light: "#ffffff"
  surface-canvas-dark: "#1e1e1e"
  surface-card-dark: "#272729"
  surface-well-dark: "#202022"
  surface-control-dark: "#323235"
  surface-popover-dark: "#2d2d30"
  text-primary-light: "#18181b"
  text-secondary-light: "#52525b"
  text-muted-light: "#6b6b76"
  text-primary-dark: "#f4f4f5"
  text-secondary-dark: "#c4c4cc"
  text-muted-dark: "#9898a3"
  accent-macos-light: "#007aff"
  accent-macos-dark: "#0a84ff"
  accent-windows-light: "#7c3aed"
  accent-windows-dark: "#b794f4"
  accent-linux-light: "#3584e4"
  accent-linux-dark: "#78aeed"
  status-success-light: "#15803d"
  status-success-dark: "#4ade80"
  status-warning-light: "#b45309"
  status-warning-dark: "#fbbf24"
  status-danger-light: "#b91c1c"
  status-danger-dark: "#f87171"
  status-info-light: "#1d4ed8"
  status-info-dark: "#60a5fa"
typography:
  page-title:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI Variable', 'Adwaita Sans', system-ui, sans-serif"
    fontSize: "1.25rem"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "-0.02em"
  section-title:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI Variable', 'Adwaita Sans', system-ui, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 650
    lineHeight: 1.3
    letterSpacing: "-0.01em"
  body:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI Variable', 'Adwaita Sans', system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.45
    letterSpacing: "normal"
  label:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI Variable', 'Adwaita Sans', system-ui, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 600
    lineHeight: 1.35
    letterSpacing: "0.02em"
  mono:
    fontFamily: "'SF Mono', 'Cascadia Code', 'Adwaita Mono', ui-monospace, Menlo, Monaco, Consolas, monospace"
    fontSize: "0.8125rem"
    fontWeight: 500
    lineHeight: 1.45
    letterSpacing: "normal"
rounded:
  xs: "4px"
  sm: "6px"
  md: "8px"
  lg: "12px"
  xl: "14px"
  pill: "9999px"
spacing:
  1: "4px"
  2: "8px"
  3: "12px"
  4: "16px"
  5: "20px"
  6: "24px"
  8: "32px"
components:
  button-primary:
    backgroundColor: "{colors.accent-macos-light}"
    textColor: "#ffffff"
    rounded: "{rounded.md}"
    padding: "0 12px"
    height: "30px"
  button-secondary:
    backgroundColor: "{colors.surface-control-light}"
    textColor: "{colors.text-primary-light}"
    rounded: "{rounded.md}"
    padding: "0 12px"
    height: "30px"
  card:
    backgroundColor: "{colors.surface-card-light}"
    textColor: "{colors.text-primary-light}"
    rounded: "{rounded.lg}"
    padding: "14px"
  nested-well:
    backgroundColor: "{colors.surface-well-light}"
    textColor: "{colors.text-primary-light}"
    rounded: "{rounded.md}"
    padding: "10px 12px"
---

# Design System: MAUI Sherpa

## Overview

**Creative North Star: "The Precision Basecamp Instrument"**

MAUI Sherpa is an Operate-mode desktop tool for .NET MAUI developers managing Android SDKs, Apple signing assets, simulators, secrets, and .NET runtimes. The interface stays calm, legible, and fast to scan so the tool disappears into the developer's task. Every surface uses pure-neutral tonal layering with crisp 1px hairlines, restrained semantic color, and familiar native desktop ergonomics.

The Blazor Hybrid content shares ~90% of its structure and semantic CSS tokens across platforms, while ~10% of platform tokens (`.platform-macos`, `.platform-windows`, `.platform-linux`) tune accent hue, font family, density, and corner geometry to match the native AppKit, WinUI 3, or GTK4 shell surrounding the WebView.

**Key Characteristics:**
- **Crisp Hairline Tonal** separation — pure neutral surfaces with 1px hairlines, never muddy slate-on-grey clashes.
- **Zero nested card stacks** — nested details step into recessed wells (`--surface-well`) or inset-divider list rows.
- **Custom, keyboard-accessible dropdowns everywhere** — `<SherpaSelect>` replaces every browser-default `<select>`.
- **Platform-faithful accents and geometry** — Apple Blue on macOS, Sherpa Violet on Windows, Adwaita Blue on Linux GTK.
- **Restrained color** — accent and status hues mark primary actions, active selections, and live health states only.

---

## Colors

The palette is built on pure neutral surfaces and text inks paired with platform-native accents and subtle translucent semantic status tints. All theme variables are defined centrally in `src/MauiSherpa/wwwroot/css/app.css`; pages and components must never hardcode hex colors or redeclare surface variables in inline `<style>` blocks.

### Neutral Surface Ladder (Pure Neutral)

Never mix blue-tinted slate greys (`#2d3748`, `#374151`, `#4a5568`, `#24292B`) into neutral dark surfaces.

| Level | CSS Variable | Light (macOS/Linux) | Light (Windows) | Dark (macOS/Linux) | Dark (Windows) | Role |
|---|---|---|---|---|---|---|
| **L0 Canvas** | `--surface-canvas` (alias `--bg-primary`) | `#ffffff` (transparent on macOS) | `#f3f3f3` | `#1e1e1e` (transparent on macOS) | `#202020` | Main scrollable page backdrop |
| **L1 Primary Card** | `--surface-card` (alias `--card-bg`) | `#f7f7f8` / `#ffffff` (GTK) | `#ffffff` | `#272729` / `#303030` (GTK) | `#2b2b2b` | Grouped lists, settings sections, cards |
| **L2 Nested Well** | `--surface-well` (alias `--bg-tertiary`) | `#efeff1` | `#f6f6f7` | `#202022` / `#272727` (GTK) | `#232323` | Expanded child rows, code paths, nested groups |
| **L2 Control Fill** | `--surface-control` (alias `--input-bg`) | `#ffffff` | `#ffffff` | `#323235` | `#333333` | Inputs, secondary buttons, select triggers |
| **L3 Elevated Overlay** | `--surface-popover` (alias `--bg-secondary`) | `#ffffff` | `#ffffff` | `#2d2d30` / `#333333` (GTK) | `#2c2c2c` | Custom dropdown menus, popovers, dialogs |

### Hairline Borders & Text Inks
- **Subtle border (`--border-subtle` / `--border-color`):** `rgba(0, 0, 0, 0.08)` light / `rgba(255, 255, 255, 0.085)` dark — outer card edges and section dividers.
- **Hairline divider (`--border-hairline` / `--border-color-light`):** `rgba(0, 0, 0, 0.06)` light / `rgba(255, 255, 255, 0.065)` dark — inset row separators and nested well edges.
- **Control border (`--border-control` / `--input-border`):** `rgba(0, 0, 0, 0.14)` light / `rgba(255, 255, 255, 0.13)` dark (with slightly stronger `--border-control-bottom` for tactile depth).
- **Text inks (WCAG AA ≥ 4.5:1):**
  - `--text-primary`: `#18181b` light / `#f4f4f5` dark
  - `--text-secondary`: `#52525b` light / `#c4c4cc` dark
  - `--text-muted`: `#6b6b76` light / `#9898a3` dark

### Platform-Native Primary Accent
- **macOS (`.platform-macos`):** Apple Blue — `#007aff` (hover `#0066d6`) light, `#0a84ff` (hover `#3399ff`) dark.
- **Windows (`.platform-windows`):** Sherpa Violet — `#7c3aed` (hover `#6d28d9`) light, `#b794f4` (hover `#c9aef8`, dark ink `#14101f`) dark, synchronized with `WindowsAccentTheme.cs`.
- **Linux GTK (`.platform-linux`):** Adwaita Blue — `#3584e4` (hover `#2974cf`) light, `#78aeed` (hover `#90bef2`, dark ink `#0e1a2b`) dark.
- **Derived accent tokens:** `--accent-soft` (~11–16% alpha), `--accent-border` (~28–36% alpha), `--accent-ring` (focus outline glow).

### Semantic Status Colors
Use translucent tinted fills (`--status-*-bg`), matching hairline borders (`--status-*-border`), and high-contrast foreground inks (`--status-*-text` / `--status-*-fg`) plus 7px status dots (`--status-*-dot`):
- **Success:** `#15803d` light / `#4ade80` dark (healthy, installed, synced, online)
- **Warning:** `#b45309` light / `#fbbf24` dark (expiring soon, update available, pending)
- **Danger:** `#b91c1c` light / `#f87171` dark (expired, failed, conflict, destructive)
- **Info:** `#1d4ed8` light / `#60a5fa` dark (informational notices, pinned SDK/workload badges)

**The Restraint Rule.** Primary accent appears only on primary CTAs, active tabs/selections, focus rings, and links—never as decorative card fills or thick left borders.

---

## Typography

Use each platform's native sans-serif family for all UI text and its native monospace family strictly for code, file paths, versions, serials, UDIDs, and terminal logs. Always apply `font-variant-numeric: tabular-nums` on version numbers, counts, dates, and sizes.

| Platform | Sans Stack (`--font-sans`) | Mono Stack (`--font-mono`) | Base Size |
|---|---|---|---|
| **macOS** | `-apple-system, BlinkMacSystemFont, "SF Pro Text", "SF Pro Display", system-ui, sans-serif` | `"SF Mono", ui-monospace, Menlo, Monaco, Consolas, monospace` | `13px` |
| **Windows** | `"Segoe UI Variable", "Segoe UI", system-ui, sans-serif` | `"Cascadia Code", "Cascadia Mono", Consolas, monospace` | `14px` |
| **Linux GTK** | `"Adwaita Sans", "Cantarell", "Inter", system-ui, sans-serif` | `"Adwaita Mono", "JetBrains Mono", "DejaVu Sans Mono", monospace` | `14px` |

### Hierarchy
- **Page Title (`h1` / `.sherpa-page-title`):** `1.25rem` (`20px` macOS / `22px` Win/Linux), weight `700`, line-height `1.25`, tracking `-0.02em`. Leading icon uses `var(--accent)`. Hidden with `.visually-hidden` only when the native shell titlebar already displays the page title and a section heading leads immediately.
- **Section Title (`h2` / `.sherpa-section-title`):** `0.9375rem`–`1rem` (`14.5px` macOS / `15.5px` Win/Linux), weight `650`, line-height `1.3`, tracking `-0.01em`.
- **Row / Card Title:** `1rem` (`13px` macOS / `14px` Win/Linux), weight `600`, `--text-primary`.
- **Body / Description:** `0.875rem`–`0.92rem`, weight `400`, line-height `1.45`, `--text-secondary` or `--text-muted`, capped at `65ch` measure for prose.
- **Metadata Label (`.sherpa-meta-label` / `.detail-label`):** `0.72rem`–`0.75rem`, weight `600`, `--text-muted`, subtle `0.02em` letter-spacing (sentence case preferred; never mix oversized bold labels with tiny all-caps labels in the same group).

---

## Layout

Preserve each page's established layout and native-toolbar integration (`PlatformInfo.HasNativeToolbar`). Standardize vertical rhythm and internal spacing using the 4px spacing scale (`4 / 8 / 12 / 16 / 20 / 24 / 32px`):

- **Section Rhythm:** More space above a section heading (`22px`–`24px`) than below it (`8px`–`10px`), visually binding each heading to its content.
- **Grouped List Rows:** `10px 14px` padding on macOS, `12px 16px` on Windows and Linux GTK. Rows inside a grouped list (`.sherpa-list`) share a single outer card container and are separated by inset 1px hairlines (`inset-inline: var(--row-padding-x)`).
- **Expandable Groups & Nested Content:** Clicking a group header reveals a recessed `--surface-well` body (`border-top: 1px solid var(--border-hairline)`). Child rows indent (`padding-inline-start: 40px`) and use inset hairlines—never floating rounded cards inside cards.
- **Responsive Wrapping:** Toolbars, filter bars, and row actions use `flex-wrap: wrap` with `8px` gaps; settings rows collapse from 2-column grid (`minmax(170px, 210px) minmax(0, 1fr)`) to single-column below `720px`.

---

## Elevation & Depth

MAUI Sherpa is **flat and hairline-layered at rest**. Depth is communicated through stepped neutral surfaces and crisp 1px borders—not drop shadows on cards.

- **Cards & Lists (L1):** `box-shadow: none; border: 1px solid var(--border-subtle);`
- **Tactile Controls (Secondary Buttons, Select Triggers):** Subtle 1px control border with `--border-control-bottom` and whisper shadow (`0 1px 1px rgba(0,0,0,0.04)` light / `0 1px 1px rgba(0,0,0,0.25)` dark).
- **Elevated Overlays (L3 Popovers, Custom Dropdowns, Dialogs):** `border: 1px solid var(--border-strong); box-shadow: var(--shadow-popover);` (`0 10px 28px rgba(0,0,0,0.12), 0 2px 6px rgba(0,0,0,0.06)` light; `0 14px 36px rgba(0,0,0,0.55), 0 2px 6px rgba(0,0,0,0.35)` dark).
- **Focus Ring:** `:focus-visible` always uses `2px solid var(--accent)` with `2px` offset (never hardcoded `#0078d4`).

---

## Shapes

All 47 legacy `border-radius` values are consolidated into **6 semantic tokens** that adapt automatically per platform while keeping concentric nesting (`inner radius = outer radius - padding`):

| Token | macOS | Windows (Fluent 2) | Linux (Libadwaita) | Usage |
|---|---|---|---|---|
| `--radius-xs` | `4px` | `3px` | `4px` | Inline `<code>` tags, checkboxes, micro-badges |
| `--radius-sm` | `6px` | `4px` | `6px` | `.btn-sm`, icon buttons, dropdown option rows, tabs |
| `--radius-md` | `8px` | `4px` | `8px` | Standard buttons, text inputs, `SherpaSelect` triggers, nested wells, icon wells |
| `--radius-lg` | `12px` | `8px` | `12px` | Cards (`--card-radius`), grouped lists, banners, custom dropdown popovers |
| `--radius-xl` | `14px` | `8px` | `12px` | Modal dialogs and sheets |
| `--radius-pill` | `9999px` | `9999px` | `9999px` | Status badges, count pills, toggle switch tracks |

---

## Components

### 1. Custom Dropdowns (`<SherpaSelect>`)
- **Rule:** Never use raw HTML `<select>` elements anywhere in `src/MauiSherpa`. Always use `<SherpaSelect>`.
- **Behavior:** Trigger button displays an optional leading icon, selected label, and animated chevron (`fa-chevron-down`). Menu panel renders with `position: fixed` via `wwwroot/js/mauiSherpaPopovers.js` so it escapes `overflow: hidden` cards and modals and clamps inside the viewport.
- **Features:** Supports option icons, secondary muted descriptions, status dots, group headers, inline search filtering (`Searchable="true"` for lists with 8+ items), and full keyboard navigation (`ArrowUp`, `ArrowDown`, `Home`, `End`, `Enter`, `Escape`).

### 2. Tabs & Segmented Controls (`<SherpaTabs>` & `<SherpaSegmentedControl>`)
Three standardized patterns cover every tabbed surface:
- **Page Underline Tabs (`<SherpaTabs>`):** Clean 1px bottom hairline rail, crisp 2px active underline bar in `var(--accent)`, leading `fa-fw` icon + label + optional `.sherpa-tab-count` pill and status dot (e.g., updates available).
- **Segmented Control (`<SherpaSegmentedControl>`):** Recessed `--surface-well` track with elevated active segment (`--surface-control` + `--shadow-segment`). Ideal for 2–4 mutually exclusive modes (e.g., Theme: System / Light / Dark; View mode).
- **Inspector Tabstrip (`<SherpaTabs Variant="SherpaTabVariant.Inspector">`):** Compact height (`36px`) strip pairing icon tabs with a compact `<SherpaSelect>` device switcher on the right.

### 3. Buttons
All button classes live globally in `app.css` with consistent heights (`--control-height-md`: `30px` macOS / `32px` Win / `34px` GTK; `--control-height-sm`: `26px` / `28px`) and press feedback:
- `.btn-primary` — Solid platform accent; max one primary CTA per action group.
- `.btn-secondary` — Tactile `--surface-control` with 1px control border.
- `.btn-outline` — Transparent with 1px control border for low-emphasis actions.
- `.btn-ghost` — Transparent at rest, `--surface-card-hover` on hover; use for inline links, toolbar icons, copy/reveal actions.
- `.btn-success` / `.btn-warning` / `.btn-danger` — Semantic actions (install/create, repair/renew, confirmed destructive action).
- `.btn-danger-soft` / `.btn-ghost-danger` — Calm destructive actions for dense list rows (muted at rest, soft danger tint on hover) instead of solid red blocks on every row.
- `.btn-copilot` — Signature indigo-to-violet gradient reserved exclusively for Copilot actions.
- `.btn-icon` — Square icon button (`width == height`) centering a single `fa-fw` glyph with `aria-label` and `title`.

### 4. Lists, Cards & Nested Containers (`<SherpaExpandableGroup>`)
- **Grouped Lists (`.sherpa-list` / `.sherpa-list-row`):** One outer bordered card with inset 1px hairlines between rows. Standard row anatomy: leading tinted icon well (`.sherpa-icon-well`) → primary title + status pills → quiet monospace subtitle with inline copy/reveal buttons → right-aligned actions.
- **Multi-Select Cards & Rows:** Whenever a list or card grid is in multi-select mode (including batch-operation confirmation lists and wizard asset pickers), **clicking anywhere on the entire row or card toggles its checked state** (not just the checkbox). Apply `cursor: pointer`, a clear hover/selected tint, `@onclick:stopPropagation="true"` on the checkbox and any nested interactive controls so clicks never double-toggle, and keyboard toggle support (`Space`/`Enter`).
- **Status Cards (e.g. Dashboard Environment Cards):** Use clean 1px `--border-subtle` + tinted icon well + top-right status pill/dot. Never use thick 3–4px colored `border-left` stripes.
- **Empty & Loading States:** Concise centered card with an icon well, clear title, helpful 1-line explanation, and at most one next action button.

### 5. Headers & Dividers (`<SherpaPageHeader>`, `<SherpaSectionHeader>`)
- **Page Header:** Left-aligned title (with accent icon) + optional 1-line description (`max-width: 65ch`), right-aligned status pills or actions.
- **Section Header:** H2 with muted leading icon, optional count badge, right-aligned section actions (Refresh, Select, View all), optional caption directly beneath.
- **Dividers:** `<hr class="sherpa-divider" />` (`1px solid var(--border-subtle)`). Avoid redundant dividers when spacing already separates sections.

### 6. Iconography
- Standardize 100% on **Font Awesome 6** class prefixes: `fa-solid`, `fa-regular`, `fa-brands` (never legacy `fas`, `far`, `fab`).
- Always include `fa-fw` inside buttons, tabs, dropdown options, and vertical lists for clean alignment.
- Use `<i class="fa-solid fa-xmark" aria-hidden="true"></i>` instead of literal `×` characters for dismiss/clear buttons.
- Use **Tinted Icon Wells** (`.sherpa-icon-well`, `34×34px`, `--radius-md`) with optional `.sherpa-brand-badge` for devices, certificates, keystores, and providers.

### 7. Native Hybrid Dialogs (`FormTheme`, `HybridFormPage`, `HybridViewPage`, `HybridProgressPage`, `ModalLayout`)
Native modal sheets host a native MAUI title header, 1px hairline separators, a BlazorWebView body, and native footer buttons. Both layers must feel like **one seamless surface** with zero color or radius mismatch:
- **Zero-Seam Canvas Match:** `FormTheme.cs` and `ModalLayout.razor` share exact platform-specific pure-neutral backgrounds (`#ffffff` / `#1e1e1e` macOS, `#f3f3f3` / `#202020` Windows, `#ffffff` / `#242424` Linux) and hairline separators (`#e8e8ec` light / `#323236` dark). Never use `#242628` or Tailwind slate greys in `FormTheme.cs`.
- **Platform-Native Accent & Contrast:** `FormTheme.AccentPrimary` and `FormTheme.AccentPrimaryText` follow each OS accent (Apple Blue on macOS, Sherpa Violet on Windows with `#14101f` dark inverse text, Adwaita Blue on Linux with `#0e1a2b` dark inverse text).
- **Consistent Frame & Button Radii:** Native header/footer margins follow one rhythm (`24, 18, 24, 12` header; `24, 12, 24, 18` footer), and native footer buttons use `FormTheme.ControlCornerRadius` (`8` on macOS/Linux, `4` on Windows) matching `--radius-md`.

### 8. Console, Terminal & Log Output Containers (`.sherpa-console`)
All command previews, activity logs, batch operation logs, and `xterm.js` terminals share one recessed dark console grammar so they remain clearly framed in **both** Light and Dark modes (never melting into `#1e1e1e` dark dialogs or wearing light-grey `#ECECEC` headers):
- **Surface Tokens:** `--surface-console: #141416`, `--surface-console-header: #1c1c1f`, `--border-console: rgba(0,0,0,0.16)` light / `rgba(255,255,255,0.10)` dark, `--console-text: #e4e4e7`, `--console-muted: #8e8e98`.
- **Unified Anatomy:** `.sherpa-console` (`border-radius: var(--radius-lg)`, `border: 1px solid var(--border-console)`, `overflow: hidden`) containing a compact dark `.sherpa-console-header` (icon + label + quiet copy/clear actions) and a `.sherpa-console-body` (`var(--font-mono)`, selectable text, `tabular-nums` timestamps, semantic ANSI/log colors).
- **Multi-Operation Step Consoles (`MultiOperationModal`):** Multi-step workflows present operations as a single connected grouped list with collapsible per-operation console logs:
  - **Completed steps auto-collapse** into a compact single-line row showing status icon, step name, status badge (`Completed`), duration, and expand chevron (still clickable to inspect its log).
  - **Active (running) steps auto-expand** to reveal their live `#141416` console log beneath the row, and auto-collapse once they succeed so the next running step takes focus.
  - **Failed steps stay expanded** so the error banner and log tail remain visible immediately.
  - Respect manual user toggles so expanding a completed step (or collapsing a running step) isn't overridden by subsequent state ticks.
- **xterm.js Sync:** `wwwroot/js/terminal.js` uses `#141416` background, `#e4e4e7` foreground, semantic ANSI palette, and cross-platform monospace stack (`"SF Mono", "Cascadia Code", "Adwaita Mono", ui-monospace, ...`).

### 9. Nested Scrolling & Scrollbars
Prevent double scrollbars and accidental outer-page scroll hijacking across modals, inspectors, and dense lists:
- **Single-Scroll-Region Rule in Modals:** Full-height flex modals (`OperationModal`, `ProcessExecutionModal`, `MultiOperationModal`) lock outer overflow (`overflow: hidden` via `.modal-layout:has(...)`) and use `flex: 1; min-height: 0;` so **only** the log, terminal, or operations list scrolls while headers, progress bars, and actions stay pinned. Never stack `overflow-y: auto` on both `.terminal-wrapper` and xterm's viewport.
- **Scroll Containment:** Every nested scrollable container (`.sherpa-console-body`, `.sherpa-select-menu`, `.selection-list`, `.operation-log`, `.sherpa-scroll`) sets `overscroll-behavior: contain;` so hitting the top or bottom never chains scroll to the parent modal or page.
- **Subtle Cross-Platform Scrollbars:** Nested scroll regions use thin, understated scrollbars (`6px`, transparent track, subtle thumb); dark consoles always use dark-tinted translucent thumbs (`rgba(255,255,255,0.20)`) so Windows and Linux never render bright white scrollbar tracks inside dark terminals.

### 10. Toasts & Background Task Center (`ToastContainer`, `BackgroundTaskCenter`)
- **Theme-Aware Popover Toasts:** Toasts use `--surface-popover` with `1px solid var(--border-strong)`, `--radius-lg`, `--shadow-popover`, a 24×24 tinted semantic icon badge (`--status-*-bg/fg`), and a ghost `fa-xmark` dismiss button — never hardcoded dark cards with thick 4px Bootstrap left stripes.
- **Collision-Free Positioning:** Stack toasts cleanly above the bottom-right Background Task Center trigger (`bottom: 4.5rem; right: 1.25rem;`) so notifications and background task controls never overlap.

### 11. Multi-Step Wizards, Data Grids, Forms & Copilot Surfaces
- **Wizards & Steppers (`CISecretsWizard`, `AppleConfigWizardModal`):** Use a crisp horizontal step rail (completed steps show `--status-success-bg/fg` checkmarks, active step uses `--accent`), selectable choice cards (`1px solid var(--border-subtle)`, `--accent-soft` + `var(--accent)` border when selected), and full-row clickable resource selection lists.
- **Bulk Selection Bars & Breadcrumbs:** Use `--accent-soft` + `1px solid var(--accent-border)` for selection mode bars (`Select all`, count badge, bulk actions) and clean icon breadcrumbs with quiet chevron separators in Secrets.
- **Dense Data Grids & Log Tables (`DataGrid`, `LogcatContent`, `SimLogContent`):** Sticky `--surface-well` header rows, 1px hairline column/row dividers, tabular monospace columns, compact severity pills (`V`/`D`/`I`/`W`/`E`), and `overscroll-behavior: contain` scroll bodies.
- **Form Fields & Path Input Groups:** Join monospace path inputs seamlessly with attached Copy / Reveal / Browse action buttons inside a single focusable wrapper, pair invalid inputs with `--status-error-fg` inline recovery copy, and use full-bordered semantic alert banners (no thick left stripes).
- **Empty States, Skeletons & Copilot (`<SherpaListSkeleton>`, `CopilotOverlay`, `CopilotToolResultCard`):** Never display an empty/"none found" state until the initial fetch has actually completed and confirmed zero items. Until then (and during initial loads), render `<SherpaListSkeleton />` matching the target list shape instead of a pulsing spinner box. Once truly empty, compose empty states with a tinted icon well, concise title, 1-line guidance, and at most one primary CTA (use `.sherpa-empty-inline` for compact section empty states). Frame Copilot headers in `--surface-card` (not full-bleed bright accent bars) with a `26×26` signature Copilot icon well, and frame tool-call cards in `--surface-well` with collapsible `--surface-console` output.
- **Row Overflow Menus (`<SherpaActionMenu>`):** Keep each `.sherpa-list-row` right action strip calm and vertically aligned across rows—show at most 1–2 consistent primary actions or status triggers (such as a Cloud Sync picker or Start/Boot/Stop toggle), and tuck conditional, secondary, and destructive actions behind `<SherpaActionMenu>` (`⋮`) backed by `mauiSherpaPopovers.js` so menus never clip or stagger across rows.
- **Structured JSON / Code Editors (`<SherpaJsonEditor>`):** Use `<SherpaJsonEditor>` (backed by the lazy-loaded vendored Monaco bundle in `wwwroot/vendor/monaco` and `js/monacoInterop.js`) for multi-line JSON or structured code editing (APNs/FCM push payloads, simulator push payloads, Google service-account credentials). Its theme is derived from the `--surface-console` / `--console-*` tokens, supports optional JSON schemas (`apns`, `fcm`, `google-service-account`), and falls back cleanly to a plain console `<textarea>` if Monaco fails to load. Keep short prose, PEM keys, and single opaque secrets in standard `.sherpa-textarea`.

### 12. Form Pages & Native Hybrid Form Dialogs (`.sherpa-form`)
All modal creation/edit forms use **Native Hybrid (`HybridFormPage<T>`)** — native titlebar + 1px hairline + Blazor `.sherpa-form` body + 1px hairline + native Cancel/Submit footer — never raw native MAUI `FormPage<T>` controls (`Entry` / `Picker` / `Editor`). This guarantees every dropdown (`<SherpaSelect>`), segmented control (`<SherpaSegmentedControl>`), password/path input group, validation message, and collapsible advanced section shares the exact same design language across macOS, Windows, and Linux GTK:
- **Body Rhythm (`.sherpa-form`):** `padding: 18px 24px 20px; display: flex; flex-direction: column; gap: 16px;`. Never render a duplicate title or Cancel/Submit footer inside a `HybridFormPage` body.
- **Field Anatomy (`.sherpa-field`):** Top `.sherpa-field-header` with `12px` weight-`600` `.sherpa-field-label` + optional subtle `(optional)` tag or right-aligned action link (e.g. `Generate strong`), followed by full-width `.sherpa-input`, `.sherpa-textarea`, `<SherpaSelect>`, `<SherpaSegmentedControl>` (for 2–4 exclusive choices like `iOS | macOS`, `RSA | EC | DSA`, `String | File`), or joined `.sherpa-input-group` (password + eye reveal + copy; path + copy + reveal + browse), and 1-line `.sherpa-field-help` or `.sherpa-field-error` beneath.
- **Responsive Multi-Column Grids (`.sherpa-form-grid-2` / `.sherpa-form-grid-3`):** Pair tightly related short fields (e.g. API Level + Image Variant, City + State + Country) in 2- or 3-column grids that collapse to 1 column below `560px`.
- **Progressive Disclosure (`.sherpa-disclosure`):** Keep primary required fields clean and scannable; tuck optional power-user knobs (Emulator RAM/Storage/SD Card/Skin, Keystore Certificate Subject DN & Key Algorithm) inside collapsible hairline disclosures displaying a live status pill (`Defaults` / `Optional` / `Customized`).
- **Pre-Action Summaries & Repeaters:** Show concise `.sherpa-well` or `.sherpa-banner-info` previews when submission triggers downloads or affects linked profiles (e.g. downloading an AVD system image or migrating provisioning profiles during certificate renewal), and use `.sherpa-kv-row` inside `.sherpa-well` for dynamic key/value metadata repeaters.

---

## Do's and Don'ts

### Do
- **Do** use `<SherpaSelect>` for every dropdown or filter menu.
- **Do** use semantic CSS variables (`--surface-*`, `--border-*`, `--text-*`, `--accent`, `--radius-*`, `--status-*`) in every component and page.
- **Do** keep nested content inside recessed `--surface-well` regions with inset hairlines.
- **Do** use `.btn-ghost-danger` or `.btn-danger-soft` for per-row delete/uninstall buttons inside dense lists.
- **Do** use `.sherpa-console` (`--surface-console: #141416`) for terminals, activity logs, and command boxes, and add `overscroll-behavior: contain` to every nested scrollable panel.
- **Do** keep `FormTheme.cs` synchronized with `app.css` whenever platform surfaces or accents change.
- **Do** validate every UI change in both **Light** and **Dark** themes and consider macOS, Windows, and Linux GTK token behavior.
- **Do** add `aria-label` on icon-only buttons and `aria-hidden="true"` on decorative icons.
- **Do** synchronize native toolbar enabled states via `ToolbarService.SetItemEnabled(...)` immediately after `SetItems(...)` and on every loading/busy/prerequisite change so toolbar buttons are disabled until ready instead of silently ignoring clicks.

### Don't
- **Don't** add raw `<select>` elements or custom per-page dropdown CSS.
- **Don't** hardcode hex colors (`#2d3748`, `#374151`, `#4299e1`, `#e2e8f0`, etc.) or arbitrary pixel/rem `border-radius` values in Razor `<style>` blocks.
- **Don't** nest rounded cards inside rounded cards or stack multiple `overflow-y: auto` wrappers around a single log/terminal container.
- **Don't** use `#1e1e1e` for terminal/console backgrounds (it disappears against dark mode canvas `#1e1e1e`) or glue light-grey headers onto dark log containers.
- **Don't** use thick colored `border-left` stripes (`3px`/`4px`) on cards, alerts, or list items; use status dots and tinted status pills instead.
- **Don't** duplicate theme variable blocks inside `MainLayout.razor` or individual pages—`app.css` is the single source of truth.
- **Don't** mix Font Awesome v5 (`fas`/`fab`/`far`) and v6 (`fa-solid`/`fa-brands`/`fa-regular`) prefixes or use raw `×` characters for close/clear icons.
