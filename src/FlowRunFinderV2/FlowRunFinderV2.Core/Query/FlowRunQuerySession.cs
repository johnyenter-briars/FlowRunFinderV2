namespace FlowRunFinderV2.Core.Query;

public sealed class FlowRunQuerySession : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    public bool IsCancellationRequested => _cancellationTokenSource.IsCancellationRequested;

    public void Cancel()
    {
        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
    }
}
