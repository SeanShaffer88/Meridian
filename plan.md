# Meridian UI Audit & Redesign Plan

Audit date: 2026-10-27 · Code state: main `1c13db3` (after PR #55 merge)
Scope: read-only audit of `src/Meridian.App` against the owner's standing design
direction — OneCommander as visual *inspiration only*: dark, professional,
studio-like; compact and information-dense; clearly organized panels; precise
controls; strong hierarchy; prominent work area; restrained accents; uncluttered
dashboard; scannable complex controls; quick common actions; discoverable
advanced controls; familiar labels; clear feedback for selection, progress,
errors, completion; customization where it helps; accessibility across skill
levels, screen sizes, and input methods. Nothing here copies OneCommander's
layout, icons, or branding.

---

## Part 1 — Current UI inventory (verified in code)

### Structure — one window, seven stacked rows

The entire UI is 2 XAML files: `App.xaml` (7 lines) and `MainWindow.xaml`
(128 lines), plus `MainWindow.xaml.cs` (1,142 lines). WinUI 3 / Windows App SDK
2.5.1, .NET 10, minimum Windows 11 (22000), x64 + ARM64 (`Meridian.App.csproj`).

`MainWindow.xaml` root Grid rows, top to bottom:

| Row | Content | Lines |
|---|---|---|
| 0 | Brand header: red logo chip + "MERIDIAN / FILE MANAGER", tagline "Your files. Your workflow.", "PREVIEW BUILD" in red | 14–22 |
| 1 | CommandBar: Copy L→R, Copy R→L, Move L→R, Move R→L, New folder, Rename, Properties, Folder DNA, Delete, separator, Cancel copy, Refresh both, Open in Explorer — all labels visible (`DefaultLabelPosition="Right"`) | 23–38 |
| 2 | Workspace tabs row: hand-built tab buttons in a ScrollViewer + "+ New tab" | 39–42 |
| 3 | Bookmarks row: "★ Add bookmark", "Remove bookmark", "SAVED LOCATIONS" label, bookmarks ComboBox | 43–48 |
| 4 | Drop Shelf row: **ten buttons** (Shelf selected, Remove, Clear, Prune missing, Show location, Preview shelf, Copy shelf…, Cancel batch, Save Capsule, View Capsules) + shelf ComboBox | 49–60 |
| 5 | **Work area (`*` row)**: left pane / 36 px transfer rail (→, ←, "COPY") / right pane | 61–119 |
| 6 | Status bar: green dot, `GlobalStatus` text, "MERIDIAN · EARLY DEVELOPMENT" | 121–126 |

Each pane (identical structure, hard-coded twice in XAML): header
("01 / SOURCE PANE", "02 / DESTINATION PANE"), nav row (← → ↑ buttons + path
TextBox), filter row (filter TextBox + sort ComboBox + descending ToggleButton),
`ListView` (columns: icon, name, size right-aligned, modified right-aligned),
per-pane status line ("Ready").

Secondary UI is all `ContentDialog`s built in code (`MainWindow.xaml.cs`
~404–990): Move item?, Create new folder, Rename item, Properties, Move to
Recycle Bin?, Folder DNA results, Operation Simulator (shelf batch preview),
Confirm batch copy, Batch copy cancelled/stopped, Save Capsule, View Capsules.

### Theming (verified)

- `App.xaml`: `RequestedTheme="Dark"` hard-coded. A `Dark` theme dictionary
  defines 5 named brushes — **and nothing references them**: there are zero
  `ThemeResource`/`StaticResource` usages in the whole app project. Every color
  in `MainWindow.xaml` is a literal hex, repeated.
  - `MeridianAccentBrush` `#F04444` (red) · `MeridianSurfaceBrush` `#17191D`
  - `MeridianPanelBrush` `#202329` · `MeridianBorderBrush` `#343840`
  - `MeridianMutedBrush` `#9CA3AF`
- Accent `#F04444` is spent on: logo chip, "PREVIEW BUILD", pane numbers 01/02,
  and **every file and folder icon glyph in both lists** (folders and files are
  the same red). `#4ADE80` green appears once (status dot). Muted `#9CA3AF` for
  secondary text, `#6B7280` for the footer credit line.
- No Light dictionary, no system-theme following, no user theme choice. The
  Windows system accent is ignored.

### Density (verified values)

- Chrome margins are generous: header `18,14`, content rows `12,*`, panes
  margin `12`, status `14,9`. Five chrome rows sit above the work area before
  any file is visible.
- List row: item `Margin="8,7"`, 17 px icon → ≈31–34 px effective row height.
  Fonts: names at default 14, size/date at 12 muted, status at 11, section
  labels at 10–11 with heavy `CharacterSpacing` (90–100) micro-caps treatment,
  "COPY" rail label at 9.
- Both `ListView`s are `SelectionMode="Single"` — no Ctrl/Shift multi-select.

### Keyboard, navigation, feedback (verified)

- 7 `KeyboardAccelerator`s (XAML lines 5–12): F5 refresh, F6 switch pane,
  Ctrl+L focus path, Ctrl+F focus filter, Alt+Left/Right/Up navigation.
  Core file operations (copy/move/rename/delete) have **no** keyboard route;
  the lists wire no Delete/F2 handling. There are **no context menus** anywhere
  (no `MenuFlyout` in XAML or code).
- Active pane is tracked (`_activePane`, `MainWindow.xaml.cs:29`) and switched
  by pointer press (lines 1089–1092) or F6 — but **nothing visual marks which
  pane is active**: no border, background, or header state change. The active
  workspace *tab* gets only a "▸ " text prefix (line ~145).
- **No progress UI exists**: no `ProgressBar`/`ProgressRing`/`InfoBar` in XAML
  or code. Long copies report through status text plus a Cancel button that
  enables/disables. ~70 status-text writes; errors land in `GlobalStatus` in
  the same muted style as normal messages — no severity color, icon, or
  persistence. Per-pane status lines exist ("Ready").

### Accessibility (verified)

- **Zero `AutomationProperties`** in the app; glyph-content buttons (← → ↑ ↓ ×,
  the "×" tab-close buttons built in code) rely on weak auto-generated names.
- Focus visuals and control rendering are WinUI defaults. No high-contrast
  handling. Footer credit text `#6B7280` on `#202329` at 10 px is low contrast
  (≈3.3:1 by calculation — judgment, not tool-measured).
- Persistence (`Services/WorkspaceStore.cs`, capsules in
  `ContextCapsuleStore`) saves paths/bookmarks/tabs only — **no user
  preferences** (theme, density, pane widths) exist to persist.

### Already on-direction — do not redo (verified)

- Dark, calm surfaces already (`#17191D`/`#202329` + defined border token);
  the work area already owns the only `*` row and panes are equal-width with a
  slim transfer rail.
- CommandBar uses standard labeled AppBarButtons (familiar labels, not
  icon-only mysteries); tooltips already explain the advanced commands
  (Folder DNA, Shelf, Capsules, sort).
- Safety feedback pattern is real: destructive ops confirm, batch copy is
  *simulated and previewed* before it runs, cancel paths exist, failures are
  caught and surfaced as text instead of crashing (catch blocks throughout).
- Workspace tabs, bookmarks, and the Drop Shelf/Capsules are genuine
  organizational features worth keeping — they need organizing, not removal.
- The 5-brush palette in `App.xaml` is a foundation; it is simply unused.

---

## Part 2 — Direction → concrete Meridian changes

**Dark, professional, studio-like** — Keep the existing near-black surfaces;
finish the token system (Slice 1) so the look is *systematic*, then remove the
two loudest non-studio elements: the all-red file icons and the red
"PREVIEW BUILD" shout. Studio-like comes from restraint and alignment, not
from adding decoration.

**Compact, information-dense** — Adopt two density tokens (Slice 2): pane
padding 12→8, row vertical padding 7→4 (≈28 px rows), chrome row margins
12→8. Density must come from tighter spacing, never from smaller than 11 px
text or removed labels.

**Clearly organized panels** — Group the three loose tool rows (tabs,
bookmarks, shelf) into one organized strip (Slice 3): workspace tabs stay a
row; bookmarks and Shelf become two labeled, collapsible sections of a single
"Organize" bar, closed by default except the one last used. Panes get visible
identity: header shows the folder name prominently, path beneath it muted.

**Precise controls** — Replace text-glyph buttons (← → ↑ ↓ ×) with real icon
buttons of one consistent size (32 px nav, 28 px tab-close), one icon style
(WinUI `SymbolIcon`/`FontIcon` from the system font only — no third-party icon
packs, no copied artwork).

**Strong visual hierarchy** — Three text tiers only, tokenized: primary
(default), secondary `#9CA3AF`, tertiary (raise `#6B7280`→`#8A919C` for
contrast). File names primary; size/date secondary; section labels tertiary
11 px SemiBold without the 90–100 character-spacing (keep small caps feel via
weight, drop the tracking noise except the brand wordmark).

**Prominent main work area** — Cut fixed chrome from five rows to three
(header+command merged treatment, tabs row, status bar): bookmarks/shelf move
into the collapsible Organize bar *inside* the tabs row's band or a slim
left edge of the work area. Target: panes start ≤120 px from the window top
at default size (today ≈230 px by layout math).

**Restrained accents** — Accent red is reserved for: brand mark, the *active*
pane's thin top border/header number, primary (default) dialog buttons, and
destructive-action hover/confirm states. File/folder icons return to neutral
(folder glyphs one muted warm tone, files neutral gray; image/code/archive
distinguishable by glyph, not by rainbow). State colors get tokens: success
`#4ADE80`, warning `#F5C044`, error `#F04444`-family reserved for errors —
each always paired with an icon or text label, never color alone.

**Uncluttered dashboard** — Default view shows: header, command bar, tabs,
panes, status. Bookmarks/Shelf/Capsules controls appear when their section is
opened or when the shelf is non-empty (a slim shelf chip with count in the
status area). "PREVIEW BUILD" and the footer credit line are removed from
chrome (version moves to the About/Properties surface); the tagline moves out
of the header.

**Complex controls easy to scan** — The Shelf's ten flat buttons become:
primary actions as buttons (Add, Preview, Copy…), item management in a
split/menu button (Remove, Clear, Prune, Show location), Capsule actions in
their own labeled group. CommandBar: the four directional copy/move buttons
group as two split buttons (Copy ▾ / Move ▾ with direction in the menu) —
halves the bar while keeping every command one menu deep.

**Common actions quick to reach** — Add the missing keyboard routes (Slice 4):
Delete key, F2 rename, Ctrl+A select all in pane, Enter open, plus
right-click context menus on both lists mirroring the CommandBar. When
multi-select lands (Single→Extended), batch counts appear in the pane status.

**Advanced controls organized & discoverable** — Folder DNA, Operation
Simulator, Capsules stay, but under one "Tools" CommandBar group/menu with
their existing plain-word tooltips; nothing advanced lives on the always-on
surface except its entry point.

**Familiar labels, consistent patterns** — Keep current verbs (Copy, Move,
Rename, Properties, Refresh). Apply one dialog pattern: title states the
action, body states consequence + recovery ("Move to Recycle Bin?" already
does this — make the other dialogs match it).

**Clear feedback — selection / progress / errors / completion** — Slice 5:
active pane gets accent top-border + header emphasis; selection count in pane
status ("3 selected · 1.2 GB"); copies show a real progress bar in the status
bar (percent + current file + Cancel); completion posts a summary line that
stays until the next action; errors post in error token + ⚠ icon + the fix or
the path, and are *not* overwritten by the next routine status for at least
the operation that caused them (keep last-error pinned until dismissed or a
new operation starts).

**Customization where it helps** — Slice 6 settings (persisted beside the
workspace store): theme Dark / Light / System (Light dictionary ships in
Slice 1 as tokens even if the UI toggle lands later), density Comfortable
(default) / Compact, follow-Windows-accent toggle (default off — brand red
stays default), pane-split position remembered. Nothing else: no layout
free-form customization.

**Accessibility** — Slice 6: `AutomationProperties.Name` on every
glyph/icon-only control ("Back", "Forward", "Up", "Close workspace tab",
"Copy to right pane"…), active-pane state exposed via header text and focus
order that follows the visual order, contrast fixes (tertiary text raise,
status severities never color-only), focus-visible preserved at 200% scaling,
minimum window size set so both panes stay usable (≈900×600), full keyboard
parity for every CommandBar command.

---

## Part 3 — Buildable slices (reviewable one at a time)

### Slice 1 — Theme & token foundation (no intended visual change)
*User sees:* the same UI, now driven by named resources instead of literals.
*Changes:* complete the token set in `App.xaml` (text tiers, state colors,
active-pane, selection, hover/pressed), convert every literal in
`MainWindow.xaml` to `{ThemeResource …}`, add the `Light` dictionary with the
same keys, dialog code stops using `Microsoft.UI.Colors.Gray` for labels.
*Files:* `App.xaml`, `MainWindow.xaml`, `MainWindow.xaml.cs` (dialog label
brush only).

### Slice 2 — Density & hierarchy pass
*User sees:* noticeably more files on screen; calmer labels; panes feel like
the main event.
*Changes:* spacing tokens applied (pane padding 8, rows ≈28 px, chrome
margins 8), three-tier text tokens applied, character-spacing removed except
brand, folder/file icon colors neutralized (accent removed from list glyphs),
header slimmed (tagline and "PREVIEW BUILD" removed, version to status tooltip).
*Files:* `App.xaml`, `MainWindow.xaml`.

### Slice 3 — Chrome reorganization (dashboard declutter)
*User sees:* three chrome rows instead of five; bookmarks and Shelf tuck into
one labeled Organize strip; the active pane is obvious at a glance.
*Changes:* merge bookmarks+shelf rows into a collapsible Organize bar;
CommandBar copy/move become split buttons; Tools menu groups Folder DNA /
Capsules / Simulator; active-pane top border + header state in XAML/code;
pane header shows folder name primary, path secondary.
*Files:* `MainWindow.xaml`, `MainWindow.xaml.cs` (organize-bar state, split
buttons, active-pane visuals).

### Slice 4 — Command reach: menus, multi-select, shortcuts
*User sees:* right-click menus in both panes, Ctrl/Shift multi-select, Delete
and F2 work, batch counts in pane status.
*Changes:* `SelectionMode="Extended"`, `MenuFlyout` per list mirroring
CommandBar commands, list `KeyDown` handling, accelerators for core ops,
status text updated for multi-item operations; Core batch APIs already accept
lists (verify against `Meridian.Core` before promising counts).
*Files:* `MainWindow.xaml`, `MainWindow.xaml.cs`, possibly
`src/Meridian.Core` service signatures (no UI code there — flag if touched).

### Slice 5 — Feedback system
*User sees:* copies show real progress with percent and current file; errors
look like errors and stay readable; finished operations leave a summary.
*Changes:* status-bar progress region (`ProgressBar` + label + Cancel wired
to the existing cancellation tokens), severity-styled status messages
(icon + token + text), completion summaries, selection readouts; dialog
pattern unified (action title, consequence, recovery).
*Files:* `MainWindow.xaml`, `MainWindow.xaml.cs`.

### Slice 6 — Accessibility & customization settings
*User sees:* a small Settings dialog: theme (Dark/Light/System), density,
accent source; screen readers name every control; high-DPI and small-window
layouts hold up.
*Changes:* `AutomationProperties` pass over XAML + code-built controls
(tab buttons, close buttons, dialog rows), contrast token fixes, persisted
settings store, min window size, theme switching wired to Slice 1 tokens.
*Files:* `App.xaml`, `MainWindow.xaml`, `MainWindow.xaml.cs`,
`Services/WorkspaceStore.cs` (or a sibling `SettingsStore.cs`).

### Explicitly out of scope
- No OneCommander layout/icon/branding imitation; no third-party icon packs.
- No new windows beyond the Settings dialog; dialogs stay `ContentDialog`s.
- No Core file-operation behavior changes except Slice 4's flagged check.
- Runtime look on a real Windows 11 desktop stays Unverified until the owner
  runs each slice — CI build/tests are the automated gate, per repo practice.
