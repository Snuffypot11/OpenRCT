# Remote Tools

All interactive remote inspection, troubleshooting, and session tools are consolidated under **OpenRCT → Remote Tools**.

---

## System Information (Multi-Device)

Inspect and manage 10 categories of live system data across one device, multiple selected devices, or an entire Device Collection.

![System Information (Multi-Device)](../assets/images/tool-system-information.png)

### Included Tabs (10)
| Tab | Data Source | Multi-Device View & Actions |
|---|---|---|
| **Operating Systems** | `Win32_OperatingSystem` | Side-by-side comparison across devices (OS caption, version, build, install date, last boot, uptime) |
| **Hardware** | `Win32_ComputerSystem`, `Win32_BIOS`, `Win32_Processor`, TPM | Manufacturer, model, serial, BIOS, CPU, RAM, chassis, TPM |
| **Disks** | `Win32_LogicalDisk` | Drive letter, label, filesystem, total size, free space, % free |
| **Applications** | Remote Registry `Uninstall` keys *(safe — never queries `Win32_Product`)* | Grouped by app + version; searchable; **Uninstall** selected apps across checked devices |
| **Windows Updates** | `Win32_QuickFixEngineering` | Grouped by KB ID + description + install date |
| **Services** | `Win32_Service` | Grouped by service; **Start**, **Stop**, **Restart**, and change startup mode across checked devices |
| **Drivers** | `Win32_PnPSignedDriver` | Grouped by driver name, provider, version, signed state |
| **User Profiles** | `Win32_UserProfile` | Grouped/per-device profiles with **Delete Profile** action (skips loaded/system profiles safely) |
| **Local Group Members** | ADSI / `Win32_GroupUser` | Inspect `Administrators` and other local groups; **Add / Remove Member** |
| **Batteries** | `Win32_Battery` / WMI battery status | Design capacity, full charge capacity, health %, charge state |

- **Group by Item Toggle:** On by default for multi-device views; uncheck to switch to a flat list.
- **Cached Tab Loads & Refresh:** Switching tabs caches results for snappy navigation; click **Refresh** to re-query live.
- **Export CSV:** Exports the current tab across all loaded devices.

---

## Remote Software Center (Multi-Device)

Inspect and act on Software Center deployments directly from the admin console — across single or multiple devices.

![Remote Software Center](../assets/images/tool-remote-software-center.png)

### Left Navigation Rail (4 Sections)
- **Applications** (`root\ccm\ClientSDK:CCM_Application`) — View install state (`Installed`, `Available`, `Failed`, `Installing`), applicability, and bulk **Install**, **Uninstall**, or **Repair** across checked devices.
- **Software Updates** (`root\ccm\ClientSDK:CCM_SoftwareUpdate`) — View available/pending/installed updates, deadline, reboot state, and trigger **Install** across checked devices.
- **Task Sequences** (`root\ccm\ClientSDK:CCM_Program`) — View deployed task sequences and programs; trigger **Run** across checked devices.
- **Compliance** (`root\ccm\dcm:SMS_DesiredConfiguration`) — Inspect Configuration Baselines (`Compliant`, `NonCompliant`, `Error`, `Unknown`, last eval time) and trigger **Evaluate** across checked devices.

---

## Remote Windows Security (Multi-Device, 8 Tabs)

Audit and remediate endpoint security posture across one or many devices simultaneously.

![Remote Windows Security](../assets/images/tool-remote-windows-security.png)

