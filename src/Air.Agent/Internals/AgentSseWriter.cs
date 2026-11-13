namespace Platform.Agent.Internals;

internal sealed class AgentSseWriter : IAsyncDisposable
{
  private readonly HttpResponse _response;
  private readonly CancellationToken _requestAborted;
  private readonly SemaphoreSlim _writeLock = new(1, 1);
  private readonly CancellationTokenSource _keepaliveCts;
  private readonly Task _keepaliveTask;
  private int _disposed;
  private int _keepaliveTicks;

  internal int KeepaliveTicks => Volatile.Read(ref _keepaliveTicks);

  public AgentSseWriter(
    HttpResponse response,
    TimeSpan keepaliveInterval,
    CancellationToken requestAborted,
    TimeProvider? timeProvider = null)
  {
    _response = response;
    _requestAborted = requestAborted;
    _response.Headers.ContentType = "text/event-stream; charset=utf-8";
    _response.Headers.CacheControl = "no-cache";
    _response.Headers.Connection = "keep-alive";
    _response.Headers["X-Accel-Buffering"] = "no";

    _keepaliveCts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
    _keepaliveTask = keepaliveInterval > TimeSpan.Zero
      ? RunKeepaliveLoopAsync(keepaliveInterval, timeProvider ?? TimeProvider.System, _keepaliveCts.Token)
      : Task.CompletedTask;
  }

  public async Task WriteEventAsync(AgentStreamEventDto evt, CancellationToken ct)
  {
    if (Volatile.Read(ref _disposed) != 0)
    {
      return;
    }

    await _writeLock.WaitAsync(ct);
    try
    {
      if (Volatile.Read(ref _disposed) != 0)
      {
        return;
      }

      var json = JsonSerializer.Serialize(evt, AgentEndpointHandlers.JsonOptions);
      await _response.WriteAsync($"data: {json}\n\n", ct);
      await _response.Body.FlushAsync(ct);
    }
    finally
    {
      _writeLock.Release();
    }
  }

  private async Task RunKeepaliveLoopAsync(TimeSpan interval, TimeProvider timeProvider, CancellationToken loopToken)
  {
    try
    {
      using var timer = new PeriodicTimer(interval, timeProvider);
      while (await timer.WaitForNextTickAsync(loopToken))
      {
        await _writeLock.WaitAsync(loopToken);
        try
        {
          if (Volatile.Read(ref _disposed) != 0)
          {
            break;
          }

          await _response.WriteAsync(": keepalive\n\n", _requestAborted);
          await _response.Body.FlushAsync(_requestAborted);
          Interlocked.Increment(ref _keepaliveTicks);
        }
        finally
        {
          _writeLock.Release();
        }
      }
    }
    catch (Exception)
    {
      // Normal loop cancellation, client disconnect, or disposed response stream
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref _disposed, 1) != 0)
    {
      return;
    }

    await _keepaliveCts.CancelAsync();
    await _keepaliveTask;
    _keepaliveCts.Dispose();
  }
}
