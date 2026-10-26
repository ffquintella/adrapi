using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Threading;
using adrapi.domain.Exceptions;
using adrapi.domain.Security;
using adrapi.Ldap;
using adrapi.Ldap.Security;
using Novell.Directory.Ldap;
using Novell.Directory.Ldap.Controls;
using Xunit;

namespace tests
{
    public class LdapCoverageTests
    {
        private static readonly LdapConfig Config = new LdapConfig
        {
            searchBase = "DC=example,DC=test",
            maxResults = 25,
            DomainKey = "coverage"
        };

        [Theory]
        [InlineData(LdapSearchType.User, "", "(objectClass=user)")]
        [InlineData(LdapSearchType.User, "mail=a*(b)", "(&(objectClass=user)(mail=a*\\28b\\29))")]
        [InlineData(LdapSearchType.Group, "", "(objectClass=group)")]
        [InlineData(LdapSearchType.Group, "cn=ops", "(&(objectClass=group)(cn=ops))")]
        [InlineData(LdapSearchType.OU, "", "(&(ou=*)(objectClass=organizationalunit))")]
        [InlineData(LdapSearchType.OU, "ou=People", "(&(ou=*)(objectClass=organizationalunit)(ou=People))")]
        [InlineData(LdapSearchType.Machine, "", "(objectClass=computer)")]
        [InlineData(LdapSearchType.Machine, "cn=pc*", "(&(objectClass=computer)(cn=pc*))")]
        public async Task TypedSearch_BuildsTheExpectedSafeFilter(
            LdapSearchType type,
            string input,
            string expected)
        {
            var scenario = new ConnectionScenario();
            scenario.Searches.Enqueue(SearchResult());
            var manager = Manager(scenario);

            var result = await manager.ExecuteSearchAsync("OU=People,", type, input, Config);

            Assert.Empty(result);
            Assert.Equal(expected, Assert.Single(scenario.Requests).Filter);
        }

        [Fact]
        public async Task TypedSearch_RejectsAnUnknownSearchType()
        {
            var manager = Manager(new ConnectionScenario());

            await Assert.ThrowsAsync<WrongParameterException>(() =>
                manager.ExecuteSearchAsync("", (LdapSearchType)999, config: Config));
        }

        [Fact]
        public async Task Search_ReturnsEntriesAndTargetsTheConfiguredBase()
        {
            var scenario = new ConnectionScenario();
            scenario.Searches.Enqueue(SearchResult(
                Entry("CN=Ada,OU=People,DC=example,DC=test"),
                Entry("CN=Grace,OU=People,DC=example,DC=test")));
            var manager = Manager(scenario);

            var result = await manager.ExecuteSearchAsync("OU=People,", "mail=*", Config);

            Assert.Equal(new[] { "CN=Ada,OU=People,DC=example,DC=test", "CN=Grace,OU=People,DC=example,DC=test" },
                result.Select(entry => entry.Dn));
            var request = Assert.Single(scenario.Requests);
            Assert.Equal("OU=People,DC=example,DC=test", request.SearchBase);
            Assert.Equal(LdapConnection.ScopeSub, request.Scope);
            Assert.Equal("mail=*", request.Filter);
            Assert.NotNull(request.Constraints);
        }

        [Fact]
        public async Task Search_IgnoresAReferralAndContinuesEnumerating()
        {
            var scenario = new ConnectionScenario();
            scenario.Searches.Enqueue(() => Task.FromResult(SearchResultsProxy.Create(
                new[] { Entry("CN=Ada,DC=example,DC=test") },
                controls: null,
                nextFailures: new Queue<Exception>(new[] { new LdapReferralException() }))));

            var result = await Manager(scenario).ExecuteSearchAsync("", "cn=*", Config);

            Assert.Equal("CN=Ada,DC=example,DC=test", Assert.Single(result).Dn);
        }

