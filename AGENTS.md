# AGENTS.md — working on «مخازن الشركة الهندسية»

Windows desktop stores (warehouse) program. C# 5 WinForms, Arabic RTL UI, data in an Access/Jet file (.AMR/.mdb)
or a SQL Server Express server on the LAN. Built with the C# compiler that ships with Windows — no SDK, no NuGet.

## Build and test (one line each)

| What | Command |
|---|---|
| Build the program | `build.bat` (→ `Inventory Management.exe`) |
| Build the test programs | `tests\build-tests.bat` (→ `tests\bin\*.exe`) |
| All CI tests on demo data | `powershell -ExecutionPolicy Bypass -File tests\RunAll.ps1` (`-Data <file>`: on a copy of real data) |
| Demo data file | `tests\bin\DemoData.exe tests\demo\empty-template.mdb <demo.mdb>` (fake data; template = schema only, see tests\MakeTemplate.cs) |
| Core tests (Access copy) | `tests\bin\CoreTests.exe <copy.mdb> <outdir>` |
| Core tests (SQL Server) | `tests\bin\CoreTests.exe --sql "(localdb)\Name" <copy.mdb> <outdir>` |
| Year closing | `tests\bin\YearCloseTests.exe <copy.mdb> <outdir>` |
| Cost centres | `tests\bin\CostCentreTests.exe <copy.mdb> <outdir> [(localdb)\Name]` |
| Recipient = cost centre (conversion of old issues) | `tests\bin\RecipientTests.exe <copy.mdb> <outdir>` |
| Write-offs «سندات الإتلاف» (approval, posting at FIFO cost, by-reason report) | `tests\bin\WriteOffTests.exe <copy.mdb> <outdir> [(localdb)\Name]` |
| Month lock + audit trail (before/after) | `tests\bin\PeriodLockTests.exe <copy.mdb> <outdir> [(localdb)\Name]` |
| Screenshots of the main screens | `tests\bin\UiShots.exe <copy.mdb> <outdir> [1366x740 1100x680] [--en]` (login, home, document, items, reports, settings; look at the PNGs) |
| English dictionary / UI layer | `tests\bin\LangTests.exe` (coverage of every Arabic literal, Lang.T, Lang.Apply, reports) |
| User manual images | `tests\bin\ManualShots.exe <fresh demo.mdb> docs\manual\img [only=home,issue-2,…]` (see «User manual» below) |
| Excel export (needs Excel) | `tests\bin\ExcelExportCheck.exe <copy.mdb> <dir>` then `tests\xlcompare.ps1 -Dir <dir>` |
| Every button on every screen | `tests\bin\UiClickTest.exe <copy.mdb> <outdir>` (≈10 min, writes ui_test.log) |
| Print to PDF | `tests\bin\PrintToPdfTest.exe <outdir> <report.html>…` (default printer must be Microsoft Print to PDF) |

