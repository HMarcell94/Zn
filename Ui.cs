using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ZnDownloader
{
    // Small attached properties the XAML styles use.
    public static class Ui
    {
        // Inherited down the tree: set on the sidebar when the window is narrow, so nav items,
        // captions and the brand text can collapse to an icon-only rail from their own styles.
        public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
            "IsCompact", typeof(bool), typeof(Ui),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        public static bool GetIsCompact(DependencyObject d) => (bool)d.GetValue(IsCompactProperty);
        public static void SetIsCompact(DependencyObject d, bool value) => d.SetValue(IsCompactProperty, value);

        // Icon glyph (Segoe Fluent Icons / MDL2 code point) shown by nav items, segments and buttons.
        public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
            "Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

        public static string? GetIcon(DependencyObject d) => (string?)d.GetValue(IconProperty);
        public static void SetIcon(DependencyObject d, string? value) => d.SetValue(IconProperty, value);
    }

    // Lays its children out side by side (widths proportional to Weight, equal heights) when it is
    // at least BreakWidth wide, and stacks them vertically below that. Used for the page cards.
    public class AdaptiveColumnsPanel : Panel
    {
        public static readonly DependencyProperty BreakWidthProperty = DependencyProperty.Register(
            nameof(BreakWidth), typeof(double), typeof(AdaptiveColumnsPanel),
            new FrameworkPropertyMetadata(760.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double BreakWidth
        {
            get => (double)GetValue(BreakWidthProperty);
            set => SetValue(BreakWidthProperty, value);
        }

        public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
            nameof(Spacing), typeof(double), typeof(AdaptiveColumnsPanel),
            new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double Spacing
        {
            get => (double)GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached(
            "Weight", typeof(double), typeof(AdaptiveColumnsPanel),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

        public static double GetWeight(UIElement e) => (double)e.GetValue(WeightProperty);
        public static void SetWeight(UIElement e, double value) => e.SetValue(WeightProperty, value);

        private bool IsWide(double width) => !double.IsInfinity(width) && width >= BreakWidth;

        private UIElement[] VisibleChildren() =>
            InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToArray();

        protected override Size MeasureOverride(Size available)
        {
            var kids = VisibleChildren();
            if (kids.Length == 0) return new Size();

            if (IsWide(available.Width))
            {
                double totalWeight = kids.Sum(GetWeight);
                double usable = Math.Max(0, available.Width - Spacing * (kids.Length - 1));
                double height = 0;
                foreach (var c in kids)
                {
                    c.Measure(new Size(usable * GetWeight(c) / totalWeight, double.PositiveInfinity));
                    height = Math.Max(height, c.DesiredSize.Height);
                }
                return new Size(available.Width, height);
            }

            double width = 0, stacked = 0;
            foreach (var c in kids)
            {
                c.Measure(new Size(available.Width, double.PositiveInfinity));
                width = Math.Max(width, c.DesiredSize.Width);
                stacked += c.DesiredSize.Height;
            }
            stacked += Spacing * (kids.Length - 1);
            return new Size(double.IsInfinity(available.Width) ? width : available.Width, stacked);
        }

        protected override Size ArrangeOverride(Size final)
        {
            var kids = VisibleChildren();
            if (IsWide(final.Width))
            {
                double totalWeight = kids.Sum(GetWeight);
                double usable = Math.Max(0, final.Width - Spacing * (kids.Length - 1));
                double x = 0;
                foreach (var c in kids)
                {
                    double w = usable * GetWeight(c) / totalWeight;
                    c.Arrange(new Rect(x, 0, w, final.Height));
                    x += w + Spacing;
                }
            }
            else
            {
                double y = 0;
                foreach (var c in kids)
                {
                    c.Arrange(new Rect(0, y, final.Width, c.DesiredSize.Height));
                    y += c.DesiredSize.Height + Spacing;
                }
            }
            return final;
        }
    }
}
