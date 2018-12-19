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
    public class UserManager
    {

        private NLog.Logger logger;

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
            var queue = sMgmt.SendSearch("", LdapSearchType.User);

            LdapMessage message;

            int results = 0;
            while ((message = queue.GetResponse()) != null)
            {
                if (message is LdapSearchResult)
                {
                    LdapEntry entry = ((LdapSearchResult)message).Entry;
                    users.Add(entry.GetAttribute("distinguishedName").StringValue);
                    results++;
                }
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

            var queue = sMgmt.SendSearch("", LdapSearchType.User);


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
        private string ConvertByteToStringSid(Byte[] sidBytes)
        {
            StringBuilder strSid = new StringBuilder();
            strSid.Append("S-");
            try
            {
                // Add SID revision.
                strSid.Append(sidBytes[0].ToString());
                // Next six bytes are SID authority value.
                if (sidBytes[6] != 0 || sidBytes[5] != 0)
                {
                    string strAuth = String.Format
                        ("0x{0:2x}{1:2x}{2:2x}{3:2x}{4:2x}{5:2x}",
                        (Int16)sidBytes[1],
                        (Int16)sidBytes[2],
                        (Int16)sidBytes[3],
                        (Int16)sidBytes[4],
                        (Int16)sidBytes[5],
                        (Int16)sidBytes[6]);
                    strSid.Append("-");
                    strSid.Append(strAuth);
                }
                else
                {
                    Int64 iVal = (Int32)(sidBytes[1]) +
                        (Int32)(sidBytes[2] << 8) +
                        (Int32)(sidBytes[3] << 16) +
                        (Int32)(sidBytes[4] << 24);
                    strSid.Append("-");
                    strSid.Append(iVal.ToString());
                }

                // Get sub authority count...
                int iSubCount = Convert.ToInt32(sidBytes[7]);
                int idxAuth = 0;
                for (int i = 0; i < iSubCount; i++)
                {
                    idxAuth = 8 + i * 4;
                    UInt32 iSubAuth = BitConverter.ToUInt32(sidBytes, idxAuth);
                    strSid.Append("-");
                    strSid.Append(iSubAuth.ToString());
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error converting the SID");
                //Trace.Warn(ex.Message);
                return "";
            }
            return strSid.ToString();
        }
    }
}
