using Godot;
using GoblinStronghold.Simulation;

namespace GoblinStronghold.GodotClient;

public partial class SeasonCycleView : Control
{
    private const float VisibleYearFraction = 0.42f;
    private static readonly Color MarkerColor = new("fff3c4");

    private ClimateCalendarProfile? _climate;
    private SimulationCalendarSnapshot _calendar;
    private Texture2D _ribbon = null!;

    public override void _Ready()
    {
        ClipContents = true;
        _ribbon = SeasonRibbonSprites.LoadTexture();
    }

    public void SetCalendar(
        ClimateCalendarProfile climate,
        SimulationCalendarSnapshot calendar)
    {
        _climate = climate ?? throw new ArgumentNullException(nameof(climate));
        _calendar = calendar;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_climate is null || Size.X <= 0f || Size.Y <= 0f)
        {
            return;
        }

        var track = new Rect2(Vector2.Zero, Size).Grow(-2f);
        DrawRect(track, new Color("111718"));

        var yearWidth = track.Size.X / VisibleYearFraction;
        var markerX = track.GetCenter().X;
        var currentYearOrigin = markerX - ((float)_calendar.YearProgress * yearWidth);
        for (var yearOffset = -2; yearOffset <= 2; yearOffset++)
        {
            var yearOrigin = currentYearOrigin + (yearOffset * yearWidth);
            foreach (var span in _climate.SeasonSpans)
            {
                var destination = new Rect2(
                    yearOrigin + (yearWidth * (float)span.Start),
                    track.Position.Y,
                    yearWidth * (float)span.Length,
                    track.Size.Y);
                DrawTextureRectRegion(
                    _ribbon,
                    destination,
                    SeasonRibbonSprites.GetRegion(_ribbon, span.Season));
            }
        }

        DrawRect(track, new Color(0.04f, 0.06f, 0.06f, 0.95f), filled: false, width: 2f);
        const float markerLength = 7f;
        DrawLine(
            new Vector2(markerX, track.Position.Y),
            new Vector2(markerX, track.Position.Y + markerLength),
            MarkerColor,
            width: 2.5f);
        DrawLine(
            new Vector2(markerX, track.End.Y - markerLength),
            new Vector2(markerX, track.End.Y),
            MarkerColor,
            width: 2.5f);
    }
}
