using Avalonia.Controls;
using Avalonia.Interactivity;
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

        private void ScrollToBottom_Click(object? sender, RoutedEventArgs e)
        {
            // 尝试滚动主图片列表和预览列表到底部（如果存在）
            var imagesSv = this.FindControl<ScrollViewer>("ImagesScrollViewer");
            imagesSv?.ScrollToEnd();

            var previewSv = this.FindControl<ScrollViewer>("PreviewScrollViewer");
            previewSv?.ScrollToEnd();
        }

        private void ScrollToTop_Click(object? sender, RoutedEventArgs e)
        {
            // 尝试滚动主图片列表和预览列表到顶部（如果存在）
            var imagesSv = this.FindControl<ScrollViewer>("ImagesScrollViewer");
            // ScrollToHome 在某些版本中可用，尝试调用，否则使用 ScrollToOffset(0)
            try
            {
                imagesSv?.ScrollToHome();
            }
            catch
            {
                // 忽略无法滚动到顶部的情况（不同 Avalonia 版本可能不支持某些方法）
            }

            var previewSv = this.FindControl<ScrollViewer>("PreviewScrollViewer");
            try
            {
                previewSv?.ScrollToHome();
            }
            catch
            {
                // 忽略无法滚动到顶部的情况
            }
        }
    }
}