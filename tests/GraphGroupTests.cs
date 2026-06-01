using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using adrapi.Directory;
using adrapi.Entra;
using adrapi.domain;
using adrapi.domain.Exceptions;

namespace tests
{
    /// <summary>
    /// Stage 5 (group + membership via Graph) unit tests: Group↔Graph mapping
    /// (security vs Microsoft 365), group CRUD, and the membership operations
    /// (list/add/remove/replace) including identifier resolution and idempotency.
    /// No network — a flexible fake routes requests by URL.
    /// </summary>
    public class GraphGroupTests
    {
        private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
        private static List<JsonElement> Arr(params string[] objects) => objects.Select(Parse).ToList();

        private sealed class FakeGraph : IGraphClient
        {
            public Func<string, GraphResult> OnGet = _ => new GraphResult(HttpStatusCode.OK, null, null, null);
            public Func<string, List<JsonElement>> OnPaged = _ => new List<JsonElement>();
            public Func<string, object, GraphResult> OnPost = (_, __) => new GraphResult(HttpStatusCode.Created, null, null, null);
            public Func<string, object, GraphResult> OnPatch = (_, __) => new GraphResult(HttpStatusCode.NoContent, null, null, null);
            public Func<string, GraphResult> OnDelete = _ => new GraphResult(HttpStatusCode.NoContent, null, null, null);

            public readonly List<string> Gets = new();
            public readonly List<string> Pageds = new();
            public readonly List<(string url, object body)> Posts = new();
            public readonly List<(string url, object body)> Patches = new();
            public readonly List<string> Deletes = new();

            public Task<GraphResult> GetAsync(string url, CancellationToken ct = default) { Gets.Add(url); return Task.FromResult(OnGet(url)); }
            public Task<List<JsonElement>> GetPagedAsync(string url, CancellationToken ct = default) { Pageds.Add(url); return Task.FromResult(OnPaged(url)); }
            public Task<GraphResult> PostAsync(string url, object body, CancellationToken ct = default) { Posts.Add((url, body)); return Task.FromResult(OnPost(url, body)); }
            public Task<GraphResult> PatchAsync(string url, object body, CancellationToken ct = default) { Patches.Add((url, body)); return Task.FromResult(OnPatch(url, body)); }
            public Task<GraphResult> DeleteAsync(string url, CancellationToken ct = default) { Deletes.Add(url); return Task.FromResult(OnDelete(url)); }
        }

        private static (GraphDirectoryProvider provider, FakeGraph graph) Build()
        {
            var graph = new FakeGraph();
            var provider = new GraphDirectoryProvider(
                new EntraConfig { DomainKey = "cloud", GraphBaseUrl = "https://graph.microsoft.com/v1.0" }, graph);
            return (provider, graph);
        }

        private const string Gid = "11111111-1111-1111-1111-111111111111";
        private const string Uid = "22222222-2222-2222-2222-222222222222";

        // ---- Mapper ----

        [Fact]
        public void Mapper_ToGroup_DetectsMicrosoft365VsSecurity()
        {
            var m365 = GraphGroupMapper.ToGroup(Parse(@"{""id"":""g1"",""displayName"":""Eng"",""groupTypes"":[""Unified""]}"));
            Assert.Equal(GraphGroupMapper.KindMicrosoft365, m365.GroupType);

            var security = GraphGroupMapper.ToGroup(Parse(@"{""id"":""g2"",""displayName"":""Admins"",""groupTypes"":[]}"));
            Assert.Equal(GraphGroupMapper.KindSecurity, security.GroupType);
        }

        [Fact]
        public void Mapper_ToCreateBody_SecurityGroupFlags()
        {
            var body = GraphGroupMapper.ToCreateBody(new Group { Name = "Domain Admins", Description = "d" });

            Assert.Equal("Domain Admins", body["displayName"]);
            Assert.Equal("DomainAdmins", body["mailNickname"]); // spaces stripped
            Assert.Equal(false, body["mailEnabled"]);
            Assert.Equal(true, body["securityEnabled"]);
            Assert.Empty((string[])body["groupTypes"]);
        }

