# Content Distribution Monitor

Monitor **Distribution Point (DP)** health across your ConfigMgr hierarchy, inspect failed or in-progress content transfers, and remediate packages directly from the dashboard.

![Content Distribution Monitor Dashboard](../assets/images/dashboard-content-distribution.png)

---

## Dashboard Layout

1. **Interactive Distribution Point Stacked Bar Chart:** Displays every Distribution Point with color-coded counts for **Success**, **In Progress**, and **Failed** content items. Click any DP bar to focus the properties inspector and content table on that DP.
2. **DP Properties Inspector:** Displays site code, FQDN/NAL path, Pull DP / PXE / BranchCache settings, and overall success/failure totals for the selected Distribution Point.
3. **Filterable Content Grid:** Filter by **State** (`All`, `Failed`, `In Progress`, `Success`), **Content Type** (Applications, Packages, Boot Images, OS Images, Driver Packages, Software Update Packages), or search by Package ID / Name.

---

## Built-In Content Actions

Select one or more content rows (or right-click a row) to perform native SMS Provider operations:

| Action | Underlying SMS Provider Call | What It Does |
|---|---|---|
| **View Content Status** | Queries `SMS_PackageStatusDistPointsSummarizer` | Opens a detailed **Content Status** dialog listing every Distribution Point holding that package, its state, last status time, and summary |
| **Redistribute Content to DP** | Sets `RefreshNow = true` on `SMS_DistributionPoint` | Forces the Distribution Manager to re-send content to the selected DP |
| **Validate Content on DP** | Calls static `SMS_DistributionPoint.VerifyPackage(PackageId, NALPath)` | Schedules content integrity hash verification on the selected DP |
| **Remove Content from DP** | Deletes the `SMS_DistributionPoint` association *(with confirmation)* | Removes the package from the selected Distribution Point |
| **Redistribute All Failed Transfers** | Bulk `RefreshNow = true` across all failed DP rows | One-click bulk remediation for failed transfers shown in the dashboard |

Every action logs progress and errors cleanly and supports CSV export of the content table.
