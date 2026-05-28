using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class HistoryService
    {
        private readonly ObjectService _objectService;
        private readonly WriteService _writeService;

        public HistoryService(ObjectService objectService, WriteService writeService)
        {
            _objectService = objectService;
            _writeService = writeService;
        }

        /// <summary>
        /// History dispatch. <paramref name="partName"/> + <paramref name="snapshotToken"/>
        /// drive the edit-snapshot <c>restore</c> action: <c>snapshot=latest</c> or
        /// a timestamp substring resolves to <c>&lt;kbPath&gt;/.gx/snapshots/&lt;guid&gt;-&lt;part&gt;-*.bak</c>
        /// and the prior bytes are routed back through <see cref="WriteService.WriteObject(string, string, string, string, bool, bool, bool, bool)"/>.
        /// When <paramref name="discard"/> is <c>true</c> and no snapshot token is
        /// supplied, the most recent EditSnapshotStore entry is restored — IDE
        /// <i>History | Restore</i> / <i>Discard changes</i> parity. Missing
        /// snapshots return a <c>NoSnapshot</c> envelope rather than an error.
        /// </summary>
        public string Execute(string target, string action, int versionId = 0,
                              string partName = null, string snapshotToken = null,
                              bool discard = false, bool dryRun = false)
        {
            try
            {
                switch (action?.ToLower())
                {
                    case "list":
                        if (!string.IsNullOrWhiteSpace(snapshotToken) || !string.IsNullOrWhiteSpace(partName))
                            return ListEditSnapshots(target, partName);
                        return ListRevisions(target);
                    case "get_source":
                        return GetVersionSource(target, versionId);
                    case "save":
                        return SaveSnapshot(target);
                    case "restore":
                        // Item 21 (friction 2026-05-22): dryRun=true returns the diff
                        // (current vs snapshot) without writing through SDK.
                        if (dryRun)
                            return DryRunRestore(target, partName, snapshotToken, discard);
                        if (!string.IsNullOrWhiteSpace(snapshotToken))
                            return RestoreEditSnapshot(target, partName, snapshotToken);
                        if (discard)
                            return DiscardLatestEditSnapshot(target, partName);
                        return RestoreSnapshot(target);
                    default:
                        return Models.McpResponse.Err(
                        code: "UnknownHistoryAction",
                        message: "Unknown history action '" + action + "'.",
                        hint: "Supported actions are list, get_source, save and restore.",
                        nextSteps: new JArray(Models.McpResponse.NextStep(
                            tool: "genexus_history",
                            args: new JObject { ["target"] = target, ["action"] = "list" },
                            why: "Lists available revisions/snapshots for this object.")),
                        target: target);
                }
            }
            catch (Exception ex)
            {
                return Models.McpResponse.Err(
                    code: "HistoryExecuteFailed",
                    message: ex.Message,
                    hint: "Check the history action and target.",
                    target: target);
            }
        }

        /// <summary>
        /// Item 21 (friction 2026-05-22) — universal dryRun for genexus_history
        /// action=restore. Resolves the same snapshot the live restore would
        /// pick, reads the current persisted source, and returns a unified
        /// diff envelope — no SDK write.
        /// </summary>
        private string DryRunRestore(string target, string partName, string snapshotToken, bool discard)
        {
            var obj = _objectService.FindObject(target);
            if (obj == null) return Models.McpResponse.Err(
                code: "ObjectNotFound",
                message: "Object not found.",
                hint: "Verify the object name and ensure the KB is open.",
                nextSteps: new JArray(
                    Models.McpResponse.NextStep(
                        tool: "genexus_list_objects",
                        args: new JObject { ["name_contains"] = target },
                        why: "Lists objects whose names match, in case of a typo."),
                    Models.McpResponse.NextStep(
                        tool: "genexus_lifecycle",
                        args: new JObject { ["action"] = "index", ["force"] = true },
                        why: "Rebuilds the SearchIndex if the object exists but isn't indexed.")),
                target: target);
            string guid;
            try { guid = obj.Guid.ToString(); }
            catch (Exception ex) { return Models.McpResponse.Err(code: "DryRunFailed", message: ex.Message, target: target); }

            string kbPath = null;
            try { kbPath = _objectService.GetKbService().GetKbPath(); } catch { }
            string root = EditSnapshotStore.ResolveRoot(kbPath);
            string part = string.IsNullOrWhiteSpace(partName) ? "Source" : partName;

            string path;
            if (!string.IsNullOrWhiteSpace(snapshotToken))
            {
                path = EditSnapshotStore.ResolveByTimestamp(root, guid, part, snapshotToken);
            }
            else
            {
                var files = EditSnapshotStore.List(root, guid, part);
                path = files.Count > 0 ? files[0] : null;
            }
            if (string.IsNullOrEmpty(path))
            {
                return Models.McpResponse.Ok(
                    target: target,
                    code: "NoSnapshot",
                    result: new JObject
                    {
                        ["part"] = part,
                        ["dryRun"] = true,
                        ["hint"] = "No snapshot to dry-run against. Edit this object first to capture a baseline."
                    });
            }

            string snapshotContent = EditSnapshotStore.ReadSnapshot(path);
            if (snapshotContent == null)
            {
                return Models.McpResponse.Err(
                    code: "SnapshotReadFailed",
                    message: "File exists but could not be decoded: " + path,
                    hint: "The snapshot file may be corrupt. List snapshots and use a different token.",
                    nextSteps: new JArray(Models.McpResponse.NextStep(
                        tool: "genexus_history",
                        args: new JObject { ["target"] = target, ["action"] = "list", ["part"] = part },
                        why: "Lists available snapshots to find a valid token.")),
                    target: target);
            }

            string currentContent = string.Empty;
            try
            {
                string readJson = _objectService.ReadObjectSource(target, part, null, null, "mcp", true, null);
                if (!string.IsNullOrWhiteSpace(readJson))
                {
                    var parsed = JObject.Parse(readJson);
                    currentContent = parsed["source"]?.ToString() ?? parsed["content"]?.ToString() ?? string.Empty;
                }
            }
            catch { /* leave currentContent empty */ }

            string diff = GxMcp.Worker.Helpers.DiffBuilder.UnifiedDiff(currentContent, snapshotContent, 3);
            return Models.McpResponse.Ok(
                target: target,
                code: "DryRun",
                result: new JObject
                {
                    ["part"] = part,
                    ["dryRun"] = true,
                    ["discard"] = discard,
                    ["restoreSource"] = System.IO.Path.GetFileName(path),
                    ["restoreSourcePath"] = path,
                    ["diff"] = diff,
                    ["hint"] = "Re-run without dryRun to write these bytes through WriteService."
                });
        }

        private string ListEditSnapshots(string target, string partName)
        {
            var obj = _objectService.FindObject(target);
            if (obj == null) return Models.McpResponse.Err(
                code: "ObjectNotFound",
                message: "Object not found.",
                hint: "Verify the object name and ensure the KB is open.",
                nextSteps: new JArray(
                    Models.McpResponse.NextStep(
                        tool: "genexus_list_objects",
                        args: new JObject { ["name_contains"] = target },
                        why: "Lists objects whose names match, in case of a typo."),
                    Models.McpResponse.NextStep(
                        tool: "genexus_lifecycle",
                        args: new JObject { ["action"] = "index", ["force"] = true },
                        why: "Rebuilds the SearchIndex if the object exists but isn't indexed.")),
                target: target);
            string guid;
            try { guid = obj.Guid.ToString(); }
            catch (Exception ex) { return Models.McpResponse.Err(code: "SnapshotListFailed", message: ex.Message, target: target); }

            string kbPath = null;
            try { kbPath = _objectService.GetKbService().GetKbPath(); } catch { }
            string root = EditSnapshotStore.ResolveRoot(kbPath);
            string part = string.IsNullOrWhiteSpace(partName) ? "Source" : partName;
            var files = EditSnapshotStore.List(root, guid, part);
            var arr = new JArray();
            foreach (var f in files)
            {
                arr.Add(new JObject
                {
                    ["path"] = f,
                    ["fileName"] = System.IO.Path.GetFileName(f)
                });
            }
            return Models.McpResponse.Ok(
                target: target,
                code: "SnapshotList",
                result: new JObject
                {
                    ["part"] = part,
                    ["count"] = files.Count,
                    ["snapshots"] = arr
                });
        }

        /// <summary>
        /// v2.6.6 Stream H (FR#28) — IDE "Discard changes" parity. Resolves
        /// the most recent pre-edit snapshot for (target, part) and restores
        /// it through WriteService (the same persistence boundary the IDE
        /// uses). Returns the snapshot token used so the caller has an
        /// audit trail. NoSnapshot is a soft outcome — the agent may ask
        /// for discard before any edit was captured and that should not be
        /// treated as an error.
        /// </summary>
        private string DiscardLatestEditSnapshot(string target, string partName)
        {
            var obj = _objectService.FindObject(target);
            if (obj == null) return Models.McpResponse.Err(
                code: "ObjectNotFound",
                message: "Object not found.",
                hint: "Verify the object name and ensure the KB is open.",
                nextSteps: new JArray(
                    Models.McpResponse.NextStep(
                        tool: "genexus_list_objects",
                        args: new JObject { ["name_contains"] = target },
                        why: "Lists objects whose names match, in case of a typo."),
                    Models.McpResponse.NextStep(
                        tool: "genexus_lifecycle",
                        args: new JObject { ["action"] = "index", ["force"] = true },
                        why: "Rebuilds the SearchIndex if the object exists but isn't indexed.")),
                target: target);
            string guid;
            try { guid = obj.Guid.ToString(); }
            catch (Exception ex) { return Models.McpResponse.Err(code: "DiscardFailed", message: ex.Message, target: target); }

            string kbPath = null;
            try { kbPath = _objectService.GetKbService().GetKbPath(); } catch { }

            return DiscardLatestEditSnapshotCore(
                target, partName, guid, kbPath,
                (t, p, content) => _writeService.WriteObject(t, p, content));
        }

        /// <summary>
        /// v2.6.6 Stream H (FR#28) — pure helper, no SDK reads. Splits out the
        /// snapshot lookup + restoration so it can be unit-tested without a live
        /// KB. <paramref name="writer"/> is the persistence hook (WriteService
        /// in production; a recording delegate in tests).
        /// </summary>
        internal static string DiscardLatestEditSnapshotCore(
            string target,
            string partName,
            string objectGuid,
            string kbPath,
            Func<string, string, string, string> writer)
        {
            string root = EditSnapshotStore.ResolveRoot(kbPath);
            string part = string.IsNullOrWhiteSpace(partName) ? "Source" : partName;
            var files = EditSnapshotStore.List(root, objectGuid, part);
            if (files.Count == 0)
            {
                return Models.McpResponse.Ok(
                    target: target,
                    code: "NoSnapshot",
                    result: new JObject
                    {
                        ["part"] = part,
                        ["hint"] = "Edit this object first to capture a baseline; discard restores the pre-edit state."
                    });
            }

            string path = files[0]; // newest
            string content = EditSnapshotStore.ReadSnapshot(path);
            if (content == null)
            {
                return Models.McpResponse.Err(
                    code: "SnapshotReadFailed",
                    message: "File exists but could not be decoded: " + path,
                    hint: "The snapshot file may be corrupt. List snapshots and use a different token.",
                    nextSteps: new JArray(Models.McpResponse.NextStep(
                        tool: "genexus_history",
                        args: new JObject { ["target"] = target, ["action"] = "list", ["part"] = part },
                        why: "Lists available snapshots to find a valid token.")),
                    target: target);
            }

            string snapshotToken;
            try { snapshotToken = System.IO.Path.GetFileName(path); } catch { snapshotToken = path; }

            string writeResult = writer(target, part, content) ?? "{}";
            try
            {
                var json = JObject.Parse(writeResult);
                json["discarded"] = true;
                json["restoredFrom"] = path;
                json["restoredSnapshot"] = snapshotToken;
                return json.ToString();
            }
            catch
            {
                return writeResult;
            }
        }

        private string RestoreEditSnapshot(string target, string partName, string snapshotToken)
        {
            var obj = _objectService.FindObject(target);
            if (obj == null) return Models.McpResponse.Err(
                code: "ObjectNotFound",
                message: "Object not found.",
                hint: "Verify the object name and ensure the KB is open.",
                nextSteps: new JArray(
                    Models.McpResponse.NextStep(
                        tool: "genexus_list_objects",
                        args: new JObject { ["name_contains"] = target },
                        why: "Lists objects whose names match, in case of a typo."),
                    Models.McpResponse.NextStep(
                        tool: "genexus_lifecycle",
                        args: new JObject { ["action"] = "index", ["force"] = true },
                        why: "Rebuilds the SearchIndex if the object exists but isn't indexed.")),
                target: target);
            string guid;
            try { guid = obj.Guid.ToString(); }
            catch (Exception ex) { return Models.McpResponse.Err(code: "SnapshotRestoreFailed", message: ex.Message, target: target); }

            string kbPath = null;
            try { kbPath = _objectService.GetKbService().GetKbPath(); } catch { }
            string root = EditSnapshotStore.ResolveRoot(kbPath);
            string part = string.IsNullOrWhiteSpace(partName) ? "Source" : partName;
            string path = EditSnapshotStore.ResolveByTimestamp(root, guid, part, snapshotToken);
            if (string.IsNullOrEmpty(path))
            {
                return Models.McpResponse.Err(
                    code: "SnapshotNotFound",
                    message: "No snapshot matched token '" + snapshotToken + "'.",
                    hint: "Use action=list with part=" + part + " to enumerate available snapshots.",
                    nextSteps: new JArray(Models.McpResponse.NextStep(
                        tool: "genexus_history",
                        args: new JObject { ["target"] = target, ["action"] = "list", ["part"] = part },
                        why: "Returns the list of snapshot tokens for this object and part.")),
                    target: target);
            }

            string content = EditSnapshotStore.ReadSnapshot(path);
            if (content == null)
            {
                return Models.McpResponse.Err(
                    code: "SnapshotReadFailed",
                    message: "File exists but could not be decoded: " + path,
                    hint: "The snapshot file may be corrupt. List snapshots and use a different token.",
                    target: target);
            }

            string writeResult = _writeService.WriteObject(target, part, content);
            try
            {
                var json = JObject.Parse(writeResult);
                json["restoredFrom"] = path;
                json["restoredSnapshot"] = System.IO.Path.GetFileName(path);
                return json.ToString();
            }
            catch
            {
                return writeResult;
            }
        }

        private string GetVersionSource(string target, int versionId)
        {
            var obj = _objectService.FindObject(target);
            if (obj == null)
            {
                return Models.McpResponse.Err(
                    code: "ObjectNotFound",
                    message: "Object not found.",
                    hint: "The requested object is not available in the active Knowledge Base.",
                    nextSteps: new JArray(Models.McpResponse.NextStep(
                        tool: "genexus_list_objects",
                        args: new JObject(),
                        why: "Lists available objects in the KB.")),
                    target: target);
            }

            try
            {
                var versions = obj.GetVersions().Cast<global::Artech.Architecture.Common.Objects.KBObject>().ToList();
                var targetVersion = versions.FirstOrDefault(v => v.VersionId == versionId);

                if (targetVersion != null)
                {
                    var sourcePart = targetVersion.Parts.Cast<global::Artech.Architecture.Common.Objects.KBObjectPart>()
                                        .FirstOrDefault(p => p is global::Artech.Architecture.Common.Objects.ISource) 
                                        as global::Artech.Architecture.Common.Objects.ISource;

                    if (sourcePart != null)
                    {
                        string content = sourcePart.Source ?? "";
                        return Models.McpResponse.Ok(
                            target: target,
                            code: "VersionSourceRead",
                            result: new JObject
                            {
                                ["source"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content)),
                                ["isBase64"] = true,
                                ["versionId"] = versionId
                            });
                    }
                }
                return Models.McpResponse.Err(
                    code: "VersionNotFound",
                    message: "Version " + versionId + " not found or has no source code.",
                    hint: "Use action=list to see available version IDs for this object.",
                    nextSteps: new JArray(Models.McpResponse.NextStep(
                        tool: "genexus_history",
                        args: new JObject { ["target"] = target, ["action"] = "list" },
                        why: "Returns available revisions with their version IDs.")),
                    target: target);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to read version source: " + ex.Message);
                return Models.McpResponse.Err(
                    code: "VersionSourceFailed",
                    message: "SDK Version access failed: " + ex.Message,
                    hint: "The SDK history API may not be available for this KB.",
                    target: target);
            }
        }

        private string ListRevisions(string target)
        {
            var obj = _objectService.FindObject(target);
            if (obj == null)
            {
                return Models.McpResponse.Err(
                    code: "ObjectNotFound",
                    message: "Object not found.",
                    hint: "The requested object is not available in the active Knowledge Base.",
                    nextSteps: new JArray(Models.McpResponse.NextStep(
                        tool: "genexus_list_objects",
                        args: new JObject(),
                        why: "Lists available objects in the KB.")),
                    target: target);
            }

            var history = new JArray();
            try
            {
                var versions = obj.GetVersions().Cast<global::Artech.Architecture.Common.Objects.KBObject>();
                foreach (var rev in versions)
                {
                    history.Add(new JObject
                    {
                        ["version"] = rev.VersionId,
                        ["date"] = rev.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["user"] = rev.UserName,
                        ["comment"] = rev.Comment
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to read revisions: " + ex.Message);
                return Models.McpResponse.Err(
                    code: "HistoryAccessFailed",
                    message: "SDK History access failed: " + ex.Message,
                    hint: "The SDK history API may not be available for this KB.",
                    target: target);
            }

            return Models.McpResponse.Ok(
                target: target,
                code: "RevisionList",
                result: new JObject { ["history"] = history });
        }

        private string SaveSnapshot(string target)
        {
            var obj = _objectService.FindObject(target);
            if (obj == null) return Models.McpResponse.Err(
                code: "ObjectNotFound",
                message: "Object not found.",
                hint: "Verify the object name and ensure the KB is open.",
                nextSteps: new JArray(
                    Models.McpResponse.NextStep(
                        tool: "genexus_list_objects",
                        args: new JObject { ["name_contains"] = target },
                        why: "Lists objects whose names match, in case of a typo."),
                    Models.McpResponse.NextStep(
                        tool: "genexus_lifecycle",
                        args: new JObject { ["action"] = "index", ["force"] = true },
                        why: "Rebuilds the SearchIndex if the object exists but isn't indexed.")),
                target: target);

            string histDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".history");
            if (!Directory.Exists(histDir)) Directory.CreateDirectory(histDir);

            string sourceJson = _objectService.ReadObjectSource(target, "Source", client: "mcp");
            if (sourceJson.Contains("\"error\"")) return sourceJson;

            var json = JObject.Parse(sourceJson);
            string code = json["source"] != null ? json["source"].ToString() : "";

            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            // Use canonical name: Type_Name
            string safeName = $"{obj.TypeDescriptor.Name}_{obj.Name}".Replace(":", "_").Replace(" ", "_");
            string filePath = Path.Combine(histDir, string.Format("{0}_{1}.txt", safeName, ts));
            File.WriteAllText(filePath, code, Encoding.UTF8);

            return Models.McpResponse.Ok(
                target: target,
                code: "SnapshotSaved",
                result: new JObject
                {
                    ["file"] = Path.GetFileName(filePath),
                    ["timestamp"] = ts,
                    ["canonicalName"] = safeName
                });
        }

        private string RestoreSnapshot(string target)
        {
            var obj = _objectService.FindObject(target);
            if (obj == null) return Models.McpResponse.Err(
                code: "ObjectNotFound",
                message: "Object not found.",
                hint: "Verify the object name and ensure the KB is open.",
                nextSteps: new JArray(
                    Models.McpResponse.NextStep(
                        tool: "genexus_list_objects",
                        args: new JObject { ["name_contains"] = target },
                        why: "Lists objects whose names match, in case of a typo."),
                    Models.McpResponse.NextStep(
                        tool: "genexus_lifecycle",
                        args: new JObject { ["action"] = "index", ["force"] = true },
                        why: "Rebuilds the SearchIndex if the object exists but isn't indexed.")),
                target: target);

            string histDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".history");
            if (!Directory.Exists(histDir)) Directory.CreateDirectory(histDir);

            // Use canonical name: Type_Name
            string safeName = $"{obj.TypeDescriptor.Name}_{obj.Name}".Replace(":", "_").Replace(" ", "_");
            var files = Directory.GetFiles(histDir, $"{safeName}_*.txt")
                .OrderByDescending(f => f)
                .ToArray();

            if (files.Length == 0)
                return Models.McpResponse.Err(
                    code: "SnapshotNotFound",
                    message: "No snapshots found for '" + safeName + "'.",
                    hint: "Use action=save first to capture a snapshot before restoring.",
                    nextSteps: new JArray(Models.McpResponse.NextStep(
                        tool: "genexus_history",
                        args: new JObject { ["target"] = target, ["action"] = "save" },
                        why: "Saves the current state as a snapshot that can be restored later.")),
                    target: target);

            string lastFile = files.First();
            string code = File.ReadAllText(lastFile, Encoding.UTF8);

            return _writeService.WriteObject(target, "Source", code);
        }
    }
}
