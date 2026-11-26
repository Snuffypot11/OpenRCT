# Hardware and Firmware Audit Dashboard

Audit hardware models, chassis types, UEFI, Secure Boot, TPM, BIOS versions, and OS builds across any **ConfigMgr Device Collection** — ideal for Windows 11 readiness, BIOS upgrade campaigns, and Secure Boot / TPM audits.

![Hardware and Firmware Audit Dashboard](../assets/images/dashboard-hardware-audit.png)

---

## Customizable Charts & Nested Filtering

1. Select a **Device Collection** and click **Scan** to query ConfigMgr hardware inventory (`SMS_G_System_COMPUTER_SYSTEM`, `SMS_G_System_SYSTEM_ENCLOSURE`, `SMS_G_System_PC_BIOS`, `SMS_G_System_FIRMWARE`, `SMS_G_System_TPM`, `SMS_G_System_OPERATING_SYSTEM`).
2. Click **Select Charts** to choose which pie charts are displayed (up to 8 dimensions):
   - **Chassis** (`Laptop`, `Desktop`, `Virtual`, `Server`, etc.)
   - **Manufacturer**
   - **Model**
   - **UEFI**
   - **Secure Boot**
   - **TPM**
   - **BIOS Version**
   - **OS Version**
3. **Single vs Nested Filtering:**
   - By default, clicking a slice filters the device table to that single attribute.
   - Check **Nested Filtering** to stack filters cumulatively across multiple charts (for example: `Chassis = Laptop` → `Manufacturer = Dell` → `Secure Boot = Disabled`) with active filter chips and one-click reset.

---

## Row Actions

Right-click one or more rows in the grid to:

- Open **System Information** or **Remote Windows Security** (including 2023 Secure Boot CA readiness)
- Trigger **Hardware Inventory Cycle** or **Machine Policy Retrieval & Evaluation Cycle**
- **Export CSV** of the filtered inventory slice.
