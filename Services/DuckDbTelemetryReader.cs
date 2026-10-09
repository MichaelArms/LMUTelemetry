using System.IO;
using DuckDB.NET.Data;
using LmuTelemetryDesktop.Models;

namespace LmuTelemetryDesktop.Services;

public class DuckDbTelemetryReader
{
    public TelemetrySession ReadSession(string filePath)
    {
        var session = new TelemetrySession();

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Il file non esiste: {filePath}");

        var connectionString = $"Data Source={filePath}";

        using var connection = new DuckDBConnection(connectionString);
        connection.Open();

        // DEBUG info
        var debugInfo = new System.Text.StringBuilder();
        var tablesToCheck = new[] { "GPS Time", "Ground Speed", "Steering Pos", "Engine RPM", "Throttle Pos", "Brake Pos" };

        foreach (var table in tablesToCheck)
        {
            using var cmdCount = connection.CreateCommand();
            cmdCount.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            var count = Convert.ToInt64(cmdCount.ExecuteScalar());
            debugInfo.AppendLine($"{table}: {count} righe");
        }

        // GPS Time
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT value FROM ""GPS Time"" ORDER BY rowid";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                session.Points.Add(new TelemetryPoint
                {
                    Time = reader.IsDBNull(0) ? 0 : Convert.ToDouble(reader.GetValue(0))
                });
            }
        }
        debugInfo.AppendLine($"Punti GPS Time caricati: {session.Points.Count}");

        // Ground Speed
        var speedValues = new List<double>();
        using (var cmdSpeed = connection.CreateCommand())
        {
            cmdSpeed.CommandText = @"SELECT value FROM ""Ground Speed"" ORDER BY rowid";
            using var r = cmdSpeed.ExecuteReader();
            while (r.Read())
            {
                speedValues.Add(r.IsDBNull(0) ? 0 : Convert.ToDouble(r.GetValue(0)));
            }
        }
        for (int i = 0; i < session.Points.Count && i < speedValues.Count; i++)
            session.Points[i].Speed = speedValues[i];

        // Steering
        var steeringValues = new List<double>();
        using (var cmdSteer = connection.CreateCommand())
        {
            cmdSteer.CommandText = @"SELECT value FROM ""Steering Pos"" ORDER BY rowid";
            using var r = cmdSteer.ExecuteReader();
            while (r.Read())
            {
                steeringValues.Add(r.IsDBNull(0) ? 0 : Convert.ToDouble(r.GetValue(0)));
            }
        }
        for (int i = 0; i < session.Points.Count && i < steeringValues.Count; i++)
            session.Points[i].Steering = steeringValues[i];

        // RPM
        var rpmValues = new List<double>();
        using (var cmdRpm = connection.CreateCommand())
        {
            cmdRpm.CommandText = @"SELECT value FROM ""Engine RPM"" ORDER BY rowid";
            using var r = cmdRpm.ExecuteReader();
            while (r.Read())
            {
                rpmValues.Add(r.IsDBNull(0) ? 0 : Convert.ToDouble(r.GetValue(0)));
            }
        }
        for (int i = 0; i < session.Points.Count && i < rpmValues.Count; i++)
            session.Points[i].RPM = rpmValues[i];

        // Throttle
        using (var cmdThrottle = connection.CreateCommand())
        {
            cmdThrottle.CommandText = @"SELECT value FROM ""Throttle Pos"" ORDER BY rowid";
            using var r = cmdThrottle.ExecuteReader();
            while (r.Read())
            {
                session.ThrottleValues.Add(r.IsDBNull(0) ? 0 : Convert.ToDouble(r.GetValue(0)));
            }
        }

        // Brake
        using (var cmdBrake = connection.CreateCommand())
        {
            cmdBrake.CommandText = @"SELECT value FROM ""Brake Pos"" ORDER BY rowid";
            using var r = cmdBrake.ExecuteReader();
            while (r.Read())
            {
                session.BrakeValues.Add(r.IsDBNull(0) ? 0 : Convert.ToDouble(r.GetValue(0)));
            }
        }

        // Lap markers
        try
        {
            using var cmdLap = connection.CreateCommand();
            cmdLap.CommandText = @"SELECT ts, value FROM ""Lap"" ORDER BY ts";
            using var r = cmdLap.ExecuteReader();
            while (r.Read())
            {
                session.Laps.Add(new LapMarker
                {
                    Time = Convert.ToDouble(r.GetValue(0)),
                    LapNumber = Convert.ToInt32(r.GetValue(1))
                });
            }
        }
        catch { }

        session.DebugInfo = debugInfo.ToString();
        return session;
    }

    public Dictionary<string, string> ReadMetadata(string filePath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath)) return result;

        try
        {
            using var connection = new DuckDBConnection($"Data Source={filePath}");
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM metadata";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var k = r.GetValue(0)?.ToString() ?? "";
                var v = r.GetValue(1)?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(k)) result[k] = v;
            }
        }
        catch { }

        return result;
    }
}