        [Fact]
        public void Mapper_ToCreateBody_Microsoft365GroupFlags()
        {
            var body = GraphGroupMapper.ToCreateBody(new Group { Name = "Marketing", GroupType = "Microsoft365" });

            Assert.Equal(true, body["mailEnabled"]);
            Assert.Equal(false, body["securityEnabled"]);
            Assert.Equal(new[] { "Unified" }, (string[])body["groupTypes"]);
        }

        [Fact]
        public void Mapper_DeriveMailNickname_StripsInvalidChars()
        {
            Assert.Equal("SalesEMEA", GraphGroupMapper.DeriveMailNickname("Sales (EMEA)!"));
            Assert.Equal("group", GraphGroupMapper.DeriveMailNickname("   "));
        }

        // ---- Group CRUD ----

        [Fact]
        public async Task GetGroups_MapsPagedResults()
        {
            var (provider, graph) = Build();
            graph.OnPaged = _ => Arr(@"{""id"":""g1"",""displayName"":""A"",""groupTypes"":[]}",
                                     @"{""id"":""g2"",""displayName"":""B"",""groupTypes"":[""Unified""]}");

            var groups = await provider.GetGroupsAsync();

            Assert.Equal(new[] { "g1", "g2" }, groups.Select(g => g.ID));
            Assert.Equal(GraphGroupMapper.KindMicrosoft365, groups[1].GroupType);
        }

