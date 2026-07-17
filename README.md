<p align="center">
  <img src="src/FlowRunFinderV2/FlowRunFinderV2.UI/Assets/FlowRunFinderV2.svg" width="96" alt="Flow Run Finder V2 icon" />
</p>

# Flow Run Finder V2

Flow Run Finder V2 is a desktop tool for finding Power Automate flow runs and inspecting the related trigger data.

It is useful when you know a flow ran, but need to answer questions like:

- Which run handled the update of this record?
- What happened during a specific time window?

## Features

| Status | Feature | Details |
| --- | --- | --- |
| ✅ | Named connections | Save multiple Dataverse/Power Platform environments with cached MSAL authentication. |
| ✅ | Interactive and device-code sign-in | Use the default browser-based flow or switch to device-code authentication in Settings. |
| ✅ | Flow picker | Search flows by name or workflow ID. |
| ✅ | Run history | Load recent runs from the Power Automate API or the Dataverse `flowruns` history table. |
| ✅ | Dynamic trigger columns | Discover trigger-output fields from loaded runs, choose visible columns, and remember selections per flow. |
| ✅ | Advanced search | Search a UTC time window using grouped `AND` / `OR` filters with `Equals` and `Contains` comparisons. |
| ✅ | Search progress and cancellation | See candidate, scanned, and match counts while an advanced search runs, and cancel it when needed. |
| ✅ | Run links and copy actions | Open runs in make.powerautomate.com or right-click to copy run links and grid values. |
| ✅ | Local logging | Write daily logs locally with configurable verbosity, including match criteria at debug level. |
| ⏳ | Filter by flow status | Filter results by run status, such as `Succeeded`, `Failed`, or `Canceled`. |
| ⏳ | Export results | Export the current run list and selected trigger columns to CSV or another file format. |
| ⏳ | Action-level run inspection | Inspect individual actions and their inputs/outputs inside a run. |
| ⏳ | Saved search presets | Save and reuse advanced-search time windows and filter groups. |
| ⏳ | Multiple flow queries | Query identical trigger conditions, but across multiple flows |

## Screenshots

![Flow runs](docs/screenshots/flow-runs.png)

![Advanced search](docs/screenshots/advanced-search.png)

## Setting Up A Connection

When the app starts, choose an existing connection or create a new one.

For a new connection, enter:

- a friendly name, like `prod` or `uat`
- the Dataverse environment URL, like `https://contoso.crm.dynamics.com`

By default, the app uses MSAL interactive browser authentication with a persisted encrypted token cache. Device-code authentication remains available in Settings for environments where the browser flow is not preferred.

Connection metadata and token caches are stored locally under:

```text
%LOCALAPPDATA%\FlowRunFinderV2\connections
```

## Using The Tool

After connecting, pick a cloud flow from the flow picker. The app loads the latest runs and shows the run start time, end time, status, and run id.

Use `Trigger Columns` to choose which trigger output fields should appear in the grid. The list is based on the trigger payloads returned for the loaded runs, so it can include custom Dataverse columns and dynamic trigger values.

Use `Advanced Search` when recent runs are not enough. Set a UTC start and end time, then add filters against trigger output fields. You can group filters with `AND` and `OR`, which is useful for searches like:

```text
accountid equals {GUID}
AND
statuscode equals 1
```

or:

```text
name contains test
OR
websiteurl contains contoso
```

Advanced search scans run history newest-to-oldest. It avoids loading trigger payloads until a run is inside the requested time window, which keeps older searches from doing unnecessary work.

While an advanced search is running, the app reports its candidate count, scan progress, and current match count. Use `Cancel` in the busy indicator to stop a long-running query.

## Local Files

The app keeps its local data here:

```text
%LOCALAPPDATA%\FlowRunFinderV2
```

Notable files and folders:

- `settings.json`: app settings and selected trigger columns
- `connections`: saved connection profiles and token caches
- `logs`: daily log files

## Development

Build the solution with:

```powershell
dotnet build .\src\FlowRunFinderV2\FlowRunFinderV2.sln
```

| Project | Description |
| --- | --- |
| `FlowRunFinderV2.Core` | Reusable [.NET Standard 2.0](https://learn.microsoft.com/dotnet/standard/net-standard) application logic with no [Avalonia](https://avaloniaui.net/) dependency, so it can be referenced by net48 hosts such as XrmToolBox plugins. |
| `FlowRunFinderV2.UI` | The [Avalonia](https://avaloniaui.net/) desktop app, dialogs, windows, app resources, UI-specific converters, and run grid built with [Avalonia DataGrid](https://docs.avaloniaui.net/docs/reference/controls/datagrid/). |

| Namespace | Description |
| --- | --- |
| `FlowRunFinderV2.Core.Auth` | MSAL authentication for interactive browser and in-app device-code flows, configurable public client IDs, and isolated token cache wiring through [MSAL.NET](https://learn.microsoft.com/entra/msal/dotnet/). |
| `FlowRunFinderV2.Core.Client` | API clients for [Dataverse](https://learn.microsoft.com/power-apps/developer/data-platform/) flow metadata and [Power Automate](https://www.microsoft.com/power-platform/products/power-automate) run history. |
| `FlowRunFinderV2.Core.Configuration` | Local connection profiles and app settings. |
| `FlowRunFinderV2.Core.Logging` | Local file logging. |
| `FlowRunFinderV2.Core.Model` | Cloud flow and run models. |
| `FlowRunFinderV2.Core.Query` | Run query and advanced-search filtering logic. |

Callers that host Core outside this app, such as an XrmToolBox plugin, can pass `TokenCacheOptions` into the auth services to choose the token cache directory instead of using the app's default local-data folder. They can also pass app-registration client IDs into the auth services instead of using the Microsoft public client IDs exposed by `AuthenticationClientIds`.

#### Publish

```powershell
dotnet publish .\src\FlowRunFinderV2\FlowRunFinderV2.UI\FlowRunFinderV2.UI.csproj /p:PublishProfile=FolderProfile
```

The executable is written to `src\FlowRunFinderV2\FlowRunFinderV2.UI\bin\Release\net8.0\win-x64\publish`.

## AI Disclosure
- AI-assisted tooling was used in the development of this codebase.

## License
- License: [MIT](LICENSE)
