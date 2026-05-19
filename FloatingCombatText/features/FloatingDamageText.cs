using System;
using System.Collections.Generic;
using FloatingCombatText.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace FloatingCombatText;

public class FloatingDamageText : IFeature
{
    private static ICoreClientAPI _capi;

    public static Dictionary<Entity, List<CombatTextEntry>> CombatTexts = new();

    private static readonly Dictionary<long, float> EntityHealthCache = new();

    private readonly HashSet<long> _visibleEntityIds = new();
    private readonly List<Entity> _combatTextEntitiesToRemove = new();
    private readonly List<long> _healthCacheIdsToRemove = new();

    private long _healthCheckTickId;

    private const int HealthCheckIntervalMs = 100;
    public const float LifetimeSeconds = 2f;

    public FloatingDamageText(ICoreClientAPI api)
    {
        _capi = api;
    }

    public EnumAppSide Side => EnumAppSide.Client;

    public bool Initialize()
    {
        _capi.Event.RegisterRenderer(new DamageTextRenderer(_capi), EnumRenderStage.Ortho);
        _healthCheckTickId = _capi.Event.RegisterGameTickListener(OnHealthCheckTick, HealthCheckIntervalMs);
        return true;
    }

    public void Teardown()
    {
        _capi.Event.UnregisterGameTickListener(_healthCheckTickId);

        foreach (List<CombatTextEntry> entries in CombatTexts.Values)
        {
            DisposeAll(entries);
        }

        CombatTexts.Clear();
        EntityHealthCache.Clear();

        _visibleEntityIds.Clear();
        _combatTextEntitiesToRemove.Clear();
        _healthCacheIdsToRemove.Clear();
    }

    private void OnHealthCheckTick(float dt)
    {
        EntityPlayer player = _capi.World.Player?.Entity;
        if (player == null) return;

        float range = ModConfig.Instance.CombatTextRange;
        if (range <= 0) return;

        _visibleEntityIds.Clear();

        Entity[] entities = _capi.World.GetEntitiesAround(
            player.Pos.XYZ,
            range,
            range,
            e => IsFastCandidate(player, e)
        );

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];

            if (!IsVisibleEnough(player, entity))
            {
                continue;
            }

