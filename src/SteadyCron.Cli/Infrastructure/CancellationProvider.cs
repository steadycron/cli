namespace SteadyCron.Cli.Infrastructure;

/// <summary>
/// Exposes a process-wide cancellation token that trips on the first Ctrl+C, letting in-flight
/// API calls unwind cleanly instead of hard-aborting. A second Ctrl+C terminates immediately.
/// </summary>
public sealed class CancellationProvider : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public CancellationProvider()
    {
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    public CancellationToken Token => _cts.Token;

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        if (_cts.IsCancellationRequested)
        {
            return; // second Ctrl+C: let the default handler terminate the process
        }

        e.Cancel = true; // first Ctrl+C: request cooperative cancellation
        _cts.Cancel();
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _cts.Dispose();
    }
}
