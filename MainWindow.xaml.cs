using System;
using System.Linq;
using Windows.Graphics;
using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Graphics.Canvas.Geometry;
using System.Numerics;

namespace GuitarTools
{
    public sealed partial class MainWindow
    {
        private readonly Tuner _tuner;
        private const int TotalRows = 8;
        private const int TotalCols = 13;
        private DateTime _lastMatchTime;
        private DispatcherTimer? _resetTimer;

        public MainWindow()
        {
            InitializeComponent();
            _tuner = new Tuner();
            TuningsComboBox.DataContext = _tuner;
            AppWindow.Resize(new SizeInt32(1024, 830));
            AddNotesToFretboard();
            Tuning_SelectionChanged(TuningsComboBox, null);
            _tuner.PropertyChanged += CurrentNote_PropertyChanged!;
        }

        // ── Win2D Gauge Drawing ──────────────────────────────────────────────

        private void TunerCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            var ds = args.DrawingSession;
            float w = (float)sender.ActualWidth;
            float h = (float)sender.ActualHeight;
            float cx = w / 2f;
            float cy = h - 14f;                    // pivot near bottom
            float radius = MathF.Min(cx * 0.88f, h - 50f);
            const float arcStroke = 22f;

            // Colored arc segments (Red → Orange → Green → Orange → Red)
            DrawArcSegment(ds, sender, cx, cy, radius, arcStroke, -50f, -30f, Color.FromArgb(255, 200, 50, 50));
            DrawArcSegment(ds, sender, cx, cy, radius, arcStroke, -30f, -15f, Color.FromArgb(255, 220, 140, 0));
            DrawArcSegment(ds, sender, cx, cy, radius, arcStroke, -15f, 15f, Color.FromArgb(255, 40, 185, 70));
            DrawArcSegment(ds, sender, cx, cy, radius, arcStroke, 15f, 30f, Color.FromArgb(255, 220, 140, 0));
            DrawArcSegment(ds, sender, cx, cy, radius, arcStroke, 30f, 50f, Color.FromArgb(255, 200, 50, 50));

            // Tick marks and labels
            float tickOuter = radius + arcStroke / 2f + 4f;
            for (int c = -50; c <= 50; c += 5)
            {
                bool major = c % 10 == 0;
                float tickLen = major ? 18f : 9f;
                float angle = CentsToAngleRad(c);
                float cos = MathF.Cos(angle);
                float sin = MathF.Sin(angle);

                float x1 = cx + tickOuter * cos;
                float y1 = cy + tickOuter * sin;
                float x2 = cx + (tickOuter + tickLen) * cos;
                float y2 = cy + (tickOuter + tickLen) * sin;

                ds.DrawLine(x1, y1, x2, y2,
                    Color.FromArgb(255, 200, 200, 200),
                    major ? 2.5f : 1.5f);

                if (major && c != 0)
                {
                    float lx = cx + (tickOuter + tickLen + 16f) * cos;
                    float ly = cy + (tickOuter + tickLen + 16f) * sin;
                    using var tf = new CanvasTextFormat
                    {
                        FontSize = 13f,
                        HorizontalAlignment = CanvasHorizontalAlignment.Center,
                        VerticalAlignment = CanvasVerticalAlignment.Center
                    };
                    ds.DrawText(Math.Abs(c).ToString(), lx, ly,
                        Color.FromArgb(180, 180, 180, 180), tf);
                }
            }

            // "♭" and "♯" labels
            float labelR = radius + arcStroke / 2f + 48f;
            using var symbolFormat = new CanvasTextFormat
            {
                FontSize = 22f,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center
            };
            float leftAngle = CentsToAngleRad(-50f);
            ds.DrawText("♭", cx + labelR * MathF.Cos(leftAngle), cy + labelR * MathF.Sin(leftAngle),
                Color.FromArgb(200, 160, 160, 255), symbolFormat);
            float rightAngle = CentsToAngleRad(50f);
            ds.DrawText("♯", cx + labelR * MathF.Cos(rightAngle), cy + labelR * MathF.Sin(rightAngle),
                Color.FromArgb(200, 255, 160, 160), symbolFormat);

