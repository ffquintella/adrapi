using System;
using System.Collections.Generic;
using adrapi.Ldap;
using adrapi.domain;
using Novell.Directory.Ldap;
using adrapi.Tools;
using NLog;

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
        /// Return a string list of the groups DNs
        /// </summary>
        /// <returns>The list.</returns>
        public List<String> GetList()
        {
            var groups = new List<String>();

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;


            var resps = sMgmt.ExecutePagedSearch("", LdapSearchType.Group);

            foreach (var entry in resps)
            {
                groups.Add(entry.GetAttribute("distinguishedName").StringValue);
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
        public List<String> GetList(int start, int end)
        {
            var groups = new List<String>();

            var sMgmt = LdapQueryManager.Instance;

            int results = 0;


            var resps = sMgmt.ExecuteLimitedSearch("", LdapSearchType.Group, start, end);

            foreach (var entry in resps)
            {
                groups.Add(entry.GetAttribute("distinguishedName").StringValue);
                results++;
            }

            logger.Debug("Group search executed results:{result}", results);


            return groups;
        }

        /// <summary>
        /// Gets the list of all groups.
        /// </summary>
        /// <returns>The users.</returns>
        public List<Group> GetGroups()
        {

            var groups = new List<Group>();

            var sMgmt = LdapQueryManager.Instance;

            var resps = sMgmt.ExecutePagedSearch("", LdapSearchType.Group);
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
        private Group ConvertfromLdap(LdapEntry entry)
        {
            var group = new Group();

            group.Name = entry.GetAttribute("name").StringValue;
          
            if (entry.GetAttribute("description") != null) group.Description = entry.GetAttribute("description").StringValue;

            //var sid = ConvertByteToStringSid((byte[])(Array)entry.GetAttribute("objectSid").ByteValue);

            //group.ID = sid;

            group.DN = entry.GetAttribute("distinguishedName").StringValue;


            if (entry.GetAttribute("memberOf") != null)
            {
                var moff = entry.GetAttribute("memberOf").StringValues;

                while (moff.MoveNext())
                {
                    String gmoff = "";
                    if (moff != null && moff.Current != null)
                        gmoff = moff.Current;
                    group.MemberOf.Add(gmoff);
                }
            }

            if (entry.GetAttribute("member") != null)
            {
                var m = entry.GetAttribute("member").StringValues;

                while (m.MoveNext())
                {
                    String member = "";
                    if (m != null && m.Current != null)
                    {
                        member = m.Current;
                        group.Member.Add(member);
                    }
                }
            }


            return group;
        }

        /// <summary>
        /// Gets the group.
        /// </summary>
        /// <returns>The user.</returns>
        /// <param name="DN">The Disitnguesh name of the group</param>
        public Group GetGroup(string DN)
        {
            var sMgmt = LdapQueryManager.Instance;

            try
            {
                var entry = sMgmt.GetRegister(DN);
                var group = ConvertfromLdap(entry);
                return group;
            }
            catch (LdapException ex)
            {
                logger.Debug("User not found {0} Ex: {1}", DN, ex.Message);
                return null;
            }

        }

        public int CreateGroup(Group group)
        {

            //Creates the List attributes of the entry and add them to attributeset

            LdapAttributeSet attributeSet = GetAttributeSet(group);

            // DN of the entry to be added
            string dn = group.DN;

            LdapEntry newEntry = new LdapEntry(dn, attributeSet);


            var qMgmt = LdapQueryManager.Instance;

            try
            {
                qMgmt.AddEntry(newEntry);
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
        public int SaveGroup(Group group)
        {

            var qMgmt = LdapQueryManager.Instance;

            var modList = new List<LdapModification>();

            var atributes = GetAttributeSet(group);

            //Get user from the Directory
            try
            {
                var dgroup = GetGroup(group.DN);

                var dattrs = GetAttributeSet(dgroup);


                foreach (LdapAttribute attr in atributes)
                {
                    if (
                        attr.Name != "cn"
                        && attr.Name != "objectclass"
                      )
                    {

                        var b1 = attr.ByteValue;
                        var b2 = dattrs.GetAttribute(attr.Name).ByteValue;

                        var equal = ByteTools.Equality(b1, b2);

                        if (!equal)
                            modList.Add(new LdapModification(LdapModification.Replace, attr));
                    }


                }


                try
                {
                    qMgmt.SaveEntry(group.DN, modList.ToArray());
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
                    
            foreach(String member in group.Member)
            {
                attributeSet.Add(new LdapAttribute("member", member));
            }


            return attributeSet;
        }

    }
}
