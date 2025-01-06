using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;

namespace ClientPlugin.Extensions
{
    public static class MyCubeGridExtensions
    {
        public static List<MyCubeGrid> GetGridsInMechanicalGroup(this MyCubeGrid grid)
        {
            var group = MyCubeGridGroups.Static.Physical.GetGroup(grid);
            if (group == null)
                return null;

            return group.Nodes.Select(node => node.NodeData).ToList();
        }

        public static List<MyCubeGrid> GetGridsInLogicalGroup(this MyCubeGrid grid)
        {
            var group = MyCubeGridGroups.Static.Logical.GetGroup(grid);
            if (group == null)
                return null;

            return group.Nodes.Select(node => node.NodeData).ToList();
        }
    }
}