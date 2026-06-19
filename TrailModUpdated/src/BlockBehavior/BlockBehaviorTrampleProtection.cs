using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace TrailModUpdated;

public class BlockBehaviorTrampleProtection(Block block) : BlockBehavior(block)
{
    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier, ref EnumHandling handling)
    {
        //Remove Trample Protection From Block.
        ModSystemTrampleProtection modTramplePro;
        modTramplePro = world.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();

        if (modTramplePro.IsTrampleProtected(pos))
        {
            string errorCode = "";
            modTramplePro.TryRemoveTrampleProtection(pos, byPlayer, ref errorCode);
        }

        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier, ref handling);
    }

    public override void OnBlockExploded(
        IWorldAccessor world,
        BlockPos pos,
        BlockPos explosionCenter,
        EnumBlastType blastType,
        ref EnumHandling handling
    )
    {
        //Remove Trample Protection From Block.
        ModSystemTrampleProtection modTramplePro;
        modTramplePro = world.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();

        if (modTramplePro.IsTrampleProtected(pos))
            modTramplePro.ClearTrampleProtection(pos);

        base.OnBlockExploded(world, pos, explosionCenter, blastType, ref handling);
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ref EnumHandling handling)
    {
        if (world.Side == EnumAppSide.Server)
        {
            //Remove Trample Protection From Block.
            ModSystemTrampleProtection modTramplePro;
            modTramplePro = world.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();

            if (modTramplePro.IsTrampleProtected(blockPos))
                modTramplePro.ClearTrampleProtection(blockPos);
        }
    }

    public override string? GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        //Verify if block at position has trample protection.
        ModSystemTrampleProtection modTramplePro;
        modTramplePro = world.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();

        if (modTramplePro.IsTrampleProtected(pos))
        {
            TrampleProtection? trampleProtection = modTramplePro.GetTrampleProtection(pos);

            StringBuilder sb = new();
            sb.AppendLine(Lang.Get("Has been trample protected by {0}.", trampleProtection?.LastPlayername ?? "<null reference>"));

            return sb.ToString();
        }

        return null;
    }
}
