using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Item 71 — `genexus_github action=create_pr`. Shells out to the `gh` CLI.
    /// Returns the PR URL on success or a typed `GhCliNotInstalled` envelope.
    /// </summary>
    public class GithubService
    {
        private readonly KbService _kbService;

        public GithubService(KbService kbService) { _kbService = kbService; }

        public string CreatePr(string title, string body, string baseBranch, string workingDir)
        {
            if (string.IsNullOrWhiteSpace(title))
                return Err("title is required.");

            // SECURITY: title / body / baseBranch are LLM-controlled. They flow
            // into a Windows command line; if any leading dash makes them look
            // like a `gh` flag the call semantics change silently. Reject
            // values that begin with '-' so they can't be re-parsed as flags
            // (the legitimate values for these never start with '-').
            if (title.TrimStart().StartsWith("-"))
                return new JObject { ["status"] = "Error", ["code"] = "InvalidTitle", ["message"] = "title may not start with '-'." }.ToString(Newtonsoft.Json.Formatting.None);
            if (!string.IsNullOrWhiteSpace(baseBranch) && baseBranch.TrimStart().StartsWith("-"))
                return new JObject { ["status"] = "Error", ["code"] = "InvalidBaseBranch", ["message"] = "baseBranch may not start with '-'." }.ToString(Newtonsoft.Json.Formatting.None);

            string cwd = string.IsNullOrEmpty(workingDir) ? (TryGetKbPath() ?? Directory.GetCurrentDirectory()) : workingDir;

            var argList = new System.Collections.Generic.List<string> { "pr", "create", "--title", title };
            if (!string.IsNullOrWhiteSpace(body)) { argList.Add("--body"); argList.Add(body); }
            if (!string.IsNullOrWhiteSpace(baseBranch)) { argList.Add("--base"); argList.Add(baseBranch); }

            int exit;
            string stdout, stderr;
            try
            {
                exit = Run("gh", argList, cwd, out stdout, out stderr);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return new JObject
                {
                    ["status"] = "Error",
                    ["code"] = "GhCliNotInstalled",
                    ["hint"] = "Install GitHub CLI from https://cli.github.com/ and run `gh auth login`."
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex) { return Err(ex.Message); }

            if (exit != 0)
            {
                return new JObject
                {
                    ["status"] = "Error",
                    ["code"] = "GhExitNonZero",
                    ["exitCode"] = exit,
                    ["stderr"] = stderr ?? "",
                    ["cwd"] = cwd
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            string url = (stdout ?? "").Trim();
            return new JObject
            {
                ["status"] = "Success",
                ["url"] = url,
                ["cwd"] = cwd
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private string TryGetKbPath() { try { return _kbService?.GetKbPath(); } catch { return null; } }
        private static string Err(string m) => new JObject { ["status"] = "Error", ["message"] = m }.ToString(Newtonsoft.Json.Formatting.None);

        /// <summary>
        /// Windows CommandLineToArgv-compatible quoting per the rules at
        /// https://learn.microsoft.com/en-us/archive/blogs/twistylittlepassagesallalike/.
        /// The naive `Replace("\"","\\\"")` pattern leaves trailing backslashes
        /// unescaped, so a value ending in '\' lets the closing quote get
        /// consumed and the next token bleeds into the argument — classic
        /// Windows arg-confusion. This version doubles every run of
        /// backslashes that precede a quote (and the closing quote).
        /// </summary>
        internal static string ArgvQuote(string arg)
        {
            if (arg == null) arg = string.Empty;
            // If there are no problematic characters, no quoting needed.
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
                return arg;
            var sb = new StringBuilder();
            sb.Append('"');
            for (int i = 0; i < arg.Length; i++)
            {
                int backslashes = 0;
                while (i < arg.Length && arg[i] == '\\') { backslashes++; i++; }
                if (i == arg.Length)
                {
                    // Escape all backslashes, but let the terminating quote be added below.
                    sb.Append('\\', backslashes * 2);
                    break;
                }
                if (arg[i] == '"')
                {
                    // Escape all backslashes and the following quote.
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append(arg[i]);
                }
                else
                {
                    // Backslashes aren't special here.
                    sb.Append('\\', backslashes);
                    sb.Append(arg[i]);
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static int Run(string exe, System.Collections.Generic.List<string> args, string cwd, out string stdout, out string stderr)
        {
            var sb = new StringBuilder();
            foreach (var a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(ArgvQuote(a));
            }
            var psi = new ProcessStartInfo(exe, sb.ToString())
            {
                WorkingDirectory = cwd,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            using (var p = Process.Start(psi))
            {
                if (p == null) { stdout = ""; stderr = "Process.Start returned null"; return -1; }
                try { p.StandardInput.Close(); } catch { }
                var outSb = new StringBuilder();
                var errSb = new StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) outSb.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) errSb.AppendLine(e.Data); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } stdout = outSb.ToString(); stderr = "gh timed out"; return -1; }
                p.WaitForExit();
                stdout = outSb.ToString();
                stderr = errSb.ToString();
                return p.ExitCode;
            }
        }
    }
}
