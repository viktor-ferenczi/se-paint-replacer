using System;
using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.Extensions;
using ClientPlugin.Tools;
using HarmonyLib;
using Sandbox;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Game.Entity.UseObject;
using VRage.Input;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Logic
{
    public class Logic
    {
        public static readonly Logic Static = new Logic();
        private static Config Cfg => Config.Current;

        // State
        private bool active;
        private MySlimBlock aimedBlock;
        private Vector3? aimedColorMaskHsv;
        private MyStringHash? aimedSkinSubtypeId;
        private Vector3? paintColorMaskHsv;
        private MyStringHash? paintSkinSubtypeId;

        // Each repainted block makes the game update the emissivity of every emissive block on its grid,
        // which is a render message each. The render message queue holds 1M messages per frame and the
        // game crashes when it fills up, so big repaints are spread over frames by a message budget.
        private const int RenderMessageBudget = 100000;
        private const int MaxRepaintsPerFrame = 1000;
        private readonly Queue<(MyCubeGrid Grid, Vector3I Position, Vector3? ColorMaskHsv, MyStringHash? SkinSubtypeId)> repaintQueue = new();

        // Reflection
        private static readonly Type MyGuiScreenColorPickerType = typeof(MyCubeGrid).Assembly.GetType("Sandbox.Game.Gui.MyGuiScreenColorPicker");
        private static readonly MethodInfo ApplyColorGetter = AccessTools.PropertyGetter(MyGuiScreenColorPickerType, "ApplyColor");
        private static readonly MethodInfo ApplySkinGetter = AccessTools.PropertyGetter(MyGuiScreenColorPickerType, "ApplySkin");
        private static readonly MethodInfo ClearRenderData = AccessTools.DeclaredMethod(typeof(MyCubeBuilder), "ClearRenderData");
        private static readonly FieldInfo ScreensField = AccessTools.DeclaredField(typeof(MyScreenManager), "m_screens");

        private bool IsInActiveSession()
        {
            // Guard conditions
            return MySession.Static != null &&
                   MySession.Static.IsValid &&
                   MySession.Static.Ready &&
                   !MySession.Static.IsUnloading &&
                   MyCubeBuilder.Static != null &&
                   MyInput.Static != null &&
                   MySandboxGame.Static != null;
        }
        
        private bool IsCharacterSeated()
        {
            return MySession.Static?.LocalCharacter != null &&
                   MySession.Static.LocalCharacter.IsSitting;
        }

        private static bool IsCharacterInFirstPersonView()
        {
            return MySession.Static?.LocalCharacter != null &&
                   MySession.Static.LocalCharacter.IsInFirstPersonView;
        }

        private static bool IsEnabledInGameMode()
        {
            var isCreative = MySession.Static != null && MySession.Static.CreativeToolsEnabled(Sync.MyId);
            return isCreative ? Cfg.EnableInCreative : Cfg.EnableInSurvival;
        }

        private static bool IsCurrentlyAimedConveyorPortHighlighted()
        {
            var selectedObject = MyHud.SelectedObjectHighlight;
            if (selectedObject?.InteractiveObject == null || !selectedObject.Visible)
                return false;

            var useObject = selectedObject.InteractiveObject;

            // Check if this is a conveyor/inventory use object by examining supported actions
            // MyUseObjectInventory is marked with [MyUseObject("conveyor")] and [MyUseObject("inventory")]
            // and has the following action combination:
            var hasInventoryAccess = (useObject.SupportedActions & UseActionEnum.OpenInventory) != 0;
            var hasTerminalAccess = (useObject.SupportedActions & UseActionEnum.OpenTerminal) != 0;
            var isPrimaryInventory = useObject.PrimaryAction == UseActionEnum.OpenInventory;

            // This combination indicates a conveyor/inventory access point
            return hasInventoryAccess && hasTerminalAccess && isPrimaryInventory;
        }

        private static bool IsPaintingOverConveyorPortAllowed()
        {
            var isCreative = MySession.Static != null && MySession.Static.CreativeToolsEnabled(Sync.MyId);
            return isCreative ? Cfg.PaintOverConveyorPortInCreative : Cfg.PaintOverConveyorPortInSurvival;
        }

        public void Update()
        {
            if (repaintQueue.Count == 0)
                return;

            if (!IsInActiveSession())
            {
                repaintQueue.Clear();
                return;
            }

            var budget = RenderMessageBudget;
            for (var i = 0; i < MaxRepaintsPerFrame && repaintQueue.Count != 0; i++)
            {
                var (grid, position, colorMaskHsv, skinSubtypeId) = repaintQueue.Peek();
                if (!grid.Closed)
                {
                    var cost = 1 + (grid.GridSystems.EmissiveSystem?.EmissiveBlocks?.Count ?? 0);
                    if (cost > budget && i != 0)
                        break;

                    budget -= cost;
                    grid.SkinBlocks(position, position, colorMaskHsv, skinSubtypeId, false);
                }

                repaintQueue.Dequeue();
            }
        }

        public bool HandleGameInputPrefix()
        {
            if (!MyInput.Static.IsAnyAltKeyPressed() || 
                !IsInActiveSession() || 
                IsCharacterSeated() ||
                !IsCharacterInFirstPersonView() ||
                !IsEnabledInGameMode())
            {
                Reset();
                return true;
            }

            GetSelectedPaint();
            ActivateOnAimedBlock();

            // Input logic copied from MyCubeBuilder.HandleGameInput
            var context = MySession.Static.ControlledEntity?.ControlContext ?? MyStringId.NullOrEmpty;
            if (MyControllerHelper.IsControl(context, MyControlsSpace.CUBE_COLOR_CHANGE, MyControlStateType.PRESSED))
            {
                // Check if we should block painting on conveyor ports
                if (IsPaintingOverConveyorPortAllowed() || !IsCurrentlyAimedConveyorPortHighlighted())
                {
                    ReplacePaint();
                }
                Reset();
            }
            return true;
        }

        private void Reset()
        {
            if (!active)
                return;

            RestorePreviewPaint();

            active = false;
            aimedBlock = null;
            aimedColorMaskHsv = null;
            aimedSkinSubtypeId = null;
            paintColorMaskHsv = null;
            paintSkinSubtypeId = null;
        }

        private void GetSelectedPaint()
        {
            // Logic copied from the MyCubeBuilder.Change method to get the color and skin to paint with.
            // Must use reflection because the MyGuiScreenColorPicker class is internal.
            // The color is snapped to the precision the game sends it with, so the preview shows the color the blocks get.
            paintColorMaskHsv = (bool)ApplyColorGetter.Invoke(null, Array.Empty<object>()) ? new Vector3?(Snap(MyPlayer.SelectedColor)) : null;
            paintSkinSubtypeId = (bool)ApplySkinGetter.Invoke(null, Array.Empty<object>()) ? new MyStringHash?(MyStringHash.GetOrCompute(MyPlayer.SelectedArmorSkin)) : null;
        }

        private void ActivateOnAimedBlock()
        {
            active = true;

            var currentlyAimedBlock = active ? MyCubeBuilderHelper.GetAimedBlock() : null;

            // Special case when getting close to a conveyor port and it gets highlighted
            if (aimedBlock != null && !IsPaintingOverConveyorPortAllowed() && IsCurrentlyAimedConveyorPortHighlighted())
                currentlyAimedBlock = null;

            // Continue aiming at the same block?
            if (currentlyAimedBlock != null && aimedBlock != null &&
                currentlyAimedBlock.CubeGrid?.EntityId == aimedBlock.CubeGrid?.EntityId &&
                currentlyAimedBlock.Min == aimedBlock.Min)
                return;

            RestorePreviewPaint();

            aimedBlock = currentlyAimedBlock;

            // Special case when a conveyor port is highlighted
            if (!IsPaintingOverConveyorPortAllowed() && IsCurrentlyAimedConveyorPortHighlighted())
            {
                aimedBlock = null;
                return;
            }
            
            aimedColorMaskHsv = aimedBlock?.ColorMaskHSV;
            aimedSkinSubtypeId = aimedBlock?.SkinSubtypeId;

            if (Cfg.PreviewPaint && aimedBlock != null)
                PreviewPaint(paintColorMaskHsv, paintSkinSubtypeId);
        }

        private void RestorePreviewPaint()
        {
            if (Cfg.PreviewPaint && aimedBlock != null)
                PreviewPaint(aimedColorMaskHsv, aimedSkinSubtypeId);
        }

        // The preview changes the block only locally. Sending it to the server would snap the color of
        // the aimed block when restoring it, which leaves an armor edge around it just by aiming at it.
        private void PreviewPaint(Vector3? colorMaskHsv, MyStringHash? skinSubtypeId)
        {
            var grid = aimedBlock.CubeGrid;
            if (grid.ChangeColorAndSkin(aimedBlock, colorMaskHsv, skinSubtypeId))
                grid.GridSystems.EmissiveSystem?.UpdateEmissivity();
        }

        private void ReplacePaint()
        {
            var aimedGrid = aimedBlock?.CubeGrid;
            if (aimedGrid == null || aimedGrid.Closed || !aimedGrid.InScene || aimedGrid.Physics == null)
                return;

            var ctrl = MyInput.Static.IsAnyCtrlKeyPressed();
            var shift = MyInput.Static.IsAnyShiftKeyPressed();

            var subgrids = ctrl ? shift ? aimedGrid.GetGridsInLogicalGroup() : aimedGrid.GetGridsInMechanicalGroup() : null;

            if (shift && !ctrl)
            {
                FloodFillPaint(aimedGrid);
            }
            else if (subgrids == null)
            {
                ReplacePaint(aimedGrid);
            }
            else
            {
                foreach (var subgrid in subgrids)
                    ReplacePaint(subgrid);
            }

            // The aimed block must retain its preview paint
            aimedColorMaskHsv = paintColorMaskHsv;
            aimedSkinSubtypeId = paintSkinSubtypeId;
        }

        private void ReplacePaint(MyCubeGrid grid)
        {
            if (aimedColorMaskHsv == null || aimedSkinSubtypeId == null)
                return;

            // The aimed block may show the preview paint, which is only local
            foreach (var slimBlock in grid.CubeBlocks)
            {
                if (slimBlock == aimedBlock || HasAimedPaint(slimBlock))
                    Repaint(grid, slimBlock);
            }
        }

        // Repaints the blocks reachable from the aimed block through face-adjacent blocks of the aimed paint
        private void FloodFillPaint(MyCubeGrid grid)
        {
            if (aimedColorMaskHsv == null || aimedSkinSubtypeId == null)
                return;

            var visited = new HashSet<MySlimBlock> { aimedBlock };
            var queue = new Queue<MySlimBlock>();
            queue.Enqueue(aimedBlock);

            while (queue.Count != 0)
            {
                var block = queue.Dequeue();
                Repaint(grid, block);

                // A block may span more cells, look through the faces of all of them
                for (var x = block.Min.X; x <= block.Max.X; x++)
                for (var y = block.Min.Y; y <= block.Max.Y; y++)
                for (var z = block.Min.Z; z <= block.Max.Z; z++)
                {
                    foreach (var direction in Base6Directions.IntDirections)
                    {
                        var neighbor = grid.GetCubeBlock(new Vector3I(x, y, z) + direction);
                        if (neighbor != null && HasAimedPaint(neighbor) && visited.Add(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }
            }
        }

        private void Repaint(MyCubeGrid grid, MySlimBlock block)
        {
            repaintQueue.Enqueue((grid, block.Position, paintColorMaskHsv, paintSkinSubtypeId));
        }

        private bool HasAimedPaint(MySlimBlock block)
        {
            return block.SkinSubtypeId == aimedSkinSubtypeId.Value &&
                   block.ColorMaskHSV.PackHSVToUint() == aimedColorMaskHsv.Value.PackHSVToUint();
        }

        // Painting by hand sends the color packed into an uint (MyCubeGrid.SkinBlocks), which rounds it
        // to whole degrees of hue and whole percents of saturation and value
        private static Vector3 Snap(Vector3 colorMaskHsv)
        {
            return ColorExtensions.UnpackHSVFromUint(colorMaskHsv.PackHSVToUint());
        }

        public bool DrawPrefix(MyCubeBuilder cubeBuilder)
        {
            if (!active || !IsInActiveSession())
                return true;

            // Do not draw over the terminal
            if (MyGuiScreenTerminal.IsOpen)
                return true;

            // Do not draw over the Blueprints screen (F10)
            var screens = ScreensField.GetValue(null) as List<MyGuiScreenBase>;
            if (screens == null)
                return true;
            foreach (var screen in screens)
            {
                if (screen is MyGuiScreenGamePlay ||
                    screen is MyGuiScreenHudBase)
                    continue;

                var name = screen.GetType().Name;
                if (name == "MyGuiScreenDebugTiming" || // Compatibility with Shift-F11 statistics
                    name == "FPSOverlay") // Compatibility with the FPS Counter plugin
                    continue;

                return true;
            }

            ClearRenderData.Invoke(cubeBuilder, Array.Empty<object>());

            // Show warning if painting is blocked due to conveyor port
            if (!IsPaintingOverConveyorPortAllowed() && IsCurrentlyAimedConveyorPortHighlighted())
            {
                DrawHint("Painting blocked because a conveyor port is highlighted", 1, Center);
            }
            else
            {
                if (aimedBlock != null)
                    DrawBlock(aimedBlock, Cfg.AimedColor);

                // The text has a fixed pixel size, so keep the columns narrow enough for 1280x720
                DrawHint("Aim at the block with the paint to replace", 1, Center);
                DrawHint("Alt+MMB: Replace on the aimed subgrid", 2, LeftColumn);
                DrawHint("Shift+Alt+MMB: Flood fill", 3, LeftColumn);
                DrawHint("Ctrl+Alt+MMB: Replace on all subgrids", 2, RightColumn);
                DrawHint("Ctrl+Shift+Alt+MMB: Replace on all ships", 3, RightColumn);
                DrawHint("Ctrl+Alt+/: Configure to hide these hints", 4, Center);
            }

            return false;
        }

        private void DrawBlock(MySlimBlock block, Color color)
        {
            var v4Color = color.ToVector4();
            for (var i = 0; i < Cfg.HighlightDensity; i++)
            {
                MyCubeBuilder.DrawSemiTransparentBox(aimedBlock.CubeGrid, block, v4Color, lineMaterial: Cfg.BlockMaterial, lineColor: v4Color, onlyWireframe: true);
            }
        }
        
        private const MyGuiDrawAlignEnum Center = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP;
        private const MyGuiDrawAlignEnum LeftColumn = MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_TOP;
        private const MyGuiDrawAlignEnum RightColumn = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP;

        private void DrawHint(string text, int lineNumber, MyGuiDrawAlignEnum align)
        {
            if (!Cfg.ShowHints)
                return;

            // The columns are aligned to the screen center with a small gap between them
            var x = align == LeftColumn ? 0.49f : align == RightColumn ? 0.51f : 0.5f;
            var y = 0.04f * (lineNumber - 1);
            var screenCoord = new Vector2(MyRenderProxy.MainViewport.Width * x, MyRenderProxy.MainViewport.Height * (Cfg.TextPosition + y));

            if (Cfg.TextShadowOffset != 0 && Cfg.TextShadowColor.A != 0)
            {
                MyRenderProxy.DebugDrawText2D(screenCoord + new Vector2(Cfg.TextShadowOffset), text, Cfg.TextShadowColor, 1f, align);
            }

            MyRenderProxy.DebugDrawText2D(screenCoord, text, Cfg.HintColor, 1f, align);
        }
    }
}