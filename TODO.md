# TODO

Ideas and known gaps, roughly in the order they are worth doing. Finished items move to [CHANGELOG.md](CHANGELOG.md).

## Ideas

- [ ] **Get a code-signing certificate and start signing releases.** Everything else is built: `scripts/sign-file.ps1`, the CI step that signs when the
  `ARGUS_SIGN_PFX_BASE64` / `ARGUS_SIGN_PASSWORD` secrets exist, and the updater's signature policy (see [docs/SIGNING.md](docs/SIGNING.md)). Until a certificate
  is bought and the two secrets are added, releases are unsigned, Windows shows SmartScreen, and the updater cannot tell a taken-over release from a real one.
- [ ] **Watch the disk, second part.** The first part is built (the Disk card on the Performance page: busy graph, hard disk or SSD, who is using the disk, *Slow down
  selected*, a tip when it stays busy; see CHANGELOG). Still to do:
  - A **disk hog** category (Off / Ask / Auto, default Ask): the disk has been busy for about a minute and one program is responsible for most of it, handled like
    the CPU hog (slow it down, never close). Needs the watcher to sample the disk in the background, cheaply, because the card only samples while its page is open. It
    follows the same rails as every other category (core Windows programs and exclusions are never touched, learning mode only records).
  - A **Disk busy** reading on the Overview. There is no room in the current layout (the "This PC" card is already full and the bottom row has four cards), so this
    needs a layout decision first, for example a smaller Activity card or a disk line in the hero.
  - More tips from real numbers: heavy paging because RAM is low (ties in with *Free up memory*), and the usual suspects (SysMain, antivirus scans) as things the user
    may want to schedule or pause. Argus itself never stops a Windows service for this.
  - Sharper per-program numbers. Today they are file reads and writes including the cache; real disk accesses per program need Windows event tracing, which needs
    administrator rights (a use for *Administrator mode*).
  - Slowing a program down cannot be undone from Argus (it resets when the program restarts). A "put it back" button, with the old priority remembered, would match
    *Disabled by Argus*.- [ ] **Disk health.** A dying drive is the other reason an old PC crawls (and the one that loses data), so check it as well as how busy it is. Read-only, nothing is
  ever written to the disk, and no network. Plan:
  - A plain-language verdict per drive on the Performance page and the Disk card: **Healthy**, **Warning** or **Failing**, with what it is based on and what to do
    ("back up your files now"; Argus never runs a repair or `chkdsk` by itself).
  - Sources, from cheapest to most detailed: Windows' own health status for each physical disk (Storage Management, `MSFT_PhysicalDisk`: HealthStatus and
    OperationalStatus); the drive's failure prediction (`MSStorageDriver_FailurePredictStatus`); and the SMART numbers that matter on a hard disk (reallocated and
    pending sectors, power-on hours, temperature, `MSStorageDriver_ATAPISmartData`) or the health log on an NVMe drive (spare capacity left, percentage used,
    temperature, critical warning). Some of these need administrator rights, which is a good use of the existing *Administrator mode*; without them Argus shows what it can and
    says what it could not read.
  - Signs that need no special rights: recent "bad block", "controller error" and "paging error" entries from the disk driver in the System event log, and a volume that
    is marked dirty (Windows wants to check it).
  - Tell the user when the verdict gets worse (tray notice, once), not on every scan. Be honest about the limit: SMART saying OK does not promise the drive is fine, and
    USB and some RAID drives do not report anything, so the card says "not reported" instead of "Healthy".
  - Tests: classifying SMART values and event entries into the three verdicts from made-up inputs, "not reported" for missing data, the notice firing once, the card drawing.
