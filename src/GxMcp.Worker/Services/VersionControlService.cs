using System;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Services
{
    public class VersionControlService
    {
        private readonly KbService _kbService;

        public VersionControlService(KbService kbService)
        {
            _kbService = kbService;
        }

        public string GetPendingChanges()
        {
            try
            {
                dynamic kb = _kbService.GetKB();
                if (kb == null) return McpResponse.Err(code: "NoKb", message: "No KB open");

                var result = new JObject();
                
                // Use dynamic to access VersionControl property safely at runtime
                bool hasVC = false;
                try { hasVC = (kb.VersionControl != null); } catch { }
                
                result["connected"] = hasVC;
                
                if (hasVC)
                {
                    result["serverUrl"] = kb.VersionControl.ServerUrl;
                    
                    var pending = new JArray();
                    foreach (dynamic change in kb.VersionControl.GetPendingChanges())
                    {
                        pending.Add(new JObject {
                            ["name"] = change.Name,
                            ["type"] = change.TypeDescriptor.Name,
                            ["action"] = change.Action.ToString()
                        });
                    }
                    result["pendingChanges"] = pending;
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                return McpResponse.Err(code: "PendingChangesFailed", message: ex.Message);
            }
        }

        public string Update()
        {
            try
            {
                dynamic kb = _kbService.GetKB();
                if (kb == null) return McpResponse.Err(code: "NoKb", message: "No KB open");

                kb.VersionControl.Update();
                return McpResponse.Ok(code: "VcUpdateCompleted");
            }
            catch (Exception ex) { return McpResponse.Err(code: "VcUpdateFailed", message: ex.Message); }
        }

        public string Commit(string message)
        {
            try
            {
                dynamic kb = _kbService.GetKB();
                if (kb == null) return McpResponse.Err(code: "NoKb", message: "No KB open");

                kb.VersionControl.Commit(message);
                return McpResponse.Ok(code: "VcCommitCompleted");
            }
            catch (Exception ex) { return McpResponse.Err(code: "VcCommitFailed", message: ex.Message); }
        }
    }
}
