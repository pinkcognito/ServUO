using System.Collections.Generic;
using System.IO;

using Server.Custom.AIAgents;
using Server.Mobiles;

namespace Server.Tests.Tier2;

// Issue #68 Tier 2: the minimum headless world a companion needs to exist
// and fight. ServUO's world is global mutable static state (Mobile's ctor
// calls World.AddMobile), so this registers one throwaway Map - never a
// stock Felucca/Trammel/etc index, never touching map*.mul - and leaves
// everything else (regions, statics, real terrain) unset. Map.Tiles falls
// back to an all-default, fully passable land block when no .mul file is
// found (TileMatrix.GetLandBlock), which is why acquisition/stance/combat
// assignment work here with zero client data: see the PR description for
// what this harness does NOT cover (real pathing/line-of-sight).
//
// Issue #62 note (load-bearing, do not add Item construction here): unlike
// Map.Tiles, Item's own ctor (Item(int itemID) -> UpdateLight() -> ItemData)
// unconditionally requires TileData.ItemTable, and TileData's static
// constructor *throws* (deliberately - "the server will terminate") when
// tiledata.mul isn't found, with no graceful-degradation fallback like
// TileMatrix has. That means no Server.Items.* type can be constructed in
// this fixture - not a Backpack, not a custom Item subclass, nothing.
// PersonaCompanion/BaseCreature (Mobiles, not Items) are unaffected, which
// is why CreateCompanion/CreateAttacker below have always worked. Plan
// steps that construct real Items (mine/sell_to_vendor/buy_from_vendor) are
// real, correct, and reused-system-backed in production (real tiledata.mul
// always present there) but are not exercisable end-to-end in this
// harness - the same documented boundary as real pathing/line-of-sight
// above, just for item art data instead of map data.
public sealed class CompanionWorldFixture
{
    // 0-5 and 0x7F are reserved by MapDefinitions.Configure() for the real
    // shards' maps; this index is never registered by product code.
    private const int TestMapIndex = 100;

    public Map TestMap { get; }

    public CompanionWorldFixture()
    {
        // World's Serial/Mobile/Item dictionaries are lazily populated by
        // World.Load(), not static field initializers - with no Saves/
        // directory present, this takes the "fresh shard" path (empty
        // dictionaries), the same one a brand-new server boots from.
        World.Load();

        // Core.FindDataFile (used by AcquireFocusMob's LineOfSight check, via
        // Map.Tiles/TileMatrix) throws outright if Core.DataDirectories is
        // empty, rather than just failing an individual File.Exists check.
        // Pointing it at any real directory with no map*.mul in it hits that
        // per-file fallback instead: TileMatrix.GetLandBlock returns an
        // all-default, fully passable block, and LOS/pathing degrade to
        // "always visible / always flat" rather than throwing.
        if (Core.DataDirectories.Count == 0)
        {
            Core.DataDirectories.Add(Path.GetTempPath());
        }

        // Mobile's ctor unconditionally reads Map.Internal.DefaultRegion
        // (Server/Mobile.cs) regardless of what map the mobile ends up on -
        // stock ServUO registers this at boot via MapDefinitions.Configure(),
        // which this harness never runs, so it's registered here too.
        if (Map.Maps[0x7F] == null)
        {
            var internalMap = new Map(0x7F, 0x7F, 0x7F, Map.SectorSize, Map.SectorSize, 1, "Internal", MapRules.Internal);
            Map.Maps[0x7F] = internalMap;
            Map.AllMaps.Add(internalMap);
        }

        if (Map.Maps[TestMapIndex] == null)
        {
            var map = new Map(
                mapID: TestMapIndex,
                mapIndex: TestMapIndex,
                fileIndex: TestMapIndex,
                width: Map.SectorSize * 8,
                height: Map.SectorSize * 8,
                season: 0,
                name: "Tier2Test",
                rules: MapRules.FeluccaRules);

            Map.Maps[TestMapIndex] = map;
            Map.AllMaps.Add(map);
        }

        TestMap = Map.Maps[TestMapIndex];
    }

    // A companion with a live character sheet: PersonaCompanion.ConfigureFrom
    // is the same path PersonaSync uses at real spawn time (issue #32/#36),
    // so skill-alias resolution and stat->Hits derivation (SetStr sets
    // Hits = HitsMax) are exercised exactly as production does, not
    // reimplemented for the test.
    public PersonaCompanion CreateCompanion(Point3D location, Dictionary<string, double> skills, int str = 100)
    {
        var companion = new PersonaCompanion();

        companion.ConfigureFrom(new PersonaConfig
        {
            PersonaId = "tier2-test",
            DisplayName = "Test Companion",
            Name = "Test Companion",
            Body = 0x190,
            Hue = 0,
            Skills = skills,
            Stats = new Dictionary<string, int> { ["str"] = str, ["dex"] = str, ["int"] = str },
        });

        companion.MoveToWorld(location, TestMap);

        return companion;
    }

    // A bare hostile mobile standing in for "a player/monster that attacks
    // the companion" - just enough Mobile to call DoHarmful, the same
    // engine entry point real weapon/spell code calls (Mobile.cs OnSwing
    // et al.), rather than reimplementing damage/hit-chance RNG.
    public BaseCreature CreateAttacker(Point3D location, int str = 500)
    {
        var attacker = new BaseCreature(AIType.AI_Melee, FightMode.Closest, 10, 1, 0.2, 0.4);

        attacker.SetStr(str);
        attacker.MoveToWorld(location, TestMap);

        return attacker;
    }
}