        [Fact]
        public async Task GetGroup_ByObjectId_GetsDirectly()
        {
            var (provider, graph) = Build();
            graph.OnGet = url => new GraphResult(HttpStatusCode.OK, Parse(@"{""id"":""" + Gid + @""",""displayName"":""Eng""}"), null, null);

            var g = await provider.GetGroupAsync(Gid);

            Assert.Equal("Eng", g.Name);
            Assert.Empty(graph.Pageds); // GUID -> no displayName filter
            Assert.Contains($"groups/{Gid}", graph.Gets.Single());
        }

        [Fact]
        public async Task GetGroup_ByDisplayName_ResolvesViaFilterThenGets()
        {
            var (provider, graph) = Build();
            graph.OnPaged = _ => Arr(@"{""id"":""" + Gid + @"""}");
            graph.OnGet = _ => new GraphResult(HttpStatusCode.OK, Parse(@"{""id"":""" + Gid + @""",""displayName"":""Engineers""}"), null, null);

            var g = await provider.GetGroupAsync("Engineers");

            Assert.Equal(Gid, g.ID);
            Assert.Contains("$filter=", graph.Pageds.Single());
            Assert.Contains("displayName%20eq", graph.Pageds.Single());
        }

        [Fact]
        public async Task CreateGroup_PostsBody_CapturesId_AndAddsInitialMembers()
        {
            var (provider, graph) = Build();
            graph.OnPost = (url, _) => url == "groups"
                ? new GraphResult(HttpStatusCode.Created, Parse(@"{""id"":""" + Gid + @"""}"), null, null)
                : new GraphResult(HttpStatusCode.NoContent, null, null, null);

            var group = new Group { Name = "Eng", GroupType = "Security" };
            group.Member.Add(Uid); // GUID member -> no resolution lookup

            Assert.True(await provider.CreateGroupAsync(group));
            Assert.Equal(Gid, group.ID);

            Assert.Equal("groups", graph.Posts[0].url);
            // Second POST adds the member by $ref.
            Assert.Equal($"groups/{Gid}/members/$ref", graph.Posts[1].url);
            var refBody = Assert.IsType<Dictionary<string, object>>(graph.Posts[1].body);
            Assert.Equal($"https://graph.microsoft.com/v1.0/directoryObjects/{Uid}", refBody["@odata.id"]);
        }

        [Fact]
        public async Task UpdateAndDelete_UseObjectId()
        {
            var (provider, graph) = Build();

            await provider.UpdateGroupAsync(new Group { ID = Gid, Description = "new" });
            Assert.Equal($"groups/{Gid}", graph.Patches.Single().url);

            await provider.DeleteGroupAsync(new Group { ID = Gid });
            Assert.Equal($"groups/{Gid}", graph.Deletes.Single());
        }

        [Fact]
        public async Task GroupExists_TrueWhenResolvable_FalseOtherwise()
        {
            var (provider, graph) = Build();
            Assert.True(await provider.GroupExistsAsync(Gid)); // GUID resolves to itself

            graph.OnPaged = _ => new List<JsonElement>(); // no displayName match
            Assert.False(await provider.GroupExistsAsync("Ghosts"));
        }

        // ---- Membership ----

        [Fact]
        public async Task GetMembers_PrefersUpnThenObjectId()
        {
            var (provider, graph) = Build();
            graph.OnPaged = url => url.Contains("/members")
                ? Arr(@"{""id"":""u1"",""userPrincipalName"":""ada@contoso.com""}", @"{""id"":""grp-2""}")
                : new List<JsonElement>();

            var members = await provider.GetGroupMembersAsync(Gid);

            Assert.Equal(new[] { "ada@contoso.com", "grp-2" }, members);
        }

        [Fact]
        public async Task AddMembers_ResolvesUpn_PostsRef()
        {
            var (provider, graph) = Build();
            graph.OnGet = url => new GraphResult(HttpStatusCode.OK, Parse(@"{""id"":""" + Uid + @"""}"), null, null);

            await provider.AddGroupMembersAsync(Gid, new[] { "ada@contoso.com" });

            Assert.Contains("users/ada%40contoso.com", graph.Gets.Single()); // UPN resolved
            var body = Assert.IsType<Dictionary<string, object>>(graph.Posts.Single().body);
            Assert.Equal($"https://graph.microsoft.com/v1.0/directoryObjects/{Uid}", body["@odata.id"]);
        }

        [Fact]
        public async Task AddMembers_AlreadyMember_IsIdempotent()
        {
            var (provider, graph) = Build();
            graph.OnPost = (_, __) => throw new GraphException(
                "bad", HttpStatusCode.BadRequest, responseBody: "One or more added object references already exist");

            // Does not throw despite the 400.
            Assert.True(await provider.AddGroupMembersAsync(Gid, new[] { Uid }));
        }

        [Fact]
        public async Task RemoveMembers_AbsentMember_IsIdempotent()
        {
            var (provider, graph) = Build();
            graph.OnDelete = _ => throw new GraphException("missing", HttpStatusCode.NotFound);

            Assert.True(await provider.RemoveGroupMembersAsync(Gid, new[] { Uid }));
        }

        [Fact]
        public async Task ReplaceMembers_AddsMissing_RemovesExtra()
        {
            var (provider, graph) = Build();
            const string keep = "33333333-3333-3333-3333-333333333333";
            const string drop = "44444444-4444-4444-4444-444444444444";

            // Current membership = {keep, drop}; desired = {keep, Uid}.
            graph.OnPaged = url => url.Contains("/members")
                ? Arr(@"{""id"":""" + keep + @"""}", @"{""id"":""" + drop + @"""}")
                : new List<JsonElement>();

            await provider.ReplaceGroupMembersAsync(Gid, new[] { keep, Uid });

            // drop removed, Uid added, keep untouched.
            Assert.Contains(graph.Deletes, d => d.Contains(drop));
            Assert.DoesNotContain(graph.Deletes, d => d.Contains(keep));
            var added = graph.Posts.Select(p => (Dictionary<string, object>)p.body).Select(b => (string)b["@odata.id"]);
            Assert.Contains(added, a => a.EndsWith(Uid));
            Assert.DoesNotContain(added, a => a.EndsWith(keep));
        }
    }
}
