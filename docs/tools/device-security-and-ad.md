# Device & Power Tools, Security Tools & AD / ConfigMgr Tools

---

## Device and Power Tools

| Tool | Description |
|---|---|
| **Ping System(s)** | Continuous or on-demand ICMP reachability monitor across 1..N devices showing online/offline state, resolved IP, round-trip latency (ms), and CSV export. |
| **Wake On LAN** | Looks up each target device's MAC address from the ConfigMgr SMS Provider (`SMS_G_System_NETWORK_ADAPTER_CONFIGURATION`) and broadcasts standard magic packets on **UDP ports 9 and 7** (`255.255.255.255`). |
| **Group Policy Update** | Launches `gpupdate.exe /force` asynchronously via `Win32_Process.Create` across targeted devices and reports the remote process ID. |
| **Send Notification** | Sends a Windows message dialog (`msg.exe * /SERVER:<host> /TIME:<seconds>`) to logged-on users on targeted devices with a custom message body and display timeout (default `300`s, minimum `10`s). |
| **Schedule Restart or Shutdown** | Supports **Restart** or **Shutdown**, **Immediate** or **Delayed/Scheduled** (countdown in **seconds** with quick presets `Now (0s)`, `1 min`, `5 min`, `15 min`, or exact date/time), **Force close apps**, optional **Skip if a user is logged on** (via WMI), and custom user notice via `shutdown.exe`. *(Prompts for confirmation when safety controls are enabled.)* |
| **Cancel Pending Restart or Shutdown** | Aborts an active scheduled restart/shutdown (`shutdown.exe /a`) on targeted devices. |

---

## Security Tools

### View LAPS Password *(Legacy & Windows LAPS)*
Queries Active Directory natively via `System.DirectoryServices` (zero RSAT PowerShell module required) and supports **both**:

- **Windows LAPS** (`msLAPS-Password`, `msLAPS-EncryptedPassword`, & `msLAPS-PasswordExpirationTime`)
- **Legacy Microsoft LAPS** (`ms-Mcs-AdmPwd` & `ms-Mcs-AdmPwdExpirationTime`)

Features masked password display with **Reveal Passwords**, one-click **Copy Selected Password**, and automatic clipboard clearing after your configured delay (default **30 seconds**).

!!! note "Windows LAPS Encryption"
    If your domain uses Windows LAPS with AD password encryption enabled (`msLAPS-EncryptedPassword`), the viewer detects the encrypted attribute, displays **`Windows LAPS (Encrypted)`** and **`Encrypted`** (instead of `None`), and shows the expiration timestamp. Cleartext `msLAPS-Password` (when `ADPasswordEncryptionEnabled = 0`) and Legacy LAPS `ms-Mcs-AdmPwd` are displayed directly.

### View BitLocker Recovery Key *(AD & ConfigMgr)*
Reconciles and displays BitLocker recovery keys from **both** on-premises sources simultaneously:

- **Active Directory:** `msFVE-RecoveryInformation` child objects (`msFVE-RecoveryPassword`, Key Protector GUID, creation timestamp)
- **ConfigMgr / MBAM:** `SMS_BitLockerRecoveryKey` via the SMS Provider

Includes one-click **Copy Selected Key** with automatic clipboard clearing. *(Strictly on-premises AD and ConfigMgr — no Entra ID / Intune calls.)*

### Windows Defender Actions
Trigger native Defender actions across 1..N devices via `root\Microsoft\Windows\Defender:MSFT_MpScan` and `MSFT_MpSignature`:

- **Windows Defender - Quick Scan**
- **Windows Defender - Full Scan**
- **Windows Defender - Update Definitions**

*(For deeper Defender status, exclusions, BitLocker, TPM, Firmware/Secure Boot 2023 CA, VBS, and Firewall management, see [Remote Windows Security](remote-tools.md#remote-windows-security-multi-device-8-tabs).)*

---

## Active Directory and ConfigMgr Tools

| Tool | Description |
|---|---|
| **AD Security Groups** | Inspect the computer account's Active Directory group memberships (`memberOf`), and **Add** (by group CN, `sAMAccountName`, or Distinguished Name) or **Remove** group memberships directly via LDAP/ADSI. |
| **Enable Device(s) in Active Directory** | Clears the `ACCOUNTDISABLE` flag in `userAccountControl` on selected AD computer accounts. |
| **Disable Device(s) in Active Directory** | Sets the `ACCOUNTDISABLE` flag in `userAccountControl` *(with confirmation)*. |
| **Delete Device(s) from Active Directory** | Deletes the computer object (and sub-objects such as BitLocker recovery info) from Active Directory *(with confirmation)*. |
| **Delete Device(s) From ConfigMgr** | Removes the `SMS_R_System` resource record from Configuration Manager *(with confirmation)*. |

*(Looking for **Add Device(s) to Collection** or **Device Status Messages**? Both live under **Client Tools** — see [Client Actions & Client Tools](client-actions-and-tools.md#collection-and-status-message-tools).)*
