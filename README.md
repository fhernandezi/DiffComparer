# DiffComparer

A .NET 10 / Avalonia text and source-code comparison tool focused on readable alignment.

See [the Spanish guide](README.es.md) for the complete workflow, shortcuts, performance strategy and limitations.

## Interface

Material Design 3-inspired presentation on Avalonia, with light/dark/system appearance, plain-language Spanish tooltips, a compact layout, resizable editor panels and secondary settings in a dismissible overlay. Clear, swap and reset actions offer one-step text/settings recovery. Copy result copies the complete right document to the clipboard. The comparison engine is unchanged. See [UI design decisions and verification](docs/UI_DESIGN.es.md).

## Features

- Always-editable side-by-side text editors with file loading, detected encoding, line-ending metadata and full-path tooltips.
- Automatic background comparison after a 250 ms typing pause, with green additions, red deletions and yellow modifications and cancellation of obsolete calculations.
- Exact content comparison by default; optional whitespace normalization and line-ending comparison. Final newline changes are always detected.
- Readable anchor-based or minimal Myers line alignment; exact Unicode grapheme highlighting, including long and split/joined lines.
- Bidirectional hunk-copy arrows with snapshot/version validation, automatic recomputation and undo/redo.
- Save either real document while preserving its encoding, BOM and line endings.
- Original source documents and line numbers; highlighting updates preserve the caret, selection and native undo history.
- Added/deleted/modified line totals and circular change-block navigation (F7 / Shift+F7).
- Filters for difference highlighting, search and navigation, with configurable context and conservative moved-text detection. Text outside the selected context is dimmed; the complete editable text remains visible.
- Debounced search across both visible panels (Ctrl+F, F3 / Shift+F3), with an optional matching-text filter.
- Per-block review tracking, visible review/final-newline markers beside source line numbers, and an unreviewed-only filter.
- Syntax highlighting and visible whitespace.
- Full HTML reports with review information and exact-content unified patch exports, independent of UI filters.

## Run and test

Install the .NET 10 SDK, then:

```powershell
dotnet run --project DiffComparer.UI
dotnet test DiffComparer.slnx -c Release
```

Tests cover engine invariants, cancellation, filtering, encoding validation, exports and headless window interactions. `PerformanceSmokeTests` exercises 20,000 lines. Set `DIFFCOMPARER_TEST_ARTIFACTS` to an output directory to save a UI screenshot, measurements and patch examples.

## Responsiveness

Comparison, decoding, filtering, search and report generation run in the background. Views are published in one update, outdated filter/search jobs are cancelled, and drawing uses row indices and reusable brushes. Anchor selection uses an O(n log n) increasing subsequence.

Bidirectional Myers uses linear frontier memory. Weighted line pairing uses a band for large replacement regions, independently of exact edit ranges. Long-line refinement has no arbitrary coarse-character cutoff. Recalculation uses immutable document snapshots and validates revisions before publishing; it is currently a full cancellable recomputation. See [architecture and provenance](docs/ENGINE_IMPLEMENTATION.es.md).

## Scope

Moved-text detection marks only identical, unambiguous candidates. Review state is retained across recalculation only for unique unchanged blocks and immediate context. Patches use a single full-context unified hunk, normalize line-ending style and preserve the presence of a final newline. Saving requires an explicit destination.

Folder comparison, three-way merge, incremental recomputation, semantic parsing, persistent sessions and drag-and-drop are future work. Very dissimilar inputs may still be computationally expensive.
