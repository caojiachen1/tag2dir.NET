using System.Collections.Generic;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace tag2dir.NET.Models
{
    /// <summary>
    /// 表示扫描到的图片信息
    /// </summary>
    public partial class ImageInfo : ObservableObject
    {
        /// <summary>
        /// 图片完整路径
        /// </summary>
        [ObservableProperty]
        private string _path = string.Empty;

        /// <summary>
        /// 文件名
        /// </summary>
        [ObservableProperty]
        private string _fileName = string.Empty;

        /// <summary>
        /// 检测到的人物列表
        /// </summary>
        [ObservableProperty]
        private List<string> _people = new();

        /// <summary>
        /// 检测到的标签列表
        /// </summary>
        [ObservableProperty]
        private List<string> _tags = new();

        /// <summary>
        /// 是否选中用于移动
        /// </summary>
        [ObservableProperty]
        private bool _isSelected = true;

        /// <summary>
        /// 选中的目标人物（用于移动时的目标文件夹）
        /// </summary>
        [ObservableProperty]
        private string? _selectedPerson;

        /// <summary>
        /// 缩略图
        /// </summary>
        [ObservableProperty]
        private Bitmap? _thumbnail;

        /// <summary>
        /// 人物名称的显示字符串
        /// </summary>
        public string PeopleDisplay => People.Count > 0 ? string.Join(", ", People) : "(无人物)";

        /// <summary>
        /// 标签的显示字符串
        /// </summary>
        public string TagsDisplay => Tags.Count > 0 ? string.Join(", ", Tags) : "(无标签)";
    }
}
