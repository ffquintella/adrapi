using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using adrapi.domain;

namespace adrapi.Entra
{
    /// <summary>
    /// Translates between the adrapi <see cref="Group"/> model and Microsoft Graph
    /// <c>group</c> resource JSON.
    ///
    /// Group kind maps to the Graph trio <c>groupTypes</c>/<c>mailEnabled</c>/
    /// <c>securityEnabled</c>:
    /// <list type="bullet">
    /// <item><b>Security</b> (default): <c>groupTypes: []</c>, <c>securityEnabled: true</c>,
    /// <c>mailEnabled: false</c>.</item>
    /// <item><b>Microsoft 365</b> ("Unified"): <c>groupTypes: ["Unified"]</c>,
    /// <c>securityEnabled: false</c>, <c>mailEnabled: true</c>.</item>
    /// </list>
    /// Graph requires a valid <c>mailNickname</c> for every group; it is derived
    /// from the display name when not supplied. <c>groupTypes</c> is immutable, so
    /// it is sent on create only.
    /// </summary>
    public static class GraphGroupMapper
    {
        public const string SelectFields = "id,displayName,description,mailNickname,groupTypes,securityEnabled,mailEnabled";

        public const string KindSecurity = "Security";
        public const string KindMicrosoft365 = "Microsoft365";
        private const string UnifiedGroupType = "Unified";

        public static bool IsUnified(string groupType)
        {
            if (string.IsNullOrWhiteSpace(groupType)) return false;
            var g = groupType.Trim();
            return g.Equals(KindMicrosoft365, StringComparison.OrdinalIgnoreCase)
                || g.Equals(UnifiedGroupType, StringComparison.OrdinalIgnoreCase)
                || g.Equals("Office365", StringComparison.OrdinalIgnoreCase)
                || g.Equals("M365", StringComparison.OrdinalIgnoreCase);
        }

        public static Group ToGroup(JsonElement el)
        {
            var group = new Group
            {
                ID = Str(el, "id"),
                Name = Str(el, "displayName"),
                Description = Str(el, "description"),
            };

            var unified = el.ValueKind == JsonValueKind.Object
                && el.TryGetProperty("groupTypes", out var types)
                && types.ValueKind == JsonValueKind.Array
                && types.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String
                    && UnifiedGroupType.Equals(t.GetString(), StringComparison.OrdinalIgnoreCase));

            group.GroupType = unified ? KindMicrosoft365 : KindSecurity;
            return group;
        }

        public static Dictionary<string, object> ToCreateBody(Group group)
        {
            var unified = IsUnified(group.GroupType);
            var body = new Dictionary<string, object>
            {
                ["displayName"] = group.Name,
                ["mailNickname"] = DeriveMailNickname(group.Name),
                ["mailEnabled"] = unified,
                ["securityEnabled"] = !unified,
                ["groupTypes"] = unified ? new[] { UnifiedGroupType } : Array.Empty<string>(),
            };

            if (!string.IsNullOrEmpty(group.Description))
            {
                body["description"] = group.Description;
            }

            return body;
        }

        /// <summary>Partial <c>PATCH</c> body — only mutable, set fields (groupTypes is immutable).</summary>
        public static Dictionary<string, object> ToUpdateBody(Group group)
        {
            var body = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(group.Name)) body["displayName"] = group.Name;
            if (group.Description != null) body["description"] = group.Description;
            return body;
        }

        /// <summary>
        /// Sanitizes a display name into a valid Graph <c>mailNickname</c>
        /// (no spaces, ASCII letters/digits and a small safe punctuation set).
        /// </summary>
        public static string DeriveMailNickname(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return "group";
            }

            var sb = new StringBuilder(displayName.Length);
            foreach (var c in displayName)
            {
                if (c < 128 && (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
                {
                    sb.Append(c);
                }
            }

            return sb.Length > 0 ? sb.ToString() : "group";
        }

        private static string Str(JsonElement el, string name)
            => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
    }
}