**Test only what your change touches** (owner's rule). Run the full set only before a release.
Tests always run on a COPY of a data file — never on the live data.

## Layout (one class per file — keeps parallel work conflict-free)

```
src/App        Program (entry), Settings (settings.ini), Session (user, permissions, mode), Backup, Branding, Common
src/Data       Db (Access + SQL Server, '?' parameters), Cache (items/persons/stores in memory), Migrate (Access → SQL)
src/Domain     Doc model, Posting (save/delete/FIFO/balances), Reservations, YearClose
src/Reports    ReportModel, Reports (all report queries), Html (screen/print), Excel (.xlsx writer), PrintSetup
src/Server     ServerSetup + setup-server.ps1 (one-click SQL Server Express, embedded resource)
src/Update     Updater (GitHub Releases) + apply-update.ps1 (embedded), ProblemReportForm
src/UI         Theme, UI helpers, Controls/*, Search/*, Screens/* (one screen or dialog per file)
tests/         test programs (each = all of src + one file with its own Main)
tools/         MakeIcon.cs (draws src/app.ico), HtmlShot.cs (renders report HTML to PNG), ExtractStrings.cs (Arabic literals → lang-en.txt)
```
`build.bat` compiles `src\**\*.cs` recursively: a new file in src is picked up automatically.

## Rules

- **C# 5 only**: no `$"..."`, `?.`, `nameof`, `=>` members, no `out var`. Target x86 (Jet is 32-bit).
- **SQL**: every value is a `?` parameter (Db rewrites them for SQL Server). Every write goes through `Db.InTx`.
  Inside `Db.InTx` use only the `tx` overloads (`Db.Q/S1/X/Insert(tx, …)`); **never** call `Db.Query/Scalar/Exec/GetSetting`
  there (on Jet they fail and GetSetting silently returns the default). Read settings before the transaction.
- **Original tables** (item, item_stor, sale, sale_menu, sale_menu_1, shop, shop_menu, trans, trans_menu, person, stor,
  type, group, pass, disply_command) are shared with the old Access program: additive changes only
  (`Db.EnsureAppTables` → `Create` / `AddColumn`), and add new tables to `Migrate` (schema + copy order).
- Jet reserved words need brackets: `[group]`, `[type]`, `[note]`; avoid aliases `no`, `at`, `action`.
- **Posting / FIFO / balances / numbering**: change only with a test in `tests/CoreTests.cs`.
- UI text is Arabic and lives in the file that uses it. Identifiers and comments are English.
  Terms: «إخراج مخزني / إدخال مخزني / مردود إخراج مخزني / مردود مشتريات / حجز / نقل مخزني», «السيرفر» for the server
  («السيرفر الرئيسي» / «سيرفر ثانوي»). «مركز الكلفة» (never «المشروع») for what an issue is charged to: stored in table
  `project` / `proj_no` (code names stay Project…), `project.cc_type` 1 مشروع، 2 قسم، 3 صيانة، 4 إدارة، 5 أخرى (NULL = مشروع), see `CostCentres`. No « — » in WinForms texts (it looks like a strike-through in Arabic): use « • » or «،».
- RTL: forms use `RightToLeft.Yes` without `RightToLeftLayout`; use `UI.Field`, `UI.FitHeight`, `FlowDirection.LeftToRight`
  (flows from the right). `ContentAlignment.MiddleLeft` shows right-aligned.
- Files are UTF-8; `.ps1` files UTF-8 **with BOM** (Windows PowerShell 5.1 needs it for Arabic).
- **Never commit** data (*.AMR, *.mdb), `settings.ini`, `errors.log`, `logo.png`, backups, the `.exe`, or `مكونات/`.
- No secrets in code: the repository is public.

## Translations (English UI)

The code stays Arabic; English is a runtime layer (`src/App/Lang.cs`, dictionary `src/App/lang-en.txt`, embedded by
`build.bat` / `tests\build-tests.bat`). Language: settings.ini `Language=ar|en` (asked once on the first start, before
the connection window) and per user `lang_user<no>` in the data; links «English» / «عربي» on the login screen and in the
top bar (the main window reopens in the other language).

- `Lang.T(text)` → English: whole text, then its core (edge spaces/punctuation kept), then known phrases inside it
  («رقم 123» → «No. 123»); unknown text (data) passes through. `Lang.TCell` = whole texts only (grid cells, combo items).
- `Lang.Apply(control)` (called by BaseDialog, MainForm, LoginForm, ReportsWindow, ReportViewerForm) translates texts,
  grid headers, combo items, tool tips and cue banners, turns RightToLeft off, swaps Dock Left/Right, mirrors padding and
  absolute positions; later added controls / changed texts follow automatically. A new top-level `Form` (not a
  BaseDialog) needs `protected override void OnLoad(EventArgs e) { base.OnLoad(e); Lang.Apply(this); }`.
- Owner-drawn controls: paint `Lang.T(text)` with `Lang.Flags(flags)` and `Lang.Mirror(rect, width)`.
  FluentButton / NavButton keep their Arabic `Text` (logic and AutoGlyph read it) and translate when painting.
- Reports: `Html.Render` and `Excel.Write` translate a copy (`Lang.Report`) and switch to left-to-right.
- **New Arabic text in the code**: build and run `tools\ExtractStrings.exe` (see the head of tools\ExtractStrings.cs)
  `tools\ExtractStrings.exe src src\App\lang-en.txt missing.txt`, translate the right side of missing.txt, append it to
  lang-en.txt (UTF-8, `arabic<TAB>english`, `\n` for new lines). `tests\bin\LangTests.exe` fails while a literal has no
  translation. A 4th argument (`unused.txt`) lists keys no literal uses any more: remove them (keep the day names).
  Use absolute paths for the files (the tool and .NET file calls resolve relative paths against the process folder).
  Screens in English: `tests\bin\UiShots.exe <copy.mdb> <outdir> 1366x740 --en [theme=dark]`.
- Terms: Issue, Receipt, Issue return, Purchase return, Reservation, Transfer, Cost centre, Stock count,
  Critical balances, Item card, Document, Store, Party, Recipient, Storekeeper, Server (British spelling).

## User manual (docs/manual)

`docs/manual/index.html` is the illustrated Arabic user manual: one self-contained RTL page (no external files except
`img/*.png`), printable on A4 (each chapter starts a page). It ships in the release zip as `docs/manual/…` next to the exe
(`scripts\package.ps1`; the updater copies sub-folders) and opens with F1 / «الإعدادات ← دليل الاستخدام» (`src/App/Manual.cs`;
without the folder it opens the copy on GitHub, built from `Updater.Repo`).

- **Images are generated, never hand-made:** `tests/ManualShots.cs` builds every screen off-screen on fake demo data, fills
  sample documents, draws the red numbered callouts and saves the PNGs. The repository is public: it refuses a file that
  is not from DemoData and replaces paths, the computer name and the company by neutral text («شركة المثال للمقاولات»).
- **Regenerate after a UI change** (the data file is modified, so always start from a fresh demo file):
  ```
  tests\build-tests.bat
  tests\bin\DemoData.exe tests\demo\empty-template.mdb %TEMP%\manual-demo.mdb
  tests\bin\ManualShots.exe %TEMP%\manual-demo.mdb docs\manual\img            (all, ≈4 min)
  tests\bin\ManualShots.exe %TEMP%\manual-demo.mdb docs\manual\img only=items,item-dialog
  ```
  Then look at the changed PNGs. The numbers on an image are explained in the list under it in index.html: when a
  shot gains or loses a callout, update both. Keep the folder small (now ≈3.7 MB, 56 images): crop to the content area, 1280×720 window.
- New feature → add its shot to ManualShots (one `if (Want("name"))` block) and a section to index.html.
  Placeholders for work in other branches are HTML comments `<!-- TODO: … -->` (none left now).

## Working in parallel

One feature per branch in its own git worktree (`git worktree add ..\wt-<name> -b feature/<name>`).
Prefer new files over growing shared ones; touch `MainForm`/`ToolsView` menus with one added line each.
Commit small, with a message saying what and why; the integrator merges and runs the targeted tests.

## Recipes

- **New report**: add a `ReportDef` line in `Reports.All` (src/Reports/Reports.cs) and its `static ReportData X(ReportParams p)`;
  columns via `r.Col/ColT`, rows via `r.Row`. Html, Excel, print and search/sort work automatically.
- **New screen**: a `UserControl` in `src/UI/Screens/`, one `Nav(...)` line in `MainForm.BuildNav`.
- **New document type**: extend `DocKind`, `Posting` (Head/IdCol/Save/Delete), `DocView`, `Reports.MvSql`; add tests.
- **Release**: bump `Program.Version`, tag `vX.Y.Z`; the release workflow builds, zips and publishes (see .github/workflows).
