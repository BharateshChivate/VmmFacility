using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;

namespace VmmFacility.Api;

public record ChatMessageInput(string? Role, string? Content);

public record ChatInput(List<ChatMessageInput>? Messages);

/// <summary>
/// Local chatbot: forwards the conversation sent by the browser to Ollama and streams the reply back.
/// Chat history is not stored; the browser keeps it in memory and sends it with every question.
/// The model can call the read-only <see cref="ExpenseLookup"/> and <see cref="PolicySearch"/> tools;
/// the backend runs them and sends the results back to Ollama before streaming the final answer.
/// </summary>
/// <remarks>
/// The response is newline-delimited JSON (application/x-ndjson), one event per line:
/// <c>{"type":"delta","text":"…"}</c> for each piece of the reply, then either
/// <c>{"type":"done"}</c> or <c>{"type":"error","message":"…"}</c>.
/// Failures before any text is streamed return a normal problem response instead.
/// </remarks>
public static class Chat
{
    public const string OllamaClient = "ollama";
    public const string OllamaBaseUrl = "http://localhost:11434";
    public const string Model = "qwen3.5:9b";

    const int MaxMessages = 200;
    const int MaxMessageLength = 32_000;
    const int MaxToolRounds = 4;

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void MapChat(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/chat", StreamReply).RequireAuthorization();

    static async Task<IResult> StreamReply(
        ChatInput input, HttpContext ctx, AppDbContext db, PolicySearch policies, IHttpClientFactory clients, ILoggerFactory loggers)
    {
        if (Validate(input) is { } invalid)
            return Results.Problem(invalid, statusCode: StatusCodes.Status400BadRequest);

        var logger = loggers.CreateLogger(typeof(Chat));
        var ct = ctx.RequestAborted;
        var http = clients.CreateClient(OllamaClient);

        var messages = new List<object> { new { role = "system", content = SystemPrompt() } };
        messages.AddRange(input.Messages!.Select(m => new { role = m.Role, content = m.Content }));

        var started = false;
        var separateNextText = false; // text written before a tool call is followed by a paragraph break
        async Task Send(ChatEvent e)
        {
            if (!started)
            {
                ctx.Response.ContentType = "application/x-ndjson; charset=utf-8";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
                await ctx.Response.StartAsync(ct);
                started = true;
            }
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(e, Json) + "\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }

        // Errors before any text has streamed become a normal problem response; after that, an error event.
        async Task<IResult> Fail(string message, int statusCode)
        {
            if (!started)
                return Results.Problem(message, statusCode: statusCode);
            await Send(new ChatEvent("error", Message: message));
            return Results.Empty;
        }

        // Each round streams one model turn. A turn that asks for the tool is followed by another round
        // with the tool result; a turn without tool calls is the final answer.
        for (var round = 0; round < MaxToolRounds; round++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
            {
                Content = JsonContent.Create(new
                {
                    model = Model,
                    messages,
                    tools = new[] { ExpenseLookup.Definition, PolicySearch.Definition },
                    stream = true,
                    think = false, // stream the answer itself, not the model's hidden reasoning
                }),
            };

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Couldn't reach Ollama at {Url}: {Error}", OllamaBaseUrl, ex.Message);
                return await Fail($"Ollama isn't running at {OllamaBaseUrl}. Start Ollama, then try again.",
                    StatusCodes.Status503ServiceUnavailable);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var error = await ReadOllamaError(response, ct);
                    logger.LogWarning("Ollama returned {Status}: {Error}", (int)response.StatusCode, error);
                    return response.StatusCode == HttpStatusCode.NotFound
                        ? await Fail($"The model {Model} isn't installed in Ollama. Run \"ollama pull {Model}\", then try again.",
                            StatusCodes.Status503ServiceUnavailable)
                        : await Fail($"Ollama couldn't answer: {error}", StatusCodes.Status502BadGateway);
                }

                var finished = false;
                var content = new StringBuilder();
                var toolCalls = new List<JsonElement>();
                try
                {
                    await using var body = await response.Content.ReadAsStreamAsync(ct);
                    using var reader = new StreamReader(body);
                    while (!finished && await reader.ReadLineAsync(ct) is { } line)
                    {
                        if (line.Length == 0)
                            continue;
                        var chunk = JsonSerializer.Deserialize<OllamaChunk>(line, Json);
                        if (chunk?.Error is { } error)
                        {
                            logger.LogWarning("Ollama stopped with an error: {Error}", error);
                            return await Fail($"Ollama stopped with an error: {error}", StatusCodes.Status502BadGateway);
                        }
                        if (chunk?.Message?.Content is { Length: > 0 } text)
                        {
                            content.Append(text);
                            await Send(new ChatEvent("delta", Text: separateNextText ? "\n\n" + text : text));
                            separateNextText = false;
                        }
                        if (chunk?.Message?.ToolCalls is { ValueKind: JsonValueKind.Array } calls)
                            toolCalls.AddRange(calls.EnumerateArray().Select(c => c.Clone()));
                        finished = chunk?.Done == true;
                    }
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException && !ct.IsCancellationRequested)
                {
                    logger.LogWarning("Lost the Ollama reply stream: {Error}", ex.Message);
                }

                if (!finished)
                    return await Fail("Ollama stopped before the reply finished. Try again.", StatusCodes.Status502BadGateway);

                if (toolCalls.Count == 0)
                {
                    await Send(new ChatEvent("done"));
                    return Results.Empty;
                }

                separateNextText = content.Length > 0;
                messages.Add(new { role = "assistant", content = content.ToString(), tool_calls = toolCalls });
                foreach (var call in toolCalls)
                {
                    var name = call.TryGetProperty("function", out var fn) && fn.TryGetProperty("name", out var n) ? n.GetString() : null;
                    JsonElement? arguments = fn.ValueKind == JsonValueKind.Object && fn.TryGetProperty("arguments", out var a) ? a : null;
                    logger.LogInformation("Chat tool call {Tool} with {Arguments}", name, arguments?.GetRawText());
                    object result;
                    try
                    {
                        result = name switch
                        {
                            ExpenseLookup.ToolName => await ExpenseLookup.Run(arguments, db, ct),
                            PolicySearch.ToolName => await policies.Run(arguments, ct),
                            _ => new { error = $"Unknown tool \"{name}\". Use {ExpenseLookup.ToolName} or {PolicySearch.ToolName}." },
                        };
                    }
                    catch (ChatToolException ex)
                    {
                        return await Fail(ex.Message, ex.StatusCode);
                    }
                    messages.Add(new { role = "tool", tool_name = name, content = JsonSerializer.Serialize(result, Json) });
                }
            }
        }

        logger.LogWarning("Chat stopped after {Rounds} tool rounds without a final answer", MaxToolRounds);
        return await Fail("The assistant couldn't finish looking up expenses. Try rephrasing the question.",
            StatusCodes.Status502BadGateway);
    }

