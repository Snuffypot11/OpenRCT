# Client Actions & Client Tools

OpenRCT provides one-click access to all 12 ConfigMgr Client Actions and 11 Client Tools across single devices, multi-selected devices, and entire Device Collections.

---

## Client Actions (12)

All actions execute via WMI (`root\ccm:SMS_Client.TriggerSchedule`) concurrently across all targeted devices, streaming live results into the **Bulk Action Results** window.

| Action Name | ConfigMgr Schedule GUID | Purpose |
|---|---|---|
| **Application Deployment Evaluation Cycle** | `{00000000-0000-0000-0000-000000000121}` | Re-evaluates all assigned Application deployments |
| **Discovery Data Collection Cycle** | `{00000000-0000-0000-0000-000000000003}` | Sends a fresh Discovery Data Record (DDR) to the MP |
| **File Collection Cycle** | `{00000000-0000-0000-0000-000000000010}` | Runs configured file collection rules |
| **Hardware Inventory Cycle** | `{00000000-0000-0000-0000-000000000001}` | Triggers an immediate hardware inventory scan |
| **Machine Policy Retrieval & Evaluation Cycle** | `{00000000-0000-0000-0000-000000000021}` + `{00000000-0000-0000-0000-000000000022}` | Downloads new machine policies and immediately evaluates them |
| **Send Unsent State Messages** | `{00000000-0000-0000-0000-000000000111}` | Flushes queued state messages to the Management Point |
| **Software Inventory Cycle** | `{00000000-0000-0000-0000-000000000002}` | Triggers a software inventory scan |
| **Software Metering Usage Report Cycle** | `{00000000-0000-0000-0000-000000000031}` | Reports metered application usage |
| **Software Updates Deployment Evaluation Cycle** | `{00000000-0000-0000-0000-000000000108}` | Re-evaluates assigned Software Update deployments |
| **Software Updates Scan Cycle** | `{00000000-0000-0000-0000-000000000113}` | Scans endpoint against WSUS / Software Update Point |
| **State Message Cache Cleanup** | `RefreshServerComplianceState` / cache cleanup | Forces a fresh state message re-sync with the site |
| **Windows Installer Source List Update Cycle** | `{00000000-0000-0000-0000-000000000032}` | Updates MSI source paths to nearest Distribution Points |

---

## Client Tools

### Client Information (Multi-Device)
Opens a multi-tab ConfigMgr client inspector supporting single devices, multi-select, and Device Collections:

- **Client Details:** Assigned site code, client version, GUID, Management Point, cache directory, configured cache size & free space, and `CcmExec` service status.
- **Components:** Lists installed ConfigMgr client components (`SmsClient`, `CcmFramework`, `SmsSoftwareDistribution`, etc.) with version and enabled state — grouped across devices with expandable device rows.
- **Cache Manager:** Lists cached content folders in `C:\Windows\ccmcache` (`Content ID`, `Location`, `Size MB`, `Last Referenced`) with **bulk Delete Selected Cache Items** across checked devices.

### Show Collections - Advanced
Queries the SMS Provider to display:

- Every **Device Collection** the target device belongs to (Collection ID, Name, Member Count, Limiting Collection, Direct vs Query rule).
- All **Maintenance Windows** effective for the device (Name, Start Time, Duration, Recurrence, Service Window Type: All Deployments / Software Updates / Task Sequences / Non-Client).

### Client Remediation & Operations
- **Restart SMS Agent Host Service:** Cleanly restarts `CcmExec` across targeted devices.
- **Run Client Check (`ccmeval`):** Launches ConfigMgr Client Health Evaluation (`C:\Windows\CCM\ccmeval.exe`) remotely.
- **Stop ConfigMgr Client Activities:** Cancels active `CcmExec` / `execmgr` jobs safely.
- **Refresh Updates Compliance State:** Calls `root\ccm\SoftwareUpdates\UpdatesStore:CCM_UpdatesStore.RefreshServerComplianceState()` to force an immediate update compliance state report to the MP.
- **Rerun Deployment:** Lets you select a deployed advertisement/application/task sequence and clear/re-trigger its execution schedule on targeted devices.
- **Repair Client:** Triggers `root\ccm:SMS_Client.RepairClient()` remotely.
- **Uninstall SCCM Client:** Executes `C:\Windows\ccmsetup\ccmsetup.exe /uninstall` remotely *(prompts for confirmation when destructive confirmation is enabled)*.

### Log Folders
- **Open Client Log Folder:** Opens `\\<device>\C$\Windows\CCM\Logs` in File Explorer.
- **Open Client Installation Log Folder:** Opens `\\<device>\C$\Windows\ccmsetup\Logs` in File Explorer.
