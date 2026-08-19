using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RatNav.Service;

/// <summary>
/// Pushes raid state to every connected surface.
///
/// <para>The compact overlay, the expanded panel and the browser all subscribe here, which is what
/// makes editing a plan in one appear in the others rather than each keeping a copy that drifts.
/// Push rather than poll: a position fix should reach the overlay in the time it takes to send a
/// frame, and polling for something that changes only when the player acts is waste.</para>
/// </summary>
public sealed class RaidHub
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Holds a socket open until the client goes away, sending it whatever gets published.</summary>
    public async Task AcceptAsync(WebSocket socket, RaidSession session, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        _clients[id] = socket;

        try
        {
            // A client that connects mid-raid needs the current state, not just the next change.
            await SendAsync(socket, session.View(), ct);

            var buffer = new byte[1024];

            // Nothing is expected from the client; this waits for it to close.
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // A surface closing is ordinary — someone shut a browser tab.
        }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }

    /// <summary>Sends state to everyone. Dead sockets are dropped rather than retried.</summary>
    public void Broadcast(RaidView view)
    {
        foreach (var (id, socket) in _clients)
        {
            if (socket.State != WebSocketState.Open)
            {
                _clients.TryRemove(id, out _);
                continue;
            }

            var key = id;
            _ = SendAsync(socket, view, CancellationToken.None)
                .ContinueWith(_ => _clients.TryRemove(key, out WebSocket? _), TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private static Task SendAsync(WebSocket socket, RaidView view, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(view, Json));
        return socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct);
    }
}
