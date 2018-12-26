using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using static adrapi.domain.LoggingEvents;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using adrapi.Ldap;
using Newtonsoft.Json;
using adrapi.Web;
using NLog;

namespace adrapi.Controllers
{
    //[Produces("application/json")]
    [Authorize(Policy = "Reading")]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [ApiController]
    public class GroupsController: BaseController
    {


        private IConfiguration configuration;

        public GroupsController(ILogger<GroupsController> logger, IConfiguration iConfig)
        {
      
            this._logger = logger;

            configuration = iConfig;
        }

        // GET api/groups
        [HttpGet]
        public ActionResult<IEnumerable<String>> Get([FromQuery]int _start, [FromQuery]int _end)
        {

            this.ProcessRequest();

            _logger.LogInformation(GetItem, "{1} listing all groups", requesterID);

            var gManager = GroupManager.Instance;

            if (_start == 0 && _end != 0)
            {
                return Conflict();
            }

            if (_start == 0 && _end == 0) 
            return gManager.GetList();
            else return gManager.GetList(_start, _end);


        }

        // GET api/groups 
        [HttpGet]
        public ActionResult<IEnumerable<domain.Group>> Get([RequiredFromQuery]bool _full, [FromQuery]int _start, [FromQuery]int _end)
        {
            if (_full)
            {
                this.ProcessRequest();

                _logger.LogInformation(GetItem, "{1} getting all groups objects", requesterID);

                if (_start == 0 && _end != 0)
                {
                    return Conflict();
                }

                var gManager = GroupManager.Instance;
                List<domain.Group> groups;

                //if (_start == 0 && _end == 0) 
                groups = gManager.GetGroups();
                //else groups = gManager.GetGroups(_start, _end);

                return groups;
            }
            else
            {
                return new List<domain.Group>();
            }
        }

        //TODO: BUG There is a bug related to doing a limited search then doing another one here... FIX IT! :-)
        // GET api/groups/CN=Convidados,CN=Builtin,DC=labesi,DC=fgv,DC=br
        [HttpGet("{DN}")]
        public ActionResult<domain.Group> Get(string DN)
        {
            var gManager = GroupManager.Instance;
            try
            {
                var group = gManager.GetGroup(DN);
                return group;
            }
            catch(Exception ex)
            {
                _logger.LogError("Error getting group ex:{0}", ex.Message);
                return null;
            }



        }
    }
}
