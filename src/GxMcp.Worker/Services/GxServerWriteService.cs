using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.Common.Services.TeamDevData.Server;
using Artech.Architecture.Common.Services.TeamDev;
using Artech.Udm.Framework;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;
using Newtonsoft.Json.Linq;
using SdkServices = Artech.Architecture.Common.Services.Services;
using ClientTeamDev = Artech.Architecture.Common.Services.TeamDevData.Client;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// GxServer (Team Development) WRITE surface for genexus_gxserver — commit,
    /// update, lock, resolve. Sibling to <see cref="GxServerSyncService"/> (the
    /// read-only status/pending/conflicts/history path); shares the same
    /// SDK-service-resolution idiom (SdkServices.TryGetService, guard every
    /// null/throw path, never crash the worker).
    ///
    /// Resolves <see cref="IGXserverService"/> for Commit/GetUpdateFile/Lock,
    /// and <see cref="ITeamDevClientService"/> (same service the read path
    /// already resolves) for MarkAsResolved.
    ///
    /// Feasibility note (spike, v2.14): the SDK's write-path data-transfer
    /// objects (<see cref="ServerCommitData"/>/<see cref="ServerUpdateData"/>/
    /// <see cref="ServerLockData"/>) are plain settable POCOs — confirmed via
    /// reflection against Artech.Architecture.Common.dll, no hidden IDE/session
    /// context required to *construct* them (Model + KBAlias + the action's own
    /// fields is enough). What remains unverified is whether
    /// <see cref="IGXserverService"/> itself resolves in a headless worker and
    /// whether Commit/GetUpdateFile/Lock succeed without the authenticated
    /// Team-Dev session the IDE's login flow normally establishes — no
    /// GXserver-linked KB was available locally to exercise this live. Every
    /// call is defensively wrapped; failures surface as clean error envelopes
    /// rather than crashing the worker.
    /// </summary>
    public class GxServerWriteService
    {
        private readonly KbService _kb;

        public GxServerWriteService(KbService kb)
        {
            _kb = kb;
        }

        public string Run(string action, JObject args)
        {
            KnowledgeBase kb;
            try
            {
                kb = _kb?.GetKB() as KnowledgeBase;
            }
            catch (Exception ex)
            {
                return McpResponse.Err(code: "KbUnavailable", message: ex.Message, hint: "Open a KB first (genexus_kb action=open).");
            }
            if (kb == null)
            {
                return McpResponse.Err(code: "KbUnavailable", message: "No KB is open in this worker.", hint: "Open a KB first (genexus_kb action=open).");
            }

            ITeamDevClientService tdSvc = GxMcp.Worker.Helpers.SdkServiceResolver.Resolve<ITeamDevClientService>();
            if (tdSvc == null)
            {
                return McpResponse.Err(
                    code: "GxServerServiceUnavailable",
                    message: "The GeneXus SDK's ITeamDevClientService is not registered in this worker session (self-heal retries were exhausted).",
                    hint: "Restart the worker (genexus_worker_reload mode=hard) and retry.");
            }

            bool linked;
            try { linked = tdSvc.IsLinkedKB(kb); }
            catch (Exception ex)
            {
                return McpResponse.Err(code: "GxServerCheckFailed", message: ex.Message, hint: "Check the worker log for details.");
            }
            if (!linked)
            {
                return McpResponse.Err(
                    code: "NotLinked",
                    message: "This KB is not linked to a GeneXus Server instance.",
                    hint: "Link the KB to a GeneXus Server first (IDE: Team Development > Connect), then retry.");
            }

            KBModel model = kb.DesignModel;
            string kbPath = _kb?.GetKbPath();
            string kbAlias = Environment.GetEnvironmentVariable("GX_KB_ALIAS")
                             ?? (string.IsNullOrEmpty(kbPath) ? string.Empty : Path.GetFileName(kbPath.TrimEnd('\\', '/')));

            switch (action)
            {
                case "commit": return DoCommit(tdSvc, model, kbAlias, args);
                case "update": return DoUpdate(tdSvc, kb, model, kbAlias, args);
                case "lock": return DoLock(model, kbAlias, args);
                case "resolve": return DoResolve(tdSvc, kb, model, args);
                default:
                    return McpResponse.Err(code: "BadAction", message: "Unknown write action '" + action + "'.");
            }
        }

        private string DoCommit(ITeamDevClientService tdSvc, KBModel model, string kbAlias, JObject args)
        {
            string message = args?["message"]?.ToString();
            if (string.IsNullOrWhiteSpace(message))
            {
                return McpResponse.Err(
                    code: "BadArgs",
                    message: "message is required for commit.",
                    hint: "Pass action=commit, message=\"<commit comment>\".");
            }

            IGXserverService svc = GxMcp.Worker.Helpers.SdkServiceResolver.Resolve<IGXserverService>();
            if (svc == null) return ServerServiceUnavailable();

            // issue #32 item 6: optional partial commit. IGXserverService.Commit has no
            // per-object selection — it commits the whole non-ignored changelist. The IDE's
            // "commit these objects" works by marking every OTHER pending object
            // IgnoreForCommit, committing, then restoring the flags. We mirror that. When
            // `targets` is omitted this is the previous whole-changelist behavior.
            var targetsToken = args?["targets"] as JArray;
            HashSet<string> wanted = null;
            if (targetsToken != null && targetsToken.Count > 0)
            {
                wanted = new HashSet<string>(
                    targetsToken.Select(t => t?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)),
                    StringComparer.OrdinalIgnoreCase);
                if (wanted.Count == 0)
                    return McpResponse.Err(code: "BadArgs", message: "targets must contain at least one non-empty object name.");
            }

            // Snapshot the pre-commit changelist names for a WHOLE commit so the response can
            // report exactly which objects went in — the SDK's Commit returns only a success
            // bool + error string, so without this the caller can't tell what was committed
            // (the previous response was just {committed:true, message}). Partial commits
            // already know their set (`wanted`). Read-only; failure only costs the report.
            List<string> wholeCommitNames = null;
            if (wanted == null)
            {
                try
                {
                    wholeCommitNames = (tdSvc.GetLocalChanges(model) ?? Enumerable.Empty<KBObjectHistory>())
                        .Where(h => h != null)
                        .Select(h => SafeStr(() => h.ObjectName))
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList();
                }
                catch { /* reporting only — never block the commit */ }
            }

            var ignored = new List<KBObjectHistory>();
            try
            {
                if (wanted != null)
                {
                    // Partial commit requires enumerating + toggling the local changelist,
                    // which is the ITeamDevClientService surface (resolved by the caller).
                    List<KBObjectHistory> pending;
                    try
                    {
                        pending = (tdSvc.GetLocalChanges(model) ?? Enumerable.Empty<KBObjectHistory>())
                            .Where(h => h != null).ToList();
                    }
                    catch (Exception ex)
                    {
                        return McpResponse.Err(code: "CommitFailed", message: "Could not read the pending changelist for partial commit: " + ex.Message, hint: "Retry without targets to commit the whole changelist, or check the worker log.");
                    }

                    var pendingNames = new HashSet<string>(
                        pending.Select(h => SafeStr(() => h.ObjectName)).Where(n => !string.IsNullOrEmpty(n)),
                        StringComparer.OrdinalIgnoreCase);

                    var unknown = wanted.Where(w => !pendingNames.Contains(w)).ToList();
                    if (unknown.Count > 0)
                    {
                        // Refuse rather than commit everything — the caller asked for a subset.
                        return McpResponse.Err(
                            code: "NoMatchingPending",
                            message: "These targets are not in the pending changelist: " + string.Join(", ", unknown) + ". Nothing was committed.",
                            hint: "Check action=pending for the exact committable object names, then retry.");
                    }

                    // Exclude every pending object the caller did NOT ask for; ensure the
                    // wanted ones are enabled. Track excluded keys so we can restore them.
                    foreach (var h in pending)
                    {
                        string name = SafeStr(() => h.ObjectName);
                        if (name != null && !wanted.Contains(name))
                        {
                            try { tdSvc.IgnoreForCommit(model, h.Key); ignored.Add(h); } catch { /* best-effort */ }
                        }
                        else
                        {
                            try { tdSvc.EnableForCommit(model, h.Key); } catch { /* best-effort */ }
                        }
                    }
                }

                var data = new ServerCommitData
                {
                    Model = model,
                    KBAlias = kbAlias,
                    Comments = message,
                    CommitDate = DateTime.Now,
                    ForceCommit = args?["force"]?.ToObject<bool?>() ?? false
                };

                string errorMsg;
                bool ok = svc.Commit(data, null, out errorMsg);
                if (!ok)
                {
                    return McpResponse.Err(
                        code: "CommitFailed",
                        message: string.IsNullOrEmpty(errorMsg) ? "Commit returned false." : errorMsg,
                        hint: "Check for pending conflicts (action=conflicts) before retrying.");
                }

                var committedList = wanted != null
                    ? (IEnumerable<string>)wanted
                    : (wholeCommitNames ?? new List<string>());

                var result = new JObject
                {
                    ["committed"] = true,
                    ["message"] = message,
                    ["committedObjects"] = new JArray(committedList),
                    ["committedCount"] = committedList.Count(),
                    // The new remote version/revision after the commit lands, so the caller can
                    // confirm the changelist reached the server (and reference it).
                    ["remoteVersion"] = SafeStr(() => tdSvc.RemoteVersionName(model)),
                    ["source"] = "sdk:IGXserverService"
                };
                if (wanted != null)
                {
                    result["partial"] = true;
                    result["committedTargets"] = new JArray(wanted);
                    result["excludedCount"] = ignored.Count;
                }
                return McpResponse.Ok(code: "GxServerCommitCompleted", result: result);
            }
            catch (Exception ex)
            {
                return McpResponse.Err(code: "CommitFailed", message: ex.Message, hint: "Check the worker log for details.");
            }
            finally
            {
                // Restore ignore flags on the objects we excluded so a later whole-changelist
                // commit doesn't silently skip them. Best-effort — a failure here only means
                // the excluded objects stay ignored until re-enabled manually.
                foreach (var h in ignored)
                {
                    try { tdSvc.EnableForCommit(model, h.Key); } catch { /* best-effort */ }
                }
            }
        }

        private string DoUpdate(ITeamDevClientService tdSvc, KnowledgeBase kb, KBModel model, string kbAlias, JObject args)
        {
            IGXserverService svc = GxMcp.Worker.Helpers.SdkServiceResolver.Resolve<IGXserverService>();
            if (svc == null) return ServerServiceUnavailable();

            bool apply = args?["apply"]?.ToObject<bool?>() ?? true;
            bool exportAll = args?["all"]?.ToObject<bool?>() ?? false;

            // Step 1: fetch the pending-changes package from the server (download).
            string updateFile;
            try
            {
                var data = new ServerUpdateData
                {
                    Model = model,
                    KBAlias = kbAlias,
                    UpdateStatistics = false,
                    ExportAll = exportAll
                };
                updateFile = svc.GetUpdateFile(data);
            }
            catch (Exception ex)
            {
                return McpResponse.Err(code: "UpdateFailed", message: ex.Message, hint: "Check the worker log for details.");
            }

            // apply=false preserves the old download-only behavior (no local mutation).
            if (!apply)
            {
                return McpResponse.Ok(
                    code: "GxServerUpdateFileRetrieved",
                    result: new JObject
                    {
                        ["applied"] = false,
                        ["updateFile"] = updateFile ?? string.Empty,
                        ["note"] = "Downloaded the pending-changes package only (apply=false). Pass apply=true (default) to receive the changes into local KB objects.",
                        ["source"] = "sdk:IGXserverService"
                    });
            }

            // Step 2: apply into local KB objects via ITeamDevClientUpdate (obtained from
            // ITeamDevClientService.JustReceiveChanges). This hits the server, so it needs
            // credentials — url auto-resolves from the linked KB; user/password come from args
            // or GXMCP_TEAMDEV_* env. Conflicting objects are left flagged for action=resolve.
            var creds = ReadCreds(tdSvc, kb, args);
            if (string.IsNullOrWhiteSpace(creds.Url))
                return CredentialsRequired("update");

            try
            {
                var rc = new ClientTeamDev.ReceiveChangesData
                {
                    Model = model,
                    Url = creds.Url,
                    User = creds.User,
                    Password = creds.Password,
                    AuthenticationToken = creds.Token,
                    FilePath = updateFile,
                    ExportAll = exportAll,
                    IncludeReferencesDependencies = true
                };

                ITeamDevClientUpdate updater = tdSvc.JustReceiveChanges(rc);
                if (updater == null)
                    return McpResponse.Err(code: "UpdateFailed", message: "JustReceiveChanges returned no updater.", hint: "Verify server credentials and that the KB is linked.");

                bool ok = updater.Update();

                var conflicts = CollectConflictNames(tdSvc, model);
                var result = new JObject
                {
                    ["applied"] = ok,
                    ["updateFile"] = updateFile ?? string.Empty,
                    ["conflictCount"] = conflicts.Count,
                    ["conflicts"] = new JArray(conflicts),
                    ["source"] = "sdk:ITeamDevClientUpdate"
                };
                if (conflicts.Count > 0)
                    result["hint"] = "Update applied with conflicts. Resolve them: action=resolve, strategy=mine|theirs|automerge, targets=[" + string.Join(",", conflicts) + "].";

                // Conflicts are an expected outcome, not a failure — report them as Ok so the
                // agent proceeds to resolve. A false `ok` with NO conflicts is a real failure.
                if (!ok && conflicts.Count == 0)
                    return McpResponse.Err(code: "UpdateFailed", message: "Update returned false with no conflicts reported.", hint: "Check the worker log; verify credentials and connectivity.");

                return McpResponse.Ok(code: "GxServerUpdateApplied", result: result);
            }
            catch (Exception ex)
            {
                return McpResponse.Err(code: "UpdateFailed", message: ex.Message, hint: "Check the worker log for details.");
            }
        }

        private string DoLock(KBModel model, string kbAlias, JObject args)
        {
            string target = args?["target"]?.ToString();
            if (string.IsNullOrWhiteSpace(target))
            {
                return McpResponse.Err(
                    code: "BadArgs",
                    message: "target is required for lock.",
                    hint: "Pass action=lock, target=<object name>.");
            }

            IGXserverService svc = GxMcp.Worker.Helpers.SdkServiceResolver.Resolve<IGXserverService>();
            if (svc == null) return ServerServiceUnavailable();

            try
            {
                var data = new ServerLockData
                {
                    Model = model,
                    KBAlias = kbAlias,
                    FilePath = target
                };

                ServerLockData result = svc.Lock(data);
                return McpResponse.Ok(
                    code: "GxServerLockAcquired",
                    result: new JObject
                    {
                        ["target"] = target,
                        ["locked"] = result != null,
                        ["note"] = "ServerLockData.FilePath expects the KB-relative export path, not necessarily the bare object name — unverified against a live GXserver.",
                        ["source"] = "sdk:IGXserverService"
                    });
            }
            catch (Exception ex)
            {
                return McpResponse.Err(code: "LockFailed", message: ex.Message, hint: "Check the worker log for details.");
            }
        }

        private string DoResolve(ITeamDevClientService tdSvc, KnowledgeBase kb, KBModel model, JObject args)
        {
            var targetsToken = args?["targets"] as JArray;
            if (targetsToken == null || targetsToken.Count == 0)
            {
                return McpResponse.Err(
                    code: "BadArgs",
                    message: "targets (non-empty array of object names) is required for resolve.",
                    hint: "Pass action=resolve, targets=[\"Customer\", ...], strategy=mine|theirs|automerge.");
            }

            var wanted = new HashSet<string>(
                targetsToken.Select(t => t?.ToString()).Where(s => !string.IsNullOrEmpty(s)),
                StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0)
            {
                return McpResponse.Err(code: "BadArgs", message: "targets must contain at least one non-empty object name.");
            }

            // Strategy chooses which version wins per conflicted object:
            //  mine      — keep the local object, discard the incoming server version (creds-free);
            //  theirs    — overwrite local with the server version (needs credentials);
            //  automerge — 3-way merge base+mine+theirs via IMergeService (needs credentials).
            // Default is the safe, creds-free "mine" (previous behavior only cleared the flag).
            string strategy = (args?["strategy"]?.ToString() ?? "mine").Trim().ToLowerInvariant();
            if (strategy != "mine" && strategy != "theirs" && strategy != "automerge")
                return McpResponse.Err(code: "BadArgs", message: "strategy must be one of: mine, theirs, automerge.", hint: "Default is 'mine' (keep local).");

            try
            {
                // Collect the conflicted entities matching the requested targets, keyed so we
                // can fetch base/theirs per object and MarkAsResolved at the end.
                var matches = new List<(string name, EntityKey key)>();
                foreach (var ct in new[] { UpdateConflict.YesMustOverwrite, UpdateConflict.YesWithAutoMerge })
                {
                    IEnumerable<Entity> raw = tdSvc.GetConflictEntities(model, ct);
                    if (raw == null) continue;
                    foreach (Entity e in raw)
                    {
                        string name = SafeStr(() => e.Name);
                        if (name != null && wanted.Contains(name))
                            matches.Add((name, e.Key));
                    }
                }

                if (matches.Count == 0)
                {
                    return McpResponse.Err(
                        code: "NoMatchingConflicts",
                        message: "None of the requested targets have an active conflict.",
                        hint: "Check action=conflicts for current conflict object names.");
                }

                var applied = new JArray();

                // theirs / automerge need to fetch the server version → credentials + services.
                Creds creds = default;
                IMergeService mergeSvc = null;
                if (strategy == "theirs" || strategy == "automerge")
                {
                    creds = ReadCreds(tdSvc, kb, args);
                    if (string.IsNullOrWhiteSpace(creds.Url))
                        return CredentialsRequired("resolve strategy=" + strategy);
                    if (strategy == "automerge")
                    {
                        mergeSvc = GxMcp.Worker.Helpers.SdkServiceResolver.Resolve<IMergeService>();
                        if (mergeSvc == null)
                            return McpResponse.Err(code: "GxServerServiceUnavailable", message: "IMergeService is not registered in this worker session.", hint: "Restart the worker (genexus_worker_reload mode=hard) and retry.");
                    }

                    foreach (var (name, key) in matches)
                    {
                        var outcome = new JObject { ["object"] = name, ["strategy"] = strategy };
                        try
                        {
                            KBObject mine = KBObject.Get(model, key);
                            KBObject theirs = tdSvc.GetServerObject(new ClientTeamDev.ServerObjectData(model, mine)
                            {
                                Url = creds.Url,
                                User = creds.User,
                                Password = creds.Password,
                                AuthenticationToken = creds.Token
                            });

                            if (theirs == null)
                            {
                                outcome["ok"] = false;
                                outcome["error"] = "Could not fetch the server version of the object.";
                            }
                            else if (strategy == "theirs")
                            {
                                theirs.EnsureSave();
                                outcome["ok"] = true;
                            }
                            else // automerge
                            {
                                KBObject baseObj = SafeObj(() => tdSvc.GetLastSynchedObject(model, key));
                                KBObject merged = baseObj != null
                                    ? mergeSvc.MergeObjects(baseObj, mine, theirs, model)
                                    : mergeSvc.MergeObjects(mine, theirs, model, false);
                                if (merged == null)
                                {
                                    outcome["ok"] = false;
                                    outcome["error"] = "MergeObjects returned null.";
                                }
                                else
                                {
                                    merged.EnsureSave();
                                    outcome["ok"] = true;
                                    outcome["threeWay"] = baseObj != null;
                                }
                            }
                        }
                        catch (Exception exObj)
                        {
                            outcome["ok"] = false;
                            outcome["error"] = exObj.Message;
                        }
                        applied.Add(outcome);
                    }
                }
                else
                {
                    // mine: no object mutation — the local version already wins; just clear the flag.
                    foreach (var (name, _) in matches)
                        applied.Add(new JObject { ["object"] = name, ["strategy"] = "mine", ["ok"] = true });
                }

                // Clear the conflict flags for every matched object (idempotent per the SDK).
                bool ok = tdSvc.MarkAsResolved(model, matches.Select(m => m.key).ToList());
                if (!ok)
                    return McpResponse.Err(code: "ResolveFailed", message: "MarkAsResolved returned false.", hint: "Check the worker log for details.");

                int failed = applied.Count(o => o["ok"]?.ToObject<bool?>() == false);
                return McpResponse.Ok(
                    code: "GxServerConflictsResolved",
                    result: new JObject
                    {
                        ["resolved"] = true,
                        ["strategy"] = strategy,
                        ["count"] = matches.Count,
                        ["failedObjects"] = failed,
                        ["objects"] = applied,
                        ["source"] = "sdk:ITeamDevClientService"
                    });
            }
            catch (Exception ex)
            {
                return McpResponse.Err(code: "ResolveFailed", message: ex.Message, hint: "Check the worker log for details.");
            }
        }

        // --- Team-Dev credential + helper plumbing (mutating server ops) ------------------

        private struct Creds { public string Url; public string User; public string Password; public string Token; }

        // Server credentials for the mutating paths (update-apply, resolve theirs/automerge).
        // URL auto-resolves from the linked KB; user/password/token come from the call args or
        // GXMCP_TEAMDEV_{URL,USER,PASSWORD,TOKEN} env. Never logged (secrets — global rule §10).
        private static Creds ReadCreds(ITeamDevClientService tdSvc, KnowledgeBase kb, JObject args)
        {
            string url = args?["url"]?.ToString();
            if (string.IsNullOrWhiteSpace(url)) url = Environment.GetEnvironmentVariable("GXMCP_TEAMDEV_URL");
            if (string.IsNullOrWhiteSpace(url)) { try { url = tdSvc.GetServerUrl(kb); } catch { } }

            string user = args?["user"]?.ToString();
            if (string.IsNullOrWhiteSpace(user)) user = Environment.GetEnvironmentVariable("GXMCP_TEAMDEV_USER");
            string pass = args?["password"]?.ToString();
            if (string.IsNullOrWhiteSpace(pass)) pass = Environment.GetEnvironmentVariable("GXMCP_TEAMDEV_PASSWORD");
            string token = args?["token"]?.ToString();
            if (string.IsNullOrWhiteSpace(token)) token = Environment.GetEnvironmentVariable("GXMCP_TEAMDEV_TOKEN");

            return new Creds { Url = url, User = user, Password = pass, Token = token };
        }

        private static string CredentialsRequired(string op)
        {
            return McpResponse.Err(
                code: "CredentialsRequired",
                message: "Server credentials are required for " + op + " (this operation talks to the GeneXus Server).",
                hint: "Pass user + password (and optionally url) in the call, or set GXMCP_TEAMDEV_USER / GXMCP_TEAMDEV_PASSWORD (and GXMCP_TEAMDEV_URL if the KB link doesn't resolve it).");
        }

        private static List<string> CollectConflictNames(ITeamDevClientService tdSvc, KBModel model)
        {
            var names = new List<string>();
            try
            {
                foreach (var ct in new[] { UpdateConflict.YesMustOverwrite, UpdateConflict.YesWithAutoMerge })
                {
                    IEnumerable<Entity> raw = tdSvc.GetConflictEntities(model, ct);
                    if (raw == null) continue;
                    foreach (Entity e in raw)
                    {
                        string n = SafeStr(() => e.Name);
                        if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
                    }
                }
            }
            catch { /* best-effort */ }
            return names;
        }

        private static KBObject SafeObj(Func<KBObject> f)
        {
            try { return f(); } catch { return null; }
        }

        private static string ServerServiceUnavailable()
        {
            return McpResponse.Err(
                code: "GxServerServiceUnavailable",
                message: "The GeneXus SDK's IGXserverService is not registered in this worker session (self-heal retries were exhausted).",
                hint: "Restart the worker (genexus_worker_reload mode=hard) and retry.");
        }


        private static string SafeStr(Func<string> f)
        {
            try { return f(); } catch { return null; }
        }
    }
}