            // Note name and status
            var note = _tuner.CurrentNote;
            if (note != null)
            {
                // Needle
                float centsRaw = (float)Math.Clamp(note.CentDeviation, -50, 50);
                float needleAngle = CentsToAngleRad(centsRaw);
                float needleLen = radius - arcStroke / 2f - 8f;

                ds.DrawLine(cx, cy,
                    cx + needleLen * MathF.Cos(needleAngle),
                    cy + needleLen * MathF.Sin(needleAngle),
                    Colors.White, 3f);
                ds.FillCircle(cx, cy, 8f, Colors.White);
                ds.FillCircle(cx, cy, 5f, Color.FromArgb(255, 30, 30, 50));

                // Note name
                using var noteFormat = new CanvasTextFormat
                {
                    FontFamily = "Consolas",
                    FontSize = 64f,
                    HorizontalAlignment = CanvasHorizontalAlignment.Center,
                    VerticalAlignment = CanvasVerticalAlignment.Center
                };
                ds.DrawText(note.Note ?? "", cx, cy - radius * 0.52f, Colors.White, noteFormat);

                // Cents / in-tune indicator
                string statusText;
                Color statusColor;
                if (note.IsInTune)
                {
                    statusText = "IN TUNE  ✓";
                    statusColor = Color.FromArgb(255, 40, 220, 80);
                }
                else
                {
                    double c = note.CentDeviation;
                    statusText = c > 0 ? $"♯ +{c:F0} cents" : $"♭ {c:F0} cents";
                    statusColor = Math.Abs(c) < 15
                        ? Color.FromArgb(255, 220, 160, 0)
                        : Color.FromArgb(255, 210, 60, 60);
                }
                using var statusFormat = new CanvasTextFormat
                {
                    FontSize = 20f,
                    HorizontalAlignment = CanvasHorizontalAlignment.Center,
                    VerticalAlignment = CanvasVerticalAlignment.Center
                };
                ds.DrawText(statusText, cx, cy - radius * 0.3f, statusColor, statusFormat);
            }
            else
            {
                // No note — idle state
                string idleText = _tuner.IsTuning ? "Listening..." : "Press  ▶  Start";
                Color idleColor = _tuner.IsTuning
                    ? Color.FromArgb(160, 160, 160, 160)
                    : Color.FromArgb(100, 150, 150, 160);
                using var idleFormat = new CanvasTextFormat
                {
                    FontSize = 28f,
                    HorizontalAlignment = CanvasHorizontalAlignment.Center,
                    VerticalAlignment = CanvasVerticalAlignment.Center
                };
                ds.DrawText(idleText, cx, cy - radius * 0.42f, idleColor, idleFormat);

                // Center tick highlight when idle
                float zeroAngle = CentsToAngleRad(0);
                float centerOuter = radius + arcStroke / 2f + 4f;
                ds.DrawLine(
                    cx + centerOuter * MathF.Cos(zeroAngle),
                    cy + centerOuter * MathF.Sin(zeroAngle),
                    cx + (centerOuter + 22f) * MathF.Cos(zeroAngle),
                    cy + (centerOuter + 22f) * MathF.Sin(zeroAngle),
                    Color.FromArgb(120, 255, 255, 255), 3f);
            }
        }

