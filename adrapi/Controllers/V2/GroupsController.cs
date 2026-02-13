using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using static adrapi.domain.LoggingEvents;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using adrapi.Ldap;
using Newtonsoft.Json;
using adrapi.Web;
using NLog;
using adrapi.domain;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using adrapi.Tools;
using adrapi.Models;

namespace adrapi.Controllers.V2
{
    [Produces("application/json")]
    [Authorize(Policy = "Reading")]
    [ApiVersion( "2.0" )]
    [Route("api/[controller]")]
    [ApiController]
    public class GroupsController: BaseController
    {


        public GroupsController(ILogger<GroupsController> logger, IConfiguration iConfig)
        {
      
            this.logger = logger;

            configuration = iConfig;
        }

        #region GET
        // GET api/groups
        [HttpGet]
        public async Task<ActionResult<IEnumerable<String>>> Get([FromQuery]int _start, [FromQuery]int _end)
        {

            this.ProcessRequest();

            logger.LogDebug(GetItem, "{0} listing all groups", requesterID);

            var gManager = GroupManager.Instance;

            if (_start == 0 && _end != 0)
            {
                return Conflict();
            }

            if (_start == 0 && _end == 0) 
            return await gManager.GetCnListAsync();
            else return await gManager.GetCnListAsync(_start, _end);


        }

        // GET api/groups 
        [HttpGet]
        public async Task<ActionResult<IEnumerable<domain.Group>>> Get([RequiredFromQuery]bool _full, [FromQuery]int _start, [FromQuery]int _end)
        {
            if (_full)
            {
                this.ProcessRequest();

                logger.LogDebug(ListItems, "{0} getting all groups objects", requesterID);

                if (_start == 0 && _end != 0)
                {
                    return Conflict();
                }

                var gManager = GroupManager.Instance;
                List<domain.Group> groups;

                //if (_start == 0 && _end == 0) 
                groups = await gManager.GetGroupsAsync();
                //else groups = gManager.GetGroups(_start, _end);

                return groups;
            }
            else
            {
                return new List<domain.Group>();
            }
        }
        
        // GET api/groups/:group
        [HttpGet("{groupId}")]
        public async Task<ActionResult<domain.Group>> Get(string groupId)
        {
            this.ProcessRequest();

            if (string.IsNullOrWhiteSpace(groupId))
            {
                return BadRequest();
            }

            var gManager = GroupManager.Instance;
            try
            {
                // Try DN first (v2 contract), then fallback to CN for backward compatibility.
                var group = await gManager.GetGroupAsync(groupId, true);
                if (group == null)
                {
                    group = await gManager.GetGroupAsync(groupId, true, true);
                }

                if (group == null)
                {
                    return NotFound();
                }

                logger.LogDebug(GetItem, "Getting Group={0}", group.Name);
                return group;
            }
            catch(Exception ex)
            {
                logger.LogError(GetItem, "Error getting group ex:{0}", ex.Message);
                return this.StatusCode(500);
            }



        }

        // GET api/groups/:group/exists
        [HttpGet("{DN}/exists")]
        public async Task<IActionResult> GetExists(string DN)
        {
            this.ProcessRequest();

            if (!IsValidGroupDn(DN))
            {
                return Conflict();
            }

            var gManager = GroupManager.Instance;

            try
            {
                logger.LogDebug(ItemExists, "Group DN={dn} found", DN);
                var group = await gManager.GetGroupAsync(DN);

                if (group == null)
                {
                    logger.LogDebug(ItemExists, "Group DN={dn} not found.", DN);
                    return NotFound();
                }
                
                return Ok();

            }
            catch (Exception ex)
            {
                logger.LogError(ItemExists, "Error checking group DN={dn}. err:" + ex.Message, DN);
                return this.StatusCode(500);
            }

        }



