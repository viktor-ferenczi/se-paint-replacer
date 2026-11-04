using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using VRage.Game.Entity;

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

            var hitGrid = hitInfo.Value.HkHitInfo.GetHitEntity()?.GetTopMostParent() as MyCubeGrid;
            if (hitGrid == null)
                return null;

            // The ray passes through blocks without physics, like sensors and cameras,
            // and hits the block they are mounted on. The cell just before the hit
            // holds them.
            var hitPosition = hitInfo.Value.Position;
            var rayDirection = placementProvider.RayDirection;
            var passedBlock = hitGrid.GetCubeBlock(
                hitGrid.WorldToGridInteger(hitPosition - rayDirection * hitGrid.GridSize * 0.05)
            );
            if (passedBlock != null && !passedBlock.BlockDefinition.HasPhysics)
                return passedBlock;

            var blockPosition = hitGrid.WorldToGridInteger(hitPosition + rayDirection * 1e-3);
            return hitGrid.GetCubeBlock(blockPosition);
        }
    }
}
