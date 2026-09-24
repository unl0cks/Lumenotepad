# Organization and Typing Round Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fonts stop resetting, sentences auto-capitalize, every panel gets a sensible right-click menu, pages can have sub-pages, and the title-bar buttons can live in one of four places with a bubble for hidden panels.

**Architecture:** Typing rules are pure functions in `Editor/TypingRules.cs`, used by `RichTextEditor`. An empty paragraph keeps a `Mark` format inside the model, so `RichDocument.FormatAt` answers correctly everywhere. Sub-pages are a `Level` on each page in the existing ordered list, with all tree logic in the pure `Models/PageTree.cs`. Button placement moves four existing buttons between named host panels.

**Tech Stack:** Avalonia 12.0.5, .NET 10, CommunityToolkit.Mvvm 8.4.0, xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-09-24-organization-and-typing-design.md`

## Global Constraints

- No comments in any code. Nothing in code or files may mention AI or any assistant.
- No em dashes in any file except the app's greeting text.
- No new packages.
- Preserve each file's line endings (patch in binary mode or with Edit).
- Settings keys (verbatim): `AutoCapitalize` (bool, true), `KeepPickedFont` (bool, true), `KeptFont` (string?, null), `ButtonPlacement` (string, "TopLeft"; also "AllTopLeft", "InPanels", "BottomLeft").
- UI strings (verbatim): "Capitalize the first letter of sentences", "Keep the last font I pick", "Button placement", "Top left", "All top left", "Inside the panels", "Bottom left", "New page", "New sub-page", "Make sub-page", "Move up a level", "New section", "New notebook", "Show notebooks", "Show pages".
- Picking a font must never rebuild editors (it would move the caret).
- Target version 1.3.0; release notes carry the unreleased 1.2.15 PDF fix too.

---

### Task 1: Emptied lines remember their format

**Files:**
- Modify: `src/Lumenotepad/Editor/RichModel.cs` (Paragraph fields and Clone, InsertText, SplitParagraphCore, DeleteRange, FormatAt)
- Modify: `src/Lumenotepad/Editor/RichDocJson.cs` (ParaDto, ToDtos, FromDtos)
- Test: `tests/Lumenotepad.Tests/RichModelTests.cs`

**Interfaces:**
- Produces: `RunFormat? Paragraph.Mark`; `RichDocument.FormatAt(pos)` returns `Mark ?? default` for an empty paragraph.

- [ ] **Step 1: Write the failing tests** (append to `RichModelTests`)

```csharp
    private static RunFormat InFont(string f) => new(false, false, false, false, null, null, null, f);

    [Fact]
    public void DeletingAllText_leavesTheLineItsFormat()
    {
        var doc = new RichDocument();
        var end = doc.InsertText(new DocPos(0, 0), "hello", InFont("Gambarino"));
        doc.DeleteRange(new DocPos(0, 0), end);
        Assert.Empty(doc.Paragraphs[0].Runs);
        Assert.Equal("Gambarino", doc.FormatAt(new DocPos(0, 0)).Font);
    }

    [Fact]
    public void BackspacingToEmpty_keepsTheLastLettersFormat()
    {
        var doc = new RichDocument();
        doc.InsertText(new DocPos(0, 0), "ab", InFont("Caveat"));
        doc.DeleteRange(new DocPos(0, 1), new DocPos(0, 2));
        doc.DeleteRange(new DocPos(0, 0), new DocPos(0, 1));
        Assert.Equal("Caveat", doc.FormatAt(new DocPos(0, 0)).Font);
    }

    [Fact]
    public void Enter_givesTheNewEmptyLineTheFormat_andEnterAtTheStartKeepsIt()
    {
        var doc = new RichDocument();
        var end = doc.InsertText(new DocPos(0, 0), "title", InFont("Yuyu"));
        var next = doc.SplitParagraph(end);
        Assert.Equal("Yuyu", doc.FormatAt(next).Font);
        var third = doc.SplitParagraph(next);
        Assert.Equal("Yuyu", doc.FormatAt(third).Font);

        var doc2 = new RichDocument();
        doc2.InsertText(new DocPos(0, 0), "x", InFont("Caveat"));
        doc2.SplitParagraph(new DocPos(0, 0));
        Assert.Equal("Caveat", doc2.FormatAt(new DocPos(0, 0)).Font);
    }

    [Fact]
    public void PlainText_leavesNoMark_andLinksAreNotCarried()
    {
        var doc = new RichDocument();
        var end = doc.InsertText(new DocPos(0, 0), "plain", default(RunFormat));
        doc.DeleteRange(new DocPos(0, 0), end);
        Assert.Null(doc.Paragraphs[0].Mark);

        var linked = new RichDocument();
        var e2 = linked.InsertText(new DocPos(0, 0), "site", default(RunFormat) with { Link = "https://x" });
        linked.DeleteRange(new DocPos(0, 0), e2);
        Assert.Null(linked.Paragraphs[0].Mark);
    }

    [Fact]
    public void Mark_survivesSaveAndLoad_andUndoSnapshots()
    {
        var doc = new RichDocument();
        var end = doc.InsertText(new DocPos(0, 0), "keep", InFont("Gambarino") with { Size = 18 });
        doc.DeleteRange(new DocPos(0, 0), end);

        var back = RichDocJson.FromJson(RichDocJson.ToJson(doc));
        Assert.Equal("Gambarino", back.FormatAt(new DocPos(0, 0)).Font);
        Assert.Equal(18, back.FormatAt(new DocPos(0, 0)).Size);

        var snap = doc.TakeSnapshot();
        doc.InsertText(new DocPos(0, 0), "z", default(RunFormat));
        doc.Restore(snap);
        Assert.Equal("Gambarino", doc.FormatAt(new DocPos(0, 0)).Font);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter RichModelTests`
Expected: build error, `Paragraph` has no `Mark`.

- [ ] **Step 3: Implement the model.** In `Paragraph` add after `public bool Footnote;`:

```csharp
    public RunFormat? Mark;
```

and add `Mark = Mark,` to `Clone()`. In `RichDocument` add:

```csharp
    private static RunFormat? AsMark(RunFormat f)
    {
        f = f with { Link = null };
        return f == default ? null : f;
    }
```

In `InsertText`, after `para.Runs.Insert(runIdx, run);` add `para.Mark = null;`. Replace `SplitParagraphCore` with:

```csharp
    private DocPos SplitParagraphCore(DocPos pos)
    {
        Clamp(ref pos);
        var at = FormatAt(pos);
        var para = Paragraphs[pos.Para];
        int runIdx = para.SplitAt(pos.Off);

        var next = new Paragraph
        {
            Runs = para.Runs.Skip(runIdx).ToList(),
            Bullet = para.Bullet,
            Indent = para.Indent,
            Align = para.Align,
            Style = ParaStyle.Body,
            Footnote = para.Footnote,
        };
        para.Runs.RemoveRange(runIdx, para.Runs.Count - runIdx);
        if (next.Runs.Count == 0) next.Mark = AsMark(at);
        if (para.Runs.Count == 0) para.Mark = next.Runs.Count > 0 ? AsMark(next.Runs[0].Format) : AsMark(at);
        para.Version++;
        Paragraphs.Insert(pos.Para + 1, next);
        return new DocPos(pos.Para + 1, 0);
    }
```

In `DeleteRange`, after the `if (a == b) return;` line add `var removed = FormatStartingAt(a);`, and before the final `OnChanged();` add:

```csharp
        var target = Paragraphs[a.Para];
        if (target.Runs.Count == 0) target.Mark = AsMark(removed);
```

In `FormatAt` change `if (para.Runs.Count == 0) return default;` to `if (para.Runs.Count == 0) return para.Mark ?? default;`.

- [ ] **Step 4: Persist it.** In `RichDocJson.ParaDto` add:

```csharp
        [JsonPropertyName("mk")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RunDto? Mk { get; set; }
```

In `ToDtos` add `Mk = p.Runs.Count == 0 && p.Mark is { } m ? MarkDto(m) : null,`. In `FromDtos`, after the run loop and before `doc.Paragraphs.Add(para);` add `if (para.Runs.Count == 0 && p.Mk is { } mk) para.Mark = MarkFormat(mk);`. Add:

```csharp
    private static RunDto MarkDto(RunFormat f) => new()
    {
        B = f.Bold, I = f.Italic, U = f.Underline, S = f.Strike, Hl = f.Highlight, C = f.Color,
        Fs = f.Size, F = f.Font, Bl = (int)f.Baseline,
    };

    private static RunFormat MarkFormat(RunDto d) =>
        new(d.B, d.I, d.U, d.S, d.Hl, d.C, d.Fs, d.F, (Baseline)d.Bl, null);
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Lumenotepad.Tests -c Release`
Expected: all PASS.

- [ ] **Step 6: Commit**: `git commit -m "Emptied lines remember their font and formatting"`

---

### Task 2: Typing rules and the editor

**Files:**
- Create: `src/Lumenotepad/Editor/TypingRules.cs`
- Modify: `src/Lumenotepad/Editor/RichTextEditor.cs`
- Test: `tests/Lumenotepad.Tests/TypingRulesTests.cs`

**Interfaces:**
- Consumes: `Paragraph.Mark` via `FormatAt` (Task 1).
- Produces: `TypingRules.Resolve(RunFormat, bool, bool, string?)`, `TypingRules.StartsSentence(string)`; statics `RichTextEditor.AutoCapitalizePref`, `KeepPickedFontPref`, `KeptFontPref`, event `RichTextEditor.FontKept (Action<string?>)`.

- [ ] **Step 1: Write the failing tests** `tests/Lumenotepad.Tests/TypingRulesTests.cs`

```csharp
using Lumenotepad.Editor;
using Xunit;

namespace Lumenotepad.Tests;

public class TypingRulesTests
{
    private static readonly RunFormat Plain = default;

    [Fact]
    public void KeptFont_fillsAnEmptyLine_only()
    {
        Assert.Equal("Caveat", TypingRules.Resolve(Plain, true, true, "Caveat").Font);
        Assert.Null(TypingRules.Resolve(Plain, false, true, "Caveat").Font);
        Assert.Null(TypingRules.Resolve(Plain, true, false, "Caveat").Font);
        Assert.Null(TypingRules.Resolve(Plain, true, true, null).Font);
    }

    [Fact]
    public void KeptFont_neverOverridesAFontAlreadyThere_butKeepsTheRestOfTheMark()
    {
        Assert.Equal("Yuyu", TypingRules.Resolve(Plain with { Font = "Yuyu" }, true, true, "Caveat").Font);
        var r = TypingRules.Resolve(Plain with { Size = 18, Bold = true }, true, true, "Caveat");
        Assert.Equal("Caveat", r.Font);
        Assert.Equal(18, r.Size);
        Assert.True(r.Bold);
    }

    [Theory]
    [InlineData("Hello. ", true)]
    [InlineData("Wow!  ", true)]
    [InlineData("Really? ", true)]
    [InlineData("He said \"stop.\" ", true)]
    [InlineData("(done.) ", true)]
    [InlineData("chapter 3. ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Hello.", false)]
    [InlineData("Hello ", false)]
    [InlineData("see e.g. ", false)]
    [InlineData("i.e. ", false)]
    [InlineData("apples, etc. ", false)]
    [InlineData("cats vs. ", false)]
    [InlineData("Dr. ", false)]
    [InlineData("J. ", false)]
    [InlineData("wait... ", false)]
    [InlineData(". ", false)]
    public void StartsSentence(string before, bool expected) =>
        Assert.Equal(expected, TypingRules.StartsSentence(before));
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter TypingRulesTests`
Expected: build error, `TypingRules` not found.

- [ ] **Step 3: Implement** `src/Lumenotepad/Editor/TypingRules.cs`

```csharp
using System;
using System.Collections.Generic;

namespace Lumenotepad.Editor;

public static class TypingRules
{
    public static RunFormat Resolve(RunFormat atCaret, bool emptyParagraph, bool keepFont, string? keptFont)
    {
        if (emptyParagraph && keepFont && atCaret.Font is null && !string.IsNullOrEmpty(keptFont))
            return atCaret with { Font = keptFont };
        return atCaret;
    }

    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "etc", "vs", "cf", "approx", "fig", "mr", "mrs", "ms", "dr", "st", "no", "vol", "pp", "ca",
    };

    public static bool StartsSentence(string before)
    {
        int i = before.Length - 1;
        if (i < 0 || !char.IsWhiteSpace(before[i])) return false;
        while (i >= 0 && char.IsWhiteSpace(before[i])) i--;
        while (i >= 0 && before[i] is '"' or '\'' or '”' or '’' or ')' or ']') i--;
        if (i < 0) return false;
        char term = before[i];
        if (term is '!' or '?') return true;
        if (term != '.') return false;
        int start = i - 1;
        while (start >= 0 && !char.IsWhiteSpace(before[start])) start--;
        string word = before.Substring(start + 1, i - start - 1).TrimStart('"', '\'', '(', '[', '“', '‘');
        if (word.Length == 0 || word.Contains('.')) return false;
        if (word.Length == 1 && char.IsLetter(word[0])) return false;
        return !Abbreviations.Contains(word);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter TypingRulesTests`
Expected: all PASS.

- [ ] **Step 5: Wire the editor.** In `RichTextEditor` add near the other prefs:

```csharp
    public static bool AutoCapitalizePref = true;
    public static bool KeepPickedFontPref = true;
    public static string? KeptFontPref;
    public static event Action<string?>? FontKept;

    private (DocPos At, string Lower)? _autoCap;

    private RunFormat TypingFormat()
    {
        if (_hasPending) return _pending;
        return TypingRules.Resolve(_doc.FormatAt(_caret), _doc.Paragraphs[_caret.Para].Runs.Count == 0,
            KeepPickedFontPref, KeptFontPref);
    }

    private static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
```

In `OnTextInput`, replace

```csharp
        var fmt = _hasPending ? _pending : _doc.FormatAt(_caret);
        _caret = _anchor = _doc.InsertText(_caret, text, fmt);
        _hasPending = false;
```

with

```csharp
        string? autoCapFrom = null;
        if (AutoCapitalizePref && text.Length == 1 && char.IsLower(text[0])
            && TypingRules.StartsSentence(_doc.Paragraphs[_caret.Para].Text[.._caret.Off]))
        {
            autoCapFrom = text;
            text = char.ToUpper(text[0]).ToString();
        }
        var fmt = TypingFormat();
        _caret = _anchor = _doc.InsertText(_caret, text, fmt);
        _hasPending = false;
        _autoCap = autoCapFrom is null ? null : (_caret, autoCapFrom);
```

In `OnKeyDown`, right after `base.OnKeyDown(e);` add:

```csharp
        var autoCap = _autoCap;
        if (!IsModifierKey(e.Key)) _autoCap = null;
```

and replace the undo case `case Key.Z when cmd: Undo(); AfterEdit(pushedUndo: false); break;` with:

```csharp
            case Key.Z when cmd:
                if (autoCap is { } ac && !HasSelection && _caret == ac.At && ac.At.Off > 0)
                {
                    var from = ac.At with { Off = ac.At.Off - 1 };
                    var f = _doc.FormatAt(ac.At);
                    _doc.DeleteRange(from, ac.At);
                    _caret = _anchor = _doc.InsertText(from, ac.Lower, f);
                    AfterEdit(pushedUndo: false);
                    break;
                }
                Undo(); AfterEdit(pushedUndo: false);
                break;
```

If `HandleKeymapShortcut` handles undo before the switch, put the same auto-capital check at the top of its undo branch instead. In the pointer-pressed handler that sets `_hasPending = false`, also set `_autoCap = null;`. In `InsertPlainText` replace `_doc.InsertText(_caret, text, _doc.FormatAt(_caret))` with `_doc.InsertText(_caret, text, TypingFormat())` and add `_hasPending = false;` after it. In `CurrentFormat`, replace `if (!HasSelection) return _doc.FormatAt(_caret);` with `if (!HasSelection) return TypingFormat();`. Replace `ApplyFont` with:

```csharp
    public void ApplyFont(string? family)
    {
        ApplyValue(r => r.Font = family, f => f with { Font = family });
        if (!KeepPickedFontPref) return;
        KeptFontPref = family;
        FontKept?.Invoke(family);
    }
```

- [ ] **Step 6: Build and run all tests**

Run: `dotnet build src/Lumenotepad -c Release` then `dotnet test tests/Lumenotepad.Tests -c Release`
Expected: 0 warnings, all PASS.

- [ ] **Step 7: Commit**: `git commit -m "Keep the picked font for new lines and capitalize sentences"`

---

### Task 3: Typing settings and Preferences switches

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`, `src/Lumenotepad/ViewModels/MainViewModel.cs`, `src/Lumenotepad/Views/MainView.axaml.cs`, `src/Lumenotepad/Views/PreferencesWindow.axaml`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: Task 2 statics and `FontKept`.
- Produces: `MainViewModel.AutoCapitalize`, `KeepPickedFont`, `KeptFont`.

- [ ] **Step 1: Write the failing tests.** Append to `AppSettingsTests`:

```csharp
    [Fact]
    public void TypingSettings_haveTheirDefaults_andRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var d = new AppSettings();
            Assert.True(d.AutoCapitalize);
            Assert.True(d.KeepPickedFont);
            Assert.Null(d.KeptFont);
            new AppSettings { AutoCapitalize = false, KeepPickedFont = false, KeptFont = "Caveat" }.Save(dir);
            var l = AppSettings.Load(dir);
            Assert.False(l.AutoCapitalize);
            Assert.False(l.KeepPickedFont);
            Assert.Equal("Caveat", l.KeptFont);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

Append to `MainViewModelTests`:

```csharp
    [Fact]
    public void TypingSettings_persist_andResetRestoresThem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.AutoCapitalize = false;
            vm.KeepPickedFont = false;
            vm.KeptFont = "Yuyu";
            var saved = AppSettings.Load(dir);
            Assert.False(saved.AutoCapitalize);
            Assert.Equal("Yuyu", saved.KeptFont);
            vm.ResetSettingsToDefaults();
            Assert.True(vm.AutoCapitalize);
            Assert.True(vm.KeepPickedFont);
            Assert.Null(vm.KeptFont);
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter TypingSettings`
Expected: build error.

- [ ] **Step 3: Implement.** `AppSettings` next to `SmartLists`:

```csharp
    public bool AutoCapitalize { get; set; } = true;
    public bool KeepPickedFont { get; set; } = true;
    public string? KeptFont { get; set; }
```

`MainViewModel`: fields `[ObservableProperty] private bool _autoCapitalize = true;`, `[ObservableProperty] private bool _keepPickedFont = true;`, `[ObservableProperty] private string? _keptFont;`; in the settings load block `AutoCapitalize = _settings.AutoCapitalize; KeepPickedFont = _settings.KeepPickedFont; KeptFont = _settings.KeptFont;`; three partial `On…Changed` methods in the `OnSmartListsChanged` pattern writing `_settings.X = value; _settings.Save(_settingsDir);`; and in `ResetSettingsToDefaults` `AutoCapitalize = d.AutoCapitalize; KeepPickedFont = d.KeepPickedFont; KeptFont = d.KeptFont;`.

`MainView.axaml.cs`: add

```csharp
    private void ApplyTypingPrefs()
    {
        if (Vm is not { } vm) return;
        RichTextEditor.AutoCapitalizePref = vm.AutoCapitalize;
        RichTextEditor.KeepPickedFontPref = vm.KeepPickedFont;
        RichTextEditor.KeptFontPref = vm.KeptFont;
    }
```

call it in `HookVm` after `ApplyEditorPrefs(rebuild: false);`, add a branch in `OnVmPropertyChanged`:

```csharp
        else if (e.PropertyName is nameof(MainViewModel.AutoCapitalize) or nameof(MainViewModel.KeepPickedFont))
            ApplyTypingPrefs();
```

and in the constructor `RichTextEditor.FontKept += f => { if (Vm is { } m && m.KeptFont != f) m.KeptFont = f; };`. `KeptFont` must not appear in any list that calls `ApplyEditorPrefs`.

`PreferencesWindow.axaml`, WRITING section, after the date format row:

```xml
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Classes="label" Text="Capitalize the first letter of sentences"/>
                                    <Border Classes="qmark" ToolTip.Tip="After a period, exclamation mark or question mark and a space, the next letter you type becomes a capital. Abbreviations like e.g. and etc. are left alone, and undo right away puts the small letter back."><TextBlock/></Border>
                                </StackPanel>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding AutoCapitalize, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Classes="label" Text="Keep the last font I pick"/>
                                    <Border Classes="qmark" ToolTip.Tip="The font you pick in the toolbar becomes your writing font for new text boxes, new pages and empty lines, until you pick another. Text you already wrote keeps its look."><TextBlock/></Border>
                                </StackPanel>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding KeepPickedFont, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
```

- [ ] **Step 4: Build and run all tests**

Run: `dotnet build src/Lumenotepad -c Release` then `dotnet test tests/Lumenotepad.Tests -c Release`
Expected: 0 warnings, all PASS.

- [ ] **Step 5: Commit**: `git commit -m "Add the auto-capital and keep-font preferences"`

---

### Task 4: Page tree model

**Files:**
- Modify: `src/Lumenotepad/Models/Workspace.cs` (Page)
- Create: `src/Lumenotepad/Models/PageTree.cs`
- Test: `tests/Lumenotepad.Tests/PageTreeTests.cs`

**Interfaces:**
- Produces: `Page.Level (int)`, `Page.Collapsed (bool)`, `[JsonIgnore] Page.HasSubPages`, `Page.IsFoldedAway`, `Page.IndentWidth (double)`; static `PageTree` with `MaxLevel`, `SubtreeEnd`, `CanIndent`, `CanOutdent`, `CanAddSubPage`, `Indent`, `Outdent`, `InsertSubPage`, `InsertAfter`, `RemovePromoting`, `Groups`, `MoveGroup`, `Refresh`, `Normalize`, `VisibleAncestor`.

- [ ] **Step 1: Write the failing tests** `tests/Lumenotepad.Tests/PageTreeTests.cs`

```csharp
using System.Collections.ObjectModel;
using System.Linq;
using Lumenotepad.Models;
using Xunit;

namespace Lumenotepad.Tests;

public class PageTreeTests
{
    private static ObservableCollection<Page> List(params (string T, int L)[] items) =>
        new(items.Select(i => new Page { Title = i.T, Level = i.L }));

    private static string Shape(ObservableCollection<Page> p) => string.Join(" ", p.Select(x => x.Title + x.Level));

    [Fact]
    public void SubtreeEnd_coversDescendantsOnly()
    {
        var p = List(("A", 0), ("a1", 1), ("a2", 2), ("a3", 1), ("B", 0));
        Assert.Equal(4, PageTree.SubtreeEnd(p, 0));
        Assert.Equal(3, PageTree.SubtreeEnd(p, 1));
        Assert.Equal(5, PageTree.SubtreeEnd(p, 4));
    }

    [Fact]
    public void InsertSubPage_goesAfterExistingChildren_andUnfoldsTheParent()
    {
        var p = List(("A", 0), ("a1", 1), ("B", 0));
        p[0].Collapsed = true;
        int at = PageTree.InsertSubPage(p, 0, new Page { Title = "n" });
        Assert.Equal(2, at);
        Assert.Equal("A0 a11 n1 B0", Shape(p));
        Assert.False(p[0].Collapsed);
    }

    [Fact]
    public void InsertAfter_skipsTheSubtree_atTheSameLevel()
    {
        var p = List(("A", 0), ("a1", 1), ("B", 0));
        PageTree.InsertAfter(p, 0, new Page { Title = "n" });
        Assert.Equal("A0 a11 n0 B0", Shape(p));
    }

    [Fact]
    public void Indent_needsAPageAbove_andCarriesDescendants()
    {
        var p = List(("A", 0), ("B", 0), ("b1", 1), ("C", 0));
        Assert.False(PageTree.CanIndent(p, 0));
        PageTree.Indent(p, 1);
        Assert.Equal("A0 B1 b12 C0", Shape(p));
        Assert.False(PageTree.CanIndent(p, 1));
        Assert.False(PageTree.CanIndent(p, 2));
    }

    [Fact]
    public void Indent_refusesToPassTheMaximumDepth()
    {
        var p = List(("A", 0), ("a1", 1), ("a2", 2), ("a3", 2));
        Assert.False(PageTree.CanIndent(p, 3));
        Assert.False(PageTree.CanAddSubPage(p, 2));
        Assert.True(PageTree.CanAddSubPage(p, 1));
    }

    [Fact]
    public void Outdent_carriesDescendants()
    {
        var p = List(("A", 0), ("a1", 1), ("a2", 2), ("B", 0));
        Assert.False(PageTree.CanOutdent(p, 0));
        PageTree.Outdent(p, 1);
        Assert.Equal("A0 a10 a21 B0", Shape(p));
    }

    [Fact]
    public void RemovePromoting_movesChildrenUpALevel()
    {
        var p = List(("A", 0), ("a1", 1), ("a2", 2), ("B", 0));
        PageTree.RemovePromoting(p, 0);
        Assert.Equal("a10 a21 B0", Shape(p));
    }

    [Fact]
    public void Refresh_marksParentsAndFoldedPages()
    {
        var p = List(("A", 0), ("a1", 1), ("a2", 2), ("B", 0), ("b1", 1));
        p[0].Collapsed = true;
        PageTree.Refresh(p);
        Assert.True(p[0].HasSubPages);
        Assert.True(p[1].HasSubPages);
        Assert.False(p[2].HasSubPages);
        Assert.True(p[1].IsFoldedAway);
        Assert.True(p[2].IsFoldedAway);
        Assert.False(p[3].IsFoldedAway);
        Assert.False(p[4].IsFoldedAway);
        Assert.Same(p[0], PageTree.VisibleAncestor(p, p[2]));
        Assert.Same(p[4], PageTree.VisibleAncestor(p, p[4]));
    }

    [Fact]
    public void MoveGroup_movesAPageWithItsSubPages_keepingTheSameObjects()
    {
        var p = List(("A", 0), ("a1", 1), ("B", 0), ("C", 0), ("c1", 1));
        var a1 = p[1];
        PageTree.MoveGroup(p, 0, 2);
        Assert.Equal("B0 C0 c11 A0 a11", Shape(p));
        Assert.Same(a1, p[4]);
        PageTree.MoveGroup(p, 2, 0);
        Assert.Equal("A0 a11 B0 C0 c11", Shape(p));
        PageTree.MoveGroup(p, 0, 1);
        Assert.Equal("B0 A0 a11 C0 c11", Shape(p));
        Assert.Equal(3, PageTree.Groups(p).Count);
    }

    [Fact]
    public void Normalize_repairsImpossibleLevels()
    {
        var p = List(("A", 2), ("B", 3), ("C", -1), ("D", 2));
        PageTree.Normalize(p);
        Assert.Equal("A0 B1 C0 D1", Shape(p));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter PageTreeTests`
Expected: build error.

- [ ] **Step 3: Extend `Page`** in `Workspace.cs`:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndentWidth))]
    private int _level;

    [ObservableProperty] private bool _collapsed;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private bool _hasSubPages;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private bool _isFoldedAway;

    [System.Text.Json.Serialization.JsonIgnore] public double IndentWidth => Level * 14;
```

- [ ] **Step 4: Implement** `src/Lumenotepad/Models/PageTree.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lumenotepad.Models;

public static class PageTree
{
    public const int MaxLevel = 2;

    public static int SubtreeEnd(IList<Page> pages, int index)
    {
        int level = pages[index].Level;
        int i = index + 1;
        while (i < pages.Count && pages[i].Level > level) i++;
        return i;
    }

    public static bool CanIndent(IList<Page> pages, int index)
    {
        if (index <= 0 || index >= pages.Count) return false;
        if (pages[index - 1].Level < pages[index].Level) return false;
        int end = SubtreeEnd(pages, index);
        for (int i = index; i < end; i++) if (pages[i].Level >= MaxLevel) return false;
        return true;
    }

    public static bool CanOutdent(IList<Page> pages, int index) =>
        index >= 0 && index < pages.Count && pages[index].Level > 0;

    public static bool CanAddSubPage(IList<Page> pages, int index) =>
        index >= 0 && index < pages.Count && pages[index].Level < MaxLevel;

    public static void Indent(IList<Page> pages, int index)
    {
        if (CanIndent(pages, index)) Shift(pages, index, +1);
    }

    public static void Outdent(IList<Page> pages, int index)
    {
        if (CanOutdent(pages, index)) Shift(pages, index, -1);
    }

    private static void Shift(IList<Page> pages, int index, int delta)
    {
        int end = SubtreeEnd(pages, index);
        for (int i = index; i < end; i++) pages[i].Level += delta;
        Refresh(pages);
    }

    public static int InsertSubPage(IList<Page> pages, int parentIndex, Page page)
    {
        page.Level = Math.Min(MaxLevel, pages[parentIndex].Level + 1);
        pages[parentIndex].Collapsed = false;
        int at = SubtreeEnd(pages, parentIndex);
        pages.Insert(at, page);
        Refresh(pages);
        return at;
    }

    public static int InsertAfter(IList<Page> pages, int index, Page page)
    {
        page.Level = pages[index].Level;
        int at = SubtreeEnd(pages, index);
        pages.Insert(at, page);
        Refresh(pages);
        return at;
    }

    public static void RemovePromoting(IList<Page> pages, int index)
    {
        int end = SubtreeEnd(pages, index);
        for (int i = index + 1; i < end; i++) pages[i].Level--;
        pages.RemoveAt(index);
        Refresh(pages);
    }

    public static List<(int Start, int End)> Groups(IList<Page> pages)
    {
        var groups = new List<(int, int)>();
        int i = 0;
        while (i < pages.Count)
        {
            int end = i + 1;
            while (end < pages.Count && pages[end].Level > 0) end++;
            groups.Add((i, end));
            i = end;
        }
        return groups;
    }

    public static void MoveGroup(ObservableCollection<Page> pages, int from, int to)
    {
        var groups = Groups(pages);
        if (from == to || from < 0 || to < 0 || from >= groups.Count || to >= groups.Count) return;
        var (s, e) = groups[from];
        int k = e - s;
        int dest;
        if (to < from) dest = groups[to].Start;
        else dest = groups[to].End - k;
        if (dest < s)
            for (int i = 0; i < k; i++) pages.Move(s + i, dest + i);
        else
            for (int i = 0; i < k; i++) pages.Move(s, dest + k - 1);
        Refresh(pages);
    }

    public static void Refresh(IList<Page> pages)
    {
        int? foldLevel = null;
        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            if (foldLevel is { } fl && p.Level <= fl) foldLevel = null;
            p.IsFoldedAway = foldLevel is not null;
            p.HasSubPages = i + 1 < pages.Count && pages[i + 1].Level > p.Level;
            if (foldLevel is null && p.Collapsed && p.HasSubPages) foldLevel = p.Level;
        }
    }

    public static void Normalize(IList<Page> pages)
    {
        int prev = -1;
        foreach (var p in pages)
        {
            int max = Math.Min(MaxLevel, prev + 1);
            p.Level = Math.Clamp(p.Level, 0, max);
            prev = p.Level;
        }
        Refresh(pages);
    }

    public static Page VisibleAncestor(IList<Page> pages, Page page)
    {
        int i = pages.IndexOf(page);
        if (i < 0) return page;
        var cur = page;
        for (int j = i - 1; j >= 0 && cur.IsFoldedAway; j--)
            if (pages[j].Level < cur.Level) cur = pages[j];
        return cur;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter PageTreeTests`
Expected: all PASS.

- [ ] **Step 6: Commit**: `git commit -m "Add the page tree model for sub-pages"`

---

### Task 5: Sub-page commands in the view model

**Files:**
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Test: `tests/Lumenotepad.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: Task 4 `PageTree`.
- Produces: `MainViewModel.NewPageAfter(Page)`, `NewSubPage(Page)`, `MakeSubPage(Page)`, `MoveUpALevel(Page)`, `ToggleFold(Page)`, `RefreshPageTree()`; `DeletePage` promotes sub-pages.

- [ ] **Step 1: Write the failing tests** (append to `MainViewModelTests`)

```csharp
    [Fact]
    public void SubPages_areCreated_indented_folded_andSurviveARestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var sec = vm.SelectedSection!;
            var parent = sec.Pages[0];
            vm.NewSubPage(parent);
            var child = vm.SelectedPage!;
            Assert.Equal(1, child.Level);
            Assert.Equal(sec.Pages.IndexOf(parent) + 1, sec.Pages.IndexOf(child));
            Assert.True(parent.HasSubPages);

            vm.NewPageAfter(parent);
            var sibling = vm.SelectedPage!;
            Assert.Equal(0, sibling.Level);
            vm.MakeSubPage(sibling);
            Assert.Equal(1, sibling.Level);
            vm.MoveUpALevel(sibling);
            Assert.Equal(0, sibling.Level);

            vm.SelectedPage = child;
            vm.ToggleFold(parent);
            Assert.True(child.IsFoldedAway);
            Assert.Same(parent, vm.SelectedPage);

            var again = new MainViewModel(new WorkspaceStore(dir), dir);
            var p2 = again.SelectedNotebook!.Sections[0].Pages;
            Assert.Equal(1, p2[1].Level);
            Assert.True(p2[0].Collapsed);
            Assert.True(p2[1].IsFoldedAway);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DeletingAParent_keepsItsSubPages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            var sec = vm.SelectedSection!;
            var parent = sec.Pages[0];
            vm.NewSubPage(parent);
            var child = vm.SelectedPage!;
            vm.DeletePageCommand.Execute(parent);
            Assert.DoesNotContain(parent, sec.Pages);
            Assert.Contains(child, sec.Pages);
            Assert.Equal(0, child.Level);
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Lumenotepad.Tests -c Release --filter "SubPages|DeletingAParent"`
Expected: build error.

- [ ] **Step 3: Implement** in `MainViewModel`:

```csharp
    public void RefreshPageTree()
    {
        if (SelectedSection is { } sec) PageTree.Refresh(sec.Pages);
    }

    private Page AddAt(Func<IList<Page>, int, Page, int> insert, Page anchor)
    {
        var sec = SelectedSection!;
        var page = new Page { Title = "Untitled page" };
        insert(sec.Pages, sec.Pages.IndexOf(anchor), page);
        StampPageStyle(page);
        SelectedPage = page;
        Save();
        return page;
    }

    public void NewPageAfter(Page pg)
    {
        if (SelectedSection is not { } sec || !sec.Pages.Contains(pg)) return;
        AddAt(PageTree.InsertAfter, pg);
    }

    public void NewSubPage(Page pg)
    {
        if (SelectedSection is not { } sec || !PageTree.CanAddSubPage(sec.Pages, sec.Pages.IndexOf(pg))) return;
        AddAt(PageTree.InsertSubPage, pg);
    }

    public void MakeSubPage(Page pg)
    {
        if (SelectedSection is not { } sec) return;
        PageTree.Indent(sec.Pages, sec.Pages.IndexOf(pg));
        Save();
    }

    public void MoveUpALevel(Page pg)
    {
        if (SelectedSection is not { } sec) return;
        PageTree.Outdent(sec.Pages, sec.Pages.IndexOf(pg));
        Save();
    }

    public void ToggleFold(Page pg)
    {
        if (SelectedSection is not { } sec) return;
        pg.Collapsed = !pg.Collapsed;
        PageTree.Refresh(sec.Pages);
        if (SelectedPage is { IsFoldedAway: true } sel) SelectedPage = PageTree.VisibleAncestor(sec.Pages, sel);
        Save();
    }
```

In `AddPage`, after `sec.Pages.Add(pg);` add `PageTree.Refresh(sec.Pages);`. Replace the body of `DeletePage` after `ForgetPageDoc(pg, deleteFile: true);` with:

```csharp
        int idx = sec.Pages.IndexOf(pg);
        PageTree.RemovePromoting(sec.Pages, idx);
        SelectedPage = sec.Pages.ElementAtOrDefault(Math.Max(0, idx - 1));
        Save();
```

After `_workspace = store.LoadOrSeed();` add:

```csharp
        foreach (var sec in _workspace.Notebooks.SelectMany(n => n.Sections)) PageTree.Normalize(sec.Pages);
```

(adjust the collection name to the one the constructor iterates). Anywhere the view model adds pages to a section outside these methods, call `PageTree.Refresh` on that section afterwards.

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/Lumenotepad.Tests -c Release`
Expected: all PASS.

- [ ] **Step 5: Commit**: `git commit -m "Create, indent, fold and delete sub-pages"`

---

### Task 6: Pages list and right-click menus

**Files:**
- Modify: `src/Lumenotepad/Views/MainView.axaml`, `src/Lumenotepad/Views/MainView.axaml.cs`

**Interfaces:**
- Consumes: Task 5 view-model methods, `PageTree`.
- Produces: `List<Control> PagesMenuItems(Page?)`, `SectionsMenuItems(Section?)`, `NotebookMenuItems(Notebook?)` (used by the probe).

- [ ] **Step 1: Pages list template and folding.** Replace the `PagesList` item template with:

```xml
                        <ListBox.ItemTemplate>
                            <DataTemplate x:DataType="models:Page">
                                <Grid ColumnDefinitions="Auto,Auto,*">
                                    <Border Width="{Binding IndentWidth}"/>
                                    <Button Grid.Column="1" Classes="fold" Click="OnPageFoldClick"
                                            IsVisible="{Binding HasSubPages}" ToolTip.Tip="Show or hide sub-pages">
                                        <Panel>
                                            <TextBlock Text="&#x25B8;" IsVisible="{Binding Collapsed}"/>
                                            <TextBlock Text="&#x25BE;" IsVisible="{Binding !Collapsed}"/>
                                        </Panel>
                                    </Button>
                                    <TextBlock Grid.Column="2" Text="{Binding Title}" FontSize="12.5"
                                               TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
                                </Grid>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
```

Add styles next to `ListBox.pages ListBoxItem`:

```xml
        <Style Selector="ListBox.pages ListBoxItem">
            <Setter Property="IsVisible" Value="{ReflectionBinding !IsFoldedAway}"/>
        </Style>
        <Style Selector="Button.fold">
            <Setter Property="Width" Value="16"/>
            <Setter Property="Height" Value="16"/>
            <Setter Property="Padding" Value="0"/>
            <Setter Property="Margin" Value="0,0,4,0"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="FontSize" Value="10"/>
            <Setter Property="HorizontalContentAlignment" Value="Center"/>
            <Setter Property="VerticalContentAlignment" Value="Center"/>
            <Setter Property="Cursor" Value="Hand"/>
        </Style>
```

In code-behind:

```csharp
    private void OnPageFoldClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Models.Page pg) Vm?.ToggleFold(pg);
        e.Handled = true;
    }
```

When the selected section changes (the existing handler for `SelectedSection`), call `Vm.RefreshPageTree()`.

- [ ] **Step 2: Menu builders.** Change `OpenMenu` to take `params Control[] items`. Add:

```csharp
    private static MenuItem Act(string header, System.Action onClick, bool enabled = true)
    {
        var m = new MenuItem { Header = header, IsEnabled = enabled };
        m.Click += (_, _) => onClick();
        return m;
    }

    private List<Control> PagesMenuItems(Models.Page? pg)
    {
        var items = new List<Control>();
        if (Vm is not { SelectedSection: { } sec } vm) return items;
        if (pg is null)
        {
            items.Add(Act("New page", () => vm.AddPageCommand.Execute(null)));
            items.Add(Act("Open a PDF as a page…", async () => await OpenPdfAsPage()));
            return items;
        }
        int i = sec.Pages.IndexOf(pg);
        items.Add(Act("New page", () => vm.NewPageAfter(pg)));
        items.Add(Act("New sub-page", () => vm.NewSubPage(pg), PageTree.CanAddSubPage(sec.Pages, i)));
        items.Add(Act("Make sub-page", () => vm.MakeSubPage(pg), PageTree.CanIndent(sec.Pages, i)));
        items.Add(Act("Move up a level", () => vm.MoveUpALevel(pg), PageTree.CanOutdent(sec.Pages, i)));
        items.Add(new Separator());
        items.Add(Act("Rename", () => BeginRenamePage(pg)));
        items.Add(Act("Customize page…", async () =>
        {
            if (Window is not { } w) return;
            await new CustomizeSheetWindow(vm, pg).ShowDialog(w);
            RefreshAfterStyleDialog(pg);
        }));
        items.Add(Act("Export page…", async () => await ExportPageAsync(pg)));
        string extra = pg.HasSubPages ? " Its sub-pages stay and move up a level." : "";
        items.Add(Act("Delete page", () => ConfirmThenDelete(
            "Delete this page?",
            $"“{Label(pg.Title)}” will be permanently deleted.{extra} This can't be undone.",
            vm.ConfirmDeletePage,
            () => CollapseThenDelete(PagesList.ContainerFromItem(pg) as Control, () => vm.DeletePageCommand.Execute(pg)))));
        return items;
    }

    private List<Control> SectionsMenuItems(Models.Section? sec)
    {
        var items = new List<Control>();
        if (Vm is not { } vm) return items;
        items.Add(Act("New section", () => vm.AddSectionCommand.Execute(null)));
        if (sec is null)
        {
            items.Add(Act("Open a PDF as a section…", async () => await OpenPdfAsSection()));
            return items;
        }
        items.Add(new Separator());
        items.Add(Act("Rename", () => BeginRenameSection(sec)));
        items.Add(Act("Customize section…", async () =>
        {
            if (Window is not { } w) return;
            await new CustomizeSheetWindow(vm, sec).ShowDialog(w);
            RefreshAfterStyleDialog(null);
        }));
        items.Add(Act("Delete section", () => ConfirmThenDelete(
            "Delete this section?",
            $"“{Label(sec.Name)}” and all its pages will be permanently deleted. This can't be undone.",
            vm.ConfirmDeleteSection,
            () => CollapseThenDelete(SectionsList.ContainerFromItem(sec) as Control, () => vm.DeleteSectionCommand.Execute(sec)))));
        return items;
    }

    private List<Control> NotebookMenuItems(Notebook? nb)
    {
        var items = new List<Control> { Act("New notebook", () => OpenNotebookWizard()) };
        if (nb is null || Vm is not { } vm) return items;
        items.Add(new Separator());
        items.Add(CustomizeMenuItem(nb));
        items.Add(PaperTintMenu(nb));
        items.Add(Act("Delete notebook", () => ConfirmThenDelete(
            "Delete this notebook?",
            $"“{Label(nb.Name)}” and all its sections and pages will be permanently deleted. This can't be undone.",
            vm.ConfirmDeleteNotebook,
            () => CollapseThenDelete(NotebooksList.ContainerFromItem(nb) as Control, () => vm.DeleteNotebookCommand.Execute(nb)))));
        return items;
    }
```

Rewrite the three handlers to use them (keeping the existing "select what was clicked" lines):

```csharp
    private void OnPagesContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var pg = (e.Source as StyledElement)?.DataContext as Models.Page;
        if (pg is not null && Vm is { } vm) vm.SelectedPage = pg;
        var items = PagesMenuItems(pg);
        if (items.Count > 0) OpenMenu(e, items.ToArray());
    }
```

and the same shape for `OnSectionsContextRequested` (`Section`, `vm.SelectedSection = sec`) and `OnNotebooksContextRequested` (`Notebook`, `vm.SelectedNotebook = nb`). Add in the constructor `HomeHost.ContextRequested += OnHomeEmptyContextRequested;` and:

```csharp
    private void OnHomeEmptyContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_rearranging || (e.Source as StyledElement)?.DataContext is Notebook) return;
        OpenMenu(e, Act("New notebook", () => OpenNotebookWizard()));
    }
```

- [ ] **Step 3: Rearrange by groups.** In `OpenPagesMenu`, replace the rearrange item with:

```csharp
        var groups = PageTree.Groups(sec.Pages);
        Item("Rearrange pages…", async () =>
        {
            if (Window is { } w)
            {
                var labels = PageTree.Groups(sec.Pages).Select(g =>
                {
                    int subs = g.End - g.Start - 1;
                    string t = sec.Pages[g.Start].Title;
                    return subs == 0 ? t : subs == 1 ? $"{t}  (1 sub-page)" : $"{t}  ({subs} sub-pages)";
                }).ToList();
                await ReorderDialog.Show(w, "Rearrange pages", labels, (f, t) => PageTree.MoveGroup(sec.Pages, f, t));
            }
            Vm.Save();
        }, groups.Count > 1);
```

and in `OpenPdfAsPage`, after `sec.Pages.Add(pg);`, add `Models.PageTree.Refresh(sec.Pages);`.

- [ ] **Step 4: Build and run all tests**

Run: `dotnet build src/Lumenotepad -c Release` then `dotnet test tests/Lumenotepad.Tests -c Release`
Expected: 0 warnings, all PASS.

- [ ] **Step 5: Commit**: `git commit -m "Right-click menus everywhere, and sub-pages in the pages list"`

---

### Task 7: Button placement and the bubble

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`, `src/Lumenotepad/ViewModels/MainViewModel.cs`, `src/Lumenotepad/Views/MainView.axaml`, `src/Lumenotepad/Views/MainView.axaml.cs`, `src/Lumenotepad/Views/PreferencesWindow.axaml`, `src/Lumenotepad/Views/PreferencesWindow.axaml.cs`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`

**Interfaces:**
- Produces: `MainViewModel.ButtonPlacement (string)`; hosts `TitleNavHost`, `TitleRightHost`, `RailTopHost`, `RailBottomHost`, `PagesHeaderHost`, `BubbleHost`; `PanelBubble`, `BubbleRailBtn`, `BubblePagesBtn`; `MainView.ApplyButtonPlacement()`.

- [ ] **Step 1: Write the failing test** (append to `AppSettingsTests`):

```csharp
    [Fact]
    public void ButtonPlacement_defaultsToTopLeft_andRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.Equal("TopLeft", new AppSettings().ButtonPlacement);
            new AppSettings { ButtonPlacement = "BottomLeft" }.Save(dir);
            Assert.Equal("BottomLeft", AppSettings.Load(dir).ButtonPlacement);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run it to see it fail**, then add `public string ButtonPlacement { get; set; } = "TopLeft";` to `AppSettings`, and to `MainViewModel` `[ObservableProperty] private string _buttonPlacement = "TopLeft";` with load, save (`OnButtonPlacementChanged`) and reset lines in the same pattern as the other settings.

- [ ] **Step 3: Hosts in XAML.**
  - In `TitleLeft`, after the "Lumenotepad" TextBlock: `<StackPanel x:Name="TitleNavHost" Orientation="Horizontal" Margin="6,0,0,0"/>`.
  - Name the right-hand title StackPanel `x:Name="TitleRightHost"` (the four buttons stay declared there).
  - In the rail `DockPanel`, after `RailAddBtn`: `<StackPanel x:Name="RailBottomHost" DockPanel.Dock="Bottom" HorizontalAlignment="Center" Spacing="2"/>` and `<StackPanel x:Name="RailTopHost" DockPanel.Dock="Top" HorizontalAlignment="Center" Spacing="2"/>`.
  - Wrap `NotebookName` in `<Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto">`, moving its `DockPanel.Dock` to the Grid, and add `<StackPanel x:Name="PagesHeaderHost" Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center"/>`.
  - As the last child of the canvas column `<Panel Grid.Column="3">`:

```xml
            <Border x:Name="PanelBubble" HorizontalAlignment="Left" VerticalAlignment="Bottom" Margin="22,0,0,22"
                    CornerRadius="20" Padding="4" IsVisible="False"
                    Background="{DynamicResource MenuBackgroundBrush}" BorderBrush="{DynamicResource MenuBorderBrush}"
                    BorderThickness="1" BoxShadow="0 6 18 0 #55000000">
                <StackPanel x:Name="BubbleHost" Orientation="Horizontal" Spacing="2">
                    <Button x:Name="BubbleRailBtn" Theme="{StaticResource IconButton}" Width="34" Height="34" FontSize="15"
                            FontFamily="{StaticResource IconFont}" Content="&#xE8A1;"
                            Command="{Binding ToggleRailCommand}" ToolTip.Tip="Show notebooks"/>
                    <Button x:Name="BubblePagesBtn" Theme="{StaticResource IconButton}" Width="34" Height="34" FontSize="15"
                            FontFamily="{StaticResource IconFont}" Content="&#xE8A5;"
                            Command="{Binding TogglePagesCommand}" ToolTip.Tip="Show pages"/>
                </StackPanel>
            </Border>
```

- [ ] **Step 4: Placement logic** in `MainView.axaml.cs`:

```csharp
    private void ApplyButtonPlacement()
    {
        if (Vm is not { } vm) return;
        bool rail = vm.IsRailVisible, pages = vm.IsPagesVisible;
        var (home, railBtn, pagesBtn, prefs) = vm.ButtonPlacement switch
        {
            "AllTopLeft" => (TitleNavHost, TitleNavHost, TitleNavHost, TitleNavHost),
            "InPanels" => (RailTopHost, RailTopHost, PagesHeaderHost, RailBottomHost),
            "BottomLeft" => (RailBottomHost, RailBottomHost, RailBottomHost, RailBottomHost),
            _ => (TitleNavHost, TitleNavHost, TitleNavHost, TitleRightHost),
        };
        bool Hidden(Panel host) =>
            (!rail && (ReferenceEquals(host, RailTopHost) || ReferenceEquals(host, RailBottomHost)))
            || (!pages && ReferenceEquals(host, PagesHeaderHost));
        foreach (var b in new Control[] { HomeBtn, RailToggle, PagesToggle, PrefsBtn })
            (b.Parent as Panel)?.Children.Remove(b);
        (Hidden(home) ? BubbleHost : home).Children.Add(HomeBtn);
        railBtn.Children.Add(RailToggle);
        pagesBtn.Children.Add(PagesToggle);
        (Hidden(prefs) ? BubbleHost : prefs).Children.Add(PrefsBtn);

        BubbleRailBtn.IsVisible = !rail;
        BubblePagesBtn.IsVisible = !pages;
        bool show = !rail || !pages;
        if (show && !PanelBubble.IsVisible) { PanelBubble.IsVisible = true; Motion.RiseIn(PanelBubble); }
        else if (!show && PanelBubble.IsVisible) Motion.FadeOut(PanelBubble, onDone: () => PanelBubble.IsVisible = false);
    }
```

Call it in `HookVm` (after `ApplyCanvasPrefs();`) and in `OnVmPropertyChanged` for `ButtonPlacement`, `IsRailVisible` and `IsPagesVisible` (alongside the existing `Motion.Reveal` calls).

- [ ] **Step 5: Preferences.** In the PANELS section of `PreferencesWindow.axaml`, first row:

```xml
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Orientation="Horizontal">
                                <TextBlock Classes="label" Text="Button placement"/>
                                <Border Classes="qmark" ToolTip.Tip="Where the All notebooks, Show notebooks, Show pages and Preferences buttons live. When a panel is hidden, a small bubble in the bottom-left corner brings it back."><TextBlock/></Border>
                            </StackPanel>
                            <ComboBox x:Name="ButtonPlacementBox" Grid.Column="1" Width="150"/>
                        </Grid>
```

In `PreferencesWindow.axaml.cs`, next to the `ToolbarPosBox` setup:

```csharp
        string[] placementKeys = { "TopLeft", "AllTopLeft", "InPanels", "BottomLeft" };
        ButtonPlacementBox.ItemsSource = new[] { "Top left", "All top left", "Inside the panels", "Bottom left" };
        ButtonPlacementBox.SelectionChanged += (_, _) =>
        {
            if (Vm is { } vm && ButtonPlacementBox.SelectedIndex is >= 0 and < 4)
                vm.ButtonPlacement = placementKeys[ButtonPlacementBox.SelectedIndex];
        };
```

and where `ToolbarPosBox.SelectedItem = vm.ToolbarPosition;` is set: `ButtonPlacementBox.SelectedIndex = System.Math.Max(0, System.Array.IndexOf(placementKeys, vm.ButtonPlacement));` (hoist `placementKeys` to a static field). Add `ButtonPlacementBox` to the list of combo boxes at line ~407.

- [ ] **Step 6: Build and run all tests**

Run: `dotnet build src/Lumenotepad -c Release` then `dotnet test tests/Lumenotepad.Tests -c Release`
Expected: 0 warnings, all PASS.

- [ ] **Step 7: Commit**: `git commit -m "Choose where the navigation buttons live, with a bubble for hidden panels"`

---

### Task 8: Drive the real app, then release 1.3.0

**Files:**
- Temporarily replace: `tools/CardRepro/Program.cs` (restore afterwards)
- Modify: `src/Lumenotepad/Lumenotepad.csproj`
- Create: `docs/release-notes-1.3.0.md`

- [ ] **Step 1: Probe.** A headless program hosting `MainView` with a fresh `MainViewModel` in a temp folder, window 1280 × 760, printing PASS/FAIL for:
  1. Click empty canvas to start a text box; call the focused editor's `ApplyFont("Caveat")` (the toolbar's own call); type "hello"; Ctrl+A, Backspace; click inside the same box; type "x": the run's font is Caveat.
  2. Click empty canvas elsewhere for a new box; type "y": the run's font is Caveat.
  3. In a box type "one. two": the text reads "one. Two"; Ctrl+Z: "one. two". Type "e.g. x": no capital.
  4. `PagesMenuItems(null)` headers are New page, Open a PDF as a page…; `PagesMenuItems(page)` starts New page, New sub-page, Make sub-page, Move up a level; `SectionsMenuItems(null)` starts New section; `NotebookMenuItems(null)` is New notebook.
  5. `vm.NewSubPage(first page)`: the list shows the child with its indent spacer width 14; `vm.ToggleFold(parent)`: the child's `ListBoxItem` is not visible.
  6. For each placement: the parents of `HomeBtn`, `RailToggle`, `PagesToggle`, `PrefsBtn` match the table; with `InPanels`, hide the rail: `HomeBtn` and `PrefsBtn` are in `BubbleHost` and `PanelBubble` is visible; show it again: the bubble hides.
  7. Save a screenshot per placement, with the pages panel hidden in one, and look at every one.

  Run: `cd tools/CardRepro && dotnet run -c Release`. Expected: every check PASS. Then `git checkout -- tools/CardRepro/Program.cs`.

- [ ] **Step 2: Version and notes.** Set `<Version>1.3.0</Version>`. Write `docs/release-notes-1.3.0.md` with a `<!-- summary: ... -->` line, covering: fonts that stay put, the keep-font and auto-capital preferences, right-click menus, sub-pages, button placement and the bubble, and the PDF reopen fix from `docs/release-notes-1.2.15.md`; end with the standard notes footer.

- [ ] **Step 3: Verify**: build with 0 warnings and the full test suite green.

- [ ] **Step 4: Commit**: `git commit -m "Release 1.3.0"`

- [ ] **Step 5: Publish** by the recorded flow: `tools/publish-windows.sh`, `tools/publish-macos.sh`, `python tools/publish-manifest.py`, `pwsh installer/build-setup.ps1 -Launcher -MaxCompression`, `git push`, `gh release create v1.3.0` (Latest; mac zips + latest.json) and `v1.3.0-win-beta` (prerelease; portable zip, Setup, Launcher, SHA256SUMS) with `--target` set to the full commit SHA; then check the live manifest reads 1.3.0, every asset URL returns 200, and the published mac arm64 hash matches the manifest.
