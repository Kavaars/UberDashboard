# UberDashboard — Privacy Policy

[Wersja polska](docs/pl/PRIVACY.md)

Effective date: **2026-11-06**

Project website: **https://github.com/Kavaars/UberDashboard**

Contact: **[Kavaars on GitHub](https://github.com/Kavaars)**

### 1. How the app works

UberDashboard is a Windows desktop application maintained by Kavaars. Dashboard and panel data is stored in the user's profile. There is no developer-operated server storing that content, and the app does not automatically send usage analytics or crash reports to the developer. User data is not used for advertising, sale, marketing profiling, or AI model training.

**Local storage does not mean that the app never communicates over the internet.** Enabling weather, connecting Microsoft/Google, or opening a website communicates with that service. The provider receives the data needed for the operation and connection information, including the IP address.

### 2. Data and purposes

- **Dashboards and panels:** notes, tasks, appearance settings, layouts, shortcuts, selected locations, file references, and user-entered content support the features you use. They are stored locally where persistence is required. Referenced photos/documents may remain in their selected folders.
- **Microsoft To Do:** account connection uses Microsoft sign-in and Microsoft Graph. The app processes account/list identifiers, selected task data, checklists, linked-resource metadata, and session credentials. Data includes names, states, dates, and provided resource URLs. Local copies may remain after disconnecting. After explicitly enabling write access, user changes are sent back to Microsoft only within the granted permissions and selected synchronization.
- **Google Calendar:** connection uses Google OAuth and Calendar API, processing session credentials, account/calendar identifiers, and event data. The account identifier is obtained through Google OpenID Connect and prevents synchronization from being assigned to the wrong account. The calendar view reads events. In releases with enabled write functionality, approved changes are sent to Google. Participant-related actions can cause Google to send invitations or cancellations according to the approved action.
- **Resource management:** when selected, the app reads the Microsoft list or Google calendar catalog, names, properties, and access restrictions. Google also supplies the primary calendar identifier and the data owner's email to check calendar deletion rights. Calendar management requires separate consent. Entered names, descriptions, locations, and calendar time zones are synchronized with the provider. Properties and contents can be visible to others according to the service's sharing settings. Approved whole-resource deletion may also affect other people's data. Deleted-identifier metadata remains in the local profile to protect unsent changes from stale bindings.
- **Google subscriptions and sharing:** with separate consent, the app reads and writes the user's calendar list, personal aliases, colors, and visibility settings. Sharing management requires different consent and processes access rules, person/group addresses, domains, and permission levels. These data are stored locally and sent directly to Google for the selected operation. Approved sharing can expose the calendar to the chosen recipients; public access can also expose it to signed-out users, subject to the role and Google's policies. Google may send access-change notifications. Unsubscribing only removes the entry from the user's list; it does not delete the calendar, revoke others' permissions, or automatically erase local copies. Sharing changes and unsubscribing require a separate review and approval in the queue.
- **Weather:** place search text is sent to `geocoding-api.open-meteo.com`; coordinates of the selected location are sent to `api.open-meteo.com`. Results and configuration may be cached locally. Notes and tasks are not required for weather and are not sent to Open-Meteo.
- **Resource monitor:** hardware readings display local CPU, GPU, memory, and available sensor information. The built-in monitor does not send them to the developer.
- **Diagnostics:** local logs record timestamps, modules, operation identifiers, and errors. Error details may include local paths, resource identifiers, and technical information. Development code further limits informational HTTP request logging. Logs are not automatically uploaded; review them before sharing them yourself.

Access to additional data or integrations requires appropriate disclosure and consent before use. Code present in the repository is not necessarily enabled in an installed release.

### 3. Recipients and external services

Attachments selected for upload are copied into the local profile and sent directly to Microsoft or Google Drive. Google requests contextual `drive.file` consent for files created by or shared with the app. Attaching does not grant file access to attendees; detaching does not delete a Drive file. Local upload snapshots can be removed separately. Upload session addresses are protected with Windows user DPAPI and excluded from HTTP logs. Series split plans and exception copies remain local; finished plans are retained for 30 days during history cleanup or removed earlier on request. Unresolved plans and unsent transfers do not expire automatically. Approved splitting can cause Google to send updates, cancellations and new invitations.

The developer does not sell user data or supply it to advertisers. Microsoft, Google, and Open-Meteo connections support user-selected features and are subject to the providers' own policies: [Microsoft](https://privacy.microsoft.com/privacystatement), [Google](https://policies.google.com/privacy), and [Open-Meteo](https://open-meteo.com/en/terms).

Visiting the repository, downloading releases, and contacting the project through GitHub is also subject to [GitHub's Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). Public issue contents are public. Do not post credentials, OAuth account files, private tasks/events, or unreviewed logs.

Opening web shortcuts or network files uses the browser, application, or network resource you selected. Installed plugins execute in the host process without system-level isolation. Third-party plugins may have their own connections and privacy practices; this policy does not guarantee their behavior.

Task resource URLs are stored as data and synchronized with Microsoft; they are not opened automatically. Selecting **Open in browser** for an HTTP/HTTPS address contacts the destination website through your browser under that site's policies. Native-app deep links can be retained, but that button does not launch them.

### 4. Storage and protection

The default profile is `%LOCALAPPDATA%\UberDashboard`, including `Data\dashboard.db`, panel files/assets, `Credentials`, `LOGS`, and `Backups`. Custom profiles may use another location. Network folders, cloud-synchronized folders, and system backups follow your own configuration.

Microsoft sessions use the Windows-protected MSAL cache. Google session files use Windows DPAPI for the current user. **The database, ordinary assets, logs, and database backups are not fully encrypted by UberDashboard.** Windows account security, file permissions, and disk protection also matter. Do not publish your profile or backups.

Content/settings remain until changed or removed. Daily log rotation retains up to 14 files. Ordinary database backups default to 10 retained copies; pre-migration and independently saved copies may remain longer. The new synchronization engine's recovery history defaults to 30 days, retaining unresolved/unsent changes longer. Removing an item from a view does not automatically erase every older copy.

Fetched calendar ranges may be stored in the local database for offline viewing, including titles, times, and event/series identifiers. Copies are separated by account and calendar; disconnecting does not automatically erase them. Removing the full app profile also removes this local cache, subject to separately retained backups.

### 5. Control and deletion

Inspecting an uncertain update or deletion reads directly from Microsoft/Google. After explicitly proceeding to a decision, the sent draft, current local content and observed service version are retained in local history. The inspection and conflict preparation do not repeat the provider write; a later version choice can prepare an ordinary synchronization change.

**Recovery history** in the synchronization window exposes older local copies and the base, local and service versions retained when resolving a conflict. Restoring opens the editor; saving prepares an ordinary change for synchronization with the provider. A selected local copy can be removed early after separate confirmation; this does not remove current content, unsent operations or separate backups. The 30-day retention rule is checked during editing, merging and history reads. Unresolved conflicts and pending changes protect related copies from automatic expiry.

You can leave network integrations disabled, disconnect accounts in the app, and revoke access in your [Google](https://myaccount.google.com/connections) or [Microsoft](https://account.live.com/consent/Manage) account. Organizational accounts may be controlled by an administrator. Disconnecting does not delete cloud tasks/events or necessarily erase local snapshots, logs, or backups.

To erase all local UberDashboard data, close the app and remove your app profile and any backups/exports stored elsewhere. Uninstallation preserves profile data by default. Delete cloud data separately in the provider's service or through an explicitly selected synchronization operation.

### 6. Google API Limited Use

UberDashboard's use and transfer to any other app of information received from Google APIs adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements. Data supports only user-facing features authorized by the user and is not used for advertising or general-purpose AI model training. The developer has no automatic access to users' locally stored account content.

### 7. Contact and updates

The project is maintained by [Kavaars](https://github.com/Kavaars). For privacy matters, use the contact information made available on that profile. Keep public reports free of private data. The current dated policy is published in the project repository. Policy changes do not replace consent for additional permissions.
