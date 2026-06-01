using System;
using System.Threading;

namespace adrapi.Directory
{
    /// <summary>
    /// Ambient per-request audit context (requester, correlation id, client IP)
    /// that flows into the directory backends so their operation logs carry the
    /// same identity/correlation fields the controllers use for LDAP.
    ///
    /// The HTTP layer opens a scope (<see cref="BeginScope"/>) for the duration of
    /// a request; backends read <see cref="Current"/>. Outside any scope it is
    /// <see cref="Unknown"/>, so logging never NREs.
    /// </summary>
    public sealed class DirectoryOperationContext
    {
        public string Requester { get; init; } = "unknown";
        public string CorrelationId { get; init; }
        public string ClientIp { get; init; } = "unknown";

        /// <summary>The context used when none has been established.</summary>
        public static readonly DirectoryOperationContext Unknown = new();

        private static readonly AsyncLocal<DirectoryOperationContext> CurrentContext = new();

        public static DirectoryOperationContext Current => CurrentContext.Value ?? Unknown;

        /// <summary>Establishes <paramref name="context"/> until the returned scope is disposed.</summary>
        public static IDisposable BeginScope(DirectoryOperationContext context)
        {
            var previous = CurrentContext.Value;
            CurrentContext.Value = context ?? Unknown;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly DirectoryOperationContext previous;
            private bool disposed;

            public Scope(DirectoryOperationContext previous) => this.previous = previous;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                CurrentContext.Value = previous;
            }
        }
    }
}
