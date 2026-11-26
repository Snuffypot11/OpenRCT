# Device & Power Tools, Security Tools & AD / ConfigMgr Tools

---

## Device & Power Tools

| Tool | Description |
|---|---|
| **Ping Systems** | Continuous or on-demand ICMP reachability monitor across 1..N devices showing online/offline state, resolved IP, round-trip latency (ms), and CSV export. |
| **Wake on LAN (WOL)** | Looks up each target device's MAC address from the ConfigMgr SMS Provider (`SMS_G_System_NETWORK_ADAPTER_CONFIGURATION`) and broadcasts standard UDP port 9 magic packets (supports subnet-directed and global broadcast). |
| **Send Notification** | Sends an interactive desktop toast/message notification to logged-on users on targeted devices with custom title and body text. |
| **Schedule Restart or Shutdown** | Supports **Restart** or **Shutdown**, **Immediate** or **Delayed/Scheduled** (minutes or exact date/time), **Force close apps** toggle, and custom user warning message via `shutdown.exe` / WMI. *(Prompts for confirmation when safety controls are enabled.)* |
| **Cancel Pending Restart / Shutdown** | Aborts an active scheduled restart/shutdown (`shutdown.exe /a`) on targeted devices. |
| **Group Policy Update** | Triggers `gpupdate /force` remotely across targeted devices and reports completion status. |

---

## Security Tools

### View LAPS Password *(Legacy & Windows LAPS)*
Queries Active Directory natively via `System.DirectoryServices` (zero RSAT PowerShell module required) and supports **both**:

- **Windows LAPS** (`msLAPS-Password` & `msLAPS-PasswordExpirationTime`)
- **Legacy Microsoft LAPS** (`ms-Mcs-AdmPwd` & `ms-Mcs-AdmPwdExpirationTime`)

Features masked password display with **Reveal**, one-click **Copy Password**, and automatic clipboard clearing after your configured delay (default **30 seconds**).

!!! note "Windows LAPS Encryption"
    If your domain uses Windows LAPS with AD password encryption enabled (`msLAPS-EncryptedPassword`), the viewer indicates an encrypted blob is present. Cleartext `msLAPS-Password` (when `ADPasswordEncryptionEnabled = 0`) and Legacy LAPS `ms-Mcs-AdmPwd` are displayed directly.

### View BitLocker Recovery Key *(AD & ConfigMgr)*
Reconciles and displays BitLocker recovery keys from **both** on-premises sources simultaneously:

- **Active Directory:** `msFVE-RecoveryInformation` child objects (`msFVE-RecoveryPassword`, Key Protector GUID, creation timestamp)
- **ConfigMgr / MBAM:** `SMS_G_System_ENCRYPTABLE_VOLUME` / `SMS_BitLockerRecoveryKey` via the SMS Provider

Includes one-click **Copy Recovery Key** with automatic clipboard clearing. *(Strictly on-premises AD and ConfigMgr — no Entra ID / Intune calls.)*

### Windows Defender Actions
Trigger native Defender actions across 1..N devices via `root\Microsoft\Windows\Defender:MSFT_MpScan` and `MSFT_MpSignature`:

- **Windows Defender Quick Scan**
- **Windows Defender Full Scan**
- **Update Windows Defender Definitions**

*(For deeper Defender status, exclusions, BitLocker, TPM, Firmware/Secure Boot 2023 CA, VBS, and Firewall management, see [Remote Windows Security](remote-tools.md#remote-windows-security-multi-device-8-tabs).)*

---

## Active Directory & ConfigMgr Tools

| Tool | Description |
|---|---|
| **AD Security Groups** | Inspect the computer account's Active Directory group memberships (`memberOf`), search domain groups, and **Add** or **Remove** group memberships directly via LDAP/ADSI. |
| **Enable Device(s) in Active Directory** | Clears the `ACCOUNTDISABLE` flag in `userAccountControl` on selected AD computer accounts. |
| **Disable Device(s) in Active Directory** | Sets the `ACCOUNTDISABLE` flag in `userAccountControl` *(with confirmation)*. |
| **Delete Device(s) in Active Directory** | Deletes the computer object (and sub-objects such as BitLocker recovery info) from Active Directory *(with confirmation)*. |
| **Add Device(s) to Collection** | Search ConfigMgr Device Collections and add direct membership rules (`SMS_Collection.AddMembershipRules`) for all targeted devices. |
| **Delete Device(s) from ConfigMgr** | Removes the `SMS_R_System` resource record from Configuration Manager *(with confirmation)*. |
| **Device Status Messages** | Queries `SMS_StatusMessage` and `SMS_StatMsgAttributes` from the SMS Provider for a device with severity (`Error`, `Warning`, `Informational`), component, time range, and keyword filtering plus CSV export. |
