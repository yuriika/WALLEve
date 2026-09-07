using WALLEve.Services.Market;
using WALLEve.Services.Market.Interfaces;

namespace WALLEve.Tests;

/// <summary>
/// Tests für die Auto-Track-Auswahl der wertvollsten Bestands-Items.
/// </summary>
public class TrackSelectionTests
{
    private static InventoryItem Item(int typeId, double marketValue) => new()
    {
        TypeId = typeId,
        TypeName = $"Item {typeId}",
        TotalQuantity = 100,
        BestSellPrice = marketValue / 100 // Marktwert = Preis × Menge
    };

    [Fact]
    public void SelectTopValueItems_LimitZero_ReturnsEmpty()
    {
        var items = new List<InventoryItem> { Item(1, 1000) };

        var result = TrackSelection.SelectTopValueItems(items, 0);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectTopValueItems_NegativeLimit_ReturnsEmpty()
    {
        var items = new List<InventoryItem> { Item(1, 1000) };

        var result = TrackSelection.SelectTopValueItems(items, -5);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectTopValueItems_SortsByMarketValueDescending()
    {
        var items = new List<InventoryItem>
        {
            Item(1, 500),
            Item(2, 9000),
            Item(3, 100)
        };

        var result = TrackSelection.SelectTopValueItems(items, 2);

        Assert.Equal(new[] { 2, 1 }, result); // teuerste zuerst
    }

    [Fact]
    public void SelectTopValueItems_LimitLargerThanCount_ReturnsAll()
    {
        var items = new List<InventoryItem>
        {
            Item(1, 500),
            Item(2, 9000)
        };

        var result = TrackSelection.SelectTopValueItems(items, 50);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SelectTopValueItems_EmptyInventory_ReturnsEmpty()
    {
        var result = TrackSelection.SelectTopValueItems(new List<InventoryItem>(), 10);

        Assert.Empty(result);
    }
}