        [Fact]
        public async Task Search_ReturnsSafelyWhenTheServerStopsEnumerationWithAnLdapError()
        {
            var scenario = new ConnectionScenario();
            scenario.Searches.Enqueue(() => Task.FromResult(SearchResultsProxy.Create(
                new[] { Entry("CN=Ada,DC=example,DC=test") },
                controls: null,
                hasMoreFailures: new Queue<Exception>(new[] { new LdapException("server stopped") }))));

            var result = await Manager(scenario).ExecuteSearchAsync("", "cn=*", Config);

            Assert.Empty(result);
        }

        [Fact]
        public async Task LimitedSearch_ReturnsTheServerWindow()
        {
            var scenario = new ConnectionScenario();
            scenario.Searches.Enqueue(SearchResult());
            scenario.Searches.Enqueue(SearchResult(Entry("CN=B,DC=example,DC=test"), Entry("CN=C,DC=example,DC=test")));
            var manager = Manager(scenario);

            var result = await manager.ExecuteLimitedSearchAsync("", LdapSearchType.User, 2, 3, config: Config);

            Assert.Equal(new[] { "CN=B,DC=example,DC=test", "CN=C,DC=example,DC=test" }, result.Select(entry => entry.Dn));
            Assert.Equal(2, scenario.Requests.Count);
            Assert.Equal(LdapConnection.ScopeOne, scenario.Requests[0].Scope);
            Assert.Equal(LdapConnection.ScopeSub, scenario.Requests[1].Scope);
        }

        [Fact]
        public async Task LimitedSearch_UsesTheServerReportedVirtualListSize()
        {
            const string vlvResponseOid = "2.16.840.1.113730.3.4.10";
            var encoded = new byte[] { 0x30, 0x09, 0x02, 0x01, 0x01, 0x02, 0x01, 0x09, 0x0a, 0x01, 0x00 };
            var vlvResponse = new LdapVirtualListResponse(vlvResponseOid, true, encoded);
            var scenario = new ConnectionScenario();
            scenario.Searches.Enqueue(SearchResult(Array.Empty<LdapEntry>(), new LdapControl[] { vlvResponse }));
            scenario.Searches.Enqueue(SearchResult(Entry("CN=B,DC=example,DC=test")));

            var result = await Manager(scenario).ExecuteLimitedSearchAsync("", "cn=*", 2, 2, Config);

            Assert.Equal("CN=B,DC=example,DC=test", Assert.Single(result).Dn);
            Assert.Equal(9, vlvResponse.ContentCount);
        }

        [Fact]
        public async Task LimitedSearch_WhenVlvIsUnsupported_FallsBackToAnInMemoryRange()
        {
            var scenario = new ConnectionScenario();
            scenario.Searches.Enqueue(SearchResult());
            scenario.Searches.Enqueue(() => Task.FromException<ILdapSearchResults>(
                new LdapException("VLV unsupported", 12, null)));
            scenario.Searches.Enqueue(SearchResult(
                Entry("CN=A,DC=example,DC=test"),
                Entry("CN=B,DC=example,DC=test"),
                Entry("CN=C,DC=example,DC=test")));
            var manager = Manager(scenario);

            var result = await manager.ExecuteLimitedSearchAsync("", "objectClass=user", 0, 2, Config);

            Assert.Equal(new[] { "CN=A,DC=example,DC=test", "CN=B,DC=example,DC=test" }, result.Select(entry => entry.Dn));
            Assert.Equal(3, scenario.Requests.Count);
        }

