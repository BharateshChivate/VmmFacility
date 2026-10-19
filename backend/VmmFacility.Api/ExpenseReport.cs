using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VmmFacility.Api;

public static class ExpenseReport
{
    private const string TextColor = "#202923";
    private const string MutedColor = "#66716A";
    private const string BorderColor = "#DDE3DE";

    private static readonly CultureInfo Inr = CreateInrCulture();

    private static CultureInfo CreateInrCulture()
    {
        // Indian digit grouping (1,23,45,678.00) regardless of the machine's locale data.
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberGroupSizes = [3, 2];
        return culture;
    }

    public static string FormatInr(decimal amount) => amount.ToString("N2", Inr);

    // Expenses must already be sorted newest date first.
    public static byte[] Generate(IReadOnlyList<Expense> expenses, DateTime generatedAtLocal)
    {
        var total = expenses.Sum(e => e.Amount);
        var paid = expenses.Where(e => e.PaymentState == "Paid").Sum(e => e.Amount);
        var unpaid = expenses.Where(e => e.PaymentState == "Unpaid").Sum(e => e.Amount);

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(TextColor));

            page.Header().PaddingBottom(12).Column(col =>
            {
                col.Item().Text("VMM Facility Management").FontSize(10).FontColor(MutedColor);
                col.Item().Text("Expense Report").FontSize(18).SemiBold();
                col.Item().Text($"Generated on {generatedAtLocal.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}")
                    .FontColor(MutedColor);
            });

            page.Content().Column(col =>
            {
                if (expenses.Count == 0)
                {
                    col.Item().PaddingVertical(8).Text("No expenses have been recorded.");
                }
                else
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(72);   // Date
                            c.RelativeColumn();     // Description
                            c.ConstantColumn(105);  // Category
                            c.ConstantColumn(85);   // Amount
                            c.ConstantColumn(80);   // Payment state
                        });

                        // Header repeats automatically on every page.
                        table.Header(h =>
                        {
                            h.Cell().Element(HeaderCell).Text("Date");
                            h.Cell().Element(HeaderCell).Text("Description");
                            h.Cell().Element(HeaderCell).Text("Category");
                            h.Cell().Element(HeaderCell).AlignRight().Text("Amount (INR)");
                            h.Cell().Element(HeaderCell).Text("Payment state");
                        });

                        foreach (var e in expenses)
                        {
                            table.Cell().Element(BodyCell).Text(e.Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
                            table.Cell().Element(BodyCell).Text(e.Description);
                            table.Cell().Element(BodyCell).Text(e.Category);
                            table.Cell().Element(BodyCell).AlignRight().Text(FormatInr(e.Amount));
                            table.Cell().Element(BodyCell).Text(e.PaymentState);
                        }
                    });
                }

                col.Item().PaddingTop(16).AlignRight().Width(260).ShowEntire().Table(totals =>
                {
                    totals.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.ConstantColumn(110);
                    });

                    TotalRow(totals, $"Total expenses ({expenses.Count})", total, bold: true);
                    TotalRow(totals, "Total Paid", paid, bold: false);
                    TotalRow(totals, "Total Unpaid", unpaid, bold: false);
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(9).FontColor(MutedColor));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.BorderBottom(1).BorderColor(TextColor).PaddingVertical(5).PaddingHorizontal(4)
            .DefaultTextStyle(t => t.SemiBold());

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(BorderColor).PaddingVertical(5).PaddingHorizontal(4);

    private static void TotalRow(TableDescriptor table, string label, decimal amount, bool bold)
    {
        var labelText = table.Cell().Element(BodyCell).Text(label);
        var amountText = table.Cell().Element(BodyCell).AlignRight().Text($"INR {FormatInr(amount)}");
        if (bold)
        {
            labelText.SemiBold();
            amountText.SemiBold();
        }
    }
}
