namespace PosterPrintRequest.Domain;

/// <summary>A consumable type kept in the database and edited in Configuration (Consumables Inventory Contract section 4).</summary>
public sealed class ConsumableType
{
    public int ConsumableTypeId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Computed by the database: UPPER(Name) in a binary collation. Never written by the application (section 4.1a).</summary>
    public string NameKey { get; private set; } = string.Empty;

    /// <summary>One of <see cref="ConsumableStockModes"/>.</summary>
    public string StockMode { get; set; } = ConsumableStockModes.DateOptional;

    public bool AcknowledgeEarliestExpiration { get; set; }

    public bool UsesPrinterCompatibility { get; set; } = true;

    public int DisplayOrder { get; set; }

    public bool Active { get; set; } = true;

    public List<PrintingConsumable> Consumables { get; set; } = [];
}

/// <summary>Section 4.1: how the stock of a type is dated.</summary>
public static class ConsumableStockModes
{
    public const string Undated = "Undated";

    public const string DateRequired = "DateRequired";

    public const string DateOptional = "DateOptional";

    public static bool IsValid(string? value) => value is Undated or DateRequired or DateOptional;
}
