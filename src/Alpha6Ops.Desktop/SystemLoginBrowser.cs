using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityModel.OidcClient.Browser;

namespace Alpha6Ops.Desktop;

// Reserve the registered loopback port for the full login, without a release/rebind race.
internal sealed class SystemLoginBrowser : IBrowser, IDisposable
{
    private readonly TcpListener listener;
    private readonly Action<string> openBrowser;
    private readonly TimeSpan loginTimeout;
    private readonly TimeSpan requestTimeLimit;
    internal string RedirectUri { get; }
    internal BrowserResultType? LastResultType { get; private set; }
    internal SystemLoginBrowser(int port, Action<string>? browserLauncher = null, TimeSpan? timeout = null, TimeSpan? requestTimeout = null)
    {
        openBrowser = browserLauncher ?? (url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        loginTimeout = timeout ?? TimeSpan.FromMinutes(3);
        requestTimeLimit = requestTimeout ?? TimeSpan.FromSeconds(3);
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Server.ExclusiveAddressUse = true;
        listener.Start();
        RedirectUri = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/callback/";
    }

    public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(loginTimeout);
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            openBrowser(options.StartUrl);
            while (true)
            {
                using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
                try
                {
                    using var stream = connection.GetStream();
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    requestTimeout.CancelAfter(requestTimeLimit);
                    var bytes = new byte[16384]; var count = 0;
                    while (count < bytes.Length)
                    {
                        var read = await stream.ReadAsync(bytes.AsMemory(count, 1), requestTimeout.Token);
                        if (read == 0) break;
                        count += read;
                        if (count >= 4 && bytes[count - 4] == 13 && bytes[count - 3] == 10 && bytes[count - 2] == 13 && bytes[count - 1] == 10) break;
                    }
                    var headers = Encoding.ASCII.GetString(bytes, 0, count);
                    var line = headers.Split("\r\n")[0].Split(' ');
                    var host = new Uri(RedirectUri).Authority;
                    if (!headers.EndsWith("\r\n\r\n", StringComparison.Ordinal) || line.Length != 3 || line[0] != "GET" || !line[1].StartsWith("/callback/?", StringComparison.Ordinal)
                        || !headers.Contains("\r\nHost: " + host + "\r\n", StringComparison.OrdinalIgnoreCase))
                    {
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token);
                        continue;
                    }
                    var failed = line[1].Contains("error=", StringComparison.Ordinal);
                    var body = Encoding.UTF8.GetBytes(CallbackPage(failed));
                    var reply = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), timeout.Token);
                    await stream.WriteAsync(body, timeout.Token);
                    LastResultType = BrowserResultType.Success;
                    return new BrowserResult { ResultType = BrowserResultType.Success, Response = RedirectUri.TrimEnd('/') + "/" + line[1]["/callback/".Length..] };
                }
                // A browser probe or abandoned connection must not cancel the real sign-in.
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { }
                catch (IOException) when (!timeout.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException)
        {
            LastResultType = cancellationToken.IsCancellationRequested ? BrowserResultType.UserCancel : BrowserResultType.Timeout;
            return new BrowserResult { ResultType = LastResultType.Value };
        }
        finally { listener.Stop(); }
    }
    public void Dispose() => listener.Stop();

    // The page the browser lands on after the provider redirects back. Self-contained: no scripts, no
    // external requests, nothing that could carry the authorization code anywhere else.
    internal static string CallbackPage(bool failed)
    {
        // The app still exchanges the code and loads the account after this page appears, so the copy promises
        // only what has happened: the browser part is done.
        var title = failed ? "Sign-in didn’t complete" : "Almost there";
        var lead = failed ? "Alpha 6 OPS didn’t receive a valid sign-in. Return to the app and try again."
            : "The browser part is done. Alpha 6 OPS is finishing your sign-in now — switch back to the app. If it shows a message instead of your workspaces, follow it there. You can close this tab.";
        var mark = failed ? "<span class=\"dot warn\"></span>SIGN-IN INTERRUPTED" : "<span class=\"dot\"></span>SIGN-IN RECEIVED";
        return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>" + title + " · Alpha 6 OPS</title><style>"
            + "html{background:#070c12}body{margin:0;min-height:100vh;display:grid;place-items:center;font-family:'Segoe UI',system-ui,sans-serif;color:#e8edf2;background:#070c12}"
            + ".card{width:min(520px,calc(100% - 32px));border:1px solid #24323f;border-radius:10px;padding:36px 40px;background:linear-gradient(180deg,#101a24,#0b1219);box-shadow:0 0 60px #0006}"
            + ".brand{display:flex;align-items:center;gap:14px;margin-bottom:28px}.mark{display:grid;place-items:center;width:40px;height:40px;border:1px solid #a68d3b;border-radius:6px;background:#121b25;color:#e5c44a;font-weight:700;letter-spacing:-1px}"
            + ".brand span{font-size:18px;font-weight:600;letter-spacing:.5px}.status{display:flex;align-items:center;gap:10px;font:11px Consolas,monospace;letter-spacing:1.2px;color:#9eafbd;margin-bottom:14px}"
            + ".dot{width:8px;height:8px;border-radius:50%;background:#5fae6e;box-shadow:0 0 8px #5fae6e99}.dot.warn{background:#e5c44a;box-shadow:0 0 8px #e5c44a99}"
            + "h1{font-size:30px;letter-spacing:-1px;margin:0 0 10px}p{color:#9eafbd;line-height:1.6;margin:0 0 22px}.line{width:26px;height:2px;background:#e5c44a;margin:0 0 18px}"
            + "footer{font:10px Consolas,monospace;letter-spacing:1px;color:#6e828f;text-transform:uppercase}</style></head><body><div class=\"card\">"
            + "<div class=\"brand\"><div class=\"mark\">A6</div><span>ALPHA 6 OPS</span></div><div class=\"status\">" + mark + "</div><div class=\"line\"></div>"
            + "<h1>" + title + "</h1><p>" + lead + "</p><footer>Same sky. A brighter tomorrow.</footer></div></body></html>";
    }
}
