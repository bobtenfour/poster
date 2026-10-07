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

    public const string ExpiringSoon = "Expiring soon";

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

    public static string? ExpirationAlert(bool hasExpirationDate, DateOnly? expirationDate, DateOnly today, int? warningDays)
    {
        if (!hasExpirationDate || expirationDate is null)
        {
            return null;
        }

        if (expirationDate.Value <= today)
        {
            return Expired;
        }

        if (warningDays is null)
        {
            return null;
        }

        DateOnly latest;
        try
        {
            latest = today.AddDays(warningDays.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        return expirationDate.Value <= latest ? ExpiringSoon : null;
    }
}
