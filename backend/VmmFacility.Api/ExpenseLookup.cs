using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace VmmFacility.Api;

/// <summary>
/// Read-only <c>get_expense_data</c> tool for the chat model. The model only supplies filter values;
/// the query is fixed, runs through EF Core without tracking, and never saves. Totals are summed here
/// as decimals (as in the PDF report) so the model never has to add up amounts itself.
/// </summary>
public static class ExpenseLookup
{
    public const string ToolName = "get_expense_data";
    const int MaxListed = 50;
    const int MaxSearchLength = 100;

    static readonly string[] Parameters = ["dateFrom", "dateTo", "category", "paymentState", "descriptionContains"];

    /// <summary>Tool definition sent to Ollama with every chat request.</summary>
    public static readonly object Definition = new
    {
        type = "function",
        function = new
        {
            name = ToolName,
            description = "Use this tool whenever a question requires facts about saved VMM expenses, such as finding expenses, " +
                          "totals, categories, dates, descriptions, or payment states. Provide date or category filters when the " +
                          "user specifies them. The tool reads saved records from SQLite and returns matching expense details and " +
                          "accurate totals in INR. Never guess expense facts or write to the database.",
            parameters = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["dateFrom"] = new { type = "string", description = "Earliest expense date to include, yyyy-MM-dd (inclusive)." },
                    ["dateTo"] = new { type = "string", description = "Latest expense date to include, yyyy-MM-dd (inclusive)." },
                    ["category"] = new { type = "string", @enum = ExpenseRules.Categories, description = "Only expenses in this category." },
                    ["paymentState"] = new { type = "string", @enum = ExpenseRules.PaymentStates, description = "Only Paid or only Unpaid expenses. Leave it out to get both; every result already includes paid and unpaid totals." },
                    ["descriptionContains"] = new { type = "string", description = "Only expenses whose description contains this text (not case-sensitive)." },
                },
                required = Array.Empty<string>(),
            },
        },
    };

    /// <summary>Validates the model's arguments and returns the tool result, or an error the model can explain.</summary>
    public static async Task<object> Run(JsonElement? arguments, AppDbContext db, CancellationToken ct)
    {
        if (!TryReadFilters(arguments, out var f, out var error))
            return new { error };

        var query = db.Expenses.AsNoTracking();
        if (f.DateFrom is { } from)
            query = query.Where(e => e.Date >= from);
        if (f.DateTo is { } to)
            query = query.Where(e => e.Date <= to);
        if (f.Category is { } category)
            query = query.Where(e => e.Category == category);
        if (f.PaymentState is { } state)
            query = query.Where(e => e.PaymentState == state);
        if (f.DescriptionContains is { } text)
        {
            var pattern = "%" + text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
            query = query.Where(e => EF.Functions.Like(e.Description, pattern, @"\"));
        }

        var matches = await query.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).ToListAsync(ct);

        return new
        {
            filters = new { dateFrom = Format(f.DateFrom), dateTo = Format(f.DateTo), f.Category, f.PaymentState, f.DescriptionContains },
            matchCount = matches.Count,
            totalInr = Format(matches.Sum(e => e.Amount)),
            paidTotalInr = Format(matches.Where(e => e.PaymentState == "Paid").Sum(e => e.Amount)),
            unpaidTotalInr = Format(matches.Where(e => e.PaymentState == "Unpaid").Sum(e => e.Amount)),
            byCategory = matches
                .GroupBy(e => e.Category)
                .OrderBy(g => Array.IndexOf(ExpenseRules.Categories, g.Key))
                .Select(g => new { category = g.Key, count = g.Count(), totalInr = Format(g.Sum(e => e.Amount)) }),
            expenses = matches.Take(MaxListed).Select(e => new
            {
                e.Id,
                date = Format(e.Date),
                e.Category,
                e.Description,
                amountInr = Format(e.Amount),
                e.PaymentState,
            }),
            note = matches.Count == 0
                ? "No saved expenses match these filters."
                : matches.Count > MaxListed
                    ? $"Only the {MaxListed} newest matching expenses are listed; the counts and totals cover all {matches.Count} matches."
                    : null,
        };
    }

    record Filters(DateOnly? DateFrom, DateOnly? DateTo, string? Category, string? PaymentState, string? DescriptionContains);

    static bool TryReadFilters(JsonElement? arguments, out Filters filters, out string? error)
    {
        filters = new Filters(null, null, null, null, null);
        error = null;

        var args = arguments ?? default;
        // Some models send the arguments as a JSON string instead of an object.
        if (args.ValueKind == JsonValueKind.String)
        {
            try { args = JsonDocument.Parse(args.GetString() ?? "{}").RootElement; }
            catch (JsonException) { error = "Tool arguments must be a JSON object."; return false; }
        }
        if (args.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return true;
        if (args.ValueKind != JsonValueKind.Object)
        {
            error = "Tool arguments must be a JSON object.";
            return false;
        }

        var values = new Dictionary<string, string>();
        foreach (var p in args.EnumerateObject())
        {
            if (!Parameters.Contains(p.Name))
            {
                error = $"Unknown filter \"{p.Name}\". Allowed filters: {string.Join(", ", Parameters)}.";
                return false;
            }
            if (p.Value.ValueKind == JsonValueKind.Null)
                continue;
            if (p.Value.ValueKind != JsonValueKind.String)
            {
                error = $"Filter \"{p.Name}\" must be a string.";
                return false;
            }
            var value = p.Value.GetString()!.Trim();
            if (value.Length > 0)
                values[p.Name] = value;
        }

        DateOnly? dateFrom = null, dateTo = null;
        if (values.TryGetValue("dateFrom", out var fromText))
        {
            if (!TryParseDate(fromText, out var d)) { error = "dateFrom must be a date in yyyy-MM-dd format."; return false; }
            dateFrom = d;
        }
        if (values.TryGetValue("dateTo", out var toText))
        {
            if (!TryParseDate(toText, out var d)) { error = "dateTo must be a date in yyyy-MM-dd format."; return false; }
            dateTo = d;
        }
        if (dateFrom > dateTo)
        {
            error = "dateFrom must be on or before dateTo.";
            return false;
        }

        string? category = null;
        if (values.TryGetValue("category", out var categoryText))
        {
            category = ExpenseRules.Categories.FirstOrDefault(c => string.Equals(c, categoryText, StringComparison.OrdinalIgnoreCase));
            if (category is null)
            {
                error = $"category must be one of: {string.Join(", ", ExpenseRules.Categories)}.";
                return false;
            }
        }

        string? paymentState = null;
        if (values.TryGetValue("paymentState", out var stateText))
        {
            paymentState = ExpenseRules.PaymentStates.FirstOrDefault(s => string.Equals(s, stateText, StringComparison.OrdinalIgnoreCase));
            if (paymentState is null)
            {
                error = "paymentState must be Paid or Unpaid.";
                return false;
            }
        }

        values.TryGetValue("descriptionContains", out var search);
        if (search?.Length > MaxSearchLength)
        {
            error = $"descriptionContains can be up to {MaxSearchLength} characters.";
            return false;
        }

        filters = new Filters(dateFrom, dateTo, category, paymentState, search);
        return true;
    }

    static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    static string? Format(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
