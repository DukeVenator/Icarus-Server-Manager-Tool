# Changelog

## 3.0.0 (November 2026)

This one is a rebuild, not a feature drop. Most of the editor and a big chunk of the manager were
rewritten. Everything both tools could already do is still here. What changed is how careful they
are about the boring parts: saving, reverting, closing, starting and stopping servers.

The short version: 3.0 stops claiming success before it has any.

### Why this release exists

An audit of both tools kept finding the same shape of bug. Something looked like it worked, and the
damage only showed up later.

- A save could shorten or replace your prospect before the new file had been read back and checked.
- Removing a recorder reloaded the form from the model, threw away whatever you had typed, then
  treated the half-edited document as the new saved state.
- Mount fields and talents were written in two passes, so a rejected talent could leave the field
  edits applied anyway.
- Changing a talent rank never reached the talent array at all.
- The manager could hold a process handle it failed to stop, then start a second server next to it.
- Settings migration bumped the schema version before importing the old ports, so custom ports were
  replaced with defaults.
- The installer could report success when SteamCMD failed or the server executable was missing.
- Webhook diagnostics could log the URL and the full response body.

All of those are fixed, and the suite now has tests that fail if they come back.

## Prospect Editor

### Saves that can't eat your file

Every save writes a unique temporary file next to the destination, flushes it, reads it back as an
encoded prospect, and only then replaces your file. The replacement is an atomic swap. If anything
fails, the original bytes are still there and the editor says so.

A backup is written before the original is replaced, and it is never deleted automatically. If a save
fails after the backup exists, the error message names the backup file so you can go get it. Two
saves in the same second no longer collide on temp or backup names.

A failed save doesn't touch the in-memory document either. Drafts are applied to an independent
snapshot, the snapshot is written, and only a published file causes the editor to adopt it. So if a
save fails, the file keeps its old bytes, the document still matches the file, and your drafts remain
in the form ready to retry.

Save As is honest about timing too. The document path only moves, and the unsaved marker only
clears, once the new file has actually been written. Cancel or fail, and the editor stays pointed at
the file you had open.

### Unsaved work survives

Recorder edits and recorder removals now count as unsaved changes, so closing, reverting and saving
all see them. Removing recorders no longer wipes pending metadata, member, custom-setting or mount
edits: rows get remapped to the shifted recorder numbers, your pending values stay in the form, and
the document stays unsaved until you save or revert.

F5 rebuilds the views without loading older values over what you typed. Revert Selected identifies
rows by a stable per-row ID, so renaming a member or a setting key and then reverting restores that
row instead of deleting it. Closing the editor or opening another prospect offers Save, Discard or
Cancel, and a cancelled or failed save leaves the editor open with your edits and drafts.

### Mounts

Editing a mount is one transaction now. Scalar fields and talents validate together, and the
recorder is written once, so a rejected talent can't leave field edits behind.

Overview edits survive a save. Touching only the Advanced tab no longer overwrites them. If both
tabs change the same field in different ways, the explicit Advanced value wins and the conflict is
reported, instead of whoever wrote last quietly deciding.

Saving only reapplies the curated mount values that actually changed, so if you edited a recorder
directly in the raw inspector, stale grid values won't overwrite it. Species swap previews are tied
to the species and talent values they were computed from; change either one and the preview
recomputes. An unknown stored species stays selectable and blocks Apply with a clear message,
instead of being silently swapped for the first entry in the list.

### Talents

Rank changes, additions and deletions now edit the real talent array, and they survive save and
reload. Added talents are cloned from an existing talent structure, so unrelated properties come
along. Entries that can't be named are preserved rather than dropped. An unsupported talent layout
reports an explicit error instead of applying nothing and saying nothing.

### Validation and input

Invalid values are reported per field with the actual problem, and the whole edit is rejected.
Nothing gets partially applied. The recorder wizard commits the value you can see, and an invalid
value blocks Apply instead of closing with an older valid one.

The player picker never assigns a player you didn't choose, keeps unknown stored account IDs
selectable, and keeps the stored spelling when only letter case differs. Custom-setting rows with a
blank name are reported instead of being silently dropped.

### Exports

Decoded export runs from an independent snapshot taken when the export starts, so editing during a
long export can't produce a mixed file. Export refuses to write over the source prospect, including
relative-path and case aliases, and refuses to overwrite an existing file that still reads as an
encoded prospect. A failed export leaves an existing destination alone and removes only its own
temporary file.

### Window, tabs and tables

Metadata is grouped into Identity, Ownership, Gameplay and Timing & rewards. It uses two columns on
a wide window and stacks when the window is narrow. The window content actually reflows now; before,
the shell kept its creation-time width, so resizing only clipped things.

All eight tabs fit at the minimum window size, and tab height follows the font instead of a fixed
pixel value, so larger text doesn't clip labels. The header shows the prospect name (falling back to
the prospect ID, then the file name), keeps the full path on one middle-ellipsized line, and gives
you a tooltip and a Copy path button.

The dark theme covers the menu bar and tab strip now, instead of leaving light bands. “Timing &
rewards” renders its ampersand instead of eating it as a keyboard mnemonic. Menu shortcuts are
actually bound: Ctrl+O, Ctrl+S, Ctrl+Shift+S, Ctrl+E, Ctrl+R, Ctrl+Shift+R and F5 used to be display
text only. The status line separates saved, edited, blocked and failed operations.

