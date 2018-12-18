using System;
using Microsoft.AspNetCore.Mvc;

namespace adrapi.Controllers
{

    public class BaseController: ControllerBase
    {
        protected string requesterID { get; set; }


        public void ProcessRequest()
        {
            requesterID = this.Request.Headers["api-key"].ToString().Split(':')[0];
        }
    }
}