        /// <summary>
        /// Draws a thick colored arc segment between two cent values.
        /// </summary>
        private static void DrawArcSegment(CanvasDrawingSession ds, ICanvasResourceCreator creator,
            float cx, float cy, float radius, float strokeWidth,
            float startCents, float endCents, Color color)
        {
            float a1 = CentsToAngleRad(startCents);
            float a2 = CentsToAngleRad(endCents);
            float sweepAngle = a2 - a1;

            float x1 = cx + radius * MathF.Cos(a1);
            float y1 = cy + radius * MathF.Sin(a1);
            float x2 = cx + radius * MathF.Cos(a2);
            float y2 = cy + radius * MathF.Sin(a2);

            using var pb = new CanvasPathBuilder(creator);
            pb.BeginFigure(x1, y1);
            pb.AddArc(
                new Vector2(x2, y2),
                radius, radius,
                0f,
                sweepAngle > 0 ? CanvasSweepDirection.Clockwise : CanvasSweepDirection.CounterClockwise,
                Math.Abs(sweepAngle) > MathF.PI ? CanvasArcSize.Large : CanvasArcSize.Small);
            pb.EndFigure(CanvasFigureLoop.Open);

            using var geom = CanvasGeometry.CreatePath(pb);
            ds.DrawGeometry(geom, color, strokeWidth, new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round });
        }

        /// <summary>
        /// Maps cents (-50..+50) to a screen angle in radians.
        /// -50 cents = 180° (left), 0 cents = 270° (up), +50 cents = 360° (right).
        /// </summary>
        private static float CentsToAngleRad(float cents)
        {
            float degrees = 270f + (cents / 50f) * 90f;
            return degrees * MathF.PI / 180f;
        }

        // ── Fretboard ────────────────────────────────────────────────────────

        private void AddNotesToFretboard()
        {
            for (var row = 0; row < 6; row++)
            {
                for (var col = 0; col <= 12; col++)
                {
                    var ellipse = new Ellipse
                    {
                        Width = 42,
                        Height = 42,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, -21),
                        Tag = (row, col)
                    };

                    if (col == 0)
                    {
                        // Open-string indicator — dark until triggered
                        ellipse.Fill = new SolidColorBrush(Color.FromArgb(255, 28, 20, 12));
                        ellipse.Stroke = new SolidColorBrush(Color.FromArgb(100, 120, 100, 80));
                        ellipse.StrokeThickness = 1.5;
                    }
                    else
                    {
                        // Non-open fret markers — invisible (not used for tuning)
                        ellipse.Opacity = 0;
                        ellipse.Fill = new SolidColorBrush(Colors.Transparent);
                    }

                    Grid.SetRow(ellipse, row);
                    Grid.SetColumn(ellipse, col);
                    FretboardGrid.Children.Add(ellipse);
                }
            }
        }

        private void CurrentNote_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "CurrentNote") return;
            TunerCanvas.Invalidate();

            if (_tuner.CurrentNote == null) return;
            if (TuningsComboBox.SelectedItem == null) return;

            var selectedTuning = (Tuning)TuningsComboBox.SelectedItem;
            var pitchToMatch = _tuner.CurrentNote.Note;
            if (pitchToMatch == null || !selectedTuning.Pitches.Contains(pitchToMatch)) return;

            var index = Array.IndexOf(selectedTuning.Pitches, pitchToMatch);
            if (index == -1) return;

            var rowOffset = 5 - index;
            var ellipse = GetEllipseAt(rowOffset, 0);
            if (ellipse == null) return;

            var difference = Math.Abs(_tuner.CurrentNote.ClosestPitch - _tuner.CurrentNote.MaxFrequency);
            var color = difference switch
            {
                <= 0.4 => Color.FromArgb(255, 40, 210, 80),
                <= 1.0 => Color.FromArgb(255, 220, 185, 0),
                _ => Color.FromArgb(255, 210, 55, 55)
            };
            ellipse.Fill = new SolidColorBrush(color);
            _lastMatchTime = DateTime.Now;
        }

        private void TunerToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_tuner.IsTuning)
            {
                _tuner.Stop();
                TunerToggleButton.Content = "▶  Start";
                ResetAllEllipses();
                TunerCanvas.Invalidate();
                if (_resetTimer == null) return;
                _resetTimer.Stop();
                _resetTimer = null;
            }
            else
            {
                _tuner.Start();
                TunerToggleButton.Content = "■  Stop";
                TunerCanvas.Invalidate();
                _resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _resetTimer.Tick += ResetTimer_Tick!;
                _resetTimer.Start();
            }
        }

        private void Tuning_SelectionChanged(object sender, SelectionChangedEventArgs? e)
        {
            var comboBox = (ComboBox)sender;
            var selectedItem = comboBox.SelectedItem;
            if (selectedItem == null) return;

            for (var row = 0; row <= TotalRows; row++)
            {
                var col = 0;
                Ellipse? foundEllipse = null;
                foreach (var child in FretboardGrid.Children)
                {
                    if (child is not Ellipse ellipse) continue;
                    if (ellipse.Tag is not (int r, int c)) continue;
                    if (r != row || c != col) continue;
                    foundEllipse = ellipse;
                    break;
                }
                if (foundEllipse == null) continue;
                var indexOffset = row + 1;
                var note = ((Tuning)selectedItem).Notes[^indexOffset];
                UpdateFretboardNotes(row, note, TotalCols);
            }
        }

        private void UpdateTextAtCell(int row, int col, string newText)
        {
            foreach (var child in FretboardGrid.Children)
            {
                if (child is not TextBlock existingTextBlock
                    || Grid.GetRow(existingTextBlock) != row
                    || Grid.GetColumn(existingTextBlock) != col) continue;
                existingTextBlock.Text = newText;
                return;
            }
            var textBlock = new TextBlock
            {
                Text = newText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -10),
                Foreground = new SolidColorBrush(Color.FromArgb(200, 200, 185, 165))
            };
            Grid.SetRow(textBlock, row);
            Grid.SetColumn(textBlock, col);
            FretboardGrid.Children.Add(textBlock);
        }

        private void UpdateFretboardNotes(int row, string startNote, int totalCols)
        {
            var startIndex = Array.IndexOf(_tuner.AllNotes, startNote);
            if (startIndex == -1) startIndex = 0;

            for (var col = 0; col < totalCols; col++)
            {
                var noteIndex = (startIndex + col) % _tuner.AllNotes.Length;
                UpdateTextAtCell(row, col, _tuner.AllNotes[noteIndex]);
            }
        }

        private Ellipse? GetEllipseAt(int row, int col)
        {
            foreach (var child in FretboardGrid.Children)
            {
                if (child is not Ellipse { Tag: (int r, int c) } ellipse) continue;
                if (r == row && c == col) return ellipse;
            }
            return null;
        }

        private void ResetTimer_Tick(object sender, object e)
        {
            if ((DateTime.Now - _lastMatchTime).TotalSeconds > 3)
                ResetAllEllipses();
        }

        private void ResetAllEllipses()
        {
            foreach (var child in FretboardGrid.Children)
            {
                if (child is Ellipse { Tag: (int, int c) } ellipse && c == 0)
                    ellipse.Fill = new SolidColorBrush(Color.FromArgb(255, 28, 20, 12));
            }
        }
    }
}
