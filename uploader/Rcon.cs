using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace WallpaperStudioUploader;

/// Sends one console command to a Rust server over WebRCON and returns the reply.
public static class Rcon
{
    public static async Task<string> Send(string host, int port, string password, string command, int timeoutMs = 8000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        using var ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(new Uri($"ws://{host}:{port}/{Uri.EscapeDataString(password)}"), cts.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
            throw new InvalidOperationException($"Couldn't reach RCON at {host}:{port}. Check the address, port and password, and that rcon.web is on.", e);
        }

        int id = Random.Shared.Next(1000, 99999);
        var msg = JsonSerializer.Serialize(new { Identifier = id, Message = command, Name = "WallpaperStudioUploader" });
        await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, cts.Token);

        var buf = new byte[65536];
        try
        {
            while (true)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buf, cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("The server closed RCON (wrong password?).");
                    sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                } while (!r.EndOfMessage);
                using var doc = JsonDocument.Parse(sb.ToString());
                if (doc.RootElement.TryGetProperty("Identifier", out var ide) && ide.GetInt32() == id)
                    return doc.RootElement.GetProperty("Message").GetString() ?? "";
            }
        }
        catch (OperationCanceledException)
        {
            return "(sent, but the server didn't reply in time)";
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        }
    }
}
