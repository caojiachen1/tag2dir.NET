using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using tag2dir.NET.Models;
using tag2dir.NET.Services;

namespace tag2dir.NET.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly FileMoveService _fileMoveService = new();
        private CancellationTokenSource? _scanCts;

        /// <summary>
        /// 源文件夹路径
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
        private string _sourceFolder = string.Empty;

        /// <summary>
        /// 目标文件夹路径
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(MoveFilesCommand))]
        [NotifyCanExecuteChangedFor(nameof(PreviewMoveCommand))]
        private string _destinationFolder = string.Empty;

        /// <summary>
        /// 扫描到的图片列表
        /// </summary>
        public ObservableCollection<ImageInfo> Images { get; } = new();

        /// <summary>
        /// 移动预览列表
        /// </summary>
        public ObservableCollection<MoveRecord> MovePreview { get; } = new();

        /// <summary>
        /// 按人物分组的移动预览
        /// </summary>
        public ObservableCollection<PersonMoveGroup> GroupedMovePreview { get; } = new();

        /// <summary>
        /// 是否正在扫描
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
        private bool _isScanning;

        /// <summary>
        /// 是否正在移动文件
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(MoveFilesCommand))]
        [NotifyCanExecuteChangedFor(nameof(UndoMoveCommand))]
        private bool _isMoving;

        /// <summary>
        /// 扫描进度文本
        /// </summary>
        [ObservableProperty]
        private string _scanProgressText = string.Empty;

        /// <summary>
        /// 状态消息
        /// </summary>
        [ObservableProperty]
        private string _statusMessage = string.Empty;

        /// <summary>
        /// ExifTool 是否可用
        /// </summary>
        [ObservableProperty]
        private bool _hasExifTool;

        /// <summary>
        /// 是否显示预览面板
        /// </summary>
        [ObservableProperty]
        private bool _showPreview;

        /// <summary>
        /// 所有检测到的人物列表（用于筛选）
        /// </summary>
        public ObservableCollection<string> AllPeople { get; } = new();

        /// <summary>
        /// 人物数量（用于 UI 绑定，确保在集合变化时触发 PropertyChanged）
        /// 使用 ObservableProperty 自动生成通知支持。
        /// </summary>
        [ObservableProperty]
        private int _peopleCount;

        /// <summary>
        /// 用于获取存储提供者的回调
        /// </summary>
        public Func<IStorageProvider>? GetStorageProvider { get; set; }

        public MainWindowViewModel()
        {
            HasExifTool = ExifToolService.HasExifTool();
            if (!HasExifTool)
            {
                StatusMessage = "⚠️ 未检测到 ExifTool，请安装 https://exiftool.org/ 并将其加入系统 PATH";
            }

            // 当人物集合变化时，更新 PeopleCount 以触发属性通知，确保 UI 刷新
            AllPeople.CollectionChanged += (_, _) => PeopleCount = AllPeople.Count;
            // 初始化
            PeopleCount = AllPeople.Count;

            // 当图片集合变化时，通知移动和预览命令的 CanExecute 状态改变
            Images.CollectionChanged += (_, _) =>
            {
                MoveFilesCommand.NotifyCanExecuteChanged();
                PreviewMoveCommand.NotifyCanExecuteChanged();
            };
        }

        /// <summary>
        /// 浏览源文件夹
        /// </summary>
        [RelayCommand]
        private async Task BrowseSourceFolderAsync()
        {
            var folder = await PickFolderAsync("选择源文件夹");
            if (!string.IsNullOrEmpty(folder))
            {
                SourceFolder = folder;
            }
        }

        /// <summary>
        /// 浏览目标文件夹
        /// </summary>
        [RelayCommand]
        private async Task BrowseDestinationFolderAsync()
        {
            var folder = await PickFolderAsync("选择目标文件夹");
            if (!string.IsNullOrEmpty(folder))
            {
                DestinationFolder = folder;
            }
        }

        /// <summary>
        /// 扫描图片
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanScan))]
        private async Task ScanAsync()
        {
            if (!HasExifTool)
            {
                StatusMessage = "⚠️ ExifTool 不可用，无法扫描";
                return;
            }

            if (!Directory.Exists(SourceFolder))
            {
                StatusMessage = "⚠️ 源文件夹不存在";
                return;
            }

            IsScanning = true;
            Images.Clear();
            AllPeople.Clear();
            MovePreview.Clear();
            ShowPreview = false;
            _scanCts = new CancellationTokenSource();

            try
            {
                // 使用字典作为 "桶" 来统计人物（键为人物名，值为出现次数）
                var peopleBuckets = new Dictionary<string, int>();
                int count = 0;

                await Task.Run(async () =>
                {
                    foreach (var path in ExifToolService.ScanImages(SourceFolder))
                    {
                        if (_scanCts.Token.IsCancellationRequested)
                            break;

                        count++;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ScanProgressText = $"正在扫描: {count} - {Path.GetFileName(path)}";
                        });

                        var (people, tags) = await ExifToolService.ExtractPeopleAndTagsAsync(path, _scanCts.Token);

                        var imageInfo = new ImageInfo
                        {
                            Path = path,
                            FileName = Path.GetFileName(path),
                            People = people,
                            Tags = tags,
                            IsSelected = people.Count > 0,
                            SelectedPerson = people.FirstOrDefault()
                        };

                        // 异步加载缩略图
                        _ = LoadThumbnailAsync(imageInfo);

                        // 更新桶：如果遇到新的人物，添加到桶并立即将其加入到 UI 绑定的 AllPeople 集合
                        foreach (var person in people)
                        {
                            if (string.IsNullOrWhiteSpace(person))
                                continue;

                            if (peopleBuckets.ContainsKey(person))
                            {
                                peopleBuckets[person]++;
                            }
                            else
                            {
                                peopleBuckets[person] = 1;
                                // 将新人物添加到 AllPeople（必须在 UI 线程）以便实时更新界面
                                await Dispatcher.UIThread.InvokeAsync(() => AllPeople.Add(person));
                            }
                        }

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            Images.Add(imageInfo);
                        });
                    }
                }, _scanCts.Token);

                StatusMessage = $"✅ 扫描完成，共找到 {Images.Count} 张图片，{PeopleCount} 个人物";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "⚠️ 扫描已取消";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ 扫描出错: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                ScanProgressText = string.Empty;
                _scanCts?.Dispose();
                _scanCts = null;
            }
        }

        private bool CanScan() => !IsScanning && !string.IsNullOrEmpty(SourceFolder);

        /// <summary>
        /// 取消扫描
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanCancelScan))]
        private void CancelScan()
        {
            _scanCts?.Cancel();
        }

        private bool CanCancelScan() => IsScanning;

        /// <summary>
        /// 预览移动（点击切换显示/隐藏）
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanPreviewMove))]
        private void PreviewMove()
        {
            // 如果已经显示预览，则关闭
            if (ShowPreview)
            {
                ShowPreview = false;
                StatusMessage = "预览已关闭";
                return;
            }

            if (string.IsNullOrEmpty(DestinationFolder))
            {
                StatusMessage = "⚠️ 请先选择目标文件夹";
                return;
            }

            MovePreview.Clear();
            GroupedMovePreview.Clear();
            
            var preview = _fileMoveService.GeneratePreview(Images, DestinationFolder);
            var groupedRecords = new Dictionary<string, PersonMoveGroup>();
            
            foreach (var record in preview)
            {
                // 从对应的 ImageInfo 复制缩略图或异步加载
                var imageInfo = Images.FirstOrDefault(i => i.Path == record.FromPath);
                if (imageInfo?.Thumbnail != null)
                {
                    record.Thumbnail = imageInfo.Thumbnail;
                }
                else
                {
                    _ = LoadMoveRecordThumbnailAsync(record);
                }
                MovePreview.Add(record);
                
                // 按人物分组
                var personName = record.PersonName ?? "未知";
                if (!groupedRecords.TryGetValue(personName, out var group))
                {
                    group = new PersonMoveGroup
                    {
                        PersonName = personName,
                        TargetFolder = Path.Combine(DestinationFolder, personName)
                    };
                    groupedRecords[personName] = group;
                }
                group.Records.Add(record);
            }
            
            // 按人物名称排序添加到集合
            foreach (var group in groupedRecords.Values.OrderBy(g => g.PersonName))
            {
                GroupedMovePreview.Add(group);
            }

            ShowPreview = true;
            StatusMessage = $"📋 预览: 将移动 {MovePreview.Count} 个文件到 {GroupedMovePreview.Count} 个人物文件夹";
        }

        /// <summary>
        /// 关闭预览面板
        /// </summary>
        [RelayCommand]
        private void ClosePreview()
        {
            ShowPreview = false;
            StatusMessage = "预览已关闭";
        }

        private bool CanPreviewMove() => Images.Count > 0 && !string.IsNullOrEmpty(DestinationFolder);

        /// <summary>
        /// 执行移动
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanMoveFiles))]
        private async Task MoveFilesAsync()
        {
            if (string.IsNullOrEmpty(DestinationFolder))
            {
                StatusMessage = "⚠️ 请先选择目标文件夹";
                return;
            }

            IsMoving = true;
            ShowPreview = false;

            try
            {
                var (moved, errors) = await _fileMoveService.MoveFilesAsync(Images, DestinationFolder);

                // 从列表中移除已移动的图片
                var movedPaths = moved.Select(m => m.FromPath).ToHashSet();
                var toRemove = Images.Where(i => movedPaths.Contains(i.Path)).ToList();
                foreach (var item in toRemove)
                {
                    Images.Remove(item);
                }

                MovePreview.Clear();

                if (errors.Count > 0)
                {
                    StatusMessage = $"⚠️ 移动完成: {moved.Count} 成功, {errors.Count} 失败";
                }
                else
                {
                    StatusMessage = $"✅ 移动完成: {moved.Count} 个文件已移动";
                }

                UndoMoveCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ 移动出错: {ex.Message}";
            }
            finally
            {
                IsMoving = false;
            }
        }

        private bool CanMoveFiles() => Images.Count > 0 && !IsMoving && !string.IsNullOrEmpty(DestinationFolder);

        /// <summary>
        /// 撤销移动
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanUndoMove))]
        private async Task UndoMoveAsync()
        {
            if (!_fileMoveService.CanUndo)
            {
                StatusMessage = "⚠️ 没有可撤销的操作";
                return;
            }

            IsMoving = true;

            try
            {
                var (undone, errors) = await _fileMoveService.UndoLastMoveAsync();

                if (errors.Count > 0)
                {
                    StatusMessage = $"⚠️ 撤销完成: {undone.Count} 成功, {errors.Count} 失败";
                }
                else
                {
                    StatusMessage = $"✅ 撤销完成: {undone.Count} 个文件已恢复";
                }

                UndoMoveCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ 撤销出错: {ex.Message}";
            }
            finally
            {
                IsMoving = false;
            }
        }

        private bool CanUndoMove() => _fileMoveService.CanUndo && !IsMoving;

        /// <summary>
        /// 全选/取消全选
        /// </summary>
        [RelayCommand]
        private void ToggleSelectAll()
        {
            var allSelected = Images.All(i => i.IsSelected);
            foreach (var image in Images)
            {
                image.IsSelected = !allSelected;
            }
        }

        /// <summary>
        /// 选择指定人物的所有图片
        /// </summary>
        [RelayCommand]
        private void SelectByPerson(string person)
        {
            foreach (var image in Images)
            {
                if (image.People.Contains(person))
                {
                    image.IsSelected = true;
                    image.SelectedPerson = person;
                }
            }
        }

        /// <summary>
        /// 打开文件夹选择对话框
        /// </summary>
        private async Task<string?> PickFolderAsync(string title)
        {
            if (GetStorageProvider == null)
                return null;

            var storageProvider = GetStorageProvider();
            var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

            return result.FirstOrDefault()?.Path.LocalPath;
        }

        /// <summary>
        /// 异步加载缩略图
        /// </summary>
        private async Task LoadThumbnailAsync(ImageInfo imageInfo)
        {
            try
            {
                // 使用 180 像素的缩略图以获得更好的清晰度
                var thumbnail = await ThumbnailService.GetThumbnailAsync(imageInfo.Path, 180);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    imageInfo.Thumbnail = thumbnail;
                });
            }
            catch
            {
                // 忽略缩略图加载失败
            }
        }

        /// <summary>
        /// 异步加载移动记录的缩略图
        /// </summary>
        private async Task LoadMoveRecordThumbnailAsync(MoveRecord record)
        {
            try
            {
                var thumbnail = await ThumbnailService.GetThumbnailAsync(record.FromPath, 120);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    record.Thumbnail = thumbnail;
                });
            }
            catch
            {
                // 忽略缩略图加载失败
            }
        }
    }
}

