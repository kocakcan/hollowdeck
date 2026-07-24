using Godot;
using Hollowdeck.Combat;
using Hollowdeck.Data;
using Hollowdeck.Map;

namespace Hollowdeck.UI;

// Convention-based art lookup: assets are named by definition id
// (assets/icons/cards/<card_id>.svg, assets/sprites/enemies/<enemy_id>.png),
// so new content picks up art by dropping in a matching file - no schema or
// code change. Missing art returns null and views degrade to text-only.
public static class ArtAssets
{
    public static Texture2D? CardIcon(string cardId) => Load($"res://assets/icons/cards/{cardId}.svg");
    public static Texture2D? RelicIcon(string relicId) => Load($"res://assets/icons/relics/{relicId}.svg");
    public static Texture2D? PotionIcon(string potionId) => Load($"res://assets/icons/potions/{potionId}.svg");
    public static Texture2D? EnemySprite(string enemyId) => Load($"res://assets/sprites/enemies/{enemyId}.png");
    public static Texture2D? PlayerSprite() => Load("res://assets/sprites/player.png");

    public static Texture2D? MapIcon(MapNodeType type) => Load($"res://assets/icons/map/{type switch
    {
        MapNodeType.Combat => "fight",
        MapNodeType.Elite => "elite",
        MapNodeType.Rest => "rest",
        MapNodeType.Shop => "shop",
        MapNodeType.Treasure => "treasure",
        MapNodeType.Boss => "boss",
        MapNodeType.Event => "event",
        _ => "unknown",
    }}.svg");

    public static Texture2D? IntentIcon(IntentType type) => Load($"res://assets/icons/intents/{type switch
    {
        IntentType.Attack => "attack",
        IntentType.Defend => "defend",
        IntentType.Buff => "buff",
        _ => "unknown",
    }}.svg");

    public static Texture2D? StatusIcon(StatusType type) =>
        Load($"res://assets/icons/status/{type.ToString().ToLowerInvariant()}.svg");

    public static Texture2D? BackgroundTile(string name) => Load($"res://assets/backgrounds/{name}.png");

    private static Texture2D? Load(string path) =>
        ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
}
