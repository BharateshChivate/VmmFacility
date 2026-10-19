using System.Globalization;
using System.Text.RegularExpressions;

namespace VmmFacility.Api;

public record ExpenseInput(string? Amount, string? Date, string? Category, string? Description, string? PaymentState);

public record ExpenseDto(
    int Id, string Amount, string Date, string Category, string Description, string PaymentState,
    DateTime CreatedAt, DateTime UpdatedAt)
{
    public static ExpenseDto From(Expense e) => new(
        e.Id,
        e.Amount.ToString("0.00", CultureInfo.InvariantCulture),
        e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        e.Category, e.Description, e.PaymentState,
        DateTime.SpecifyKind(e.CreatedAtUtc, DateTimeKind.Utc),
        DateTime.SpecifyKind(e.UpdatedAtUtc, DateTimeKind.Utc));
}

public record ValidExpense(decimal Amount, DateOnly Date, string Category, string Description, string PaymentState);

public static partial class ExpenseRules
{
    public const int DescriptionMaxLength = 500;
    public const decimal MaxAmount = 999_999_999.99m;

    public static readonly string[] Categories =
        ["Utilities", "Maintenance & Repairs", "Housekeeping", "Security", "Supplies", "Other"];

    public static readonly string[] PaymentStates = ["Paid", "Unpaid"];

    [GeneratedRegex(@"^\d+(\.\d{1,2})?$")]
    private static partial Regex AmountPattern();

    public static Dictionary<string, string[]> Validate(ExpenseInput input, out ValidExpense? valid)
    {
        var errors = new Dictionary<string, string[]>();
        valid = null;

        decimal amount = 0;
        var amountText = input.Amount?.Trim() ?? "";
        if (amountText.Length == 0)
            errors["amount"] = ["Enter the amount."];
        else if (!AmountPattern().IsMatch(amountText)
                 || !decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
            errors["amount"] = ["Enter a number in INR with up to 2 decimal places, for example 1250 or 1250.50."];
        else if (amount <= 0)
            errors["amount"] = ["Amount must be greater than 0."];
        else if (amount > MaxAmount)
            errors["amount"] = ["Amount must be 999,999,999.99 or less."];

        DateOnly date = default;
        var dateText = input.Date?.Trim() ?? "";
        if (dateText.Length == 0)
            errors["date"] = ["Enter the expense date."];
        else if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            errors["date"] = ["Enter a valid date."];
        else if (date > DateOnly.FromDateTime(DateTime.Now))
            errors["date"] = ["Date can't be in the future."];

        var category = input.Category?.Trim() ?? "";
        if (category.Length == 0)
            errors["category"] = ["Choose a category."];
        else if (!Categories.Contains(category))
            errors["category"] = ["Choose a category from the list."];

        var description = input.Description?.Trim() ?? "";
        if (description.Length == 0)
            errors["description"] = ["Enter a description."];
        else if (description.Length > DescriptionMaxLength)
            errors["description"] = [$"Description must be {DescriptionMaxLength} characters or fewer."];

        var paymentState = input.PaymentState?.Trim() ?? "";
        if (paymentState.Length == 0)
            errors["paymentState"] = ["Choose Paid or Unpaid."];
        else if (!PaymentStates.Contains(paymentState))
            errors["paymentState"] = ["Payment state must be Paid or Unpaid."];

        if (errors.Count == 0)
            valid = new ValidExpense(amount, date, category, description, paymentState);
        return errors;
    }
}
