# TODO

Ideas and known gaps, roughly in the order they are worth doing. Finished items move to [CHANGELOG.md](CHANGELOG.md).

## Asked for

- [ ] **Self-updater**, so people do not have to re-download from GitHub.
  - This breaks the current "no network, ever" promise (README, SECURITY.md, SAFETY.md). Decide the wording first: *"Argus only connects to
    GitHub to check for updates, and only if you turn that on."*
  - Opt-in and off by default. Check at most once a day; show the changelog; never install silently.
  - Download the portable zip and its `.sha256` from the GitHub release (CI already attaches both), over HTTPS, and verify the hash before
    anything is replaced. Better still, code-sign the exe and verify the signature.
  - Replace the running exe through a small helper that waits for Argus to exit, keeps the previous version for rollback, and restarts it.
  - Needs: a real GitHub repository with releases, and tests for the version comparison and the hash check.

## Ideas

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