The Genetics radar no longer draws its zero tick through the middle of the plot, and the 2 to 10
scale labels stay visible. You can open a prospect from the command line
(`IcarusProspectEditor.exe "path\to\prospect.json"` or `--open <path>`), which also makes
drag-and-drop onto the executable work.

Metadata inputs, checkboxes, the expiry picker, the tab strip and every grid now expose accessible
names, so a screen reader says which field it is, not just its value. The Advanced identifiers
disclosure is a real button instead of a link that screen readers saw as static text with no action.

Two table bugs reported during testing are fixed. Alternating rows used a transparent background,
which `DataGridView` can't erase properly, so scrolling could leave old row text behind and make
rows look printed on top of each other; alternating rows now use an opaque themed colour and every
grid is double-buffered. Clicking a column header now sorts the table, toggling ascending and
descending with a sort glyph. Sorting keeps the same row objects and doesn't mark the document
unsaved.

### Speed

Recorder data is decoded once per document revision and reused by every tab and mount row. Before,
one refresh decoded the full recorder list about five times, plus once per mount.

## Server Manager

### One operation at a time

Start, stop, restart, force kill and close can't overlap any more. One lifecycle operation owns the
server, and the status line says which one is running.

A process handle stays owned until its exit is confirmed. If a stop or kill can't confirm exit, the
manager keeps the handle, tells you the server may still be running, and refuses to launch a
replacement. A restart holds its guard for the whole stop, delay and launch cycle, so a nested stop
can't release or overwrite it. Closing the manager is refused while a lifecycle operation or an
install is running, with an explanation, instead of closing mid-operation.

### Crash restarts

The crash-retry budget only resets after ten minutes of stable, non-crashed running. A crash after
long uptime no longer clears the history and refills the budget. Crash decisions report whether a
restart is allowed, already pending or exhausted, instead of making callers guess from a boolean. A
shutdown the manager started no longer counts as a crash.

### Install and update

Installing or updating runs asynchronously so the window stays responsive, and only one install can
run at a time. Start, restart and close are refused while SteamCMD is alive. A timeout kills the
installer and waits for it to exit. If exit still can't be confirmed, the manager keeps ownership
and keeps blocking rather than starting a server on top of a half-finished install.

Success needs a zero exit code and the expected server executable. Failure is reported and never
enables Start or claims the server is ready.

### Settings

Migrations run in ascending order, and legacy game and query ports are imported before the schema
moves past the version that expects them. That includes the first time the options file is created.

A malformed options file is reported and left byte-for-byte unchanged. Automatic saves are
suppressed while it's unreadable, so startup defaults can't quietly overwrite it; an explicit
settings save or a preset/bundle import can still replace it. Saving options creates a backup,
validates the staged file, then replaces the old one. A failed backup or validation leaves the
original where it was. The scheduled restart time is validated as HH:mm when you save, instead of
storing a value the scheduler will skip.

### Discord webhooks

Webhook delivery goes through an injectable HTTP transport, which makes it testable and lets
delivery failures surface instead of disappearing. Responses and request bodies are disposed.

Diagnostics include the host, status code and exception type only. The webhook URL path, token,
payload and response body are never logged. Description truncation counts the trailing ellipsis, so
a 900 character limit produces at most 900 characters.

### Scheduled restarts

The scheduled action is a restart. It doesn't run SteamCMD, and the labels and tooltips say so now.
Deduplication is per scheduled occurrence (date plus configured time), and an occurrence isn't
consumed while another lifecycle operation is running.

A late timer tick still performs the restart: the occurrence can still be acted on for 15 minutes
instead of only the first minute. The timing rule lives in a small tested policy instead of inside the form.
Policy evaluation pauses during lifecycle, install or close operations, so timer ticks can't burn
crash budget while something else is happening.

## Under the hood

The regression suite covers save failure injection, export aliasing, talent persistence,
transactional mount edits, dialog isolation, projection caching, settings migration, webhook
delivery, crash policy and restart guard behaviour. It's 274 tests in the current tree.

A feature-inventory test fails if a tab, menu, shortcut or theme option disappears. A visual harness
renders the real editor at three window sizes in both themes, at 150% and 200% text scale, and
captures every tab plus the mount editor and recorder inspector. Published screenshots are
anonymised: demo players, demo account IDs and a neutral file path.

The suite runs without touching real saves, user settings, servers, SteamCMD or webhooks. Two
manager form smoke tests are quarantined because they construct the real form against your settings
and timers; they're skipped rather than run unsafely.

Versions now come from git tags via MinVer instead of hand-edited project files. Manager builds use
`manager-vX.Y.Z` tags and editor builds use `editor-vX.Y.Z`. Untagged commits build as the next
prerelease with the commit hash, for example `2.0.3-alpha.0.2+1930c79c`, and the tagged commit builds
exactly `3.0.0`. Release workflows check out full history so the tags are available.

## Upgrading

Manager options migrate automatically on first launch, with a backup of the previous file. Nothing
about the prospect save format changed; saves written by 3.0 are normal encoded prospect saves.

Backups are never pruned. If you save a lot, expect a growing pile of `*.bak` files next to the
prospect. The scheduled action is still restart only. If you expected a scheduled SteamCMD update,
it was never performed; the labels just said it was.

## Known limitations

Keyboard focus order and true per-monitor display scaling above 100% haven't been checked on real
hardware here. Text-scale renders cover 150% and 200%, but they aren't the same as a scaled display.
At 200% text size, the two longest tab labels ellipsize instead of renaming themselves.

The manager form smoke tests still need an isolated settings/profile harness before they can run
safely in CI.
