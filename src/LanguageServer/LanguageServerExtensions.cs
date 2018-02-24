using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using LSP = OmniSharp.Extensions.LanguageServer;

namespace MSBuildProjectTools.LanguageServer
{
    /// <summary>
    ///     Extension methods for <see cref="LanguageServer"/>.
    /// </summary>
    static class LanguageServerExtensions
    {
        /// <summary>
        ///     Create a <see cref="Task"/> representing either an orderly shutdown of the language server, or its parent process exiting unexpectedly.
        /// </summary>
        /// <param name="server">
        ///     The language server
        /// </param>
        /// <returns>
        ///     The <see cref="Task"/>.
        /// </returns>
        public static Task WasShutDownOrParentProcessTerminated(this LSP.Server.LanguageServer server)
        {
            if (!server.Client.ProcessId.HasValue)
            {
                Log.Verbose("Cannot determine parent process Id; language server will only shut down if requested by client.");

                return server.WasShutDown;
            }

            // Last-chance cleanup; if our parent process (i.e. the language client) terminates without asking us to shut down, then exit anyway.
            Log.Verbose("Watching parent process {ParentProcessId} (language server will automatically shut down if it exits unexpectedly).", server.Client.ProcessId);

            var parentProcessTerminated = new TaskCompletionSource<bool>();

            Process parentProcess = Process.GetProcessById((int)server.Client.ProcessId.Value);
            parentProcess.Exited += (_, __) =>
            {
                Log.Warning("Parent process {ParentProcessId} has exited unexpectedly; language server will shut down.", parentProcess.Id);
                parentProcessTerminated.SetResult(true);
            };
            parentProcess.EnableRaisingEvents = true;

            return Task.WhenAny(
                server.WasShutDown,
                parentProcessTerminated.Task
            );
        }
    }
}
