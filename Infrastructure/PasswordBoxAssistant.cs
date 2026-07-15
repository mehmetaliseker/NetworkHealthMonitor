using System.Windows;
using System.Windows.Controls;

namespace NetworkHealthMonitor.Infrastructure;

public static class PasswordBoxAssistant
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxAssistant),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private static readonly DependencyProperty UpdatingPasswordProperty =
        DependencyProperty.RegisterAttached(
            "UpdatingPassword",
            typeof(bool),
            typeof(PasswordBoxAssistant));

    public static string GetBoundPassword(DependencyObject obj)
    {
        return (string)obj.GetValue(BoundPasswordProperty);
    }

    public static void SetBoundPassword(DependencyObject obj, string value)
    {
        obj.SetValue(BoundPasswordProperty, value);
    }

    private static bool GetUpdatingPassword(DependencyObject obj)
    {
        return (bool)obj.GetValue(UpdatingPasswordProperty);
    }

    private static void SetUpdatingPassword(DependencyObject obj, bool value)
    {
        obj.SetValue(UpdatingPasswordProperty, value);
    }

    private static void OnBoundPasswordChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not PasswordBox passwordBox)
        {
            return;
        }

        passwordBox.PasswordChanged -= PasswordChanged;
        if (!GetUpdatingPassword(passwordBox))
        {
            passwordBox.Password = e.NewValue as string ?? string.Empty;
        }

        passwordBox.PasswordChanged += PasswordChanged;
    }

    private static void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox)
        {
            return;
        }

        SetUpdatingPassword(passwordBox, true);
        SetBoundPassword(passwordBox, passwordBox.Password);
        SetUpdatingPassword(passwordBox, false);
    }
}
