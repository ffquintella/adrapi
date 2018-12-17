using System;
using System.Collections.Generic;

namespace adrapi.domain.Security
{
	public class ApiKey
	{
		public string secretKey { get; set; }
		public string keyID { get; set; }
		public string authorizedIP { get; set; }
		public List<string> claims { get; set; }

		public ApiKey()
		{
		}
	}
}
