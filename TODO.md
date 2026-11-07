# TODO

Ideas and known gaps, roughly in the order they are worth doing. Finished items move to [CHANGELOG.md](CHANGELOG.md).

## Ideas

- [ ] **Sign the exe, and check the signature in the updater.** A code-signing certificate also removes the SmartScreen warning, and it is the only
  thing that protects the updater against a taken-over GitHub release (a checksum published next to the file cannot).
- [ ] **Test the update window by script.** The updater logic and the swap/rollback are tested, but the window itself is only checked by hand.
- [ ] **Package managers.** A winget / Scoop manifest, as another way to get updates.
- [ ] **Who is using it?** Show the top three programs by CPU and by GPU right now, under the new graphs, with a one-click "slow it down".
- [ ] **Longer history.** Click a graph to expand it to 10 minutes or an hour (kept only in memory, never written to disk).
- [ ] **Per-core CPU view** and a GPU memory readout for older graphics cards.
- [ ] **Scheduled startup tasks.** Many older PCs are slowed by logon tasks in Task Scheduler, not just the startup list. Show them next to the
  startup programs and let the user disable one (disabling is reversible; deleting is not).
- [ ] **Programs that keep coming back.** Some analytics programs are restarted by a service or scheduled task. Today Argus only warns. Offer to
  disable that service or task instead of closing the program again and again (needs administrator rights).
- [ ] **First-run learning mode.** Start with everything on Ask for the first days, then show what Argus would have done and let the user
  switch categories to Auto. (SAFETY.md already recommends this; the defaults do not follow it yet.)
- [ ] **Publisher check for "Suspicious".** Verify the file's digital signature (Authenticode / `WinVerifyTrust`) instead of only looking for a
  publisher name, to cut false alarms.
- [ ] **Run as administrator, on request.** A clearly labelled option to restart elevated, so Argus can slow down or close protected services.
  Every safety rail must stay in force.

## Polish and known issues

- [ ] Faint vertical lines show in the empty space of the *Needs a decision* list.
- [ ] An About page: version, license, links, and the ARGUS wordmark.
- [ ] Check the Activity page and the other pages at other window sizes and at 125% / 150% display scaling.
- [ ] Sign the exe. Unsigned downloads trigger a Windows SmartScreen warning.
- [ ] Automated UI smoke test (open every page, assert nothing throws).
- [ ] Tests for the hung-window and resource-hog detectors (they need a window that really hangs and a controllable CPU load).
