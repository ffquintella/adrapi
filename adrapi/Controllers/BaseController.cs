using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace adrapi.Controllers
{

    public class BaseController: ControllerBase
    {
        protected string requesterID { get; set; }

        protected ILogger _logger;

        public void ProcessRequest()
        {
            requesterID = this.Request.Headers["api-key"].ToString().Split(':')[0];
        }
    }
}
