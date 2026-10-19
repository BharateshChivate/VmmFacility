# Feature 002: Staff Attendance

**Status:** Draft for implementation  
**Version:** 0.1  
**Date:** 2026-10-18  
**Product:** VMM Facility Management

## 1. Purpose

Give the Facility Manager a simple, dependable way to record and review staff attendance from the desktop application. Attendance data is stored locally in SQLite through the ASP.NET Core API and remains available offline. The feature must fit the existing application and its current UI patterns; it must not replace or redesign the desktop shell. For a Mac POC, use the repository's macOS-compatible shell; do not assume Windows-only WebView2.

## 2. Goals

- Record when a staff member starts and finishes work for a local calendar day.
- Show the current day's attendance at a glance and allow the FM to find past records.
- Support a clear correction path for missed or mistaken entries, preserving an audit trail.
- Prevent duplicate or contradictory attendance records.
- Keep attendance operations usable without internet access, Ollama, or cloud backup.

## 3. Non-goals

- Payroll calculation, wage rules, overtime, leave balances, or statutory compliance reports.
- Facial recognition, biometric devices, GPS tracking, geofencing, or employee self-service login.
- Shift planning, rota creation, or automatic assignment of staff to shifts.
- Automatic attendance inference from other facility modules.
- Multi-site/time-zone support unless already present in the application.

## 4. Roles and assumptions

- **Facility Manager (FM):** the authenticated application user who records, reviews, and corrects attendance.
- **Staff member:** a person already present in the application's staff/employee directory. This feature does not create a second staff directory.
- For the initial release, one attendance record per staff member per local calendar day is assumed.
- A record has one of three states: **Present**, **Absent**, or **On leave**. “On leave” is a manual attendance status only; it does not represent a leave-management system.
- The FM records attendance manually. The system records timestamps for audit and does not claim to prove physical presence.
- Use the facility's configured local time zone if one exists. Otherwise use the desktop machine's local time zone and clearly show the time zone in the UI. Store timestamps in UTC and retain the facility-local attendance date.

If the existing project uses different names for users, staff, roles, or facility time zone, reuse those models and conventions.

## 5. User stories

1. As the FM, I can open **Attendance** and see today's staff attendance, so I can quickly identify who has not been marked.
2. As the FM, I can mark a staff member Present, Absent, or On leave, with optional notes.
3. As the FM, I can record a start time and finish time for a present staff member, so the daily record reflects the shift observed.
4. As the FM, I can correct a record with a reason, and the old value remains auditable.
5. As the FM, I can view attendance for a selected date and filter by staff member or status.
6. As the FM, I can see when a record was created or changed and by whom.

## 6. User experience and behavior

### Attendance screen

- Add an **Attendance** entry to the existing application navigation.
- Default to the current facility-local date. Display the selected date and provide previous/next-day navigation plus a date picker.
- Show one row per active staff member, using the existing staff directory's display name and any useful existing identifier.
- Each row shows: staff name, status, start time, finish time, note indicator, and last-updated indicator.
- Show summary counts for Present, Absent, On leave, and Not recorded.
- Provide an obvious **Mark** action for unrecorded staff and **Edit** action for recorded staff.
- Include a search field and status filter. Search is case-insensitive and matches staff display name and existing staff identifier.
- Clearly distinguish “Not recorded” from “Absent”. Opening the screen must never mark anyone absent automatically.
- If there are no active staff members, show an empty state with a link/action to the existing staff-management feature if available.

### Create or edit a record

- Select one status: Present, Absent, or On leave.
- Start and finish times are optional for Present, and unavailable for Absent/On leave. If the application convention requires working hours for Present, expose that as a configurable/confirmed rule rather than silently requiring it.
- Notes are optional, limited to 500 characters, and must not be used as a substitute for structured status.
- A correction to a saved record requires a reason (up to 500 characters). The reason is required only when changing an existing saved record, not for first entry.
- Confirm before saving a correction. Provide a clear success or validation message and refresh the row without losing the selected date or filters.
- Prevent accidental duplicate records if two requests race or the user double-clicks Save.

### Past records

- The selected-date view is the initial history view; no separate reporting dashboard is required.
- Permit date selection for past and current dates. Future dates are not editable in this release.
- The list is ordered by staff name by default. Where the existing app uses pagination, follow its convention; otherwise load the selected day's records and active staff only.
- Each record's audit history can be opened from the row and shows the old/new values, correction reason, actor, and timestamp.

## 7. Data model

Reuse the existing staff and user entities. Add an attendance entity and audit history using the existing EF Core conventions.

### AttendanceRecord

- `Id`: primary key using the project's standard key type.
- `StaffId`: required foreign key to the existing staff entity.
- `AttendanceDate`: required facility-local calendar date, stored as a date without a time component.
- `Status`: required enum/value (`Present`, `Absent`, `OnLeave`).
- `StartTimeUtc`: nullable UTC instant; permitted only for Present.
- `EndTimeUtc`: nullable UTC instant; permitted only for Present.
- `Note`: nullable text, maximum 500 characters.
- `CreatedAtUtc`, `CreatedByUserId`: required creation audit fields where application identity is available.
- `UpdatedAtUtc`, `UpdatedByUserId`: required last-change audit fields.
- Unique constraint on (`StaffId`, `AttendanceDate`).

