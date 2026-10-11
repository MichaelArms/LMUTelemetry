# LMU Telemetry Analyzer

Unofficial desktop telemetry analysis tool for **Le Mans Ultimate** (Studio 397).

It reads the DuckDB telemetry logs produced by the game, renders lap-by-lap
channel traces in MoTeC-style lanes, lets you build a personal library of
reference laps, and compares laps across sessions and cars (e.g. LMH vs LMDh)
on the same track — either against time or against distance, with automatic
time-delta computation.

> ⚠️ **Disclaimer:** this is an unofficial, community-made tool.
> It is not affiliated with, endorsed by, or supported by Studio 397 or the
> Le Mans Ultimate development team. Use at your own risk.

## Features

### Session analysis
- Opens LMU `.duckdb` telemetry files directly (no export step needed)
- Automatic session parsing with lap list and lap times (`hh:mm:ss.fff`)
- Session header showing detected track name and date

### Visualization
- MoTeC-style lane chart (Speed, Throttle, Brake, Steering, RPM) drawn with
  pure WPF — no charting dependencies, no native-renderer pitfalls
- **Time or Distance X-axis**: switch between time-based and distance-based
  analysis. Distance mode aligns braking points and corner exits across laps
- Peak-preserving downsampling (min/max bucketing): brake spikes and rev
  limiter cuts are never lost in the rendered trace

### Lap comparison
- Compare up to 2 laps: session vs session, session vs saved, saved vs saved
- **Automatic time-delta trace** (distance mode): shows where lap 2 gains or
  loses time relative to lap 1, with final gap readout
- Track-name validation when mixing laps from different sources
- Click-to-toggle lap selection with colored bars; a third lap is rejected
  with clear feedback (no hidden selection state)

### Reference lap library
- Save reference laps to a local JSON library with track / car / class / date
  metadata
- Saved-laps list automatically filtered to the current track
- Delete saved laps you no longer need

### Export
- Full-resolution CSV export (Excel-friendly, `;` separator)

See the [docs folder](docs/) for the full documentation:
- [DuckDB schema](docs/LMU_DUCKDB_SCHEMA.md)
- [Analysis techniques](docs/ANALYSIS_TECHNIQUES.md)
- [User guide](docs/USER_GUIDE.md)

## Requirements

- Windows 10 / 11 (64-bit)
- .NET 10 SDK (to build) **or** just download the release zip (no .NET needed)
- Le Mans Ultimate (to generate telemetry)

## Download (no build required)

Go to [Releases](https://github.com/MichaelArms/LMUTelemetry/releases), download
the latest `LMUTelemetry-win-x64.zip`, extract it anywhere and run
`LmuTelemetryDesktop.exe`. The build is self-contained: no .NET installation
required.

## Build from source

```bash
git clone https://github.com/MichaelArms/LMUTelemetry.git
cd LMUTelemetry
dotnet build
dotnet run