using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using adrapi.Ldap;
using adrapi.Ldap.Security;
using Novell.Directory.Ldap;
using adrapi.Tools;
using NLog;
using Group = adrapi.domain.Group;

namespace adrapi
{
    public class GroupManager: ObjectManager
    {

        #region SINGLETON

        private static readonly Lazy<GroupManager> lazy = new Lazy<GroupManager>(() => new GroupManager());

        public static GroupManager Instance { get { return lazy.Value; } }

        private GroupManager()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
        }

        #endregion


        /// <summary>
        /// Return a string list of the groups CNs
        /// </summary>
        /// <returns>The list.</returns>
        public async Task<List<String>> GetCnListAsync()
        {
            var groups = new List<String>();

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;


            var resps = await sMgmt.ExecuteSearchAsync("", LdapSearchType.Group);

            foreach (var entry in resps)
            {
                groups.Add(entry.GetStringValueOrDefault("cn"));
                results++;
            }

            logger.Debug("Group search executed results:{result}", results);


            return groups;
        }
        
        /// <summary>
        /// Return a string list of the groups DNs
        /// </summary>
        /// <returns>The list.</returns>
        public async Task<List<String>> GetListAsync()
        {
            var groups = new List<String>();

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;


            var resps = await sMgmt.ExecuteSearchAsync("", LdapSearchType.Group);

            foreach (var entry in resps)
            {
                groups.Add(entry.GetStringValueOrDefault("distinguishedName"));
                results++;
            }

            logger.Debug("Group search executed results:{result}", results);


            return groups;
        }
        
        /// <summary>
        /// Gets the list, based on a start and end
        /// </summary>
        /// <returns>The list.</returns>
        /// <param name="start">Start.</param>
        /// <param name="end">End.</param>
        public async Task<List<String>> GetCnListAsync(int start, int end)
        {
            var groups = new List<String>();

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;


            var resps = await sMgmt.ExecuteLimitedSearchAsync("", LdapSearchType.Group, start, end);

            foreach (var entry in resps)
            {
                groups.Add(entry.GetStringValueOrDefault("cn"));
                results++;
            }

            logger.Debug("Group search executed results:{result}", results);


            return groups;
        }
        
        //TODO: Verify the lower limit witch is not working
        /// <summary>
        /// Gets the list, based on a start and end
        /// </summary>
        /// <returns>The list.</returns>
        /// <param name="start">Start.</param>
        /// <param name="end">End.</param>
        public async Task<List<String>> GetListAsync(int start, int end)
        {
            var groups = new List<String>();

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;


            var resps = await sMgmt.ExecuteLimitedSearchAsync("", LdapSearchType.Group, start, end);

            foreach (var entry in resps)
            {
                groups.Add(entry.GetStringValueOrDefault("distinguishedName"));
                results++;
            }

            logger.Debug("Group search executed results:{result}", results);


            return groups;
        }

        /// <summary>
        /// Gets the list of all groups.
        /// </summary>
        /// <returns>The users.</returns>
        public async Task<List<Group>> GetGroupsAsync()
        {

            var groups = new List<Group>();

            var sMgmt = LdapQueryManager.Instance;

            var resps = await sMgmt.ExecuteSearchAsync("", LdapSearchType.Group);
            int results = 0;

            foreach (var entry in resps)
            {
                groups.Add(ConvertfromLdap(entry));
                results++;
            }

            logger.Debug("Group search executed results:{result}", results);


            return groups;
        }

        /// <summary>
        /// Converts the data from the LDAP result
        /// </summary>
        /// <returns>The LDAP.</returns>
        /// <param name="entry">Entry.</param>
        private Group ConvertfromLdap(LdapEntry entry, Boolean _listCN = false)
        {
            var group = new Group();

            group.Name = entry.GetStringValueOrDefault("name");
            group.ID = entry.GetStringValueOrDefault("objectSid");
          
            if (entry.GetAttributeSet().ContainsKey("description")) group.Description = entry.GetStringValueOrDefault("description");

            //var sid = ConvertByteToStringSid((byte[])(Array)entry.GetAttribute("objectSid").ByteValue);

            //group.ID = sid;

            group.DN = entry.GetStringValueOrDefault("distinguishedName");


            if (entry.GetAttributeSet().ContainsKey("memberOf"))
            {
                var moff = entry.GetAttributeSet("memberOf");// ("memberOf").StringValues;

                foreach (var m in moff)
                {
                    var gmoff = "";
                    gmoff = m.StringValue;
                    group.MemberOf.Add(gmoff);
                }
            }

            if (entry.GetAttributeSet().ContainsKey("member"))
            {
                var set = entry.GetAttributeSet("member");

                foreach (var m in set)
                {
                    var gm = "";
                    gm = m.StringValue;
                    if (_listCN)
                    {
                        var regex = new Regex("^(?:CN=)(?<cn>[^,]+?)(?:,)");
                        var result = regex.Match(gm);
                        gm = result.Groups["cn"].Value;
                    }
                    group.Member.Add(gm);
                }
                
            }


            return group;
        }