| Tab | What It Shows | Built-In Actions |
|---|---|---|
| **BitLocker** | Drive letter, protection status, encryption %, conversion status, cipher method, key protectors (`root\CIMV2\Security\MicrosoftVolumeEncryption`) | **Suspend Protection**, **Resume Protection**, **View Recovery Keys** |
| **TPM** | Spec/TCG version, Activated, Enabled, Owned, Ready, Manufacturer, Manufacturer Version, FIPS Ready, Locked Out (`root\CIMV2\Security\MicrosoftTpm`) | **Clear TPM** *(with confirmation)* |
| **Firmware** | UEFI enabled, Secure Boot state, BIOS version, **Secure Boot 2023 CA Certificate** update readiness (`WindowsUEFICA2023Capable`), status, error code, and confidence level | **Run Secure Boot Update Task** (`\Microsoft\Windows\PI\Secure-Boot-Update`) |
| **Virtualization-Based Security** | VBS status (`Running`/`Enabled`/`Not Enabled`), Credential Guard, HVCI (Memory Integrity), running & configured security services | Live refresh & CSV export |
| **Defender** | Antivirus enabled, Real-Time Protection, Antimalware Client Version, Engine Version, Signature Version, Last Signature Update, Last Quick/Full Scan (`root\Microsoft\Windows\Defender`) | **Quick Scan**, **Full Scan**, **Update Definitions** |
| **Defender Exclusions** | Configured Path, Extension, Process, and IP Address exclusions | Audit & CSV export |
| **Windows Firewall** | Domain, Private, and Public profile enabled states and default Inbound/Outbound actions | Audit & CSV export |
| **Firewall Rules** | Display name, Action (`Allow`/`Block`), Direction (`Inbound`/`Outbound`), Profiles, Enabled state (`root\StandardCimv2:MSFT_NetFirewallRule`) | **Enable Rule**, **Disable Rule** |

---

## Remote Event Viewer

Browse and filter remote Windows Event Logs natively via `System.Diagnostics.Eventing.Reader.EventLogSession` (RPC / Event Log service) in a fast split-pane viewer.

![Remote Event Viewer](../assets/images/tool-remote-event-viewer.png)

### Key Features
- **Hierarchical Log Tree:** Left sidebar lists **Windows Logs** (`Application`, `Security`, `Setup`, `System`, `ForwardedEvents`) plus expandable **Applications and Services Logs** (`Microsoft → Windows → ...`, ConfigMgr, Defender, BitLocker, LAPS, etc.).
- **Auto-Applying Server-Side XPath Filter Bar:**
  - **Device** selector (when multiple devices are targeted)
  - **Severity:** `All`, `Critical & Error`, `Critical`, `Error`, `Warning`, `Information`
  - **Source / Provider** substring filter
  - **Event ID(s):** Comma-separated list (e.g., `1000, 7036, 4625`)
  - **Start Date / End Date** range pickers (converted to UTC XPath `TimeCreated`)
  - **Max Entries:** `100`, `250`, `500`, `1000`
  - **Keyword:** Client-side search across message text, source, and ID
- **Pagination:** Clean `Previous` / `Page N` / `Next` paging for responsive browsing.
- **Detail Pane:** Toggle between formatted **General** message view and raw **XML** view, with **Copy Details** and **Export CSV**.

---

## Remote Registry Editor

Native WPF remote registry explorer built on `Microsoft.Win32.RegistryKey.OpenRemoteBaseKey` (requires the **Remote Registry** service on the target endpoint).

![Remote Registry Editor](../assets/images/tool-remote-registry.png)

- Browse `HKEY_LOCAL_MACHINE`, `HKEY_USERS`, and `HKEY_CURRENT_CONFIG`.
- Lazy-expanding tree with instant path jump bar, key/value search, value type/data display (`REG_SZ`, `REG_DWORD`, `REG_QWORD`, `REG_BINARY`, `REG_MULTI_SZ`), and copy/export actions.

---

## Additional Remote Tools

- **Running Processes:** Live view of remote processes (`Name`, `PID`, `Session`, `User`, `Memory`, `Command Line`) with bulk **Terminate Process** across single or multiple devices.
- **Open `C$` Share:** Opens `\\<device>\C$` directly in Windows File Explorer.
- **Interactive PowerShell Prompt:** Launches an interactive WinRM PowerShell remoting session (`Enter-PSSession -ComputerName <device>`).
- **Interactive Command Prompt *(Optional PsExec)*:** Launches an interactive remote `cmd.exe` session using your configured Sysinternals `PsExec.exe` / `PsExec64.exe` path in [Settings](../getting-started/settings.md).
- **Manage Computer:** Launches Microsoft Computer Management (`compmgmt.msc /computer=\\<device>`).
