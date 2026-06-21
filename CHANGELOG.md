# Changelog

## 1.4.0

### Features/Additions

- remap Maro/legacy `trailmod` blocks to `trailmodupdated` equivalents
- fix typo: add `TrailModGlobals.CreativeTrampling`; deprecate `CeativeTrampling`
- allow the `/removetrails` command to restore `terrainslab` (TerrainSlabsTrailModCompatibility's `BlockTrailSlab`) and `wilderlandssoillayering` (`BlockTrailLayered` not implemented) trails to non-trail blocks

### Fixes

- replace `block is BlockTallGrass` with `block.Variant.ContainsKey("tallgrass")`

  The `BlockPlant` child class `BlockTallClass` was removed in `VSSurvivalMod@1.22.0-pre.1`
  VSSurvivalMod instead uses `bool blockIsTallgrass = block.Variant.ContainsKey("tallgrass");` or `block.Variant["tallgrass"] != null`.
- migrate to non-obsolete `OnBlockBroken`

  `OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, ref EnumHandling handling)` was superseded by `OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier, ref EnumHandling handling)`
- migrate from `Entity.ServerPos` to `Entity.Pos`
- replace `block is BlockTallGrass` with `block.Variant.ContainsKey("tallgrass")`

  The `BlockPlant` child class `BlockTallClass` was removed in `VSSurvivalMod@1.22.0-pre.1`
  VSSurvivalMod instead uses `bool blockIsTallgrass = block.Variant.ContainsKey("tallgrass");` or `block.Variant["tallgrass"] != null`

#### Assets

- add notes regarding trail/trample prevention to "trails" game mechanic text

  I'm praying the hyperlink works. The trowel help page's code is generated at runtime so I can't be certain its link works during design time.

  TRANSLATORS NEEDED!

### Performance

- process deferred transform queues only if they aren't empty
- pass `TrailChunkManager` instance into `ProcessDeferredTransforms`; remove redundant function call

### Documentation

- fix typo "UNIVERAL"
- fix typo
- note that `ProcessDeferredTransforms` should be called only if its queue isn't empty
- describe `BlockTrail.OnServerGameTick`

#### TODO

- plan to remove obsolete `CeativeTrampling`
- plan to utilize `deferredTransforms`
- change old "To Do" notes to "todo:" for uniformity and for VSCode's "Better Comments Next" and "TODO highlight" extensions
- plan to de-duplicate string constants

### Build System Changes

- minimize projects

#### Dependencies

- target net10.0; update deps

### Chores

#### VSCode

- add settings, extension recommendations
