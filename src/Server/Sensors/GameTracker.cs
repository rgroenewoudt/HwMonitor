using HwMonitor.Models;

namespace HwMonitor.Sensors;

/// <summary>
/// Which game the FPS is for: the app in front while there is an FPS value. After Alt-Tab the shown FPS still comes from
/// the old game for a poll or two (<see cref="FpsFilter"/>), so a new app is only shown after it was in front for 2 polls.
/// The desktop/taskbar (no app) keeps the last game.
/// </summary>
public sealed class GameTracker(Func<int, string?> graphicsApi)
{
    private const int SwitchAfterPolls = 2;

    private ForegroundAppInfo? _game;
    private ForegroundAppInfo? _next;
    private int _nextCount;

    public GameInfo? Update(double? fps, ForegroundAppInfo? app)
    {
        if (fps is null)
        {
            _game = _next = null;
            return null;
        }

        if (app is null || app.Path == _game?.Path)
        {
            // Same exe again: take the new pid (the game was restarted).
            _game = app ?? _game;
            _next = null;
        }
        else if (_game is null)
            _game = app;
        else
        {
            _nextCount = app.Path == _next?.Path ? _nextCount + 1 : 1;
            _next = app;
            if (_nextCount >= SwitchAfterPolls)
                (_game, _next) = (app, null);
        }
        return _game is null ? null : new GameInfo(_game.Name, _game.IconId, graphicsApi(_game.Pid));
    }
}
