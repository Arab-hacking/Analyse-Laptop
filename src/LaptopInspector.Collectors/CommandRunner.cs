using System.Diagnostics;
using System.Text;

namespace LaptopInspector.Collectors;

internal sealed record CommandExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    TimeSpan Duration);

internal static class CommandRunner
{
    public static async Task<CommandExecutionResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        using Process process = new();
        process.StartInfo.FileName = fileName;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        process.StartInfo.CreateNoWindow = true;
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Не удалось запустить процесс " + fileName + ".");
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        bool timedOut = false;

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(timeout);
        }

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (NotSupportedException)
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                }
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        stopwatch.Stop();

        return new CommandExecutionResult(
            timedOut ? -1 : process.ExitCode,
            stdout.Trim(),
            stderr.Trim(),
            timedOut,
            stopwatch.Elapsed);
    }
}
