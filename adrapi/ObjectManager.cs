using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using adrapi.Ldap;
using Novell.Directory.Ldap;
namespace adrapi
{
    public class ObjectManager
    {
        public ObjectManager()
        {
        }

        protected NLog.Logger logger;

        /// <summary>
        /// Directory access used by every manager. Defaults to the production
        /// singletons; tests swap in fakes so manager logic can be exercised
        /// without a live directory (see the <c>LdapFakeScope</c> fixture, which
        /// always restores the defaults).
        /// </summary>
        internal static ILdapQueryManager QueryManagerOverride { get; set; }

        internal static ILdapAuthenticator AuthenticatorOverride { get; set; }

        protected static ILdapQueryManager Query => QueryManagerOverride ?? LdapQueryManagerAdapter.Instance;

        protected static ILdapAuthenticator Authenticator => AuthenticatorOverride ?? LdapConnectionAuthenticator.Instance;

        /// <summary>
        /// Reads every value of an attribute by *name*, tolerating the ranged
        /// windows Active Directory returns for large multi-valued attributes
        /// (<c>member;range=0-1499</c>). <see cref="LdapEntry.GetAttributeSet(string)"/>
        /// cannot be used for this: it matches attribute subtypes, not names,
        /// so it returns nothing for a plain <c>member</c> attribute.
        /// </summary>
        protected static List<string> GetAttributeStringValues(LdapEntry entry, string attributeName)
        {
            if (entry == null || string.IsNullOrWhiteSpace(attributeName))
            {
                return new List<string>();
            }

            var values = new List<string>();
            foreach (LdapAttribute attribute in entry.GetAttributeSet())
            {
                var key = attribute.Name;
                if (attribute == null || string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!string.Equals(key, attributeName, StringComparison.OrdinalIgnoreCase)
                    && !TryParseRangeAttributeName(key, attributeName, out _, out _, out _))
                {
                    continue;
                }

                if (attribute.StringValueArray != null && attribute.StringValueArray.Length > 0)
                {
                    values.AddRange(attribute.StringValueArray.Where(v => !string.IsNullOrWhiteSpace(v)));
                }
                else if (!string.IsNullOrWhiteSpace(attribute.StringValue))
                {
                    values.Add(attribute.StringValue);
                }
            }

            return values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        protected static bool TryParseRangeAttributeName(string attributeKey, string baseAttributeName, out int start, out int end, out bool terminal)
        {
            start = -1;
            end = -1;
            terminal = false;

            if (string.IsNullOrWhiteSpace(attributeKey) || string.IsNullOrWhiteSpace(baseAttributeName))
            {
                return false;
            }

            var prefix = $"{baseAttributeName};range=";
            if (!attributeKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rangePart = attributeKey.Substring(prefix.Length);
            var separatorIndex = rangePart.IndexOf('-');
            if (separatorIndex <= 0 || separatorIndex >= rangePart.Length - 1)
            {
                return false;
            }

            var startPart = rangePart.Substring(0, separatorIndex);
            var endPart = rangePart.Substring(separatorIndex + 1);

            if (!int.TryParse(startPart, out start))
            {
                return false;
            }

            if (endPart == "*")
            {
                terminal = true;
                end = int.MaxValue;
                return true;
            }

            if (!int.TryParse(endPart, out end))
            {
                return false;
            }

            return true;
        }

        protected string ConvertByteToStringSid(Byte[] sidBytes)
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
