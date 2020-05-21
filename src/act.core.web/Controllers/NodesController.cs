﻿using System;
using System.Linq;
using System.Threading.Tasks;
using act.core.data;
using act.core.etl;
using act.core.web.Extensions;
using act.core.web.Framework;
using act.core.web.Models.AppSpecs;
using act.core.web.Models.Nodes;
using act.core.web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace act.core.web.Controllers
{
    
    public class NodesController : PureMvcControllerBase
    {
        private readonly INodeFactory _nodeFactory;
        private readonly ISpecificationFactory<AppSpecInformation, AppSpecSearchResult> _appSpecificationFactory;
        private readonly IGatherer _gatherer;


        public NodesController(INodeFactory nodeFactory, ISpecificationFactory<AppSpecInformation, AppSpecSearchResult> appSpecificationFactory, IGatherer gatherer, ILoggerFactory logger) : base(logger)
        {
            _nodeFactory = nodeFactory;
            _appSpecificationFactory = appSpecificationFactory;
            _gatherer = gatherer;
        }

        [HttpGet]
        public async Task<ActionResult> Automate(int environmentId)
        {
            var url = GetUri().PathAndQuery.Substring(4);
            var response = await _gatherer.Proxy(environmentId, url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                AddHeaders(response.Headers.Where(p => p.Key.ToLower().StartsWith("x-")));
                return Content(content, "application/json");
            }
            return StatusCode(response.StatusCode, response.ReasonPhrase);
        }

        [HttpPost]
        public async Task<JsonResult> GatherForSpec(long id, int environmentId)
        {
            Logger.LogInformation($"Gather Called for {id} and {environmentId}");
            var ids = await _nodeFactory.ChefIdsForAppOrOsSpecAndEnvironment(id, environmentId);
            await _gatherer.Gather(environmentId, ids);            
            return Json(JsonEnvelope.Success());
        }


        [HttpPost]
        public async Task<JsonResult> ComplianceReport(long id)
        {
            Logger.LogInformation($"Compliance Report Called for {id}");
            await _gatherer.ComplianceReport(id);
            return Json(JsonEnvelope.Success());
        }

        
        [HttpGet]
        public  async Task<ViewResult> Index()
        {
            return View(new NodeSearch(await _nodeFactory.GetEnvironments()));
        }

        [HttpGet]
        public async Task<ViewResult> ForBuildSpec(long id)
        {
            return View(new NodesForApp(await _appSpecificationFactory.GetOne(id), await _nodeFactory.GetAssignedToBuildSpec(id), await _nodeFactory.GetEnvironments()));
        }

        [HttpPost]
        public async Task<PartialViewResult> Search(PlatformConstant[] platform, int[] environment, PciScopeConstant[] securityClass,
           NodeComplianceSearchTypeConstant[] compliance, NodeSearchTypeConstant searchType, string search, bool? hideProductExclusions, int? pageIndex, bool? showButtons)
        {
            return PartialView(await _nodeFactory.Search(platform, environment, securityClass, compliance, searchType, search, hideProductExclusions.GetValueOrDefault(), UserSecurity, pageIndex.GetValueOrDefault(), showButtons.GetValueOrDefault()));
        }

        [HttpPost]
        public async Task<JsonResult> Assign(long id, long? specId)
        {
            try
            {
                await _nodeFactory.AssignBuildSpecification(id, specId, UserSecurity.SamAccountName);
                Logger.LogInformation($"Node with Inventory ID {id} was assigned to build spec {specId.GetValueOrDefault()} by {UserSecurity.SamAccountName}");
                if (specId.HasValue)
                {
                    return Json(JsonEnvelope.Success(new
                    {
                        specUrl = Url.BuildSpecReport(specId.Value),
                        portUrl = Url.PortReport(specId.Value),
                        complianceUrl = Url.Review(specId.Value)
                    }));
                }
                return Json(JsonEnvelope.Success());
            }
            catch (ArgumentException)
            {
                return Json(JsonEnvelope.Error($"A node with the id {id} was not found."));
            }
        }
        [HttpPost]
        [AllowAnonymous]
        public JsonResult BuildSpecId(string host)
        {
            return Json(JsonEnvelope.Success(new {id = _nodeFactory.BuildSpecIdByHost(host)}));
        }
    }
}