using System.Text.Json;

namespace MonveriRegister.Models;

public class DenominationBreakdown
{
    // Bills
    public int Bills100 { get; set; }
    public int Bills50 { get; set; }
    public int Bills20 { get; set; }
    public int Bills10 { get; set; }
    public int Bills5 { get; set; }
    public int Bills1 { get; set; }

    // Coins
    public int CoinsDollar { get; set; }
    public int CoinsHalfDollar { get; set; }
    public int CoinsQuarter { get; set; }
    public int CoinsDime { get; set; }
    public int CoinsNickel { get; set; }
    public int CoinsPenny { get; set; }

    // Rolls
    public int RollsDollar { get; set; }
    public int RollsHalfDollar { get; set; }
    public int RollsQuarter { get; set; }
    public int RollsDime { get; set; }
    public int RollsNickel { get; set; }
    public int RollsPenny { get; set; }

    // Straps
    public int Straps20 { get; set; }
    public int Straps10 { get; set; }
    public int Straps5 { get; set; }
    public int Straps1 { get; set; }

    // Mirrors PHP getDenominationValues()
    public static readonly Dictionary<string, decimal> DenominationValues = new()
    {
        ["bills_100"] = 100.00m,
        ["bills_50"] = 50.00m,
        ["bills_20"] = 20.00m,
        ["bills_10"] = 10.00m,
        ["bills_5"] = 5.00m,
        ["bills_1"] = 1.00m,
        ["coins_100"] = 1.00m,
        ["coins_50"] = 0.50m,
        ["coins_25"] = 0.25m,
        ["coins_10"] = 0.10m,
        ["coins_5"] = 0.05m,
        ["coins_1"] = 0.01m,
        ["rolls_100"] = 25.00m,
        ["rolls_50"] = 10.00m,
        ["rolls_25"] = 10.00m,
        ["rolls_10"] = 5.00m,
        ["rolls_5"] = 2.00m,
        ["rolls_1"] = 0.50m,
        ["straps_20"] = 500.00m,
        ["straps_10"] = 250.00m,
        ["straps_5"] = 100.00m,
        ["straps_1"] = 25.00m,
    };

    public decimal CalculateTotal()
    {
        return (Bills100 * 100.00m) + (Bills50 * 50.00m) + (Bills20 * 20.00m) +
               (Bills10 * 10.00m) + (Bills5 * 5.00m) + (Bills1 * 1.00m) +
               (CoinsDollar * 1.00m) + (CoinsHalfDollar * 0.50m) + (CoinsQuarter * 0.25m) +
               (CoinsDime * 0.10m) + (CoinsNickel * 0.05m) + (CoinsPenny * 0.01m) +
               (RollsDollar * 25.00m) + (RollsHalfDollar * 10.00m) + (RollsQuarter * 10.00m) +
               (RollsDime * 5.00m) + (RollsNickel * 2.00m) + (RollsPenny * 0.50m) +
               (Straps20 * 500.00m) + (Straps10 * 250.00m) + (Straps5 * 100.00m) + (Straps1 * 25.00m);
    }

    public Dictionary<string, int> ToDictionary()
    {
        return new Dictionary<string, int>
        {
            ["bills_100"] = Bills100, ["bills_50"] = Bills50, ["bills_20"] = Bills20,
            ["bills_10"] = Bills10, ["bills_5"] = Bills5, ["bills_1"] = Bills1,
            ["coins_100"] = CoinsDollar, ["coins_50"] = CoinsHalfDollar, ["coins_25"] = CoinsQuarter,
            ["coins_10"] = CoinsDime, ["coins_5"] = CoinsNickel, ["coins_1"] = CoinsPenny,
            ["rolls_100"] = RollsDollar, ["rolls_50"] = RollsHalfDollar, ["rolls_25"] = RollsQuarter,
            ["rolls_10"] = RollsDime, ["rolls_5"] = RollsNickel, ["rolls_1"] = RollsPenny,
            ["straps_20"] = Straps20, ["straps_10"] = Straps10, ["straps_5"] = Straps5, ["straps_1"] = Straps1,
        };
    }

    public string ToJson() => JsonSerializer.Serialize(ToDictionary());

    public static DenominationBreakdown FromJson(string json)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new();
        return new DenominationBreakdown
        {
            Bills100 = dict.GetValueOrDefault("bills_100"),
            Bills50 = dict.GetValueOrDefault("bills_50"),
            Bills20 = dict.GetValueOrDefault("bills_20"),
            Bills10 = dict.GetValueOrDefault("bills_10"),
            Bills5 = dict.GetValueOrDefault("bills_5"),
            Bills1 = dict.GetValueOrDefault("bills_1"),
            CoinsDollar = dict.GetValueOrDefault("coins_100"),
            CoinsHalfDollar = dict.GetValueOrDefault("coins_50"),
            CoinsQuarter = dict.GetValueOrDefault("coins_25"),
            CoinsDime = dict.GetValueOrDefault("coins_10"),
            CoinsNickel = dict.GetValueOrDefault("coins_5"),
            CoinsPenny = dict.GetValueOrDefault("coins_1"),
            RollsDollar = dict.GetValueOrDefault("rolls_100"),
            RollsHalfDollar = dict.GetValueOrDefault("rolls_50"),
            RollsQuarter = dict.GetValueOrDefault("rolls_25"),
            RollsDime = dict.GetValueOrDefault("rolls_10"),
            RollsNickel = dict.GetValueOrDefault("rolls_5"),
            RollsPenny = dict.GetValueOrDefault("rolls_1"),
            Straps20 = dict.GetValueOrDefault("straps_20"),
            Straps10 = dict.GetValueOrDefault("straps_10"),
            Straps5 = dict.GetValueOrDefault("straps_5"),
            Straps1 = dict.GetValueOrDefault("straps_1"),
        };
    }
}