        [Fact]
        public async Task LimitedSearch_FallbackHandlesEmptyAndOutOfRangeWindows()
        {
            var empty = new ConnectionScenario();
            empty.Searches.Enqueue(SearchResult());
            empty.Searches.Enqueue(() => Task.FromException<ILdapSearchResults>(new LdapException("VLV", 12, null)));
            empty.Searches.Enqueue(SearchResult());
            Assert.Empty(await Manager(empty).ExecuteLimitedSearchAsync("", "cn=*", 1, 3, Config));

            var outOfRange = new ConnectionScenario();
            outOfRange.Searches.Enqueue(SearchResult());
            outOfRange.Searches.Enqueue(() => Task.FromException<ILdapSearchResults>(new LdapException("VLV", 12, null)));
            outOfRange.Searches.Enqueue(SearchResult(Entry("CN=A,DC=example,DC=test")));
            Assert.Empty(await Manager(outOfRange).ExecuteLimitedSearchAsync("", "cn=*", 4, 6, Config));

            var openEnded = new ConnectionScenario();
            openEnded.Searches.Enqueue(SearchResult());
            openEnded.Searches.Enqueue(() => Task.FromException<ILdapSearchResults>(new LdapException("VLV", 12, null)));
            openEnded.Searches.Enqueue(SearchResult(
                Entry("CN=A,DC=example,DC=test"),
                Entry("CN=B,DC=example,DC=test")));
            Assert.Equal(2, (await Manager(openEnded).ExecuteLimitedSearchAsync("", "cn=*", 1, 0, Config)).Count);
        }

        [Fact]
        public async Task PagedSearch_DecodesAndReturnsTheOpaqueCookie()
        {
            const string oid = "1.2.840.113556.1.4.319";
            var encodedResponse = new LdapPagedResultsControl(9, "next-page").GetValue();
            var responseControl = new LdapPagedResultsResponse(oid, true, encodedResponse);
            var scenario = new ConnectionScenario();
            scenario.Searches.Enqueue(SearchResult(
                new[] { Entry("CN=A,DC=example,DC=test") },
                new LdapControl[] { responseControl }));
            var manager = Manager(scenario);

            var incoming = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("previous-page"));
            var result = await manager.ExecutePagedSearchAsync("", LdapSearchType.Group, cookie: incoming, config: Config);

