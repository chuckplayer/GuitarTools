using System;
using System.Linq;
using Windows.Graphics;
using ABI.Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GuitarTools
{

    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
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
            TunerGrid.DataContext = _tuner;
            TuningsComboBox.DataContext = _tuner;
            AppWindow.Resize(new SizeInt32(1024, 768));
            AddNotesToFretboard();
            Tuning_SelectionChanged(TuningsComboBox, null);
            _tuner.PropertyChanged += CurrentNote_PropertyChanged!;
        }

        private void AddNotesToFretboard()
        {
            for (var row = 0; row < 6; row++)
            {
                for (var col = 0; col <= 12; col++)
                {
                    var ellipse = new Ellipse
                    {
                        Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                        Width = 45,
                        Height = 45,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, -22),
                        Tag = (row, col)
                    };
                    if (col > 0)
                        ellipse.Opacity = 0.75;

                    Grid.SetRow(ellipse, row);
                    Grid.SetColumn(ellipse, col);

                    FretboardGrid.Children.Add(ellipse);
                }
            }
        }
        private void CurrentNote_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_tuner.CurrentNote == null) return;
            if (TuningsComboBox.SelectedItem == null) return;
            if (e.PropertyName != "CurrentNote") return;
            var selectedTuning = (Tuning)TuningsComboBox.SelectedItem;
            var pitchToMatch = _tuner.CurrentNote.Note;
            if(pitchToMatch == null || !selectedTuning.Pitches.Contains(pitchToMatch)) return;
            var index = Array.IndexOf(selectedTuning.Pitches, pitchToMatch);
            if(index == -1) return;
            var rowOffset = 5 - index;
            var ellipse = GetEllipseAt(rowOffset, 0);
            if(ellipse == null) return;
            var difference = Math.Abs(_tuner.CurrentNote.ClosestPitch - _tuner.CurrentNote.MaxFrequency);
            var color = difference switch
            {
                <= 0.4 => new SolidColorBrush(Colors.Green),
                <= 1.0 => new SolidColorBrush(Colors.Yellow),
                _ => new SolidColorBrush(Colors.Red)
            };
            ellipse.Fill = color;
            _lastMatchTime = DateTime.Now;
        }
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _tuner.Start();
            _resetTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _resetTimer.Tick += ResetTimer_Tick!;
            _resetTimer.Start();
        }
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _tuner.Stop();
            ResetAllEllipses();
            if (_resetTimer == null) return;
            _resetTimer.Stop();
            _resetTimer = null;
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
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
            };

            Grid.SetRow(textBlock, row);
            Grid.SetColumn(textBlock, col);
            FretboardGrid.Children.Add(textBlock);
        }
        private void UpdateFretboardNotes(int row, string startNote, int totalCols)
        {
            var startIndex = Array.IndexOf(_tuner.AllNotes, startNote);
            if (startIndex == -1)
            {
                startIndex = 0;
            }

            for (var col = 0; col < totalCols; col++)
            {
                var noteIndex = (startIndex + col) % _tuner.AllNotes.Length;
                var noteForCell = _tuner.AllNotes[noteIndex];
                UpdateTextAtCell(row, col, noteForCell);
            }
        }
        private Ellipse? GetEllipseAt(int row, int col)
        {
            foreach (var child in FretboardGrid.Children)
            {
                if (child is not Ellipse { Tag: (int r, int c) } ellipse) continue;
                if (r == row && c == col)
                    return ellipse;
            }
            return null;
        }
        private void ResetTimer_Tick(object sender, object e)
        {
            // If more than a few seconds have passed since the last match, reset all ellipses
            if ((DateTime.Now - _lastMatchTime).TotalSeconds > 3)             
            {
                ResetAllEllipses();
            }
        }
        private void ResetAllEllipses()
        {
            foreach (var child in FretboardGrid.Children)
            {
                if (child is Ellipse { Tag: (int row, int col) } ellipse)
                {
                    ellipse.Fill = new SolidColorBrush(Colors.Black);
                }
            }
        }
    }

}
