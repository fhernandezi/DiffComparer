# DiffComparer

[Download for Windows](https://github.com/fhernandezi/DiffComparer/releases/latest)

A desktop app for comparing and editing text or source code side by side. Differences update as you type, and you can copy complete sections in either direction without leaving the editors.

Built with **.NET 10** and **Avalonia**. The interface is currently in **Spanish**.

[Guía en español](README.es.md) · [Engine architecture](docs/ENGINE_IMPLEMENTATION.es.md) · [Interface design](docs/UI_DESIGN.es.md)

![DiffComparer showing added, deleted and modified lines in dark mode](docs/images/comparison-dark.png)

*Dark mode. [View the light-mode example](docs/images/comparison-light.png).*

## Features

- **Adjust text for reading.** **Vista → Texto** in comparison and merge offers installed fonts, font size, line spacing, word wrap and zoom. Use Ctrl+wheel, Ctrl++/− and Ctrl+0. Preferences are shared across both windows and saved on normal application exit.
- **Organized toolbar.** File, editing, view and help actions are grouped in dropdowns, with quick navigation above the editors. Open merge from **Herramientas → Merge…**.
- **Selective merge.** Compare original and incoming text live in a separate window. Differences have individual margin checkboxes that apply each section to the result below. [Merge guide and architecture (Spanish)](docs/MERGE.es.md).
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

## Tests

```powershell
dotnet test DiffComparer.slnx -c Release
```

Tests cover the comparison engine, cancellation, file encodings, copy validation, undo/redo, filters, exports and rendered Avalonia windows. UI tests also check themes, contrast, compact layouts, tooltips and workspace recovery; file dialogs and clipboard operations use simulated services.

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
- Folder comparison, three-way merge, incremental recomputation and drag-and-drop are not currently supported.
