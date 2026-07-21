using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Arch
{
    public static class ArchExtensions
    {
        // ── ICanGetStore ──

        public static T GetStore<T>(this ICanGetStore self) where T : StoreBase
            => ArchContext.Current?.GetStore<T>();

        // ── ICanEvent ──

        public static void Subscribe<T>(this ICanEvent self, Action<T> handler)
            => ArchContext.Current?.Event?.Subscribe(handler, self);

        public static void Unsubscribe<T>(this ICanEvent self, Action<T> handler)
            => ArchContext.Current?.Event?.Unsubscribe(handler);

        public static void UnsubscribeAll(this ICanEvent self)
            => ArchContext.Current?.Event?.UnsubscribeAll(self);

        public static void Publish<T>(this ICanEvent self, T eventData)
            => ArchContext.Current?.Event?.Publish(eventData);

        // ── ICanGetSystem ──

        public static T GetSystem<T>(this ICanGetSystem self) where T : class
            => ArchContext.Current?.GetSystem<T>();

        // ── ICanGetView ──

        public static T GetView<T>(this ICanGetView self) where T : GameView
            => ArchContext.Current?.GetView<T>();

        // ── ICanRunProcedure ──

        public static UniTask RunProcedure(this ICanRunProcedure self, ProcedureBase procedure, CancellationToken ct = default)
            => ArchContext.Current?.RunProcedure(procedure, ct) ?? UniTask.CompletedTask;
    }
}
