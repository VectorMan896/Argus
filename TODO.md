# TODO

Ideas and known gaps, roughly in the order they are worth doing. Finished items move to [CHANGELOG.md](CHANGELOG.md).

## Ideas

- [ ] **Get a code-signing certificate and start signing releases.** Everything else is built: `scripts/sign-file.ps1`, the CI step that signs when the
  `ARGUS_SIGN_PFX_BASE64` / `ARGUS_SIGN_PASSWORD` secrets exist, and the updater's signature policy (see [docs/SIGNING.md](docs/SIGNING.md)). Until a certificate
  is bought and the two secrets are added, releases are unsigned, Windows shows SmartScreen, and the updater cannot tell a taken-over release from a real one.
- [ ] **Test the update window by script.** The updater logic and the swap/rollback are tested, but the window itself is only checked by hand.
- [ ] **Package managers.** A winget / Scoop manifest, as another way to get updates.
- [ ] **Who is using it?** Show the top three programs by CPU and by GPU right now, under the new graphs, with a one-click "slow it down".
- [ ] **Longer history.** Click a graph to expand it to 10 minutes or an hour (kept only in memory, never written to disk).
- [ ] **Per-core CPU view** and a GPU memory readout for older graphics cards.
- [ ] **Scheduled startup tasks.** Many older PCs are slowed by logon tasks in Task Scheduler, not just the startup list. Show them next to the
  startup programs and let the user disable one (disabling is reversible; deleting is not).
- [ ] **Test the real administrator restart.** The command and the refusals are tested, but the Windows approval prompt (UAC) can only be checked by hand.
- [ ] **"Keeps coming back" for more than analytics.** It only watches programs Argus closes automatically as Analytics; hogs are slowed down, not closed, and
  hung programs are one-off, so they never trigger it. Revisit if a real case turns up.
- [ ] **Show scheduled logon tasks on the Performance page** (the finder in `Persistence.cs` already reads Task Scheduler; the page does not list them yet).

## Polish and known issues

- [ ] Faint vertical lines show in the empty space of the *Needs a decision* list.
- [ ] An About page: version, license, links, and the ARGUS wordmark.
- [ ] Check the Activity page and the other pages at other window sizes and at 125% / 150% display scaling.
- [ ] Automated UI smoke test (open every page, assert nothing throws).
- [ ] Tests for the hung-window and resource-hog detectors (they need a window that really hangs and a controllable CPU load).
