namespace adrapi.Entra
{
    /// <summary>
    /// Helpers for safely composing Microsoft Graph OData query fragments.
    /// Centralizes input hardening so user-supplied values cannot break out of an
    /// OData string literal (the Graph equivalent of LDAP filter injection).
    /// </summary>
    public static class GraphQuery
    {
        /// <summary>
        /// Escapes a value for use inside a single-quoted OData string literal by
        /// doubling embedded single quotes (per the OData spec). Null becomes "".
        /// </summary>
        public static string EscapeODataLiteral(string value)
            => (value ?? string.Empty).Replace("'", "''");
    }
}
