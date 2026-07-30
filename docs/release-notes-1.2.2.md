<!-- summary: Plainer wording throughout, and the settings hints read like a person wrote them. -->
# Lumenotepad 1.2.2

A wording and tidy-up pass. Nothing changes about how the app works.

## Wording

Every hint, tooltip and message in the app has been reworded to read more plainly. The stiff
punctuation is gone from all of them, apart from the greeting on the home page, which stays as it was.

The README and the release notes got the same treatment.

## Under the hood

The source has been tidied up: comments stripped from the C#, XAML and Python, and the build scripts
simplified. No behaviour changed, and the full test suite still passes.

---

**Your notes** are never touched by installing or updating: `~/Library/Application Support/Lumenotepad`
on macOS, the `userdata` folder beside the executable on Windows.