    static string SystemPrompt() =>
        $"""
        You are the assistant in VMM Facility Management, a local app where the Facility Manager records expenses.
        Today's date is {DateTime.Now.ToString("yyyy-MM-dd (dddd)", CultureInfo.InvariantCulture)}. All amounts are in INR.
        Expense categories: {string.Join(", ", ExpenseRules.Categories)}. Payment states: Paid, Unpaid.
        For any question about saved expenses, call the {ExpenseLookup.ToolName} tool and answer only from its results.
        This includes follow-up questions: earlier replies in this chat don't include the underlying records, so call the tool again
        (reusing the filters from the conversation) before naming any expense, category, date, or amount. Never reuse or invent figures.
        Quote counts and totals exactly as the tool returns them; never add up or estimate amounts yourself.
        Only state facts about expenses that the tool returned. Results include paid and unpaid totals, so leave out paymentState unless the user asks about only one of them.
        If no expenses match, say so. If the question is unclear (for example, the period is ambiguous), ask the user to clarify instead of guessing.
        For questions about policies, procedures, approvals, or contracts (such as housekeeping rules or the lift AMC), call the {PolicySearch.ToolName} tool
        and answer only from the sections it returns, citing the document and section number (for example "Lift AMC, section 5.2").
        If the tool finds nothing relevant, say the policy documents don't cover it; don't answer policy or contract questions from general knowledge.
        Search one topic per call: for a question with several parts (for example who pays for a repair and what staff should do),
        call {PolicySearch.ToolName} once for each part. Some questions need both tools, for example what a contract covers and what has been spent.
        Quarters and "this year" follow the Indian financial year (April to March): Q1 is April–June, Q2 July–September,
        Q3 October–December, Q4 January–March. For "so far" or "to date", search from the start of the financial year (or contract) to today.
        Call tools before writing anything; don't describe what you are about to look up.
        You can only read expenses. You can't add or edit them; tell the user to use the Expenses page for that. The app doesn't support deleting expenses.
        For other questions, answer helpfully and briefly.
        """;

    static string? Validate(ChatInput input)
    {
        var messages = input.Messages;
        if (messages is null || messages.Count == 0)
            return "Type a question to send.";
        if (messages.Count > MaxMessages)
            return "This chat is too long. Start a new chat to continue.";
        foreach (var m in messages)
        {
            if (m.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(m.Content))
                return "The chat history is invalid. Start a new chat.";
            if (m.Content.Length > MaxMessageLength)
                return $"Each message can be up to {MaxMessageLength:N0} characters.";
        }
        if (messages[^1].Role != "user")
            return "Type a question to send.";
        return null;
    }

    static async Task<string> ReadOllamaError(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<OllamaChunk>(body, Json)?.Error ?? $"HTTP {(int)response.StatusCode}";
        }
        catch (JsonException)
        {
            return $"HTTP {(int)response.StatusCode}";
        }
    }

    record ChatEvent(string Type, string? Text = null, string? Message = null);

    record OllamaMessage(string? Content, [property: JsonPropertyName("tool_calls")] JsonElement? ToolCalls);

    record OllamaChunk(OllamaMessage? Message, bool Done, string? Error);
}
