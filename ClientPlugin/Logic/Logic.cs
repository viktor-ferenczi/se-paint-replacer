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
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
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
        private Vector3? paintColorMaskHsv;
        private MyStringHash? paintSkinSubtypeId;

        // Reflection
        private static readonly Type MyGuiScreenColorPickerType = typeof(MyCubeGrid).Assembly.GetType("Sandbox.Game.Gui.MyGuiScreenColorPicker");
        private static readonly MethodInfo ApplyColorGetter = AccessTools.PropertyGetter(MyGuiScreenColorPickerType, "ApplyColor");
        private static readonly MethodInfo ApplySkinGetter = AccessTools.PropertyGetter(MyGuiScreenColorPickerType, "ApplySkin");
        private static readonly MethodInfo ClearRenderData = AccessTools.DeclaredMethod(typeof(MyCubeBuilder), "ClearRenderData");
        private static readonly FieldInfo ScreensField = AccessTools.DeclaredField(typeof(MyScreenManager), "m_screens");

        public void Reset()
        {
            active = false;
            aimedBlock = null;
            paintColorMaskHsv = null;
            paintSkinSubtypeId = null;
        }

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

        public bool HandleGameInputPrefix()
        {
            if (!MyInput.Static.IsAnyAltKeyPressed() || !IsInActiveSession())
            {
                Reset();
                return true;
            }

            active = true;
            aimedBlock = active ? MyCubeBuilderHelper.GetAimedBlock() : null;

            // Logic copied from MyCubeBuilder.HandleGameInput
            var context = MySession.Static.ControlledEntity?.AuxiliaryContext ?? MyStringId.NullOrEmpty;
            if (MyControllerHelper.IsControl(context, MyControlsSpace.CUBE_COLOR_CHANGE, MyControlStateType.PRESSED))
                ReplacePaint();

            return true;
        }

        private void ReplacePaint()
        {
            var aimedGrid = aimedBlock?.CubeGrid;
            if (aimedGrid == null || aimedGrid.Closed || !aimedGrid.InScene || aimedGrid.Physics == null)
                return;

            var aimedColorMaskHsv = aimedBlock.ColorMaskHSV;
            var aimedSkinSubtypeId = aimedBlock.SkinSubtypeId;

            // Logic copied from the MyCubeBuilder.Change method to get the color and skin to paint with.
            // Must use reflection because the MyGuiScreenColorPicker class is internal.
            paintColorMaskHsv = (bool)ApplyColorGetter.Invoke(null, Array.Empty<object>()) ? new Vector3?(MyPlayer.SelectedColor) : null;
            paintSkinSubtypeId = (bool)ApplySkinGetter.Invoke(null, Array.Empty<object>()) ? new MyStringHash?(MyStringHash.GetOrCompute(MyPlayer.SelectedArmorSkin)) : null;

            var ctrl = MyInput.Static.IsAnyCtrlKeyPressed();
            var shift = MyInput.Static.IsAnyShiftKeyPressed();

            var subgrids = ctrl ? shift ? aimedGrid.GetGridsInLogicalGroup() : aimedGrid.GetGridsInMechanicalGroup() : null;

            if (subgrids == null)
            {
                ReplacePaint(aimedGrid, aimedColorMaskHsv, aimedSkinSubtypeId);
                return;
            }

            foreach (var subgrid in subgrids)
            {
                ReplacePaint(subgrid, aimedColorMaskHsv, aimedSkinSubtypeId);
            }
        }

        private void ReplacePaint(MyCubeGrid grid, Vector3 aimedColorMaskHsv, MyStringHash aimedSkinSubtypeId)
        {
            foreach (var slimBlock in grid.CubeBlocks)
            {
                if (slimBlock.SkinSubtypeId != aimedSkinSubtypeId)
                    continue;

                if ((slimBlock.ColorMaskHSV - aimedColorMaskHsv).AbsMax() > 0.005f * 0.005f)
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

            if (aimedBlock != null)
                DrawBlock(aimedBlock, Cfg.AimedColor);

            DrawHint("Aim at the block with the paint to replace", 1, x: 0f);
            DrawHint("Alt+MMB: Replace on the aimed subgrid", 2, x: -0.12f);
            DrawHint("Ctrl+Alt+MMB: Replace on all subgrids", 3, x: -0.12f);
            DrawHint("Ctrl+Shift+Alt+MMB: Replace on all connected ships", 2, x: 0.12f);
            DrawHint("Ctrl+Alt+/: Configure to hide these hints", 3, x: 0.12f);

            return false;
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

        private void DrawBlock(MySlimBlock block, Color color)
        {
            var v4Color = color.ToVector4();
            for (var i = 0; i < Cfg.HighlightDensity; i++)
            {
                MyCubeBuilder.DrawSemiTransparentBox(aimedBlock.CubeGrid, block, v4Color, lineMaterial: Cfg.BlockMaterial, lineColor: v4Color);
            }
        }
    }
}