        /// <summary>
        /// Gets the group.
        /// </summary>
        /// <returns>The user.</returns>
        /// <param name="DN">The Disitnguesh name of the group</param>
        /// <param name="_listCN">If true the members will only contain the CN</param>
        public async Task<Group> GetGroupAsync(string DN, Boolean _listCN = false, Boolean _searchByCN = false)
        {
            var sMgmt = LdapQueryManager.Instance;

            try
            {

                LdapEntry entry;
                if (!_searchByCN)
                {
                    entry = await sMgmt.GetRegister(DN); 
                }
                else
                {
                    var results = await sMgmt.ExecuteSearchAsync("", "(&(objectClass=group)(cn="+LdapInjectionControll.EscapeForSearchFilter(DN)+"))");


                    if (results.Count == 0)
                    {
                        logger.Debug("Group not found {0}", DN);
                        return null;
                    }
                    
                    entry = results.First();   
                }
                
                var group = ConvertfromLdap(entry, _listCN);
                return group;
            }
            catch (LdapException ex)
            {
                logger.Debug("Group not found {0} Ex: {1}", DN, ex.Message);
                return null;
            }

        }

        public async Task<int> CreateGroupAsync(Group group)
        {

            //Creates the List attributes of the entry and add them to attributeset

            LdapAttributeSet attributeSet = GetAttributeSet(group);

            // DN of the entry to be added
            string dn = group.DN;

            LdapEntry newEntry = new LdapEntry(dn, attributeSet);


            var qMgmt = LdapQueryManager.Instance;

            try
            {
                await qMgmt.AddEntryAsync(newEntry);
                return 0;

            }
            catch (Exception ex)
            {
                logger.Error("Error saving group");
                logger.Log(LogLevel.Error, ex);
                return -1;
            }

        }

        /// <summary>
        /// Saves the group.
        /// </summary>
        /// <returns>The group. Must have DN set</returns>
        /// <param name="group">Group.</param>
        /// <param name="_listCN">If true the members will only contain the CN</param>
        public async Task<int> SaveGroupAsync(Group group)
        {

            var qMgmt = LdapQueryManager.Instance;

            var modList = new List<LdapModification>();

            var atributes = GetAttributeSet(group);

            //Get user from the Directory
            try
            {
                var dgroup = await GetGroupAsync(group.DN);

                var dattrs = GetAttributeSet(dgroup);

                bool members_clean = false;

                foreach (LdapAttribute attr in atributes)
                {
                    if (
                        attr.Name != "cn"
                        && attr.Name != "objectclass"
                        && attr.Name != "member"
                      )
                    {

                        var b1 = attr.ByteValue;

                        var attribute = dattrs.GetAttribute(attr.Name);

                        bool equal = true;

                        if (attribute != null)
                        {
                            var b2 = attribute.ByteValue;
                            
                            equal = ByteTools.Equality(b1, b2);
                        }


                        if (!equal)
                            modList.Add(new LdapModification(LdapModification.Replace, attr));
                    }
                    else
                    {
                        if(attr.Name == "member")
                        {
                            if (!members_clean)
                            {
                                var dattr = dattrs.GetAttribute("member");

                                modList.Add(new LdapModification(LdapModification.Delete, dattr));

                                members_clean = true;
                            }


                            modList.Add(new LdapModification(LdapModification.Add, attr));
                        }
                    }


                }


                try
                {
                    await qMgmt.SaveEntry(group.DN, modList.ToArray());
                    return 0;

                }
                catch (Exception ex)
                {
                    logger.Error("Error updating group");
                    logger.Log(LogLevel.Error, ex);
                    return -1;
                }

            }
            catch (Exception ex)
            {
                logger.Error("Error group not found");
                logger.Log(LogLevel.Error, ex);
                return -1;
            }


        }

        private LdapAttributeSet GetAttributeSet(Group group)
        {
            LdapAttributeSet attributeSet = new LdapAttributeSet();

            attributeSet.Add(new LdapAttribute("objectclass", new string[] { "top", "group" }));
            attributeSet.Add(new LdapAttribute("name", group.Name));
            attributeSet.Add(new LdapAttribute("sAMAccountName", group.Name));
            attributeSet.Add(new LdapAttribute("cn", group.Name));
            attributeSet.Add(new LdapAttribute("description", group.Description));

            var amember = new LdapAttribute("member");

            foreach (String member in group.Member)
            {
                amember.AddValue(member);
            }

            attributeSet.Add(amember);

            return attributeSet;
        }


        public async Task<int> DeleteGroup(Group group)
        {


            var qMgmt = LdapQueryManager.Instance;

            try
            {
                await qMgmt.DeleteEntry(group.DN);
                return 0;

            }
            catch (Exception ex)
            {
                logger.Error("Error deleting group={group}", group.DN);
                logger.Log(LogLevel.Error, ex);
                return -1;
            }

        }

    }
}
