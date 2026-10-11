using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpDX.Mathematics.Interop;
using D2D = SharpDX.Direct2D1;
using DW = SharpDX.DirectWrite;
using WIC = SharpDX.WIC;

namespace SmartHunter.Ui.Controls
{
    // Overlay text drawn by DirectWrite instead of WPF. WPF's own text in a transparent window is soft and low in
    // contrast next to Chrome or Discord; DirectWrite with these gamma and contrast settings matches them. The text is
    // rendered at the screen's real pixel size (widget scale included) and shown 1:1. Use it like a TextBlock.
    public class SharpText : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(SharpText),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(SharpText),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(SharpText),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(SharpText),
            new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FontStyleProperty = TextElement.FontStyleProperty.AddOwner(typeof(SharpText),
            new FrameworkPropertyMetadata(FontStyles.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(SharpText),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TextAlignmentProperty = Block.TextAlignmentProperty.AddOwner(typeof(SharpText),
            new FrameworkPropertyMetadata(TextAlignment.Left, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TextWrappingProperty = TextBlock.TextWrappingProperty.AddOwner(typeof(SharpText),
            new FrameworkPropertyMetadata(TextWrapping.NoWrap, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TextTrimmingProperty = TextBlock.TextTrimmingProperty.AddOwner(typeof(SharpText),
            new FrameworkPropertyMetadata(TextTrimming.None, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public string Text { get { return (string)GetValue(TextProperty); } set { SetValue(TextProperty, value); } }
        public FontFamily FontFamily { get { return (FontFamily)GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }
        public double FontSize { get { return (double)GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }
        public FontWeight FontWeight { get { return (FontWeight)GetValue(FontWeightProperty); } set { SetValue(FontWeightProperty, value); } }
        public FontStyle FontStyle { get { return (FontStyle)GetValue(FontStyleProperty); } set { SetValue(FontStyleProperty, value); } }
        public Brush Foreground { get { return (Brush)GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }
        public TextAlignment TextAlignment { get { return (TextAlignment)GetValue(TextAlignmentProperty); } set { SetValue(TextAlignmentProperty, value); } }
        public TextWrapping TextWrapping { get { return (TextWrapping)GetValue(TextWrappingProperty); } set { SetValue(TextWrappingProperty, value); } }
        public TextTrimming TextTrimming { get { return (TextTrimming)GetValue(TextTrimmingProperty); } set { SetValue(TextTrimmingProperty, value); } }

        // Gamma 1.8 and full contrast: what Chrome-like text uses. Grayscale, as transparent windows can't do ClearType.
        static readonly D2D.Factory s_D2D = new D2D.Factory();
        static readonly DW.Factory s_DW = new DW.Factory();
        static readonly WIC.ImagingFactory s_Wic = new WIC.ImagingFactory();
        static readonly DW.RenderingParams s_Params = new DW.RenderingParams(s_DW, 1.8f, 1f, 0f, DW.PixelGeometry.Flat, DW.RenderingMode.NaturalSymmetric);

        // The last render: coverage (white text) at a pixel size, and the key it was made for
        byte[] m_Coverage;
        int m_PixelWidth, m_PixelHeight;
        double m_Scale;
        string m_RenderedKey;
        BitmapSource m_Bitmap;
        Color m_BitmapColor;

        public SharpText()
        {
            // Shown at exactly its pixel size; nearest neighbour keeps a half-pixel offset from blurring it
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            LayoutUpdated += (s, e) =>
            {
                // A widget's scale changed: render again at the new size
                if (m_Bitmap != null && Math.Abs(DeviceScale() - m_Scale) > 0.001)
                {
                    InvalidateVisual();
                }
            };
        }

        DW.TextLayout CreateLayout(float maxWidth)
        {
            string family = FontFamily?.Source ?? "Segoe UI";
            family = family.Split(',')[0].Trim();
            int hash = family.LastIndexOf('#');
            if (hash >= 0) family = family.Substring(hash + 1);

            using (var format = new DW.TextFormat(s_DW, family, (DW.FontWeight)FontWeight.ToOpenTypeWeight(),
                FontStyle == FontStyles.Italic ? DW.FontStyle.Italic : DW.FontStyle.Normal, (float)FontSize))
            {
                format.WordWrapping = TextWrapping == TextWrapping.NoWrap ? DW.WordWrapping.NoWrap : DW.WordWrapping.Wrap;
                format.TextAlignment = TextAlignment == TextAlignment.Right ? DW.TextAlignment.Trailing
                    : TextAlignment == TextAlignment.Center ? DW.TextAlignment.Center : DW.TextAlignment.Leading;
                var layout = new DW.TextLayout(s_DW, Text ?? "", format, maxWidth, float.MaxValue);
                if (Typography.GetNumeralAlignment(this) == FontNumeralAlignment.Tabular && !string.IsNullOrEmpty(Text))
                {
                    // Timers and damage don't shift sideways as their digits change
                    using (var typography = new DW.Typography(s_DW))
                    {
                        typography.AddFontFeature(new DW.FontFeature(DW.FontFeatureTag.TabularFigures, 1));
                        layout.SetTypography(typography, new DW.TextRange(0, Text.Length));
                    }
                }
                if (TextTrimming != TextTrimming.None)
                {
                    using (var sign = new DW.EllipsisTrimming(s_DW, format))
                    {
                        layout.SetTrimming(new DW.Trimming { Granularity = TextTrimming == TextTrimming.WordEllipsis ? DW.TrimmingGranularity.Word : DW.TrimmingGranularity.Character }, sign);
                    }
                }
                return layout;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return new Size(0, 0);
            }
            bool limited = TextWrapping != TextWrapping.NoWrap || TextTrimming != TextTrimming.None;
            float maxWidth = limited && !double.IsInfinity(availableSize.Width) ? (float)availableSize.Width : 100000f;
            using (var layout = CreateLayout(maxWidth))
            {
                var metrics = layout.Metrics;
                double width = Math.Min(metrics.WidthIncludingTrailingWhitespace, maxWidth);
                return new Size(Math.Ceiling(width), Math.Ceiling(metrics.Height));
            }
        }

        // Screen pixels per unit: the monitor's, times every scale above (widget size, overlay size), but not render
        // transforms, which are only animations passing through
        double DeviceScale()
        {
            var source = PresentationSource.FromVisual(this);
            double scale = source?.CompositionTarget != null ? source.CompositionTarget.TransformToDevice.M11 : 1;
            for (DependencyObject v = this; v != null; v = VisualTreeHelper.GetParent(v))
            {
                if (v is FrameworkElement fe && fe.LayoutTransform != null && !fe.LayoutTransform.Value.IsIdentity)
                {
                    var m = fe.LayoutTransform.Value;
                    scale *= Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
                }
            }
            return scale > 0.01 ? scale : 1;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (string.IsNullOrEmpty(Text) || RenderSize.Width <= 0 || RenderSize.Height <= 0)
            {
                return;
            }

            double scale = DeviceScale();
            string key = string.Join("|", Text, FontFamily?.Source, FontSize, FontWeight, FontStyle, TextAlignment, TextWrapping, TextTrimming, RenderSize.Width, RenderSize.Height, scale);
            if (key != m_RenderedKey)
            {
                RenderCoverage(scale);
                m_RenderedKey = key;
                m_Bitmap = null;
            }

            var rect = new Rect(0, 0, m_PixelWidth / m_Scale, m_PixelHeight / m_Scale);
            if (Foreground is SolidColorBrush solid)
            {
                var color = solid.Color;
                color.A = (byte)(color.A * Math.Max(0, Math.Min(1, solid.Opacity)));
                if (m_Bitmap == null || color != m_BitmapColor)
                {
                    m_Bitmap = Tint(color);
                    m_BitmapColor = color;
                }
                dc.DrawImage(m_Bitmap, rect);
            }
            else if (Foreground != null)
            {
                // Gradient text (the quest result): the coverage as a mask over the brush
                if (m_Bitmap == null || m_BitmapColor != Colors.White)
                {
                    m_Bitmap = Tint(Colors.White);
                    m_BitmapColor = Colors.White;
                }
                dc.PushOpacityMask(new ImageBrush(m_Bitmap));
                dc.DrawRectangle(Foreground, null, rect);
                dc.Pop();
            }
        }

        void RenderCoverage(double scale)
        {
            m_Scale = scale;
            m_PixelWidth = Math.Max(1, (int)Math.Ceiling(RenderSize.Width * scale));
            m_PixelHeight = Math.Max(1, (int)Math.Ceiling(RenderSize.Height * scale));

            var props = new D2D.RenderTargetProperties(D2D.RenderTargetType.Software,
                new D2D.PixelFormat(SharpDX.DXGI.Format.B8G8R8A8_UNorm, D2D.AlphaMode.Premultiplied),
                (float)(96 * scale), (float)(96 * scale), D2D.RenderTargetUsage.None, D2D.FeatureLevel.Level_DEFAULT);
            using (var bitmap = new WIC.Bitmap(s_Wic, m_PixelWidth, m_PixelHeight, WIC.PixelFormat.Format32bppPBGRA, WIC.BitmapCreateCacheOption.CacheOnLoad))
            using (var target = new D2D.WicRenderTarget(s_D2D, bitmap, props))
            using (var white = new D2D.SolidColorBrush(target, new RawColor4(1, 1, 1, 1)))
            using (var layout = CreateLayout((float)RenderSize.Width))
            {
                target.TextAntialiasMode = D2D.TextAntialiasMode.Grayscale;
                target.TextRenderingParams = s_Params;
                target.BeginDraw();
                target.Clear(new RawColor4(0, 0, 0, 0));
                target.DrawTextLayout(new RawVector2(0, 0), layout, white, D2D.DrawTextOptions.Clip);
                target.EndDraw();

                int stride = m_PixelWidth * 4;
                var pixels = new byte[stride * m_PixelHeight];
                bitmap.CopyPixels(pixels, stride);
                m_Coverage = new byte[m_PixelWidth * m_PixelHeight];
                for (int i = 0; i < m_Coverage.Length; i++)
                {
                    m_Coverage[i] = pixels[i * 4 + 3];
                }
            }
        }

        BitmapSource Tint(Color color)
        {
            var pixels = new byte[m_Coverage.Length * 4];
            for (int i = 0; i < m_Coverage.Length; i++)
            {
                int a = m_Coverage[i] * color.A / 255;
                pixels[i * 4] = (byte)(color.B * a / 255);
                pixels[i * 4 + 1] = (byte)(color.G * a / 255);
                pixels[i * 4 + 2] = (byte)(color.R * a / 255);
                pixels[i * 4 + 3] = (byte)a;
            }
            var bitmap = BitmapSource.Create(m_PixelWidth, m_PixelHeight, 96 * m_Scale, 96 * m_Scale, PixelFormats.Pbgra32, null, pixels, m_PixelWidth * 4);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
