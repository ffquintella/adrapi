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
    /// Stage 4 (user management via Graph) unit tests: User↔Graph mapping and the
    /// GraphDirectoryProvider user lifecycle (read/list/search/exists/create/
    /// update/disable/delete/password). No network — a scriptable fake records
    /// requests and returns canned JSON.
    /// </summary>
    public class GraphUserTests
    {
        private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

        /// <summary>Records calls and returns scripted results.</summary>
        private sealed class FakeGraphClient : IGraphClient
        {
            public Func<string, GraphResult> GetResponder = _ => new GraphResult(HttpStatusCode.OK, null, null, null);
            public List<JsonElement> Paged = new();
            public readonly List<(string url, object body)> Posts = new();
            public readonly List<(string url, object body)> Patches = new();
            public readonly List<string> Deletes = new();
            public readonly List<string> Gets = new();
            public GraphResult PostResult = new(HttpStatusCode.Created, null, null, null);

            public Task<GraphResult> GetAsync(string relativeUrl, CancellationToken ct = default)
            {
                Gets.Add(relativeUrl);
                return Task.FromResult(GetResponder(relativeUrl));
            }

            public Task<List<JsonElement>> GetPagedAsync(string relativeUrl, CancellationToken ct = default)
            {
                Gets.Add(relativeUrl);
                return Task.FromResult(Paged);
            }

            public Task<GraphResult> PostAsync(string relativeUrl, object body, CancellationToken ct = default)
            {
                Posts.Add((relativeUrl, body));
                return Task.FromResult(PostResult);
            }

            public Task<GraphResult> PatchAsync(string relativeUrl, object body, CancellationToken ct = default)
            {
                Patches.Add((relativeUrl, body));
                return Task.FromResult(new GraphResult(HttpStatusCode.NoContent, null, null, null));
            }

            public Task<GraphResult> DeleteAsync(string relativeUrl, CancellationToken ct = default)
            {
                Deletes.Add(relativeUrl);
                return Task.FromResult(new GraphResult(HttpStatusCode.NoContent, null, null, null));
            }
        }

        private static (GraphDirectoryProvider provider, FakeGraphClient graph) Build()
        {
            var graph = new FakeGraphClient();
            var provider = new GraphDirectoryProvider(new EntraConfig { DomainKey = "cloud" }, graph);
            return (provider, graph);
        }

        // ---- Mapper ----

        [Fact]
        public void Mapper_ToUser_MapsAllFields()
        {
            var el = Parse(@"{
                ""id"": ""00000000-0000-0000-0000-000000000001"",
                ""displayName"": ""Ada Lovelace"",
                ""givenName"": ""Ada"",
                ""surname"": ""Lovelace"",
                ""userPrincipalName"": ""ada@contoso.com"",
                ""mailNickname"": ""ada"",
                ""mail"": ""ada@contoso.com"",
                ""mobilePhone"": ""+1 555 0100"",
                ""accountEnabled"": false }");

            var u = GraphUserMapper.ToUser(el);

            Assert.Equal("00000000-0000-0000-0000-000000000001", u.ID);
            Assert.Equal("Ada Lovelace", u.Name);
            Assert.Equal("Ada", u.GivenName);
            Assert.Equal("Lovelace", u.Surname);
            Assert.Equal("ada@contoso.com", u.Login);
            Assert.Equal("ada", u.Account);
            Assert.Equal("ada@contoso.com", u.Mail);
            Assert.Equal("+1 555 0100", u.Mobile);
            Assert.True(u.IsDisabled); // accountEnabled:false -> disabled
            Assert.Null(u.DN);         // Entra has no DN
        }

        [Fact]
        public void Mapper_ToUser_FallsBackToUpnPrefixForAccount()
        {
            var u = GraphUserMapper.ToUser(Parse(@"{ ""userPrincipalName"": ""grace@contoso.com"" }"));
            Assert.Equal("grace", u.Account);
        }

        [Fact]
        public void Mapper_ToCreateBody_HasRequiredFieldsAndPasswordProfile()
        {
            var body = GraphUserMapper.ToCreateBody(new User
            {
                Name = "Ada Lovelace",
                Account = "ada",
                Login = "ada@contoso.com",
                Password = "S3cret!",
                IsDisabled = false,
            });

            Assert.Equal(true, body["accountEnabled"]);
            Assert.Equal("Ada Lovelace", body["displayName"]);
            Assert.Equal("ada", body["mailNickname"]);
            Assert.Equal("ada@contoso.com", body["userPrincipalName"]);
            Assert.True(body.ContainsKey("passwordProfile"));
            Assert.False(body.ContainsKey("mail")); // mail is read-only in Graph
        }

        [Fact]
        public void Mapper_ToUpdateBody_OnlyIncludesSetFields()
        {
            var body = GraphUserMapper.ToUpdateBody(new User { Surname = "Byron", IsDisabled = true });

            Assert.Equal("Byron", body["surname"]);
            Assert.Equal(false, body["accountEnabled"]); // disabled -> accountEnabled:false
            Assert.False(body.ContainsKey("displayName"));
            Assert.False(body.ContainsKey("givenName"));
        }

        // ---- Provider ----

        [Fact]
        public async Task GetUser_ParsesBody_AndRequestsSelect()
        {
            var (provider, graph) = Build();
            graph.GetResponder = _ => new GraphResult(HttpStatusCode.OK, Parse(@"{""id"":""abc"",""displayName"":""Ada""}"), "r1", null);

            var u = await provider.GetUserAsync("ada@contoso.com");

            Assert.Equal("abc", u.ID);
            Assert.Equal("Ada", u.Name);
            Assert.Contains("$select=", graph.Gets.Single());
            Assert.Contains("ada%40contoso.com", graph.Gets.Single()); // identifier escaped
        }

        [Fact]
        public async Task GetUser_NotFound_ReturnsNull()
        {
            var (provider, graph) = Build();
            graph.GetResponder = _ => throw new GraphException("nope", HttpStatusCode.NotFound);

            Assert.Null(await provider.GetUserAsync("ghost@contoso.com"));
        }

        [Fact]
        public async Task UserExists_TrueOn200_FalseOn404()
        {
            var (provider, graph) = Build();
            graph.GetResponder = _ => new GraphResult(HttpStatusCode.OK, Parse(@"{""id"":""abc""}"), null, null);
            Assert.True(await provider.UserExistsAsync("ada"));

            graph.GetResponder = _ => throw new GraphException("nope", HttpStatusCode.NotFound);
            Assert.False(await provider.UserExistsAsync("ghost"));
        }

        [Fact]
        public async Task ListAndSearch_MapPagedResults()
        {
            var (provider, graph) = Build();
            graph.Paged = new List<JsonElement>
            {
                Parse(@"{""id"":""1"",""displayName"":""A""}"),
                Parse(@"{""id"":""2"",""displayName"":""B""}"),
            };

            var all = await provider.GetUsersAsync();
            Assert.Equal(new[] { "1", "2" }, all.Select(u => u.ID));

            var found = await provider.SearchUsersAsync("a'b"); // single quote must be OData-escaped
            Assert.Equal(2, found.Count);
            Assert.Contains("startswith", Uri.UnescapeDataString(graph.Gets.Last()));
            Assert.Contains("a''b", Uri.UnescapeDataString(graph.Gets.Last()));
        }

        [Fact]
        public async Task CreateUser_PostsBody_AndCapturesGeneratedId()
        {
            var (provider, graph) = Build();
            graph.PostResult = new GraphResult(HttpStatusCode.Created, Parse(@"{""id"":""new-guid""}"), null, null);

            var user = new User { Name = "Ada", Account = "ada", Login = "ada@contoso.com", Password = "p" };
            Assert.True(await provider.CreateUserAsync(user));

            Assert.Equal("users", graph.Posts.Single().url);
            Assert.Equal("new-guid", user.ID); // server-assigned id flowed back
        }

        [Fact]
        public async Task UpdateUser_PatchesByObjectIdWhenPresent()
        {
            var (provider, graph) = Build();
            await provider.UpdateUserAsync(new User { ID = "obj-1", Login = "ada@contoso.com", Surname = "B" });

            Assert.Equal("users/obj-1", graph.Patches.Single().url);
        }

        [Fact]
        public async Task DeleteUser_FallsBackToUpnWhenNoId()
        {
            var (provider, graph) = Build();
            await provider.DeleteUserAsync(new User { Login = "ada@contoso.com" });

            Assert.Equal("users/ada%40contoso.com", graph.Deletes.Single());
        }

        [Fact]
        public async Task SetEnabled_PatchesAccountEnabled()
        {
            var (provider, graph) = Build();
            await provider.SetUserEnabledAsync("ada@contoso.com", false);

            var body = Assert.IsType<Dictionary<string, object>>(graph.Patches.Single().body);
            Assert.Equal(false, body["accountEnabled"]);
        }

        [Fact]
        public async Task SetPassword_PatchesPasswordProfile()
        {
            var (provider, graph) = Build();
            await provider.SetUserPasswordAsync("ada@contoso.com", "N3w!", forceChangeAtNextLogin: true);

            var body = Assert.IsType<Dictionary<string, object>>(graph.Patches.Single().body);
            var profile = Assert.IsType<Dictionary<string, object>>(body["passwordProfile"]);
            Assert.Equal("N3w!", profile["password"]);
            Assert.Equal(true, profile["forceChangePasswordNextSignIn"]);
        }

        [Fact]
        public async Task EmptyIdentifier_Throws()
        {
            var (provider, _) = Build();
            await Assert.ThrowsAsync<WrongParameterException>(() => provider.SetUserEnabledAsync("", true));
            await Assert.ThrowsAsync<WrongParameterException>(() => provider.DeleteUserAsync(new User()));
        }
    }
}
