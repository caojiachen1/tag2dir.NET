using Avalonia.Controls;
using tag2dir.NET.ViewModels;

namespace tag2dir.NET.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 设置存储提供者回调
            this.Loaded += (_, _) =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.GetStorageProvider = () => this.StorageProvider;
                }
            };
        }
    }
}