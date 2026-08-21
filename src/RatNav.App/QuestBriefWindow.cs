using System.Windows;

// WinForms is in scope for the tray icon and brings a Brushes of its own.
using Brushes = System.Windows.Media.Brushes;

namespace RatNav.App;

/// <summary>
/// The quest brief, in a window of its own, for when the quest log has been torn off.
///
/// <para>Opening the brief over the map is the right answer while the log is docked — you are
/// already looking there. It is the wrong answer once somebody has pulled the log out, because
/// pulling it out is what you do when you do not want the map covered. Answering that by covering
/// the map anyway is the one thing the gesture was against.</para>
///
/// <para>It shows the same controls rather than a copy of them: the brief is moved into this
/// window and moved back when it closes. A second set would be a second thing to keep in step,
/// and the carousel, the wiki link and the step list all already work.</para>
///
/// <para>Centred and a share of the screen rather than sized to its content — a window that
/// changes size as you page through pictures of different shapes is one you have to find again
/// after every press.</para>
/// </summary>
public sealed class QuestBriefWindow : Window
{
    public QuestBriefWindow()
    {
        Title = "RatNav — Quest";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var work = SystemParameters.WorkArea;

        // Well under the "no more than 40%" it was asked to stay within: four tenths of the width
        // and half the height is a fifth of the screen.
        Width = Math.Max(340, work.Width * 0.40);
        Height = Math.Max(260, work.Height * 0.50);
    }
}