            Assert.Equal("CN=A,DC=example,DC=test", Assert.Single(result.Entries).Dn);
            Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("next-page")), result.Cookie);
            Assert.Equal("(objectClass=group)", Assert.Single(scenario.Requests).Filter);
        }

        [Fact]
        public async Task PagedSearch_WithoutResponseControlLeavesCookieUnset()
        {
            var scenario = new ConnectionScenario();
            scenario.Searches.Enqueue(SearchResult(Entry("CN=A,DC=example,DC=test")));

            var result = await Manager(scenario).ExecutePagedSearchAsync("", "cn=*", config: Config);

            Assert.Single(result.Entries);
            Assert.Null(result.Cookie);
        }

        [Fact]
        public async Task PagedSearch_IgnoresReferralsAndStopsSafelyOnOtherEnumerationErrors()
        {
            var referral = new ConnectionScenario();
            referral.Searches.Enqueue(() => Task.FromResult(SearchResultsProxy.Create(
                new[] { Entry("CN=A,DC=example,DC=test") },
                controls: null,
                nextFailures: new Queue<Exception>(new[] { new LdapReferralException() }))));

            var referralResult = await Manager(referral).ExecutePagedSearchAsync("", "cn=*", config: Config);
            Assert.Equal("CN=A,DC=example,DC=test", Assert.Single(referralResult.Entries).Dn);

            var failure = new ConnectionScenario();
            failure.Searches.Enqueue(() => Task.FromResult(SearchResultsProxy.Create(
                new[] { Entry("CN=A,DC=example,DC=test") },
                controls: null,
                nextFailures: new Queue<Exception>(new[] { new InvalidOperationException("server stopped") }))));

            Assert.Empty((await Manager(failure).ExecutePagedSearchAsync("", "cn=*", config: Config)).Entries);
        }

        [Fact]
        public async Task ReadAndWriteOperations_AreForwardedToTheCleanConnection()
        {
            var scenario = new ConnectionScenario
            {
                ReadResult = Entry("CN=Ada,DC=example,DC=test")
            };
            var manager = Manager(scenario);
            var added = Entry("CN=Grace,DC=example,DC=test");
            var changes = new[] { new LdapModification(LdapModification.Replace, new LdapAttribute("mail", "new@example.test")) };

            Assert.Same(scenario.ReadResult, await manager.GetRegister(scenario.ReadResult.Dn, config: Config));
            Assert.Same(scenario.ReadResult, await manager.GetRegister(scenario.ReadResult.Dn, new[] { "mail" }, Config));
            await manager.AddEntryAsync(added, Config);
            await manager.DeleteEntry(added.Dn, Config);
            await manager.SaveEntry(scenario.ReadResult.Dn, changes, Config);

            Assert.All(scenario.CleanFlags, Assert.True);
            Assert.Equal(scenario.ReadResult.Dn, scenario.Read.Dn);
            Assert.Equal(new[] { "mail" }, scenario.Read.Attributes);
            Assert.Same(added, scenario.Added);
            Assert.Equal(added.Dn, scenario.DeletedDn);
            Assert.Equal(scenario.ReadResult.Dn, scenario.Modified.Dn);
            Assert.Same(changes, scenario.Modified.Changes);
        }

        [Fact]
        public void InjectionEscaping_ProtectsDnAndFilterSyntax()
        {
            Assert.Equal("\\ leading\\,plus\\+quote\\\"lt\\<gt\\>semi\\;trail\\ ",
                LdapInjectionControll.EscapeForDN(" leading,plus+quote\"lt<gt>semi;trail "));
            Assert.Equal("#hash", LdapInjectionControll.EscapeForDN("#hash").TrimStart('\\'));
            Assert.Equal("a\\5cb\\2a\\28c\\29\\00", LdapInjectionControll.EscapeForSearchFilter("a\\b*(c)\0"));
            Assert.Equal("a*\\28b\\29\\5c\\00", LdapInjectionControll.EscapeForSearchFilterAllowWC("a*(b)\\\0"));
        }

        [Fact]
        public void PagedControl_RoundTripsSizeAndCookie()
        {
            const string oid = "1.2.840.113556.1.4.319";
            var request = new LdapPagedResultsControl(37, "opaque-cookie");

            var response = new LdapPagedResultsResponse(oid, true, request.GetValue());

            Assert.Equal(37, response.Size);
            Assert.Equal("opaque-cookie", response.Cookie);
        }

        [Fact]
        public void PagedResponse_RejectsMalformedBerPayloads()
        {
            const string oid = "1.2.840.113556.1.4.319";

            Assert.Throws<IOException>(() =>
                new LdapPagedResultsResponse(oid, true, new byte[] { 0x02, 0x01, 0x01 }));
            Assert.Throws<IOException>(() =>
                new LdapPagedResultsResponse(oid, true,
                    new byte[] { 0x30, 0x06, 0x04, 0x01, 0x78, 0x04, 0x01, 0x79 }));
            Assert.Throws<IOException>(() =>
                new LdapPagedResultsResponse(oid, true,
                    new byte[] { 0x30, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02 }));
        }

        [Fact]
        public void CertificateValidator_AcceptsSystemTrustOrExactHostPin_AndRejectsOtherCertificates()
        {
            var path = Path.Combine(Path.GetTempPath(), $"adrapi-pins-{Guid.NewGuid():N}.json");
            try
            {
                using var rsa = RSA.Create(2048);
                var request = new CertificateRequest("CN=ldap.example.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
                var store = new LdapCertificatePinStore(path);
                var validator = new LdapCertificateValidator(store, "ldap.example.test");

                Assert.True(validator.Validate(null, null, null, SslPolicyErrors.None));
                Assert.False(validator.Validate(null, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
                Assert.False(validator.Validate(null, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));

                store.Add(new LdapCertificatePin
                {
                    Host = "ldap.example.test",
                    Sha256 = LdapCertificatePinStore.ComputeSha256(certificate)
                });

                Assert.True(validator.Validate(null, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));
                Assert.False(new LdapCertificateValidator(store, "other.example.test")
                    .Validate(null, certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public async Task ConnectionManager_ValidatesConfigurationBeforeConnecting()
        {
            var manager = new LdapConnectionManager();

            await Assert.ThrowsAsync<NullException>(() => manager.GetConnectionAsync(null));
            await Assert.ThrowsAsync<WrongParameterException>(() => manager.GetConnectionAsync(new LdapConfig
            {
                servers = Array.Empty<string>(),
                poolSize = 1,
                DomainKey = $"empty-{Guid.NewGuid():N}"
            }));
            await Assert.ThrowsAsync<WrongParameterException>(() => manager.GetConnectionAsync(new LdapConfig
            {
                servers = new[] { "missing-port" },
                poolSize = 1,
                DomainKey = $"invalid-{Guid.NewGuid():N}"
            }));
        }

        [Fact]
        public async Task ConnectionManager_InitializesAndReusesRegularAndCleanPools()
        {
            using var server = new LoopbackLdapServer();
            var manager = new LdapConnectionManager();
            var config = ServerConfig(server.Port, "service", domainKey: $"pool-{Guid.NewGuid():N}");
            config.poolSize = 0;

            var regular = await manager.GetConnectionAsync(config);
            var clean = await manager.GetConnectionAsync(config, clean: true);
            var reused = await manager.GetConnectionAsync(config);

            Assert.True(regular.Connected);
            Assert.True(clean.Connected);
            Assert.Same(regular, reused);
            Assert.Equal(1, config.poolSize);

            regular.Disconnect();
            var reconnected = await manager.GetConnectionAsync(config);
            Assert.True(reconnected.Connected);
            Assert.Same(regular, reconnected);
            reconnected.Disconnect();
            clean.Disconnect();
        }

        [Fact]
        public async Task ConnectionManager_MapsConnectAndPoolBindFailures()
        {
            using var unavailableEndpoint = BindUnavailableEndpoint();
            var unavailablePort = ((IPEndPoint)unavailableEndpoint.LocalEndPoint).Port;
            var manager = new LdapConnectionManager();

            var connectError = await Assert.ThrowsAsync<WrongParameterException>(() =>
                manager.GetConnectionAsync(ServerConfig(unavailablePort, "service", $"connect-{Guid.NewGuid():N}")));
            Assert.Contains("Failed to connect", connectError.Message);

            using var server = new LoopbackLdapServer(login => !login.Contains("reject", StringComparison.OrdinalIgnoreCase));
            await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
                manager.GetConnectionAsync(ServerConfig(server.Port, "reject-service", $"bind-{Guid.NewGuid():N}")));
        }

        [Fact]
        public async Task ConnectionManager_AuthenticationBindsWithCallerCredentials()
        {
            using var server = new LoopbackLdapServer(login => !login.Contains("reject", StringComparison.OrdinalIgnoreCase));
            var manager = new LdapConnectionManager();
            var config = ServerConfig(server.Port, "service", $"auth-{Guid.NewGuid():N}");

            Assert.True(await manager.ValidateAuthenticationAsync("CN=Ada", "correct", config));
            Assert.False(await manager.ValidateAuthenticationAsync("CN=Reject", "wrong", config));
            Assert.Contains("CN=Ada", server.BindDns);
            Assert.Contains("CN=Reject", server.BindDns);
        }

        private static LdapQueryManager Manager(ConnectionScenario scenario)
            => new LdapQueryManager((config, clean) =>
            {
                scenario.CleanFlags.Add(clean);
                return Task.FromResult(scenario.CreateConnection());
            });

        private static LdapConfig ServerConfig(int port, string bindDn, string domainKey)
            => new LdapConfig
            {
                servers = new[] { $"127.0.0.1:{port}" },
                bindDn = bindDn,
                bindCredentials = "password",
                poolSize = 1,
                maxResults = 25,
                searchBase = "DC=example,DC=test",
                ssl = false,
                DomainKey = domainKey
            };

        private static Socket BindUnavailableEndpoint()
        {
            for (var port = 20000; port < 30000; port++)
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                    return socket;
                }
                catch (SocketException)
                {
                    socket.Dispose();
                }
            }

            throw new InvalidOperationException("No loopback port is available for the LDAP connection failure test.");
        }

        private static TcpListener StartLowPortListener()
        {
            for (var port = 20000; port < 30000; port++)
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    listener.Start();
                    return listener;
                }
                catch (SocketException)
                {
                    listener.Stop();
                }
            }

            throw new InvalidOperationException("No loopback port is available for the LDAP protocol test.");
        }

        private static LdapEntry Entry(string dn)
            => new LdapEntry(dn, new LdapAttributeSet
            {
                new LdapAttribute("objectClass", "user"),
                new LdapAttribute("cn", dn.Split(',')[0].Substring(3))
            });

        private static Func<Task<ILdapSearchResults>> SearchResult(params LdapEntry[] entries)
            => SearchResult(entries, null);

        private static Func<Task<ILdapSearchResults>> SearchResult(
            IEnumerable<LdapEntry> entries,
            LdapControl[] controls)
            => () => Task.FromResult(SearchResultsProxy.Create(entries, controls));

        private sealed class ConnectionScenario
        {
            internal Queue<Func<Task<ILdapSearchResults>>> Searches { get; } = new();
            internal List<SearchRequest> Requests { get; } = new();
            internal List<bool> CleanFlags { get; } = new();
            internal LdapEntry ReadResult { get; set; }
            internal (string Dn, string[] Attributes) Read { get; set; }
            internal LdapEntry Added { get; set; }
            internal string DeletedDn { get; set; }
            internal (string Dn, LdapModification[] Changes) Modified { get; set; }

            internal ILdapConnection CreateConnection()
            {
                var proxy = DispatchProxy.Create<ILdapConnection, ConnectionProxy>();
                ((ConnectionProxy)(object)proxy).Scenario = this;
                return proxy;
            }
        }

        private class ConnectionProxy : DispatchProxy
        {
            internal ConnectionScenario Scenario { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                switch (targetMethod.Name)
                {
                    case "get_SearchConstraints":
                        return new LdapSearchConstraints();
                    case "SearchAsync":
                        Scenario.Requests.Add(new SearchRequest(
                            (string)args[0], (int)args[1], (string)args[2],
                            args.Length > 5 ? args[5] as LdapSearchConstraints : null));
                        return Scenario.Searches.Dequeue()();
                    case "ReadAsync":
                        Scenario.Read = ((string)args[0], args.Length > 1 ? args[1] as string[] : null);
                        return Task.FromResult(Scenario.ReadResult);
                    case "AddAsync":
                        Scenario.Added = (LdapEntry)args[0];
                        return Task.CompletedTask;
                    case "DeleteAsync":
                        Scenario.DeletedDn = (string)args[0];
                        return Task.CompletedTask;
                    case "ModifyAsync":
                        Scenario.Modified = ((string)args[0], (LdapModification[])args[1]);
                        return Task.CompletedTask;
                    case "Dispose":
                    case "Disconnect":
                        return null;
                    default:
                        throw new NotSupportedException(targetMethod.ToString());
                }
            }
        }

        private class SearchResultsProxy : DispatchProxy
        {
            private List<LdapEntry> _entries;
            private int _index;
            private LdapControl[] _controls;

            private Queue<Exception> _hasMoreFailures;
            private Queue<Exception> _nextFailures;

            internal static ILdapSearchResults Create(
                IEnumerable<LdapEntry> entries,
                LdapControl[] controls,
                Queue<Exception> hasMoreFailures = null,
                Queue<Exception> nextFailures = null)
            {
                var proxy = DispatchProxy.Create<ILdapSearchResults, SearchResultsProxy>();
                var state = (SearchResultsProxy)(object)proxy;
                state._entries = entries.ToList();
                state._controls = controls;
                state._hasMoreFailures = hasMoreFailures ?? new Queue<Exception>();
                state._nextFailures = nextFailures ?? new Queue<Exception>();
                return proxy;
            }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == "HasMoreAsync" && _hasMoreFailures.Count > 0)
                {
                    return Task.FromException<bool>(_hasMoreFailures.Dequeue());
                }
                if (targetMethod.Name == "NextAsync" && _nextFailures.Count > 0)
                {
                    return Task.FromException<LdapEntry>(_nextFailures.Dequeue());
                }

                return targetMethod.Name switch
                {
                    "HasMoreAsync" => Task.FromResult(_index < _entries.Count),
                    "NextAsync" => Task.FromResult(_entries[_index++]),
                    "get_ResponseControls" => _controls,
                    _ => throw new NotSupportedException(targetMethod.ToString())
                };
            }
        }

        private sealed record SearchRequest(
            string SearchBase,
            int Scope,
            string Filter,
            LdapSearchConstraints Constraints);

        private sealed class LoopbackLdapServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _stop = new();
            private readonly Func<string, bool> _acceptLogin;
            private readonly Task _acceptLoop;
            private readonly List<Task> _clientTasks = new();

            internal LoopbackLdapServer(Func<string, bool> acceptLogin = null)
            {
                _acceptLogin = acceptLogin ?? (_ => true);
                _listener = StartLowPortListener();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _acceptLoop = AcceptConnectionsAsync();
            }

            internal int Port { get; }
            internal List<string> BindDns { get; } = new();

            private async Task AcceptConnectionsAsync()
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        var clientTask = ServeAsync(client);
                        lock (_clientTasks) _clientTasks.Add(clientTask);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            private async Task ServeAsync(TcpClient client)
            {
                using (client)
                {
                    var stream = client.GetStream();
                    try
                    {
                        while (!_stop.IsCancellationRequested)
                        {
                            var packet = await ReadPacketAsync(stream, _stop.Token);
                            if (packet == null) return;
                            if (!TryReadBind(packet, out var messageId, out var bindDn)) return;

                            lock (BindDns) BindDns.Add(bindDn);
                            var resultCode = _acceptLogin(bindDn) ? (byte)0 : (byte)49;
                            var response = new byte[]
                            {
                                0x30, 0x0c, 0x02, 0x01, messageId,
                                0x61, 0x07, 0x0a, 0x01, resultCode,
                                0x04, 0x00, 0x04, 0x00
                            };
                            await stream.WriteAsync(response, _stop.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            private static async Task<byte[]> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
            {
                var header = new byte[2];
                if (await ReadExactAsync(stream, header, cancellationToken) == 0) return null;
                if (header[0] != 0x30 || (header[1] & 0x80) != 0) return null;

                var packet = new byte[2 + header[1]];
                packet[0] = header[0];
                packet[1] = header[1];
                return await ReadExactAsync(stream, packet.AsMemory(2), cancellationToken) == header[1]
                    ? packet
                    : null;
            }

            private static async Task<int> ReadExactAsync(
                NetworkStream stream,
                Memory<byte> buffer,
                CancellationToken cancellationToken)
            {
                var read = 0;
                while (read < buffer.Length)
                {
                    var count = await stream.ReadAsync(buffer[read..], cancellationToken);
                    if (count == 0) return read;
                    read += count;
                }
                return read;
            }

            private static bool TryReadBind(byte[] packet, out byte messageId, out string bindDn)
            {
                messageId = 0;
                bindDn = null;
                if (packet.Length < 9 || packet[2] != 0x02 || packet[3] != 0x01 || packet[5] != 0x60) return false;
                messageId = packet[4];

                var index = 7;
                if (packet[index] != 0x02) return false;
                index += 2 + packet[index + 1];
                if (index + 2 > packet.Length || packet[index] != 0x04) return false;
                var length = packet[index + 1];
                index += 2;
                if (index + length > packet.Length) return false;
                bindDn = System.Text.Encoding.UTF8.GetString(packet, index, length);
                return true;
            }

            public void Dispose()
            {
                _stop.Cancel();
                _listener.Stop();
                try { _acceptLoop.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
                Task[] clientTasks;
                lock (_clientTasks) clientTasks = _clientTasks.ToArray();
                Task.WhenAll(clientTasks).GetAwaiter().GetResult();
                _stop.Dispose();
            }
        }
    }
}
