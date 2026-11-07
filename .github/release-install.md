---

## How to install Argus (there is no installer)

Argus is a **portable program**. Nothing is installed and Windows is not changed: you extract a zip to a folder and run `Argus.exe`.

1. **Download** `Argus-<version>-win-x64.zip` from *Assets* below. Windows 10 or 11, 64-bit.
2. **Unblock the zip before you extract it.** Right-click the zip, choose **Properties**, tick **Unblock** at the bottom, click **OK**. Windows marks everything downloaded from the internet, and that mark is what makes it block Argus. If there is no *Unblock* box, the zip is not marked and you can skip this.
3. **Extract it to a safe folder that you keep and can write to**, for example `C:\Users\<you>\Apps\Argus`.
   - Do not run Argus from inside the zip window: Windows runs it from a temporary folder that gets cleaned up.
   - Avoid *Downloads* and *Temp* (they get cleaned up) and *Program Files* (read-only, so Argus cannot update itself there).
4. **Run `Argus.exe`.** If Windows says **"Windows protected your PC"**, click **More info**, then **Run anyway**. Argus is not code-signed yet, so Windows does not recognise it.

If it is still blocked: right-click `Argus.exe`, **Properties**, **Unblock**, **OK** (or run `Unblock-File .\Argus.exe` in PowerShell). Some antivirus tools are wary of any program that closes other programs; Argus is open source and you can build it yourself (see the README).

To check the download was not changed: `Get-FileHash .\Argus-<version>-win-x64.zip -Algorithm SHA256` must match the `.sha256` file.

To remove Argus: turn off *Start Argus when I sign in* if you used it, quit Argus from the tray icon, and delete the folder. Settings and the log are in `%APPDATA%\Argus`.