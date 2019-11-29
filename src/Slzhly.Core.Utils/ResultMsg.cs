using System;
using System.Collections.Generic;
using System.Text;

namespace Slzhly.Core.Utils
{
    /// <summary>
    /// 
    /// </summary>
    public class ResultMsg
    {
        /// <summary>
        /// 编号
        /// </summary>
        public int Code { get; set; }
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }
        /// <summary>
        /// 返回值
        /// </summary>
        public Object Result { get; set; }
        /// <summary>
        /// 消息
        /// </summary>
        public string Msg { get; set; }
    }
}
