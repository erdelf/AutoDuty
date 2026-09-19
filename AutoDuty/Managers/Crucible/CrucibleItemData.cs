using System;
using System.Linq;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace AutoDuty.Managers;

internal static class CrucibleItemData
{
    public static readonly uint[] ShopHealing =
    [
        140, // Beast Potion Kit
        79,  // G4 Beast Potion
        78,  // G3 Beast Potion
        77,  // G2 Beast Potion
        76,  // G1 Beast Potion
        82,  // G3 Crucible Ash
        81,  // G2 Crucible Ash
        80   // G1 Crucible Ash
    ];

    public static readonly uint[] ShopGear =
    [
        23, // Angel Robe
        3,  // Ring of Curing
        73, // Empress Hairpin
        2,  // Ring of Sacrifice
        27, // Hero's Crown
        24, // Master Shield
        15, // Earth Shield
        49, // Wind Armor
        21, // Genji Armor
        47, // Wind Shield
        25, // Mystic Veil
        16, // Water Shield
        35, // Astral Mantle
        44, // Ninja Suit
        9,  // Power Armlet
        11, // Green Beret
        51, // Mythril Armor
        58, // Thief's Garb
        72, // Demonic Armor
        32, // Umbral Mantle
        62, // Thunder Armor
        48, // Wind Mask
        28, // Mirage Vest
        14, // Ice Shield
        54, // Beast Mask
        26, // Black Cowl
        66, // Flame Shield
        52, // Mythril Gloves
        70, // Spirited Ring
        34, // Umbral Wristlet
        13, // Ring of Protection
        69, // Merchant's Shoes
        29, // Soulreaper's Armor
        1,  // Belt of Constitution
        20, // Genji Greatshield
        12, // Mystic Boots
        31, // Coward's Knife
        37, // Astral Wristlet
        53, // Mythril Greaves
        10, // Twisted Headband
        56, // Beast Earring
        63, // Warded Shield
        17, // Force Shield
        33, // Umbral Band
        36, // Astral Band
        75, // Warrior's Buckler
        6,  // Steel Armor
        5,  // Hexed Hat
        68, // Merchant's Garb
        60, // Thief's Boots
        61, // Thunder Axe
        50, // Heavy Axe
        71, // Demonic Helm
        65, // Flame Knife
        19, // Genji Gloves
        57, // Thief's Knife
        7,  // Crown of the Wild
        8,  // Staff of the Wise
        30, // Briar Armor
        38, // Flame-wreathed Axe
        39, // Icebitten Axe
        40, // Thunderstruck Axe
        41, // Earthcrushed Axe
        42, // Deepdrowned Axe
        43, // Windblown Axe
        64, // Gold Hairpin
        22, // Silver Specs
        46, // Ninja Eyepatch
        55, // Beastly Knife
        74, // Haste Belt
        45, // Ninja Gloves
        59, // Thief's Gloves
        67, // Merchant's Cap
        18, // Crimson Ribbon
        4   // Chemist's Satchel
    ];

    public static readonly uint[] ShopFeed =
    [
        144, // G1 Primafodder
        145, // G2 Primafodder
        149, // Crab Ball Simular
        153, // Yellow Egg Simular
        155, // Milk Simular
        163, // Magnum Water
        165, // Noble Blood Simular
        174, // Lugworm Simular
        185, // Cream Cheese Simular
        188, // Cornbread Simular
        191, // Belladonna Simular
        202, // Cottage Cheese Simular
        203, // Lassi Simular
        148, // Honey Simular
        154, // Tomato Simular
        157, // Porcini Simular
        158, // Morel Simular
        159, // Black Truffle Simular
        160, // White Truffle Simular
        161, // Mushroom Simular
        166, // Lily Simular
        169, // Black Scorpion Simular
        171, // Angelfish Simular
        175, // Crucible Tonic
        176, // Carrot Simular
        177, // Onion Simular
        178, // Lettuce Simular
        179, // Lemon Simular
        180, // Untaming Oil
        181, // Mucus Simular
        182, // Sap Simular
        183, // Salt Simular
        184, // Syrup Simular
        187, // Toast Simular
        194, // Herbal Tea Simular
        195, // Honeycomb Simular
        197, // Grape Juice Simular
        146, // Meat Simular
        147, // Hydrolixer
        150, // Banana Simular
        151, // Berry Simular
        152, // Egg Simular
        156, // Rolanberry Cheese Simular
        162, // Sole Simular
        164, // Blood Simular
        167, // Flounder Simular
        170, // Herring Simular
        172, // Grape Simular
        173, // Orange Simular
        186, // Red Egg Simular
        189, // Mandrake Simular
        190, // Tarantula Simular
        192, // Steak Simular
        193, // Roe Simular
        196, // Orange Juice Simular
        198, // Skewer Simular
        199, // Pineapple Juice Simular
        200, // Oyster Simular
        201, // Blue Cheese Simular
        168  // White Scorpion Simular
    ];

    public static readonly uint[] FightItems =
    [
        140, // Beast Potion Kit
        79,  // G4 Beast Potion
        78,  // G3 Beast Potion
        77,  // G2 Beast Potion
        76,  // G1 Beast Potion
        82,  // G3 Crucible Ash
        81,  // G2 Crucible Ash
        80,  // G1 Crucible Ash
        112, // Potion of Tempered Constitution
        102, // Crucible Tannin
        137, // Tome of the Impervious
        135  // Vampiric Essence
    ];

    public static readonly uint[] BoardItems =
    [
        79, // G4 Beast Potion
        78, // G3 Beast Potion
        77, // G2 Beast Potion
        76, // G1 Beast Potion
        82, // G3 Crucible Ash
        81, // G2 Crucible Ash
        80  // G1 Crucible Ash
    ];

    public static readonly uint[] TreasureOrder = ShopHealing.Concat(FightItems).Concat(ShopGear).Concat(ShopFeed).Distinct().ToArray();

    private static ExcelSheet<RawRow> Items => Svc.Data.GetExcelSheet<RawRow>(name: "XBMItem");

    public static string NameOf(uint row) =>
        Items.TryGetRow(row, out RawRow item) && item.ReadStringColumn(11).ExtractText() is { Length: > 0 } name ? name : $"item {row}";

    public static uint ItemIn(string text) =>
        Items.Where(x => x.RowId > 0)
             .Select(x => (x.RowId, Name: x.ReadStringColumn(11).ExtractText()))
             .Where(x => x.Name.Length > 0 && text.Contains(x.Name, StringComparison.OrdinalIgnoreCase))
             .OrderByDescending(x => x.Name.Length)
             .Select(x => x.RowId)
             .FirstOrDefault();

    public static int TreasureRank(uint row)
    {
        int index = Array.IndexOf(TreasureOrder, row);
        return index < 0 ? int.MaxValue : index;
    }
}
