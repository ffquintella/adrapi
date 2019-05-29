using System;
using System.Text;

namespace adrapi.Ldap.Security
{
    public static class LdapInjectionControll
    {

        /// <summary>
        /// Escape a string for usage in an LDAP DN to prevent LDAP injection attacks.
        /// There are certain characters that are considered special characters in a DN.
        /// The exhaustive list is the following: ',','\','#','+','<','>',';','"','=', and leading or trailing spaces
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string EscapeForDN(string name)
        {
            StringBuilder sb = new StringBuilder();

            if (name.Length > 0 && ((name[0] == ' ') || (name[0] == '#')))
            {
                sb.Append('\\'); // add the leading backslash if needed
            }

            for (int i = 0; i < name.Length; i++)
            {
                char curChar = name[i];
                switch (curChar)
                {
                    case '\\':
                        sb.Append(@"\\");
                        break;
                    case ',':
                        sb.Append(@"\,");
                        break;
                    case '+':
                        sb.Append(@"\+");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '<':
                        sb.Append(@"\<");
                        break;
                    case '>':
                        sb.Append(@"\>");
                        break;
                    case ';':
                        sb.Append(@"\;");
                        break;
                    default:
                        sb.Append(curChar);
                        break;
                }
            }

            if (name.Length > 1 && name[name.Length - 1] == ' ')
            {
                sb.Insert(sb.Length - 1, '\\'); // add the trailing backslash if needed
            }

            return sb.ToString();
        }

        /// <summary>
        /// Escape a string for usage in an LDAP DN to prevent LDAP injection attacks.
        /// </summary>
        public static string EscapeForSearchFilter(string filter)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < filter.Length; i++)
            {
                char curChar = filter[i];
                switch (curChar)
                {
                    case '\\':
                        sb.Append("\\5c");
                        break;
                    case '*':
                        sb.Append("\\2a");
                        break;
                    case '(':
                        sb.Append("\\28");
                        break;
                    case ')':
                        sb.Append("\\29");
                        break;
                    case '\u0000':
                        sb.Append("\\00");
                        break;
                    default:
                        sb.Append(curChar);
                        break;
                }
            }
            return sb.ToString();
        }
        
        /// <summary>
        /// Escape a string for usage in an LDAP DN to prevent LDAP injection attacks.
        /// </summary>
        public static string EscapeForSearchFilterAllowWC(string filter)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < filter.Length; i++)
            {
                char curChar = filter[i];
                switch (curChar)
                {
                    case '\\':
                        sb.Append("\\5c");
                        break;
                    case '(':
                        sb.Append("\\28");
                        break;
                    case ')':
                        sb.Append("\\29");
                        break;
                    case '\u0000':
                        sb.Append("\\00");
                        break;
                    default:
                        sb.Append(curChar);
                        break;
                }
            }
            return sb.ToString();
        }
        
    }
}
