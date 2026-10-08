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
    public void Expiration_alerts_use_today_and_give_six_months_precedence()
    {
        Assert.Null(ConsumableStock.ExpirationAlert(null, Today));
        Assert.Equal(ConsumableStock.Expired, ConsumableStock.ExpirationAlert(Today, Today));
        Assert.Equal(ConsumableStock.Expired, ConsumableStock.ExpirationAlert(Today.AddDays(-1), Today));
        Assert.Equal(ConsumableStock.ExpiresWithinSixMonths, ConsumableStock.ExpirationAlert(Today.AddMonths(6), Today));
        Assert.Equal(ConsumableStock.ExpiresWithinTwelveMonths, ConsumableStock.ExpirationAlert(Today.AddMonths(6).AddDays(1), Today));
        Assert.Equal(ConsumableStock.ExpiresWithinTwelveMonths, ConsumableStock.ExpirationAlert(Today.AddMonths(12), Today));
        Assert.Null(ConsumableStock.ExpirationAlert(Today.AddMonths(12).AddDays(1), Today));
    }
}
