# User Guide

## First run

1. Download the latest release zip and extract it anywhere.
2. Run `LmuTelemetryDesktop.exe`. No .NET installation is required.

## Loading a session

1. Click **Open .duckdb file**.
2. Select a file from
   `Le Mans Ultimate/UserData/Telemetry/`.
3. The session is parsed automatically: the lap list appears on the left, with
   the detected **track name and date** shown above it.

## Viewing a lap

1. Click a lap in the **Session laps** list.
2. The chart shows the enabled channels (Speed, Throttle, Brake, Steering, RPM)
   as separate lanes.
3. Toggle channels with the checkboxes at the top.

## Time vs Distance axis

- **Time** (default): X-axis is seconds from lap start.
- **Distance**: X-axis is metres from the start/finish line. Use this to align
  braking points and corner exits between laps.

## Comparing two laps

1. Select up to **two laps** (Ctrl+click). They can come from the current
   session, from your saved library, or one of each.
2. The first lap is drawn with solid lines, the second with dashed lines.
3. In **Distance** mode, an extra **Δ Time** lane appears at the top showing
   where lap 2 gains or loses time relative to lap 1, with the final gap in the
   lane header.

> Laps from different tracks cannot be compared. If the tracks don't match, the
> tool shows a warning and does not add the lap.

## Saving a reference lap

1. Select a lap in the session list.
2. Click **Save selected lap**.
3. Confirm/edit the track, car, class, date and notes, then click **Save**.
4. The lap is stored as a JSON file in
   `%APPDATA%/LmuTelemetry/SavedLaps/` and appears in the **Saved laps** list.

The saved-laps list is automatically filtered to the current track, so you only
see comparable laps.

## Deleting a saved lap

Select one or more laps in the **Saved laps** list and click
**Delete selected lap**. Confirm the prompt. This permanently removes the JSON
file.

## Exporting to Excel

Click **Export CSV (Excel)** to write the full-resolution session to a CSV file
(`;` separator, Excel-friendly). The file opens directly and is useful for
custom analysis, pivot tables or overlay charts.

## Typical workflows

### Find where a setup change helped
Save a reference lap with the old setup, then load the new-setup session and
compare the two laps in Distance mode. The Δ Time lane shows exactly where the
new setup is faster.

### Compare LMH vs LMDh
Save a lap from each class on the same track, then compare them in Distance
mode to see where each prototype gains or loses time.

### Analyse a full stint
For tyre/energy/fuel trends over many laps, use the CSV export and build charts
in Excel (a dedicated stint view is on the roadmap).