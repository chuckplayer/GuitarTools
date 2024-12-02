using Windows.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GuitarTools
{
    
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private Tuner _tuner;
        public MainWindow()
        {
            this.InitializeComponent();
            _tuner = new Tuner();
            TunerGrid.DataContext = _tuner;
            this.AppWindow.Resize(new SizeInt32(800,600));
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _tuner.Start();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _tuner.Stop();
        }
    }
}