- [ ] **A list of known unwanted software, and where it hides.** Browser toolbars and hijackers, adware and "optimizer" junk, data harvesters, and the usual
  places malware leaves itself, so Argus can recognise them and offer to remove them. This is a big step (it makes Argus partly a malware cleaner), so
  decide the rules before writing code, and build it in this order:
  - **What it is and is not.** A small hand-kept list is not an antivirus and must never be described as one: Windows Security does the real work for viruses, and
    Argus should say so and send people to it. Argus' own value is the stuff antivirus tends to leave alone: potentially unwanted programs, toolbars, hijacked
    browser settings, fake optimizers and trackers, which also slow old PCs down. The README and SAFETY must say this plainly.
  - **The list is data, not code**, in a file Argus reads (like `blocklist.txt`), one entry per threat: a name, a kind (toolbar, hijacker, adware, data harvester,
    ransomware, other), how to recognise it (file or folder name patterns, install paths, publisher names, SHA-256 hashes of known bad files, registry run keys,
    scheduled task and service names, browser extension IDs), and what to do about it. It ships inside each release, so it needs no network; an optional signed
    "definitions" update could use the updater's existing checks (trusted hosts, SHA-256, signature) later. Every entry needs a source and a reason, and a
    hash or exact path beats a loose name pattern, because a false alarm that removes someone's real program is worse than missing a threat.
  - **Where to look** (read-only first): the startup lists and scheduled tasks and services Argus already reads (`Perf.StartupItems`, `Persistence.cs`);
    the usual dropper folders (`%TEMP%`, `%APPDATA%`, `%LOCALAPPDATA%`, `C:\Users\Public`, `ProgramData`); **browser extensions** for Chrome, Edge, Firefox and Brave
    (their extension folders and enterprise-policy keys, which is where toolbars and forced extensions live); and changed browser home page, search engine and
    proxy settings, the classic hijack.
  - **Removal is reversible and never silent.** Nothing is deleted: files are moved to a quarantine folder in `%APPDATA%\Argus` with a record to put them back
    (the same idea as *Disabled by Argus*), a service or task is switched off first, an extension is disabled before it is removed, and a registry value is saved
    before it is changed. It is always **Ask**, never Auto, and it lists exactly what will change before it does it. This **changes a promise in SAFETY.md**
    ("never deletes files outside your own temp folder"), so that page, the README and the changelog must be rewritten honestly when it is built.
  - **Same rails as everything else:** core Windows files and anything on the exclusion list are never offered, a valid signature from a known publisher clears an
    entry, learning mode only records, and administrator mode is needed for anything outside the user's own profile.
  - **Ransomware is a warning, not a removal.** Once files are encrypted, deleting the program does not bring them back and can destroy evidence. Argus can watch for
    the early signs (a program renaming or rewriting lots of user files quickly, the known ransom-note file names, one new extension appearing across folders) and
    **warn loudly**, offer to end the program (a pause, not a cleanup), and show plain steps: disconnect from the network, do not pay, keep the encrypted files, use
    Windows Security and offline backups. It must not promise recovery.
  - **Data harvesters** overlap with the analytics blocklist: reuse that machinery (names, publisher, network-reporting programs) rather than a second list.
  - **Where entries come from.** Write them by hand from public, licence-compatible sources, and record each source; do not copy a commercial vendor's database or
    anything whose licence forbids it. The list needs a maintainer and a review step for every change, or it will rot into false alarms.
  - Tests: matching each kind of entry against throwaway files, folders, startup entries and fake extension folders; quarantine and restore round trips; a core Windows
    file and an excluded program never matched; a signed known publisher clearing an entry; the ransomware-pattern detector on a temp folder of made-up files, never on real ones.
- [ ] **Test the update window by script.** The updater logic and the swap/rollback are tested, but the window itself is only checked by hand.
- [ ] **Package managers.** A winget / Scoop manifest, as another way to get updates.
- [ ] **Who is using it?** Show the top three programs by CPU and by GPU right now, under the new graphs, with a one-click "slow it down". (The disk version is done on the Performance page.)
- [ ] **Longer history.** Click a graph to expand it to 10 minutes or an hour (kept only in memory, never written to disk).
- [ ] **Per-core CPU view** and a GPU memory readout for older graphics cards.
- [ ] **Scheduled startup tasks.** Many older PCs are slowed by logon tasks in Task Scheduler, not just the startup list. Show them next to the
  startup programs and let the user disable one (disabling is reversible; deleting is not).
- [ ] **Test the real administrator restart.** The command and the refusals are tested, but the Windows approval prompt (UAC) can only be checked by hand.
- [ ] **Show scheduled logon tasks on the Performance page** (the finder in `Persistence.cs` already reads Task Scheduler; the page does not list them yet).
- [ ] **Programs that restart themselves.** "Keeps coming back" only finds services, scheduled tasks and startup entries. A program restarted by another program
  (a watchdog, an updater, a vendor launcher) is not found; the dialog says nothing starts it and that is as far as Argus goes.
- [ ] **Services that run from a user-writable folder.** A service running as SYSTEM from somewhere any user can write to (for example `C:\Users\Public`) can be
  replaced by a local user. Worth a plain-language warning on the "Keeps coming back" window and in the Suspicious reason.

## Polish and known issues

- [ ] Faint vertical lines show in the empty space of the *Needs a decision* list.
- [ ] An About page: version, license, links, and the ARGUS wordmark.
- [ ] Check the Activity page and the other pages at other window sizes and at 125% / 150% display scaling.
- [ ] Automated UI smoke test (open every page, assert nothing throws).
- [ ] Tests for the hung-window and resource-hog detectors (they need a window that really hangs and a controllable CPU load).
