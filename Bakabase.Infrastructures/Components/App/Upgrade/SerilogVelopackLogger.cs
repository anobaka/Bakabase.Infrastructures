using System;
using Serilog.Events;
using Velopack.Logging;

namespace Bakabase.Infrastructures.Components.App.Upgrade
{
    /// <summary>
    /// Forwards Velopack's own diagnostics into the application log.
    /// <para>
    /// Velopack writes them to a log file of its own (on Windows,
    /// <c>%LocalAppData%/velopack/velopack_{AppId}.log</c>), which nobody thinks to open when
    /// an update check is being questioned. Without these lines our log records nothing but a
    /// bare "no new version": which channel was queried, which feed URL was read and which
    /// version the feed was compared against are all invisible.
    /// </para>
    /// <para>
    /// Serilog's static logger is resolved on every call on purpose.
    /// <c>VelopackApp.Build().Run()</c> must be the first thing in <c>Main</c>, before
    /// <see cref="AppService"/> installs the file sink, so capturing a logger instance when
    /// this adapter is constructed would capture the silent placeholder and drop everything.
    /// Velopack's own startup messages are still lost for that reason; every update check
    /// happens long afterwards and is logged.
    /// </para>
    /// </summary>
    public sealed class SerilogVelopackLogger : IVelopackLogger
    {
        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            var level = logLevel switch
            {
                VelopackLogLevel.Trace => LogEventLevel.Verbose,
                VelopackLogLevel.Debug => LogEventLevel.Debug,
                VelopackLogLevel.Information => LogEventLevel.Information,
                VelopackLogLevel.Warning => LogEventLevel.Warning,
                VelopackLogLevel.Error => LogEventLevel.Error,
                VelopackLogLevel.Critical => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };

            // ":l" keeps the message literal: Velopack's text is not a Serilog template and
            // may well contain braces (feed URLs carry query strings).
            global::Serilog.Log.Logger
                .ForContext("SourceContext", "Velopack")
                .Write(level, exception, "{VelopackMessage:l}", message);
        }
    }
}
