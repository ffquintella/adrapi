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

        public string Password { get; set; }


        public Boolean IsDisabled { get; set; }
        public Boolean IsLocked { get; set; }
        public Boolean PasswordExpired { get; set; }

        public int accountControl {
            get
            {
                int val = 512;

                if (IsDisabled) val += 2;
                if (IsLocked) val += 16;
                if (PasswordExpired) val += 8388608;


                return val;
            }
        }

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
