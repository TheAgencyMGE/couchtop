using System.Windows.Input;

namespace Couchtop.App.Views;

/// <summary>A full-screen Couchtop screen hosted by <see cref="MainWindow"/>.</summary>
public interface IScreenView
{
    void OnShown();
    void OnHidden();

    /// <summary>Return true when the view handled Back itself (e.g. closing an inner panel).</summary>
    bool HandleBack();

    bool HandleKey(KeyEventArgs e) => false;
    bool WantsSystemCursor => false;
    bool PlaysAmbience => false;
}
