using System;
using System.Collections.Generic;

namespace adrapi.domain
{
    public class User
    {

        private List<Group> _memberOf;


        public string Name { get; set; }
        public string Login { get; set; }
        public string Description { get; set; }
        public string ID { get; set; }
        public string DN { get; set; }

        public List<Group> MemberOf
        {
            get
            {
                if (_memberOf == null) _memberOf = new List<Group>();
                return _memberOf;
            }
            set
            {
                _memberOf = value;
            }
        }


        public User()
        {
        }


    }
}
