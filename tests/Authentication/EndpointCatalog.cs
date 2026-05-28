using System.Collections.Generic;
using System.Net.Http;

namespace tests.Authentication
{
    /// <summary>
    /// Single source of truth for every routable controller action in adrapi.
    ///
    /// **Whenever a new endpoint is added or removed, update this list.** The
    /// authentication test suite drives off it — see AGENTS.md.
    /// </summary>
    public static class EndpointCatalog
    {
        public record Endpoint(HttpMethod Method, string Path, string Policy, string ApiVersion, bool RequiresBody = false);

        // Policy names match those declared in Startup.ConfigureServices.
        public const string Reading = "Reading";   // isAdministrator OR isMonitor
        public const string Writting = "Writting"; // isAdministrator only

        public static IEnumerable<Endpoint> All => new[]
        {
            // ---------- V1 (deprecated) ----------
            new Endpoint(HttpMethod.Get,    "/api/users?_start=0&_end=1",                "Reading",  "1.0"),
            new Endpoint(HttpMethod.Get,    "/api/users?_full=true&_start=0&_end=1",     "Reading",  "1.0"),
            new Endpoint(HttpMethod.Get,    "/api/users/sampleuser",                     "Reading",  "1.0"),
            new Endpoint(HttpMethod.Get,    "/api/users/sampleuser/exists",              "Reading",  "1.0"),
            new Endpoint(HttpMethod.Get,    "/api/users/sampleuser/attributes",          "Reading",  "1.0"),
            new Endpoint(HttpMethod.Get,    "/api/users/sampleuser/member-of/grp",       "Reading",  "1.0"),
            new Endpoint(HttpMethod.Get,    "/api/users/sampleuser/groups",              "Reading",  "1.0"),
            new Endpoint(HttpMethod.Post,   "/api/users/sampleuser/authenticate",        "Reading",  "1.0", RequiresBody: true),
            new Endpoint(HttpMethod.Post,   "/api/users/authenticate",                   "Reading",  "1.0", RequiresBody: true),
            new Endpoint(HttpMethod.Put,    "/api/users/cn=x,dc=test",                   "Writting", "1.0", RequiresBody: true),
            new Endpoint(HttpMethod.Delete, "/api/users/sampleuser",                     "Writting", "1.0"),

            new Endpoint(HttpMethod.Get,    "/api/Groups?_start=0&_end=1",               "Reading",  "1.0"),
            new Endpoint(HttpMethod.Get,    "/api/Groups?_full=true&_start=0&_end=1",    "Reading",  "1.0"),
            new Endpoint(HttpMethod.Get,    "/api/Groups/cn=x,dc=test",                  "Reading",  "1.0"),
            new Endpoint(HttpMethod.Get,    "/api/Groups/cn=x,dc=test/exists",           "Reading",  "1.0"),
            new Endpoint(HttpMethod.Get,    "/api/Groups/grpid/members",                 "Reading",  "1.0"),
            new Endpoint(HttpMethod.Put,    "/api/Groups/cn=x,dc=test",                  "Writting", "1.0", RequiresBody: true),
            new Endpoint(HttpMethod.Put,    "/api/Groups/cn=x,dc=test/members",          "Writting", "1.0", RequiresBody: true),
            new Endpoint(HttpMethod.Delete, "/api/Groups/cn=x,dc=test",                  "Writting", "1.0"),

            // ---------- V2 ----------
            new Endpoint(HttpMethod.Get,    "/api/users?_start=0&_end=1",                "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/users?_full=true&_start=0&_end=1",     "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/users/sampleuser",                     "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/users/sampleuser/exists",              "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/users/sampleuser/attributes",          "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/users/sampleuser/member-of/grp",       "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/users/sampleuser/groups",              "Reading",  "2.0"),
            new Endpoint(HttpMethod.Post,   "/api/users/sampleuser/authenticate",        "Reading",  "2.0", RequiresBody: true),
            new Endpoint(HttpMethod.Post,   "/api/users/authenticate",                   "Reading",  "2.0", RequiresBody: true),
            new Endpoint(HttpMethod.Put,    "/api/users/cn=x,dc=test",                   "Writting", "2.0", RequiresBody: true),
            new Endpoint(HttpMethod.Delete, "/api/users/sampleuser",                     "Writting", "2.0"),

            new Endpoint(HttpMethod.Get,    "/api/Groups?_start=0&_end=1",               "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/Groups?_full=true&_start=0&_end=1",    "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/Groups/grpid",                         "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/Groups/cn=x,dc=test/exists",           "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/Groups/grpid/members",                 "Reading",  "2.0"),
            new Endpoint(HttpMethod.Post,   "/api/Groups",                               "Writting", "2.0", RequiresBody: true),
            new Endpoint(HttpMethod.Put,    "/api/Groups/cn=x,dc=test",                  "Writting", "2.0", RequiresBody: true),
            new Endpoint(HttpMethod.Put,    "/api/Groups/cn=x,dc=test/members",          "Writting", "2.0", RequiresBody: true),
            new Endpoint(HttpMethod.Patch,  "/api/Groups/cn=x,dc=test/members",          "Writting", "2.0", RequiresBody: true),
            new Endpoint(HttpMethod.Delete, "/api/Groups/cn=x,dc=test",                  "Writting", "2.0"),

            // ---------- OUs (unversioned, defaults to v2) ----------
            new Endpoint(HttpMethod.Get,    "/api/OUs",                                  "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/OUs/ou=x,dc=test",                     "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/OUs/ou=x,dc=test/exists",              "Reading",  "2.0"),
            new Endpoint(HttpMethod.Post,   "/api/OUs",                                  "Writting", "2.0", RequiresBody: true),
            new Endpoint(HttpMethod.Put,    "/api/OUs/ou=x,dc=test",                     "Writting", "2.0", RequiresBody: true),
            new Endpoint(HttpMethod.Delete, "/api/OUs/ou=x,dc=test",                     "Writting", "2.0"),

            // ---------- Infos ----------
            new Endpoint(HttpMethod.Get,    "/api/Infos",                                "Reading",  "2.0"),
            new Endpoint(HttpMethod.Get,    "/api/Infos/sampleinfo",                     "Reading",  "2.0"),
        };

        public static IEnumerable<object[]> AllAsTheoryData()
        {
            foreach (var e in All)
                yield return new object[] { e.Method.Method, e.Path, e.Policy, e.ApiVersion, e.RequiresBody };
        }

        public static IEnumerable<object[]> WriteEndpointsAsTheoryData()
        {
            foreach (var e in All)
                if (e.Policy == Writting)
                    yield return new object[] { e.Method.Method, e.Path, e.Policy, e.ApiVersion, e.RequiresBody };
        }
    }
}
