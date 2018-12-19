using System;
using Xunit;
using adrapi.Ldap;
using adrapi.domain.Exceptions;
using Novell.Directory.Ldap;

namespace tests
{
    public class LdapTests
    {
        [Fact]
        public void ConnectionManagerTest()
        {

            string[] servers = new string[] { "teste:389", "teste2:389" };

            var lconfig = new LdapConfig(servers, false, 2, "testeDN", "testCred", "testSearch", "testFilter", "testAdmin");

            Assert.NotNull(lconfig.adminCn);

            Assert.Equal(2, lconfig.servers.Length);

            var lcm = LdapConnectionManager.Instance;

            Exception ex = Assert.Throws<NullException>(() => lcm.GetConnection(null));

            Exception ex2 = Assert.Throws<Novell.Directory.Ldap.LdapException>(() => lcm.GetConnection(lconfig));

                



        }
    }
}
