using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        // (A) Engine-exposed “changed columns”
        TryAddChangedColumnsFromVanilla(targetColumns);

        // (B) Trail mod's own tracker (optional)
        TryAddChangedColumnsFromTrailMod(targetColumns);

        // (C) All loaded columns (portable across builds)
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
                        string domain = block.Code.Domain;
                        if (domain != "trailmodupdated" && domain != "trailmod") continue;

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

                        if (vanilla.BlockId != block.BlockId)
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

    private void TryAddChangedColumnsFromVanilla(HashSet<Vec2i> targetColumns)
    {
        try
        {
            var wm = sapi?.WorldManager;
            if (wm == null) return;

            var type = wm.GetType();

            var mGet = type.GetMethod("GetChangedChunkColumns")
                      ?? type.GetMethod("GetModifiedChunkColumns")
                      ?? type.GetMethod("get_ChangedChunkColumns");

            IEnumerable<Vec2i>? cols = null;

            if (mGet != null && mGet.GetParameters().Length == 0)
            {
                var res = mGet.Invoke(wm, null) as System.Collections.IEnumerable;
                cols = res?.Cast<object>().Select(o => (Vec2i)o);
            }
            else
            {
                var p = type.GetProperty("ChangedChunkColumns")
                     ?? type.GetProperty("ModifiedChunkColumns");
                if (p != null)
                {
                    var res = p.GetValue(wm) as System.Collections.IEnumerable;
                    cols = res?.Cast<object>().Select(o => (Vec2i)o);
                }
            }

            if (cols != null)
            {
                foreach (var c in cols) targetColumns.Add(new Vec2i(c.X, c.Y));
            }
        }
        catch { /* not exposed on this build */ }
    }

    private void TryAddChangedColumnsFromTrailMod(HashSet<Vec2i> targetColumns)
    {
        try
        {
            var tcmType = Type.GetType("TrailModUpdated.TrailChunkManager, TrailModUpdated");
            if (tcmType == null) return;

            var field = tcmType.GetField("ChangedColumns")
                       ?? tcmType.GetField("ModifiedColumns");
            if (field?.GetValue(null) is System.Collections.IEnumerable fset)
            {
                foreach (var o in fset) targetColumns.Add((Vec2i)o);
                return;
            }

            var prop = tcmType.GetProperty("ChangedColumns")
                      ?? tcmType.GetProperty("ModifiedColumns");
            if (prop?.GetValue(null) is System.Collections.IEnumerable pset)
            {
                foreach (var o in pset) targetColumns.Add((Vec2i)o);
                return;
            }
        }
        catch { /* optional */ }
    }

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
