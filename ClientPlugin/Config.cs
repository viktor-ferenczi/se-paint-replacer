using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VRage.Utils;
using VRageMath;


namespace ClientPlugin
{
    public class Config : INotifyPropertyChanged
    {
        #region Options

        private bool showHints = true;
        private float textPosition = 0.70f;
        private Color hintColor = new Color(0xdd, 0xdd, 0);
        private int textShadowOffset = 2;
        private Color textShadowColor = new Color(0, 0, 0, 0xcc);

        private int highlightDensity = 1;
        private Color aimedColor = Color.White;
        private bool previewPaint = true;

        // Not configurable yet
        public readonly MyStringId BlockMaterial = MyStringId.GetOrCompute("ContainerBorderSelected");

        #endregion

        #region User interface

        public readonly string Title = "Paint Replacer";

        [Separator("Overlay")]
        [Checkbox(description: "Enable showing the hints on screen")]
        public bool ShowHints
        {
            get => showHints;
            set => SetField(ref showHints, value);
        }

        [Slider(0f, 0.9f, 0.01f, description: "Vertical position of the hints on the screen")]
        public float TextPosition
        {
            get => textPosition;
            set => SetField(ref textPosition, value);
        }

        [Color(description: "Hint text color")]
        public Color HintColor
        {
            get => hintColor;
            set => SetField(ref hintColor, value);
        }

        [Slider(0f, 10f, 1f, SliderAttribute.SliderType.Integer, description: "Text shadow offset (set to zero to turn off text shadows)")]
        public int TextShadowOffset
        {
            get => textShadowOffset;
            set => SetField(ref textShadowOffset, value);
        }

        [Color(hasAlpha: true, description: "Text shadow color")]
        public Color TextShadowColor
        {
            get => textShadowColor;
            set => SetField(ref textShadowColor, value);
        }

        [Separator("Block Selection")]
        [Slider(0f, 10f, 1f, SliderAttribute.SliderType.Integer, description: "Density of the block highlight (number of overdraws, zero disables highlighting)")]
        public int HighlightDensity
        {
            get => highlightDensity;
            set => SetField(ref highlightDensity, value);
        }

        [Color(description: "Highlight color of the aimed block", hasAlpha: true)]
        public Color AimedColor
        {
            get => aimedColor;
            set => SetField(ref aimedColor, value);
        }

        [Checkbox(description: "Enable previewing of the paint on the aimed block")]
        public bool PreviewPaint
        {
            get => previewPaint;
            set => SetField(ref previewPaint, value);
        }

        #endregion

        #region Property change notification bilerplate

        public static readonly Config Default = new Config();
        public static readonly Config Current = ConfigStorage.Load();

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}