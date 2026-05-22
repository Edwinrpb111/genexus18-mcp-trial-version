using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GxMcp.Worker.Helpers;
using Microsoft.Build.Framework;

namespace GxMcp.Worker.Services
{
    // v2.6.6 Stream D — orchestrates in-process invocation of the two GeneXus
    // MSBuild tasks the IDE pipeline relies on:
    //   - Genexus.MsBuild.Tasks.SpecifyOneOnly   (when action=Build with targets)
    //   - Genexus.MsBuild.Tasks.IdeWebBuildAndDeploy
    //
    // The worker already holds the KB open through KbService._kb; this runner
    // shares that instance via the task's public `KB` property instead of
    // spawning MSBuild.exe and re-opening the KB out-of-process. Expected
    // speedup: 5-10 min → 5-30 s for targeted builds.
    internal static class InProcessBuildRunner
    {
        // Type cache — Assembly.LoadFrom is amortised across calls.
        private static Type _typeSpecifyOneOnly;
        private static Type _typeIdeWebBuildAndDeploy;
        private static bool _assemblyLoadAttempted;
        private static readonly object _typeCacheLock = new object();

        // Public entry. Returns true on success (caller writes the final
        // status). Returns false on any failure (caller falls back to the
        // external MSBuild.exe spawn). Never throws.
        public static bool Run(
            BuildService.BuildTaskStatus status,
            string action,
            List<string> targets,
            Action<BuildService.BuildTaskStatus, string, bool> sink,
            object kbHandle,
            object kbLock,
            bool skipFullDeploy = false)
        {
            if (status == null) return false;
            if (kbHandle == null)
            {
                Logger.Warn("[BUILD-INPROCESS] kb handle is null — skipping in-process path");
                return false;
            }
            if (kbLock == null)
            {
                Logger.Warn("[BUILD-INPROCESS] kb lock is null — skipping in-process path");
                return false;
            }

            // Stream D follow-up: only Build/RebuildAll are wired through the
            // in-process task pair (SpecifyOneOnly + IdeWebBuildAndDeploy). Other
            // actions need distinct tasks the external-msbuild template owns
            // (Reorg → CheckAndInstallDatabase, Validate/Check → CheckKnowledgeBase,
            // Sync → full IdeWebBuildAndDeploy). Refuse here so RunBuild's
            // unchanged fallback runs them through MSBuild.exe.
            if (!string.IsNullOrEmpty(action)
                && !action.Equals("Build", StringComparison.OrdinalIgnoreCase)
                && !action.Equals("RebuildAll", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info("[BUILD-INPROCESS] action='" + action + "' not supported in-process — falling back to MSBuild.exe");
                return false;
            }

            try
            {
                if (!EnsureTypesLoaded())
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[BUILD-INPROCESS] EnsureTypesLoaded threw: " + ex.Message);
                return false;
            }

            // Wrap the BuildService sink so the engine sees a (line,isError) action
            // and HandleLine still receives the status object.
            Action<string, bool> lineSink = (l, e) => { try { sink(status, l, e); } catch { } };
            var engine = new InProcessBuildEngine(lineSink);

            lock (kbLock)
            {
                try
                {
                    bool isBuildWithTargets = action != null
                        && action.Equals("Build", StringComparison.OrdinalIgnoreCase)
                        && targets != null
                        && targets.Count > 0;

                    if (isBuildWithTargets)
                    {
                        if (!ExecuteSpecifyOneOnly(kbHandle, targets, engine))
                        {
                            // Task returned false → spec errors are already logged
                            // via the engine sink; let the caller decide whether to
                            // fall back. Treat as failure of the in-process path.
                            return false;
                        }
                    }

                    bool forceRebuild = action != null && action.Equals("RebuildAll", StringComparison.OrdinalIgnoreCase);

                    // Friction 2026-05-22 (experimental): when the caller passes
                    // skipFullDeploy=true on a single-target Build with no callees,
                    // we stop after SpecifyOneOnly. Spec+Gen wrote the .cs sources
                    // and the next IDE Build All (or a normal lifecycle build)
                    // picks the DLL up. This skips Copying Module GeneXus / GAM /
                    // Crypto and the WebAppConfig step — turning 5-13min into ~30s.
                    // EXPERIMENTAL: validate live against the runtime before
                    // making this the default for includeCallees=none.
                    if (skipFullDeploy && isBuildWithTargets && !forceRebuild)
                    {
                        lineSink("[BUILD-INPROCESS] skipFullDeploy=true — stopping after SpecifyOneOnly. DLL output is NOT redeployed; the IDE build task is bypassed.", false);
                        return true;
                    }

                    if (!ExecuteIdeWebBuildAndDeploy(kbHandle, engine, forceRebuild))
                    {
                        return false;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    LogExceptionChain("Outer-Execute", ex);
                    return false;
                }
            }
        }

        private static bool EnsureTypesLoaded()
        {
            lock (_typeCacheLock)
            {
                if (_typeSpecifyOneOnly != null && _typeIdeWebBuildAndDeploy != null) return true;
                if (_assemblyLoadAttempted && (_typeSpecifyOneOnly == null || _typeIdeWebBuildAndDeploy == null))
                {
                    // We already failed once; don't spam the log on every build.
                    return false;
                }
                _assemblyLoadAttempted = true;

                string gxDir = Environment.GetEnvironmentVariable("GX_PROGRAM_DIR")
                               ?? @"C:\Program Files (x86)\GeneXus\GeneXus18";
                if (string.IsNullOrWhiteSpace(gxDir) || !Directory.Exists(gxDir))
                {
                    Logger.Error("[BUILD-INPROCESS] GX_PROGRAM_DIR not found: " + gxDir);
                    return false;
                }

                string asmPath = Path.Combine(gxDir, "Genexus.MsBuild.Tasks.dll");
                if (!File.Exists(asmPath))
                {
                    Logger.Error("[BUILD-INPROCESS] Genexus.MsBuild.Tasks.dll not found at " + asmPath);
                    return false;
                }

                Assembly asm;
                try
                {
                    asm = Assembly.LoadFrom(asmPath);
                }
                catch (Exception ex)
                {
                    Logger.Error("[BUILD-INPROCESS] LoadFrom failed for " + asmPath + ": " + ex.Message);
                    return false;
                }

                _typeSpecifyOneOnly = asm.GetType("Genexus.MsBuild.Tasks.SpecifyOneOnly", throwOnError: false);
                _typeIdeWebBuildAndDeploy = asm.GetType("Genexus.MsBuild.Tasks.IdeWebBuildAndDeploy", throwOnError: false);

                if (_typeSpecifyOneOnly == null || _typeIdeWebBuildAndDeploy == null)
                {
                    Logger.Error("[BUILD-INPROCESS] Required task types missing in Genexus.MsBuild.Tasks.dll "
                                 + "(SpecifyOneOnly=" + (_typeSpecifyOneOnly != null) + ", "
                                 + "IdeWebBuildAndDeploy=" + (_typeIdeWebBuildAndDeploy != null) + ")");
                    return false;
                }

                Logger.Info("[BUILD-INPROCESS] Task types loaded from " + asmPath);

                // Force-trigger Artech.MsBuild.Common.ArtechTask's static ctor in
                // isolation so we can capture the REAL cause (the live runtime hides
                // it inside two layers of TargetInvocationException + TypeInitException).
                try
                {
                    var artechAsm = asm.GetReferencedAssemblies()
                        .FirstOrDefault(n => n.Name.Equals("Artech.MsBuild.Common", StringComparison.OrdinalIgnoreCase));
                    if (artechAsm != null)
                    {
                        var loaded = Assembly.Load(artechAsm);
                        var artechTaskType = loaded.GetType("Artech.MsBuild.Common.ArtechTask", throwOnError: false);
                        if (artechTaskType != null)
                        {
                            try
                            {
                                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                                    artechTaskType.TypeHandle);
                                Logger.Info("[BUILD-INPROCESS] ArtechTask static ctor: OK");
                            }
                            catch (Exception ctorEx)
                            {
                                LogExceptionChain("ArtechTask-cctor", ctorEx);
                            }
                        }
                        else
                        {
                            Logger.Warn("[BUILD-INPROCESS] Artech.MsBuild.Common.ArtechTask type NOT FOUND in " + loaded.Location);
                        }
                    }
                    else
                    {
                        Logger.Warn("[BUILD-INPROCESS] Artech.MsBuild.Common assembly is not a referenced dependency of Genexus.MsBuild.Tasks");
                    }
                }
                catch (Exception probeEx)
                {
                    LogExceptionChain("ArtechTask-probe", probeEx);
                }

                return true;
            }
        }

        private static bool ExecuteSpecifyOneOnly(object kbHandle, List<string> targets, IBuildEngine engine)
        {
            try
            {
                object task = Activator.CreateInstance(_typeSpecifyOneOnly);
                SetProp(task, "KB", kbHandle);
                SetProp(task, "ObjectNames", string.Join(";", targets));
                SetProp(task, "BuildEngine", engine);

                var execute = _typeSpecifyOneOnly.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance);
                if (execute == null)
                {
                    Logger.Error("[BUILD-INPROCESS] SpecifyOneOnly.Execute method not found");
                    return false;
                }
                object result = execute.Invoke(task, null);
                bool ok = result is bool b && b;
                if (!ok) Logger.Warn("[BUILD-INPROCESS] SpecifyOneOnly.Execute returned false");
                return ok;
            }
            catch (Exception ex)
            {
                LogExceptionChain("SpecifyOneOnly", ex);
                return false;
            }
        }

