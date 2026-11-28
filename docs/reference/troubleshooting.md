# Troubleshooting

## 1. OpenCMTools Menus or Dashboards Do Not Appear in MECM Console

1. **Check the MECM 2103+ Hierarchy Setting:**
   - Open MECM Console → **Administration → Site Configuration → Sites → Hierarchy Settings → General**.
   - Ensure **`Only allow console extensions that are approved for the hierarchy`** is **unchecked**.
   - Close and relaunch the MECM Console.
2. **Inspect the Install Log:**
   - Open `C:\ProgramData\OpenCMTools\install.log`.
   - Confirm the MECM Admin Console path was detected and `OpenCMTools.ConsoleExtension.dll` bound cleanly (`Successfully bound OpenCMTools.ConsoleExtension.dll (...)` and `EffectiveMode=Assembly, DashboardsBound=True`).
   - If your console is installed in a non-standard path, re-run:
     ```powershell
     & "C:\Program Files\OpenCMTools\scripts\Install-OpenCMTools.ps1" -ConsoleRoot "D:\Path\To\AdminConsole" -SkipBinaryCopy
     ```
3. **Close MECM Console Before Repairing:**
   - If the console held an exclusive lock during installation, close all `Microsoft.ConfigurationManagement.exe` instances and re-run Repair on `OpenCMTools.msi` (or re-run `Install-OpenCMTools.ps1`).

---

## 2. MECM Console Icons Didn't Change After Switching Icon Style

- Changing **Icon Style** (`Fluent Duotone` vs `Crisp Monochrome Outline`) updates WPF windows immediately when saved, while MECM Console tree and right-click menu icons are loaded from `<AdminConsole>\bin\OpenCMTools.Icons.dll` at console startup.
- Open **OpenCMTools Settings → Appearance & Security**, click **Apply to MECM Console** (approve the UAC prompt if non-elevated), and **restart the MECM Admin Console**.
- If the swap still fails, verify both `OpenCMTools.Icons.Duotone.dll` and `OpenCMTools.Icons.Outline.dll` exist in `C:\Program Files\OpenCMTools\` and check `C:\ProgramData\OpenCMTools\install.log`.

---

## 3. "Access Denied" or "RPC Server Is Unavailable" on Remote Devices

- Ensure the account running the MECM Console (or Standalone App) is a member of the target endpoint's local **Administrators** group.
- Verify Windows Firewall rules on the target endpoint allow:
  - **Windows Management Instrumentation (WMI-In)** (TCP `135` + Dynamic RPC)
  - **Remote Event Log Management** (for Remote Event Viewer)
  - **File and Printer Sharing (SMB-In)** (TCP `445`, for `C$`, logs, and Remote Registry)
  - **Windows Remote Management (HTTP-In)** (TCP `5985`, for Interactive PowerShell Prompt)
- Increase **Connection Timeout** in **Settings → Connections & Performance** for slow VPN/WAN links.

---

## 4. Remote Registry Editor or System Information → Applications Fails

- System Information enumerates installed software safely via the **Remote Registry** (avoiding `Win32_Product`, which triggers MSI self-repair).
- Confirm the **Remote Registry** (`RemoteRegistry`) service is running or enabled (Manual Trigger Start) on the target endpoint and TCP `445` is reachable.

---

## 5. View LAPS Password Shows "None" or "Encrypted"

- Confirm your admin account has delegated **Read** rights on `msLAPS-Password` / `msLAPS-EncryptedPassword` (Windows LAPS) or `ms-Mcs-AdmPwd` (Legacy LAPS) in Active Directory.
- On Windows LAPS endpoints, confirm the GPO `Configure password backup directory` is set to **Active Directory** and check Event Viewer → `Microsoft-Windows-LAPS/Operational`.
- If `ADPasswordEncryptionEnabled` is enabled (`1`), Windows LAPS stores an encrypted blob in `msLAPS-EncryptedPassword` instead of cleartext `msLAPS-Password`; OpenCMTools detects `msLAPS-EncryptedPassword` and displays **`Windows LAPS (Encrypted)`** / **`Encrypted`** (with expiration timestamp) rather than `None`. Decrypt encrypted passwords using `Get-LapsADPassword` or Active Directory Users and Computers (ADUC).

---

## 6. Standalone App Cannot Find SMS Provider

- If running `OpenCMTools.App.exe` on a workstation without the MECM Admin Console installed, open **Settings → Connections & Performance** and explicitly enter your **Default SMS Provider Server** (FQDN) and **Default Site Code** (3 characters), then click **Save**.
