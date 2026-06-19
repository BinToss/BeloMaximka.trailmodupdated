using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace TrailModUpdated;

public class ItemTrowel : Item
{
    const int TROWEL_HIGHLIGHT_SLOT_ID = 9001;
    const string AttrSelStart = "trailmodTrowelSelStart";
    const string AttrSelEnd = "trailmodTrowelSelEnd";
    const string CFG_SHOW_OVERLAY = "trailmodShowOverlay";

    const int OVERLAY_SCAN_RADIUS = 10;
    const int OVERLAY_Y_HALF = 6;
    const int OVERLAY_TICK_THROTTLE = 3;
    const int OVERLAY_MOVE_THRESHOLD = 3;

    const int MAX_VOLUME = 32 * 32 * 32;

    WorldInteraction[] interactions = [];
    SkillItem[] modeItems = [];

    readonly List<BlockPos> blockHighlightPositions = [];
    readonly List<int> blockHighlightColors = [];
    int overlayUpdateCounter = 0;

    private BlockPos? lastOverlayCenter;
    private BlockPos? lastOverlayStart;
    private BlockPos? lastOverlayEnd;
    private BlockPos? lastOverlayHover;
    private int lastOverlayMode = -1;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        interactions = ObjectCacheUtil.GetOrCreate(
            api,
            "trowelInteractions",
            () =>
            {
                return new WorldInteraction[]
                {
                    new() { ActionLangCode = "trailmodupdated:heldhelp-trampleprotectblock", MouseButton = EnumMouseButton.Right },
                    new()
                    {
                        ActionLangCode = "trailmodtrailmodupdated:heldhelp-removetrampleprotection",
                        MouseButton = EnumMouseButton.Left,
                    },
                };
            }
        );
    }

    public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        if (modeItems.Length != 0 || forPlayer.Entity.Api is not ICoreClientAPI capi)
        {
            return modeItems;
        }

        modeItems =
        [
            new SkillItem() { Code = new AssetLocation("single"), Name = Lang.Get("Single block") },
            new SkillItem() { Code = new AssetLocation("select"), Name = Lang.Get("Protect area") },
            new SkillItem() { Code = new AssetLocation("selectremove"), Name = Lang.Get("Unprotect area") },
        ];

        // Load textures
        modeItems[0].Texture = capi.Gui.LoadSvgWithPadding(
            new AssetLocation("trailmodupdated:textures/icons/single.svg"),
            48,
            48,
            5,
            ColorUtil.WhiteArgb
        );
        modeItems[1].Texture = capi.Gui.LoadSvgWithPadding(
            new AssetLocation("trailmodupdated:textures/icons/select.svg"),
            48,
            48,
            5,
            ColorUtil.WhiteArgb
        );
        modeItems[2].Texture = capi.Gui.LoadSvgWithPadding(
            new AssetLocation("trailmodupdated:textures/icons/selectremove.svg"),
            48,
            48,
            5,
            ColorUtil.WhiteArgb
        );

        return modeItems;
    }

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
    {
        return slot?.Itemstack?.Attributes?.GetInt("toolMode", 0) ?? 0;
    }

    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        slot?.Itemstack?.Attributes?.SetInt("toolMode", toolMode);
        slot?.Itemstack?.Attributes?.SetInt("trailmodPrevToolMode", toolMode);
        base.SetToolMode(slot, byPlayer, blockSelection, toolMode);
    }

    public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
    {
        base.OnHeldIdle(slot, byEntity);

        int current = slot?.Itemstack?.Attributes?.GetInt("toolMode", 0) ?? 0;
        int prev = slot?.Itemstack?.Attributes?.GetInt("trailmodPrevToolMode", -1) ?? -1;
        if (current != prev)
        {
            slot?.Itemstack?.Attributes.SetInt("trailmodPrevToolMode", current);
            lastOverlayCenter = null;
        }

        overlayUpdateCounter++;
        if (overlayUpdateCounter >= OVERLAY_TICK_THROTTLE)
        {
            overlayUpdateCounter = 0;
            UpdateSelectionOverlayClient(byEntity);
        }
    }

    public override void OnHeldAttackStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandHandling handling
    )
    {
        // todo: Attributes is never null
        int mode = slot?.Itemstack?.Attributes?.GetInt("toolMode", 0) ?? 0;
        if (mode == 0)
        {
            HandleSingleRemove(byEntity, blockSel, ref handling);
            return;
        }

        if (blockSel == null)
            return;
        SetSelectionPos(byEntity, AttrSelStart, blockSel.Position);
        UpdateSelectionOverlayClient(byEntity);
        handling = EnumHandHandling.PreventDefaultAction;
    }

    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handling
    )
    {
        if (handling == EnumHandHandling.PreventDefault)
            return;

        int mode = slot?.Itemstack?.Attributes?.GetInt("toolMode", 0) ?? 0;
        if (mode == 0)
        {
            HandleSingleAdd(byEntity, blockSel, ref handling);
            return;
        }

        if (blockSel == null)
            return;
        SetSelectionPos(byEntity, AttrSelEnd, blockSel.Position);
        UpdateSelectionOverlayClient(byEntity);

        if (byEntity.World.Side == EnumAppSide.Server)
        {
            var start = GetSelectionPos(byEntity, AttrSelStart);
            var end = GetSelectionPos(byEntity, AttrSelEnd);
            if (start != null && end != null)
            {
                if (mode == 1)
                    TryProtectArea(byEntity, start, end);
                else if (mode == 2)
                    TryUnprotectArea(byEntity, start, end);
            }
        }

        handling = EnumHandHandling.PreventDefaultAction;
    }

    private static void HandleSingleAdd(EntityAgent byEntity, BlockSelection blockSel, ref EnumHandHandling handling)
    {
        if (byEntity.World.Side == EnumAppSide.Client)
        {
            handling = EnumHandHandling.PreventDefaultAction;
            return;
        }
        if (blockSel == null)
            return;

        var modTramplePro = byEntity.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();
        var player = (byEntity as EntityPlayer)?.Player as IServerPlayer;
        if (player == null)
            return;

        var ba = byEntity.Api.World.BlockAccessor;
        var blk = ba.GetBlock(blockSel.Position);

        if (!blk.HasBehavior<BlockBehaviorTrampleProtection>())
        {
            player.SendIngameError("nottrampleprotectable", "This block can not be trample protected!");
            return;
        }
        if (modTramplePro.IsTrampleProtected(blockSel.Position))
        {
            player.SendIngameError("alreadytrampleprotected", "Cannot trample protect block, it's already protected!");
            return;
        }

        modTramplePro.TryAddTrampleProtection(blockSel.Position, player);
        var pos = blockSel.Position;
        byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/reinforce"), pos.X, pos.Y, pos.Z, null);
        handling = EnumHandHandling.PreventDefaultAction;
    }

    private static void HandleSingleRemove(EntityAgent byEntity, BlockSelection blockSel, ref EnumHandHandling handling)
    {
        if (byEntity.World.Side == EnumAppSide.Client)
        {
            handling = EnumHandHandling.PreventDefaultAction;
            return;
        }
        if (blockSel == null)
            return;

        var modTramplePro = byEntity.Api.ModLoader.GetModSystem<ModSystemTrampleProtection>();
        var player = (byEntity as EntityPlayer)?.Player as IServerPlayer;
        if (player == null)
            return;

        if (modTramplePro.IsTrampleProtected(blockSel.Position))
        {
            string error = "";
            modTramplePro.TryRemoveTrampleProtection(blockSel.Position, player, ref error);
        }

        var pos = blockSel.Position;
        byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/reinforce"), pos.X, pos.Y, pos.Z, null);
        handling = EnumHandHandling.PreventDefaultAction;
    }

    void TryProtectArea(EntityAgent byEntity, BlockPos a, BlockPos b)
    {
        var api = byEntity.Api;
        var modTramplePro = api.ModLoader.GetModSystem<ModSystemTrampleProtection>();
        if ((byEntity as EntityPlayer)?.Player is not IServerPlayer splayer)
            return;

        int minX = GameMath.Min(a.X, b.X);
        int minY = GameMath.Min(a.Y, b.Y);
        int minZ = GameMath.Min(a.Z, b.Z);
        int maxX = GameMath.Max(a.X, b.X);
        int maxY = GameMath.Max(a.Y, b.Y);
        int maxZ = GameMath.Max(a.Z, b.Z);

        long volume = (long)(maxX - minX + 1) * (maxY - minY + 1) * (maxZ - minZ + 1);
        if (volume > MAX_VOLUME)
        {
            splayer.SendIngameError("selectiontoolarge", Lang.Get("Selection too large ({0} blocks). Limit is {1}.", volume, MAX_VOLUME));
            return;
        }

        int added = 0;
        var ba = api.World.BlockAccessor;

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    var pos = new BlockPos(x, y, z);
                    var block = ba.GetBlock(pos);

                    if (!block.HasBehavior<BlockBehaviorTrampleProtection>())
                        continue;
                    if (modTramplePro.IsTrampleProtected(pos))
                        continue;

                    if (modTramplePro.TryAddTrampleProtection(pos, splayer))
                        added++;
                }

        double cx = (minX + maxX) / 2.0;
        double cy = (minY + maxY) / 2.0;
        double cz = (minZ + maxZ) / 2.0;
        byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/reinforce"), cx, cy, cz, null);

        splayer.SendMessage(
            GlobalConstants.InfoLogChatGroup,
            Lang.Get("Protected {0} blocks from trampling.", added),
            EnumChatType.Notification
        );

        ClearSelection(splayer, true);
    }

    private static void TryUnprotectArea(EntityAgent byEntity, BlockPos a, BlockPos b)
    {
        var api = byEntity.Api;
        var modTramplePro = api.ModLoader.GetModSystem<ModSystemTrampleProtection>();
        if ((byEntity as EntityPlayer)?.Player is not IServerPlayer splayer)
            return;

        int minX = GameMath.Min(a.X, b.X);
        int minY = GameMath.Min(a.Y, b.Y);
        int minZ = GameMath.Min(a.Z, b.Z);
        int maxX = GameMath.Max(a.X, b.X);
        int maxY = GameMath.Max(a.Y, b.Y);
        int maxZ = GameMath.Max(a.Z, b.Z);

        long volume = (long)(maxX - minX + 1) * (maxY - minY + 1) * (maxZ - minZ + 1);
        if (volume > MAX_VOLUME)
        {
            splayer.SendIngameError("selectiontoolarge", Lang.Get("Selection too large ({0} blocks). Limit is {1}.", volume, MAX_VOLUME));
            return;
        }

        int removed = 0;
        var ba = api.World.BlockAccessor;

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    var pos = new BlockPos(x, y, z);
                    var block = ba.GetBlock(pos);
                    if (!block.HasBehavior<BlockBehaviorTrampleProtection>())
                        continue;
                    if (!modTramplePro.IsTrampleProtected(pos))
                        continue;

                    string error = "";
                    if (modTramplePro.TryRemoveTrampleProtection(pos, splayer, ref error))
                        removed++;
                }

        double cx = (minX + maxX) / 2.0;
        double cy = (minY + maxY) / 2.0;
        double cz = (minZ + maxZ) / 2.0;
        byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/reinforce"), cx, cy, cz, null);

        splayer.SendMessage(
            GlobalConstants.InfoLogChatGroup,
            Lang.Get("Removed trample protection from {0} blocks.", removed),
            EnumChatType.Notification
        );

        ClearSelection(splayer, true);
    }

    // Helpers
    private static BlockPos? GetSelectionPos(EntityAgent byEntity, string key)
    {
        return byEntity?.WatchedAttributes?.GetBlockPos(key);
    }

    private static void SetSelectionPos(EntityAgent byEntity, string key, BlockPos pos)
    {
        byEntity?.WatchedAttributes?.SetBlockPos(key, pos);
    }

    private static void ClearSelection(IPlayer player, bool alsoClearOverlay)
    {
        if (player.Entity.Api is not ICoreClientAPI capi)
        {
            return;
        }

        player.Entity.WatchedAttributes.RemoveAttribute(AttrSelStart);
        player.Entity.WatchedAttributes.RemoveAttribute(AttrSelEnd);

        if (alsoClearOverlay && capi != null)
        {
            capi.World.HighlightBlocks(capi.World.Player, TROWEL_HIGHLIGHT_SLOT_ID, []);
        }
    }

    private void UpdateSelectionOverlayClient(EntityAgent byEntity)
    {
        if (byEntity.Api is not ICoreClientAPI capi || capi.World == null)
            return;
        var player = capi.World.Player;
        if (player == null)
            return;

        bool showOverlay = capi.World.Config.GetBool(CFG_SHOW_OVERLAY, true);
        if (!showOverlay)
        {
            capi.World.HighlightBlocks(player, TROWEL_HIGHLIGHT_SLOT_ID, new List<BlockPos>());
            return;
        }

        var holdingThis = player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible == this;
        if (!holdingThis)
        {
            capi.World.HighlightBlocks(player, TROWEL_HIGHLIGHT_SLOT_ID, new List<BlockPos>());
            return;
        }

        var modTrample = capi.ModLoader.GetModSystem<ModSystemTrampleProtection>();
        if (modTrample == null)
        {
            capi.World.HighlightBlocks(player, TROWEL_HIGHLIGHT_SLOT_ID, new List<BlockPos>());
            return;
        }

        BlockPos center = player.Entity.Pos.AsBlockPos;
        BlockPos? hover = (player.CurrentBlockSelection?.Position) ?? null;
        BlockPos? selA = GetSelectionPos(player.Entity, AttrSelStart);
        BlockPos? selB = GetSelectionPos(player.Entity, AttrSelEnd);
        int mode = player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Attributes.GetInt("toolMode", 0) ?? 0;

        bool movedFar =
            lastOverlayCenter == null
            || Math.Abs(center.X - lastOverlayCenter.X) >= OVERLAY_MOVE_THRESHOLD
            || Math.Abs(center.Y - lastOverlayCenter.Y) >= OVERLAY_MOVE_THRESHOLD
            || Math.Abs(center.Z - lastOverlayCenter.Z) >= OVERLAY_MOVE_THRESHOLD;

        bool selectionChanged = !BlockPosEqual(selA, lastOverlayStart) || !BlockPosEqual(selB, lastOverlayEnd);
        bool hoverChanged = !BlockPosEqual(hover, lastOverlayHover);
        bool modeChanged = (mode != lastOverlayMode);

        if (!movedFar && !selectionChanged && !hoverChanged && !modeChanged)
        {
            return;
        }

        lastOverlayCenter = center.Copy();
        lastOverlayStart = selA?.Copy();
        lastOverlayEnd = selB?.Copy();
        lastOverlayHover = hover?.Copy();
        lastOverlayMode = mode;

        int r = OVERLAY_SCAN_RADIUS;
        int r2 = r * r;
        int minX = center.X - r,
            maxX = center.X + r;
        int minZ = center.Z - r,
            maxZ = center.Z + r;
        int minY = center.Y - OVERLAY_Y_HALF;
        int maxY = center.Y + OVERLAY_Y_HALF;

        blockHighlightPositions.Clear();
        blockHighlightColors.Clear();

        int col = ColorUtil.ColorFromRgba(30, 200, 255, 160);

        for (int x = minX; x <= maxX; x++)
        {
            int dx = x - center.X;
            int dx2 = dx * dx;
            for (int z = minZ; z <= maxZ; z++)
            {
                int dz = z - center.Z;
                int dz2 = dz * dz;
                if (dx2 + dz2 > r2)
                    continue;

                for (int y = minY; y <= maxY; y++)
                {
                    int dy = y - center.Y;
                    if (dx2 + dz2 + dy * dy > r2)
                        continue;

                    var pos = new BlockPos(x, y, z);
                    if (!modTrample.IsTrampleProtected(pos))
                        continue;

                    blockHighlightPositions.Add(pos);
                    blockHighlightColors.Add(col);
                }
            }
        }

        capi.World.HighlightBlocks(player, TROWEL_HIGHLIGHT_SLOT_ID, blockHighlightPositions, blockHighlightColors);
    }

    private static bool BlockPosEqual(BlockPos? a, BlockPos? b)
    {
        if (a == null && b == null)
            return true;
        if (a == null || b == null)
            return false;
        return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        return interactions.Append(base.GetHeldInteractionHelp(inSlot));
    }
}
