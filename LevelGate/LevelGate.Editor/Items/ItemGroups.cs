namespace LevelGate.Editor;

/// <summary>Item categories (the editor's tabs) by base class, and weapon classes.</summary>
public static class ItemGroups
{
    /// <summary>Group key, display name, base class ids (an item belongs to the first group whose class is one of its ancestors).</summary>
    public static readonly (string Key, string Name, string[] Classes)[] All =
    {
        ("Weapons", "Weapons", new[] { "5422acb9af1c889c16000029" }),
        ("Melee", "Melee", new[] { "5447e1d04bdc2dff2f8b4567" }),
        ("Grenades", "Grenades", new[] { "543be6564bdc2df4348b4568" }),
        ("Ammo", "Ammo", new[] { "5485a8684bdc2da71d8b4567", "543be5cb4bdc2deb348b4568" }),
        ("WeaponParts", "Weapon Parts", new[] { "5448fe124bdc2da5018b4567" }),
        ("Armor", "Armor", new[] { "5448e54d4bdc2dcc718b4568", "644120aa86ffbe10ee032b6f" }),
        ("Headwear", "Headwear", new[] { "5a341c4086f77401f2541505" }),
        ("Rigs", "Rigs", new[] { "5448e5284bdc2dcb718b4567" }),
        ("Backpacks", "Backpacks", new[] { "5448e53e4bdc2d60728b4567" }),
        ("Gear", "Other Gear", new[] { "543be5f84bdc2dd4348b456a", "57bef4c42459772e8d35a53b" }),
        ("Medical", "Medical", new[] { "543be5664bdc2dd4348b4569" }),
        ("Food", "Food & Drink", new[] { "5448e8d04bdc2ddf718b4569", "5448e8d64bdc2dce718b4568" }),
        ("Electronics", "Electronics", new[] { "57864a66245977548f04a81f" }),
        ("Barter", "Barter Items", new[] { "5448eb774bdc2d0a728b4567", "5448ecbe4bdc2d60728b4568", "616eb7aea207f41933308f46" }),
        ("Keys", "Keys", new[] { "543be5e94bdc2df1348b4568" }),
        ("Containers", "Containers", new[] { "5795f317245977243854e041", "5671435f4bdc2d96058b4569" }),
        ("Special", "Special", new[] { "5447e0e74bdc2d3c308b4567", "567849dd4bdc2d150f8b456e" }),
    };

    /// <summary>Weapon classes: key = the class name the game uses.</summary>
    public static readonly (string Key, string Name, string Class)[] WeaponClasses =
    {
        ("AssaultRifle", "Assault Rifles", "5447b5f14bdc2d61278b4567"),
        ("AssaultCarbine", "Assault Carbines", "5447b5fc4bdc2d87278b4567"),
        ("Smg", "SMGs", "5447b5e04bdc2d62278b4567"),
        ("Shotgun", "Shotguns", "5447b6094bdc2dc3278b4567"),
        ("MarksmanRifle", "Marksman Rifles", "5447b6194bdc2d67278b4567"),
        ("SniperRifle", "Bolt-Action Rifles", "5447b6254bdc2dc3278b4568"),
        ("MachineGun", "Machine Guns", "5447bed64bdc2d97278b4568"),
        ("Pistol", "Pistols", "5447b5cf4bdc2d65278b4567"),
        ("Revolver", "Revolvers", "617f1ef5e8b54b0998387733"),
        ("GrenadeLauncher", "Grenade Launchers", "5447bedf4bdc2d87278b4568"),
        ("Knife", "Melee", "5447e1d04bdc2dff2f8b4567"),
        ("ThrowWeap", "Grenades", "543be6564bdc2df4348b4568"),
    };
}
