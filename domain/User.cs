using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace adrapi.domain
{
    public class User
    {
        [Required]
        public string Name { get; set; }
        
        public string Login { get; set; }
        [Required]
        public string Account { get; set; }
        public string Description { get; set; }
        
        public string Mail { get; set; }
        public string Mobile { get; set; }
        public string ID { get; set; }
        public string DN { get; set; }

        public string Password { get; set; }


        public bool? IsDisabled { get; set; }
        public bool IsLocked { get; set; }
        public bool PasswordExpired { get; set; }

        public int accountControl {
            get
            {
                int val = 512;

                if (IsDisabled == true) val += 2;
                
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
