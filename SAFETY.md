# Safety

Argus changes things on your PC, so it is built to be gentle and hard to misuse.

## What it will never do
- Close core Windows processes (`csrss`, `lsass`, `winlogon`, `dwm`, `svchost`, `explorer` and friends) automatically,
  and never the Windows Security engine. A protected name only counts if the file is in a system location, so a fake
  `svchost.exe` in a temp folder is treated as suspicious, not protected.
- Close the program that started it (your terminal, shell, or Claude) or itself.
- Delete startup entries, services, scheduled tasks, registry keys, or any file outside your own temp folder.
- Switch off a service, scheduled task or startup entry without you choosing it in a window that lists exactly what will change (the "keeps coming back"
  fix is never automatic, and core Windows programs are never offered).
- Follow links/junctions while cleaning temp files, or touch recent, read-only or in-use files.
- Use the network, except for the optional update feature (off by default, GitHub only).
- Ask for administrator rights by itself. It runs as an ordinary user; only **Restart as administrator** (Rules > Options) asks, through Windows' own
  approval prompt.
- Install an update that fails its checksum, version or signature check, or overwrite a copy built from source.

## Administrator mode
It is opt-in and clearly labelled. Nothing about the safety rules changes with it: core Windows programs, the Windows Security engine, the program that started
Argus and Argus itself are refused with or without administrator rights. What changes is only that Windows no longer says "access denied" for protected
services, so a mistaken rule can now affect the whole PC. Use it for a specific job, then quit and start Argus normally.

## Updates
Update checks are off until you turn them on. An install only happens when you press **Update and restart**. Your current version stays on disk
(`Argus.exe.old`) until the new one has run for ten seconds, and Argus goes back to it automatically if the new one fails to start twice.

## What is reversible
| Action | How to undo |
|---|---|
| Stop a startup program | tick it again (it uses Task Manager's own switch; nothing is deleted) |
| Slow down a hog | priority resets when the program restarts; or add it to exclusions |
| Lower a program's disk priority (Performance > Disk) | resets when the program restarts; nothing is closed |
| Free up memory | nothing to undo; Windows pages memory back in as needed |
| Invisible window made click-through | restarts with its program; or exclude its class |
| Hide a stuck popup | Explorer shows it again the next time it opens one |
| Switch off a service, task or startup entry that keeps restarting a program | Rules > Options > Disabled by Argus > Show list > Restore (puts a service back to its old start type) |

## What is not reversible
Closing a program loses its unsaved work, and temp cleanup cannot be undone. Both ask first unless you set a category
to *Automatic*; analytics closing is the only destructive action that is *Automatic* by default, and it only touches
programs matching your editable `blocklist.txt` (never anything in `exclusions.txt`).

## Before trusting it
A fresh install starts in **learning mode** for three days: Argus only writes down what it would have done, and you read that list (the **Learning**
button) before letting it act. If you upgraded from an older version, put every category on **Ask me** for a few days instead, read the Activity page,
and add anything you rely on to `exclusions.txt`.