namespace LmuTelemetryDesktop.Models;

public class TelemetrySession
{
    public List<TelemetryPoint> Points { get; set; } = new();
    public List<double> ThrottleValues { get; set; } = new();
    public List<double> BrakeValues { get; set; } = new();
    public List<LapMarker> Laps { get; set; } = new();
    public string DebugInfo { get; set; } = string.Empty;
}

public class TelemetryPoint
{
    public double Time { get; set; }
    public double Distance { get; set; }
    public double Speed { get; set; }
    public double Steering { get; set; }
    public double RPM { get; set; }
    public double Throttle { get; set; }
    public double Brake { get; set; }
}

public class LapMarker
{
    public double Time { get; set; }
    public int LapNumber { get; set; }
}

public class LapInfo
{
    public int LapNumber { get; set; }
    public double LapTime { get; set; }
    public double StartTime { get; set; }
    public double EndTime { get; set; }

    public string LapTimeFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(LapTime);
            return ts.ToString(@"hh\:mm\:ss\.fff");
        }
    }
}

// ---- Giro salvato su disco (JSON) ----
public class SavedLap
{
    public int Version { get; set; } = 1;
    public string TrackName { get; set; } = "";
    public string CarName { get; set; } = "";
    public string CarClass { get; set; } = "";
    public int LapNumber { get; set; }
    public double LapTimeSeconds { get; set; }
    public string LapDateIso { get; set; } = "";
    public string SavedAtIso { get; set; } = "";
    public string Note { get; set; } = "";
    public List<SavedPoint> Points { get; set; } = new();
}

public class SavedPoint
{
    public double T { get; set; }
    public double S { get; set; }
    public double Th { get; set; }
    public double Br { get; set; }
    public double St { get; set; }
    public double R { get; set; }
}

public class ComparisonEntry
{
    public string Label { get; set; } = "";
    public string TrackName { get; set; } = "";
    public string CarName { get; set; } = "";
    public List<TelemetryPoint> Points { get; set; } = new();
    public bool IsSaved { get; set; }
}

public class SavedLapListItem
{
    public string FilePath { get; set; } = "";
    public string TrackName { get; set; } = "";
    public string CarName { get; set; } = "";
    public string DisplayDate { get; set; } = "";
    public string LapTimeFormatted { get; set; } = "";
    public int LapNumber { get; set; }
}