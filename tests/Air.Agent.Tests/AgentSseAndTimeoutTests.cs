using System.Text;
using Microsoft.AspNetCore.Http;
using Platform.Agent.Internals;
using Platform.Agent.Tests.Internals;

namespace Platform.Agent.Tests;

public sealed class AgentSseAndTimeoutTests
{
  [Fact]
  public async Task AgentSseWriter_EmitsHeadersKeepaliveCommentsAndEventsWithoutInterleaving()
  {
    var httpContext = new DefaultHttpContext();
    using var bodyStream = new MemoryStream();
    httpContext.Response.Body = bodyStream;
    var clock = new ManualTimerTimeProvider();

    await using (var sse = new AgentSseWriter(
      httpContext.Response,
      TimeSpan.FromSeconds(15),
      CancellationToken.None,
      clock))
    {
      await sse.WriteEventAsync(
        new AgentStreamEventDto { Type = AgentStreamEventType.Status, Text = "Analyzing" },
        CancellationToken.None);

      clock.FireTick();
      using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
      while (sse.KeepaliveTicks < 1 && !waitCts.IsCancellationRequested)
      {
        await Task.Yield();
      }

      await sse.WriteEventAsync(
        new AgentStreamEventDto { Type = AgentStreamEventType.Done, SessionId = "sess-1" },
        CancellationToken.None);
    }

    Assert.Equal("text/event-stream; charset=utf-8", httpContext.Response.Headers.ContentType.ToString());
    Assert.Equal("no-cache", httpContext.Response.Headers.CacheControl.ToString());
    Assert.Equal("keep-alive", httpContext.Response.Headers.Connection.ToString());
    Assert.Equal("no", httpContext.Response.Headers["X-Accel-Buffering"].ToString());

    var payload = Encoding.UTF8.GetString(bodyStream.ToArray());
    var frames = payload.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

    Assert.Equal(3, frames.Length);
    Assert.StartsWith("data: ", frames[0], StringComparison.Ordinal);
    Assert.Contains("\"status\"", frames[0], StringComparison.Ordinal);
    Assert.Equal(": keepalive", frames[1]);
    Assert.StartsWith("data: ", frames[2], StringComparison.Ordinal);
    Assert.Contains("\"done\"", frames[2], StringComparison.Ordinal);
  }
}
