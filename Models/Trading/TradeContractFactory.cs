namespace WALLEve.Models.Trading;

/// <summary>
/// Factory für unveränderliche Trade-Verträge (Issue #37): baut die artspezifische
/// Eingabe-/Ausgabe-Kombination und prüft die Pflichtfelder/-Quellen JEDER Art.
/// Fehlende Pflichteingaben ergeben einen NICHT ausführbaren Vertrag
/// (IsActionable = false mit Begründung) statt scheinbar gültiger
/// Default-Nullwerte. Reine C#-Logik ohne Datenbankzugriff.
/// </summary>
public static class TradeContractFactory
{
    private static TradeContract Invalid(int tradingOpportunityId, int characterId, int typeId,
        string algorithmVersion, DateTime createdAt, TradeKind kind, IReadOnlyList<string> missing,
        string evidence) => new()
    {
        TradingOpportunityId = tradingOpportunityId,
        Kind = kind,
        CharacterId = characterId,
        TypeId = typeId,
        AlgorithmVersion = algorithmVersion,
        CreatedAt = createdAt,
        IsActionable = false,
        NotActionableReason = $"Fehlende Pflichteingaben: {string.Join(", ", missing)}",
        IsLegacy = false,
        Evidence = evidence
    };

    /// <summary>Bestandsverkauf: ortsgebundene Menge, Quote aus genau einem Markt-Snapshot.</summary>
    public static TradeContract CreateInventorySell(
        int tradingOpportunityId, int characterId, int typeId, string algorithmVersion,
        DateTime createdAt, decimal unitCostBasis, decimal sellPricePerUnit, int quantity,
        long sellLocationId, string sellLocationLabel, decimal brokerFee, decimal salesTax,
        decimal estimatedNetProceeds, decimal breakEvenPrice, int? marketSnapshotId, string? costBasisSource,
        decimal estimatedProfit, decimal requiredCapital, decimal netRoiPercent, string evidence,
        bool isLegacy = false)
    {
        var missing = new List<string>();
        if (unitCostBasis <= 0) missing.Add("UnitCostBasis");
        if (sellPricePerUnit <= 0) missing.Add("SellPricePerUnit");
        if (quantity <= 0) missing.Add("Quantity");
        if (sellLocationId <= 0) missing.Add("SellLocationId");
        if (marketSnapshotId is not > 0) missing.Add("MarketSnapshotId");
        if (string.IsNullOrWhiteSpace(costBasisSource)) missing.Add("CostBasisSource");
        if (string.IsNullOrWhiteSpace(evidence)) missing.Add("Evidence");

        if (missing.Count > 0)
            return Invalid(tradingOpportunityId, characterId, typeId, algorithmVersion, createdAt,
                TradeKind.InventorySell, missing, evidence);

        return new TradeContract
        {
            TradingOpportunityId = tradingOpportunityId,
            Kind = TradeKind.InventorySell,
            CharacterId = characterId,
            TypeId = typeId,
            AlgorithmVersion = algorithmVersion,
            CreatedAt = createdAt,
            IsActionable = true,
            IsLegacy = isLegacy,
            Evidence = evidence,
            EstimatedProfit = estimatedProfit,
            RequiredCapital = requiredCapital,
            NetRoiPercent = netRoiPercent,
            UnitCostBasis = unitCostBasis,
            SellPricePerUnit = sellPricePerUnit,
            Quantity = quantity,
            SellLocationId = sellLocationId,
            SellLocationLabel = sellLocationLabel,
            BrokerFee = brokerFee,
            SalesTax = salesTax,
            MarketSnapshotId = marketSnapshotId,
            CostBasisSource = costBasisSource,
            EstimatedNetProceeds = estimatedNetProceeds,
            BreakEvenPrice = breakEvenPrice
        };
    }