            _visibleEntityIds.Add(entity.EntityId);
            CheckHealthChange(entity);
        }

        CleanupCombatTexts();
        CleanupHealthCache();
    }

    private static bool IsFastCandidate(EntityPlayer player, Entity entity)
    {
        return entity is EntityAgent
            && entity != player
            && entity.Alive
            && entity.IsRendered;
    }

    private static bool IsVisibleEnough(EntityPlayer player, Entity entity)
    {
        // Renderer says this entity was drawn last frame.
        if (!entity.IsRendered)
            return false;

        // "Cheap" camera-frustum check.
        if (!IsInCameraFrustum(entity))
            return false;

        // Leave expensive check for last.
        return HasLineOfSightTo(player, entity);
    }

    private static bool IsInCameraFrustum(Entity entity)
    {
        float entityHeight = GetEntityHeight(entity);
        Vec3d pos = entity.Pos.XYZ;

        double centerX = pos.X;
        double centerY = pos.Y + entityHeight * 0.5;
        double centerZ = pos.Z;

        // Simple approximate sphere around the creature.
        double radius = Math.Max(0.75, entityHeight * 0.5);

        return _capi.Render.DefaultFrustumCuller.SphereInFrustum(
            centerX,
            centerY,
            centerZ,
            radius
        );
    }

    private static bool HasLineOfSightTo(EntityPlayer player, Entity target)
    {
        Vec3d playerPos = player.Pos.XYZ;
        Vec3d eye = player.LocalEyePos;

        Vec3d from = new(
            playerPos.X + eye.X,
            playerPos.Y + eye.Y,
            playerPos.Z + eye.Z
        );

        float targetHeight = GetEntityHeight(target);
        Vec3d targetPos = target.Pos.XYZ;

        Vec3d to = new(
            targetPos.X,
            targetPos.Y + targetHeight * 0.5,
            targetPos.Z
        );

        BlockSelection blockSel = null;
        EntitySelection entitySel = null;

        _capi.World.RayTraceForSelection(
            from,
            to,
            ref blockSel,
            ref entitySel,
            bfilter: BlocksLineOfSight,
            efilter: e => e == target
        );

        return blockSel == null && entitySel?.Entity == target;
    }

    private static void CheckHealthChange(Entity entity)
    {
        ITreeAttribute healthTree = entity.WatchedAttributes.GetTreeAttribute("health");
        long entityId = entity.EntityId;

        if (healthTree == null)
        {
            EntityHealthCache.Remove(entityId);
            return;
        }

        float currentHealth = healthTree.GetFloat("currenthealth", 0);

        if (!EntityHealthCache.TryGetValue(entityId, out float previousHealth))
        {
            EntityHealthCache[entityId] = currentHealth;

            _capi.Logger.Debug(
                $"[FCT] Seed health cache. Entity={entityId}, Health={currentHealth}"
            );

            return;
        }

        float deltaHealth = previousHealth - currentHealth;

        _capi.Logger.Debug(
            $"[FCT] Health check. Entity={entityId}, Previous={previousHealth}, Current={currentHealth}, Delta={deltaHealth}"
        );

        if (deltaHealth >= 0.1f)
        {
            _capi.Logger.Debug($"[FCT] Damage detected: {deltaHealth}");

            AddCombatText(
                entity,
                Math.Abs(deltaHealth),
                DamageTextRenderer.DefaultDamageColor   
            );

            EntityHealthCache[entityId] = currentHealth;
            return;
        }

        if (deltaHealth <= -0.1f)
        {
            _capi.Logger.Debug($"[FCT] Healing detected: {Math.Abs(deltaHealth)}");

            AddCombatText(
                entity,
                Math.Abs(deltaHealth),
                DamageTextRenderer.DefaultHealingColor
            );

            EntityHealthCache[entityId] = currentHealth;
            return;
        }

        // Do not update the cache here if you want tiny regen ticks to accumulate.
    }
    
    private static bool BlocksLineOfSight(BlockPos pos, Block block)
    {
        if (block == null)
        {
            return false;
        }

        // Air / highly replaceable blocks: grass, flowers, etc.
        if (block.BlockId == 0 || block.Replaceable >= 6000)
        {
            return false;
        }

        // Liquids should not block visual LOS for combat text.
        if (block.IsLiquid())
        {
            return false;
        }

        // Ignore transparent/translucent render passes.
        // Glass, liquids, blended/translucent blocks should generally not block LOS.
        if (block.RenderPass == EnumChunkRenderPass.Transparent
            || block.RenderPass == EnumChunkRenderPass.BlendNoCull
            || block.RenderPass == EnumChunkRenderPass.Liquid)
        {
            return false;
        }

        // SideOpaque means the block side is fully opaque and used for face-culling.
        // Fences, panes, plants, ladders, etc. usually should fail this and be ignored.
        if (!block.SideOpaque.All)
        {
            return false;
        }

        // LightAbsorption > 32 fully blocks light, so this is a good final "solid visual blocker" test.
        return block.LightAbsorption > 32;
    }

    private static void AddCombatText(Entity entity, float absDeltaHealth, int color)
    {
        float entityHeight = GetEntityHeight(entity);
        Vec3d entityPos = entity.Pos.XYZ;

        Vec3d pos = new(
            entityPos.X,
            entityPos.Y + entityHeight,
            entityPos.Z
        );

        CombatTextEntry entry = new()
        {
            Position = pos,
            AbsDeltaHealth = absDeltaHealth,
            SpawnTime = _capi.ElapsedMilliseconds,
            Color = color
        };

        if (!CombatTexts.TryGetValue(entity, out List<CombatTextEntry> entries))
        {
            entries = new List<CombatTextEntry>();
            CombatTexts[entity] = entries;
        }

        entries.Add(entry);
    }

    private void CleanupCombatTexts()
    {
        _combatTextEntitiesToRemove.Clear();

        foreach (KeyValuePair<Entity, List<CombatTextEntry>> pair in CombatTexts)
        {
            Entity entity = pair.Key;
            List<CombatTextEntry> entries = pair.Value;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                CombatTextEntry entry = entries[i];

                if (entry.GetAge() > LifetimeSeconds)
                {
                    entry.Dispose();
                    entries.RemoveAt(i);
                }
            }

            if (entries.Count == 0)
            {
                _combatTextEntitiesToRemove.Add(entity);
            }
        }

        for (int i = 0; i < _combatTextEntitiesToRemove.Count; i++)
        {
            CombatTexts.Remove(_combatTextEntitiesToRemove[i]);
        }
    }

    private void CleanupHealthCache()
    {
        _healthCacheIdsToRemove.Clear();

        foreach (long entityId in EntityHealthCache.Keys)
        {
            if (!_visibleEntityIds.Contains(entityId))
            {
                _healthCacheIdsToRemove.Add(entityId);
            }
        }

        for (int i = 0; i < _healthCacheIdsToRemove.Count; i++)
        {
            EntityHealthCache.Remove(_healthCacheIdsToRemove[i]);
        }
    }

    private static void DisposeAll(List<CombatTextEntry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].Dispose();
        }

        entries.Clear();
    }

    private static float GetEntityHeight(Entity entity)
    {
        return entity.SelectionBox?.Y2
            ?? entity.CollisionBox?.Y2
            ?? 1.0f;
    }

    public static long GetElapsedTime()
    {
        return _capi.ElapsedMilliseconds;
    }

    public class CombatTextEntry
    {
        public Vec3d Position { get; init; }
        public float AbsDeltaHealth { get; init; }
        public long SpawnTime { get; set; }
        public int Color { get; init; }

        public LoadedTexture Texture { get; set; }

        public float GetAge()
        {
            return (GetElapsedTime() - SpawnTime) / 1000f;
        }

        public void Dispose()
        {
            Texture?.Dispose();
            Texture = null;
        }
    }
}