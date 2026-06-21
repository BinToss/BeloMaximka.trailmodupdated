using System;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TrailModUpdated;

public class BlockTrail : Block
{
    private const string SOIL_CODE = "soil";
    private const string SOIL_GRASS_NONE_CODE = "none";
    private const string SOIL_GRASS_SPARSE_CODE = "sparse";
    private const string PRETRAIL_START_CODE = "soil"; // We are intentionally not including the trailmod: domain here becauase it is appended to the string at runtime.
    private const string PRETRAIL_END_CODE = "pretrail";

    private readonly string[] trailVariants = { "pretrail", "new", "established", "veryestablished", "old" };

    // todo: Devolve Trails Over Time
    double lastTrailTouchDay = 0;

    public override void OnServerGameTick(IWorldAccessor world, BlockPos pos, object? extra = null)
    {
        base.OnServerGameTick(world, pos, extra);

        string endVariant = Code.EndVariant();

        if (world.Calendar.ElapsedDays - lastTrailTouchDay >= GetTrailDevolveDays(endVariant))
        {
            TrailChunkManager trailChunkManager = TrailChunkManager.GetTrailChunkManager();
            trailChunkManager.worldAccessor ??= world;

            //Devolve the block to the previous level.
            double daysSinceTouched = trailChunkManager.worldAccessor.Calendar.ElapsedDays - lastTrailTouchDay;

            double devolveDays = GetTrailDevolveDays(endVariant);
            int devolveLevels = (int)(daysSinceTouched / devolveDays); //This should round down, not up.

            if (devolveLevels == 0)
                return;

            int startingLevel = GetTrailWearIndexFromWearCode(endVariant);
            int finalLevel = startingLevel - devolveLevels;
            string devolveBlockCode = "";

            //If we are a new trail devolving to pretrail.
            if (finalLevel > 0)
            {
                string baseCode = CodeWithoutParts(1);
                string newWearVariantCode = trailVariants[finalLevel];
                devolveBlockCode = Code.Domain + ":" + baseCode + "-" + newWearVariantCode;
            }
            else if (finalLevel == 0)
            {
                devolveBlockCode =
                    Code.Domain + ":" + PRETRAIL_START_CODE + "-" + GetFertilityVariantCode() + "-" + PRETRAIL_END_CODE;
            }
            else if (finalLevel < 0)
            {
                devolveBlockCode = SOIL_CODE + "-" + GetFertilityVariantCode() + "-" + SOIL_GRASS_NONE_CODE;
            }

            AssetLocation devolveBlockAsset = new(devolveBlockCode);
            Block? devolveBlock = world.GetBlock(devolveBlockAsset);

            if (devolveBlock is null)
            {
                api.Logger.Error("[trailmodupdated] Unable to get devolve block by code {0}", devolveBlockAsset);
                return;
            }

            SetLastTrailTouchDay(world.Calendar.ElapsedDays);
            world.BlockAccessor.SetBlock(devolveBlock.Id, pos);

            if (trailChunkManager.BlockPosHasTrailData(pos))
                trailChunkManager.ClearBlockTouchCount(pos);
        }
    }

    public override bool ShouldReceiveServerGameTicks(IWorldAccessor world, BlockPos pos, Random offThreadRandom, out object? extra)
    {
        //Look at making this more efficient.
        extra = null;
        return true;
    }

