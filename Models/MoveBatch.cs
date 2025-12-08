using System;
using System.Collections.Generic;

namespace tag2dir.NET.Models
{
    /// <summary>
    /// 记录一批移动操作，用于批量撤销
    /// </summary>
    public class MoveBatch
    {
        /// <summary>
        /// 移动记录列表
        /// </summary>
        public List<MoveRecord> Records { get; set; } = new();

        /// <summary>
        /// 移动时间
        /// </summary>
        public DateTime MoveTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 目标根目录
        /// </summary>
        public string DestinationRoot { get; set; } = string.Empty;
    }
}
