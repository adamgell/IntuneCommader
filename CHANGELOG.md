# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Documentation
- Added `PR_STATUS.md` - comprehensive pull request organization and status tracking document
  - Categorizes all 9 open PRs by priority (P1/P2/P3) and type
  - Documents PR dependencies and recommended merge order
  - Provides detailed analysis of each PR's purpose and risks
  - Includes action items and decision points for repository owner

## [2026-02-18 Release]

### Added
- Added new Graph-backed services and interfaces for:
  - Conditional Access Policies (`IConditionalAccessPolicyService`, `ConditionalAccessPolicyService`)
  - Assignment Filters (`IAssignmentFilterService`, `AssignmentFilterService`)
  - Policy Sets (`IPolicySetService`, `PolicySetService`)
- Added manual `@odata.nextLink` pagination patterns to newly introduced list operations.
- Added new navigation categories and table wiring for:
  - Conditional Access
  - Assignment Filters
  - Policy Sets
- Added dedicated detail-pane sections for the new categories in the desktop UI.
- Added cache-first support for the new categories, including:
  - cache keys
  - lazy load flags
  - cache restore on connect
  - cache persistence on successful loads
- Added asynchronous lazy loading for the new categories when navigating to their tabs.
- Added `HumanDateTimeConverter` and applied it to detail-pane timestamp displays.
- Added Wave 4–5 Graph-backed services and interfaces for:
  - Windows Autopilot deployment profiles
  - Device Health Scripts
  - macOS Custom Attributes
  - Feature Update Profiles
  - Named Locations
  - Authentication Strength policies
  - Authentication Context references
  - Terms of Use agreements
- Added Wave 4–5 export/import parity coverage in core tests, including new service contract tests.
- Added Graph endpoint audit artifacts:
  - `docs/graph-uri-inventory.csv`
  - `docs/GRAPH_URI_AUDIT.md`
- Added `Micke-K/IntuneManagement` as a Git submodule for endpoint parity and reference.

### Changed
- Rebranded product/user-facing naming to **Intune Commander** across app surfaces and documentation.
- Updated assembly metadata (`AssemblyTitle`, `Product`) to Intune Commander.
- Updated repository URL references to the renamed remote.
- Improved dark/light theme behavior by moving hardcoded UI text/error colors to dynamic theme brushes.
- Tuned left navigation text color for dark mode readability with a dedicated nav text brush.
- Expanded `MainWindowViewModel` category refresh/filter/selection flows to include new service categories.
- Extended `IExportService` / `ExportService` and `IImportService` / `ImportService` with Wave 4–5 object types and migration-map support.
- Updated connection flow to use cache-first behavior for expanded cached dataset coverage.
- Updated refresh behavior to keep new categories lazy (load on selected tab) instead of always eagerly fetching.

### Fixed
- Fixed right-pane “no selection” placeholder logic to account for newly added selection types.
- Fixed XAML issues introduced during detail card expansion:
  - invalid `WrapPanel` size setting (`ItemWidth="Auto"`)
  - invalid `PolicySet` property binding
- Resolved startup/runtime XAML load instability by correcting compile-time XAML errors and validating clean rebuilds.
- Fixed build regressions caused by temporary method-placement/bracing conflicts in `MainWindowViewModel`.
- Fixed remaining Avalonia compiled binding errors in detail panes by replacing invalid `Description` bindings for `NamedLocation` and `Agreement` with valid model properties.

### UX Improvements
- Improved readability of secondary text and error text in dark theme.
- Enhanced new detail cards with richer metadata:
  - Conditional Access: state, timestamps, control indicators
  - Policy Sets: item count, timestamps
- Standardized detail-pane timestamp formatting to human-readable local time.

### Documentation
- Updated branding and links in key project docs (`README.md`, `CLAUDE.md`, planning docs, and Copilot instructions).
- Documented Graph URI inventory and service expansion mapping.

### Build & Validation
- Verified successful desktop project builds after each major implementation wave.
- Validated no remaining compile diagnostics for impacted files at release cut.
- Confirmed full core test pass (`175` passed) via `dotnet test` after Wave 4–5 integration.