    /// <summary>Order-Preisänderung: bestehende Order, alter/neuer Preis, Modify-Fee.</summary>
    public static TradeContract CreateOrderAdjustment(
        int tradingOpportunityId, int characterId, int typeId, string algorithmVersion,
        DateTime createdAt, long orderId, int quantity, long locationId,
        decimal currentPricePerUnit, decimal suggestedPricePerUnit, int marketSnapshotId,
        decimal brokerRatePercent, decimal relistDiscountPercent, decimal modifyFee,
        decimal estimatedExtraNetProceeds, decimal estimatedProfit, decimal requiredCapital,
        decimal netRoiPercent, string evidence)
    {
        var missing = new List<string>();
        if (orderId <= 0) missing.Add("OrderId");
        if (quantity <= 0) missing.Add("Quantity");
        if (locationId <= 0) missing.Add("LocationId");
        if (currentPricePerUnit <= 0) missing.Add("CurrentPricePerUnit");
        if (suggestedPricePerUnit <= 0) missing.Add("SuggestedPricePerUnit");
        if (marketSnapshotId <= 0) missing.Add("MarketSnapshotId");
        if (string.IsNullOrWhiteSpace(evidence)) missing.Add("Evidence");

        if (missing.Count > 0)
            return Invalid(tradingOpportunityId, characterId, typeId, algorithmVersion, createdAt,
                TradeKind.OrderAdjustment, missing, evidence);

        return new TradeContract
        {
            TradingOpportunityId = tradingOpportunityId,
            Kind = TradeKind.OrderAdjustment,
            CharacterId = characterId,
            TypeId = typeId,
            AlgorithmVersion = algorithmVersion,
            CreatedAt = createdAt,
            IsActionable = true,
            Evidence = evidence,
            EstimatedProfit = estimatedProfit,
            RequiredCapital = requiredCapital,
            NetRoiPercent = netRoiPercent,
            OrderId = orderId,
            Quantity = quantity,
            LocationId = locationId,
            CurrentPricePerUnit = currentPricePerUnit,
            SuggestedPricePerUnit = suggestedPricePerUnit,
            MarketSnapshotId = marketSnapshotId,
            BrokerRatePercent = brokerRatePercent,
            RelistDiscountPercent = relistDiscountPercent,
            ModifyFee = modifyFee,
            EstimatedExtraNetProceeds = estimatedExtraNetProceeds
        };
    }

    /// <summary>Stations-Trade: Kauf und Verkauf an zwei Orten mit je eigenem Snapshot-Quote.</summary>
    public static TradeContract CreateStationTrade(
        int tradingOpportunityId, int characterId, int typeId, string algorithmVersion,
        DateTime createdAt, int quantity, long buyLocationId, long sellLocationId,
        int buyRegionId, int sellRegionId, decimal buyPricePerUnit, decimal sellPricePerUnit,
        int buyMarketSnapshotId, int sellMarketSnapshotId, decimal brokerRatePercent,
        decimal salesTaxPercent, int? jumpDistance, decimal estimatedNetProceeds,
        decimal estimatedProfit, decimal requiredCapital, decimal netRoiPercent, string evidence)
    {
        var missing = new List<string>();
        if (quantity <= 0) missing.Add("Quantity");
        if (buyLocationId <= 0) missing.Add("BuyLocationId");
        if (sellLocationId <= 0) missing.Add("SellLocationId");
        if (buyPricePerUnit <= 0) missing.Add("BuyPricePerUnit");
        if (sellPricePerUnit <= 0) missing.Add("SellPricePerUnit");
        if (buyMarketSnapshotId <= 0) missing.Add("BuyMarketSnapshotId");
        if (sellMarketSnapshotId <= 0) missing.Add("SellMarketSnapshotId");
        if (string.IsNullOrWhiteSpace(evidence)) missing.Add("Evidence");

        if (missing.Count > 0)
            return Invalid(tradingOpportunityId, characterId, typeId, algorithmVersion, createdAt,
                TradeKind.StationTrade, missing, evidence);

        return new TradeContract
        {
            TradingOpportunityId = tradingOpportunityId,
            Kind = TradeKind.StationTrade,
            CharacterId = characterId,
            TypeId = typeId,
            AlgorithmVersion = algorithmVersion,
            CreatedAt = createdAt,
            IsActionable = true,
            Evidence = evidence,
            EstimatedProfit = estimatedProfit,
            RequiredCapital = requiredCapital,
            NetRoiPercent = netRoiPercent,
            Quantity = quantity,
            BuyLocationId = buyLocationId,
            SellLocationId = sellLocationId,
            BuyRegionId = buyRegionId,
            SellRegionId = sellRegionId,
            BuyPricePerUnit = buyPricePerUnit,
            SellPricePerUnit = sellPricePerUnit,
            BuyMarketSnapshotId = buyMarketSnapshotId,
            SellMarketSnapshotId = sellMarketSnapshotId,
            BrokerRatePercent = brokerRatePercent,
            SalesTaxPercent = salesTaxPercent,
            JumpDistance = jumpDistance,
            EstimatedNetProceeds = estimatedNetProceeds
        };
    }

