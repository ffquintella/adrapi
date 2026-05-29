using System;
using System.Collections.Generic;
using System.Text.Json;
using adrapi.domain;

namespace adrapi.Entra
{
    /// <summary>
    /// Translates between the adrapi <see cref="User"/> model and Microsoft Graph
    /// <c>user</c> resource JSON. Stage 4 owns the user-specific mapping; Stage 6
    /// generalises normalization and DN↔objectId/UPN translation across backends.
    ///
    /// Notes:
    /// <list type="bullet">
    /// <item><c>mail</c> is read-only in Graph (derived from proxy addresses), so it
    /// is mapped on read only — never written.</item>
    /// <item>adrapi's <c>Account</c> maps to Graph <c>mailNickname</c>; <c>Login</c>
    /// to <c>userPrincipalName</c>; <c>IsDisabled</c> to the inverse of
    /// <c>accountEnabled</c>.</item>
    /// <item>Entra has no LDAP-style DN, so <see cref="User.DN"/> is left null
    /// (the objectId in <see cref="User.ID"/> is the durable identifier).</item>
    /// </list>
    /// </summary>
    public static class GraphUserMapper
    {
        /// <summary>The user properties adrapi reads back; passed as <c>$select</c>.</summary>
        public const string SelectFields =
            "id,displayName,givenName,surname,userPrincipalName,mailNickname,mail,mobilePhone,accountEnabled";

        public static User ToUser(JsonElement el)
        {
            var user = new User
            {
                ID = Str(el, "id"),
                Name = Str(el, "displayName"),
                GivenName = Str(el, "givenName"),
                Surname = Str(el, "surname"),
                Login = Str(el, "userPrincipalName"),
                Mail = Str(el, "mail"),
                Mobile = Str(el, "mobilePhone"),
            };

            user.Account = Str(el, "mailNickname") ?? AccountFromUpn(user.Login);

            var enabled = Bool(el, "accountEnabled");
            if (enabled.HasValue)
            {
                user.IsDisabled = !enabled.Value;
            }

            return user;
        }

        /// <summary>Builds the request body for <c>POST /users</c>.</summary>
        public static Dictionary<string, object> ToCreateBody(User user)
        {
            var body = new Dictionary<string, object>
            {
                ["accountEnabled"] = !(user.IsDisabled ?? false),
                ["displayName"] = user.Name,
                ["mailNickname"] = user.Account,
                ["userPrincipalName"] = user.Login,
            };

            AddIfPresent(body, "givenName", user.GivenName);
            AddIfPresent(body, "surname", user.Surname);
            AddIfPresent(body, "mobilePhone", user.Mobile);

            if (!string.IsNullOrEmpty(user.Password))
            {
                body["passwordProfile"] = new Dictionary<string, object>
                {
                    ["password"] = user.Password,
                    ["forceChangePasswordNextSignIn"] = true,
                };
            }

            return body;
        }

        /// <summary>Builds a partial <c>PATCH /users/{id}</c> body — only set fields are sent.</summary>
        public static Dictionary<string, object> ToUpdateBody(User user)
        {
            var body = new Dictionary<string, object>();
            AddIfPresent(body, "displayName", user.Name);
            AddIfPresent(body, "givenName", user.GivenName);
            AddIfPresent(body, "surname", user.Surname);
            AddIfPresent(body, "mobilePhone", user.Mobile);
            AddIfPresent(body, "userPrincipalName", user.Login);
            AddIfPresent(body, "mailNickname", user.Account);
            if (user.IsDisabled.HasValue)
            {
                body["accountEnabled"] = !user.IsDisabled.Value;
            }

            return body;
        }

        private static void AddIfPresent(Dictionary<string, object> body, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                body[key] = value;
            }
        }

        private static string AccountFromUpn(string upn)
        {
            if (string.IsNullOrWhiteSpace(upn))
            {
                return null;
            }

            var at = upn.IndexOf('@');
            return at > 0 ? upn.Substring(0, at) : upn;
        }

        private static string Str(JsonElement el, string name)
            => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;

        private static bool? Bool(JsonElement el, string name)
            => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p)
               && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False)
                ? p.GetBoolean()
                : null;
    }
}
