using System.IO;
using System.Text;
using System.Text.Json;
using System.Globalization;
using LmuTelemetryDesktop.Models;

namespace LmuTelemetryDesktop.Services;

public class SavedLapStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public static string SaveDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "LmuTelemetry", "SavedLaps");

    public SavedLapStore()
    {
        Directory.CreateDirectory(SaveDirectory);
    }

    public string Save(SavedLap lap)
    {
        var file = BuildFileName(lap);
        var path = Path.Combine(SaveDirectory, file);
        File.WriteAllText(path, JsonSerializer.Serialize(lap, JsonOpts), Encoding.UTF8);
        return path;
    }

    private static string BuildFileName(SavedLap lap)
    {
        string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unknown";
            var sb = new StringBuilder();
            foreach (var ch in s)
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(ch);
                else if (ch == ' ') sb.Append('_');
            return sb.ToString();
        }

        var date = string.IsNullOrWhiteSpace(lap.LapDateIso)
            ? DateTime.Now.ToString("yyyyMMdd")
            : lap.LapDateIso.Replace(":", "").Replace("-", "").Substring(0, Math.Min(8, lap.LapDateIso.Replace(":", "").Replace("-", "").Length));

        return $"{Sanitize(lap.TrackName)}__{Sanitize(lap.CarName)}__{date}__L{lap.LapNumber}.json";
    }

    public List<(string Path, SavedLap Lap)> LoadAll()
    {
        var list = new List<(string Path, SavedLap Lap)>();   // <-- AGGIUNTI I NOMI
        if (!Directory.Exists(SaveDirectory)) return list;

        foreach (var f in Directory.GetFiles(SaveDirectory, "*.json"))
        {
            try
            {
                var lap = JsonSerializer.Deserialize<SavedLap>(File.ReadAllText(f));
                if (lap != null) list.Add((Path: f, Lap: lap));
            }
            catch { /* file corrotto, lo saltiamo */ }
        }

        return list.OrderByDescending(x => x.Lap.SavedAtIso).ToList();
    }

    public static string NormalizeTrack(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var form = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in form)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);

        var s = sb.ToString().Normalize(NormalizationForm.FormC);
        s = s.ToLowerInvariant();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9]+", "");
        return s;
    }

    public static bool SameTrack(string a, string b)
    {
        var na = NormalizeTrack(a);
        var nb = NormalizeTrack(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        return na == nb || na.Contains(nb) || nb.Contains(na);
    }
}