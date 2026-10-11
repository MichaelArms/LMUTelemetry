using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;
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
    private List<SavedLapListItem> _savedItems = new();
    private readonly List<object> _comparison = new();
    private static readonly SolidColorBrush TransparentBrush = new SolidColorBrush(Colors.Transparent);

    private string _currentTrackName = "";
    private string _currentSessionDate = "";
    private bool _suppressSelectionEvents = false;
    

    private static readonly Color[] LapColors =
    {
        Color.FromRgb(0, 255, 255),    // Giro 1: ciano
        Color.FromRgb(255, 145, 0)     // Giro 2: arancio
    };
    private const double LeftPad = 74, RightPad = 12, TopPad = 6, BottomPad = 48;

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
            _comparison.Clear();
            _allPoints.Clear();
            _allLaps.Clear();
            _lapInfos.Clear();
            _suppressSelectionEvents = true;
            LapList.ItemsSource = null;
            _suppressSelectionEvents = false;
            ChartCanvas.Children.Clear();
            SessionInfo.Text = string.Empty;
            BtnSaveLap.IsEnabled = false;
            SessionHeader.Text = string.Empty;

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
            _currentSessionDate = DetectDate(meta, filePath);

            TrackRefText.Text = _currentTrackName != ""
                ? $"Reference track: {_currentTrackName}"
                : "Track: not detected";

            SessionHeader.Text = _currentTrackName != ""
                ? $"{_currentTrackName}  •  {_currentSessionDate}"
                : _currentSessionDate;

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


    // Risale dal punto cliccato all'elemento della lista, gestendo sia
    // Visual sia ContentElement (es. <Run> dentro TextBlock: il genitore
    // logico è il TextBlock, non un TextElement).
    private static object? ItemAtPoint(ListBox list, Point p)
    {
        var el = list.InputHitTest(p) as DependencyObject;
        while (el != null && el != list)
        {
            if (el is ListBoxItem lbi)
                return list.ItemContainerGenerator.ItemFromContainer(lbi);
            el = GetParentAny(el);
        }
        return null;
    }

    private static DependencyObject? GetParentAny(DependencyObject el)
    {
        if (el is Visual v)
            return VisualTreeHelper.GetParent(v);

        if (el is ContentElement)
            return LogicalTreeHelper.GetParent(el);   // Run -> TextBlock -> StackPanel -> ...

        return null;
    }
    private void Channel_Click(object sender, RoutedEventArgs e) => ApplyComparisonState();
    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyComparisonState();
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

            ComputeDistance(pts);   // <-- CALCOLA LA DISTANZA PER IL GIRO DI SESSIONE
            if (ChkHideShifts.IsChecked == true)
            {
                MedianFilter(pts, p => p.Throttle * 100.0, (p, v) => p.Throttle = v / 100.0, 0.30);
                MedianFilter(pts, p => p.Brake * 100.0, (p, v) => p.Brake = v / 100.0, 0.30);
            }

            entries.Add(new ComparisonEntry
            {
                Label = $"L{li.LapNumber} • {li.LapTimeFormatted}",
                TrackName = _currentTrackName,
                CarName = "sessione corrente",
                Points = pts,
                IsSaved = false,
                SourceItem = li
            });
        }

        // Giri salvati
        var toRemove = new List<SavedLapListItem>();

        foreach (SavedLapListItem item in SavedList.SelectedItems)
        {
            var saved = _savedLaps.FirstOrDefault(x => x.Path == item.FilePath).Lap;
            if (saved == null) continue;

            // Verifica tracciato (rete di sicurezza: normalmente la lista è già filtrata)
            if (!string.IsNullOrWhiteSpace(refTrack) && !SavedLapStore.SameTrack(refTrack, saved.TrackName))
            {
                MessageBox.Show(
                    $"This saved lap is from a different track and will not be added to the comparison.\n\n" +
                    $"Reference: {refTrack}\n" +
                    $"Saved lap: {saved.TrackName}",
                    "Different track",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                toRemove.Add(item);   // lo rimuoveremo DOPO il foreach
                continue;             // non aggiungerlo al confronto
            }

            if (string.IsNullOrWhiteSpace(refTrack)) refTrack = saved.TrackName;

            var ptsSaved = saved.Points.Select(sp => new TelemetryPoint
            {
                Time = sp.T, Speed = sp.S, Throttle = sp.Th, Brake = sp.Br,
                Steering = sp.St, RPM = sp.R
            }).ToList();

            ComputeDistance(ptsSaved);

            if (ChkHideShifts.IsChecked == true)
            {
                MedianFilter(ptsSaved, p => p.Throttle * 100.0, (p, v) => p.Throttle = v / 100.0, 0.30);
                MedianFilter(ptsSaved, p => p.Brake * 100.0, (p, v) => p.Brake = v / 100.0, 0.30);
            }

            entries.Add(new ComparisonEntry
            {
                Label = $"L{saved.LapNumber} • {TimeSpan.FromSeconds(saved.LapTimeSeconds).ToString(@"hh\:mm\:ss\.fff")}",
                TrackName = saved.TrackName,
                CarName = saved.CarName,
                Points = ptsSaved,
                IsSaved = true,
                SourceItem = item
            });
        }

        // Rimuovi gli elementi non coerenti DOPO il foreach (evita il crash)
        if (toRemove.Count > 0)
        {
            _suppressSelectionEvents = true;
            foreach (var it in toRemove)
                SavedList.SelectedItems.Remove(it);
            _suppressSelectionEvents = false;
        }

        if (entries.Count > 2)
            entries = entries.Take(2).ToList();

        // Reset evidenziazioni
        foreach (var li in _lapInfos) li.RowBg = new SolidColorBrush(Colors.Transparent);
        foreach (var si in _savedItems) si.RowBg = new SolidColorBrush(Colors.Transparent);

        // Evidenzia con il colore della traccia i giri attualmente in confronto
        for (int gi = 0; gi < entries.Count; gi++)
        {
            var tint = new SolidColorBrush(Color.FromArgb(255, LapColors[gi].R, LapColors[gi].G, LapColors[gi].B));
            if (entries[gi].SourceItem is LapInfo li2) li2.RowBg = tint;
            else if (entries[gi].SourceItem is SavedLapListItem si2) si2.RowBg = tint;
        }

        LapList.Items.Refresh();
        SavedList.Items.Refresh();

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
            Center(W, H, "Select laps from the session or from the saved library", 14, "#FFFFFF");
            return;
        }

        bool useDistance = RbDistance.IsChecked == true;
        // Stile MoTeC: in confronto (2 giri) un colore unico per giro su tutte le corsie.
        // Coppia colorblind-safe ad alto contrasto su sfondo scuro.
        bool compareMode = entries.Count == 2;
        

        var lanes = new List<Lane>();
        if (ChkSpeed.IsChecked == true)
            lanes.Add(new Lane("Speed", "km/h", p => p.Speed, Color.FromRgb(255, 255, 255)));
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

        // Delta: solo in modalità distanza con esattamente 2 giri
        var deltaData = new List<(double X, double Y)>();
        bool showDelta = useDistance && entries.Count == 2;
        if (showDelta)
        {
            deltaData = ComputeDelta(entries[0].Points, entries[1].Points);
            if (deltaData.Count < 2) showDelta = false;
        }

        double plotW = W - LeftPad - RightPad;
        double plotH = H - TopPad - BottomPad;
        if (plotW < 30 || plotH < 30) return;

        int totalLanes = lanes.Count + (showDelta ? 1 : 0);
        double laneH = plotH / totalLanes;
        int laneOffset = showDelta ? 1 : 0;   // il delta occupa la corsia 0

        // Asse X comune (tempo o distanza)
        Func<TelemetryPoint, double> selX = useDistance ? (p => p.Distance) : (p => p.Time);
        double xMax = 0;
        foreach (var e in entries)
        {
            if (e.Points.Count == 0) continue;
            double last = selX(e.Points[^1]);
            if (last > xMax) xMax = last;
        }
        double xSpan = Math.Max(1e-6, xMax);

        // ---- Sfondo corsie ----
        for (int i = 0; i < totalLanes; i++)
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

        // ---- Griglia asse X ----
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

            double xVal = xSpan * s / 10.0;
            string lab = useDistance ? $"{xVal:F0} m" : $"{xVal:F1} s";
            var labEl = Label(lab, 10, "#FFFFFF");
            Canvas.SetLeft(labEl, x - 20);
            Canvas.SetTop(labEl, TopPad + plotH + 6);
            ChartCanvas.Children.Add(labEl);
        }

        // ---- CORSIA DELTA (in alto, se attiva) ----
        if (showDelta)
        {
            double dTop = TopPad;
            double traceTop = dTop + 4;
            double traceH = Math.Max(6, laneH - 11);

            double dMin = deltaData.Min(p => p.Y);
            double dMax = deltaData.Max(p => p.Y);
            dMin = Math.Min(dMin, 0);   // forza lo zero visibile
            dMax = Math.Max(dMax, 0);
            double dSpan = dMax - dMin;
            if (dSpan < 1e-9) { dMin -= 0.5; dSpan = 1.0; }

            // linea dello zero
            double zeroY = traceTop + traceH - ((0 - dMin) / dSpan) * traceH;
            var zeroLine = new Line
            {
                X1 = LeftPad, Y1 = zeroY, X2 = LeftPad + plotW, Y2 = zeroY,
                Stroke = new SolidColorBrush(Color.FromRgb(110, 110, 116)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 3 }
            };
            ChartCanvas.Children.Add(zeroLine);

            // traccia delta
            var poly = new Polyline
            {
                Points = BuildTraceXY(deltaData, 0, xSpan, dMin, dSpan, plotW, traceH, 2000),
                Stroke = new SolidColorBrush(Color.FromRgb(255, 77, 210)),
                StrokeThickness = 1.1,
                RenderTransform = new TranslateTransform(LeftPad, traceTop)
            };
            ChartCanvas.Children.Add(poly);

            // etichetta con gap finale
            double finalGap = deltaData[^1].Y;
            string gapTxt = finalGap >= 0 ? $"+{finalGap:F3}s" : $"{finalGap:F3}s";
            var name = Label($"Δ Time (lap2 − lap1)   final gap: {gapTxt}", 11, Color.FromRgb(255, 77, 210));
            name.FontWeight = FontWeights.SemiBold;
            Canvas.SetLeft(name, 5);
            Canvas.SetTop(name, dTop + 3);
            ChartCanvas.Children.Add(name);

            var unit = Label("s", 9, "#FFFFFF");
            Canvas.SetLeft(unit, 5);
            Canvas.SetTop(unit, dTop + 18);
            ChartCanvas.Children.Add(unit);
        }

        // ---- TRACCE DEI CANALI ----
        for (int gi = 0; gi < entries.Count; gi++)
        {
            var entry = entries[gi];
            bool dashed = gi > 0;

            // Risoluzione adattiva: sotto i 15k campioni disegniamo TUTTI i punti
            // (come i tool professionali), sopra usiamo il min/max bucketing.
            int buckets = entry.Points.Count <= 15000 ? entry.Points.Count : 6000;

            for (int i = 0; i < lanes.Count; i++)
            {
                var lane = lanes[i];
                double laneTop = TopPad + (laneOffset + i) * laneH;
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

                var baseCol = compareMode ? LapColors[gi] : lane.Color;
                var brush = new SolidColorBrush(baseCol);

                var poly = new Polyline
                {
                    Points = BuildTrace(entry.Points, selX, lane.Sel, 0, xSpan, vMin, vSpan, plotW, traceH, buckets),
                    Stroke = brush,
                    StrokeThickness = dashed ? 0.9 : 1.0,
                    RenderTransform = new TranslateTransform(LeftPad, traceTop)
                };

                ChartCanvas.Children.Add(poly);
            }
        }

        // ---- ETICHETTE CORSIE CANALI ----
        for (int i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            double laneTop = TopPad + (laneOffset + i) * laneH;

            var name = Label(lane.Name, 11, compareMode ? Color.FromRgb(200, 200, 206) : lane.Color);
            name.FontWeight = FontWeights.SemiBold;
            Canvas.SetLeft(name, 5);
            Canvas.SetTop(name, laneTop + 3);
            ChartCanvas.Children.Add(name);

            var unit = Label(lane.Unit, 9, "#FFFFFF");
            Canvas.SetLeft(unit, 5);
            Canvas.SetTop(unit, laneTop + 18);
            ChartCanvas.Children.Add(unit);
        }

        // ---- LEGENDA (sotto il plot: non copre mai i dati) ----
        // Per ogni giro disegna un campione di linea REALE (continua o tratteggiata)
        // seguito dall'etichetta del giro, così l'associazione è immediata.
        if (compareMode)
        {
            double legendY = TopPad + plotH + 26;
            double lx = LeftPad;
            for (int gi = 0; gi < entries.Count; gi++)
            {
                bool dashed = gi > 0;
                var col = LapColors[gi];

                var sw = new Line
                {
                    X1 = lx, Y1 = legendY + 7, X2 = lx + 30, Y2 = legendY + 7,
                    Stroke = new SolidColorBrush(col),
                    StrokeThickness = 1.0
                };
                ChartCanvas.Children.Add(sw);

                var txt = Label(entries[gi].Label, 11, col);
                Canvas.SetLeft(txt, lx + 36);
                Canvas.SetTop(txt, legendY);
                ChartCanvas.Children.Add(txt);

                lx += 36 + entries[gi].Label.Length * 6.3 + 28;
            }
        }  
    }
    private PointCollection BuildTrace(
        List<TelemetryPoint> points,
        Func<TelemetryPoint, double> selX,
        Func<TelemetryPoint, double> selY,
        double xMin, double xSpan,
        double vMin, double vSpan,
        double width, double height,
        int buckets)
    {
        int n = points.Count;
        var pc = new PointCollection(Math.Min(n, buckets * 2) + 4);

        double MapX(double x) => (x - xMin) / xSpan * width;
        double MapY(double v) => height - ((v - vMin) / vSpan) * height;

        if (n <= buckets)
        {
            foreach (var p in points)
                pc.Add(new Point(MapX(selX(p)), MapY(selY(p))));
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
            double vLo = selY(points[i0]), vHi = vLo;

            for (int i = i0 + 1; i < i1; i++)
            {
                double v = selY(points[i]);
                if (v < vLo) { vLo = v; iLo = i; }
                if (v > vHi) { vHi = v; iHi = i; }
            }

            if (iLo <= iHi)
            {
                pc.Add(new Point(MapX(selX(points[iLo])), MapY(vLo)));
                pc.Add(new Point(MapX(selX(points[iHi])), MapY(vHi)));
            }
            else
            {
                pc.Add(new Point(MapX(selX(points[iHi])), MapY(vHi)));
                pc.Add(new Point(MapX(selX(points[iLo])), MapY(vLo)));
            }
        }
        return pc;
    }

    // Variante generica per il delta (lista di coppie X,Y)
    private PointCollection BuildTraceXY(
        IReadOnlyList<(double X, double Y)> data,
        double xMin, double xSpan,
        double yMin, double ySpan,
        double width, double height,
        int buckets)
    {
        int n = data.Count;
        var pc = new PointCollection(Math.Min(n, buckets * 2) + 4);

        double MapX(double x) => (x - xMin) / xSpan * width;
        double MapY(double y) => height - ((y - yMin) / ySpan) * height;

        if (n <= buckets)
        {
            foreach (var p in data)
                pc.Add(new Point(MapX(p.X), MapY(p.Y)));
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
            double vLo = data[i0].Y, vHi = vLo;

            for (int i = i0 + 1; i < i1; i++)
            {
                double v = data[i].Y;
                if (v < vLo) { vLo = v; iLo = i; }
                if (v > vHi) { vHi = v; iHi = i; }
            }

            if (iLo <= iHi)
            {
                pc.Add(new Point(MapX(data[iLo].X), MapY(vLo)));
                pc.Add(new Point(MapX(data[iHi].X), MapY(vHi)));
            }
            else
            {
                pc.Add(new Point(MapX(data[iHi].X), MapY(vHi)));
                pc.Add(new Point(MapX(data[iLo].X), MapY(vLo)));
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
        var sessionLaps = _comparison.OfType<LapInfo>().ToList();
        if (sessionLaps.Count == 0)
        {
            MessageBox.Show("Click the lap you want to save first (it gets the colored bar).",
                            "No lap", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (sessionLaps.Count > 1)
        {
            MessageBox.Show("Keep only one session lap in the comparison to save it.",
                            "Too many laps", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var lapInfo = sessionLaps[0];

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

    private void DeleteSavedLap_Click(object sender, RoutedEventArgs e)
    {
        var toDelete = _comparison.OfType<SavedLapListItem>().ToList();
        if (toDelete.Count == 0)
        {
            MessageBox.Show("Click the saved lap(s) you want to delete first (they get the colored bar).",
                            "No selection",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int count = toDelete.Count;
        string message = count == 1
            ? "Are you sure you want to delete the selected saved lap?\nThis action cannot be undone."
            : $"Are you sure you want to delete {count} saved laps?\nThis action cannot be undone.";

        var confirm = MessageBox.Show(message, "Confirm deletion",
                                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        int deleted = 0;
        var errors = new List<string>();

        foreach (var item in toDelete)
        {
            try
            {
                if (System.IO.File.Exists(item.FilePath))
                {
                    System.IO.File.Delete(item.FilePath);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{System.IO.Path.GetFileName(item.FilePath)}: {ex.Message}");
            }
        }

        // Aggiorna la lista e il grafico
        RefreshSavedList();
        //UpdateComparison();

        StatusText.Text = errors.Count == 0
            ? $"Deleted {deleted} saved lap(s)."
            : $"Deleted {deleted} lap(s), {errors.Count} error(s).";

        if (errors.Count > 0)
        {
            MessageBox.Show("Some files could not be deleted:\n\n" + string.Join("\n", errors),
                            "Deletion errors",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
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
    // Calcola la distanza percorsa integrando la velocità (m/s) sul tempo
    private static void ComputeDistance(List<TelemetryPoint> pts)
    {
        if (pts.Count == 0) return;
        pts[0].Distance = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double dt = pts[i].Time - pts[i - 1].Time;
            if (dt < 0) dt = 0;
            double avgSpeed = (pts[i - 1].Speed + pts[i].Speed) / 2.0 / 3.6; // speed è in km/h, converti in m/s
            pts[i].Distance = pts[i - 1].Distance + avgSpeed * dt;
        }
    }

    // Filtro a mediana su finestra temporale: rimuove le escursioni brevi
    // (cut di cambiata, blip di doppietta) preservando rampe e plateau.
    // Solo visivo: CSV e salvataggi restano grezzi.
    private static void MedianFilter(
        List<TelemetryPoint> pts,
        Func<TelemetryPoint, double> get,
        Action<TelemetryPoint, double> set,
        double windowSec)
    {
        int n = pts.Count;
        if (n < 3) return;

        double dur = pts[n - 1].Time - pts[0].Time;
        if (dur <= 0) return;

        int rate = Math.Max(1, (int)Math.Round(n / dur));
        int half = Math.Max(1, (int)Math.Round(windowSec * rate) / 2);

        var src = new double[n];
        for (int i = 0; i < n; i++) src[i] = get(pts[i]);

        var dst = new double[n];
        var buf = new double[2 * half + 1];

        for (int i = 0; i < n; i++)
        {
            int a = Math.Max(0, i - half);
            int b = Math.Min(n - 1, i + half);
            int m = b - a + 1;

            for (int k = 0; k < m; k++) buf[k] = src[a + k];
            Array.Sort(buf, 0, m);
            dst[i] = buf[m / 2];
        }

        for (int i = 0; i < n; i++) set(pts[i], dst[i]);
    }

    // Interpola il tempo a una data distanza (pts ordinati per Distance crescente)
    private static double InterpTimeAtDistance(List<TelemetryPoint> pts, double d, ref int idx)
    {
        while (idx < pts.Count - 1 && pts[idx + 1].Distance < d) idx++;
        if (idx >= pts.Count - 1) return double.NaN;

        var p0 = pts[idx];
        var p1 = pts[idx + 1];
        double span = p1.Distance - p0.Distance;
        if (span < 1e-9) return p0.Time;
        double frac = (d - p0.Distance) / span;
        return p0.Time + frac * (p1.Time - p0.Time);
    }

    // Calcola il delta temporale tra due giri in funzione della distanza
    private static List<(double X, double Y)> ComputeDelta(List<TelemetryPoint> refLap, List<TelemetryPoint> cmpLap, int numPoints = 1200)
    {
        var result = new List<(double, double)>();
        if (refLap.Count < 2 || cmpLap.Count < 2) return result;

        double refLen = refLap[^1].Distance;
        double cmpLen = cmpLap[^1].Distance;
        double maxDist = Math.Min(refLen, cmpLen);
        if (maxDist <= 1) return result;

        int iRef = 0, iCmp = 0;
        for (int k = 0; k <= numPoints; k++)
        {
            double d = maxDist * k / numPoints;
            double tRef = InterpTimeAtDistance(refLap, d, ref iRef);
            double tCmp = InterpTimeAtDistance(cmpLap, d, ref iCmp);
            if (double.IsNaN(tRef) || double.IsNaN(tCmp)) continue;
            result.Add((d, tCmp - tRef));   // positivo = giro2 più lento
        }
        return result;
    }

    private void Axis_Click(object sender, RoutedEventArgs e) => ApplyComparisonState();

    private void RefreshSavedList()
    {
        _savedLaps = _store.LoadAll();   // lista completa (serve per il lookup in UpdateComparison)

        // Mostra solo i giri coerenti col tracciato corrente.
        // Se non c'è un tracciato di riferimento (nessun duckdb), mostra tutti.
        var visible = _savedLaps
            .Where(x => string.IsNullOrWhiteSpace(_currentTrackName)
                        || SavedLapStore.SameTrack(_currentTrackName, x.Lap.TrackName))
            .Select(x => new SavedLapListItem
            {
                FilePath = x.Path,
                TrackName = x.Lap.TrackName,
                CarName = x.Lap.CarName,
                DisplayDate = x.Lap.LapDateIso,
                LapNumber = x.Lap.LapNumber,
                LapTimeFormatted = TimeSpan.FromSeconds(x.Lap.LapTimeSeconds).ToString(@"hh\:mm\:ss\.fff")
            }).ToList();

        SavedList.ItemsSource = visible;
        _savedItems = visible;
        // I giri salvati vengono ricreati: scarta dal confronto quelli non più presenti
        _comparison.RemoveAll(o => o is SavedLapListItem s && !_savedItems.Contains(s));
        ApplyComparisonState();
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
                  .Append(p.Speed.ToString("0.##", c)).Append(';')
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


    private void LapsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        var clicked = ItemAtPoint(list, e.GetPosition(list));
        if (clicked is null)
        {
            return;
        }

        e.Handled = true;   // blocca il click PRIMA che il controllo lo gestisca
        ToggleComparison(clicked);
    }

    private void ToggleComparison(object clicked)
    {

        // Click su un giro già nel confronto -> lo toglie
        if (_comparison.Contains(clicked))
        {
            _comparison.Remove(clicked);
            ApplyComparisonState();
            return;
        }

        // Terzo giro -> rifiutato
        if (_comparison.Count >= 2)
        {
            StatusText.Text = "Max 2 laps in comparison: deselect one before adding another.";
            return;
        }

        // Validazione tracciato per i giri salvati
        if (clicked is SavedLapListItem si)
        {
            var saved = _savedLaps.FirstOrDefault(x => x.Path == si.FilePath).Lap;
            if (saved is null)
            {
                return;
            }

            var refTrack = CurrentRefTrack();

            if (!string.IsNullOrWhiteSpace(refTrack)
                && !SavedLapStore.SameTrack(refTrack, saved.TrackName))
            {
                MessageBox.Show(
                    $"This saved lap is from a different track and will not be added to the comparison.\n\n" +
                    $"Reference: {refTrack}\n" +
                    $"Saved lap: {saved.TrackName}",
                    "Different track",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _comparison.Add(clicked);
        ApplyComparisonState();
    }

    private string CurrentRefTrack()
    {
        if (!string.IsNullOrWhiteSpace(_currentTrackName)) return _currentTrackName;
        foreach (var o in _comparison)
            if (o is SavedLapListItem s) return s.TrackName;
        return "";
    }

    private static void SetRowBg(object src, SolidColorBrush brush)
    {
        if (src is LapInfo l) l.RowBg = brush;
        else if (src is SavedLapListItem s) s.RowBg = brush;
    }

    private void ApplyComparisonState()
    {
        // Barrette: reset e colori per posizione nel confronto
        foreach (var l in _lapInfos) l.RowBg = TransparentBrush;
        foreach (var s in _savedItems) s.RowBg = TransparentBrush;
        for (int gi = 0; gi < _comparison.Count && gi < 2; gi++)
            SetRowBg(_comparison[gi], new SolidColorBrush(LapColors[gi]));

        var entries = new List<ComparisonEntry>();

        for (int gi = 0; gi < _comparison.Count && gi < 2; gi++)
        {
            var src = _comparison[gi];

            if (src is LapInfo li)
            {
                var pts = _allPoints
                    .Where(p => p.Time >= li.StartTime && p.Time <= li.EndTime)
                    .Select(p => new TelemetryPoint
                    {
                        Time = p.Time - li.StartTime,
                        Speed = p.Speed, Throttle = p.Throttle, Brake = p.Brake,
                        Steering = p.Steering, RPM = p.RPM
                    }).ToList();

                ComputeDistance(pts);
                if (ChkHideShifts.IsChecked == true)
                {
                    MedianFilter(pts, p => p.Throttle * 100.0, (p, v) => p.Throttle = v / 100.0, 0.30);
                    MedianFilter(pts, p => p.Brake * 100.0, (p, v) => p.Brake = v / 100.0, 0.30);
                }

                entries.Add(new ComparisonEntry
                {
                    Label = $"L{li.LapNumber} • {li.LapTimeFormatted}",
                    TrackName = _currentTrackName,
                    CarName = "sessione corrente",
                    Points = pts,
                    IsSaved = false,
                    SourceItem = li
                });
            }
            else if (src is SavedLapListItem si)
            {
                var saved = _savedLaps.FirstOrDefault(x => x.Path == si.FilePath).Lap;
                if (saved == null) continue;

                var ptsSaved = saved.Points.Select(sp => new TelemetryPoint
                {
                    Time = sp.T, Speed = sp.S, Throttle = sp.Th, Brake = sp.Br,
                    Steering = sp.St, RPM = sp.R
                }).ToList();

                ComputeDistance(ptsSaved);
                if (ChkHideShifts.IsChecked == true)
                {
                    MedianFilter(ptsSaved, p => p.Throttle * 100.0, (p, v) => p.Throttle = v / 100.0, 0.30);
                    MedianFilter(ptsSaved, p => p.Brake * 100.0, (p, v) => p.Brake = v / 100.0, 0.30);
                }

                entries.Add(new ComparisonEntry
                {
                    Label = $"L{saved.LapNumber} • {TimeSpan.FromSeconds(saved.LapTimeSeconds).ToString(@"hh\:mm\:ss\.fff")}",
                    TrackName = saved.TrackName,
                    CarName = saved.CarName,
                    Points = ptsSaved,
                    IsSaved = true,
                    SourceItem = si
                });
            }
        }

        DrawChart(entries);
    }
}