        // Unwrap nested TargetInvocationException / TypeInitializationException so
        // the real root cause (usually a missing assembly / IBuildEngine surface
        // mismatch) shows in worker_debug.log instead of the generic outer wrapper.
        private static void LogExceptionChain(string what, Exception ex)
        {
            int depth = 0;
            for (Exception e = ex; e != null && depth < 8; e = e.InnerException, depth++)
            {
                string indent = new string(' ', depth * 2);
                Logger.Error("[BUILD-INPROCESS] " + indent + what + " ex[" + depth + "] "
                             + e.GetType().FullName + ": " + e.Message);
                if (e is System.Reflection.ReflectionTypeLoadException rtle && rtle.LoaderExceptions != null)
                {
                    int li = 0;
                    foreach (var le in rtle.LoaderExceptions)
                    {
                        Logger.Error("[BUILD-INPROCESS] " + indent + "  loader[" + (li++) + "] "
                                     + le?.GetType().FullName + ": " + le?.Message);
                    }
                }
                if (depth == 0 && e.StackTrace != null)
                {
                    foreach (var line in e.StackTrace.Split('\n').Take(5))
                        Logger.Error("[BUILD-INPROCESS]   at " + line.Trim());
                }
            }
        }

        private static bool ExecuteIdeWebBuildAndDeploy(object kbHandle, IBuildEngine engine, bool forceRebuild)
        {
            try
            {
                object task = Activator.CreateInstance(_typeIdeWebBuildAndDeploy);
                SetProp(task, "KB", kbHandle);
                SetProp(task, "ForceRebuild", forceRebuild);
                SetProp(task, "CompileMains", true);
                SetProp(task, "Output", "IDE");
                SetProp(task, "EventsSuspended", true);
                SetProp(task, "BuildEngine", engine);

                var execute = _typeIdeWebBuildAndDeploy.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance);
                if (execute == null)
                {
                    Logger.Error("[BUILD-INPROCESS] IdeWebBuildAndDeploy.Execute method not found");
                    return false;
                }
                object result = execute.Invoke(task, null);
                bool ok = result is bool b && b;
                if (!ok) Logger.Warn("[BUILD-INPROCESS] IdeWebBuildAndDeploy.Execute returned false");
                return ok;
            }
            catch (Exception ex)
            {
                LogExceptionChain("IdeWebBuildAndDeploy", ex);
                return false;
            }
        }

        private static void SetProp(object target, string name, object value)
        {
            if (target == null) return;
            var pi = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite)
            {
                // Not all properties are guaranteed to exist across GeneXus versions —
                // missing optional properties are ignored, required ones blow up
                // later in Execute() and surface through the catch.
                Logger.Debug("[BUILD-INPROCESS] property '" + name + "' not settable on " + target.GetType().Name);
                return;
            }
            pi.SetValue(target, value);
        }

        // Test seam — exposes the type-resolution result so unit tests can
        // assert SDK presence without needing a live KB.
        internal static bool TryResolveTypes(out string error)
        {
            error = null;
            if (EnsureTypesLoaded()) return true;
            error = "Genexus.MsBuild.Tasks types not loaded — see worker_debug.log";
            return false;
        }
    }
}
