using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using tag2dir.NET.Models;

namespace tag2dir.NET.Services
{
    /// <summary>
    /// 文件移动服务
    /// </summary>
    public class FileMoveService
    {
        private readonly List<MoveBatch> _moveHistory = new();
        private const int MaxHistorySize = 20;

        /// <summary>
        /// 获取移动历史记录
        /// </summary>
        public IReadOnlyList<MoveBatch> MoveHistory => _moveHistory;

        /// <summary>
        /// 是否有可撤销的记录
        /// </summary>
        public bool CanUndo => _moveHistory.Count > 0;

        /// <summary>
        /// 生成移动预览
        /// </summary>
        public List<MoveRecord> GeneratePreview(IEnumerable<ImageInfo> images, string destRoot)
        {
            var records = new List<MoveRecord>();

            foreach (var image in images)
            {
                if (!image.IsSelected || string.IsNullOrEmpty(image.SelectedPerson))
                    continue;

                var safePerson = SanitizeName(image.SelectedPerson);
                var targetDir = Path.Combine(destRoot, safePerson);
                var targetPath = Path.Combine(targetDir, Path.GetFileName(image.Path));
                targetPath = GetUniquePath(targetPath);

                records.Add(new MoveRecord
                {
                    FromPath = image.Path,
                    ToPath = targetPath,
                    PersonName = image.SelectedPerson
                });
            }

            return records;
        }

        /// <summary>
        /// 执行移动操作
        /// </summary>
        public async Task<(List<MoveRecord> Moved, List<(string Path, string Error)> Errors)> MoveFilesAsync(
            IEnumerable<ImageInfo> images, string destRoot)
        {
            var moved = new List<MoveRecord>();
            var errors = new List<(string Path, string Error)>();

            if (!Directory.Exists(destRoot))
            {
                try
                {
                    Directory.CreateDirectory(destRoot);
                }
                catch (Exception ex)
                {
                    errors.Add((destRoot, $"无法创建目标目录: {ex.Message}"));
                    return (moved, errors);
                }
            }

            foreach (var image in images)
            {
                if (!image.IsSelected || string.IsNullOrEmpty(image.SelectedPerson))
                    continue;

                if (!File.Exists(image.Path))
                {
                    errors.Add((image.Path, "源文件不存在"));
                    continue;
                }

                var safePerson = SanitizeName(image.SelectedPerson);
                var targetDir = Path.Combine(destRoot, safePerson);
                var targetPath = Path.Combine(targetDir, Path.GetFileName(image.Path));
                targetPath = GetUniquePath(targetPath);

                try
                {
                    Directory.CreateDirectory(targetDir);

                    // 使用复制+删除来处理跨磁盘移动
                    await Task.Run(() =>
                    {
                        if (Path.GetFullPath(image.Path) != Path.GetFullPath(targetPath))
                        {
                            File.Copy(image.Path, targetPath, false);
                            try
                            {
                                File.Delete(image.Path);
                            }
                            catch
                            {
                                // 如果删除失败，回滚目标文件
                                try { File.Delete(targetPath); } catch { }
                                throw;
                            }
                        }
                    });

                    moved.Add(new MoveRecord
                    {
                        FromPath = image.Path,
                        ToPath = targetPath,
                        PersonName = image.SelectedPerson
                    });
                }
                catch (Exception ex)
                {
                    errors.Add((image.Path, ex.Message));
                }
            }

            // 记录到历史
            if (moved.Count > 0)
            {
                _moveHistory.Add(new MoveBatch
                {
                    Records = moved,
                    MoveTime = DateTime.Now,
                    DestinationRoot = destRoot
                });

                // 限制历史记录大小
                while (_moveHistory.Count > MaxHistorySize)
                {
                    _moveHistory.RemoveAt(0);
                }
            }

            return (moved, errors);
        }

        /// <summary>
        /// 撤销最后一批移动操作
        /// </summary>
        public async Task<(List<MoveRecord> Undone, List<(string Path, string Error)> Errors)> UndoLastMoveAsync()
        {
            var undone = new List<MoveRecord>();
            var errors = new List<(string Path, string Error)>();

            if (_moveHistory.Count == 0)
                return (undone, errors);

            var batch = _moveHistory[^1];
            _moveHistory.RemoveAt(_moveHistory.Count - 1);

            // 反向撤销
            for (int i = batch.Records.Count - 1; i >= 0; i--)
            {
                var record = batch.Records[i];

                if (!File.Exists(record.ToPath))
                {
                    errors.Add((record.ToPath, "目标文件不存在，无法撤销"));
                    continue;
                }

                var restorePath = record.FromPath;
                if (File.Exists(restorePath))
                {
                    restorePath = GetUniquePath(restorePath);
                }

                try
                {
                    var restoreDir = Path.GetDirectoryName(restorePath);
                    if (!string.IsNullOrEmpty(restoreDir))
                        Directory.CreateDirectory(restoreDir);

                    await Task.Run(() =>
                    {
                        File.Copy(record.ToPath, restorePath, false);
                        try
                        {
                            File.Delete(record.ToPath);
                        }
                        catch
                        {
                            try { File.Delete(restorePath); } catch { }
                            throw;
                        }
                    });

                    undone.Add(new MoveRecord
                    {
                        FromPath = record.ToPath,
                        ToPath = restorePath,
                        PersonName = record.PersonName
                    });
                }
                catch (Exception ex)
                {
                    errors.Add((record.ToPath, ex.Message));
                }
            }

            return (undone, errors);
        }

        /// <summary>
        /// 清理文件名中的非法字符
        /// </summary>
        private static string SanitizeName(string name)
        {
            // 允许中文、英文、数字、空格、下划线、连字符和点
            var pattern = new Regex(@"[^\w\-\u4e00-\u9fa5\. ]+", RegexOptions.None);
            var result = pattern.Replace(name, "").Trim();
            return string.IsNullOrEmpty(result) ? "Unknown" : result;
        }

        /// <summary>
        /// 获取唯一的文件路径（如果文件已存在，添加序号）
        /// </summary>
        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path))
                return path;

            var dir = Path.GetDirectoryName(path) ?? "";
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);

            int i = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                i++;
            } while (File.Exists(candidate));

            return candidate;
        }
    }
}
