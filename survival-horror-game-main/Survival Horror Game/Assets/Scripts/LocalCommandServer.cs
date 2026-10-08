using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Tek bir TCP portunda hem telefon kumanda sayfasını (HTTP) hem de komut kanalını (WebSocket, RFC 6455) sunar.
/// Unity API'si KULLANMAZ; tüm olaylar thread-pool iş parçacıklarından tetiklenir.
/// Ana iş parçacığına aktarım NetworkInputController tarafından yapılır.
/// </summary>
public sealed class LocalCommandServer : IDisposable
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const int MaxPayloadBytes = 4096;
    private const int HeaderTimeoutMs = 5000;

    private readonly int _port;
    private readonly ConcurrentDictionary<TcpClient, byte> _activeClients = new ConcurrentDictionary<TcpClient, byte>();
    private TcpListener _listener;
    private volatile bool _stopping;
    private int _clientCount;

    /// <summary>Her komut (büyük harfe çevrilmiş, kırpılmış) için tetiklenir. Thread-pool iş parçacığında çalışır.</summary>
    public event Action<string> CommandReceived;

    /// <summary>Teşhis amaçlı günlük çıktısı (thread-pool iş parçacığında çalışabilir).</summary>
    public Action<string> Log;

    public int Port { get { return _port; } }
    public bool IsListening { get; private set; }
    public string ErrorMessage { get; private set; }
    public int ClientCount { get { return Volatile.Read(ref _clientCount); } }

    public LocalCommandServer(int port)
    {
        _port = port;
    }

    public bool Start()
    {
        if (IsListening) return true;

        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start(16);
        }
        catch (Exception e)
        {
            ErrorMessage = "Port " + _port + " dinlenemedi: " + e.Message;
            IsListening = false;
            return false;
        }

        IsListening = true;
        ErrorMessage = null;
        _stopping = false;
        Task.Run(AcceptLoopAsync);
        return true;
    }

    public void Dispose()
    {
        _stopping = true;
        IsListening = false;

        try { if (_listener != null) _listener.Stop(); } catch { /* yoksay */ }

        foreach (TcpClient client in _activeClients.Keys)
        {
            try { client.Close(); } catch { /* yoksay */ }
        }
        _activeClients.Clear();
    }

    // ------------------------------------------------------------ Kabul döngüsü

    private async Task AcceptLoopAsync()
    {
        while (!_stopping)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (ObjectDisposedException) { break; }
            catch (InvalidOperationException) { break; }
            catch (SocketException)
            {
                if (_stopping) break;
                continue;
            }

            _activeClients[client] = 0;
            Task ignored = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        bool counted = false;
        try
        {
            using (client)
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();

                string header = await ReadHeaderAsync(stream);
                if (header == null) return;

                string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
                string requestLine = lines[0];
                string wsKey = FindHeader(lines, "Sec-WebSocket-Key");
                string upgrade = FindHeader(lines, "Upgrade");
                bool isWebSocket = wsKey != null && upgrade != null &&
                                   upgrade.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isWebSocket)
                {
                    await ServeHttpAsync(stream, requestLine);
                    return;
                }

                await SendHandshakeAsync(stream, wsKey);

                counted = true;
                Interlocked.Increment(ref _clientCount);
                Log?.Invoke("Telefon baglandi: " + client.Client.RemoteEndPoint);

                await ReceiveLoopAsync(stream);
            }
        }
        catch (Exception)
        {
            // Bağlantı koptu / sunucu kapanıyor: sessizce bitir.
        }
        finally
        {
            byte removed;
            _activeClients.TryRemove(client, out removed);
            if (counted)
            {
                Interlocked.Decrement(ref _clientCount);
                Log?.Invoke("Telefon baglantisi kapandi.");
            }
        }
    }

    // ------------------------------------------------------------------ HTTP

    private static async Task<string> ReadHeaderAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        int length = 0;
        Task timeout = Task.Delay(HeaderTimeoutMs);

        while (length < buffer.Length)
        {
            Task<int> read = stream.ReadAsync(buffer, length, buffer.Length - length);
            Task finished = await Task.WhenAny(read, timeout);
            if (finished != read) return null;

            int n = await read;
            if (n <= 0) return null;
            length += n;

            for (int i = 3; i < length; i++)
            {
                if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
                    return Encoding.ASCII.GetString(buffer, 0, length);
            }
        }
        return null;
    }

    private static string FindHeader(string[] lines, string name)
    {
        string prefix = name + ":";
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return lines[i].Substring(prefix.Length).Trim();
        }
        return null;
    }

    private static async Task ServeHttpAsync(NetworkStream stream, string requestLine)
    {
        string[] parts = requestLine.Split(' ');
        string path = parts.Length >= 2 ? parts[1] : "/";
        int query = path.IndexOf('?');
        if (query >= 0) path = path.Substring(0, query);

        string status;
        string contentType = "text/plain; charset=utf-8";
        byte[] body;

        if (parts.Length >= 1 && parts[0] == "GET" && (path == "/" || path == "/index.html" || path == "/kumanda"))
        {
            status = "200 OK";
            contentType = "text/html; charset=utf-8";
            body = Encoding.UTF8.GetBytes(ControllerPage.Html);
        }
        else if (path == "/favicon.ico")
        {
            status = "204 No Content";
            body = new byte[0];
        }
        else
        {
            status = "404 Not Found";
            body = Encoding.UTF8.GetBytes("404");
        }

        string head = "HTTP/1.1 " + status + "\r\n" +
                      "Content-Type: " + contentType + "\r\n" +
                      "Content-Length: " + body.Length + "\r\n" +
                      "Cache-Control: no-store\r\n" +
                      "Connection: close\r\n\r\n";
        byte[] headBytes = Encoding.ASCII.GetBytes(head);
        await stream.WriteAsync(headBytes, 0, headBytes.Length);
        if (body.Length > 0) await stream.WriteAsync(body, 0, body.Length);
        await stream.FlushAsync();
    }

    // ------------------------------------------------------------- WebSocket

    private static async Task SendHandshakeAsync(NetworkStream stream, string key)
    {
        string accept;
        using (SHA1 sha1 = SHA1.Create())
        {
            accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
        }

        string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                          "Upgrade: websocket\r\n" +
                          "Connection: Upgrade\r\n" +
                          "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
        byte[] bytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(bytes, 0, bytes.Length);
        await stream.FlushAsync();
    }

    private async Task ReceiveLoopAsync(NetworkStream stream)
    {
        var head = new byte[2];
        var maskKey = new byte[4];
        var message = new MemoryStream();

        while (!_stopping)
        {
            await ReadExactAsync(stream, head, 2);

            bool fin = (head[0] & 0x80) != 0;
            int opcode = head[0] & 0x0F;
            bool masked = (head[1] & 0x80) != 0;
            long length = head[1] & 0x7F;

            if (length == 126)
            {
                var ext = new byte[2];
                await ReadExactAsync(stream, ext, 2);
                length = (ext[0] << 8) | ext[1];
            }
            else if (length == 127)
            {
                var ext = new byte[8];
                await ReadExactAsync(stream, ext, 8);
                length = 0;
                for (int i = 0; i < 8; i++) length = (length << 8) | ext[i];
            }

            // İstemci → sunucu çerçeveleri maskeli olmak zorundadır; aşırı büyük yükleri reddet.
            if (!masked || length < 0 || length > MaxPayloadBytes)
            {
                await SendFrameAsync(stream, 0x8, new byte[0]);
                return;
            }

            await ReadExactAsync(stream, maskKey, 4);
            var payload = new byte[length];
            if (length > 0) await ReadExactAsync(stream, payload, (int)length);
            for (int i = 0; i < payload.Length; i++) payload[i] ^= maskKey[i & 3];

            switch (opcode)
            {
                case 0x8: // close
                    await SendFrameAsync(stream, 0x8, payload.Length <= 125 ? payload : new byte[0]);
                    return;

                case 0x9: // ping
                    await SendFrameAsync(stream, 0xA, payload.Length <= 125 ? payload : new byte[0]);
                    break;

                case 0xA: // pong
                    break;

                case 0x0: // continuation
                case 0x1: // text
                case 0x2: // binary
                    message.Write(payload, 0, payload.Length);
                    if (message.Length > MaxPayloadBytes) return;
                    if (fin)
                    {
                        if (opcode != 0x2 || message.Length > 0)
                            DispatchText(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                        message.SetLength(0);
                    }
                    break;
            }
        }
    }

    private void DispatchText(string text)
    {
        Action<string> handler = CommandReceived;
        if (handler == null) return;

        string[] commands = text.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string raw in commands)
        {
            string command = raw.Trim().ToUpperInvariant();
            if (command.Length == 0) continue;
            try { handler(command); } catch { /* dinleyici hatası sunucuyu durdurmasın */ }
        }
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int n = await stream.ReadAsync(buffer, offset, count - offset);
            if (n <= 0) throw new EndOfStreamException();
            offset += n;
        }
    }

    private static async Task SendFrameAsync(NetworkStream stream, int opcode, byte[] payload)
    {
        // Sunucu → istemci çerçeveleri maskesizdir. Burada yalnızca kısa (<126 bayt) kontrol çerçeveleri gönderilir.
        var frame = new byte[2 + payload.Length];
        frame[0] = (byte)(0x80 | opcode);
        frame[1] = (byte)payload.Length;
        Buffer.BlockCopy(payload, 0, frame, 2, payload.Length);
        await stream.WriteAsync(frame, 0, frame.Length);
        await stream.FlushAsync();
    }
}

