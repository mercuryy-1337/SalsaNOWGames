using System.Windows;
using System.Windows.Controls;
using SalsaNOWGames.ViewModels;

namespace SalsaNOWGames
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // Auto-scroll download output
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.DownloadOutput))
                {
                    Dispatcher.Invoke(() =>
                    {
                        OutputScrollViewer?.ScrollToEnd();
                    });
                }
            };
        }
    }
}
