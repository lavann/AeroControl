using System.ComponentModel;
using System.Diagnostics;

namespace AeroControl.Core.Services;

internal interface IBatteryReportProcess : IDisposable
{
    Task<string> StandardOutput { get; }

    Task<string> StandardError { get; }

    int ExitCode { get; }

    bool HasExited { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill();
}

internal interface IBatteryReportProcessFactory
{
    IBatteryReportProcess Start(string reportPath);
}

internal sealed class SystemBatteryReportProcessFactory : IBatteryReportProcessFactory
{
    public IBatteryReportProcess Start(string reportPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/batteryreport");
        startInfo.ArgumentList.Add("/xml");
        startInfo.ArgumentList.Add("/output");
        startInfo.ArgumentList.Add(reportPath);

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows could not start powercfg.exe.");
        return new SystemBatteryReportProcess(process);
    }
}

internal sealed class SystemBatteryReportProcess : IBatteryReportProcess
{
    private readonly Process _process;

    public SystemBatteryReportProcess(Process process)
    {
        _process = process;
        StandardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        StandardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
    }

    public Task<string> StandardOutput { get; }

    public Task<string> StandardError { get; }

    public int ExitCode => _process.ExitCode;

    public bool HasExited => _process.HasExited;

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _process.WaitForExitAsync(cancellationToken);

    public void Kill()
    {
        if (_process.HasExited)
        {
            return;
        }

        try
        {
            _process.Kill(true);
        }
        catch (Win32Exception treeException)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(false);
                }
            }
            catch (Exception processException) when (
                processException is Win32Exception or InvalidOperationException)
            {
                throw new AggregateException(treeException, processException);
            }
        }
        catch (InvalidOperationException)
        {
            // The child exited between the state check and termination request.
        }
    }

    public void Dispose()
    {
        _process.Dispose();
        GC.SuppressFinalize(this);
    }
}
