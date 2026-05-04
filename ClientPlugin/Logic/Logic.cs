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
            paintColorMaskHsv = (bool)ApplyColorGetter.Invoke(null, Array.Empty<object>()) ? new Vector3?(MyPlayer.SelectedColor) : null;
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
                aimedBlock.CubeGrid.SkinBlocks(aimedBlock.Position, aimedBlock.Position, paintColorMaskHsv, paintSkinSubtypeId, false);
        }

        private void RestorePreviewPaint()
        {
            if (Cfg.PreviewPaint && aimedBlock != null)
                aimedBlock.CubeGrid.SkinBlocks(aimedBlock.Position, aimedBlock.Position, aimedColorMaskHsv, aimedSkinSubtypeId, false);
        }

        private void ReplacePaint()
        {
            var aimedGrid = aimedBlock?.CubeGrid;
            if (aimedGrid == null || aimedGrid.Closed || !aimedGrid.InScene || aimedGrid.Physics == null)
                return;

            var ctrl = MyInput.Static.IsAnyCtrlKeyPressed();
            var shift = MyInput.Static.IsAnyShiftKeyPressed();

            var subgrids = ctrl ? shift ? aimedGrid.GetGridsInLogicalGroup() : aimedGrid.GetGridsInMechanicalGroup() : null;

            if (subgrids == null)
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

            foreach (var slimBlock in grid.CubeBlocks)
            {
                if (slimBlock.SkinSubtypeId != aimedSkinSubtypeId.Value)
                    continue;

                if ((slimBlock.ColorMaskHSV - aimedColorMaskHsv.Value).AbsMax() > 0.005f * 0.005f)
                    continue;

                grid.SkinBlocks(slimBlock.Position, slimBlock.Position, paintColorMaskHsv, paintSkinSubtypeId, false);
            }
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
                DrawHint("Painting blocked because a conveyor port is highlighted", 1, -0.05f);
            }
            else
            {
                if (aimedBlock != null)
                    DrawBlock(aimedBlock, Cfg.AimedColor);

                DrawHint("Aim at the block with the paint to replace", 1, x: 0f);
                DrawHint("Alt+MMB: Replace on the aimed subgrid", 2, x: -0.12f);
                DrawHint("Ctrl+Alt+MMB: Replace on all subgrids", 3, x: -0.12f);
                DrawHint("Ctrl+Shift+Alt+MMB: Replace on all connected ships", 2, x: 0.12f);
                DrawHint("Ctrl+Alt+/: Configure to hide these hints", 3, x: 0.12f);
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
        
        private void DrawHint(string text, int lineNumber, float x)
        {
            if (!Cfg.ShowHints)
                return;

            DrawText(text, Cfg.HintColor, lineNumber, center: false, x: 0.43f + x);
        }

        private void DrawText(string text, Color color, int lineNumber = 1, float scale = 1f, bool center = true, float x = 0.5f, float? y = null)
        {
            if (!y.HasValue)
                y = 0.04f * scale * (lineNumber - 1);

            var screenCoord = new Vector2(MyRenderProxy.MainViewport.Width * x, MyRenderProxy.MainViewport.Height * (Cfg.TextPosition + y.Value));

            if (Cfg.TextShadowOffset != 0 && Cfg.TextShadowColor.A != 0)
            {
                MyRenderProxy.DebugDrawText2D(screenCoord + new Vector2(Cfg.TextShadowOffset), text, Cfg.TextShadowColor, scale, center ? MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP : MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP);
            }

            MyRenderProxy.DebugDrawText2D(screenCoord, text, color, scale, center ? MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP : MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP);
        }
    }
}