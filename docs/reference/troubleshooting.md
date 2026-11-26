# Troubleshooting

## 1. OpenRCT Menus or Dashboards Do Not Appear in MECM Console

1. **Check the MECM 2103+ Hierarchy Setting:**
   - Open MECM Console → **Administration → Site Configuration → Sites → Hierarchy Settings → General**.
   - Ensure **`Only allow console extensions that are approved for the hierarchy`** is **unchecked**.
   - Close and relaunch the MECM Console.
2. **Inspect the Install Log:**
   - Open `C:\ProgramData\OpenRCT\install.log`.
   - Confirm the MECM Admin Console path was detected and `OpenRCT.ConsoleExtension.dll` bound cleanly (`ActionsBound = True`, `DashboardsBound = True`).
   - If your console is installed in a non-standard path, re-run:
     ```powershell
     & "C:\Program Files\OpenRCT\scripts\Install-OpenRCT.ps1" -ConsoleRoot "D:\Path\To\AdminConsole" -SkipBinaryCopy
     ```
3. **Close MECM Console Before Repairing:**
   - If the console held an exclusive lock during installation, close all `Microsoft.ConfigurationManagement.exe` instances and re-run Repair on `OpenRCT.msi` (or re-run `Install-OpenRCT.ps1`).

---

## 2. MECM Console Icons Didn't Change After Switching Icon Style

- Changing **Icon Style** (`Fluent Duotone` vs `Crisp Monochrome Outline`) updates WPF windows immediately, while MECM Console tree and right-click menu icons are loaded from `<AdminConsole>\bin\OpenRCT.Icons.dll` at console startup.
- Open **OpenRCT Settings → Appearance & Security**, click **Apply to MECM Console** (approve the UAC prompt if non-elevated), and **restart the MECM Admin Console**.
- If the swap still fails, verify both `OpenRCT.Icons.Duotone.dll` and `OpenRCT.Icons.Outline.dll` exist in `C:\Program Files\OpenRCT\` and check `C:\ProgramData\OpenRCT\install.log`.

---

## 3. "Access Denied" or "RPC Server Is Unavailable" on Remote Devices

- Ensure the account running the MECM Console (or Standalone App) is a member of the target endpoint's local **Administrators** group.
- Verify Windows Firewall rules on the target endpoint allow:
  - **Windows Management Instrumentation (WMI-In)** (TCP `135` + Dynamic RPC)
  - **Remote Event Log Management** (for Remote Event Viewer)
  - **File and Printer Sharing (SMB-In)** (TCP `445`, for `C$`, logs, and Remote Registry)
  - **Windows Remote Management (HTTP-In)** (TCP `5985`, for Interactive PowerShell or WinRM transport)
- Increase **Connection Timeout** in **Settings → Connections & Performance** for slow VPN/WAN links.

---

## 4. Remote Registry Editor or System Information → Applications Fails

- System Information enumerates installed software safely via the **Remote Registry** (avoiding `Win32_Product`, which triggers MSI self-repair).
- Confirm the **Remote Registry** (`RemoteRegistry`) service is running or enabled (Manual Trigger Start) on the target endpoint and TCP `445` is reachable.

---

## 5. View LAPS Password Shows "None" or "Encrypted"

- Confirm your admin account has delegated **Read** rights on `msLAPS-Password` (Windows LAPS) or `ms-Mcs-AdmPwd` (Legacy LAPS) in Active Directory.
- On Windows LAPS endpoints, confirm the GPO `Configure password backup directory` is set to **Active Directory** and check Event Viewer → `Microsoft-Windows-LAPS/Operational`.
- If `ADPasswordEncryptionEnabled` is enabled (`1`), Windows LAPS stores an encrypted blob in `msLAPS-EncryptedPassword` instead of cleartext `msLAPS-Password`; OpenRCT detects this and reports that an encrypted Windows LAPS password is present.

---

## 6. Standalone App Cannot Find SMS Provider

- If running `OpenRCT.App.exe` on a workstation without the MECM Admin Console installed, open **Settings → Connections & Performance** and explicitly enter your **Default SMS Provider Server** (FQDN) and **Default Site Code** (3 characters), then click **Save**.
