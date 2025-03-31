using System;
using System.Threading.Tasks;
using Xunit;
using adrapi.Ldap;
using adrapi.domain.Exceptions;
using Novell.Directory.Ldap;

namespace tests
{
    public class LdapTests
    {
        [Fact]
        public async Task ConnectionManagerTest()
        {

            string[] servers = new string[] { "teste:389", "teste2:389" };

            var lconfig = new LdapConfig(servers, false, 1000, 2, "testeDN", "testCred", "testSearch", "testFilter", "testAdmin");

            Assert.NotNull(lconfig.adminCn);

            Assert.Equal(2, lconfig.servers.Length);

            var lcm = LdapConnectionManager.Instance;

            Exception ex = await Assert.ThrowsAsync<NullException>(async () => await lcm.GetConnectionAsync(null));

            Exception ex2 = await Assert.ThrowsAnyAsync<Exception>(async () => await lcm.GetConnectionAsync(lconfig));
            

        }
    }
}
