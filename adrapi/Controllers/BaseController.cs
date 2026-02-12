using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace adrapi.Controllers
{

    /// <summary>
    /// Shared controller base with request metadata extraction.
    /// </summary>
    public class BaseController: ControllerBase
    {
        protected string requesterID { get; set; }

        protected ILogger logger;

        protected IConfiguration configuration;

        /// <summary>
        /// Extracts the request key identifier from the <c>api-key</c> header.
        /// </summary>
        protected void ProcessRequest()
        {
            requesterID = this.Request.Headers["api-key"].ToString().Split(':')[0];
        }
    }
}
