# Endpoint Tools Overview & Multi-Device UI

Right-click any **single Device**, **multiple selected Devices**, or **Device Collection** inside the Configuration Manager Admin Console and expand the **OpenCMTools** menu (also mirrored on the MECM Home Ribbon).

![Device Right-Click Context Menu](../assets/images/context-menu-device.png)

---

## Reorganized 6-Group Menu Layout

OpenCMTools groups tools logically by workflow rather than scattering remote utilities across unrelated menus:

| Menu Group | Device Menu | Device Collection Menu |
|---|---|---|
| **1. Remote Tools** | System Information, Remote Software Center, Remote Windows Security, Remote Registry Editor, Remote Event Viewer, Running Processes, Connect to `C$` *(single device)*, Interactive PowerShell Prompt *(single device)*, Interactive Command Prompt *(PsExec, single device)*, Manage Computer *(single device)* | System Information, Remote Software Center, Remote Windows Security, Running Processes |
| **2. Client Actions** | All 12 ConfigMgr Client Actions *(11 schedules + State Message Cache Cleanup)* | 6 Actions: Application Deployment Evaluation Cycle, Discovery Data Collection Cycle, Hardware Inventory Cycle, Machine Policy Retrieval and Evaluation Cycle, Software Updates Deployment Evaluation Cycle, Software Updates Scan Cycle |
| **3. Client Tools** | Client Information, Show Collections - Advanced, Add Device(s) to Collection, Device Status Messages, Open Client Log File Folder *(single device)*, Open Client Installation Log File Folder *(single device)*, Refresh Updates Compliance State, Rerun Deployment, Restart SMS Agent Host Service, Run Client Check, Stop ConfigMgr Client Activities, Repair Client, Uninstall SCCM Client | 5 Tools: Client Information, Refresh Updates Compliance State, Restart SMS Agent Host Service, Run Client Check, Repair Client |
| **4. Device and Power Tools** | Ping System(s), Wake On LAN, Group Policy Update, Send Notification, Schedule Restart or Shutdown, Cancel Pending Restart or Shutdown | Ping System(s), Wake On LAN, Group Policy Update, Send Notification, Schedule Restart or Shutdown, Cancel Pending Restart or Shutdown |
| **5. Security Tools** | View LAPS Password, View BitLocker Recovery Key, Windows Defender - Quick Scan, Windows Defender - Full Scan, Windows Defender - Update Definitions | 2 Tools: Windows Defender - Quick Scan, Windows Defender - Update Definitions |
| **6. Active Directory and ConfigMgr** | AD Security Groups, Enable Device(s) in Active Directory, Disable Device(s) in Active Directory, Delete Device(s) from Active Directory, Delete Device(s) From ConfigMgr | *(Device menu only)* |
| **OpenCMTools Settings** | Available at the root of the OpenCMTools menu | *(Device menu only)* |

*(Items marked **single device** use `SelectionMode="Single"` and are hidden when multiple devices are selected. In `Executable` fallback mode, only a reduced 11-tool device menu plus OpenCMTools Settings is shown on the context menu.)*

---

## Multi-Device Expandable Item-First UI

When targeting **2 or more devices** (via multi-select or a Device Collection) in **System Information**, **Remote Software Center**, **Remote Windows Security**, or **Client Information**:

1. **Concurrent Fan-Out:** All targeted devices are queried in parallel up to your configured `MaxConcurrentDevices` setting (default `16`).
2. **Item-First Grouping:** Instead of making you pick one device at a time from a top-left dropdown, items (applications, updates, services, compliance baselines, TPM states, firewall rules, cache items, etc.) are aggregated into a single list showing **applicable device counts** and **status summaries** (e.g., `12 / 15 installed`, `8 / 8 compliant`).
3. **Expandable Applicable Devices:** Click the **`▸`** chevron on any item row (or use **Expand All / Collapse All**) to inspect every applicable device underneath with resizable columns and smooth horizontal scrolling.
4. **Synced Bulk Selection:** Check the parent item checkbox to select all devices underneath it, or expand the row and check only specific devices — then click **Install**, **Uninstall**, **Repair**, **Restart Service**, **Evaluate**, **Delete**, etc. to execute in bulk.
5. **Device Filter & Flat View Toggle:** Filter to a single device using the **Device Filter** dropdown (top-left sidebar in System Information; top-right in Remote Software Center, Remote Windows Security, and Client Information), or uncheck **Group by Item** on grouped System Information tabs (`Applications`, `Windows Updates`, `Services`, `Drivers`, `User Profiles`, `Local Group Members`) to inspect a flat per-device list.
6. **Error Drawer & CSV Export:** Unreachable or access-denied devices never block successful devices — failures collect neatly in the collapsible **Error Drawer**, and **System Information**, **Remote Software Center**, and **Remote Windows Security** support one-click **Export to CSV**.