    public void UpdateLastTrailTouchDayFromLoadedData(double lastTouchDay, BlockPos pos)
    {
        //Make sure this only ever runs on the server.

        lastTrailTouchDay = lastTouchDay;

        TrailChunkManager trailChunkManager = TrailChunkManager.GetTrailChunkManager();

        Debug.Assert(trailChunkManager.worldAccessor != null);

        double daysSinceTouched = trailChunkManager.worldAccessor.Calendar.ElapsedDays - lastTrailTouchDay;

        string endVariant = Code.EndVariant();
        double devolveDays = GetTrailDevolveDays(endVariant);
        int devolveLevels = (int)(daysSinceTouched / devolveDays); //This should round down, not up.

        if (devolveLevels == 0)
            return;

        int startingLevel = GetTrailWearIndexFromWearCode(endVariant);
        int finalLevel = startingLevel - devolveLevels;

        if (finalLevel > 0)
        {
            // Devolve the block to the previous level.
            string baseCode = CodeWithoutParts(1);
            string newWearVariantCode = trailVariants[finalLevel];
            string devolveBlockCode = baseCode + "-" + newWearVariantCode;

            AssetLocation devolveBlockAsset = GetDevolveBlockAsset(devolveBlockCode);
            Block? devolveBlock = trailChunkManager.worldAccessor.GetBlock(devolveBlockAsset);

            if (devolveBlock is null)
            {
                api.Logger.Error("[trailmodupdated] Unable to get devolve block by code {0}", devolveBlockAsset);
                return;
            }

            SetLastTrailTouchDay(trailChunkManager.worldAccessor.Calendar.ElapsedDays);
            trailChunkManager.worldAccessor.BlockAccessor.SetBlock(devolveBlock.Id, pos);

            if (trailChunkManager.BlockPosHasTrailData(pos))
                trailChunkManager.ClearBlockTouchCount(pos);
        }
        // if we are new trail devolving to pretrail.
        else if (finalLevel == 0)
        {
            string devolveBlockCode = PRETRAIL_START_CODE + "-" + GetFertilityVariantCode() + "-" + PRETRAIL_END_CODE;

            AssetLocation devolveBlockAsset = GetDevolveBlockAsset(devolveBlockCode);
            Block? devolveBlock = trailChunkManager.worldAccessor.GetBlock(devolveBlockAsset);

            if (devolveBlock is null)
            {
                api.Logger.Error("[trailmodupdated] Unable to get devolve block by code {0}", devolveBlockAsset);
                return;
            }

            SetLastTrailTouchDay(trailChunkManager.worldAccessor.Calendar.ElapsedDays);
            trailChunkManager.worldAccessor.BlockAccessor.SetBlock(devolveBlock.Id, pos);

            if (trailChunkManager.BlockPosHasTrailData(pos))
                trailChunkManager.ClearBlockTouchCount(pos);
        }
        // if we are native soil
        else if (finalLevel < 0)
        {
            string devolveToSoilCode = SOIL_CODE + "-" + GetFertilityVariantCode() + "-" + SOIL_GRASS_SPARSE_CODE;

            AssetLocation devolveSoilBlockAsset = new(devolveToSoilCode);

            Block? devolveSoilBlock = trailChunkManager.worldAccessor.GetBlock(devolveSoilBlockAsset);

            if (devolveSoilBlock is null)
            {
                api.Logger.Error("[trailmodupdated] Unable to get devolve block by code {0}", devolveSoilBlockAsset);
                return;
            }

            SetLastTrailTouchDay(trailChunkManager.worldAccessor.Calendar.ElapsedDays);
            trailChunkManager.worldAccessor.BlockAccessor.SetBlock(devolveSoilBlock.Id, pos);

            if (trailChunkManager.BlockPosHasTrailData(pos))
                trailChunkManager.ClearBlockTouchCount(pos);
        }
    }

    protected virtual AssetLocation GetDevolveBlockAsset(string code) => new(Code.Domain, code);

    protected virtual string GetFertilityVariantCode() => Code.SecondCodePart();

    private static double GetTrailDevolveDays(string wearVariant)
    {
        if (wearVariant == "pretrail")
            return TrailModGlobals.TrampledSoilDevolveDays;

        return TrailModGlobals.TrailDevolveDays;
    }

    private static int GetTrailWearIndexFromWearCode(string wearCode)
    {
        switch (wearCode)
        {
            case "pretrail":
                return 0;
            case "new":
                return 1;
            case "established":
                return 2;
            case "veryestablished":
                return 3;
            case "old":
                return 4;
        }

        Debug.Assert(false, "Wear code is invalid.");

        return -1;
    }

    private void SetLastTrailTouchDay(double lastTouchDay)
    {
        lastTrailTouchDay = lastTouchDay;
    }

    public void TrailBlockTouched(IWorldAccessor world)
    {
        SetLastTrailTouchDay(world.Calendar.ElapsedDays);
    }

    public double GetLastTouchDay()
    {
        return lastTrailTouchDay;
    }
}
