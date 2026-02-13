using System;
using System.Linq;
using System.Reflection;
using adrapi;
using adrapi.domain;
using Novell.Directory.Ldap;
using Xunit;

namespace tests
{
    public class ManagerLogicTests
    {
        private static object InvokeNonPublic(object instance, string methodName, params object[] args)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method.Invoke(instance, args);
        }

        [Fact]
        public void GroupManager_GetAttributeSet_ContainsCoreAndMembers()
        {
            var manager = GroupManager.Instance;
            var group = new Group
            {
                Name = "MyGroup",
                Description = "desc"
            };
            group.Member.Add("CN=user1,OU=Users,DC=homologa,DC=br");
            group.Member.Add("CN=user2,OU=Users,DC=homologa,DC=br");

            var attrs = (LdapAttributeSet)InvokeNonPublic(manager, "GetAttributeSet", group);

            Assert.Equal("MyGroup", attrs.GetAttribute("cn").StringValue);
            Assert.Equal("MyGroup", attrs.GetAttribute("sAMAccountName").StringValue);
            Assert.Equal("desc", attrs.GetAttribute("description").StringValue);

            var members = attrs.GetAttribute("member").StringValueArray;
            Assert.Equal(2, members.Length);
            Assert.Contains("CN=user1,OU=Users,DC=homologa,DC=br", members);
            Assert.Contains("CN=user2,OU=Users,DC=homologa,DC=br", members);
        }

        [Fact]
        public void GroupManager_ConvertFromLdap_MapsCoreFields()
        {
            var manager = GroupManager.Instance;
            var attrSet = new LdapAttributeSet
            {
                new LdapAttribute("name", "MyGroup"),
                new LdapAttribute("objectSid", "S-1-5-21"),
                new LdapAttribute("distinguishedName", "CN=MyGroup,OU=Groups,DC=homologa,DC=br"),
                new LdapAttribute("description", "My Desc")
            };
            var memberOfAttr = new LdapAttribute("memberOf");
            memberOfAttr.AddValue("CN=Parent,OU=Groups,DC=homologa,DC=br");
            attrSet.Add(memberOfAttr);
            var entry = new LdapEntry("CN=MyGroup,OU=Groups,DC=homologa,DC=br", attrSet);

            var group = (Group)InvokeNonPublic(manager, "ConvertfromLdap", entry, true);

            Assert.Equal("MyGroup", group.Name);
            Assert.Equal("CN=MyGroup,OU=Groups,DC=homologa,DC=br", group.DN);
            Assert.Equal("My Desc", group.Description);
        }

        [Fact]
        public void OUManager_GetAttributeSet_ContainsCoreFields()
        {
            var manager = OUManager.Instance;
            var ou = new OU
            {
                Name = "Infra",
                Description = "Infrastructure",
                DN = "OU=Infra,DC=homologa,DC=br"
            };

            var attrs = (LdapAttributeSet)InvokeNonPublic(manager, "GetAttributeSet", ou);

            Assert.Equal("Infra", attrs.GetAttribute("ou").StringValue);
            Assert.Equal("Infra", attrs.GetAttribute("name").StringValue);
            Assert.Equal("Infrastructure", attrs.GetAttribute("description").StringValue);
            Assert.Equal("OU=Infra,DC=homologa,DC=br", attrs.GetAttribute("distinguishedName").StringValue);
            Assert.Contains("organizationalUnit", attrs.GetAttribute("objectclass").StringValueArray);
        }

        [Fact]
        public void OUManager_ConvertFromLdap_MapsOuFields()
        {
            var manager = OUManager.Instance;
            var attrSet = new LdapAttributeSet
            {
                new LdapAttribute("name", "Infra"),
                new LdapAttribute("description", "Infrastructure"),
                new LdapAttribute("distinguishedName", "OU=Infra,DC=homologa,DC=br")
            };
            var entry = new LdapEntry("OU=Infra,DC=homologa,DC=br", attrSet);

            var ou = (OU)InvokeNonPublic(manager, "ConvertfromLdap", entry);

            Assert.Equal("Infra", ou.Name);
            Assert.Equal("Infrastructure", ou.Description);
            Assert.Equal("OU=Infra,DC=homologa,DC=br", ou.DN);
        }
    }
}
