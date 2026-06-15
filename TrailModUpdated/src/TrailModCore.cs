using System;
using System.Reflection;
using HarmonyLib;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace TrailModUpdated;

[ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
public class TrailModConfig
{
    public bool CreativeTrampling { get; set; } = false;
    public bool DirtRoadsOnly { get; set; } = false;
    public bool FoliageTrampleSounds { get; set; } = true;
    public bool OnlyPlayersCreateTrails { get; set; } = false;
    public bool FlowerTrampling { get; set; } = true;
    public bool FernTrampling { get; set; } = true;

    public bool OnlyTrampleGrassOnTrailCreation { get; set; } = false;
    public bool OnlyTrampleFlowersOnTrailCreation { get; set; } = true;
    public bool OnlyTrampleFernsOnTrailCreation { get; set; } = true;

    public float TrampledSoilDevolveDays { get; set; } = 7.0f;
    public float TrailDevolveDays { get; set; } = 60.0f;

    public int NormalToSparseGrassTouchCount { get; set; } = 1;
    public int SparseToVerySparseGrassTouchCount { get; set; } = 1;
    public int VerySparseToSoilTouchCount { get; set; } = 1;
    public int SoilToTrampledSoilTouchCount { get; set; } = 1;
    public int TrampledSoilToNewTrailTouchCount { get; set; } = 3;
    public int NewToEstablishedTrailTouchCount { get; set; } = 25;
    public int EstablishedToDirtRoadTouchCount { get; set; } = 50;
    public int DirtRoadToHighwayTouchCount { get; set; } = 75;

    public int ForestFloorToSoilTouchCount { get; set; } = 2;

    public int CobLoseGrassTouchCount { get; set; } = 1;
    public int PeatLoseGrassTouchCount { get; set; } = 1;
    public int ClayLoseGrassTouchCount { get; set; } = 1;

    public float MinEntityHullSizeToTrampleX { get; set; } = 0;
    public float MinEntityHullSizeToTrampleY { get; set; } = 0;

    // NEW: Allow players to disable the client overlay while keeping tool modes functional.
    public bool ShowProtectionOverlay { get; set; } = true;
}

public class TrailModCore : ModSystem
{
    TrailModConfig config = new();

    private Harmony harmony;
    private TrailChunkManager trailChunkManager;

    public override double ExecuteOrder()
    {
        return 0.0;
    }

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);

#if DEBUG
#pragma warning disable S2696 // Instance members should not write to "static" fields
        RuntimeEnv.DebugOutOfRangeBlockAccess = true;
#pragma warning restore S2696
#endif

        if (api is ICoreServerAPI sapi)
        {
            ReadConfigFromJson(sapi);
            ApplyConfigPatchFlags(sapi);
            ApplyConfigGlobalConsts();
        }
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        harmony = new Harmony("com.grifthegnome.trailmod.trailpatches");
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        RegisterBlocksShared(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        trailChunkManager = TrailChunkManager.GetTrailChunkManager();
        trailChunkManager.InitData(api.World, api);

        api.Event.RegisterCallback(trailChunkManager.Clean, (int)TrailChunkManager.TRAIL_CLEANUP_INTERVAL);

        api.Event.ChunkDirty += trailChunkManager.OnChunkDirty;
        api.Event.ChunkColumnUnloaded += trailChunkManager.OnChunkColumnUnloaded;

        api.Event.SaveGameLoaded += trailChunkManager.OnSaveGameLoading;
        api.Event.GameWorldSave += trailChunkManager.OnSaveGameSaving;

        api.Event.ServerRunPhase(
            EnumServerRunPhase.Shutdown,
            () =>
            {
                trailChunkManager.ShutdownSaveState();
                //Clean up all manager stuff.
                //If we don't it persists between loads.
                trailChunkManager.ShutdownCleanup();
                trailChunkManager = null!;
            }
        );
    }

    private static void RegisterBlocksShared(ICoreAPI api)
    {
        api.RegisterBlockClass("BlockTrail", typeof(BlockTrail));
    }

    private void ReadConfigFromJson(ICoreServerAPI api)
    {
        string fileName = "TrailModConfig.json";
        try
        {
            TrailModConfig modConfig = api.LoadModConfig<TrailModConfig>(fileName);

            if (modConfig != null)
            {
                config = modConfig;
            }
        }
        catch (Exception e)
        {
            api.World.Logger.Error($"{fileName} is invalid, will generate a new one", e);
        }
        api.StoreModConfig(config, fileName);
    }

    private void ApplyConfigPatchFlags(ICoreAPI api)
    {
        //Enable/Disable Config Settngs (shared to clients via world config bag)
        api.World.Config.SetBool("dirtRoadsOnly", config.DirtRoadsOnly);

        // NEW: whether to render the protection overlay on clients
        api.World.Config.SetBool("trailmodShowOverlay", config.ShowProtectionOverlay);
    }

    private void ApplyConfigGlobalConsts()
    {
        //GENERAL SETTINGS
        TrailModGlobals.CeativeTrampling = config.CreativeTrampling;
        TrailModGlobals.FoliageTrampleSounds = config.FoliageTrampleSounds;
        TrailModGlobals.OnlyPlayersCreateTrails = config.OnlyPlayersCreateTrails;
        TrailModGlobals.FlowerTrampling = config.FlowerTrampling;
        TrailModGlobals.FernTrampling = config.FernTrampling;

        //FOLIAGE TRAMPLE SETTINGS
        TrailModGlobals.OnlyTrampleGrassOnTrailCreation = config.OnlyTrampleGrassOnTrailCreation;
        TrailModGlobals.OnlyTrampleFlowersOnTrailCreation = config.OnlyTrampleFlowersOnTrailCreation;
        TrailModGlobals.OnlyTrampleFernsOnTrailCreation = config.OnlyTrampleFernsOnTrailCreation;

        //TRAIL DEVOLVE TIMES
        TrailModGlobals.TrampledSoilDevolveDays = config.TrampledSoilDevolveDays;
        TrailModGlobals.TrailDevolveDays = config.TrailDevolveDays;

        //SOIL
        TrailModGlobals.NormalToSparseGrassTouchCount = config.NormalToSparseGrassTouchCount;
        TrailModGlobals.SparseToVerySparseGrassTouchCount = config.SparseToVerySparseGrassTouchCount;
        TrailModGlobals.VerySparseToSoilTouchCount = config.VerySparseToSoilTouchCount;
        TrailModGlobals.SoilToTrampledSoilTouchCount = config.SoilToTrampledSoilTouchCount;

        //TRAILS
        TrailModGlobals.TrampledSoilToNewTrailTouchCount = config.TrampledSoilToNewTrailTouchCount;
        TrailModGlobals.NewToEstablishedTrailTouchCount = config.NewToEstablishedTrailTouchCount;
        TrailModGlobals.EstablishedToDirtRoadTouchCount = config.EstablishedToDirtRoadTouchCount;
        TrailModGlobals.DirtRoadToHighwayTouchCount = config.DirtRoadToHighwayTouchCount;
        TrailModGlobals.ForestFloorToSoilTouchCount = config.ForestFloorToSoilTouchCount;

        //COB, PEAT, CLAY
        TrailModGlobals.CobLoseGrassTouchCount = config.CobLoseGrassTouchCount;
        TrailModGlobals.PeatLoseGrassTouchCount = config.PeatLoseGrassTouchCount;
        TrailModGlobals.ClayLoseGrassTouchCount = config.ClayLoseGrassTouchCount;

        //ENTITY MIN HULL SIZE TO TRAMPLE
        TrailModGlobals.MinEntityHullSizeToTrampleX = config.MinEntityHullSizeToTrampleX;
        TrailModGlobals.MinEntityHullSizeToTrampleY = config.MinEntityHullSizeToTrampleY;
    }

    public override void Dispose()
    {
        harmony.UnpatchAll(harmony.Id);
        base.Dispose();
    }
}
