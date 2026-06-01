namespace adrapi.Directory
{
    /// <summary>
    /// A single auditable Microsoft Graph operation: what was done, to which
    /// target, the outcome and Graph correlation ids, plus the requester/
    /// correlation/client-IP from <see cref="DirectoryOperationContext"/>.
    /// </summary>
    public record GraphOperationLog(
        string Method,
        string Path,
        int StatusCode,
        string Outcome,
        string GraphRequestId,
        string ClientRequestId,
        string Requester,
        string CorrelationId,
        string ClientIp);

    /// <summary>Sink for Graph operation audit records.</summary>
    public interface IDirectoryAuditSink
    {
        void Write(GraphOperationLog log);
    }

    /// <summary>
    /// Default sink — emits one structured NLog line per Graph operation under the
    /// <c>GraphAudit</c> logger, mirroring the controller LDAP audit format and
    /// surfacing the Graph <c>request-id</c> for cross-correlation with Microsoft
    /// support. Failures log at Warn, successes at Info.
    /// </summary>
    public sealed class NLogDirectoryAuditSink : IDirectoryAuditSink
    {
        public static readonly NLogDirectoryAuditSink Instance = new();

        private readonly NLog.Logger logger = NLog.LogManager.GetLogger("GraphAudit");

        public void Write(GraphOperationLog e)
        {
            var level = e.Outcome == "success" ? NLog.LogLevel.Info : NLog.LogLevel.Warn;
            logger.Log(
                level,
                "GRAPH op={method} path={path} status={status} outcome={outcome} graphRequestId={graphRequestId} graphClientRequestId={clientRequestId} requester={requester} correlationId={correlationId} clientIp={clientIp}",
                e.Method, e.Path, e.StatusCode, e.Outcome, e.GraphRequestId, e.ClientRequestId, e.Requester, e.CorrelationId, e.ClientIp);
        }
    }
}
