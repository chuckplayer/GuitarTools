using System;
using System.Diagnostics;
using Windows.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GuitarTools
{

    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly Tuner _tuner;
        public MainWindow()
        {
            InitializeComponent();
            _tuner = new Tuner();
            TunerGrid.DataContext = _tuner;
            TuningsComboBox.DataContext = _tuner;
            AppWindow.Resize(new SizeInt32(1024, 768));
            AddNotesToFretboard();
            Tuning_SelectionChanged(TuningsComboBox, null);
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

                    Microsoft.UI.Xaml.Controls.Grid.SetRow(ellipse, row);
                    Microsoft.UI.Xaml.Controls.Grid.SetColumn(ellipse, col);

                    FretboardGrid.Children.Add(ellipse);
                }
            }
        }
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _tuner.Start();
        }
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _tuner.Stop();
        }
        private void Tuning_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            var comboBox = (Microsoft.UI.Xaml.Controls.ComboBox)sender;
            var selectedItem = comboBox.SelectedItem;

            if (selectedItem == null) return;

            const int totalRows = 8;
            const int totalCols = 13;

            for (var row = 0; row <= totalRows ; row++)
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
                UpdateFretboardNotes(row, note, totalCols);
            }
        }
        private void UpdateTextAtCell(int row, int col, string newText)
        {
            foreach (var child in FretboardGrid.Children)
            {
                if (child is not Microsoft.UI.Xaml.Controls.TextBlock existingTextBlock
                    || Microsoft.UI.Xaml.Controls.Grid.GetRow(existingTextBlock) != row
                    || Microsoft.UI.Xaml.Controls.Grid.GetColumn(existingTextBlock) != col) continue;
                existingTextBlock.Text = newText;
                return;
            }
            var textBlock = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = newText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -10),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
            };

            Microsoft.UI.Xaml.Controls.Grid.SetRow(textBlock, row);
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(textBlock, col);
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
    }

}
