using System.Net.Sockets;
using System.Text;
using Alpha6Ops.Desktop;
using Duende.IdentityModel.OidcClient.Browser;

internal static class BrowserChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        var launches = 0;
        using (var browser = new SystemLoginBrowser(0, _ => launches++, requestTimeout: TimeSpan.FromMilliseconds(150)))
        {
            var callback = new Uri(browser.RedirectUri);
            var result = browser.InvokeAsync(new BrowserOptions("https://identity.example.invalid/authorize", browser.RedirectUri), token);
            async Task<string> SendAsync(string request)
            {
                using var client = new TcpClient();
                await client.ConnectAsync(callback.Host, callback.Port, token);
                using var stream = client.GetStream();
                await stream.WriteAsync(Encoding.ASCII.GetBytes(request), token);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(token);
            }
            var badHost = await SendAsync("GET /callback/?code=wrong HTTP/1.1\r\nHost: invalid.example\r\n\r\n");
            check(badHost.Contains("400") && !result.IsCompleted, "callback rejects wrong Host and continues waiting");
            var probe = await SendAsync($"GET /favicon.ico HTTP/1.1\r\nHost: {callback.Authority}\r\n\r\n");
            check(probe.Contains("400") && !result.IsCompleted, "unrelated browser requests cannot finish login");
            using (var stalled = new TcpClient())
            {
                await stalled.ConnectAsync(callback.Host, callback.Port, token);
                using var stream = stalled.GetStream();
                await stream.WriteAsync(Encoding.ASCII.GetBytes("GET /callback/"), token);
                var read = await stream.ReadAsync(new byte[1], token);
                check(read == 0 && !result.IsCompleted, "slow incomplete callback connection is closed without canceling login");
            }
            var response = await SendAsync($"GET /callback/?code=test-code&state=test-state HTTP/1.1\r\nHost: {callback.Authority}\r\n\r\n");
            var complete = await result.WaitAsync(token);
            check(launches == 1 && complete.ResultType == BrowserResultType.Success
                && complete.Response == browser.RedirectUri + "?code=test-code&state=test-state",
                "loopback callback preserves code and state for OIDC validation");
            check(response.Contains("Cache-Control: no-store") && !response.Contains("test-code"),
                "browser response does not disclose or cache authorization code");
            using var rebound = new SystemLoginBrowser(callback.Port, _ => { });
            check(true, "completed login releases the callback port");
        }
        using (var browser = new SystemLoginBrowser(0, _ => { }, timeout: TimeSpan.FromMilliseconds(100)))
        {
            var result = await browser.InvokeAsync(new BrowserOptions("https://identity.example.invalid/authorize", browser.RedirectUri), token);
            check(result.ResultType == BrowserResultType.Timeout, "browser timeout is distinct from user cancellation");
        }
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
        using (var browser = new SystemLoginBrowser(0, _ => { }))
        {
            var task = browser.InvokeAsync(new BrowserOptions("https://identity.example.invalid/authorize", browser.RedirectUri), cancellation.Token);
            cancellation.Cancel();
            check((await task.WaitAsync(token)).ResultType == BrowserResultType.UserCancel, "cancel stops the pending callback immediately");
            using var rebound = new SystemLoginBrowser(new Uri(browser.RedirectUri).Port, _ => { });
            check(true, "canceled sign-in releases the callback port for retry");
        }
    }
}
