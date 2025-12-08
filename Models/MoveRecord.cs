using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace tag2dir.NET.Models
{
    /// <summary>
    /// 记录文件移动操作，用于撤销
    /// </summary>
    public partial class MoveRecord : ObservableObject
    {
        /// <summary>
        /// 原始路径
        /// </summary>
        public string FromPath { get; set; } = string.Empty;

        /// <summary>
        /// 目标路径
        /// </summary>
        public string ToPath { get; set; } = string.Empty;

        /// <summary>
        /// 人物名称
        /// </summary>
        public string PersonName { get; set; } = string.Empty;

        /// <summary>
        /// 缩略图
        /// </summary>
        [ObservableProperty]
        private Bitmap? _thumbnail;

        /// <summary>
        /// 文件名
        /// </summary>
        public string FileName => System.IO.Path.GetFileName(FromPath);
    }
}
