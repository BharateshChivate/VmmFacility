using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VmmFacility.Api;

/// <summary>Failure the chat should show to the user as-is, rather than pass to the model.</summary>
public class ChatToolException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Read-only <c>search_policy_documents</c> tool: semantic search over the Markdown policy documents.
/// Each heading section becomes one chunk, embedded with a local Ollama embedding model and kept in
/// memory. The index is built on first use and rebuilt when the files change; it can always be
/// recreated from the files, so nothing is stored.
/// </summary>
public class PolicySearch(IHttpClientFactory clients, ILogger<PolicySearch> logger, string folder)
{
    public const string ToolName = "search_policy_documents";
    public const string EmbeddingModel = "qwen3-embedding:0.6b";

    const int TopResults = 4;
    const float MinScore = 0.45f;
    const int MaxChunkLength = 1500;
    const int MaxQueryLength = 500;

    // Qwen3-embedding retrieves better when queries (not documents) carry a task instruction.
    const string QueryInstruction =
        "Instruct: Given a question about facility policies or contracts, retrieve the policy sections that answer it\nQuery: ";

    public static readonly object Definition = new
    {
        type = "function",
        function = new
        {
            name = ToolName,
            description = "Search the facility's policy documents and contracts (such as the housekeeping working policy and the " +
                          "lift AMC agreement) for the sections relevant to a question. Use this for questions about rules, " +
                          "procedures, responsibilities, approval limits, schedules, what a contract covers or excludes, response " +
                          "times, penalties, or contract terms. Returns the most relevant sections with their document and section " +
                          "names. Answer only from the returned text and cite the document and section.",
            parameters = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["query"] = new { type = "string", description = "What to look for, phrased as a question or short description." },
                },
                required = new[] { "query" },
            },
        },
    };

    public record Chunk(string Document, string DocumentId, string Section, string Text);

    record IndexedChunk(Chunk Chunk, float[] Vector);

    readonly SemaphoreSlim buildLock = new(1, 1);
    List<IndexedChunk> index = [];
    string fingerprint = "";

    /// <summary>Validates the model's arguments and returns matching sections, or an error the model can explain.</summary>
    public async Task<object> Run(JsonElement? arguments, CancellationToken ct)
    {
        var args = arguments ?? default;
        if (args.ValueKind == JsonValueKind.String)
        {
            try { args = JsonDocument.Parse(args.GetString() ?? "{}").RootElement; }
            catch (JsonException) { return new { error = "Tool arguments must be a JSON object." }; }
        }
        if (args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty("query", out var q) || q.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(q.GetString()))
            return new { error = "Provide a \"query\" string describing what to look for." };
        var query = q.GetString()!.Trim();
        if (query.Length > MaxQueryLength)
            return new { error = $"query can be up to {MaxQueryLength} characters." };

        var chunks = await GetIndex(ct);
        if (chunks.Count == 0)
            return new { error = $"No policy documents are loaded. Add Markdown files to {folder}." };

        var queryVector = (await Embed([QueryInstruction + query], ct))[0];
        var ranked = chunks
            .Select(c => (c.Chunk, Score: Dot(queryVector, c.Vector)))
            .OrderByDescending(r => r.Score)
            .Take(TopResults)
            .ToList();
        logger.LogInformation("Policy search scores: {Scores}",
            string.Join(", ", ranked.Select(r => $"{r.Score:0.000} {r.Chunk.DocumentId} {r.Chunk.Section}")));

        var relevant = ranked.Where(r => r.Score >= MinScore).ToList();
        return new
        {
            query,
            results = relevant.Select(r => new
            {
                document = r.Chunk.Document,
                documentId = r.Chunk.DocumentId,
                section = r.Chunk.Section,
                score = Math.Round(r.Score, 3),
                text = r.Chunk.Text,
            }),
            note = relevant.Count == 0
                ? "No policy section is relevant enough to this question. Say the documents don't cover it; don't guess."
                : null,
        };
    }

    async Task<List<IndexedChunk>> GetIndex(CancellationToken ct)
    {
        var files = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.md").Order(StringComparer.Ordinal).ToArray()
            : [];
        var contents = files.Select(f => (Path: f, Text: File.ReadAllText(f))).ToList();
        var current = Fingerprint(contents);
        if (current == fingerprint)
            return index;

        await buildLock.WaitAsync(ct);
        try
        {
            if (current == fingerprint)
                return index;
            var chunks = contents.SelectMany(c => Split(Path.GetFileNameWithoutExtension(c.Path), c.Text)).ToList();
            var vectors = chunks.Count == 0 ? [] : await Embed(chunks.Select(Header).ToList(), ct);
            index = chunks.Zip(vectors, (c, v) => new IndexedChunk(c, v)).ToList();
            fingerprint = current;
            logger.LogInformation("Indexed {Chunks} sections from {Files} policy documents in {Folder}", index.Count, files.Length, folder);
            return index;
        }
        finally
        {
            buildLock.Release();
        }
    }

    /// <summary>The text that gets embedded: the section's document and heading path, then its body.</summary>
    static string Header(Chunk c) => $"{c.Document} ({c.DocumentId}) › {c.Section}\n\n{c.Text}";

    /// <summary>Splits a Markdown document into one chunk per ## / ### section; the preamble becomes "Overview".</summary>
    public static IEnumerable<Chunk> Split(string fileName, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var title = lines.FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim() ?? fileName;
        var idLine = lines.FirstOrDefault(l => l.StartsWith("**Document ID:**"));
        var documentId = idLine is null ? fileName : idLine["**Document ID:**".Length..].Trim();

        var chunks = new List<Chunk>();
        string? h2 = null, h3 = null;
        var body = new StringBuilder();

        void Flush()
        {
            var text = body.ToString().Trim();
            body.Clear();
            if (text.Length == 0)
                return;
            var section = h2 is null ? "Overview" : h3 is null ? h2 : $"{h2} › {h3}";
            foreach (var part in SplitLong(text))
                chunks.Add(new Chunk(title, documentId, section, part));
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("# "))
                continue;
            if (line.StartsWith("## "))
            {
                Flush();
                h2 = line[3..].Trim();
                h3 = null;
            }
            else if (line.StartsWith("### "))
            {
                Flush();
                h3 = line[4..].Trim();
            }
            else
            {
                body.AppendLine(line);
            }
        }
        Flush();
        return chunks;
    }

    /// <summary>Splits an over-long section at blank lines, keeping tables and paragraphs whole.</summary>
    static IEnumerable<string> SplitLong(string text)
    {
        if (text.Length <= MaxChunkLength)
        {
            yield return text;
            yield break;
        }
        var part = new StringBuilder();
        foreach (var block in text.Split("\n\n"))
        {
            if (part.Length > 0 && part.Length + block.Length > MaxChunkLength)
            {
                yield return part.ToString().Trim();
                part.Clear();
            }
            part.Append(block).Append("\n\n");
        }
        if (part.Length > 0)
            yield return part.ToString().Trim();
    }

    /// <summary>Embeds texts with the local embedding model; vectors are normalised so a dot product is the cosine.</summary>
    async Task<List<float[]>> Embed(List<string> texts, CancellationToken ct)
    {
        var http = clients.CreateClient(Chat.OllamaClient);
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("/api/embed", new { model = EmbeddingModel, input = texts }, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ChatToolException($"Ollama isn't running at {Chat.OllamaBaseUrl}. Start Ollama, then try again.",
                StatusCodes.Status503ServiceUnavailable);
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new ChatToolException(
                    $"The embedding model {EmbeddingModel} isn't installed in Ollama. Run \"ollama pull {EmbeddingModel}\", then try again.",
                    StatusCodes.Status503ServiceUnavailable);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Ollama embed returned {Status}: {Error}", (int)response.StatusCode, detail);
                throw new ChatToolException("Ollama couldn't search the policy documents. Try again.", StatusCodes.Status502BadGateway);
            }

            var result = await response.Content.ReadFromJsonAsync<EmbedResponse>(ct);
            if (result?.Embeddings is not { } embeddings || embeddings.Count != texts.Count)
                throw new ChatToolException("Ollama returned an unexpected embedding response.", StatusCodes.Status502BadGateway);
            return embeddings.Select(Normalise).ToList();
        }
    }

    static float[] Normalise(float[] v)
    {
        var length = MathF.Sqrt(v.Sum(x => x * x));
        return length == 0 ? v : v.Select(x => x / length).ToArray();
    }

    static float Dot(float[] a, float[] b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }

    static string Fingerprint(IEnumerable<(string Path, string Text)> files)
    {
        var all = string.Join("\0", files.Select(f => Path.GetFileName(f.Path) + "\0" + f.Text));
        return EmbeddingModel + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(all)));
    }

    record EmbedResponse(List<float[]>? Embeddings);
}
