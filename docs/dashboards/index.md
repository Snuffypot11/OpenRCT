# Console Dashboards Overview

All 6 OpenRCT dashboards are available directly under **Assets and Compliance → Overview → OpenRCT** inside the MECM Admin Console (with zero extra nesting folders), and can also be popped out into a dedicated window via **Right-Click → Open in New Window** or launched from the [Standalone App](../getting-started/standalone-app.md).

---

## Included Dashboards

| Dashboard | Primary Use Case |
|---|---|
| **[Active Directory Cleanup Tool](ad-cleanup.md)** | Find stale or orphaned computer records that exist only in AD or only in ConfigMgr, and clean them up |
| **[BitLocker Compliance](bitlocker-compliance.md)** | Audit BitLocker recovery key backup across AD & ConfigMgr alongside encryption and compliance status |
| **[Content Distribution Monitor](content-distribution.md)** | Monitor Distribution Point transfer health, inspect per-DP content status, and redistribute, validate, or remove content |
| **[Hardware and Firmware Audit](hardware-firmware-audit.md)** | Audit hardware models, chassis, UEFI, Secure Boot, TPM, BIOS, and OS versions with customizable charts and Nested Filtering |
| **[LAPS Dashboard](laps-dashboard.md)** | Track Legacy Microsoft LAPS and Windows LAPS password coverage and client deployment across Active Directory OUs |
| **[Software Updates Deployment Status](software-updates.md)** | Analyze collection patch compliance by age, deployment status, Software Update Group, and update classification |

---

## Shared Dashboard Capabilities

- **Interactive Native Vector Charts:** Click any pie slice or Distribution Point bar to filter the data grid immediately; click the selected slice again to reset (or use **Clear Filters** on Hardware & Firmware Audit / **Show All DPs** on Content Distribution Monitor).
- **Right-Click Row Actions:** Right-click any device or content row directly inside the dashboard grid to launch OpenRCT remediation tools (e.g., *System Information*, *Client Information*, *Hardware Inventory Cycle*, *Delete from AD/ConfigMgr*, *Redistribute Content*, *Validate Content*).
- **Resizable Columns & Auto Scrollbars:** All columns can be resized and scrolled horizontally without clipping.
- **Collapsible Error Drawer:** Any failed WMI/AD/SMS Provider queries are logged cleanly in the bottom Error Drawer (`Time`, `Source`, `Message`, plus full details via **Copy All**) without interrupting the rest of the scan.
- **CSV Export:** Every dashboard includes a bottom-bar **Export to CSV** button exporting its current filtered (or full) view to standards-compliant RFC 4180 CSV.
