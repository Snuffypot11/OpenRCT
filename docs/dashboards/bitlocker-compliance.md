# BitLocker Compliance Dashboard

Audit on-premises **BitLocker** recovery key escrow and volume compliance across **Active Directory** and **Configuration Manager** (strictly on-premises — no Entra ID / Intune dependencies).

![BitLocker Compliance Dashboard](../assets/images/dashboard-bitlocker.png)

---

## How to Use

1. Choose your scope mode:
   - **Search By AD OU** *(multi-select; sub-OUs are included automatically; leave blank for the entire domain)*, or
   - **Search By Collection** *(multi-select; leave blank for all devices)*
2. Click **Scan** to query `msFVE-RecoveryInformation` objects in Active Directory and ConfigMgr BitLocker inventory (`SMS_G_System_ENCRYPTABLE_VOLUME`, `SMS_BitLockerRecoveryKey`, and `SMS_G_System_BITLOCKER_DETAILS`).

---

## Interactive Charts

- **BitLocker Recovery Key Storage:** Breaks down devices by where recovery keys are backed up (`Computers without stored keys`, `Computers with keys stored in AD`, `Computers with keys stored in ConfigMgr`, `Computers with keys stored in multiple locations`; grid column values: `None`, `AD`, `CM`, `AD, CM`).
- **Unified Compliance:** Breaks down encryption/protection compliance (`CM Compliant`, `Encrypted (Unmanaged)`, `Non-Compliant`, `Unknown`).

Click any slice on either chart to filter the device grid (click again to reset).

---

## Row Actions & Export

Right-click any device row to:

- **View BitLocker Recovery Key** (with copy + auto-clear clipboard)
- **System Information** or **Client Information**
- **Machine Policy Retrieval & Evaluation** or **Ping System(s)**

Click **Export to CSV** in the bottom bar for audit reporting.
