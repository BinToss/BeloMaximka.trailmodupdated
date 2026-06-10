using HarmonyLib;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TrailModUpdated;

//////////////////////////////////////////////////////////////////////////////////////
///PATCHING TO ADD A UNIVERAL SET LOCATION FOR LAST ENTITY TO ATTACK ON ENTITY AGENT//
//////////////////////////////////////////////////////////////////////////////////////

[HarmonyPatch(typeof(Block))]
public class OverrideOnEntityCollide
{
    private struct DeferredTransform
    {
        public BlockPos pos;
        public int blockId;
        public long entityId;
        public long enqueuedTime;
    }

    private struct DeferredSnowIceTransform
    {
        public BlockPos pos;
        public int blockId;
        public EnumBlockMaterial material;
        public float snowLevel;
    }

    private static ConcurrentQueue<DeferredTransform> deferredTransforms = new();
    private static ConcurrentQueue<DeferredSnowIceTransform> deferredSnowIceTransforms = new();
    private const int DEFERRED_BATCH_SIZE = 20;
    private const int SNOW_ICE_BATCH_SIZE = 50;
    private const long DEFERRED_PROCESS_INTERVAL_MS = 500;
    private static long lastDeferredProcessTime = 0;

    [HarmonyPrepare]
    static bool Prepare(MethodBase original, Harmony harmony)
    {
        return true;
    }

