using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EventTracker.Models;

namespace EventTracker.Services;

/// <summary>
/// Handles all OpenAI API calls: timeline explanation, auto-classification, and chat.
/// </summary>
public class AIService
{
    private readonly HttpClient _http;
    private string _apiKey = "";
    private const string Model = "gpt-4o-mini";
    private const string BaseUrl = "https://api.openai.com/v1/chat/completions";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public AIService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public void SetApiKey(string key)
    {
        _apiKey = key.Trim();
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Explain a time segment in human language
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<string> ExplainEventsAsync(
        IEnumerable<SystemEvent> events,
        string? timeLabel = null,
        CancellationToken ct = default)
    {
        var evList = events.ToList();
        if (evList.Count == 0) return "No events to analyse.";

        var sb = new StringBuilder();
        sb.AppendLine("You are a system activity analyst. Explain in friendly, plain Russian what happened on the PC based on these events. Be concise (2–5 sentences). Highlight key actions: installations, updates, deletions, suspicious activity. Merge related events into one statement.");
        sb.AppendLine();
        if (timeLabel != null) sb.AppendLine($"Time period: {timeLabel}");
        sb.AppendLine($"Total events: {evList.Count}");
        sb.AppendLine();
        sb.AppendLine("Events (timestamp | category | action | process | path):");

        foreach (var ev in evList.Take(80))
        {
            sb.AppendLine($"{ev.TimeDisplay} | {ev.Category} | {ev.Action} | {ev.ProcessName} | {ev.Path}");
        }
        if (evList.Count > 80)
            sb.AppendLine($"... and {evList.Count - 80} more events.");

        return await CallAsync(sb.ToString(), ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Auto-classify a batch of events → returns JSON tags
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<Dictionary<long, string>> ClassifyEventsAsync(
        IEnumerable<SystemEvent> events,
        CancellationToken ct = default)
    {
        var evList = events.ToList();
        if (evList.Count == 0) return new();

        var sb = new StringBuilder();
        sb.AppendLine("Classify each system event into ONE tag from this list:");
        sb.AppendLine("installation | update | game-session | browser | deletion | suspicious | system | backup | development | media | office | other");
        sb.AppendLine();
        sb.AppendLine("Return ONLY a JSON object like: {\"123\": \"installation\", \"124\": \"game-session\"}");
        sb.AppendLine("Events (id | process | action | path):");

        foreach (var ev in evList.Take(50))
        {
            sb.AppendLine($"{ev.Id} | {ev.ProcessName} | {ev.Action} | {ev.Path}");
        }

        var raw = await CallAsync(sb.ToString(), ct);

        try
        {
            // Strip markdown fences if present
            var json = raw.Trim().TrimStart('`');
            if (json.StartsWith("json")) json = json[4..];
            json = json.TrimEnd('`').Trim();

            var doc = JsonDocument.Parse(json);
            var result = new Dictionary<long, string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (long.TryParse(prop.Name, out var id))
                    result[id] = prop.Value.GetString() ?? "other";
            return result;
        }
        catch
        {
            return new();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Chat with PC history — free-form Q&A
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<string> ChatWithHistoryAsync(
        string userQuestion,
        IEnumerable<SystemEvent> relevantEvents,
        List<ChatMessage> history,
        CancellationToken ct = default)
    {
        var evList = relevantEvents.ToList();

        var sysPrompt = new StringBuilder();
        sysPrompt.AppendLine("You are a PC activity assistant. Answer the user's questions about their computer history in friendly, plain Russian. Base answers strictly on provided events. If events are empty, say so.");
        sysPrompt.AppendLine();
        sysPrompt.AppendLine($"Relevant events ({evList.Count} total, newest first):");
        foreach (var ev in evList.Take(100))
            sysPrompt.AppendLine($"{ev.Timestamp:yyyy-MM-dd HH:mm:ss} | {ev.Category} | {ev.Action} | {ev.ProcessName} | {ev.Path}");
        if (evList.Count > 100)
            sysPrompt.AppendLine($"... +{evList.Count - 100} more");

        var messages = new List<object>
        {
            new { role = "system", content = sysPrompt.ToString() }
        };

        // Add conversation history (last 10 turns)
        foreach (var msg in history.TakeLast(10))
            messages.Add(new { role = msg.Role, content = msg.Content });

        messages.Add(new { role = "user", content = userQuestion });

        return await CallWithMessagesAsync(messages, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<string> CallAsync(string prompt, CancellationToken ct)
    {
        var messages = new List<object>
        {
            new { role = "user", content = prompt }
        };
        return await CallWithMessagesAsync(messages, ct);
    }

    private async Task<string> CallWithMessagesAsync(List<object> messages, CancellationToken ct)
    {
        if (!IsConfigured)
            return "⚠️ OpenAI API key not set. Go to Settings → AI Settings.";

        var body = JsonSerializer.Serialize(new
        {
            model = Model,
            messages,
            max_tokens = 1024,
            temperature = 0.4
        });

        try
        {
            var response = await _http.PostAsync(
                BaseUrl,
                new StringContent(body, Encoding.UTF8, "application/json"),
                ct);

            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // Try to parse error message from OpenAI
                try
                {
                    var err = JsonDocument.Parse(json);
                    var msg = err.RootElement
                        .GetProperty("error")
                        .GetProperty("message")
                        .GetString();
                    return $"❌ OpenAI error: {msg}";
                }
                catch
                {
                    return $"❌ HTTP {(int)response.StatusCode}: {json}";
                }
            }

            var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "(empty response)";
        }
        catch (OperationCanceledException)
        {
            return "⏹ Request cancelled.";
        }
        catch (Exception ex)
        {
            return $"❌ Connection error: {ex.Message}";
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Chat conversation turn
// ─────────────────────────────────────────────────────────────────────────────
public class ChatMessage
{
    public string Role { get; set; } = "user"; // "user" or "assistant"
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
