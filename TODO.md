# TODO (Required Functions / Verification Checklist)

This file tracks the **required behaviors** for TextFileWatch and provides a lightweight checklist for manual verification.

## Core App Flows
- [ ] App starts without errors and shows the “Add a file to start watching.” hint when no tabs exist (`MainForm`).
- [ ] Startup auto-loads supported text files from the app directory when no prior state exists (`MainForm.LoadAllTextFilesAtStartup`).
- [ ] Startup restores previously-opened file tabs (skips missing files) (`MainForm.RestoreTabsOnStartup`, `AppStateStore.Load`).
- [ ] App persists open tabs and directory-watch state on exit (`MainForm.PersistTabsOnExit`, `AppStateStore.Save`).

## File Tabs (Per-File Viewer)
- [ ] “Add File…” opens a file dialog and creates a new tab (`MainForm.PromptAndAddTab`, `MainForm.AddFileTab`).
- [ ] Adding a file that is already open focuses the existing tab (no duplicates) (`MainForm.AddFileTab`).
- [ ] “Refresh now” updates content immediately (`FileTabView.RefreshFromFile(force: true)`).
- [ ] “Watch” toggle starts/stops periodic refreshes (`FileTabView.UpdateTimerState`).
- [ ] “Interval (s)” changes polling cadence (`FileTabView.UpdateTimerInterval`).
- [ ] Missing file is handled gracefully (message in viewer + status) and recovers when the file returns (`FileTabView.ShowMissing`, `FileTabView.RefreshFromFile`).
- [ ] “Highlight changes” highlights changed lines after refresh (`FileTabView.ApplyTextWithHighlight`, `LineDiff.ComputeChangedNewLines`).
- [ ] “Scroll to changes” scrolls to the first changed line after refresh (`FileTabView.ApplyTextWithHighlight`).

## Directory Watch Tab
- [ ] “Add Dir…” creates a directory tab and starts watching (`MainForm.PromptAndAddDirectoryTab`, `MainForm.AddDirectoryTab`).
- [ ] Multiple watched directories with the same leaf folder name are disambiguated by including parent folder(s) in the tab title.
- [ ] Directory tab lists supported files with last-write + size (`DirectoryTabView.UpsertItem`).
- [ ] Create/change/delete/rename events update the list (debounced) (`DirectoryTabView.Queue`, `DirectoryTabView.ProcessPending`).
- [ ] Directory missing is detected and shown without closing the tab; watcher resumes when the directory returns (including delete + recreate at the same path) (`DirectoryTabView.SetDirectoryExists`).
- [ ] Directory rename/delete in parent folder is detected (parent watcher) (`DirectoryTabView.StartParentWatcher`, `DirectoryTabView.OnParentDirectoryChanged`).
- [ ] “Auto-open new files” opens newly created/updated files in file tabs (`DirectoryTabView`, `MainForm.AddDirectoryTab` handler).
- [ ] “Open selected” and double-click open the selected file (`DirectoryTabView.OpenSelected`).
- [x] Directory tab has “Close all opened file tabs” to close any file tabs that belong to that watched directory.

## Tab Management / UX
- [ ] Tabs are large enough to be readable with many open items (scrolling/overflow behavior is acceptable).
- [x] Tabs have an in-tab close button (clickable “x”) in addition to the toolbar “Close Tab”.
- [ ] “Close Tab” button closes the selected tab (`MainForm.CloseSelectedTab`).
- [ ] Ctrl+W closes the selected tab (`MainForm.MainForm_KeyDown`).
- [ ] Right-click tab menu supports “Close / Close Others / Close All” (`MainForm.TabControl_MouseUp`).
- [ ] Closing the directory tab disposes the watcher cleanly (`MainForm.CloseTab`, `DirectoryTabView.Dispose`).

## Recent Items
- [ ] Recently opened files list (open from history; de-duplicate; persists across restarts).
- [ ] Recently watched directories list (open from history; de-duplicate; persists across restarts).

## Non-Functional Requirements (Quality)
- [ ] App remains responsive when watching frequently-updated logs (no UI freezes during refresh).
- [ ] Large file behavior is acceptable (scrolling, highlighting, refresh time).
- [ ] Errors are non-fatal and presented as status text; app keeps running.
