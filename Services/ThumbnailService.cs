using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace tag2dir.NET.Services
{
    /// <summary>
    /// 缩略图服务，用于生成和缓存图片缩略图
    /// </summary>
    public class ThumbnailService
    {
        private static readonly ConcurrentDictionary<string, Bitmap?> _thumbnailCache = new();
        private static readonly string _cacheDir;
        private const int DefaultThumbnailSize = 64;

        static ThumbnailService()
        {
            // 使用临时目录存储缓存的缩略图
            _cacheDir = Path.Combine(Path.GetTempPath(), "tag2dir.NET", "thumbnails");
            try
            {
                Directory.CreateDirectory(_cacheDir);
            }
            catch
            {
                // 忽略创建目录失败
            }
        }

        /// <summary>
        /// 获取图片缩略图
        /// </summary>
        public static async Task<Bitmap?> GetThumbnailAsync(string imagePath, int size = DefaultThumbnailSize)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return null;

            var cacheKey = GetCacheKey(imagePath, size);

            // 检查内存缓存
            if (_thumbnailCache.TryGetValue(cacheKey, out var cached))
                return cached;

            // 检查磁盘缓存
            var diskCachePath = GetDiskCachePath(imagePath, size);
            if (File.Exists(diskCachePath))
            {
                try
                {
                    var bitmap = new Bitmap(diskCachePath);
                    _thumbnailCache.TryAdd(cacheKey, bitmap);
                    return bitmap;
                }
                catch
                {
                    // 缓存文件损坏，删除后重新生成
                    try { File.Delete(diskCachePath); } catch { }
                }
            }

            // 生成新的缩略图
            try
            {
                var bitmap = await Task.Run(() => GenerateThumbnail(imagePath, size, diskCachePath));
                _thumbnailCache.TryAdd(cacheKey, bitmap);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 同步获取缩略图（如果已缓存）
        /// </summary>
        public static Bitmap? GetThumbnailCached(string imagePath, int size = DefaultThumbnailSize)
        {
            if (string.IsNullOrEmpty(imagePath))
                return null;

            var cacheKey = GetCacheKey(imagePath, size);
            _thumbnailCache.TryGetValue(cacheKey, out var cached);
            return cached;
        }

        /// <summary>
        /// 预加载缩略图
        /// </summary>
        public static void PreloadThumbnail(string imagePath, int size = DefaultThumbnailSize)
        {
            _ = GetThumbnailAsync(imagePath, size);
        }

        /// <summary>
        /// 清除缩略图缓存
        /// </summary>
        public static void ClearCache()
        {
            foreach (var bitmap in _thumbnailCache.Values)
            {
                bitmap?.Dispose();
            }
            _thumbnailCache.Clear();
        }

        private static Bitmap? GenerateThumbnail(string imagePath, int size, string? savePath)
        {
            try
            {
                using var stream = File.OpenRead(imagePath);
                var originalBitmap = new Bitmap(stream);

                // 计算缩放比例
                double scale = Math.Min((double)size / originalBitmap.PixelSize.Width,
                                        (double)size / originalBitmap.PixelSize.Height);

                int newWidth = Math.Max(1, (int)(originalBitmap.PixelSize.Width * scale));
                int newHeight = Math.Max(1, (int)(originalBitmap.PixelSize.Height * scale));

                // 创建缩略图 - 使用高质量插值
                var thumbnail = originalBitmap.CreateScaledBitmap(new PixelSize(newWidth, newHeight), BitmapInterpolationMode.HighQuality);

                // 保存到磁盘缓存
                if (!string.IsNullOrEmpty(savePath))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(savePath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);
                        thumbnail.Save(savePath);
                    }
                    catch
                    {
                        // 保存失败不影响返回
                    }
                }

                originalBitmap.Dispose();
                return thumbnail;
            }
            catch
            {
                return null;
            }
        }

        private static string GetCacheKey(string imagePath, int size)
        {
            return $"{imagePath}|{size}|{GetFileModifiedTime(imagePath)}";
        }

        private static string GetDiskCachePath(string imagePath, int size)
        {
            // 使用路径的哈希作为缓存文件名
            using var md5 = MD5.Create();
            var pathBytes = Encoding.UTF8.GetBytes($"{imagePath}|{size}");
            var hashBytes = md5.ComputeHash(pathBytes);
            var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return Path.Combine(_cacheDir, $"{hashString}.jpg");
        }

        private static long GetFileModifiedTime(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch
            {
                return 0;
            }
        }
    }
}
