# Changelog

## 1.1.0

- Added interactive browser authentication as the default sign-in flow, while keeping device-code authentication available in Settings.
- Updated browser-auth configuration and corrected the browser-auth redirect URI handling.
- Added cancellation for long-running advanced searches, including cancellation support throughout the query engine.
- Added live advanced-search progress reporting for candidate records, scanned records, scan percentage, and matches.
- Expanded advanced-search debug logging to record the criteria used when runs match or are rejected.
- Split the app into `FlowRunFinderV2.Core` and `FlowRunFinderV2.UI`, keeping Avalonia-specific code in UI and reusable auth, client, configuration, logging, model, and query logic in Core.
- Retargeted Core to `.NET Standard 2.0` for .NET Framework 4.8 consumers such as XrmToolBox plugins.
- Added configurable token cache locations through `TokenCacheOptions` so callers can choose where MSAL cache files are stored.
- Added settings for default run count, max runs to query, log verbosity, configurable Dataverse and Power Automate client IDs, and the flow run history table toggle.
- Added advanced-search support for the `flowruns` table path, including time-window filtering and trigger-input matching.
- Added right-click copy behavior for run links and grid cell values, with toast notifications.

## 1.0.0

- Added named Dataverse/Power Platform connections with cached device-code authentication.
- Added recent Power Automate run history loaded directly from the environment API.
- Added dynamic trigger-output columns remembered per flow.
- Added searchable flow and trigger-field pickers.
- Added advanced UTC time-window search with grouped `AND` / `OR` filters.
- Added `Equals` and `Contains` matching for trigger output fields.
- Added run links to make.powerautomate.com and right-click copy actions.
- Added local daily logs with configurable verbosity.
