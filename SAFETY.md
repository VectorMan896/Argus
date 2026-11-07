# Safety

Argus changes things on your PC, so it is built to be gentle and hard to misuse.

## What it will never do
- Close core Windows processes (`csrss`, `lsass`, `winlogon`, `dwm`, `svchost`, `explorer` and friends) automatically,
  and never the Windows Security engine. A protected name only counts if the file is in a system location, so a fake
  `svchost.exe` in a temp folder is treated as suspicious, not protected.
- Close the program that started it (your terminal, shell, or Claude) or itself.
- Delete startup entries, registry keys, or any file outside your own temp folder.
- Follow links/junctions while cleaning temp files, or touch recent, read-only or in-use files.
- Use the network or ask for administrator rights.

## What is reversible
| Action | How to undo |
|---|---|
| Stop a startup program | tick it again (it uses Task Manager's own switch; nothing is deleted) |
| Slow down a hog | priority resets when the program restarts; or add it to exclusions |
| Free up memory | nothing to undo; Windows pages memory back in as needed |
| Invisible window made click-through | restarts with its program; or exclude its class |
| Hide a stuck popup | Explorer shows it again the next time it opens one |

## What is not reversible
Closing a program loses its unsaved work, and temp cleanup cannot be undone. Both ask first unless you set a category
to *Automatic*; analytics closing is the only destructive action that is *Automatic* by default, and it only touches
programs matching your editable `blocklist.txt` (never anything in `exclusions.txt`).

## Before trusting it
Start with every category on **Ask me** for a few days, read the Activity page, and add anything you rely on to
`exclusions.txt`.