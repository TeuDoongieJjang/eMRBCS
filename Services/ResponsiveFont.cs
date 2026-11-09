using System.ComponentModel;

namespace eMRBCS.Helpers;

public static class ResponsiveFont
{
    // Attach to any Label to make its font scale with the window width.
    public static readonly BindableProperty BaseSizeProperty = BindableProperty.CreateAttached(
        "BaseSize", typeof(double), typeof(ResponsiveFont), 0.0,
        propertyChanged: OnBaseSizeChanged);

    // Width (in DIPs) at which BaseSize is used at 100%.
    public static readonly BindableProperty ReferenceWidthProperty = BindableProperty.CreateAttached(
        "ReferenceWidth", typeof(double), typeof(ResponsiveFont), 400.0);

    // Lower bound of the scale on very narrow screens.
    public static readonly BindableProperty MinScaleProperty = BindableProperty.CreateAttached(
        "MinScale", typeof(double), typeof(ResponsiveFont), 0.7);

    // Upper bound — keep at 1.0 so wide screens stay at default size.
    public static readonly BindableProperty MaxScaleProperty = BindableProperty.CreateAttached(
        "MaxScale", typeof(double), typeof(ResponsiveFont), 1.0);

    public static double GetBaseSize(BindableObject b) => (double)b.GetValue(BaseSizeProperty);
    public static void SetBaseSize(BindableObject b, double v) => b.SetValue(BaseSizeProperty, v);
    public static double GetReferenceWidth(BindableObject b) => (double)b.GetValue(ReferenceWidthProperty);
    public static void SetReferenceWidth(BindableObject b, double v) => b.SetValue(ReferenceWidthProperty, v);
    public static double GetMinScale(BindableObject b) => (double)b.GetValue(MinScaleProperty);
    public static void SetMinScale(BindableObject b, double v) => b.SetValue(MinScaleProperty, v);
    public static double GetMaxScale(BindableObject b) => (double)b.GetValue(MaxScaleProperty);
    public static void SetMaxScale(BindableObject b, double v) => b.SetValue(MaxScaleProperty, v);

    private static readonly HashSet<WeakReference<Label>> _labels = new();
    private static bool _hooked;

    private static void OnBaseSizeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not Label label) return;

        lock (_labels)
        {
            _labels.RemoveWhere(wr => !wr.TryGetTarget(out _));
            _labels.Add(new WeakReference<Label>(label));
        }

        EnsureWindowHooked();
        Apply(label);
    }

    private static void EnsureWindowHooked()
    {
        if (_hooked) return;

        if (Application.Current?.Windows.FirstOrDefault() is { } window)
        {
            window.SizeChanged += OnWindowSizeChanged;
            _hooked = true;
            ReapplyAll(); // window may already be laid out
        }
        else if (Application.Current is not null)
        {
            // Defer until a window is created.
            Application.Current.PropertyChanged += OnAppPropertyChanged;
        }
    }

    private static void OnAppPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Application.Current?.Windows.FirstOrDefault() is { } window)
        {
            Application.Current!.PropertyChanged -= OnAppPropertyChanged;
            window.SizeChanged += OnWindowSizeChanged;
            _hooked = true;
            ReapplyAll();
        }
    }

    private static void OnWindowSizeChanged(object? sender, EventArgs e) => ReapplyAll();

    private static void ReapplyAll()
    {
        lock (_labels)
        {
            foreach (var wr in _labels.ToArray())
                if (wr.TryGetTarget(out var lbl)) Apply(lbl);
        }
    }

    private static void Apply(Label label)
    {
        double baseSize = GetBaseSize(label);
        if (baseSize <= 0) return;

        double width = Application.Current?.Windows.FirstOrDefault()?.Width ?? 0;
        if (width <= 0) return;

        double scale = Math.Clamp(
            width / GetReferenceWidth(label),
            GetMinScale(label),
            GetMaxScale(label));

        label.FontSize = Math.Round(baseSize * scale, 1);
    }
}