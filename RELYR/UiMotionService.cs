using System.Windows;
using System.Windows.Media;

namespace RELYR;

internal static class UiMotionService
{
    internal static bool Enabled { get; private set; } = true;

    internal static void Apply(bool enabled)
        => Enabled = enabled;

    internal static void RunSafely(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            DisableAfterFailure(operation, ex);
        }
    }

    internal static bool TryHandleDispatcherException(Exception exception)
    {
        if (!IsAnimationFailure(exception))
            return false;
        DisableAfterFailure("dispatcher-animation", exception);
        return true;
    }

    internal static ScaleTransform MutableScale(FrameworkElement element, double fallbackX = 1, double fallbackY = 1)
    {
        element.RenderTransformOrigin = new System.Windows.Point(.5, .5);
        if (element.RenderTransform is ScaleTransform scale)
        {
            if (!scale.IsFrozen)
                return scale;
            var mutable = scale.CloneCurrentValue();
            element.RenderTransform = mutable;
            return mutable;
        }
        var created = new ScaleTransform(fallbackX, fallbackY);
        element.RenderTransform = created;
        return created;
    }

    internal static TranslateTransform MutableTranslate(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform translate)
        {
            if (!translate.IsFrozen)
                return translate;
            var mutable = translate.CloneCurrentValue();
            element.RenderTransform = mutable;
            return mutable;
        }
        var created = new TranslateTransform();
        element.RenderTransform = created;
        return created;
    }

    internal static bool IsAnimationFailure(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            string stack = current.StackTrace ?? string.Empty;
            if (current is InvalidOperationException
                && (stack.Contains("System.Windows.Media.Animation.Animatable.BeginAnimation", StringComparison.Ordinal)
                    || stack.Contains("System.Windows.Media.Animation.AnimationClock", StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    static void DisableAfterFailure(string operation, Exception exception)
    {
        Enabled = false;
        LifecycleDiagnostics.Write("ui-motion-disabled-after-failure", $"operation={operation} {exception}");
    }
}
