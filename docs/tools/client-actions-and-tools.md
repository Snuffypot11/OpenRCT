# Client Actions & Client Tools

OpenCMTools provides one-click access to all **12 ConfigMgr Client Actions** and **13 Client Tools** on Devices (with a curated subset of **6 Client Actions** and **5 Client Tools** exposed on the Device Collection menu, and all tools runnable against collections via the [Standalone App](../getting-started/standalone-app.md)).

---

## Client Actions (12)

All actions execute via WMI (`root\ccm:SMS_Client.TriggerSchedule`) concurrently across all targeted devices, streaming live results into the **Bulk Action Results** window (`State Message Cache Cleanup` additionally refreshes updates compliance state via COM before triggering `{00000000-0000-0000-0000-000000000111}`).

| Action Name | ConfigMgr Schedule GUID | Purpose |
|---|---|---|
| **Application Deployment Evaluation Cycle** | `{00000000-0000-0000-0000-000000000121}` | Re-evaluates all assigned Application deployments |
| **Discovery Data Collection Cycle** | `{00000000-0000-0000-0000-000000000003}` | Sends a fresh Discovery Data Record (DDR) to the MP |
| **File Collection Cycle** | `{00000000-0000-0000-0000-000000000010}` | Runs configured file collection rules |
| **Hardware Inventory Cycle** | `{00000000-0000-0000-0000-000000000001}` | Triggers an immediate hardware inventory scan |
| **Machine Policy Retrieval and Evaluation Cycle** | `{00000000-0000-0000-0000-000000000021}` + `{00000000-0000-0000-0000-000000000022}` | Downloads new machine policies and immediately evaluates them |
| **Send Unsent State Messages** | `{00000000-0000-0000-0000-000000000111}` | Flushes queued state messages to the Management Point |
| **Software Inventory Cycle** | `{00000000-0000-0000-0000-000000000002}` | Triggers a software inventory scan |
| **Software Metering Usage Report Cycle** | `{00000000-0000-0000-0000-000000000031}` | Reports metered application usage |
| **Software Updates Deployment Evaluation Cycle** | `{00000000-0000-0000-0000-000000000108}` | Re-evaluates assigned Software Update deployments |
| **Software Updates Scan Cycle** | `{00000000-0000-0000-0000-000000000113}` | Scans endpoint against WSUS / Software Update Point |
| **State Message Cache Cleanup** | `Microsoft.CCM.UpdatesStore.RefreshServerComplianceState()` + `{00000000-0000-0000-0000-000000000111}` | Refreshes compliance state via COM and triggers Send Unsent State Messages |
| **Windows Installer Source List Update Cycle** | `{00000000-0000-0000-0000-000000000032}` | Updates MSI source paths to nearest Distribution Points |

---

## Client Tools (13)

### Client Information (Multi-Device)
Opens a multi-tab ConfigMgr client inspector supporting single devices, multi-select, and Device Collections:

- **Client Details:** Assigned site code, client version, GUID, Management Point, cache used (MB) & configured max cache size (MB), `CcmExec` service status, and *(in single-device view)* cache directory path.
- **Components:** Lists installed ConfigMgr client components (`root\ccm:CCM_InstalledComponent` — `SmsClient`, `CcmFramework`, `SmsSoftwareDistribution`, etc.) with version and `Installed` state — grouped across devices with expandable device rows.
- **Cache:** Lists cached content entries from `root\ccm\SoftMgmtAgent:CacheInfoEx` (`Content ID`, `Ver`, `Location`, `Size (MB)`, `Last Referenced`) with bulk **Delete Selected Cache Item(s)** across checked devices.

### Show Collections - Advanced
Queries the SMS Provider to display:

- Every **Device Collection** the target device belongs to (`Device`, `Collection ID`, `Collection Name`, `Membership` [`Direct` or `Query / Include`], `Members`, `Comment`).
- All **Maintenance Windows** effective for the device (`Collection`, `Window Name`, `Type` [`All Deployments`, `Software Updates`, `Task Sequences`], `Enabled`, `Duration (min)`, `Description`).

### Collection and Status Message Tools
- **Add Device(s) to Collection:** Search ConfigMgr Device Collections and add direct membership rules (`SMS_Collection.AddMembershipRule`) for all targeted devices.
- **Device Status Messages:** Queries `SMS_StatusMessage` from the SMS Provider for targeted device(s) with time range (`Last 24 Hours`, `Last 7 Days`, `Last 30 Days`), severity (`All Severities`, `Error`, `Warning`, `Informational`), and keyword search (matching component, description, and message ID), plus CSV export.

### Client Remediation & Operations
- **Restart SMS Agent Host Service:** Cleanly restarts `CcmExec` across targeted devices.
- **Run Client Check (`ccmeval`):** Launches ConfigMgr Client Health Evaluation (`C:\Windows\CCM\ccmeval.exe`) remotely.
- **Stop ConfigMgr Client Activities:** Stops the `CcmExec` (SMS Agent Host) service via WMI `Win32_Service.StopService` *(prompts for confirmation when destructive confirmation is enabled)*.
- **Refresh Updates Compliance State:** Launches remote `powershell.exe` via `Win32_Process.Create` to invoke `(New-Object -ComObject Microsoft.CCM.UpdatesStore).RefreshServerComplianceState()`, forcing an immediate update compliance state report to the MP.
- **Rerun Deployment:** Triggers **Machine Policy Retrieval and Evaluation Cycle** followed by **Application Deployment Evaluation Cycle** across targeted devices to re-evaluate and rerun deployments.
- **Repair Client:** Triggers `root\ccm:SMS_Client.RepairClient()` remotely *(prompts for confirmation when destructive confirmation is enabled)*.
- **Uninstall SCCM Client:** Executes `C:\Windows\ccmsetup\ccmsetup.exe /uninstall` remotely *(prompts for confirmation when destructive confirmation is enabled)*.

### Log Folders *(Single Device Only)*
- **Open Client Log File Folder:** Opens `\\<device>\C$\Windows\CCM\Logs` in File Explorer.
- **Open Client Installation Log File Folder:** Opens `\\<device>\C$\Windows\ccmsetup\Logs` in File Explorer.
