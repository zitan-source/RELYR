using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RELYR;

internal static class UiMotionService
{
    internal static bool Enabled { get; private set; } = true;

    internal static void Apply(bool enabled)
        => Enabled = enabled;

    internal static void RunSafely(string operation, Action action)
        => TryRunSafely(operation, action);

    internal static bool TryRunSafely(string operation, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            DisableAfterFailure(operation, ex);
            return false;
        }
    }

    internal static IEasingFunction ResponsiveEaseOut()
        => new PowerEase { Power = 4, EasingMode = EasingMode.EaseOut };

    internal static IEasingFunction GentleSettleEase()
        => new BackEase { Amplitude = .18, EasingMode = EasingMode.EaseOut };

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
        if (element.RenderTransform is TransformGroup existingGroup)
        {
            var group = existingGroup.IsFrozen ? existingGroup.CloneCurrentValue() : existingGroup;
            var groupedScale = group.Children.OfType<ScaleTransform>().FirstOrDefault();
            if (groupedScale == null)
            {
                groupedScale = new ScaleTransform(fallbackX, fallbackY);
                group.Children.Insert(0, groupedScale);
            }
            else if (groupedScale.IsFrozen)
            {
                int index = group.Children.IndexOf(groupedScale);
                groupedScale = groupedScale.CloneCurrentValue();
                group.Children[index] = groupedScale;
            }
            element.RenderTransform = group;
            return groupedScale;
        }
        if (element.RenderTransform is TranslateTransform existingTranslate)
        {
            var mutableTranslate = existingTranslate.IsFrozen ? existingTranslate.CloneCurrentValue() : existingTranslate;
            var groupedScale = new ScaleTransform(fallbackX, fallbackY);
            element.RenderTransform = new TransformGroup
            {
                Children = new TransformCollection { groupedScale, mutableTranslate }
            };
            return groupedScale;
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
        if (element.RenderTransform is TransformGroup existingGroup)
        {
            var group = existingGroup.IsFrozen ? existingGroup.CloneCurrentValue() : existingGroup;
            var groupedTranslate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (groupedTranslate == null)
            {
                groupedTranslate = new TranslateTransform();
                group.Children.Add(groupedTranslate);
            }
            else if (groupedTranslate.IsFrozen)
            {
                int index = group.Children.IndexOf(groupedTranslate);
                groupedTranslate = groupedTranslate.CloneCurrentValue();
                group.Children[index] = groupedTranslate;
            }
            element.RenderTransform = group;
            return groupedTranslate;
        }
        if (element.RenderTransform is ScaleTransform existingScale)
        {
            var mutableScale = existingScale.IsFrozen ? existingScale.CloneCurrentValue() : existingScale;
            var groupedTranslate = new TranslateTransform();
            element.RenderTransform = new TransformGroup
            {
                Children = new TransformCollection { mutableScale, groupedTranslate }
            };
            return groupedTranslate;
        }
        var created = new TranslateTransform();
        element.RenderTransform = created;
        return created;
    }

    internal static (ScaleTransform Scale, TranslateTransform Translate) MutableMotionTransform(
        FrameworkElement element,
        double fallbackScaleX = 1,
        double fallbackScaleY = 1)
    {
        element.RenderTransformOrigin = new System.Windows.Point(.5, .5);
        var scale = MutableScale(element, fallbackScaleX, fallbackScaleY);
        var translate = MutableTranslate(element);
        return (scale, translate);
    }

    internal static void StopAndSetDouble(DependencyObject target, DependencyProperty property, double value)
    {
        if (target is IAnimatable animatable)
            animatable.BeginAnimation(property, null);
        target.SetValue(property, value);
    }

    internal static bool AnimateDouble(
        string operation,
        DependencyObject target,
        DependencyProperty property,
        double value,
        TimeSpan duration,
        IEasingFunction? easing = null,
        TimeSpan? beginTime = null,
        Action? completed = null)
    {
        if (!Enabled || duration <= TimeSpan.Zero || target is not IAnimatable animatable)
        {
            StopAndSetDouble(target, property, value);
            completed?.Invoke();
            return false;
        }

        return TryRunSafely(operation, () =>
        {
            double current = target.GetValue(property) is double liveValue && double.IsFinite(liveValue)
                ? liveValue
                : value;
            animatable.BeginAnimation(property, null);
            target.SetValue(property, current);
            var animation = new DoubleAnimation(current, value, duration)
            {
                BeginTime = beginTime ?? TimeSpan.Zero,
                EasingFunction = easing ?? ResponsiveEaseOut(),
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, _) =>
            {
                if (!TryRunSafely(operation + "-complete", () => StopAndSetDouble(target, property, value)))
                    return;
                completed?.Invoke();
            };
            animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        });
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
