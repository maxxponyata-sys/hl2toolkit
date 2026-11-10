namespace HL2Toolkit;

/// <summary>Готовые «архетипы» для перетаскивания: NPC, оружие, предметы, транспорт, свет.</summary>
public sealed class EntityDef
{
    public string Category = "", Title = "", Cls = "", Model = "";
    public ObjKind Kind = ObjKind.Entity;
    public Dictionary<string, string> Keys = new();
    public EntityDef(string cat, string title, string cls, string model, params string[] kv)
    {
        Category = cat; Title = title; Cls = cls; Model = model;
        for (int i = 0; i + 1 < kv.Length; i += 2) Keys[kv[i]] = kv[i + 1];
    }
}

public static class Catalog
{
    public static readonly List<EntityDef> Entities = new()
    {
        new("Игрок", "Старт игрока", "info_player_start", ""),
        new("Враги", "Солдат Альянса (SMG)", "npc_combine_s", "models/combine_soldier.mdl", "additionalequipment", "weapon_smg1"),
        new("Враги", "Солдат Альянса (AR2)", "npc_combine_s", "models/combine_soldier.mdl", "additionalequipment", "weapon_ar2", "model", "models/combine_soldier.mdl"),
        new("Враги", "Метрокоп", "npc_metropolice", "models/police.mdl", "additionalequipment", "weapon_pistol"),
        new("Враги", "Зомби", "npc_zombie", "models/zombie/classic.mdl"),
        new("Враги", "Быстрый зомби", "npc_fastzombie", "models/zombie/fast.mdl"),
        new("Враги", "Хедкраб", "npc_headcrab", "models/headcrabclassic.mdl"),
        new("Враги", "Муравьиный лев", "npc_antlion", "models/antlion.mdl"),
        new("Враги", "Мэнхэк", "npc_manhack", "models/manhack.mdl"),
        new("Враги", "Турель", "npc_turret_floor", "models/combine_turrets/floor_turret.mdl"),
        new("Союзники", "Повстанец", "npc_citizen", "models/humans/group03/male_07.mdl", "citizentype", "3", "additionalequipment", "weapon_smg1"),
        new("Союзники", "Аликс", "npc_alyx", "models/alyx.mdl", "additionalequipment", "weapon_alyxgun"),
        new("Союзники", "Барни", "npc_barney", "models/barney.mdl", "additionalequipment", "weapon_ar2"),
        new("Союзники", "Пёс", "npc_dog", "models/dog.mdl"),
        new("Оружие", "Монтировка", "weapon_crowbar", "models/weapons/w_crowbar.mdl"),
        new("Оружие", "Пистолет", "weapon_pistol", "models/weapons/w_pistol.mdl"),
        new("Оружие", "Револьвер .357", "weapon_357", "models/weapons/w_357.mdl"),
        new("Оружие", "SMG", "weapon_smg1", "models/weapons/w_smg1.mdl"),
        new("Оружие", "Импульсная винтовка", "weapon_ar2", "models/weapons/w_irifle.mdl"),
        new("Оружие", "Дробовик", "weapon_shotgun", "models/weapons/w_shotgun.mdl"),
        new("Оружие", "Арбалет", "weapon_crossbow", "models/weapons/w_crossbow.mdl"),
        new("Оружие", "РПГ", "weapon_rpg", "models/weapons/w_rocket_launcher.mdl"),
        new("Оружие", "Граната", "weapon_frag", "models/weapons/w_grenade.mdl"),
        new("Оружие", "Грави-пушка", "weapon_physcannon", "models/weapons/w_physics.mdl"),
        new("Предметы", "Аптечка", "item_healthkit", "models/items/healthkit.mdl"),
        new("Предметы", "Батарея брони", "item_battery", "models/items/battery.mdl"),
        new("Предметы", "Патроны SMG", "item_ammo_smg1", "models/items/boxmrounds.mdl"),
        new("Предметы", "Патроны пистолета", "item_ammo_pistol", "models/items/boxsrounds.mdl"),
        new("Предметы", "Патроны дробовика", "item_box_buckshot", "models/items/boxbuckshot.mdl"),
        new("Предметы", "Костюм HEV", "item_suit", "models/items/hevsuit.mdl"),
        new("Транспорт", "Багги", "prop_vehicle_jeep", "models/buggy.mdl", "vehiclescript", "scripts/vehicles/jeep_test.txt", "actionScale", "1"),
        new("Транспорт", "Аэроглиссер", "prop_vehicle_airboat", "models/airboat.mdl", "vehiclescript", "scripts/vehicles/airboat.txt", "actionScale", "1"),
        new("Свет", "Лампа (точечный свет)", "light", "") { Kind = ObjKind.Light },
        new("Свет", "Прожектор", "light_spot", "") { Kind = ObjKind.Light },
    };

    public static readonly string[] BlockMaterials =
    {
        "dev/dev_measuregeneric01b", "dev/dev_measurewall01a", "brick/brickwall001a", "brick/brickwall003a",
        "brick/brickwall031b", "concrete/concretefloor001a", "concrete/concretewall001a", "concrete/concretewall004a",
        "metal/metalwall001a", "metal/metalwall048b", "metal/metaldoor001a", "wood/woodwall009a", "wood/woodfloor005a",
        "wood/woodshelf002a", "plaster/plasterwall003a", "tile/tilefloor001a", "stone/stonewall012a",
        "nature/rockwall004a", "building_template/buildingset040a", "tools/toolsnodraw", "tools/toolsplayerclip",
    };

    public static readonly string[] Skies =
    {
        "sky_day01_01", "sky_day01_04", "sky_day01_05", "sky_day01_06", "sky_day01_07", "sky_day01_08", "sky_day01_09",
        "sky_day02_01", "sky_day02_02", "sky_day02_03", "sky_day02_04", "sky_day02_05", "sky_day02_06", "sky_day02_07",
        "sky_day02_09", "sky_day02_10", "sky_day03_01", "sky_day03_02", "sky_day03_03", "sky_day03_04", "sky_day03_05",
        "sky_day03_06", "sky_borealis01", "sky_wasteland02",
    };

    public static readonly string[] Waters =
    {
        "nature/water_canals_water2", "nature/water_coast01", "nature/water_wasteland002a", "nature/water_dam02",
    };
}