        // GET api/groups/:group/members
        [HttpGet("{groupId}/members")]
        public async Task<ActionResult<List<String>>> GetMembers(string groupId, [FromQuery]Boolean _listCN = true)
        {
            this.ProcessRequest();

            if (string.IsNullOrWhiteSpace(groupId))
            {
                return BadRequest();
            }

            var gManager = GroupManager.Instance;

            try
            {
                logger.LogDebug(ListItems, "Group DN={dn} found", groupId);
                var group = await gManager.GetGroupAsync(groupId, _listCN);
                if (group == null)
                {
                    group = await gManager.GetGroupAsync(groupId, _listCN, true);
                }

                if (group == null)
                {
                    return NotFound();
                }

                return group.Member;

            }
            catch (Exception ex)
            {
                logger.LogError(ListItems, "Error listing members for group={groupId}. err:" + ex.Message, groupId);
                return this.StatusCode(500);
            }

        }
        #endregion

        #region POST
        // POST api/groups
        [Authorize(Policy = "Writting")]
        [HttpPost]
        public async Task<ActionResult> Post([FromBody] GroupCreateRequest request, [FromQuery] Boolean _listCN = false)
        {
            ProcessRequest();

            if (!ModelState.IsValid || request == null || string.IsNullOrWhiteSpace(request.DN))
            {
                return BadRequest();
            }

            if (!IsValidGroupDn(request.DN))
            {
                return Conflict();
            }

            if (!TryExtractGroupName(request.DN, out var groupNameFromDn))
            {
                return Conflict();
            }

            if (!string.Equals(groupNameFromDn, request.Name, System.StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError(PutItem, "Group name and DN CN mismatch name={name} DN={DN}", request.Name, request.DN);
                return Conflict();
            }

            var gManager = GroupManager.Instance;
            if (await HasConflictingGroupAsync(request.DN, request.Name))
            {
                return Conflict();
            }

            var members = await ResolveMembersAsync(request.Members, request.DN, _listCN);
            if (members == null)
            {
                return this.StatusCode(422);
            }

            var group = new domain.Group
            {
                DN = request.DN,
                Name = request.Name,
                Description = request.Description,
                Member = members
            };

            LogAudit("group.create.request", request.DN, $"membersCount={members.Count}");
            var result = await gManager.CreateGroupAsync(group);
            if (result == 0)
            {
                LogAudit("group.create.success", request.DN, $"membersCount={members.Count}");
            }
            return result == 0 ? Ok() : this.StatusCode(500);
        }
        #endregion

        #region PUT
        // PUT api/groups/:group
        /// <summary>
        /// Creates the specified group.
        /// </summary>
        /// <returns>The put.</returns>
        /// <param name="group">Group.</param>
        /// <param name="_listCN">If the members are in CN format</param>
        [Authorize(Policy = "Writting")]
        [HttpPut("{DN}")]
        public async Task<ActionResult> Put(string DN, [FromBody] domain.Group group, [FromQuery] Boolean _listCN = false)
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to create group:{0}", DN);

            if (ModelState.IsValid && group != null)
            {
                if (group.DN != null && group.DN != DN)
                {
                    logger.LogError(PutItem, "Group DN different of the URL DN in put request group.DN={0} DN={1}", group.DN, DN);
                    return Conflict();
                }

                if (!TryExtractGroupName(DN, out var groupNameFromDn))
                {
                    logger.LogError(PutItem, "DN is not correcly formated  DN={0}", DN);
                    return Conflict();
                }

                var gManager = GroupManager.Instance;
                var adgroup = await gManager.GetGroupAsync(DN);

                if (!string.Equals(groupNameFromDn, group.Name, System.StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogError(PutItem, "Group name and DN CN mismatch name={name} DN={DN}", group.Name, DN);
                    return Conflict();
                }

                if (adgroup == null && await HasConflictingGroupAsync(DN, group.Name))
                {
                    return Conflict();
                }

                var resolvedMembers = await ResolveMembersAsync(group.Member, DN, _listCN);
                if (resolvedMembers == null)
                {
                    return this.StatusCode(422);
                }

                group.Member = resolvedMembers;
                group.DN = DN;

                if (adgroup == null)
                {
                    // New Group
                    logger.LogInformation(InsertItem, "Creating group DN={DN}", DN);

                    LogAudit("group.create.request", DN, $"membersCount={group.Member.Count}");
                    var result = await gManager.CreateGroupAsync(group);
                    if (result == 0)
                    {
                        LogAudit("group.create.success", DN, $"membersCount={group.Member.Count}");
                    }

                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }
                else
                {
                    // Update 
                    logger.LogInformation(UpdateItem, "Updating group DN={DN}", DN);

                    LogAudit("group.update.request", DN, $"membersCount={group.Member.Count}");
                    var result = await gManager.SaveGroupAsync(group);
                    if (result == 0)
                    {
                        LogAudit("group.update.success", DN, $"membersCount={group.Member.Count}");
                    }
                    if (result == 0) return Ok();
                    else return this.StatusCode(500);

                }



            }
            else
            {
                return BadRequest();
            }

            //return Conflict();
        }



        /// <summary>
        /// Update the member of a single group
        /// </summary>
        /// <param name="DN">The DN of the group</param>
        /// <param name="members">The list of members or in DN or CN format</param>
        /// <param name="_listCN">If the list is in CN format</param>
        /// <returns></returns>
        // PUT api/groups/:group/members
        [Authorize(Policy = "Writting")]
        [HttpPut("{DN}/members")]
        public async Task<ActionResult> PutMembers(string DN, [FromBody] String[] members, [FromQuery] Boolean _listCN = false)
        {
            this.ProcessRequest();
            var gManager = GroupManager.Instance;
            
            if (!IsValidGroupDn(DN))
            {
                return Conflict();
            }

            logger.LogDebug(ListItems, "Group DN={dn} found", DN);
            var group = await gManager.GetGroupAsync(DN);
            if (group == null)
            {
                return NotFound();
            }

            var resolvedMembers = await ResolveMembersAsync(members, DN, _listCN);
            if (resolvedMembers == null)
            {
                return this.StatusCode(422);
            }
            group.Member = resolvedMembers;

            try
            {
                logger.LogInformation(PutItem, "Saving group members for group:{DN}", DN);
                LogAudit("group.members.replace.request", DN, $"membersCount={group.Member.Count}");
                await gManager.SaveGroupAsync(group);
                LogAudit("group.members.replace.success", DN, $"membersCount={group.Member.Count}");
                return Ok();
            }
            catch(Exception ex)
            {
                logger.LogError(InternalError, "Error saving DN={dn} EX: {message}", DN, ex.Message);
                return this.StatusCode(500);
            }

            //return group.Member;

        }

        // PATCH api/groups/:group/members
        [Authorize(Policy = "Writting")]
        [HttpPatch("{DN}/members")]
        public async Task<ActionResult> PatchMembers(string DN, [FromBody] GroupMembersPatchRequest request, [FromQuery] Boolean _listCN = false)
        {
            this.ProcessRequest();
            var gManager = GroupManager.Instance;

            if (request == null)
            {
                return BadRequest();
            }

            if (!IsValidGroupDn(DN))
            {
                return Conflict();
            }

            var addList = request.Add ?? new List<string>();
            var removeList = request.Remove ?? new List<string>();
            if (addList.Count == 0 && removeList.Count == 0)
            {
                return BadRequest();
            }

            var group = await gManager.GetGroupAsync(DN);
            if (group == null)
            {
                return NotFound();
            }

            var addDns = await ResolveMembersAsync(addList, DN, _listCN);
            if (addDns == null)
            {
                return this.StatusCode(422);
            }

            var removeDns = await ResolveMembersAsync(removeList, DN, _listCN);
            if (removeDns == null)
            {
                return this.StatusCode(422);
            }

            var members = new HashSet<string>(group.Member, System.StringComparer.OrdinalIgnoreCase);
            foreach (var memberDn in addDns)
            {
                members.Add(memberDn);
            }

            foreach (var memberDn in removeDns)
            {
                members.Remove(memberDn);
            }

            group.Member = members.ToList();
            LogAudit("group.members.patch.request", DN, $"add={addDns.Count};remove={removeDns.Count};result={group.Member.Count}");
            var result = await gManager.SaveGroupAsync(group);
            if (result == 0)
            {
                LogAudit("group.members.patch.success", DN, $"add={addDns.Count};remove={removeDns.Count};result={group.Member.Count}");
            }
            return result == 0 ? Ok() : this.StatusCode(500);
        }

        #endregion

        #region DELETE

        /// <summary>
        /// Delete the specified DN.
        /// </summary>
        /// <response code="200">Deleted Ok</response>
        /// <response code="404">User not found</response>
        /// <response code="500">Internal Server error</response>
        [Authorize(Policy = "Writting")]
        [HttpDelete("{DN}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public async Task<ActionResult> Delete(string DN)
        {
            ProcessRequest();

            logger.LogDebug(PutItem, "Tring to delete group:{0}", DN);

            Regex regex = new Regex(@"\Acn=(?<login>[^,]+?),", RegexOptions.IgnoreCase);

            Match match = regex.Match(DN);

            if (!match.Success)
            {
                logger.LogError(PutItem, "DN is not correcly formated  DN={0}", DN);
                return Conflict();
            }

            var gManager = GroupManager.Instance;

            var dgroup = await gManager.GetGroupAsync(DN);

            if (dgroup == null)
            {
                // No User
                logger.LogError(DeleteItem, "Tring to delete unexistent group DN={DN}", DN);

                return NotFound();

            }
            else
            {
                // Delete 
                logger.LogInformation(DeleteItem, "Deleting group DN={DN}", DN);

                LogAudit("group.delete.request", DN, "delete");
                var result = await gManager.DeleteGroup(dgroup);
                if (result == 0)
                {
                    LogAudit("group.delete.success", DN, "delete");
                }
                if (result == 0) return Ok();
                else return this.StatusCode(500);

            }


        }

        #endregion

        private static bool IsValidGroupDn(string dn)
        {
            var regex = new Regex(@"\Acn=(?<gname>[^,]+?),", RegexOptions.IgnoreCase);
            return regex.IsMatch(dn);
        }

        private static bool TryExtractGroupName(string dn, out string groupName)
        {
            groupName = null;
            if (string.IsNullOrWhiteSpace(dn))
            {
                return false;
            }

            var regex = new Regex(@"\Acn=(?<gname>[^,]+?),", RegexOptions.IgnoreCase);
            var match = regex.Match(dn);
            if (!match.Success)
            {
                return false;
            }

            groupName = match.Groups["gname"].Value;
            return !string.IsNullOrWhiteSpace(groupName);
        }

        private static bool LooksLikeDistinguishedName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Contains("=") && value.Contains(",");
        }

        private async Task<bool> HasConflictingGroupAsync(string dn, string groupName)
        {
            var gManager = GroupManager.Instance;

            var byDn = await gManager.GetGroupAsync(dn);
            if (byDn != null)
            {
                return true;
            }

            var byCn = await gManager.GetGroupAsync(groupName, true, true);
            return byCn != null && !string.Equals(byCn.DN, dn, System.StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> ResolveMemberDnAsync(string member, bool listCnHint)
        {
            var gManager = GroupManager.Instance;
            var uManager = UserManager.Instance;

            if (string.IsNullOrWhiteSpace(member))
            {
                return null;
            }

            var memberValue = member.Trim();

            if (LooksLikeDistinguishedName(memberValue))
            {
                var groupByDn = await gManager.GetGroupAsync(memberValue);
                if (groupByDn != null)
                {
                    return groupByDn.DN;
                }

                var userByDn = await uManager.GetUserAsync(memberValue, "distinguishedName");
                return userByDn?.DN;
            }

            var grp = await gManager.GetGroupAsync(memberValue, true, true);
            if (grp != null)
            {
                return grp.DN;
            }

            var userBySamAccountName = await uManager.GetUserAsync(memberValue, "samaccountname");
            if (userBySamAccountName != null)
            {
                return userBySamAccountName.DN;
            }

            if (!listCnHint)
            {
                var userByCn = await uManager.GetUserAsync(memberValue, "cn");
                if (userByCn != null)
                {
                    return userByCn.DN;
                }
            }

            return null;
        }

        private async Task<List<string>> ResolveMembersAsync(IEnumerable<string> members, string groupDn, bool listCn)
        {
            var resolved = new List<string>();
            if (members == null)
            {
                return resolved;
            }

            foreach (var member in members)
            {
                var dname = await ResolveMemberDnAsync(member, listCn);

                if (string.IsNullOrWhiteSpace(dname))
                {
                    logger.LogError(InternalError, "Could not find member {member} for group {DN}", member, groupDn);
                    return null;
                }

                if (!resolved.Contains(dname, System.StringComparer.OrdinalIgnoreCase))
                {
                    resolved.Add(dname);
                }
            }

            return resolved;
        }
    }
}
