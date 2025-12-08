using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace tag2dir.NET.Models
{
    /// <summary>
    /// 按人物分组的移动记录
    /// </summary>
    public partial class PersonMoveGroup : ObservableObject
    {
        /// <summary>
        /// 人物名称
        /// </summary>
        [ObservableProperty]
        private string _personName = string.Empty;

        /// <summary>
        /// 目标文件夹路径
        /// </summary>
        [ObservableProperty]
        private string _targetFolder = string.Empty;

        /// <summary>
        /// 该人物的所有移动记录
        /// </summary>
        public ObservableCollection<MoveRecord> Records { get; } = new();

        /// <summary>
        /// 文件数量
        /// </summary>
        public int FileCount => Records.Count;
    }
}
