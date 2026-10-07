using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

public sealed class ConsumableStockTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Theory]
    [InlineData(0, 0, 0, ConsumableStock.Depleted)]
    [InlineData(1, 0, 0, ConsumableStock.InStock)]
    [InlineData(2, 2, 0, ConsumableStock.LowStock)]
    [InlineData(1, 2, 0, ConsumableStock.LowStock)]
    [InlineData(0, 2, 0, ConsumableStock.Depleted)]
    [InlineData(3, 2, 1, ConsumableStock.InStock)]
    [InlineData(1, 3, 1, ConsumableStock.Depleted)]
    public void Quantity_is_compared_with_the_critical_and_low_stock_thresholds(int quantity, int low, int critical, string status)
    {
        Assert.Equal(status, ConsumableStock.FromQuantity(quantity, low, critical));
    }

    [Fact]
    public void Expiration_uses_only_a_recorded_date_and_a_configured_period()
    {
        Assert.Null(ConsumableStock.ExpirationAlert(false, new DateOnly(2026, 10, 1), Today, 30));
        Assert.Null(ConsumableStock.ExpirationAlert(true, null, Today, 30));
        Assert.Null(ConsumableStock.ExpirationAlert(true, new DateOnly(2026, 10, 20), Today, null));
        Assert.Equal(ConsumableStock.Expired, ConsumableStock.ExpirationAlert(true, Today, Today, null));
        Assert.Equal(ConsumableStock.Expired, ConsumableStock.ExpirationAlert(true, new DateOnly(2026, 10, 6), Today, 30));
        Assert.Equal(ConsumableStock.ExpiringSoon, ConsumableStock.ExpirationAlert(true, new DateOnly(2026, 10, 20), Today, 30));
        Assert.Null(ConsumableStock.ExpirationAlert(true, new DateOnly(2026, 12, 1), Today, 30));
        Assert.Equal(ConsumableStock.ExpiringSoon, ConsumableStock.ExpirationAlert(true, Today.AddDays(7), Today, 7));
    }
}
