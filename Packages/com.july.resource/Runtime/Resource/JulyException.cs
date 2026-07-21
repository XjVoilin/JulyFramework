using System;

namespace July.Resource
{
    /// <summary>
    /// 资源加载或场景加载失败时抛出的领域异常。
    /// </summary>
    public class ResourceException : Exception
    {
        public ResourceException(string message) : base(message) { }

        public ResourceException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
