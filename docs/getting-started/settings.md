# Settings & Customization

Open **OpenCMTools Settings** from the **Settings (Cog)** button in the standalone app, or from **Right-Click → OpenCMTools → OpenCMTools Settings** inside the MECM Admin Console.

Settings are stored per-user as plain JSON in:

```text
%AppData%\OpenCMTools\settings.json
```

*(If upgrading from an earlier `v0.2.x` release previously named `OpenRCT`, existing preferences in `%AppData%\OpenRCT\settings.json` are automatically migrated on first load.)*

![OpenCMTools Settings — Appearance & Icon Style](../assets/images/settings-appearance.png)

---

## 1. External Tools (Optional PsExec)

OpenCMTools has **zero mandatory external dependencies**. Sysinternals **PsExec** is only needed if you want to use **Remote Tools → Interactive Command Prompt**.

- **Path to PsExec.exe or PsExec64.exe:** Click **Browse...** to select your local copy of Sysinternals PsExec. Live validation confirms the file exists and warns if the filename is not `PsExec.exe` or `PsExec64.exe`.
- **Run Interactive Command Prompt as Local SYSTEM (`-s`):** Passes `-s` to PsExec (enabled by default).
- **Automatically pass `-accepteula`:** Suppresses the first-run Sysinternals EULA dialog.
- **Additional PsExec Arguments:** Optional extra CLI flags appended before `cmd.exe`.

---

## 2. Connections & Performance

Tune how OpenCMTools communicates with remote endpoints and your MECM SMS Provider:

| Setting | Default | Range / Options | Description |
|---|---|---|---|
| **Preferred Remote Transport** | `WmiDcomFirst` | `WmiDcomFirst`, `WinRmCimFirst`, `WmiDcomOnly`, `WinRmCimOnly` | Saved transport preference *(currently all remote WMI queries execute over WMI/DCOM; WinRM is used by Interactive PowerShell Prompt)* |
| **Connection Timeout (seconds)** | `15` | `3` – `300` | Per-device timeout for remote operations |
| **Max Concurrent Devices** | `16` | `1` – `64` | Maximum number of devices queried in parallel during multi-device and collection operations |
| **Default SMS Provider Server** | *(auto-detect)* | Hostname / FQDN | Optional override if you manage multiple sites or run standalone without a local MECM Console |
| **Default Site Code** | *(auto-detect)* | 3-character site code | Optional MECM Site Code override (e.g., `LAB`, `P01`) |
| **Fast Offline Pre-Check** | Enabled (`true`) | Checkbox | Runs a fast DNS + ICMP Ping + RPC (`135`) / SMB (`445`) probe before WMI and Remote Registry queries so powered-off devices fail in ~1.5s instead of hanging on OS DCOM/RPC timeouts |
| **Offline Check Timeout (ms)** | `1500` | `300` – `10000` | Per-device timeout for the fast online reachability pre-check |

---

## 3. Appearance & Security

### UI Theme
Choose between:

- **Light** *(default — matches standard MECM Admin Console chrome)*
- **Dark** *(modern dark surfaces with high-contrast text and dark-adapted vector icons)*
- **System** *(automatically follows Windows Apps Light/Dark mode)*

### Icon Style (`Fluent Duotone` vs `Crisp Monochrome Outline`)
OpenCMTools includes two complete vector icon sets (while keeping the primary blue Option B OpenCMTools logo consistent in both styles):

- **Fluent Duotone (Default)** — Color-coded Fluent accents with soft translucent fills for quick visual scanning.
- **Crisp Monochrome Outline** — Minimal, high-contrast dark slate outline strokes (auto-inverting to light gray in Dark Mode for WPF windows).

#### How MECM Console Icon Sync Works
1. Selecting an **Icon Style** and clicking **Save** (or **Apply to MECM Console**) updates all open WPF windows (standalone launcher, tool windows, and embedded dashboards) **immediately**.
2. When **Icon Style** changed and you click **Save** (or anytime you click **Apply to MECM Console**), OpenCMTools also updates `<AdminConsole>\bin\OpenCMTools.Icons.dll` by deploying `OpenCMTools.Icons.Duotone.dll` or `OpenCMTools.Icons.Outline.dll`:
   - If you are already running elevated, the swap happens silently (safely rotating `OpenCMTools.Icons.dll` if the console currently has it locked).
   - If you are running as a standard user, OpenCMTools asks for confirmation and triggers a single **Windows UAC prompt** to run the fast-path `Install-OpenCMTools.ps1 -ApplyIconStyleOnly -IconStyle <Duotone|Outline>`.
3. **Restart the MECM Admin Console** once to see the updated tree, ribbon, and right-click menu icons.

!!! tip "Out-of-Sync Indicator"
    If `<AdminConsole>\bin\OpenCMTools.Icons.dll` does not match your saved `IconStyle` (for example after an MSI upgrade), Settings displays an amber hint under **Icon Style** — click **Apply to MECM Console** at any time to re-sync.

### Safety Controls
- **Prompt for confirmation before destructive actions:** Prompts before *Delete from AD/ConfigMgr*, *Disable in AD*, *Stop ConfigMgr Client Activities*, *Repair Client*, *Uninstall SCCM Client*, *Uninstall Application*, *Terminate Process*, *Restart/Shutdown*, *Suspend/Resume BitLocker*, *Enable/Disable Firewall Rules*, *Remove Content from DP*, *Redistribute Failed Content*, and multi-device *Service Control* / *Software Center* bulk actions. *(High-impact actions like **Clear TPM**, **Delete User Profile**, and **Delete Registry Value** always prompt for confirmation.)*
- **Automatically clear clipboard after copying secrets:** Clears the Windows clipboard automatically after copying **LAPS passwords** or **BitLocker recovery keys** (default: **30 seconds**, configurable `5`–`300` seconds).

---

## 4. Updates (Manual & Fork-Friendly)

OpenCMTools **never phones home automatically in the background**.

- **Enable manual 'Check for Updates' button:** Uncheck this in air-gapped environments to hide the update check button in the standalone launcher.
- **GitHub Repository Override:** Accepts `owner/repo` or a full `https://github.com/owner/repo` URL.
  - When left blank, CI-built releases automatically check the repository from which they were compiled (embedded at build time via `GITHUB_REPOSITORY` in `Directory.Build.props`).
  - Forks and private mirrors automatically check their own releases without editing source code.
- **Check for Updates Now:** Queries the GitHub Releases API on demand, compares SemVer versions, shows release notes, and provides a button to open the release download page in your default browser.
