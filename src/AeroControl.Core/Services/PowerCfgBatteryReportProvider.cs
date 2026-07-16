using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using AeroControl.Core.Abstractions;
using AeroControl.Core.Models;

namespace AeroControl.Core.Services;

public sealed class PowerCfgBatteryReportProvider : IBatteryReportProvider
{
    private readonly IBatteryReportProcessFactory _processFactory;
    private readonly TimeSpan _reportTimeout;
    private readonly TimeSpan _terminationTimeout;
    private readonly string _temporaryDirectory;

    public PowerCfgBatteryReportProvider()
        : this(
            new SystemBatteryReportProcessFactory(),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(3),
            Path.GetTempPath())
    {
    }

    internal PowerCfgBatteryReportProvider(
        IBatteryReportProcessFactory processFactory,
        TimeSpan reportTimeout,
        TimeSpan terminationTimeout,
        string temporaryDirectory)
    {
        _processFactory = processFactory;
        _reportTimeout = reportTimeout;
        _terminationTimeout = terminationTimeout;
        _temporaryDirectory = temporaryDirectory;
    }

    public async Task<BatteryReportData?> GetReportAsync(
        CancellationToken cancellationToken = default)
    {
        var reportPath = Path.Combine(
            _temporaryDirectory,
            $"AeroControl-battery-{Guid.NewGuid():N}.xml");
        BatteryReportData? report = null;
        Exception? operationFailure = null;
        try
        {
            report = await GenerateReportAsync(reportPath, cancellationToken);
        }
        catch (Exception exception)
        {
            operationFailure = exception;
        }

        Exception? cleanupFailure = null;
        try
        {
            await DeleteReportAsync(reportPath);
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }

        if (operationFailure is not null)
        {
            if (cleanupFailure is not null)
            {
                throw new AggregateException(operationFailure, cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }

        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }

        return report;
    }

    private async Task<BatteryReportData?> GenerateReportAsync(
        string reportPath,
        CancellationToken cancellationToken)
    {
        using var process = _processFactory.Start(reportPath);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_reportTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException cancellation)
        {
            await TerminateAndDrainAsync(process, cancellation);
            throw;
        }

        await DrainOutputAsync(process.StandardOutput, process.StandardError);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"powercfg.exe exited with code {process.ExitCode}."
                    : error.Trim());
        }

        return File.Exists(reportPath)
            ? ParseReport(await File.ReadAllTextAsync(reportPath, cancellationToken))
            : null;
    }

    public static BatteryReportData? ParseReport(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.None);
        var root = document.Root;
        if (root is null)
        {
            return null;
        }

        var ns = root.GetDefaultNamespace();
        var battery = root
            .Element(ns + "Batteries")?
            .Elements(ns + "Battery")
            .FirstOrDefault();
        if (battery is null)
        {
            return null;
        }

        return new BatteryReportData(
            GetText(battery, ns, "Id"),
            GetText(battery, ns, "Manufacturer"),
            GetText(battery, ns, "Chemistry"),
            GetInt32(battery, ns, "DesignCapacity"),
            GetInt32(battery, ns, "FullChargeCapacity"),
            GetInt32(battery, ns, "CycleCount"));
    }

    private static string GetText(XElement parent, XNamespace ns, string name) =>
        parent.Element(ns + name)?.Value.Trim() ?? string.Empty;

    private static int? GetInt32(XElement parent, XNamespace ns, string name) =>
        int.TryParse(
            parent.Element(ns + name)?.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    private async Task TerminateAndDrainAsync(
        IBatteryReportProcess process,
        OperationCanceledException cancellation)
    {
        var failures = new List<Exception>();
        for (var attempt = 0; attempt < 2 && !process.HasExited; attempt++)
        {
            try
            {
                process.Kill();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            using var timeout = new CancellationTokenSource(_terminationTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException exception)
            {
                failures.Add(exception);
            }
        }

        if (!process.HasExited)
        {
            throw new AggregateException(
                "AeroControl could not terminate the timed-out powercfg.exe process.",
                [cancellation, .. failures]);
        }

        await DrainOutputAsync(process.StandardOutput, process.StandardError)
            .WaitAsync(_terminationTimeout);
    }

    private static async Task DrainOutputAsync(params Task<string>[] readers)
    {
        try
        {
            await Task.WhenAll(readers);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException)
        {
            // The child is already terminated; output is discarded during cleanup.
        }
    }

    private static async Task DeleteReportAsync(string reportPath)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                File.Delete(reportPath);
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)));
            }
        }

        try
        {
            if (File.Exists(reportPath))
            {
                await File.WriteAllTextAsync(reportPath, string.Empty);
                File.Delete(reportPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                "AeroControl could not remove its temporary battery report.",
                exception);
        }
    }
}
