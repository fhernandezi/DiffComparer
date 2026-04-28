DiffComparer

A text diff tool focused on human readability, not just minimal edit distance.

Unlike traditional diff tools, this project prioritizes stable alignment and clarity, making differences easier to understand at a glance.

✨ Features
🔍 Line-based diff engine
🧠 Anchor-based alignment (reduces noisy diffs)
⚡ Myers algorithm fallback for precise comparison
✏️ Word-level diff highlighting
📂 Open and compare real files
🖥️ Clean side-by-side UI (Avalonia)
🎯 Philosophy

Most diff tools optimize for:

minimal number of edits

This project optimizes for:

maximum readability for humans

👉 That means:

fewer “jumping” lines
more stable alignment
clearer visual changes
🧪 Example

Instead of breaking everything when a line is inserted:

A
B
C

vs

A
X
B
C

This engine keeps alignment stable:

A
+ X
B


🏗️ How it works (simplified)
Detect anchor lines (exact or similar matches)
Split text into stable segments
Apply Myers diff inside segments
Perform word-level diff for modified lines

🚀 Getting Started
Requirements
.NET (latest recommended)
Avalonia UI
Run
dotnet run

📸 UI
Open left/right files
Compare instantly
Visual diff with colors:
🟢 Added
🔴 Deleted
🟡 Modified

🧠 Roadmap
 Navigation between changes
 “Show only changes” mode
 Drag & drop support
 Performance optimizations
 Moved block detection
 Merge tool (future)
 
⚠️ Known limitations
Large moved blocks are treated as delete + insert
Performance not optimized for very large files yet
No merge functionality (yet)

💡 Why this project?

This was built as an experiment to explore:

Diff algorithms (Myers, LCS)
Heuristic alignment strategies
UI/UX for developer tools

🤝 Contributing

Feedback and ideas are welcome.

If you find a case where the diff looks wrong or confusing, please open an issue.

📜 License

MIT

