using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace adrapi.domain
{
    public class User
    {
        [Required]
        public string Name { get; set; }
        [Required]
        public string Login { get; set; }
        public string Description { get; set; }
        public string ID { get; set; }
        public string DN { get; set; }




        private List<Group> _memberOf;
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
