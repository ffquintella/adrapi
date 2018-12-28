using System;
using System.Collections.Generic;
using adrapi.domain;
using adrapi.Ldap;
using System.Linq;
using Novell.Directory.Ldap;
using System.Text;
using NLog;
using adrapi.Tools;

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

            var sMgmt = LdapQueryManager.Instance;

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

            var sMgmt = LdapQueryManager.Instance;

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

            var sMgmt = LdapQueryManager.Instance;

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

            var sMgmt = LdapQueryManager.Instance;

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
            var sMgmt = LdapQueryManager.Instance;

            try
            {
                var entry = sMgmt.GetRegister(DN);
                var user = ConvertfromLdap(entry);
                return user;
            }catch(LdapException ex)
            {
                logger.Debug("User not found {0} Ex: {1}", DN, ex.Message);
                return null;
            }

        }

        /// <summary>
        /// Creates the user on LDAP Directory.
        /// </summary>
        /// <returns> -1 Error </returns>
        /// <returns> 0 OK </returns>
        /// <param name="user">User.</param>
        public int CreateUser(User user)
        {

            //Creates the List attributes of the entry and add them to attributeset

            LdapAttributeSet attributeSet = GetAttributeSet(user);

            // DN of the entry to be added
            string dn = user.DN;

            LdapEntry newEntry = new LdapEntry(dn, attributeSet);


            var qMgmt = LdapQueryManager.Instance;

            try
            {
                qMgmt.AddEntry(newEntry);
                return 0;

            }catch(Exception ex)
            {
                logger.Error("Error saving user");
                logger.Log(LogLevel.Error, ex);
                return -1;
            }

        }

        /// <summary>
        /// Saves the user.
        /// </summary>
        /// <returns>The user. Must have DN set</returns>
        /// <param name="user">User.</param>
        public int SaveUser(User user)
        {

            var qMgmt = LdapQueryManager.Instance;

            var modList = new List<LdapModification>();

            var atributes = GetAttributeSet(user);

            //Get user from the Directory
            try
            {
                var duser = GetUser(user.DN);

                var dattrs = GetAttributeSet(duser);

   
                foreach (LdapAttribute attr in atributes)
                {
                    //TODO: Threat the userAccountControl
                    if (
                        attr.Name != "cn"
                        && attr.Name != "objectclass"
                        && attr.Name != "userAccountControl"
                      )
                    {

                        var b1 = attr.ByteValue;
                        var b2 = dattrs.GetAttribute(attr.Name).ByteValue;

                        var equal = ByteTools.Equality(b1, b2);
                                     
                        if (! equal)
                            modList.Add(new LdapModification(LdapModification.Replace, attr));
                    }

           
                }



                try
                {
                    qMgmt.SaveEntry(user.DN, modList.ToArray());
                    return 0;

                }
                catch (Exception ex)
                {
                    logger.Error("Error updating user");
                    logger.Log(LogLevel.Error, ex);
                    return -1;
                }

            }catch(Exception ex)
            {
                logger.Error("Error user not found");
                logger.Log(LogLevel.Error, ex);
                return -1;
            }


        }

        public bool ValidateAuthentication(string login, string password)
        {

            LdapConnectionManager lcm = LdapConnectionManager.Instance;

            return lcm.ValidateAuthentication(login, password);

        }


        private LdapAttributeSet GetAttributeSet(User user)
        {
            LdapAttributeSet attributeSet = new LdapAttributeSet();

            attributeSet.Add(new LdapAttribute("objectclass", new string[] { "top", "person", "organizationalPerson", "user" }));
            attributeSet.Add(new LdapAttribute("cn", new string[] { user.Login }));
            attributeSet.Add(new LdapAttribute("name", user.Name));
            attributeSet.Add(new LdapAttribute("sAMAccountName", user.Login));


            attributeSet.Add(new LdapAttribute("displayName", user.Name));
            attributeSet.Add(new LdapAttribute("description", user.Description));


            if (user.Password == null )
            {
                if(user.IsDisabled == null) user.IsDisabled = true;
            }
            else
            {
                if (user.IsDisabled == null) user.IsDisabled = false;
                var ldapCfg = new LdapConfig();
                if (ldapCfg.ssl == false)
                {
                    throw new domain.Exceptions.SSLRequiredException();
                }

                string quotePwd = String.Format(@"""{0}""", user.Password);
                byte[] encodedBytes = Encoding.Unicode.GetBytes(quotePwd);
                attributeSet.Add(new LdapAttribute("unicodePwd", encodedBytes));


            }

            attributeSet.Add(new LdapAttribute("userAccountControl", user.accountControl.ToString()));

            //attributeSet.Add(new LdapAttribute("givenname", "James"));
            //attributeSet.Add(new LdapAttribute("sn", "Smith"));
            //attributeSet.Add(new LdapAttribute("mail", "JSmith@Acme.com"));

            return attributeSet;
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
