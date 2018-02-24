using Autofac;
using OmniSharp.Extensions.LanguageServer;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using LSP = OmniSharp.Extensions.LanguageServer;

namespace MSBuildProjectTools.LanguageServer
{
    using Documents;
    using Handlers;
    using Logging;
    using Utilities;

    /// <summary>
    ///     The MSBuild language server.
    /// </summary>
    static class Program
    {
        /// <summary>
        ///     The current process (used for auto-shutdown).
        /// </summary>
        static Process _currentProcess = Process.GetCurrentProcess();
        
        /// <summary>
        ///     The parent process (we automatically shut down if it exits unexpectedly).
        /// </summary>
        static Process _parentProcess;

        /// <summary>
        ///     The main program entry-point.
        /// </summary>
        static void Main()
        {
            SynchronizationContext.SetSynchronizationContext(
                new SynchronizationContext()
            );

            try
            {
                AutoDetectExtensionDirectory();

                AsyncMain().Wait();
            }
            catch (AggregateException aggregateError)
            {
                foreach (Exception unexpectedError in aggregateError.Flatten().InnerExceptions)
                {
                    Console.WriteLine(unexpectedError);
                }
            }
            catch (Exception unexpectedError)
            {
                Console.WriteLine(unexpectedError);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        ///     The asynchronous program entry-point.
        /// </summary>
        /// <returns>
        ///     A <see cref="Task"/> representing program execution.
        /// </returns>
        static async Task AsyncMain()
        {
            using (ActivityCorrelationManager.BeginActivityScope())
            using (IContainer container = BuildContainer())
            {
                var server = container.Resolve<LSP.Server.LanguageServer>();

                Log.Verbose("Language server starting in process {ProcessId}.", _currentProcess.Id);

                await server.Initialize();

                if (server.Client.ProcessId.HasValue)
                {
                    // Last-chance cleanup; if our parent process (i.e. the language client) terminates without asking us to shut down, then exit anyway.
                    Log.Verbose("Parent process is {ParentProcessId}.", server.Client.ProcessId);

                    _parentProcess = Process.GetProcessById(
                        (int)server.Client.ProcessId.Value
                    );
                    _parentProcess.EnableRaisingEvents = true;
                    _parentProcess.Exited += (sender, args) =>
                    {
                        Serilog.Log.Verbose("Parent process {ParentProcessId} has exited unexpectedly; terminating down language server process {ProcessId}.",
                            _parentProcess.Id,
                            _currentProcess.Id
                        );
                        Serilog.Log.CloseAndFlush();

                        _currentProcess.Kill();
                    };

                    Log.Verbose("Watching parent process {ParentProcessId} (will automatically shut down if it exits unexpectedly).", server.Client.ProcessId);
                }
                else
                    Log.Verbose("Cannot determine parent process Id.");

                await server.WasShutDown;

                Log.Verbose("Server shutdown.");
            }
        }

        /// <summary>
        ///     Build a container for language server components.
        /// </summary>
        /// <returns>
        ///     The container.
        /// </returns>
        static IContainer BuildContainer()
        {
            ContainerBuilder builder = new ContainerBuilder();
            
            builder.RegisterModule<LoggingModule>();
            builder.RegisterModule<LanguageServerModule>();

            return builder.Build();
        }

        /// <summary>
        ///     Auto-detect the directory containing the extension's files.
        /// </summary>
        static void AutoDetectExtensionDirectory()
        {
            string extensionDir = Environment.GetEnvironmentVariable("MSBUILD_PROJECT_TOOLS_DIR");
            if (String.IsNullOrWhiteSpace(extensionDir))
            {
                extensionDir = Path.Combine(
                    AppContext.BaseDirectory, "..", ".."
                );
            }
            extensionDir = Path.GetFullPath(extensionDir);
            Environment.SetEnvironmentVariable("MSBUILD_PROJECT_TOOLS_DIR", extensionDir);
        }
    }
}
