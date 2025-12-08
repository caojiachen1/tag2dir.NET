using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using tag2dir.NET.Services;

namespace tag2dir.NET.Converters
{
    /// <summary>
    /// 将图片路径转换为缩略图的转换器
    /// </summary>
    public class PathToThumbnailConverter : IValueConverter
    {
        public static PathToThumbnailConverter Instance { get; } = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                int size = 64;
                if (parameter is string sizeStr && int.TryParse(sizeStr, out var parsedSize))
                {
                    size = parsedSize;
                }
                else if (parameter is int intSize)
                {
                    size = intSize;
                }

                // 首先尝试获取缓存的缩略图
                var cached = ThumbnailService.GetThumbnailCached(path, size);
                if (cached != null)
                    return cached;

                // 异步加载缩略图
                ThumbnailService.PreloadThumbnail(path, size);
                return null;
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
