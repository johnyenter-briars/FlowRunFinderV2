# Changelog

## 1.0.2

- Split the app into `FlowRunFinderV2.Core` and `FlowRunFinderV2.UI`, keeping Avalonia-specific code in UI and reusable auth, client, configuration, logging, model, and query logic in Core.
- Retargeted Core to `.NET Standard 2.0` for .NET Framework 4.8 consumers such as XrmToolBox plugins.
- Added configurable token cache locations through `TokenCacheOptions` so callers can choose where MSAL cache files are stored.
- Added settings for default run count, max runs to query, log verbosity, configurable Dataverse and Power Automate client IDs, and the flow run history table toggle.
- Added advanced-search support for the `flowruns` table path, including time-window filtering and trigger-input matching.
- Added right-click copy behavior for run links and grid cell values, with toast notifications.