    [HarmonyPatch(nameof(Block.OnEntityCollide))]
    [HarmonyPostfix]
    static void OnEntityCollideOverride(
        Block __instance,
        IWorldAccessor world,
        Entity entity,
        BlockPos pos,
        BlockFacing facing,
        Vec3d collideSpeed,
        bool isImpact
    )
    {
        if (world.Side.IsClient())
            return;

        if (entity == null)
            return;

        //Skip any block with trample protection
        ModSystemTrampleProtection modTramplePro = entity.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();
        if (modTramplePro.IsTrampleProtected(pos))
            return;

        if (!entity.Alive)
            return;

        if (entity is not EntityAgent)
            return;

        // Check if a player is nearby (within 10 blocks for immediate processing)
        bool playerNearby = false;
        foreach (var p in world.AllOnlinePlayers)
        {
            var pe = p.Entity;
            if (pe == null)
                continue;

            double distSq = pe.Pos.SquareDistanceTo(entity.Pos);
            if (distSq <= 100) // 10x10
            {
                playerNearby = true;
                break;
            }
        }

        bool anyNearby = playerNearby;
        if (!anyNearby)
        {
            foreach (var p in world.AllOnlinePlayers)
            {
                var pe = p.Entity;
                if (pe == null)
                    continue;

                if (pe.Pos.SquareDistanceTo(entity.Pos) <= 22500) // 150x150
                {
                    anyNearby = true;
                    break;
                }
            }
        }

        if (!anyNearby)
            return;

        if (entity is EntityPlayer)
        {
            EntityPlayer entityPlayer = (EntityPlayer)entity;
            if (entityPlayer.Player.WorldData.CurrentGameMode != EnumGameMode.Survival)
                if (!TrailModGlobals.CeativeTrampling || entityPlayer.Player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                    return;
        }

        if (world.Side == EnumAppSide.Client)
            return;

        TrailChunkManager trailChunkManager = TrailChunkManager.GetTrailChunkManager();
        bool shouldTrackTrailData = trailChunkManager.ShouldTrackBlockTrailData(__instance);

        if (shouldTrackTrailData)
        {
            //We only touch blocks we collide with the top of.
            if (facing == BlockFacing.UP && pos.Y < entity.Pos.Y)
            {
                // Always track trail data within 150 blocks
                trailChunkManager.AddOrUpdateBlockPosTrailData(world, __instance, pos, entity);
            }

            //Check if the center of the block overlaps the entity bounding box.
            if (!trailChunkManager.BlockCenterHorizontalInEntityBoundingBox(entity, pos))
                return;

            // Queue snow/ice for deferred processing instead of handling inline
            if (__instance.BlockMaterial == EnumBlockMaterial.Snow)
            {
                deferredSnowIceTransforms.Enqueue(
                    new DeferredSnowIceTransform
                    {
                        pos = pos.Copy(),
                        blockId = __instance.Id,
                        material = EnumBlockMaterial.Snow,
                        snowLevel = __instance.snowLevel,
                    }
                );
            }
            else if (__instance.BlockMaterial == EnumBlockMaterial.Ice)
            {
                deferredSnowIceTransforms.Enqueue(
                    new DeferredSnowIceTransform
                    {
                        pos = pos.Copy(),
                        blockId = __instance.Id,
                        material = EnumBlockMaterial.Ice,
                        snowLevel = 0,
                    }
                );
            }
        }

        // Process deferred transforms periodically — only one thread wins the CAS and processes.
        long currentTime = world.ElapsedMilliseconds;
        long prevTime = Interlocked.Read(ref lastDeferredProcessTime);
        if (currentTime - prevTime > DEFERRED_PROCESS_INTERVAL_MS)
        {
            if (Interlocked.CompareExchange(ref lastDeferredProcessTime, currentTime, prevTime) == prevTime)
            {
                ProcessDeferredSnowIceTransforms(world);
                ProcessDeferredTransforms(world);
            }
        }
    }

    private static void ProcessDeferredSnowIceTransforms(IWorldAccessor world)
    {
        int processed = 0;

        while (processed < SNOW_ICE_BATCH_SIZE && deferredSnowIceTransforms.TryDequeue(out DeferredSnowIceTransform transform))
        {
            Block block = world.BlockAccessor.GetBlock(transform.pos);

            if (block.Id != transform.blockId)
                continue;

            switch (transform.material)
            {
                case EnumBlockMaterial.Snow:
                    if (transform.snowLevel > 0)
                    {
                        if (block is BlockSnowLayer snowLayer)
                        {
                            if (transform.snowLevel == 1)
                            {
                                Block baseSnowBlock = world.GetBlock(snowLayer.CodeWithVariant("height", "1"));
                                world.BlockAccessor.SetBlock(baseSnowBlock.Id, transform.pos);
                            }
                            else
                            {
                                Block newSnowBlock = world.GetBlock(snowLayer.CodeWithVariant("height", "" + (transform.snowLevel - 1)));
                                world.BlockAccessor.SetBlock(newSnowBlock.Id, transform.pos);
                            }
                        }
                        else if (block.Variant.ContainsKey("tallgrass"))
                        {
                            Block baseTallGrassBlock = world.GetBlock(block.CodeWithVariant("cover", "snow"));
                            world.BlockAccessor.SetBlock(baseTallGrassBlock.Id, transform.pos);
                        }
                    }
                    break;

                case EnumBlockMaterial.Ice:
                    if (block is BlockLakeIce && world.Rand.NextDouble() < 0.001)
                    {
                        BlockFacing[] horizontals = BlockFacing.HORIZONTALS;

                        foreach (BlockFacing blockFacing in horizontals)
                        {
                            BlockPos possibleIcePos = transform.pos.AddCopy(blockFacing);
                            Block possibleIceBlock = world.BlockAccessor.GetBlock(possibleIcePos);

                            if (possibleIceBlock == null)
                                continue;

                            if (possibleIceBlock is BlockLakeIce)
                            {
                                world.BlockAccessor.BreakBlock(possibleIcePos, null);
                            }
                        }

                        world.BlockAccessor.BreakBlock(transform.pos, null);
                    }
                    break;
            }
            processed++;
        }
    }

    private static void ProcessDeferredTransforms(IWorldAccessor world)
    {
        int processed = 0;
        TrailChunkManager trailChunkManager = TrailChunkManager.GetTrailChunkManager();

        while (processed < DEFERRED_BATCH_SIZE && deferredTransforms.TryDequeue(out DeferredTransform transform))
        {

            // Verify block still exists and hasn't changed
            Block currentBlock = world.BlockAccessor.GetBlock(transform.pos);
            if (currentBlock.Id != transform.blockId)
                continue;

            // Try to find the entity - if it's gone, skip
            Entity touchEntity = world.GetEntityById(transform.entityId);
            if (touchEntity == null || !touchEntity.Alive)
                continue;

            // Process the transformation
            trailChunkManager.AddOrUpdateBlockPosTrailData(world, currentBlock, transform.pos, touchEntity);
            processed++;
        }
    }
}