    /// <summary>Routen-Trade: Kauf an einem und Verkauf an einem anderen Ort, mit vollständiger Route.</summary>
    public static TradeContract CreateRouteTrade(
        int tradingOpportunityId, int characterId, int typeId, string algorithmVersion,
        DateTime createdAt, int quantity, int startSystemId, int endSystemId, int jumpCount,
        long buyLocationId, long sellLocationId, decimal buyPricePerUnit, decimal sellPricePerUnit,
        int buyMarketSnapshotId, int sellMarketSnapshotId, decimal brokerRatePercent,
        decimal salesTaxPercent, string routeJson, decimal estimatedNetProceeds,
        decimal estimatedProfit, decimal requiredCapital, decimal netRoiPercent, string evidence)
    {
        var missing = new List<string>();
        if (quantity <= 0) missing.Add("Quantity");
        if (startSystemId <= 0) missing.Add("StartSystemId");
        if (endSystemId <= 0) missing.Add("EndSystemId");
        if (buyLocationId <= 0) missing.Add("BuyLocationId");
        if (sellLocationId <= 0) missing.Add("SellLocationId");
        if (buyPricePerUnit <= 0) missing.Add("BuyPricePerUnit");
        if (sellPricePerUnit <= 0) missing.Add("SellPricePerUnit");
        if (buyMarketSnapshotId <= 0) missing.Add("BuyMarketSnapshotId");
        if (sellMarketSnapshotId <= 0) missing.Add("SellMarketSnapshotId");
        if (string.IsNullOrWhiteSpace(routeJson)) missing.Add("RouteJson");
        if (string.IsNullOrWhiteSpace(evidence)) missing.Add("Evidence");

        if (missing.Count > 0)
            return Invalid(tradingOpportunityId, characterId, typeId, algorithmVersion, createdAt,
                TradeKind.RouteTrade, missing, evidence);

        return new TradeContract
        {
            TradingOpportunityId = tradingOpportunityId,
            Kind = TradeKind.RouteTrade,
            CharacterId = characterId,
            TypeId = typeId,
            AlgorithmVersion = algorithmVersion,
            CreatedAt = createdAt,
            IsActionable = true,
            Evidence = evidence,
            EstimatedProfit = estimatedProfit,
            RequiredCapital = requiredCapital,
            NetRoiPercent = netRoiPercent,
            Quantity = quantity,
            StartSystemId = startSystemId,
            EndSystemId = endSystemId,
            JumpCount = jumpCount,
            BuyLocationId = buyLocationId,
            SellLocationId = sellLocationId,
            BuyPricePerUnit = buyPricePerUnit,
            SellPricePerUnit = sellPricePerUnit,
            BuyMarketSnapshotId = buyMarketSnapshotId,
            SellMarketSnapshotId = sellMarketSnapshotId,
            BrokerRatePercent = brokerRatePercent,
            SalesTaxPercent = salesTaxPercent,
            RouteJson = routeJson,
            EstimatedNetProceeds = estimatedNetProceeds
        };
    }
}