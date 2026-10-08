namespace PosterPrintRequest.Web.Requests;

public static class ConsumableCategory
{
    public const string Cartridge = "Cartridge";

    public const string Paper = "Paper";

    public const string Laminating = "Laminating";
}

public static class ConsumableStock
{
    public const string Depleted = "Depleted";

    public const string LowStock = "Low stock";

    public const string InStock = "In stock";

    public const string Expired = "Expired";

    public const string ExpiresWithinSixMonths = "Expires within 6 months";

    public const string ExpiresWithinTwelveMonths = "Expires within 12 months";

    public static string FromQuantity(int quantity, int lowStockThreshold, int criticalStockThreshold)
    {
        if (quantity <= criticalStockThreshold)
        {
            return Depleted;
        }

        if (quantity <= lowStockThreshold)
        {
            return LowStock;
        }

        return InStock;
    }

    public static string? ExpirationAlert(DateOnly? expirationDate, DateOnly today)
    {
        if (expirationDate is null)
        {
            return null;
        }

        if (expirationDate.Value <= today)
        {
            return Expired;
        }

        if (expirationDate.Value <= today.AddMonths(6))
        {
            return ExpiresWithinSixMonths;
        }

        if (expirationDate.Value <= today.AddMonths(12))
        {
            return ExpiresWithinTwelveMonths;
        }

        return null;
    }
}
