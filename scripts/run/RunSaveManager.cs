using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

// Persists RunState + the run seed to disk so quitting mid-run doesn't lose
// progress. Same tolerant-JSON idiom as MetaProgressionManager/
// SettingsManager (FileAccess, PropertyNameCaseInsensitive, try/catch ->
// treat as "no save"), path-parameterized the same way for test isolation.
// A plain static class, not an autoload: TryLoad is only ever called
// explicitly from MainMenu's "Continue" handler, no _Ready-time load needed.
public static class RunSaveManager
{
    public const string DefaultPath = "user://run_save.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        // MapNode (embedded in RunSaveData.MapNodes) exposes its data as
        // public fields, not properties - System.Text.Json ignores fields
        // by default, so without this a saved map would silently round-trip
        // as a list of empty nodes.
        IncludeFields = true,
    };

    public static bool SaveExists(string? path = null) => FileAccess.FileExists(path ?? DefaultPath);

    public static void Save(int runSeed, string? path = null)
    {
        var data = new RunSaveData
        {
            RunSeed = runSeed,
            Gold = RunState.Gold,
            PlayerMaxHp = RunState.PlayerMaxHp,
            PlayerCurrentHp = RunState.PlayerCurrentHp,
            DeckCardIds = RunState.Deck.Select(c => c.Id).ToList(),
            RelicIds = RunState.Relics.Select(r => r.Definition.Id).ToList(),
            Potions = RunState.Potions.Select(p => new PotionSaveEntry { DefinitionId = p.DefinitionId }).ToList(),
            MapNodes = RunState.MapNodes,
            CurrentNodeId = RunState.CurrentNodeId,
            VisitedNodeIds = RunState.VisitedNodeIds.ToList(),
        };
        using var file = FileAccess.Open(path ?? DefaultPath, FileAccess.ModeFlags.Write);
        file.StoreString(JsonSerializer.Serialize(data, Options));
    }

    // Populates RunState in place (same role as RunState.InitNewRun) and
    // returns the seed to re-init RngStreams with; null if there's no save
    // or it's unreadable - caller falls back to StartNewRun() either way.
    public static int? TryLoad(string? path = null)
    {
        var p = path ?? DefaultPath;
        if (!FileAccess.FileExists(p)) return null;

        RunSaveData? data;
        try
        {
            using var file = FileAccess.Open(p, FileAccess.ModeFlags.Read);
            data = JsonSerializer.Deserialize<RunSaveData>(file.GetAsText(), Options);
        }
        catch (Exception e)
        {
            GD.PushWarning($"RunSaveManager: '{p}' unreadable ({e.Message}); ignoring save.");
            return null;
        }
        if (data is null) return null;

        RunState.Gold = data.Gold;
        RunState.PlayerMaxHp = data.PlayerMaxHp;
        RunState.PlayerCurrentHp = data.PlayerCurrentHp;
        // Stale ids (content removed/renamed post-save) are dropped, never
        // thrown on - same membership-check discipline documented on
        // MetaProgressionManager.IsRelicUnlocked. Upgraded cards round-trip
        // as "<baseId>+" (CardUpgrade.Apply's naming) rather than their own
        // CardDatabase entry - resolve the base id and re-derive the
        // upgraded definition instead of a direct lookup.
        RunState.Deck = data.DeckCardIds
            .Select(ResolveSavedCardId)
            .Where(card => card is not null)
            .Select(card => card!)
            .ToList();
        RunState.Relics = data.RelicIds
            .Where(id => RelicDatabase.All.Any(r => r.Id == id))
            .Select(id => new RelicInstance(RelicDatabase.Get(id))).ToList();
        RunState.Potions = data.Potions
            .Where(entry => PotionDatabase.All.Any(pd => pd.Id == entry.DefinitionId))
            .Select(entry => new PotionInstance(PotionDatabase.Get(entry.DefinitionId))).ToList();
        RunState.MapNodes = data.MapNodes;
        RunState.CurrentNodeId = data.CurrentNodeId;
        RunState.VisitedNodeIds = data.VisitedNodeIds.ToHashSet();

        return data.RunSeed;
    }

    private static CardDefinition? ResolveSavedCardId(string id)
    {
        var baseId = id.EndsWith("+") ? id[..^1] : id;
        var baseCard = CardDatabase.All.FirstOrDefault(c => c.Id == baseId);
        if (baseCard is null) return null;
        return id.EndsWith("+") ? CardUpgrade.Apply(baseCard) : baseCard;
    }

    public static void Delete(string? path = null)
    {
        var p = path ?? DefaultPath;
        if (FileAccess.FileExists(p)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(p));
    }
}
