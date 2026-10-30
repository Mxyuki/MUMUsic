using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace MUMUsic.Desktop.Windowing;

/// <summary>
/// The window controls the page calls through WebView2 messages (web/app.js, "window" section):
/// request {id, method, params} -> response {id, result} or {id, error}; host -> page events {event, payload}.
/// Everything else goes through the local server's /api/.
/// </summary>
internal sealed class HostBridge(CoreWebView2 webView, string origin, Func<string, JsonElement[], object?> call)
{
    public void Attach() => webView.WebMessageReceived += OnMessage;

    /// <summary>Sends an event to the page. Call on the UI thread.</summary>
    public void Emit(string name, object? payload)
    {
        try
        {
            webView.PostWebMessageAsJson(JsonSerializer.Serialize(new { @event = name, payload }, Json.Options));
        }
        catch (InvalidOperationException)
        {
            // The WebView is gone (the app is quitting).
        }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!args.Source.StartsWith(origin, StringComparison.OrdinalIgnoreCase)) return;
        Request? request;
        try
        {
            request = JsonSerializer.Deserialize<Request>(args.WebMessageAsJson, Json.Options);
        }
        catch (JsonException)
        {
            return;
        }
        if (request is null) return;
        object response;
        try
        {
            response = new { id = request.Id, result = call(request.Method, request.Params ?? []) };
        }
        catch (Exception error)
        {
            Log.Error($"bridge {request.Method}", error);
            response = new { id = request.Id, error = error.Message };
        }
        webView.PostWebMessageAsJson(JsonSerializer.Serialize(response, Json.Options));
    }

    private sealed record Request(int Id, string Method, JsonElement[]? Params);
}
