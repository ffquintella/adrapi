using System;
using System.IO;
using NLog;
using Newtonsoft.Json;
using System.Collections.Generic;
using adrapi.domain.Security;

namespace adrapi.Security
{
	public static class ApiKeyManager
	{

		private static Logger logger = LogManager.GetCurrentClassLogger();

		public static ApiKey FindBySecretKey(string secretKey, bool isTest = false)
		{
			string json = "";
			if (isTest) json = File.ReadAllText("security-tests.json");
			else json = File.ReadAllText("security.json");

			List<ApiKey> keys = JsonConvert.DeserializeObject<List<ApiKey>>(json);
			logger.Debug("Loaded {count} API key(s) from store.", keys?.Count ?? 0);

			foreach (ApiKey key in keys)
			{
				if (key.secretKey == secretKey) return key;
			}

			return null;
		}

		public static ApiKey Find(string keyID, bool isTest = false)
		{

			string json = "";
			if (isTest) json = File.ReadAllText("security-tests.json");
			else json = File.ReadAllText("security.json");

			List<ApiKey> keys = JsonConvert.DeserializeObject<List<ApiKey>>(json);
			logger.Debug("Loaded {count} API key(s) from store.", keys?.Count ?? 0);

			foreach (ApiKey key in keys)
			{
                if (key.keyID == keyID) return key;
			}

			return null;
		}
	}
}
