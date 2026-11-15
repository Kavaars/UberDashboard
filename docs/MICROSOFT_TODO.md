# Microsoft To Do in UberDashboard

[Wersja polska](pl/MICROSOFT_TODO.md)

Open **Menu → Settings → Integrations → Open the Microsoft To Do wizard**.

1. Enter the application (client) ID from Microsoft Entra and choose **Connect account and fetch lists**. To switch accounts use **Choose another Microsoft account**.
2. Pick the **To-do list** panel on the active dashboard. If there is none, add it from the dashboard menu.
3. For the lists you want, tick **Import a copy**, **Sync**, or both. Choose **Save selection and fetch tasks**.

An import creates a local, editable copy. Connecting the account requires the `Tasks.ReadWrite` permission. Saving the list selection turns on two-way sync for those lists; the **Two-way sync** item in a tab's context menu opens the task editor; the field and + button in the panel add tasks to the sync queue. Syncs you paused manually earlier stay paused. Local imports are not published automatically. Exact Microsoft statuses are preserved, also beyond a simple done/not done.

A change is first saved locally together with the queue. The editor shows pending operations, conflicts, retry and pause. The **Sync center** in Integrations lets you return to the links. Deleting just the tab does not send task deletions to the service. Pause a two-way link in its editor; the existing read-selection wizard does not yet manage all of the newer links. The full API coverage, detaching with copies and account tests remain open items.

The tab of a linked list can be removed locally, or you can choose to also delete the Microsoft To Do list after confirmation. Deleting from the account goes through the sync queue; a list with pending changes or without the required permissions will not be deleted. If an operation is unconfirmed, the tab stays and the operation's state can be checked in the sync center.

## Client setup

1. Register a public desktop application in Microsoft Entra. To support personal and organizational accounts, choose a multi-tenant registration that includes personal Microsoft accounts.
2. Add the mobile and desktop applications platform and the redirect URI `http://localhost` for the system browser. Enable public client flows.
3. Add the delegated Microsoft Graph permission `Tasks.ReadWrite`. Organization policy may require administrator consent.
4. Copy the Application (client) ID into the wizard. On first open the wizard may pre-fill the value from the older `UBERDASHBOARD_MS_TODO_CLIENT_ID` environment variable.

MSAL opens the Microsoft sign-in in your browser. The authentication cache is protected by Windows and stored in the `Credentials` folder of the application profile. **Disconnect saved account** clears the application's session for that client.

[Microsoft Entra desktop client setup](https://learn.microsoft.com/en-us/azure/active-directory/develop/scenario-desktop-app-registration) · [Reading tasks in Microsoft Graph](https://learn.microsoft.com/en-us/graph/api/todotasklist-list-tasks?view=graph-rest-1.0)

## Database update

Schema 3 moves the tasks of built-in to-do lists from JSON to the `TodoItem` table. Before migrating an existing database the application saves a consistent SQLite copy `pre-todo-v3-*.db` in the profile's `Backups` folder. It also keeps the original JSON of the migrated lists in the database. Invalid source data aborts the transaction.

Going back to a pre-migration version of the application requires restoring the database copy made before the update. Close the application and keep a separate copy of the current data before restoring.

## Integration log

In Settings → Integrations you can turn event logging on or off, refresh a copyable log and clear its entries. Logging is on by default; the setting persists across restarts. The log covers selected errors from background sync and sync run in the editor, refreshes, and Microsoft and Google sign-in. It contains the time, service, stage, error type, HTTP status and a safe reason code and module where available. It does not record exception messages, tokens, account addresses or task contents. The `LOGS/integration.txt` file in the application profile is limited to about 1 MB; once the limit is reached, older entries are replaced. Turning logging off stops new entries and does not delete existing ones.
