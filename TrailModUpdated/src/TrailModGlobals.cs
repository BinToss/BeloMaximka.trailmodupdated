using System;

namespace TrailModUpdated;

public static class TrailModGlobals
{
    // todo: BREAKING CHANGE: remove obsolete class member
    [Obsolete($"Use {nameof(CreativeTrampling)} instead.")]
    public static bool CeativeTrampling { get => CreativeTrampling; set => CreativeTrampling = value; }
    public static bool CreativeTrampling { get; set; } = false;
    public static bool FoliageTrampleSounds { get; set; } = true;
    public static bool OnlyPlayersCreateTrails { get; set; } = false;
    public static bool FlowerTrampling { get; set; } = true;
    public static bool FernTrampling { get; set; } = true;

    public static bool OnlyTrampleGrassOnTrailCreation { get; set; } = false;
    public static bool OnlyTrampleFlowersOnTrailCreation { get; set; } = true;
    public static bool OnlyTrampleFernsOnTrailCreation { get; set; } = true;

    public static float TrampledSoilDevolveDays { get; set; } = 7.0f;
    public static float TrailDevolveDays { get; set; } = 60.0f;

    public static int NormalToSparseGrassTouchCount { get; set; } = 1;
    public static int SparseToVerySparseGrassTouchCount { get; set; } = 1;
    public static int VerySparseToSoilTouchCount { get; set; } = 1;
    public static int SoilToTrampledSoilTouchCount { get; set; } = 1;
    public static int TrampledSoilToNewTrailTouchCount { get; set; } = 3;
    public static int NewToEstablishedTrailTouchCount { get; set; } = 25;
    public static int EstablishedToDirtRoadTouchCount { get; set; } = 50;
    public static int DirtRoadToHighwayTouchCount { get; set; } = 75;

    public static int ForestFloorToSoilTouchCount { get; set; } = 2;

    public static int CobLoseGrassTouchCount { get; set; } = 1;
    public static int PeatLoseGrassTouchCount { get; set; } = 1;
    public static int ClayLoseGrassTouchCount { get; set; } = 1;

    public static float MinEntityHullSizeToTrampleX { get; set; } = 0;
    public static float MinEntityHullSizeToTrampleY { get; set; } = 0;
}
