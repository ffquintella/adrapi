using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace adrapi.domain
{
    public class Group
    {
        public Group()
        {
        }

        [Required]
        public string Name { get; set; }
        public string Description { get; set; }


        public string DN { get; set; }
        public string ID { get; set; }

        /// <summary>
        /// Backend-agnostic group kind. Null/"Security" is a security group;
        /// "Microsoft365" (aka "Unified"/"Office365") is a Microsoft 365 group.
        /// Meaningful for the Entra ID/Graph backend; ignored by LDAP/AD.
        /// </summary>
        public string GroupType { get; set; }

        private List<String> _member;
        public List<String> Member
        {
            get
            {
                if (_member == null) _member = new List<String>();
                return _member;
            }
            set
            {
                _member = value;
            }
        }

        private List<String> _memberOf;
        public List<String> MemberOf
        {
            get
            {
                if (_memberOf == null) _memberOf = new List<String>();
                return _memberOf;
            }
            set
            {
                _memberOf = value;
            }
        }
    }
}
