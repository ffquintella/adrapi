using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace adrapi.Controllers
{

    public class BaseController: ControllerBase
    {
        protected string requesterID { get; set; }

        protected ILogger logger;

        protected IConfiguration configuration;

        public void ProcessRequest()
        {
            requesterID = this.Request.Headers["api-key"].ToString().Split(':')[0];
        }
    }
}
