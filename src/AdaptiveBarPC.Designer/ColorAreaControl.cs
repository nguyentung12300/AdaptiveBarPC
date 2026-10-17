using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AdaptiveBarPC.Designer;

public sealed class ColorAreaControl : Control
{
    public static readonly DependencyProperty HueBrushProperty =
        DependencyProperty.Register(
            nameof(HueBrush),
            typeof(Brush),
            typeof(ColorAreaControl),
            new FrameworkPropertyMetadata(Brushes.Red, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SaturationProperty =
        DependencyProperty.Register(
            nameof(Saturation),
            typeof(double),
            typeof(ColorAreaControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSaturationOrBrightnessChanged));

    public static readonly DependencyProperty BrightnessProperty =
        DependencyProperty.Register(
            nameof(Brightness),
            typeof(double),
            typeof(ColorAreaControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSaturationOrBrightnessChanged));

    public Brush HueBrush
    {
        get => (Brush)GetValue(HueBrushProperty);
        set => SetValue(HueBrushProperty, value);
    }

    public double Saturation
    {
        get => (double)GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    public double Brightness
    {
        get => (double)GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, value);
    }

    private static void OnSaturationOrBrightnessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorAreaControl control)
        {
            control.UpdateReticlePosition();
        }
    }

    private Canvas? _canvas;
    private FrameworkElement? _reticle;

    public ColorAreaControl()
    {
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        Loaded += (_, _) => UpdateReticlePosition();
        SizeChanged += (_, _) => UpdateReticlePosition();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _canvas = GetTemplateChild("PART_Canvas") as Canvas;
        _reticle = GetTemplateChild("PART_Reticle") as FrameworkElement;
        UpdateReticlePosition();
    }

    public void UpdateReticlePosition()
    {
        if (_reticle is null) return;
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double s = Math.Clamp(Saturation, 0.0, 1.0);
        double v = Math.Clamp(Brightness, 0.0, 1.0);

        double x = s * w;
        double y = (1.0 - v) * h;

        double halfW = _reticle.ActualWidth > 0 ? _reticle.ActualWidth / 2.0 : 7.0;
        double halfH = _reticle.ActualHeight > 0 ? _reticle.ActualHeight / 2.0 : 7.0;

        Canvas.SetLeft(_reticle, x - halfW);
        Canvas.SetTop(_reticle, y - halfH);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        UpdateFromPoint(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateFromPoint(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private void UpdateFromPoint(Point p)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double s = Math.Clamp(p.X / w, 0.0, 1.0);
        double v = Math.Clamp(1.0 - (p.Y / h), 0.0, 1.0);

        Saturation = Math.Round(s, 3);
        Brightness = Math.Round(v, 3);
        UpdateReticlePosition();
    }
}