/// <summary>Telefon tarayıcısına sunulan, tek dosyalık kumanda sayfası.</summary>
internal static class ControllerPage
{
    public const string Html = @"<!DOCTYPE html>
<html lang='tr'><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no'>
<title>Oyun Kumandasi</title>
<style>
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent;-webkit-user-select:none;user-select:none;-webkit-touch-callout:none}
html,body{margin:0;height:100%;background:#0d0d10;color:#eee;font-family:system-ui,sans-serif;touch-action:none;overflow:hidden}
body{display:flex;flex-direction:column;padding:12px;gap:12px}
#st{text-align:center;font-size:15px;padding:6px;border-radius:8px;background:#3a2a10}
#st.ok{background:#12351f}
button{border:0;border-radius:18px;color:#fff;font-size:26px;font-weight:700;touch-action:none}
#fwd{flex:3;background:#2a6f3a;font-size:40px}
#fwd.on{background:#43b05a}
.row{flex:2;display:flex;gap:12px}
.row button{flex:1}
#fl{background:#8a6a10}
#e{background:#2a4a8a}
button:active{filter:brightness(1.4)}
</style></head><body>
<div id='st'>Baglaniyor...</div>
<button id='fwd'>ILERI</button>
<div class='row'><button id='fl'>FENER</button><button id='e'>E</button></div>
<script>
var ws=null,held=false,st=document.getElementById('st'),fwd=document.getElementById('fwd');
function send(c){if(ws&&ws.readyState===1){ws.send(c);return true}return false}
function buzz(){if(navigator.vibrate)navigator.vibrate(12)}
function connect(){
  ws=new WebSocket('ws://'+location.host+'/ws');
  ws.onopen=function(){st.textContent='Bagli';st.className='ok'};
  ws.onclose=function(){held=false;fwd.className='';st.textContent='Baglanti koptu, yeniden deneniyor...';st.className='';setTimeout(connect,1000)};
  ws.onerror=function(){try{ws.close()}catch(e){}};
}
function press(){if(held)return;held=true;fwd.className='on';send('ILERI_BAS');buzz()}
function release(){if(!held)return;held=false;fwd.className='';send('ILERI_BIRAK')}
fwd.addEventListener('pointerdown',function(e){fwd.setPointerCapture(e.pointerId);press()});
['pointerup','pointercancel','lostpointercapture'].forEach(function(n){fwd.addEventListener(n,release)});
document.getElementById('fl').addEventListener('pointerdown',function(){send('FENER_TETIKLE');buzz()});
document.getElementById('e').addEventListener('pointerdown',function(){send('E_TETIKLE');buzz()});
document.addEventListener('visibilitychange',function(){if(document.hidden)release()});
document.addEventListener('contextmenu',function(e){e.preventDefault()});
setInterval(function(){send('PING')},1000);
connect();
</script></body></html>";
}
