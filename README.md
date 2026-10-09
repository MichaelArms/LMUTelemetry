# LMU Telemetry Analyzer

Unofficial desktop telemetry analysis tool for **Le Mans Ultimate** (Studio 397).

It reads the DuckDB telemetry logs produced by the game, renders lap-by-lap
channel traces in MoTeC-style lanes, lets you build a personal library of
reference laps, and compares laps across sessions and cars (e.g. LMH vs LMDh)
on the same track.

> ⚠️ **Disclaimer:** this is an unofficial, community-made tool.
> It is not affiliated with, endorsed by, or supported by Studio 397 or the
> Le Mans Ultimate development team. Use at your own risk.

## Features

- Opens LMU `.duckdb` telemetry files directly (no export step needed)
- Automatic session parsing with lap list and lap times (`hh:mm:ss.fff`)
- MoTeC-style lane chart (Speed, Throttle, Brake, Steering, RPM) drawn with
  pure WPF — no charting dependencies, no native-renderer pitfalls
- Peak-preserving downsampling (min/max bucketing): brake spikes and rev
  limiter cuts are never lost in the rendered trace
- Save reference laps to a local JSON library with track / car / class / date
  metadata
- Compare up to 2 laps: session vs session, session vs saved, saved vs saved
- Track-name validation when mixing laps from different sources
- Full-resolution CSV export (Excel-friendly, `;` separator)

## Requirements

- Windows 10 / 11
- .NET 10 SDK (build) or .NET 10 Desktop Runtime (run)
- Le Mans Ultimate (to generate telemetry)

## Build & run

```bash
git clone https://github.com/<your-user>/LMUTelemetry.git
cd LMUTelemetry
dotnet build
dotnet run