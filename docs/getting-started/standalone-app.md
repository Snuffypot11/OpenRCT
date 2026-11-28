# Standalone App (`OpenCMTools.App.exe`)

In addition to running directly inside the Configuration Manager Admin Console, OpenCMTools ships with a full **Standalone Launcher** (`OpenCMTools.App.exe`) available from the **Start Menu → OpenCMTools**.

This is ideal when:

- You want to run bulk tools or dashboards without launching the full MECM Console.
- You are working from an admin jump box where the MECM Console is not installed (or only querying AD/WMI/WinRM directly).
- You want to search across all 51 endpoint tools and launch dashboards from a single workspace.

![OpenCMTools Standalone Launcher](../assets/images/standalone-launcher.png)

---

## Header, Target Picker & Footer Controls

- **Header Bar:**
  - **Check for Updates** — Manual, on-demand GitHub Releases check (visible when enabled in Settings; never phones home in the background).
  - **Settings (Cog Icon)** — Opens [OpenCMTools Settings](settings.md) to configure PsExec, concurrency/timeouts, theme, icon style, and updates.
- **Target Picker:** Switch between:
  - **Target Device(s):** Enter one or more computer names, FQDNs, or IP addresses (comma-, semicolon-, or newline-separated), with live target count and a **Clear** button.
  - **ConfigMgr Device Collection:** Connect to your SMS Provider (auto-discovered or configured in Settings) and pick any **Device Collection** (with a **Refresh** button) to target all its members at once.
- **Tool Filter (`Filter:`):** Real-time search across all 51 endpoint tools and categories inside the **Endpoint & Management Tools** pane.
- **Footer Utilities:** Displays the current OpenCMTools version and detected SMS Provider / Site Code, plus one-click **View Install Log** (`C:\ProgramData\OpenCMTools\install.log`) and **Repair MECM Console Extension** buttons.

---

## Workspace Categories

The standalone launcher mirrors the same clean organization as the MECM Admin Console, plus a pinned **Quick Access** tab:

- **Dashboards (6)** — Launch any of the 6 dashboards in a dedicated full-screen-capable window:
  - *Active Directory Cleanup Tool*
  - *BitLocker Compliance*
  - *Content Distribution Monitor*
  - *Hardware and Firmware Audit*
  - *LAPS Dashboard*
  - *Software Updates Deployment Status*
- **Quick Access** — 15 frequently used tools pinned for one-click access.
- **Remote Tools (10)** — *System Information*, *Remote Software Center*, *Remote Windows Security*, *Remote Registry Editor*, *Remote Event Viewer*, *Running Processes*, *Connect to C$*, *Interactive PowerShell Prompt*, *Interactive Command Prompt (PsExec)*, *Manage Computer*.
- **Client Actions (12)** — Trigger any ConfigMgr client schedule or State Message Cache Cleanup across all targeted devices.
- **Client Tools (13)** — *Client Information*, *Show Collections - Advanced*, *Add Device(s) to Collection*, *Device Status Messages*, *Open Client Log File Folder*, *Open Client Installation Log File Folder*, *Refresh Updates Compliance State*, *Rerun Deployment*, *Restart SMS Agent Host Service*, *Run Client Check*, *Stop ConfigMgr Client Activities*, *Repair Client*, *Uninstall SCCM Client*.
- **Device & Power (6)** — *Ping System(s)*, *Wake On LAN*, *Group Policy Update*, *Send Notification*, *Schedule Restart or Shutdown*, *Cancel Pending Restart or Shutdown*.
- **Security (5)** — *View LAPS Password*, *View BitLocker Recovery Key*, *Windows Defender - Quick Scan*, *Windows Defender - Full Scan*, *Windows Defender - Update Definitions*.
- **AD & ConfigMgr (5)** — *AD Security Groups*, *Enable Device(s) in Active Directory*, *Disable Device(s) in Active Directory*, *Delete Device(s) from Active Directory*, *Delete Device(s) From ConfigMgr*.

---

## Command-Line Invocation (Advanced / Scripting)

`OpenCMTools.App.exe` also supports CLI arguments (used by `Executable` mode XML extensions and available for custom scripts):

```powershell
# Open System Information for multiple devices
OpenCMTools.App.exe --action "ConsoleTool.SystemInformation" --device "PC-01,PC-02,PC-03"

# Open Remote Windows Security for a collection
OpenCMTools.App.exe --action "RemoteTool.RemoteWindowsSecurity" --collection-id "SMS00001" --collection-name "All Systems"

# Open a dashboard directly
OpenCMTools.App.exe --action "Dashboard.BitLockerCompliance"
```
