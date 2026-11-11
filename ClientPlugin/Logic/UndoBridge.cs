using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace ClientPlugin.Logic
{
    // Makes each paint replacement one step of the Undo plugin's history when Undo is
    // loaded, through its API (mod message 487710606, see Undo's Docs/API.md). No
    // reference to Undo: without it nothing happens. Offline and on a lobby host
    // Undo records the repaint here; on a client of a server with the Undo companion
    // Undo passes the call on and the server records it for this player.
    public static class UndoBridge
    {
        private const long UndoApiMessageId = 487710606L;

        private static Func<List<IMyCubeGrid>, string, ulong, bool> recordPaint;

        // Asked again until Undo answers
        private static bool Connect()
        {
            if (recordPaint != null)
                return true;

            MyAPIGateway.Utilities?.SendModMessage(
                UndoApiMessageId,
                (Action<Dictionary<string, Delegate>>)(
                    api =>
                    {
                        if (api.TryGetValue("RecordPaint", out var function))
                            recordPaint = function as Func<List<IMyCubeGrid>, string, ulong, bool>;
                    }
                )
            );
            return recordPaint != null;
        }

        // Right before the repaint requests of these grids are sent
        public static void RecordPaint(IEnumerable<MyCubeGrid> grids, string label)
        {
            try
            {
                if (Connect())
                    recordPaint(grids.Cast<IMyCubeGrid>().ToList(), label, 0);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"{Plugin.Name}: recording in Undo failed: {e.Message}");
            }
        }
    }
}