### AttendanceChange

- `Id`: primary key.
- `AttendanceRecordId`: required foreign key.
- `ChangedAtUtc`, `ChangedByUserId`: required audit fields.
- `Reason`: required correction reason, maximum 500 characters.
- `BeforeJson`, `AfterJson`: structured snapshots of attendance fields, excluding unrelated personal data and secrets. Follow existing audit-log patterns if present.

Do not hard-delete attendance records through the UI. Staff deactivation must not delete historical records. If a record needs reversal, use an auditable correction consistent with the status model.

## 8. API requirements

Follow the API's existing routing, validation, error, and authorization conventions. Suggested routes (adapt route names to the existing API style):

- `GET /api/attendance?date=YYYY-MM-DD&staffId=&status=` — return active staff for the selected day, each with either a record or a `NotRecorded` state, plus summary counts. Avoid requiring one request per staff member.
- `PUT /api/attendance/{staffId}/{date}` — create or update the staff member's record for that date. Use an idempotent upsert protected by the unique constraint.
- `GET /api/attendance/{recordId}/history` — return the record's correction history in chronological order.

Validation and behavior:

- Reject invalid dates, unknown/inactive staff for new records, invalid statuses, overlong notes/reasons, and malformed times with a field-level validation response using the existing API error format.
- Reject future dates for create/update.
- Reject start/end time fields for Absent and On leave.
- When both times exist, reject an end instant earlier than the start instant. Overnight shifts are out of scope; do not infer a next-day finish.
- Treat a same-value save as idempotent: it must not create a spurious correction-history entry.
- For a changed existing record, require correction reason and write the audit row in the same database transaction as the update.
- Enforce uniqueness in the database and return a recoverable conflict response if concurrent writes race.
- Require the same FM authorization used by other write operations. Do not expose unauthenticated write endpoints.
- Core API behavior must not depend on cloud services or AI.

## 9. Local data and offline behavior

- Store records in the existing SQLite database through EF Core.
- Add an EF Core migration; never reset or recreate the user's database to apply this feature.
- The feature must work with the network disconnected. No attendance write may wait for Google Drive, Ollama, or another remote service.
- Ensure existing backup/restore behavior includes attendance records and their correction history.
- Apply the app's existing retention and backup security policies. Do not add personal data beyond staff linkage, attendance status/times, notes, and required audit information.

## 10. Accessibility and usability

- All actions must be usable by keyboard; provide visible focus and labels for controls.
- Status must be represented by text/icon as well as color.
- Date and time must be displayed in facility-local time with an unambiguous format; API/storage timestamps remain UTC.
- Show loading, empty, validation, success, and service-error states.
- Do not silently discard an unsaved edit when changing date or navigating away; prompt to save or discard.

## 11. Acceptance criteria

1. Attendance appears in the existing desktop app navigation and defaults to today's facility-local date.
2. The selected-day list includes every active staff member exactly once, with unrecorded staff labeled **Not recorded** rather than Absent.
3. The FM can create Present, Absent, and On leave records; valid Present records may include start/end times and an optional note.
4. Invalid combinations, future dates, unknown staff, and over-limit text are rejected with actionable validation.
5. A staff member cannot have more than one record for the same date, including under concurrent or repeated saves.
6. Editing a record requires a correction reason; the change and audit history are committed atomically.
7. The history view shows the actor, timestamp, reason, and before/after values for each correction.
8. Date selection and staff/status filters work without clearing each other unexpectedly; future dates are read-only.
9. Attendance create, read, update, and history operations work while offline and do not call Ollama or cloud backup services.
10. Existing attendance data survives app restart, app upgrade, backup, and restore using the application's supported mechanisms.
11. Keyboard navigation, labels, focus visibility, and non-color status cues are present.

## 12. Implementation and verification tasks for Claude Code

1. Inspect the existing repository before editing. Identify staff/user models, authorization, API error format, UI component patterns, SQLite/EF Core configuration, migrations, backup behavior, and timezone conventions.
2. Reuse existing entities and patterns; do not create duplicate staff/auth models or introduce a second database.
3. Implement the EF Core entities/configuration/migration, API endpoints and validation, UI screen/forms/history, and navigation integration.
4. Add automated tests for API validation, unique-per-day behavior, same-value idempotency, correction audit transaction, timezone/date handling, and UI's Not recorded distinction.
5. Run the repository's relevant tests and build/lint checks. Report commands run, results, changed files, and any assumptions that needed resolution.
6. Do not implement payroll, leave accrual, shift scheduling, biometrics, or cloud-dependent behavior as part of this feature.

## 13. Decisions to confirm with the product owner

These are intentionally visible assumptions. Use existing project behavior where it answers them; otherwise record the chosen default in the implementation summary:

- Confirm the exact staff entity and definition of “active”.
- Confirm whether status should include additional values such as Half Day, Holiday, or Weekly Off. Do not add them without confirmation.
- Confirm whether start/end times are needed in the first release and whether they are manually entered or simply audit timestamps. This spec treats them as optional manually entered shift times.
- Confirm facility time-zone source. Default to configured facility time zone, falling back to the machine's local zone only if none exists.
- Confirm whether any role other than FM can view or correct attendance. Default: reuse existing FM authorization; no staff self-service.
