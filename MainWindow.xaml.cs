using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using LmuTelemetryDesktop.Models;
using LmuTelemetryDesktop.Services;

namespace LmuTelemetryDesktop;

public partial class MainWindow : Window
{
    private string? _selectedFile;
    private readonly DuckDbTelemetryReader _reader = new();
    private readonly SavedLapStore _store = new();

    private List<TelemetryPoint> _allPoints = new();
    private List<LapMarker> _allLaps = new();
    private List<LapInfo> _lapInfos = new();
    private List<(string Path, SavedLap Lap)> _savedLaps = new();

    private string _currentTrackName = "";
    private bool _suppressSelectionEvents = false;

    private const double LeftPad = 74, RightPad = 12, TopPad = 6, BottomPad = 26;

    public MainWindow()
    {
        InitializeComponent();
        RefreshSavedList();
    }

    private sealed record Lane(string Name, string Unit, Func<TelemetryPoint, double> Sel, Color Color);

    private void OpenDuckDb_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "LMU Telemetry (*.duckdb;*.db)|*.duckdb;*.db|All Files (*.*)|*.*",
            Title = "Select LMU Telemetry File"
        };

        if (dlg.ShowDialog() == true)
        {
            _ = LoadAndAnalyzeAsync(dlg.FileName);
        }
    }

    private async Task LoadAndAnalyzeAsync(string filePath)
    {
        try
        {
            // Pulizia completa
            _allPoints.Clear();
            _allLaps.Clear();
            _lapInfos.Clear();
            _suppressSelectionEvents = true;
            LapList.ItemsSource = null;
            LapList.SelectedItems.Clear();
            SavedList.SelectedItems.Clear();
            _suppressSelectionEvents = false;
            ChartCanvas.Children.Clear();
            SessionInfo.Text = string.Empty;
            BtnSaveLap.IsEnabled = false;

            _selectedFile = filePath;
            StatusText.Text = $"Lettura: {System.IO.Path.GetFileName(filePath)}...";

            var session = await Task.Run(() => _reader.ReadSession(filePath));

            // Allineamento Throttle/Brake
            for (int i = 0; i < session.ThrottleValues.Count && i * 2 < session.Points.Count; i++)
            {
                double thr = session.ThrottleValues[i];
                double brk = i < session.BrakeValues.Count ? session.BrakeValues[i] : 0;

                session.Points[i * 2].Throttle = thr;
                session.Points[i * 2].Brake = brk;

                if (i * 2 + 1 < session.Points.Count)
                {
                    session.Points[i * 2 + 1].Throttle = thr;
                    session.Points[i * 2 + 1].Brake = brk;
                }
            }

            // Normalizza tempo
            if (session.Points.Count > 0)
            {
                double t0 = session.Points[0].Time;
                foreach (var p in session.Points) p.Time -= t0;
                foreach (var l in session.Laps) l.Time -= t0;
            }

            _allPoints = session.Points;
            _allLaps = session.Laps;

            // Calcola info giri
            for (int i = 0; i < _allLaps.Count; i++)
            {
                var lap = _allLaps[i];
                double startTime = lap.Time;
                double endTime = (i + 1 < _allLaps.Count) ? _allLaps[i + 1].Time : _allPoints[^1].Time;
                double lapTime = endTime - startTime;

                _lapInfos.Add(new LapInfo
                {
                    LapNumber = lap.LapNumber,
                    StartTime = startTime,
                    EndTime = endTime,
                    LapTime = lapTime
                });
            }

            LapList.ItemsSource = _lapInfos;

            // Rileva tracciato
            var meta = await Task.Run(() => _reader.ReadMetadata(filePath));
            _currentTrackName = DetectTrackName(meta, filePath);
            TrackRefText.Text = _currentTrackName != ""
                ? $"Reference Track: {_currentTrackName}"
                : "Track: not detected";

            BtnSaveLap.IsEnabled = _lapInfos.Count > 0;
            RefreshSavedList();

            SessionInfo.Text = $"{_allPoints.Count:N0} campioni  |  {_lapInfos.Count} giri";
            StatusText.Text = "Session loaded. Select 1 or 2 laps.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error";
            MessageBox.Show(ex.Message, "Error reading telemetry",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LapList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateComparison();
    private void SavedList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateComparison();
    private void Channel_Click(object sender, RoutedEventArgs e) => UpdateComparison();
    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateComparison();

    private void UpdateComparison()
    {
        if (_suppressSelectionEvents) return;

        var entries = new List<ComparisonEntry>();
        var refTrack = _currentTrackName;

        // Giri della sessione corrente
        foreach (LapInfo li in LapList.SelectedItems)
        {
            var pts = _allPoints
                .Where(p => p.Time >= li.StartTime && p.Time <= li.EndTime)
                .Select(p => new TelemetryPoint
                {
                    Time = p.Time - li.StartTime,
                    Speed = p.Speed, Throttle = p.Throttle, Brake = p.Brake,
                    Steering = p.Steering, RPM = p.RPM
                }).ToList();

            entries.Add(new ComparisonEntry
            {
                Label = $"Lap {li.LapNumber} (sessione)",
                TrackName = _currentTrackName,
                CarName = "sessione corrente",
                Points = pts,
                IsSaved = false
            });
        }

        // Giri salvati
        foreach (SavedLapListItem item in SavedList.SelectedItems)
        {
            var saved = _savedLaps.FirstOrDefault(x => x.Path == item.FilePath).Lap;
            if (saved == null) continue;

            if (!string.IsNullOrWhiteSpace(refTrack) && !SavedLapStore.SameTrack(refTrack, saved.TrackName))
            {
                var res = MessageBox.Show(
                    $"Il giro salvato è su un tracciato diverso.\n\n" +
                    $"Riferimento: {refTrack}\n" +
                    $"Giro salvato: {saved.TrackName}\n\n" +
                    @"Vuoi comunque aggiungerlo al confronto?",
                    "Tracciato diverso",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (res != MessageBoxResult.Yes)
                {
                    _suppressSelectionEvents = true;
                    SavedList.SelectedItems.Remove(item);
                    _suppressSelectionEvents = false;
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(refTrack)) refTrack = saved.TrackName;

            entries.Add(new ComparisonEntry
            {
                Label = $"Lap {saved.LapNumber} • {saved.CarName}",
                TrackName = saved.TrackName,
                CarName = saved.CarName,
                Points = saved.Points.Select(sp => new TelemetryPoint
                {
                    Time = sp.T, Speed = sp.S, Throttle = sp.Th, Brake = sp.Br,
                    Steering = sp.St, RPM = sp.R
                }).ToList(),
                IsSaved = true
            });
        }

        if (entries.Count > 2)
            entries = entries.Take(2).ToList();

        DrawChart(entries);
    }

    private void DrawChart(List<ComparisonEntry> entries)
    {
        if (ChartCanvas is null) return;
        ChartCanvas.Children.Clear();

        double W = ChartCanvas.ActualWidth;
        double H = ChartCanvas.ActualHeight;
        if (W < 80 || H < 80) return;

        if (entries == null || entries.Count == 0)
        {
            Center(W, H, "Select laps from the session or saved library", 14, "#FFFFFF");
            return;
        }

        var lanes = new List<Lane>();
        if (ChkSpeed.IsChecked == true)
            lanes.Add(new Lane("Speed", "km/h", p => p.Speed * 3.6, Color.FromRgb(255, 255, 255)));
        if (ChkThrottle.IsChecked == true)
            lanes.Add(new Lane("Throttle", "%", p => p.Throttle * 100.0, Color.FromRgb(0, 214, 255)));
        if (ChkBrake.IsChecked == true)
            lanes.Add(new Lane("Brake", "%", p => p.Brake * 100.0, Color.FromRgb(255, 82, 82)));
        if (ChkSteering.IsChecked == true)
            lanes.Add(new Lane("Steering", "raw", p => p.Steering, Color.FromRgb(150, 255, 130)));
        if (ChkRpm.IsChecked == true)
            lanes.Add(new Lane("Engine RPM", "rpm", p => p.RPM, Color.FromRgb(255, 205, 60)));

        if (lanes.Count == 0)
        {
            Center(W, H, "Select at least one channel", 14, "#FFFFFF");
            return;
        }

        double plotW = W - LeftPad - RightPad;
        double plotH = H - TopPad - BottomPad;
        if (plotW < 30 || plotH < 30) return;

        double laneH = plotH / lanes.Count;
        double tMax = entries.Max(e => e.Points.Count > 0 ? e.Points[^1].Time : 0);
        double tSpan = Math.Max(1e-6, tMax);

        // Sfondo corsie
        for (int i = 0; i < lanes.Count; i++)
        {
            var bg = new Rectangle
            {
                Width = plotW,
                Height = Math.Max(1, laneH - 3),
                Fill = new SolidColorBrush(i % 2 == 0 ? Color.FromRgb(37, 37, 40) : Color.FromRgb(31, 31, 34)),
                Stroke = new SolidColorBrush(Color.FromRgb(58, 58, 62)),
                StrokeThickness = 1
            };
            Canvas.SetLeft(bg, LeftPad);
            Canvas.SetTop(bg, TopPad + i * laneH);
            ChartCanvas.Children.Add(bg);
        }

        // Griglia temporale
        for (int s = 0; s <= 10; s++)
        {
            double x = LeftPad + plotW * s / 10.0;
            var grid = new Line
            {
                X1 = x, Y1 = TopPad, X2 = x, Y2 = TopPad + plotH,
                Stroke = new SolidColorBrush(Color.FromRgb(50, 50, 54)),
                StrokeThickness = 1
            };
            ChartCanvas.Children.Add(grid);

            double t = tSpan * s / 10.0;
            var lab = Label($"{t:F1}s", 10, "#FFFFFF");
            Canvas.SetLeft(lab, x - 16);
            Canvas.SetTop(lab, TopPad + plotH + 6);
            ChartCanvas.Children.Add(lab);
        }

        // Tracce
        for (int gi = 0; gi < entries.Count; gi++)
        {
            var entry = entries[gi];
            bool dashed = gi > 0;

            for (int i = 0; i < lanes.Count; i++)
            {
                var lane = lanes[i];
                double laneTop = TopPad + i * laneH;
                double traceTop = laneTop + 4;
                double traceH = Math.Max(6, laneH - 11);

                double vMin = double.MaxValue, vMax = double.MinValue;
                foreach (var p in entry.Points)
                {
                    double v = lane.Sel(p);
                    if (v < vMin) vMin = v;
                    if (v > vMax) vMax = v;
                }
                double vSpan = vMax - vMin;
                if (vSpan < 1e-9) { vMin -= 0.5; vSpan = 1.0; }

                byte alpha = dashed ? (byte)200 : (byte)255;
                var color = Color.FromArgb(alpha, lane.Color.R, lane.Color.G, lane.Color.B);
                var brush = new SolidColorBrush(color);

                var poly = new Polyline
                {
                    Points = BuildTrace(entry.Points, lane.Sel, 0, tSpan, vMin, vSpan, plotW, traceH, 2200),
                    Stroke = brush,
                    StrokeThickness = dashed ? 1.1 : 1.5,
                    RenderTransform = new TranslateTransform(LeftPad, traceTop)
                };
                if (dashed) poly.StrokeDashArray = new DoubleCollection { 4, 3 };

                ChartCanvas.Children.Add(poly);
            }
        }

        // Etichette corsie
        for (int i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            double laneTop = TopPad + i * laneH;

            var name = Label(lane.Name, 11, lane.Color);
            name.FontWeight = FontWeights.SemiBold;
            Canvas.SetLeft(name, 5);
            Canvas.SetTop(name, laneTop + 3);
            ChartCanvas.Children.Add(name);

            var unit = Label(lane.Unit, 9, "#FFFFFF");
            Canvas.SetLeft(unit, 5);
            Canvas.SetTop(unit, laneTop + 18);
            ChartCanvas.Children.Add(unit);
        }

        // Legenda
        double legendY = TopPad + 2;
        for (int gi = 0; gi < entries.Count; gi++)
        {
            var txt = Label((gi == 0 ? "— " : "- - ") + entries[gi].Label, 11, gi == 0 ? "#FFFFFF" : "#B0B0B0");
            Canvas.SetLeft(txt, LeftPad + 8);
            Canvas.SetTop(txt, legendY + gi * 16);
            ChartCanvas.Children.Add(txt);
        }
    }

    private PointCollection BuildTrace(
        List<TelemetryPoint> points,
        Func<TelemetryPoint, double> sel,
        double tMin, double tSpan,
        double vMin, double vSpan,
        double width, double height,
        int buckets)
    {
        int n = points.Count;
        var pc = new PointCollection(Math.Min(n, buckets * 2) + 4);

        double MapX(double t) => (t - tMin) / tSpan * width;
        double MapY(double v) => height - ((v - vMin) / vSpan) * height;

        if (n <= buckets)
        {
            foreach (var p in points)
                pc.Add(new Point(MapX(p.Time), MapY(sel(p))));
            return pc;
        }

        double step = (double)n / buckets;
        for (int b = 0; b < buckets; b++)
        {
            int i0 = (int)(b * step);
            int i1 = (int)((b + 1) * step);
            if (i1 > n) i1 = n;
            if (i1 <= i0) continue;

            int iLo = i0, iHi = i0;
            double vLo = sel(points[i0]), vHi = vLo;

            for (int i = i0 + 1; i < i1; i++)
            {
                double v = sel(points[i]);
                if (v < vLo) { vLo = v; iLo = i; }
                if (v > vHi) { vHi = v; iHi = i; }
            }

            if (iLo <= iHi)
            {
                pc.Add(new Point(MapX(points[iLo].Time), MapY(vLo)));
                pc.Add(new Point(MapX(points[iHi].Time), MapY(vHi)));
            }
            else
            {
                pc.Add(new Point(MapX(points[iHi].Time), MapY(vHi)));
                pc.Add(new Point(MapX(points[iLo].Time), MapY(vLo)));
            }
        }
        return pc;
    }

    private static TextBlock Label(string text, double size, string hex) =>
        new() { Text = text, FontSize = size, Foreground = Brush(hex) };

    private static TextBlock Label(string text, double size, Color c) =>
        new() { Text = text, FontSize = size, Foreground = new SolidColorBrush(c) };

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private void Center(double w, double h, string text, double size, string hex)
    {
        var tb = Label(text, size, hex);
        Canvas.SetLeft(tb, w / 2 - 200);
        Canvas.SetTop(tb, h / 2 - 10);
        ChartCanvas.Children.Add(tb);
    }

    // ---------- SALVATAGGIO ----------
    private void SaveLap_Click(object sender, RoutedEventArgs e)
    {
        // Pattern matching: se SelectedItems[0] non è un LapInfo, esce subito.
        // In questo modo lapInfo è garantito non-null dal compilatore.
        if (LapList.SelectedItems.Count == 0 ||
            LapList.SelectedItems[0] is not LapInfo lapInfo)
        {
            MessageBox.Show("Select the lap you want to save first", "No lap selected",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Estrai i punti del giro e normalizzali a tempo 0
        var lapPoints = _allPoints
            .Where(p => p.Time >= lapInfo.StartTime && p.Time <= lapInfo.EndTime)
            .Select(p => new TelemetryPoint
            {
                Time = p.Time - lapInfo.StartTime,
                Speed = p.Speed, Throttle = p.Throttle, Brake = p.Brake,
                Steering = p.Steering, RPM = p.RPM
            })
            .ToList();

        if (lapPoints.Count < 2)
        {
            MessageBox.Show("The selected lap does not have enough data.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var filePath = _selectedFile ?? "";

        var meta = _reader.ReadMetadata(filePath);
        var dlg = new SaveLapDialog(
            track: _currentTrackName,
            car: DetectCarName(meta),
            carClass: "",
            date: DetectDate(meta, filePath),
            lapInfo: $"Lap {lapInfo.LapNumber}   •   " + TimeSpan.FromSeconds(lapInfo.LapTime).ToString(@"hh\:mm\:ss\.fff")
        );

        if (dlg.ShowDialog() != true || !dlg.Confirmed) return;

        var saved = new SavedLap
        {
            TrackName = dlg.TrackName,
            CarName = dlg.CarName,
            CarClass = dlg.CarClass,
            LapNumber = lapInfo.LapNumber,
            LapTimeSeconds = lapInfo.LapTime,
            LapDateIso = dlg.LapDate,
            SavedAtIso = DateTime.Now.ToString("s"),
            Note = dlg.Note,
            Points = DownsampleForSave(lapPoints, 4000)
        };

        try
        {
            var path = _store.Save(saved);
            RefreshSavedList();
            StatusText.Text = $"Lap saved: {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error saving lap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static List<SavedPoint> DownsampleForSave(List<TelemetryPoint> pts, int target)
    {
        var result = new List<SavedPoint>();
        if (pts.Count == 0) return result;

        int step = Math.Max(1, pts.Count / target);
        for (int i = 0; i < pts.Count; i += step)
        {
            var p = pts[i];
            result.Add(new SavedPoint
            {
                T = p.Time, S = p.Speed, Th = p.Throttle, Br = p.Brake, St = p.Steering, R = p.RPM
            });
        }

        var last = pts[^1];
        if (result.Count == 0 || Math.Abs(result[^1].T - last.Time) > 1e-6)
            result.Add(new SavedPoint { T = last.Time, S = last.Speed, Th = last.Throttle, Br = last.Brake, St = last.Steering, R = last.RPM });

        return result;
    }

    // ---------- HELPERS ----------
    private static string DetectTrackName(Dictionary<string, string> meta, string filePath)
    {
        foreach (var kv in meta)
        {
            var k = kv.Key.ToLowerInvariant();
            if (k.Contains("track") || k.Contains("circuit") || k.Contains("venue") || k.Contains("map"))
                if (!string.IsNullOrWhiteSpace(kv.Value)) return kv.Value.Trim();
        }

        var name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        var idx = name.LastIndexOf('_');
        if (idx > 0)
        {
            var head = name.Substring(0, idx);
            var idx2 = head.LastIndexOf('_');
            if (idx2 > 0) head = head.Substring(0, idx2);
            return head.Replace('_', ' ').Trim();
        }
        return name;
    }

    private static string DetectCarName(Dictionary<string, string> meta)
    {
        foreach (var kv in meta)
        {
            var k = kv.Key.ToLowerInvariant();
            if (k.Contains("vehicle") || k.Contains("car"))
                if (!string.IsNullOrWhiteSpace(kv.Value)) return kv.Value.Trim();
        }
        return "";
    }

    private static string DetectDate(Dictionary<string, string> meta, string filePath)
    {
        foreach (var kv in meta)
        {
            var k = kv.Key.ToLowerInvariant();
            if (k.Contains("date") || k.Contains("time"))
                if (!string.IsNullOrWhiteSpace(kv.Value)) return kv.Value.Trim();
        }
        var m = System.Text.RegularExpressions.Regex.Match(filePath, @"\d{4}-\d{2}-\d{2}");
        return m.Success ? m.Value : DateTime.Now.ToString("yyyy-MM-dd");
    }

    private void RefreshSavedList()
    {
        _savedLaps = _store.LoadAll();

        var items = _savedLaps.Select(x => new SavedLapListItem
        {
            FilePath = x.Path,
            TrackName = x.Lap.TrackName,
            CarName = x.Lap.CarName,
            DisplayDate = x.Lap.LapDateIso,
            LapNumber = x.Lap.LapNumber,
            LapTimeFormatted = TimeSpan.FromSeconds(x.Lap.LapTimeSeconds).ToString(@"hh\:mm\:ss\.fff")
        }).ToList();

        _suppressSelectionEvents = true;
        SavedList.ItemsSource = items;
        SavedList.SelectedItems.Clear();
        _suppressSelectionEvents = false;
    }

    // ---------- EXPORT CSV ----------
    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_allPoints.Count == 0)
        {
            MessageBox.Show("Please load a session first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV separated by semicolon (*.csv)|*.csv",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_selectedFile ?? "telemetry") + "_export.csv",
            Title = "Export Telemetry for Excel"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            StatusText.Text = "Exporting CSV...";

            var c = CultureInfo.CurrentCulture;
            var sb = new StringBuilder(1 << 20);

            sb.AppendLine("Time_s;Lap;Speed_kmh;Throttle_pct;Brake_pct;Steering;Engine_RPM");

            int lapIdx = 0;
            foreach (var p in _allPoints)
            {
                while (lapIdx + 1 < _allLaps.Count && _allLaps[lapIdx + 1].Time <= p.Time) lapIdx++;
                int lapNo = _allLaps.Count > 0 ? _allLaps[lapIdx].LapNumber : 0;

                sb.Append(p.Time.ToString("0.####", c)).Append(';')
                  .Append(lapNo).Append(';')
                  .Append((p.Speed * 3.6).ToString("0.##", c)).Append(';')
                  .Append((p.Throttle * 100).ToString("0.##", c)).Append(';')
                  .Append((p.Brake * 100).ToString("0.##", c)).Append(';')
                  .Append(p.Steering.ToString("0.####", c)).Append(';')
                  .Append(p.RPM.ToString("0.#", c)).Append('\n');
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);

            StatusText.Text = $"CSV exported: {System.IO.Path.GetFileName(dlg.FileName)}";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error exporting";
            MessageBox.Show(ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}