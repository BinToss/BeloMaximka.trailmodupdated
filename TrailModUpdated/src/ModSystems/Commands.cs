using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TrailModUpdated.ModSystems;

public class Commands : ModSystem
{
    ICoreServerAPI? sapi;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;

        api.ChatCommands
            .Create("removetrails")
            .WithDescription("Replace TrailModUpdated trail/soil blocks with vanilla soil (matching fertility). Scans changed + loaded chunks and a 3x3 halo around each player.")
            .RequiresPrivilege("worldedit")
            .HandleWith(OnRestoreTrails);
    }

    private TextCommandResult OnRestoreTrails(TextCommandCallingArgs args)
    {
        if (sapi == null)
            return TextCommandResult.Error("ServerAPI unavailable; TrailModUpdated.ModSystem.Commands.sapi is null!");
        var ba = sapi.World.BlockAccessor;

        // ---------- Build target chunk list (UNION of all sources) ----------
        var targetColumns = new HashSet<Vec2i>();

        // All loaded columns (portable across builds)
        foreach (var kv in sapi.WorldManager?.AllLoadedChunks ?? [])
        {
            var chunk = kv.Value;
            if (chunk == null) continue;
            if (TryGetChunkColumnXZ(chunk, out var cx, out var cz))
                targetColumns.Add(new Vec2i(cx, cz));
        }

        // 3x3 halo around each online player to guarantee local coverage
        AddPlayerHalos(targetColumns, radius: 1); // 1 => 3x3; increase to 2 for 5x5

        if (targetColumns.Count == 0)
            return TextCommandResult.Success("No target chunks found (none changed or loaded, and no players online).");

        // ---------- Cache vanilla soil blocks by fertility once ----------
        var soilCache = new Dictionary<string, Block>(StringComparer.Ordinal);

        int changedBlocks = 0;
        int scannedColumns = 0;
        int mapY = ba.MapSizeY;

        foreach (var col in targetColumns)
        {
            scannedColumns++;

            EnsureChunkColumnLoaded(col);

            int baseX = col.X * GlobalConstants.ChunkSize;
            int baseZ = col.Y * GlobalConstants.ChunkSize;

            var pos = new BlockPos(0, 0, 0);
            for (int x = 0; x < GlobalConstants.ChunkSize; x++)
                for (int z = 0; z < GlobalConstants.ChunkSize; z++)
                    for (int y = 0; y < mapY; y++)
                    {
                        pos.SetAndCorrectDimension(baseX + x, y, baseZ + z);
                        var block = ba.GetBlock(pos);
                        if (block?.Code == null) continue;

                        // Only our mod’s trail/soil blocks from either old or new modid
                        // or from trail-slab mods
                        // todo: or from trail-layering mods (when it exists: Wilderlands Soil Layering + Trails).
                        switch (block.Code.Domain)
                        {
                            case "terrainslabs":
                            case "trailmod":
                            case "trailmodupdated": break;
                            // skip this block
                            default: continue;
                        }

                        var path = block.Code.Path;
                        if (!(path.StartsWith("trail-") || path.StartsWith("soil-"))) continue;

                        // Fertility from variant, else parse from path
                        string? fert = null;
                        block.Variant?.TryGetValue("fertility", out fert);
                        if (string.IsNullOrEmpty(fert))
                        {
                            var parts = path.Split('-');
                            if (parts.Length >= 3) fert = parts[1];
                        }
                        if (string.IsNullOrEmpty(fert)) continue;

                        if (!soilCache.TryGetValue(fert, out var vanilla))
                        {
                            vanilla = sapi.World.GetBlock(new AssetLocation($"game:soil-{fert}-none"));
                            if (vanilla == null) continue; // unexpected fertility, skip
                            soilCache[fert] = vanilla;
                        }

                        Block? fullBlock = null;
                        string code = string.Empty;

                        /* This is inelegant. Replacing a block entirely loses
                         any and all stateful properties that could be
                         preserved. But if it's more performant than tracking
                         blocks' "trail" status via variants, attributes, or
                         some other property; then so be it.*/
                        /* todo: explore "patching" trails to a non-trail state
                        instead of replacing them. Is performance acceptable? Is
                        memory usage better or worse? */
                        /** todo: check if trail devolve/regrowth/restoration needs mod compatibility. See <see cref="BlockTrail.OnServerGameTick"/>*/
                        /** todo: add trailSlabCache, trailLayeredCache */

                        // `terrainslabs` (TerrainSlabsTrailModCompatibility)
                        if (block.Class == "BlockTrailSlab")
                        {
                            string domain = true == block.Variant?.TryGetValue("domain", out string _domain)
                                ? _domain
                                : "game";
                            // terrainslabs stores the original {domain} as as variant e.g. "terrainslabs:soil-game". That's clever!
                            code = $"terrainslabs:soil-{domain}-{fert}-none";
                        }
                        // todo: `wilderlandssoillayering` (trail-layering currently nonexistent; this is prep)
                        else if (block.Class == "BlockTrailLayered")
                        {
                            // "wilderlandssoillayering:soil-layered-{fertility}-{grasscoverage}-{layer}"
                            string layerLevel = true == block.Variant?.TryGetValue("layer", out string layer)
                                ? layer
                                : "1";
                            code = $"wilderlandssoillayering:soil-layered-{fert}-none-{layerLevel}";
                        }

                        fullBlock = sapi.World.GetBlock(new AssetLocation(code));
                        if (fullBlock != null)
                        {
                            ba.SetBlock(fullBlock.BlockId, pos);
                            changedBlocks++;
                        }
                        else if (vanilla.BlockId != block.BlockId)
                        {
                            ba.SetBlock(vanilla.BlockId, pos);
                            changedBlocks++;
                        }
                    }
        }

        return TextCommandResult.Success(
            $"Restore complete. Columns scanned: {scannedColumns:N0}. Blocks replaced: {changedBlocks:N0}.");
    }

    // ---------- Helpers ----------

    private void EnsureChunkColumnLoaded(Vec2i column)
    {
        if (sapi == null) return;
        try
        {
            var wm = sapi.WorldManager;
            var type = wm.GetType();
            var m = type.GetMethod("LoadChunkColumnFast")
                ?? type.GetMethod("LoadChunkColumn")
                ?? type.GetMethod("PreloadChunkColumn");

            if (m != null)
            {
                var pars = m.GetParameters();
                if (pars.Length >= 2 &&
                    pars[0].ParameterType == typeof(int) &&
                    pars[1].ParameterType == typeof(int))
                {
                    m.Invoke(wm, new object[] { column.X, column.Y });
                }
            }
        }
        catch { /* non-fatal */ }
    }

    private bool TryGetChunkColumnXZ(IServerChunk chunk, out int cx, out int cz)
    {
        cx = cz = 0;
        var ct = chunk.GetType();

        var posProp = ct.GetProperty("Pos")
                    ?? ct.GetProperty("ChunkPos")
                    ?? ct.GetProperty("Position");
        if (posProp != null)
        {
            var posVal = posProp.GetValue(chunk);
            if (posVal != null)
            {
                var pt = posVal.GetType();
                var fx = pt.GetField("X") ?? pt.GetField("x");
                var fz = pt.GetField("Z") ?? pt.GetField("z");
                if (fx != null && fz != null)
                {
                    cx = Convert.ToInt32(fx.GetValue(posVal));
                    cz = Convert.ToInt32(fz.GetValue(posVal));
                    return true;
                }
                var px = pt.GetProperty("X") ?? pt.GetProperty("x");
                var pz = pt.GetProperty("Z") ?? pt.GetProperty("z");
                if (px != null && pz != null)
                {
                    cx = Convert.ToInt32(px.GetValue(posVal));
                    cz = Convert.ToInt32(pz.GetValue(posVal));
                    return true;
                }
            }
        }

        var propX = ct.GetProperty("ChunkX") ?? ct.GetProperty("X") ?? ct.GetProperty("cx");
        var propZ = ct.GetProperty("ChunkZ") ?? ct.GetProperty("Z") ?? ct.GetProperty("cz");
        if (propX != null && propZ != null)
        {
            cx = Convert.ToInt32(propX.GetValue(chunk));
            cz = Convert.ToInt32(propZ.GetValue(chunk));
            return true;
        }

        // Fields
        var fieldX = ct.GetField("ChunkX") ?? ct.GetField("X") ?? ct.GetField("cx");
        var fieldZ = ct.GetField("ChunkZ") ?? ct.GetField("Z") ?? ct.GetField("cz");
        if (fieldX != null && fieldZ != null)
        {
            cx = Convert.ToInt32(fieldX.GetValue(chunk));
            cz = Convert.ToInt32(fieldZ.GetValue(chunk));
            return true;
        }

        return false;
    }

    // todo: add non-nullable ICoreServerAPI sapi param; change to static member.
    // Add a 3x3 (radius=1) or 5x5 (radius=2) halo around each online player
    private void AddPlayerHalos(HashSet<Vec2i> targetColumns, int radius)
    {
        int cs = GlobalConstants.ChunkSize;
        Debug.Assert(sapi != null);
        foreach (var plr in sapi.World.AllOnlinePlayers)
        {
            var e = plr.Entity;
            if (e == null) continue;

            var bpos = e.Pos.AsBlockPos;
            int cx = Math.Floor((double)bpos.X / cs) is double dx ? (int)dx : bpos.X / cs;
            int cz = Math.Floor((double)bpos.Z / cs) is double dz ? (int)dz : bpos.Z / cs;

            for (int ox = -radius; ox <= radius; ox++)
                for (int oz = -radius; oz <= radius; oz++)
                {
                    targetColumns.Add(new Vec2i(cx + ox, cz + oz));
                }
        }
    }
}
