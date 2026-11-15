# Calendar and resource monitor

[Wersja polska](pl/PANELE_KALENDARZ_I_ZASOBY.md)

Both panels are available in edit mode, in the add-panel menu.

## Calendar

The list above the calendar switches the view between a full month, a week, two weeks, a single day, a custom range and an event list. The panel settings let you choose from 1 to 90 days for the custom range and the list, turn on hours, and show empty days in the single-column view. The arrows move the displayed range, and the **Today** button returns to the current date. In the grid you can hover over a day or an event to see its full description.

1. In a Google Cloud project, enable the Google Calendar API and configure the OAuth consent screen. If the app is in testing mode, add your account as a test user.
2. Create an OAuth client of type **Desktop app** and download its JSON file. [Google's guide](https://developers.google.com/identity/protocols/oauth2/native-app)
3. In the panel header open its options and choose **Connect Google**. Select the downloaded file and finish sign-in in the browser.
4. The **primary** identifier points to the account's main calendar. To choose another accessible calendar, enter its identifier from the Google Calendar settings.

The calendar view reads expanded events when the range changes and every five minutes. **Refresh** fetches them immediately. In the draft version the **Two-way sync** button asks separately for write consent and opens an editor for creating, changing basic details of and deleting events. Data waiting to be sent is stored in the existing local database. Operations involving attendees require approval in the editor's queue. Editing an existing series requires confirming that it affects the whole series; editing a single occurrence and splitting a series are not finished yet.

Session data is encrypted for the current Windows user, separately for each panel instance. The account identity is checked before the session is used to send changes. **Disconnect** removes the session data of that panel; the local queue and data are not deleted this way. You can revoke consent in your Google account settings. The full feature scope and account testing remain open.

## Resource monitor

Every two seconds the monitor updates CPU, GPU, RAM and VRAM usage, CPU and GPU temperature and fan data. Each recognized graphics card gets its own measurement rows. The control percentage is the value reported by the controller, and RPM is the measured rotation speed. Both depend on the sensors available on the given hardware. A row without a measurement shows **Unavailable**. Hovering over a module shows a tooltip describing what the reading represents. Modules have equal size, and the panel no longer shows the time of the last measurement.

Access to some motherboard and CPU sensors depends on the driver, hardware and permissions. Reading the CPU temperature usually requires running the application as administrator; without it the module shows **Unavailable** and the tooltip explains why. Readings are provided by [LibreHardwareMonitor 0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6).

In the panel options, checkboxes show or hide individual detected readings. The choice is saved separately for each instance; newly detected readings are visible by default. Availability of readings depends on the hardware and permissions.

In a tab's options the resource monitor lets you choose a text or tile view; the choice is saved together with the visibility of the indicators.
