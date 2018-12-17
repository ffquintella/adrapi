using System.Collections.Generic;
using NLog;
using Newtonsoft.Json.Linq;
using System;
using adrapi.domain.Security;

namespace adrapi.Security
{


	/// <summary>
	/// A class representing a service ticket
	/// </summary>
	public class HttpSecurity
	{

		private static Logger logger = LogManager.GetCurrentClassLogger();

		public static bool ValidateKey(string apiKey, string IPAddress)
		{

			var result = false;

            logger.Trace("Validating API Key. key={0}", apiKey);


			ApiKey key = ApiKeyManager.FindBySecretKey(apiKey);

			if (key != null)
			{
                logger.Debug("Key found: Key={0} IP={1} RequestIP={2}", key.secretKey, key.authorizedIP, IPAddress);
				if (IPAddress == key.authorizedIP)
				{
					logger.Debug("IP authorized");
					result = true;
				}
				else
				{
                    logger.Warn("IP address not authorized IP={0}", IPAddress);
				}
			}

			logger.Trace("End of validation API Key.");
			return result;
		}

		public static string getKeyID(string apiKey)
		{

			string result = "";

			ApiKey key = ApiKeyManager.FindBySecretKey(apiKey);

			if (key != null)
			{
				logger.Debug("Key found: Key={0} ", key.secretKey);
				result = key.keyID;
			}

			return result;
		}

        public static ApiKey getKey(string apiKeyID)
		{

			ApiKey key = ApiKeyManager.Find(apiKeyID);

			if (key != null)
			{
                logger.Debug("Key found: Key={0} ", key.keyID);
                return key;
			}

            return null;
		}

		public static List<string> getClaims(string apiKey)
		{

			ApiKey key = ApiKeyManager.FindBySecretKey(apiKey);

			if (key != null)
			{
				return key.claims;
			}

			return null;
		}

	}
}

