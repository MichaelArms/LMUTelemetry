# User Guide

## First run

1. Download the latest release zip and extract it anywhere.
2. Run `LmuTelemetryDesktop.exe`. No .NET installation is required.

## Loading a session

1. Click **Open .duckdb file**.
2. Select a file from `Le Mans Ultimate/UserData/Telemetry/`.
3. The session is parsed automatically: the lap list appears on the left, with
   the detected **track name and date** shown above it.

## Selecting laps (the comparison model)

- **Click a lap** in either list to add it to the comparison: the row gets a
  **colored bar** (cyan = lap 1, orange = lap 2).
- **Click a barred lap again** to remove it from the comparison.
- The comparison holds **at most 2 laps**: clicking a third lap shows a message
  and does nothing. Deselect one first.
- The colored bars are the only selection indicator: what you see barred is
  exactly what is being compared.

## Viewing channels

- Toggle channels with the checkboxes at the top:
  - **Speed, Throttle, Brake, Steering, RPM**: standard telemetry
  - **SoC**: State of Charge (battery level, 0–100%)
  - **ERS power**: derived from SoC slope (positive = deploy, negative = regen)
  - **Tyre temp**: carcass temperature per wheel (FL/FR/RL/RR, 4 overlapping lines)
  - **Tyre wear**: tread wear per wheel (FL/FR/RL/RR, 4 overlapping lines)
  - **Brake temp**: brake disc/pad temperature per wheel (FL/FR/RL/RR, 4 overlapping lines)
  - **Brake wear**: brake thickness (only if LMU logs dynamic wear; otherwise checkbox is disabled)
- **Hide shift cuts** (on by default) removes gear-shift throttle cuts and
  downshift blips from the displayed traces for readability. Uncheck it to see
  the raw data. CSV export and saved laps always keep raw data.

## Vertical cursor

A slider below the chart controls a vertical cursor line:
- Drag the slider to move the line across all lanes simultaneously
- The label at the top shows the exact position in **metres** (Distance mode)
  or **seconds** (Time mode)
- Use this to align events (braking points, apexes, gear shifts) across all
  channels at once
- The cursor appears only when at least one lap is selected

## Time vs Distance axis

- **Time** (default): X-axis is seconds from lap start.
- **Distance**: X-axis is metres from the start/finish line. Use this to align
  braking points and corner exits between laps.

## Comparing two laps

- Each lap is drawn in its own color on every lane (cyan = lap 1,
  orange = lap 2), MoTeC-style.
- For **per-wheel channels** (Tyre temp, Tyre wear, Brake temp):
  - In single-lap mode: each wheel has its own color (FL/FR/RL/RR)
  - In comparison mode: all 4 wheels of lap 1 are cyan, all 4 of lap 2 are orange;
    wheel labels (FL/FR/RL/RR) appear at the start of each line
- Temperature channels show **°C scale** on the left side of the lane
- The legend under the chart shows a line sample and the lap time for each lap
  (e.g. `L5 • 00:01:44.812`).
- In **Distance** mode with 2 laps, an extra **Δ Time** lane (magenta) appears
  at the top: positive = lap 2 slower at that point; the header shows the final
  gap.

## Saving a reference lap

1. Click the session lap you want to save (it gets a colored bar). Keep only
   one session lap in the comparison for saving.
2. Click **Save selected lap**.
3. Confirm/edit track, car, class, date and notes, then click **Save**.
4. The lap is stored as JSON in `%APPDATA%/LmuTelemetry/SavedLaps/` and appears
   in the **Saved laps** list, filtered to the current track. All channels
   (including SoC, ERS, temps, wear) are preserved in the saved file.

## Deleting a saved lap

1. Click the saved lap(s) you want to delete (they get colored bars; max 2).
2. Click **Delete selected lap** and confirm. The JSON file is permanently
   removed.

## Exporting to Excel

Click **Export CSV (Excel)** to write the full-resolution session to a CSV file
(`;` separator). All channels (Speed, Throttle, Brake, Steering, RPM, SoC,
ERS, TyreTemp FL/FR/RL/RR, TyreWear FL/FR/RL/RR, BrakeWear FL/FR/RL/RR,
BrakeTemp FL/FR/RL/RR) are exported. Useful for custom analysis, pivot tables
or overlay charts.

## Typical workflows

### Find where a setup change helped
Save a reference lap with the old setup, load the new-setup session, click both
laps and switch to Distance mode: the Δ Time lane shows exactly where the new
setup gains or loses. Use the cursor to pinpoint the exact corner.

### Compare LMH vs LMDh
Save a lap from each class on the same track, click both, use Distance mode to
see where each prototype is stronger. Use Tyre temp and Brake temp to compare
thermal management between the two architectures.

### Analyse tyre and brake thermal windows
Enable Tyre temp and Brake temp: check if your tyres stay in the optimal
temperature window (typically 90–110 °C carcass for slicks) and if brakes are
overheating in specific corners. The per-wheel breakdown shows left/right
imbalances that indicate setup issues (camber, toe, ducting).

### Energy management (Hypercar)
Enable SoC and ERS power: see where you deploy (positive ERS, SoC dropping)
and where you regen (negative ERS, SoC rising). Compare laps to optimize
deployment strategy and avoid running out of battery before the finish.