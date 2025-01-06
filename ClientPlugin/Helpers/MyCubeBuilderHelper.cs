using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;

namespace ClientPlugin.Tools
{
    public static class MyCubeBuilderHelper
    {
        public static MySlimBlock GetAimedBlock()
        {
            var placementProvider = MyCubeBuilder.PlacementProvider;
            var hitInfo = placementProvider.HitInfo;
            if (!hitInfo.HasValue)
                return null;

            var hitGrid = hitInfo.Value.HkHitInfo.GetHitEntity() as MyCubeGrid;
            if (hitGrid == null)
                return null;

            var blockPosition = hitGrid.WorldToGridInteger(hitInfo.Value.Position + placementProvider.RayDirection * 1e-3);
            return hitGrid.GetCubeBlock(blockPosition);
        }
    }
}