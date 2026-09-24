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
