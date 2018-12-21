using System;
using System.Collections.Generic;
using adrapi.domain;
using adrapi.Ldap;
using System.Linq;
using Novell.Directory.Ldap;
using System.Text;
using NLog;

namespace adrapi
{
    public class UserManager: ObjectManager
    {

        #region SINGLETON

        private static readonly Lazy<UserManager> lazy = new Lazy<UserManager>(() => new UserManager());

        public static UserManager Instance { get { return lazy.Value; } }

        private UserManager()
        {
            logger = NLog.LogManager.GetCurrentClassLogger();
        }

        #endregion

        /// <summary>
        /// Return a string list of the users DNs
        /// </summary>
        /// <returns>The list.</returns>
        public List<String> GetList()
        {
            var users = new List<String>();

            var sMgmt = LdapSearchManager.Instance;

            int results = 0;

            /*
            var queue = sMgmt.SendSearch("", LdapSearchType.User);

            LdapMessage message;


            while ((message = queue.GetResponse()) != null)
            {
                if (message is LdapSearchResult)
                {
                    LdapEntry entry = ((LdapSearchResult)message).Entry;
                    users.Add(entry.GetAttribute("distinguishedName").StringValue);
                    results++;
                }
            }
            */

            var resps = sMgmt.ExecutePagedSearch("", LdapSearchType.User);

            foreach(var entry in resps)
            {
                users.Add(entry.GetAttribute("distinguishedName").StringValue);
                results++;
            }

            logger.Debug("User search executed results:{result}", results);


            return users;
        }

        //TODO: Verify the lower limit witch is not working

        /// <summary>
        /// Gets the list. Limited to a start and end number based on the total colection sorted by the name
        /// </summary>
        /// <returns>The list.</returns>
        /// <param name="start">Start.</param>
        /// <param name="end">End.</param>
        public List<String> GetList(int start, int end)
        {
            var users = new List<String>();

            var sMgmt = LdapSearchManager.Instance;

            int results = 0;


            var resps = sMgmt.ExecuteLimitedSearch("", LdapSearchType.User, start, end);

            foreach (var entry in resps)
            {
                users.Add(entry.GetAttribute("distinguishedName").StringValue);
                results++;
            }

            logger.Debug("User search executed results:{result}", results);


            return users;
        }

        /// <summary>
        /// Gets the list of all users.
        /// </summary>
        /// <returns>The users.</returns>
        public List<User> GetUsers()
        {

            var users = new List<User>();

            var sMgmt = LdapSearchManager.Instance;

            /*var queue = sMgmt.SendSearch("", LdapSearchType.User);


            LdapMessage message;

            int results = 0;
            while((message = queue.GetResponse()) != null)
            {
                if (message is LdapSearchResult)
                {
                    LdapEntry entry = ((LdapSearchResult)message).Entry;
                    users.Add(ConvertfromLdap(entry));
                    results++;
                }
            }*/

            var resps = sMgmt.ExecutePagedSearch("", LdapSearchType.User);
            int results = 0;

            foreach (var entry in resps)
            {
                users.Add(ConvertfromLdap(entry));
                results++;
            }

            logger.Debug("User search executed results:{result}", results);


            return users;
        }


        public List<User> GetUsers(int start, int end)
        {
            var users = new List<User>();

            var sMgmt = LdapSearchManager.Instance;

            int results = 0;


            var resps = sMgmt.ExecuteLimitedSearch("", LdapSearchType.User, start, end);

            foreach (var entry in resps)
            {
                users.Add(ConvertfromLdap(entry));
                results++;
            }

            logger.Debug("User search executed results:{result}", results);


            return users;
        }

        /// <summary>
        /// Gets the user.
        /// </summary>
        /// <returns>The user.</returns>
        /// <param name="DN">The Disitnguesh name of the user</param>
        public User GetUser (string DN)
        {
            var sMgmt = LdapSearchManager.Instance;

            var entry = sMgmt.GetRegister(DN);

            var user = ConvertfromLdap(entry);
            return user;
        }

        private User ConvertfromLdap(LdapEntry entry)
        {
            var user = new User();

            user.Name = entry.GetAttribute("name").StringValue;
            user.Login = entry.GetAttribute("sAMAccountName").StringValue;

            if(entry.GetAttribute("description") != null) user.Description = entry.GetAttribute("description").StringValue;

            var sid = ConvertByteToStringSid((byte[])(Array)entry.GetAttribute("objectSid").ByteValue);

            user.ID = sid;

            user.DN = entry.GetAttribute("distinguishedName").StringValue;
           

            if (entry.GetAttribute("memberOf") != null)
            {
                var moff = entry.GetAttribute("memberOf").StringValues;

                while (moff.MoveNext())
                {
                    var group = new Group();
                    if (moff != null && moff.Current != null)
                        group.DN = moff.Current;
                    user.MemberOf.Add(group);
                }
            }


            return user;
        }

    }
}
