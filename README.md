# DiffComparer

[Download for Windows](https://github.com/fhernandezi/DiffComparer/releases/latest)

A desktop app for comparing and editing text or source code side by side. Differences update as you type, and you can copy complete sections in either direction without leaving the editors.

Built with **.NET 10** and **Avalonia**. The interface is currently in **Spanish**.

This guide describes **0.2.0** in the repository. The download link points to the latest published release.

[Guía en español](README.es.md) · [Engine architecture](docs/ENGINE_IMPLEMENTATION.es.md) · [Interface design](docs/UI_DESIGN.es.md)

![DiffComparer showing added, deleted and modified lines in dark mode](docs/images/comparison-dark.png)

*Comparison highlighting in dark mode; these screenshots show an earlier toolbar layout. [Light-mode example](docs/images/comparison-light.png).*

## Features

- **Adjust text for reading.** **Vista → Texto** in comparison and merge offers installed fonts, font size, line spacing, word wrap and zoom. Use Ctrl+wheel, Ctrl++/− and Ctrl+0. Preferences are shared across both windows and saved on normal application exit.
- **Organized toolbar.** File, editing, view and help actions are grouped in dropdowns, with quick navigation above the editors. Open merge from **Herramientas → Merge…**.
- **Selective merge.** Original on the left, incoming on the right and the result below. Independent margin checkboxes let you keep either block, both blocks or neither for each section. [Merge guide and architecture (Spanish)](docs/MERGE.es.md).
- **Compare while editing.** Both panels remain editable. Comparison runs automatically after a 250 ms typing pause, preserving the caret, selection and undo history.
- **Understand each change.** Green marks added lines, red marks deleted lines and yellow marks modified lines. Changed fragments, section brackets and original line numbers help locate the differences.
- **Copy complete sections.** Apply changes from either side, undo or redo copies, and reject actions based on outdated comparisons.
- **Focus your review.** Search both texts, filter change types, adjust surrounding context and mark sections as reviewed. The complete editable text remains available.
- **Work comfortably.** Light, dark and system appearance; resizable panels; keyboard shortcuts; and plain-language tooltips. The presentation is inspired by Material Design 3.
- **Open and save files.** Detect encoding and line endings, choose another encoding when needed, and save either document with its encoding and newline style.
- **Recover from mistakes.** Clear a panel, swap texts or reset the workspace, with one-step recovery of the previous texts and options. Copy the complete right-hand result to the clipboard.
- **Share the comparison.** Export a complete HTML report or a unified patch, independently of the active display filters.

## Run from source

Install the .NET 10 SDK, then run this command from the repository root:

```powershell
dotnet run --project DiffComparer.UI
```

Open a file in each panel or paste your texts. The **left panel is the original**; the right panel is the version being compared. Differences appear automatically. The arrows between the panels copy the entire section indicated by the brackets.

See the [Spanish guide](README.es.md) for comparison options, shortcuts, saving and exports.

## Toolbar and text settings

| Menu | Main actions |
| --- | --- |
| **Archivo** | Open either document, save a copy, export HTML or a patch |
| **Edición** | Undo/redo, copy the selected section, copy complete text, clear, swap and recover texts |
| **Vista** | Light/dark/system appearance and **Texto** settings |
| **Herramientas** | Open the independent **Merge…** window |
| **Ayuda** | Application help |

The quick bar keeps change navigation, review and search within reach. **Opciones** opens comparison settings; **Listo** or Esc closes them.

**Vista → Texto** offers installed fonts, size (8–48 px), line spacing (1–2.5), word wrap and zoom (50–300%). With an editor focused, use Ctrl+wheel or Ctrl++/− to zoom and Ctrl+0 to return to 100%. Settings are shared by comparison and merge and saved on normal application exit. **Restablecer texto** restores presentation defaults while preserving the documents.

## Merge sections

1. Open **Herramientas → Merge…** and open or paste the original and incoming texts in the upper editors. Differences update automatically after a 250 ms typing pause.
2. The result starts with the complete original. Each differing section has a checkbox in each source margin.
3. Check the incoming block while keeping the original checked to include **both, original first and incoming second**. Uncheck the other side to keep only one block; uncheck both to omit the section. Unchanged text remains in the result.
4. Use **Edición** to undo/redo section decisions or include both blocks in the current section. Source edits recalculate the comparison; only uniquely identifiable, unchanged decisions carry forward.
5. Save the result directly as UTF-8 without BOM. For final adjustments, choose **Edición → Editar resultado manualmente**. This locks the sources and checkboxes while you edit the result. **Usar resultado como original** resumes section selection with those edits as the new original.

The merge window and its state are separate from the comparer. Its two-source workflow uses `SectionMergeSession` in the independent `DiffComparer.Merge` module. That module also contains a tested common-base three-way engine; the current window exposes the original/incoming workflow. See the [merge guide](docs/MERGE.es.md) for architecture and details.

## Tests

```powershell
dotnet test DiffComparer.slnx -c Release
```

Tests cover comparison and merge engines, live section selection, cancellation, file encodings, copy validation, undo/redo, filters, exports and rendered Avalonia windows. UI tests also check typography, themes, button hover/focus/disabled states, contrast, compact layouts, tooltips and workspace recovery; file dialogs and clipboard operations use simulated services.

`PerformanceSmokeTests` exercises 20,000 lines. To save screenshots, measurements and patch examples:

```powershell
$env:DIFFCOMPARER_TEST_ARTIFACTS = Join-Path $PWD 'artifacts'
dotnet test DiffComparer.slnx -c Release
```

## Comparison engine

The engine combines readable anchors or minimal Myers line comparison with Unicode grapheme refinement. Editable documents, comparison results and filtered views are kept separate. Background calculations use immutable snapshots and validate document versions before publishing results or applying copies.

Comparison, decoding, filtering, search and report generation run in the background. Recalculation currently recomputes the complete comparison and cancels outdated work; very dissimilar inputs can still be expensive.

Algorithm details, performance tradeoffs and references are documented in the [engine implementation guide](docs/ENGINE_IMPLEMENTATION.es.md).

## Current limitations

- Comparison is textual; it does not interpret the meaning of code or structured data.
- Moved-text detection recognizes only identical, unambiguous sections. Review marks survive recalculation only when their content and immediate context remain uniquely identifiable.
- Patches use one full-context unified hunk, normalize newline style and preserve whether a final newline is present. They are not binary patches.
- Workspace recovery keeps only the most recent clear, swap or reset snapshot. Sessions and review state are not persisted between app runs.
- Combining both blocks concatenates text; review the result for duplicate declarations or other code conflicts and use manual editing when needed.
- The UI does not expose common-base three-way merging or Git conflict/branch management. Folder comparison, incremental recomputation and drag-and-drop are not currently supported.
