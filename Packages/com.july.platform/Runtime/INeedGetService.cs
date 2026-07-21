using System;

namespace July.Platform
{
    public interface INeedGetService
    {
        Func<Type, object> ServiceGetter { get; set; }
    }

    public static class NeedGetServiceExtensions
    {
        public static T GetService<T>(this INeedGetService self) where T : class
            => self.ServiceGetter?.Invoke(typeof(T)) as T;
    }
}

