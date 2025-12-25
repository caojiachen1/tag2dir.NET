using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using tag2dir.NET.Models;

namespace tag2dir.NET.Services
{
    /// <summary>
    /// ExifTool 服务，用于解析图片元数据
    /// </summary>
    public class ExifToolService
    {
        private static bool? _hasExifTool;
        private static readonly object _lock = new();

        /// <summary>
        /// 支持的图片扩展名
        /// </summary>
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".heic", ".heif", ".raw", ".cr2", ".nef", ".arw", ".dng"
        };

        /// <summary>
        /// 检查 ExifTool 是否可用
        /// </summary>
        public static bool HasExifTool()
        {
            if (_hasExifTool.HasValue)
                return _hasExifTool.Value;

            lock (_lock)
            {
                if (_hasExifTool.HasValue)
                    return _hasExifTool.Value;

                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "exiftool",
                        Arguments = "-ver",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        process.WaitForExit(5000);
                        _hasExifTool = process.ExitCode == 0;
                    }
                    else
                    {
                        _hasExifTool = false;
                    }
                }
                catch
                {
                    _hasExifTool = false;
                }

                return _hasExifTool.Value;
            }
        }

        /// <summary>
        /// 检查文件是否为支持的图片格式
        /// </summary>
        public static bool IsAllowedImage(string path)
        {
            var ext = Path.GetExtension(path);
            return AllowedExtensions.Contains(ext);
        }

        /// <summary>
        /// 扫描目录中的所有图片
        /// </summary>
        public static IEnumerable<string> ScanImages(string directory, SearchOption searchOption = SearchOption.AllDirectories)
        {
            if (!Directory.Exists(directory))
                yield break;

            foreach (var file in Directory.EnumerateFiles(directory, "*.*", searchOption))
            {
                if (IsAllowedImage(file))
                    yield return file;
            }
        }

        /// <summary>
        /// 使用 ExifTool 提取图片中的人物和标签信息
        /// </summary>
        public static async Task<(List<string> People, List<string> Tags)> ExtractPeopleAndTagsAsync(
            string path, CancellationToken cancellationToken = default)
        {
            var people = new List<string>();
            var tags = new List<string>();

            if (!HasExifTool() || !File.Exists(path))
                return (people, tags);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "exiftool",
                    Arguments = $"-json -all -charset utf8 -coordFormat %.6f -dateFormat \"%Y:%m:%d %H:%M:%S\" -ignoreMinorErrors \"{path}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return (people, tags);

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    return (people, tags);

                // 解析 JSON 输出
                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return (people, tags);

                var metadata = doc.RootElement[0];

                // 提取标签字段
                string[] tagFields = {
                    "Keywords", "XPKeywords", "Subject", "HierarchicalKeywords",
                    "TagsList", "CatalogSets", "SupplementalCategories",
                    "XPSubject"
                };

                foreach (var field in tagFields)
                {
                    if (metadata.TryGetProperty(field, out var value))
                    {
                        ExtractStrings(value, tags);
                    }
                }

                // 提取人物字段
                string[] peopleFields = {
                    "RegionName", "PersonInImage", "PersonDisplayName",
                    "FaceName", "PeopleKeywords"
                };

                foreach (var field in peopleFields)
                {
                    if (metadata.TryGetProperty(field, out var value))
                    {
                        ExtractStrings(value, people);
                    }
                }

                // 特殊处理 RegionInfo
                if (metadata.TryGetProperty("RegionInfo", out var regionInfo))
                {
                    if (regionInfo.ValueKind == JsonValueKind.String)
                    {
                        var regionStr = regionInfo.GetString();
                        if (!string.IsNullOrEmpty(regionStr))
                        {
                            var namePattern = new Regex(@"""Name""\s*:\s*""([^""]+)""");
                            var matches = namePattern.Matches(regionStr);
                            foreach (Match match in matches)
                            {
                                if (match.Groups.Count > 1)
                                {
                                    var name = match.Groups[1].Value.Trim();
                                    if (!string.IsNullOrEmpty(name) && !people.Contains(name))
                                        people.Add(name);
                                }
                            }
                        }
                    }
                }

                // 去重
                people = new List<string>(new HashSet<string>(people));
                tags = new List<string>(new HashSet<string>(tags));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 忽略解析错误
            }

            return (people, tags);
        }

        /// <summary>
        /// 从 JSON 元素中提取字符串列表
        /// </summary>
        private static void ExtractStrings(JsonElement element, List<string> list)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    // 尝试分割
                    if (value.Contains(';'))
                    {
                        foreach (var part in value.Split(';'))
                        {
                            var trimmed = part.Trim();
                            if (!string.IsNullOrEmpty(trimmed) && !list.Contains(trimmed))
                                list.Add(trimmed);
                        }
                    }
                    else if (value.Contains(','))
                    {
                        foreach (var part in value.Split(','))
                        {
                            var trimmed = part.Trim();
                            if (!string.IsNullOrEmpty(trimmed) && !list.Contains(trimmed))
                                list.Add(trimmed);
                        }
                    }
                    else
                    {
                        if (!list.Contains(value))
                            list.Add(value);
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(value) && !list.Contains(value))
                            list.Add(value);
                    }
                }
            }
        }
    }
}
